using Ticketing.Domain.Common;
using Ticketing.Domain.Events;
using static Ticketing.Domain.Tests.TestData;

namespace Ticketing.Domain.Tests;

public sealed class EventCreationTests
{
    [Fact]
    public void Create_with_valid_input_schedules_event_and_normalises_currency()
    {
        var concert = Concert();

        concert.Status.ShouldBe(EventStatus.Scheduled);
        concert.Currency.ShouldBe("USD");
        concert.TotalCapacity.ShouldBe(100);
        concert.TicketsAvailable.ShouldBe(100);
        concert.Tiers.Count.ShouldBe(2);
        concert.Tiers.ShouldAllBe(t => t.EventId == concert.Id && t.Price.Currency == "USD");
    }

    [Fact]
    public void Create_rejects_start_time_in_the_past()
    {
        var ex = Should.Throw<BusinessRuleViolationException>(() => Concert(startsAt: Now.AddMinutes(-1)));
        ex.Code.ShouldBe("event.starts_in_past");
    }

    [Theory]
    [InlineData(99)]
    [InlineData(101)]
    public void Create_rejects_tier_allocations_that_do_not_add_up_to_total_capacity(int totalCapacity)
    {
        var ex = Should.Throw<BusinessRuleViolationException>(() => Event.Create(
            "Gig", null, "Club", NextMonth, "USD", totalCapacity, [Tier("VIP", 100m, 10), Tier("GA", 20m, 90)], Now));

        ex.Code.ShouldBe("event.capacity_mismatch");
    }

    [Fact]
    public void Create_rejects_duplicate_tier_names_ignoring_case()
    {
        var ex = Should.Throw<BusinessRuleViolationException>(() => Event.Create(
            "Gig", null, "Club", NextMonth, "USD", 20, [Tier("VIP", 100m, 10), Tier(" vip ", 20m, 10)], Now));

        ex.Code.ShouldBe("tier.duplicate_name");
    }

    [Fact]
    public void Create_requires_at_least_one_tier()
    {
        var ex = Should.Throw<BusinessRuleViolationException>(() => Event.Create(
            "Gig", null, "Club", NextMonth, "USD", 10, [], Now));

        ex.Code.ShouldBe("event.tier_count");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("")]
    public void Create_rejects_invalid_currency(string currency)
    {
        var ex = Should.Throw<BusinessRuleViolationException>(() => Event.Create(
            "Gig", null, "Club", NextMonth, currency, 10, [Tier("GA", 20m, 10)], Now));

        ex.Code.ShouldBe("money.invalid_currency");
    }

    [Fact]
    public void Create_allows_free_tiers()
    {
        var @event = Event.Create("Open Day", null, "Campus", NextMonth, "USD", 10, [Tier("Free", 0m, 10)], Now);
        @event.Tiers.Single().Price.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Create_rejects_blank_name()
    {
        var ex = Should.Throw<BusinessRuleViolationException>(() => Event.Create(
            "   ", null, "Club", NextMonth, "USD", 10, [Tier("GA", 20m, 10)], Now));

        ex.Code.ShouldBe("event.name.required");
    }
}

public sealed class TicketReservationTests
{
    [Fact]
    public void Reserve_reduces_availability_of_tier_and_event()
    {
        var concert = Concert();
        var vip = concert.TierNamed("VIP");

        concert.ReserveTickets(vip.Id, 3, Now);

        vip.Sold.ShouldBe(3);
        vip.Available.ShouldBe(7);
        concert.TicketsAvailable.ShouldBe(97);
    }

    [Fact]
    public void Reserve_exactly_the_remaining_seats_succeeds()
    {
        var concert = Concert();
        var vip = concert.TierNamed("VIP");
        concert.ReserveTickets(vip.Id, 5, Now);

        concert.ReserveTickets(vip.Id, 5, Now);

        vip.Available.ShouldBe(0);
    }

    [Fact]
    public void Reserve_more_than_remaining_is_rejected_without_changing_inventory()
    {
        var concert = Concert();
        var vip = concert.TierNamed("VIP");
        concert.ReserveTickets(vip.Id, 8, Now);

        var ex = Should.Throw<DomainConflictException>(() => concert.ReserveTickets(vip.Id, 3, Now));

        ex.Code.ShouldBe("tickets.sold_out");
        ex.Message.ShouldContain("Only 2 tickets remain");
        vip.Sold.ShouldBe(8);
    }

