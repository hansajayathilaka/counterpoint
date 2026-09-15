using System;
using System.Threading.Tasks;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Security;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Cash;

/// <summary>
/// A no-sale drawer open - always owner authorised, always audited (SRS FR-7.7, task P3-T01
/// "Do this" #5).
/// </summary>
public sealed class NoSaleDrawerServiceTests
{
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";

    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_7_7_ANoSaleDrawerOpenRequiresAnOwnerOverrideAndWritesAnAuditRow()
    {
        await using var fixture = await CashMovementServiceTests.SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            CashMovementAuditActions.NoSaleDrawerOpen, "Cashier needs change for a customer.", Owner, OwnerPassword));

        await fixture.Resolve<INoSaleDrawerService>().OpenAsync(
            new NoSaleDrawerCommand(shiftId, cashier.Id, OccurredAt, token));

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = '" + CashMovementAuditActions.NoSaleDrawerOpen + "' "
            + "AND entity_type = 'shift' AND entity_id = " + shiftId + ";"))
            .Should().Be(1, "task P3-T01 'Done when': a no-sale drawer open writes an audit row");

        // The grant itself is also audited, naming both the cashier and the owner (SRS FR-1.6).
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = '" + SecurityAuditActions.OwnerOverrideGranted + "';"))
            .Should().Be(1);

        // Single-use: the same token cannot open the drawer a second time.
        var reuse = () => fixture.Resolve<INoSaleDrawerService>().OpenAsync(
            new NoSaleDrawerCommand(shiftId, cashier.Id, OccurredAt, token));
        await reuse.Should().ThrowAsync<NotAuthorisedException>();
    }

    [Fact]
    public async Task ATokenGrantedForADifferentActionDoesNotAuthoriseANoSaleDrawerOpen()
    {
        await using var fixture = await CashMovementServiceTests.SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            CashMovementAuditActions.CashOutAboveThreshold, "Wrong action on purpose.", Owner, OwnerPassword));

        var attempt = () => fixture.Resolve<INoSaleDrawerService>().OpenAsync(
            new NoSaleDrawerCommand(shiftId, cashier.Id, OccurredAt, token));

        await attempt.Should().ThrowAsync<NotAuthorisedException>();

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = '" + CashMovementAuditActions.NoSaleDrawerOpen + "';"))
            .Should().Be(0, "a refused attempt must not have written the no-sale audit row");
    }

    [Fact]
    public async Task ANoSaleDrawerOpenCanOnlyBeRecordedAgainstTheShiftCurrentlyOpenOnTheTill()
    {
        await using var fixture = await CashMovementServiceTests.SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            CashMovementAuditActions.NoSaleDrawerOpen, "Test.", Owner, OwnerPassword));

        var attempt = () => fixture.Resolve<INoSaleDrawerService>().OpenAsync(
            new NoSaleDrawerCommand(999_999, cashier.Id, OccurredAt, token));

        await attempt.Should().ThrowAsync<NotAuthorisedException>();
    }
}
