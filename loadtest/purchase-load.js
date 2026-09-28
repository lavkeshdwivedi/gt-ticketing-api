// Purchase load test. Three scenarios, run back to back:
//   hot_event   - everyone buys from ONE event: measures the per-event serialization cost (ADR 0001)
//   many_events - the same load spread over 20 events: measures parallelism across events
//   sell_out    - 2,000 attempts for 1,000 seats: the books must show exactly 1,000 sold
//
// Run against the compose stack (see README, "Load test"):
//   docker run --rm -i --network gt-ticketing-api_default -e BASE_URL=http://api:8080 grafana/k6 run - < loadtest/purchase-load.js

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const VUS = 50;
const SELL_OUT_SEATS = 1000;

const ticketsSold = new Counter('tickets_sold');
const soldOut = new Counter('sold_out_responses');

// 200 (setup, reports) and 409 (sold out) are correct answers, not failures.
http.setResponseCallback(http.expectedStatuses(200, 201, 409));

export const options = {
  scenarios: {
    hot_event: { executor: 'constant-vus', vus: VUS, duration: '30s', exec: 'hotEvent' },
    many_events: { executor: 'constant-vus', vus: VUS, duration: '30s', exec: 'manyEvents', startTime: '35s' },
    sell_out: { executor: 'per-vu-iterations', vus: 100, iterations: 20, exec: 'sellOut', startTime: '70s' },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
    checks: ['rate==1.0'],
    'http_req_duration{scenario:hot_event}': ['p(95)<1000'],
    'http_req_duration{scenario:many_events}': ['p(95)<1000'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function post(path, body, token, extraHeaders) {
  const headers = Object.assign({ 'Content-Type': 'application/json' }, extraHeaders || {});
  if (token) headers.Authorization = `Bearer ${token}`;
  return http.post(`${BASE}${path}`, JSON.stringify(body), { headers });
}

function token(subject, roles) {
  const res = post('/dev/token', { subject, roles: roles || [] });
  if (res.status !== 200) throw new Error(`token: ${res.status} ${res.body}`);
  return res.json('accessToken');
}

function createEvent(admin, name, capacity) {
  const res = post('/api/v1/events', {
    name,
    venue: 'Load Test Arena',
    startsAt: new Date(Date.now() + 30 * 24 * 3600 * 1000).toISOString(),
    currency: 'USD',
    totalCapacity: capacity,
    tiers: [{ name: 'General Admission', price: 25.0, capacity }],
  }, admin);
  if (res.status !== 201) throw new Error(`create event: ${res.status} ${res.body}`);
  return { id: res.json('id'), tierId: res.json('tiers.0.id') };
}

export function setup() {
  const admin = token('loadtest-admin', ['events.manage', 'reports.read']);
  const buyers = [];
  for (let i = 0; i < 100; i++) buyers.push(token(`loadtest-buyer-${i}`));
  const run = Date.now();
  return {
    admin,
    buyers,
    hot: createEvent(admin, `Hot ${run}`, 1000000),
    many: Array.from({ length: 20 }, (_, i) => createEvent(admin, `Spread ${run}-${i}`, 100000)),
    sellOut: createEvent(admin, `Sell-out ${run}`, SELL_OUT_SEATS),
  };
}

function buy(data, target) {
  const res = post(`/api/v1/events/${target.id}/purchases`, {
    tierId: target.tierId,
    quantity: 1,
    customerName: 'Load Test',
    customerEmail: 'load@example.com',
  }, data.buyers[(__VU - 1) % data.buyers.length]);

  check(res, { 'purchase answered 201 or 409': (r) => r.status === 201 || r.status === 409 });
  if (res.status === 201) ticketsSold.add(1);
  if (res.status === 409) soldOut.add(1);
}

export function hotEvent(data) {
  buy(data, data.hot);
}

export function manyEvents(data) {
  buy(data, data.many[(__VU + __ITER) % data.many.length]);
}

export function sellOut(data) {
  buy(data, data.sellOut);
}

export function teardown(data) {
  const res = http.get(`${BASE}/api/v1/events/${data.sellOut.id}/sales-summary`, {
    headers: { Authorization: `Bearer ${data.admin}` },
  });
  const summary = res.json();
  console.log(`sell_out event: sold=${summary.ticketsSold} remaining=${summary.ticketsRemaining} orders=${summary.orderCount}`);
  check(summary, {
    'sell-out: exactly capacity sold': (s) => s.ticketsSold === SELL_OUT_SEATS,
    'sell-out: nothing remaining': (s) => s.ticketsRemaining === 0,
    'sell-out: one order per ticket': (s) => s.orderCount === SELL_OUT_SEATS,
  });
}
