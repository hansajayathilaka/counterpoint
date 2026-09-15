using System;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Application.Cash;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Cash;

/// <summary>
/// Cash in and cash out (SRS FR-8.2, FR-1.7, task P3-T01 "Do this" #1, #3, #4).
/// </summary>
public sealed class CashMovementServiceTests
{
    private const string Owner = "owner";
    private const string OwnerPassword = "till2026";
    private const string CashierUsername = "priya";
    private const string CashierPassword = "counter1";

    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 10, 11, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_8_2_CashInIsRecordedWithReasonUserAndTimestamp()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var recorded = await fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            shiftId, cashier.Id, Money.FromDecimal(2000m), "Float top-up", OccurredAt));

        recorded.Direction.Should().Be(CashMovementDirection.In);
        recorded.Amount.Should().Be(Money.FromDecimal(2000m));

        (await fixture.ScalarAsync(
            "SELECT direction || '|' || amount || '|' || reason || '|' || user_id || '|' || occurred_at "
            + "FROM cash_movement WHERE id = " + recorded.MovementId + ";"))
            .Should().Be("IN|20000000|Float top-up|" + cashier.Id + "|"
                + OccurredAt.ToString(Iso8601TimestampConverter.Format, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task FR_8_2_CashOutBelowTheThresholdIsRecordedWithoutAnOverride()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        // Default threshold is 5000 (SettingDefaults.Policy.CashOutAuthorisationThreshold).
        var recorded = await fixture.Resolve<ICashMovementService>().RecordCashOutAsync(new RecordCashOutCommand(
            shiftId, cashier.Id, Money.FromDecimal(1500m), "Petty expense", OccurredAt));

        recorded.Direction.Should().Be(CashMovementDirection.Out);

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = '" + CashMovementAuditActions.CashOutAboveThreshold + "';"))
            .Should().Be(0, "a cash-out below the threshold needs no owner authorisation and no extra audit row");
    }

    [Fact]
    public async Task P3_T01_CashOutAboveTheThresholdIsRefusedWithoutAnOwnerOverride()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var attempt = () => fixture.Resolve<ICashMovementService>().RecordCashOutAsync(new RecordCashOutCommand(
            shiftId, cashier.Id, Money.FromDecimal(6000m), "Supplier payment", OccurredAt));

        await attempt.Should().ThrowAsync<CashOutAuthorisationRequiredException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM cash_movement;")).Should().Be(
            0, "a refused attempt must not have written a row");
    }

    [Fact]
    public async Task P3_T01_CashOutAboveTheThresholdWithAnOwnerOverrideIsRecordedAndAudited()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var token = await fixture.Resolve<IOwnerOverrideService>().RequestAsync(new OwnerOverrideRequest(
            CashMovementAuditActions.CashOutAboveThreshold, "Owner approved, urgent supplier payment.",
            Owner, OwnerPassword));

        var recorded = await fixture.Resolve<ICashMovementService>().RecordCashOutAsync(new RecordCashOutCommand(
            shiftId, cashier.Id, Money.FromDecimal(6000m), "Supplier payment", OccurredAt, OwnerOverride: token));

        recorded.Amount.Should().Be(Money.FromDecimal(6000m));

        // Audited twice over, deliberately (task P3-T01 "Done when": "cash out above the
        // threshold ... is audited") - the grant itself, naming both the cashier and the owner...
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE action = '" + SecurityAuditActions.OwnerOverrideGranted + "';"))
            .Should().Be(1);

        // ...and the cash-movement-scoped row a future exceptions report (P3-T08) filters on.
        (await fixture.ScalarAsync(
            "SELECT entity_type || '|' || entity_id FROM audit_log WHERE action = '"
            + CashMovementAuditActions.CashOutAboveThreshold + "';"))
            .Should().Be("cash_movement|" + recorded.MovementId);

        // Single-use: the same token cannot authorise a second cash-out.
        var reuse = () => fixture.Resolve<ICashMovementService>().RecordCashOutAsync(new RecordCashOutCommand(
            shiftId, cashier.Id, Money.FromDecimal(6000m), "Second attempt", OccurredAt, OwnerOverride: token));
        await reuse.Should().ThrowAsync<CashOutAuthorisationRequiredException>();
    }

    [Fact]
    public async Task P3_T01_AnOptionalPrintedSlipIsQueuedInTheOutboxNeverPrintedDirectly()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var recorded = await fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            shiftId, cashier.Id, Money.FromDecimal(2000m), "Float top-up", OccurredAt, PrintSlip: true));

        recorded.PrintJobId.Should().NotBeNull();

        (await fixture.ScalarAsync(
            "SELECT doc_type || '|' || doc_id || '|' || status FROM print_job WHERE id = " + recorded.PrintJobId + ";"))
            .Should().Be("CASH_SLIP|" + recorded.MovementId + "|PENDING");

        var withoutSlip = await fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            shiftId, cashier.Id, Money.FromDecimal(500m), "Float top-up", OccurredAt));

        withoutSlip.PrintJobId.Should().BeNull("PrintSlip defaults to false - the slip is optional (task P3-T01)");
    }

    [Fact]
    public async Task ABlankReasonIsRefused()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        var attempt = () => fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            shiftId, cashier.Id, Money.FromDecimal(100m), "   ", OccurredAt));

        await attempt.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ACashMovementCannotBeRecordedAgainstAShiftTheCallerIsNotCurrentlyTradingIn()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;

        var attempt = () => fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            999_999, cashier.Id, Money.FromDecimal(100m), "Float top-up", OccurredAt));

        await attempt.Should().ThrowAsync<NotAuthorisedException>();
    }

    [Fact]
    public async Task FR_8_2_TheCashierSeesTheHistoryForTheirOwnShiftButNotAnotherShift()
    {
        await using var fixture = await SignedInAsCashierAsync();

        var cashier = fixture.Resolve<ISession>().CurrentUser!;
        var firstShiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        await fixture.Resolve<ICashMovementService>().RecordCashInAsync(new RecordCashInCommand(
            firstShiftId, cashier.Id, Money.FromDecimal(1000m), "Float top-up", OccurredAt));

        var ownHistory = await fixture.Resolve<ICashMovementService>().GetHistoryAsync(firstShiftId);
        ownHistory.Should().HaveCount(1);
        ownHistory[0].Reason.Should().Be("Float top-up");
        ownHistory[0].UserId.Should().Be(cashier.Id);

        // Close the shift directly (P3-T03's own close handler does not exist yet, the same
        // technique OpenShiftHandlerTests uses) and open a second one, so there is a shift the
        // signed-in cashier is not currently trading in.
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(cashier.Id, Money.Zero, OccurredAt.AddHours(1)));

        var attempt = () => fixture.Resolve<ICashMovementService>().GetHistoryAsync(firstShiftId);
        await attempt.Should().ThrowAsync<NotAuthorisedException>(
            "a cashier may only see the cash movement history for the shift they are currently trading in");

        // An owner may see any shift's history.
        await AsOwnerAsync(fixture, async () =>
        {
            var ownerView = await fixture.Resolve<ICashMovementService>().GetHistoryAsync(firstShiftId);
            ownerView.Should().HaveCount(1);
        });
    }

    private static async Task AsOwnerAsync(SaleFixture fixture, Func<Task> action)
    {
        var authentication = fixture.Resolve<IAuthenticationService>();

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(Owner, OwnerPassword)).Succeeded.Should().BeTrue();

        await action();

        await authentication.LogOutAsync();
        (await authentication.LogInAsync(CashierUsername, CashierPassword)).Succeeded.Should().BeTrue();
    }

    /// <summary>An owner account with a password, a cashier account, and the cashier signed in - same shape as ReturnPolicyAuthorisationServiceTests.</summary>
    internal static async Task<SaleFixture> SignedInAsCashierAsync()
    {
        var fixture = await SaleFixture.CreateAsync();

        try
        {
            await fixture.Resolve<IInitialOwnerSetup>().CompleteAsync(Owner, OwnerPassword);

            var authentication = fixture.Resolve<IAuthenticationService>();
            await authentication.LogInAsync(Owner, OwnerPassword);

            await fixture.Resolve<IUserAdministration>().CreateAsync(
                new CreateUserCommand(CashierUsername, "Priya", CashierPassword, Role.Cashier));

            await authentication.LogOutAsync();
            (await authentication.LogInAsync(CashierUsername, CashierPassword)).Succeeded.Should().BeTrue();

            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }
}
