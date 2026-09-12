using System;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Returns;

/// <summary>
/// NFR-L3: the sentence a receipt would print and the rule the engine enforces both come from
/// the one <see cref="PolicySettings"/> instance - there is no second, hand-typed copy of the
/// return window, the fee or the non-returnable list for either to drift from (task P2-T01
/// step 4).
/// </summary>
public sealed class ReturnPolicyTextFormatterTests
{
    [Fact]
    public void NFR_L3_ChangingTheReturnWindowInSettingsChangesBothTheEnforcedRuleAndThePrintedText()
    {
        var fourteenDays = SettingDefaults.Policy with { ReturnWindowDays = 14 };
        var thirtyDays = SettingDefaults.Policy with { ReturnWindowDays = 30 };

        var saleDate = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var now = saleDate.AddDays(20);

        // Enforcement: the very same PolicySettings.ReturnWindowDays this test is about to
        // describe in text is what ReturnPolicy.EvaluateReturnWindow enforces - no separate
        // number is invented for either side.
        ReturnPolicy.EvaluateReturnWindow(saleDate, now, fourteenDays.ReturnWindowDays)
            .Should().BeOfType<ReturnEligibility.AllowedWithOverride>(
                "20 days after the sale is outside a 14-day window");

        ReturnPolicy.EvaluateReturnWindow(saleDate, now, thirtyDays.ReturnWindowDays)
            .Should().BeOfType<ReturnEligibility.Allowed>(
                "20 days after the sale is still inside a 30-day window");

        // The printed text: built from the same two PolicySettings values as above.
        var fourteenDayText = ReturnPolicyTextFormatter.Describe(fourteenDays, []);
        var thirtyDayText = ReturnPolicyTextFormatter.Describe(thirtyDays, []);

        fourteenDayText.Should().Contain("14 day");
        thirtyDayText.Should().Contain("30 day");
        thirtyDayText.Should().NotContain("14 day");
    }

    [Fact]
    public void NFR_L3_TheReceiptRequirementSentenceMatchesWhatTheEngineEnforces()
    {
        var receiptRequired = SettingDefaults.Policy with { ReceiptRequired = true };
        var receiptNotRequired = SettingDefaults.Policy with { ReceiptRequired = false };

        ReturnPolicy.EvaluateReceiptRequirement(receiptRequired.ReceiptRequired, billReferencePresented: false)
            .Should().BeOfType<ReturnEligibility.AllowedWithOverride>();
        ReturnPolicy.EvaluateReceiptRequirement(receiptNotRequired.ReceiptRequired, billReferencePresented: false)
            .Should().BeOfType<ReturnEligibility.Allowed>();

        ReturnPolicyTextFormatter.Describe(receiptRequired, []).Should().Contain("bill number is required");
        ReturnPolicyTextFormatter.Describe(receiptNotRequired, []).Should().Contain("not required");
    }

    [Fact]
    public void FR_5_10_NonReturnableCategoryNamesAppearInThePrintedTextWhenTheListIsNotEmpty()
    {
        var policy = SettingDefaults.Policy with { NonReturnableCategoryIds = [3, 7] };

        var text = ReturnPolicyTextFormatter.Describe(policy, ["Paint", "Cut Cable"]);

        text.Should().Contain("Paint, Cut Cable are non-returnable");
    }

    [Fact]
    public void FR_5_ApplyingNoCashRefundLimitOmitsTheCappedSentenceEntirely()
    {
        var noLimit = SettingDefaults.Policy with { CashRefundLimit = Money.Zero };
        var limited = SettingDefaults.Policy with { CashRefundLimit = Money.FromDecimal(5000m) };

        ReturnPolicyTextFormatter.Describe(noLimit, []).Should().NotContain("capped");
        ReturnPolicyTextFormatter.Describe(limited, []).Should().Contain("capped");
    }
}
