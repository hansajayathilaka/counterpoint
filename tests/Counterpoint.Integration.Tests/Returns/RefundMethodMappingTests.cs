using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Returns;

/// <summary>
/// <see cref="RefundMethodMapping"/> no longer refuses <see cref="RefundMethod.CreditNote"/>
/// (task P2-T05): a credit note is now a real, redeemable row, not a placeholder a return could
/// never actually choose. Reachable here, without a database, through the same
/// <c>InternalsVisibleTo</c> seam <c>OverrideTokenTests</c> uses for its own internal Domain type
/// (<see cref="RefundMethodMapping"/> is <c>internal</c>, in <c>Counterpoint.Application</c>).
/// </summary>
public sealed class RefundMethodMappingTests
{
    [Fact]
    public void CreditNoteIsASupportedRefundMethodNotRefused()
    {
        var act = () => RefundMethodMapping.RequireSupported(RefundMethod.CreditNote);

        act.Should().NotThrow(
            "task P2-T05 backs CreditNote with an actual credit_note row now - the refuse-everything "
            + "behaviour RequireSupported used to have for it is gone; only Exchange is still refused");
    }

    [Fact]
    public void ExchangeIsStillRefusedAsAStandaloneRefundMethod()
    {
        var act = () => RefundMethodMapping.RequireSupported(RefundMethod.Exchange);

        act.Should().Throw<System.InvalidOperationException>(
            "Exchange marks a return settled by a paired sale - a standalone return never has one");
    }

    [Fact]
    public void CreditNoteMapsToTheCreditNoteTenderType()
    {
        RefundMethodMapping.ToTenderType(RefundMethod.CreditNote).Should().Be(TenderTypes.CreditNote);
    }

    [Fact]
    public void CreditNoteMapsToTheCreditNoteAuditToken()
    {
        RefundMethodMapping.ToAuditToken(RefundMethod.CreditNote).Should().Be("CREDIT_NOTE");
    }
}