    [Fact]
    public void One_tier_selling_out_does_not_affect_another()
    {
        var concert = Concert();
        concert.ReserveTickets(concert.TierNamed("VIP").Id, 10, Now);

        Should.Throw<DomainConflictException>(() => concert.ReserveTickets(concert.TierNamed("VIP").Id, 1, Now));
        Should.NotThrow(() => concert.ReserveTickets(concert.TierNamed("General Admission").Id, 1, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Reserve_rejects_quantity_outside_per_order_limits(int quantity)
    {
        var concert = Concert();

        var ex = Should.Throw<BusinessRuleViolationException>(
            () => concert.ReserveTickets(concert.TierNamed("General Admission").Id, quantity, Now));

        ex.Code.ShouldBe("order.quantity_invalid");
    }

    [Fact]
    public void Reserve_on_cancelled_event_is_rejected()
    {
        var concert = Concert();
        concert.Cancel(Now);

        var ex = Should.Throw<DomainConflictException>(() => concert.ReserveTickets(concert.Tiers[0].Id, 1, Now));
        ex.Code.ShouldBe("event.cancelled");
    }

    [Fact]
    public void Reserve_once_event_has_started_is_rejected()
    {
        var concert = Concert();

        var ex = Should.Throw<DomainConflictException>(() => concert.ReserveTickets(concert.Tiers[0].Id, 1, concert.StartsAt));
        ex.Code.ShouldBe("event.already_started");
    }

    [Fact]
    public void Reserve_on_tier_of_another_event_is_rejected()
    {
        var concert = Concert();

        var ex = Should.Throw<BusinessRuleViolationException>(() => concert.ReserveTickets(Guid.NewGuid(), 1, Now));
        ex.Code.ShouldBe("tier.unknown");
    }
}

public sealed class EventUpdateTests
{
    [Fact]
    public void Update_can_add_a_tier_and_grow_capacity()
    {
        var concert = Concert();
        var tiers = concert.CurrentTiers();
        tiers.Add(Tier("Balcony", 30m, 20));

        concert.UpdateTiers(tiers);

        concert.TotalCapacity.ShouldBe(120);
        concert.Tiers.Count.ShouldBe(3);
    }

    [Fact]
    public void Update_cannot_shrink_a_tier_below_tickets_already_sold()
    {
        var concert = Concert();
        var vip = concert.TierNamed("VIP");
        concert.ReserveTickets(vip.Id, 6, Now);
        var tiers = concert.CurrentTiers().Select(t => t.Id == vip.Id ? t with { Capacity = 5 } : t).ToList();

        var ex = Should.Throw<DomainConflictException>(() => concert.UpdateTiers(tiers));

        ex.Code.ShouldBe("tier.capacity_below_sold");
        vip.Capacity.ShouldBe(10);
    }

    [Fact]
    public void Update_can_shrink_a_tier_down_to_exactly_tickets_sold()
    {
        var concert = Concert();
        var vip = concert.TierNamed("VIP");
        concert.ReserveTickets(vip.Id, 6, Now);
        var tiers = concert.CurrentTiers().Select(t => t.Id == vip.Id ? t with { Capacity = 6 } : t).ToList();

        concert.UpdateTiers(tiers);

        vip.Capacity.ShouldBe(6);
        vip.Available.ShouldBe(0);
    }

    [Fact]
    public void Update_cannot_remove_a_tier_that_has_sales()
    {
        var concert = Concert();
        var vip = concert.TierNamed("VIP");
        concert.ReserveTickets(vip.Id, 1, Now);
        var withoutVip = concert.CurrentTiers().Where(t => t.Id != vip.Id).ToList();

        var ex = Should.Throw<DomainConflictException>(() => concert.UpdateTiers(withoutVip));
        ex.Code.ShouldBe("tier.has_sales");
    }

    [Fact]
    public void Update_can_remove_a_tier_without_sales()
    {
        var concert = Concert();
        var withoutVip = concert.CurrentTiers().Where(t => t.Name != "VIP").ToList();

        concert.UpdateTiers(withoutVip);

        concert.Tiers.ShouldHaveSingleItem().Name.ShouldBe("General Admission");
        concert.TotalCapacity.ShouldBe(90);
    }

    [Fact]
    public void Update_rejects_tier_ids_from_elsewhere()
    {
        var concert = Concert();
        var tiers = concert.CurrentTiers();
        tiers[0] = tiers[0] with { Id = Guid.NewGuid() };

        var ex = Should.Throw<BusinessRuleViolationException>(() => concert.UpdateTiers(tiers));
        ex.Code.ShouldBe("tier.unknown");
    }

    [Fact]
    public void Update_cannot_change_currency_after_sales()
    {
        var concert = Concert();
        concert.ReserveTickets(concert.Tiers[0].Id, 1, Now);

        var ex = Should.Throw<DomainConflictException>(() => concert.UpdateTiers(concert.CurrentTiers(), currency: "EUR"));
        ex.Code.ShouldBe("event.currency_locked");
    }

    [Fact]
    public void Update_can_change_currency_before_sales_and_reprices_tiers()
    {
        var concert = Concert();

        concert.UpdateTiers(concert.CurrentTiers(), currency: "EUR");

        concert.Currency.ShouldBe("EUR");
        concert.Tiers.ShouldAllBe(t => t.Price.Currency == "EUR");
    }

    [Fact]
    public void Update_of_cancelled_event_is_rejected()
    {
        var concert = Concert();
        concert.Cancel(Now);

        var ex = Should.Throw<DomainConflictException>(() => concert.UpdateTiers(concert.CurrentTiers()));
        ex.Code.ShouldBe("event.cancelled");
    }
}

public sealed class EventLifecycleTests
{
    [Fact]
    public void Cancel_is_idempotent()
    {
        var concert = Concert();

        concert.Cancel(Now);
        concert.Cancel(Now.AddMinutes(1));

        concert.Status.ShouldBe(EventStatus.Cancelled);
        concert.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Cancel_after_start_is_rejected()
    {
        var concert = Concert();

        var ex = Should.Throw<DomainConflictException>(() => concert.Cancel(concert.StartsAt.AddMinutes(5)));
        ex.Code.ShouldBe("event.already_started");
    }

    [Fact]
    public void Delete_without_sales_soft_deletes()
    {
        var concert = Concert();

        concert.Delete(Now);

        concert.IsDeleted.ShouldBeTrue();
        concert.DeletedAt.ShouldBe(Now);
    }

    [Fact]
    public void Delete_with_sales_is_rejected_so_financial_records_survive()
    {
        var concert = Concert();
        concert.ReserveTickets(concert.Tiers[0].Id, 1, Now);

        var ex = Should.Throw<DomainConflictException>(() => concert.Delete(Now));

        ex.Code.ShouldBe("event.has_sales");
        concert.IsDeleted.ShouldBeFalse();
    }
}
