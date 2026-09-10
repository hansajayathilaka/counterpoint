using System;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Shifts;

/// <summary>
/// Opening a shift (SRS FR-8.1, C-01) - the minimum P1-T14 builds to make <c>sale.shift_id</c>
/// meaningful, and the piece FR-8.7's recovery-on-restart relies on being real
/// (<see cref="ShiftRecoveryTests"/> proves the recovery half).
/// </summary>
public sealed class OpenShiftHandlerTests
{
    [Fact]
    public async Task FR_8_1_OpeningAShiftRecordsTheOpeningFloatShiftNoAndTimestamp()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        // The seeded shift already holds SH-000001 (FirstRunSeeder) - close it directly, the one
        // column-scoped update the append-only triggers already permit (P3-T01 builds the real
        // close handler; this only needs the database to allow reaching "no shift open").
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var openedAt = new DateTimeOffset(2026, 9, 10, 8, 5, 0, TimeSpan.FromHours(5.5));

        var opened = await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(user.Id, Money.FromDecimal(5000m), openedAt));

        opened.ShiftNo.Should().Be("SH-000002", "the seeded shift already consumed SH-000001");
        opened.OpeningFloat.Amount.Should().Be(5000m);

        (await fixture.ScalarAsync(
            "SELECT shift_no || '|' || opening_float || '|' || status || '|' || user_id || '|' || business_date "
            + "FROM shift WHERE id = " + opened.ShiftId + ";"))
            .Should().Be("SH-000002|50000000|OPEN|" + user.Id + "|2026-09-10");

        (await fixture.CountAsync("SELECT COUNT(*) FROM shift WHERE status = 'OPEN';"))
            .Should().Be(1, "C-01: at most one open shift");

        (await fixture.ScalarAsync(
            "SELECT action || '|' || entity_type || '|' || entity_id FROM audit_log "
            + "WHERE action = 'SHIFT_OPENED' ORDER BY id DESC LIMIT 1;"))
            .Should().Be("SHIFT_OPENED|shift|" + opened.ShiftId);
    }

    [Fact]
    public async Task C_01_OpeningAShiftWhileOneIsAlreadyOpenIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var user = fixture.Resolve<ISession>().CurrentUser!;

        var act = () => fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(user.Id, Money.FromDecimal(1000m), DateTimeOffset.Now));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*already open*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM shift;"))
            .Should().Be(1, "the refused attempt must not have written a second row");
    }

    [Fact]
    public async Task FR_1_1_OpeningAShiftRequiresTheCallerToBeTheSignedInUser()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");

        var act = () => fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(UserId: 999_999, Money.FromDecimal(1000m), DateTimeOffset.Now));

        await act.Should().ThrowAsync<InvalidOperationException>();

        (await fixture.CountAsync("SELECT COUNT(*) FROM shift WHERE status = 'OPEN';"))
            .Should().Be(0);
    }

    [Fact]
    public async Task FR_8_1_ANegativeOpeningAmountIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");

        var user = fixture.Resolve<ISession>().CurrentUser!;

        var act = () => fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(user.Id, Money.FromDecimal(-1m), DateTimeOffset.Now));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task P1_T14_OpeningAShiftUpdatesTheCurrentSessionWithoutASignInCycle()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");

        var session = fixture.Resolve<ISession>();
        var previousShiftId = session.ShiftId;

        var opened = await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(session.CurrentUser!.Id, Money.FromDecimal(2500m), DateTimeOffset.Now));

        session.ShiftId.Should().Be(
            opened.ShiftId,
            "the already-signed-in session picks up the newly opened shift without a fresh sign-in");
        session.ShiftId.Should().NotBe(previousShiftId);
    }
}
