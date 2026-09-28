using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using static Ticketing.Domain.Tests.TestData;

namespace Ticketing.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Of_normalises_currency_code()
    {
        Money.Of(10m, " eur ").Currency.ShouldBe("EUR");
    }

    [Fact]
    public void Of_rejects_negative_amounts()
    {
        Should.Throw<BusinessRuleViolationException>(() => Money.Of(-0.01m, "USD")).Code.ShouldBe("money.negative");
    }

    [Fact]
    public void Of_rejects_sub_cent_precision()
    {
        Should.Throw<BusinessRuleViolationException>(() => Money.Of(10.005m, "USD")).Code.ShouldBe("money.precision");
    }

    [Fact]
    public void Add_refuses_to_mix_currencies()
    {
        Should.Throw<BusinessRuleViolationException>(() => Money.Of(1m, "USD").Add(Money.Of(1m, "EUR")))
            .Code.ShouldBe("money.currency_mismatch");
    }

    [Fact]
    public void Arithmetic_is_exact_decimal()
    {
        var total = Money.Of(0.10m, "USD").Multiply(3).Add(Money.Of(0.20m, "USD"));
        total.ShouldBe(Money.Of(0.50m, "USD"));
    }
}

public sealed class TicketOrderTests
{
    [Fact]
    public void Place_snapshots_price_and_issues_one_ticket_per_seat()
    {
        var concert = Concert();
        var vip = concert.ReserveTickets(concert.TierNamed("VIP").Id, 3, Now);

        var order = TicketOrder.Place(concert, vip, 3, "Ada Lovelace", "Ada@Example.com", "user-1", "key-1", "fp", Now);

        order.UnitPrice.ShouldBe(Money.Of(150m, "USD"));
        order.Total.ShouldBe(Money.Of(450m, "USD"));
        order.TierName.ShouldBe("VIP");
        order.CustomerEmail.ShouldBe("ada@example.com");
        order.PurchasedBy.ShouldBe("user-1");
        order.Tickets.Count.ShouldBe(3);
        order.Tickets.Select(t => t.Code).Distinct().Count().ShouldBe(3);
        order.Tickets.ShouldAllBe(t => t.OrderId == order.Id && t.Code.Length == Ticket.CodeLength);
    }

    [Fact]
    public void Later_price_changes_do_not_rewrite_an_existing_order()
    {
        var concert = Concert();
        var vip = concert.ReserveTickets(concert.TierNamed("VIP").Id, 1, Now);
        var order = TicketOrder.Place(concert, vip, 1, "Ada", "ada@example.com", "user-1", null, null, Now);

        concert.UpdateTiers(concert.CurrentTiers().Select(t => t with { Price = t.Price * 2 }).ToList());

        vip.Price.Amount.ShouldBe(300m);
        order.UnitPrice.Amount.ShouldBe(150m);
    }

    [Fact]
    public void Place_rejects_a_tier_from_a_different_event()
    {
        var concert = Concert();
        var otherTier = Concert().Tiers[0];

        Should.Throw<BusinessRuleViolationException>(
            () => TicketOrder.Place(concert, otherTier, 1, "Ada", "ada@example.com", "user-1", null, null, Now));
    }

    [Fact]
    public void Ticket_codes_avoid_ambiguous_characters()
    {
        var concert = Concert();
        var ga = concert.ReserveTickets(concert.TierNamed("General Admission").Id, 10, Now);
        var order = TicketOrder.Place(concert, ga, 10, "Ada", "ada@example.com", "user-1", null, null, Now);

        order.Tickets.ShouldAllBe(t => !t.Code.Any(c => "0O1IL".Contains(c)));
    }
}
