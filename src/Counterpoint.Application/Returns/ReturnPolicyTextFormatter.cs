using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Returns;

/// <summary>
/// Turns the shop's return policy settings into the sentence a receipt or a settings screen
/// shows a customer or a cashier (SRS NFR-L3, task P2-T01 step 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The one rule this file exists to prove:</b> the text a receipt prints and the rule
/// <see cref="ReturnPolicyAuthorisationService"/> enforces both read the same
/// <see cref="PolicySettings"/> instance. Change <c>policy.return_window_days</c> in settings and
/// both the sentence this method builds and what the engine allows change together, because there
/// is exactly one place either of them gets the number from - there is no second, hand-typed
/// copy of the window, the fee or the non-returnable list for this formatter to drift from.
/// </para>
/// <para>
/// This is deliberately not wired into <c>ReceiptSettings.PolicyText</c> - that field is the
/// owner's own free-text blurb (opening hours, a slogan, anything), typed once in the settings
/// screen and printed as-is. Where a future return receipt (task P2-T02) calls this formatter
/// instead of, or alongside, that free text is that task's decision, not this one's.
/// </para>
/// </remarks>
public static class ReturnPolicyTextFormatter
{
    /// <summary>
    /// Describes <paramref name="policy"/> in one sentence per rule, in the order a customer
    /// would want to read them.
    /// </summary>
    /// <param name="policy">The policy settings in force right now.</param>
    /// <param name="nonReturnableCategoryNames">
    /// The display names of the categories named in <see cref="PolicySettings.NonReturnableCategoryIds"/>,
    /// resolved by the caller (this formatter has no catalogue to look them up in - Domain and
    /// Application both stay free of a database dependency here).
    /// </param>
    public static string Describe(PolicySettings policy, IReadOnlyList<string> nonReturnableCategoryNames)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(nonReturnableCategoryNames);

        var sentences = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"Returns are accepted within {policy.ReturnWindowDays} day{(policy.ReturnWindowDays == 1 ? string.Empty : "s")} of purchase."),
        };

        sentences.Add(policy.ReceiptRequired
            ? "The original bill number is required."
            : "The original bill is helpful but not required.");

        if (nonReturnableCategoryNames.Count > 0)
        {
            sentences.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{string.Join(", ", nonReturnableCategoryNames)} are non-returnable."));
        }

        sentences.Add(policy.RestockingFeeRate.IsZero
            ? "No restocking fee applies."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"A {policy.RestockingFeeRate.AsPercent:0.##}% restocking fee applies."));

        if (!policy.CashRefundLimit.IsZero)
        {
            sentences.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Cash refunds are capped at {policy.CashRefundLimit}; the rest is refunded by {RefundMethodText(policy.DefaultRefundMethod)}."));
        }

        sentences.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"Refunds are issued by {RefundMethodText(policy.DefaultRefundMethod)} unless agreed otherwise."));

        var builder = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(sentence);
        }

        return builder.ToString();
    }

    private static string RefundMethodText(RefundMethod method) => method switch
    {
        RefundMethod.CreditNote => "store credit",
        RefundMethod.Card => "card",
        _ => "cash",
    };
}
