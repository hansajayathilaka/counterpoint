using System.Globalization;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// How a report figure becomes text. Display only: the figures arrive already computed by the
/// Application layer, and nothing here adds, rounds for storage or derives one (SRS FR-9.6). The same
/// invariant two-decimal format the Overview dashboard already uses.
/// </summary>
internal static class ReportText
{
    internal static string Money(Money amount) =>
        amount.Amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    internal static string Quantity(Quantity quantity, string? uomSymbol) =>
        string.IsNullOrEmpty(uomSymbol)
            ? quantity.Value.ToString("0.####", CultureInfo.InvariantCulture)
            : quantity.Value.ToString("0.####", CultureInfo.InvariantCulture) + " " + uomSymbol;

    /// <summary>A fraction (0.25) as a percentage (25.0%).</summary>
    internal static string Percent(decimal fraction) =>
        (fraction * 100m).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    internal static string Date(System.DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string Hour(int hour) =>
        hour.ToString("00", CultureInfo.InvariantCulture) + ":00-" + hour.ToString("00", CultureInfo.InvariantCulture) + ":59";

    internal static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>A timestamp as the shop reads it: date and minute, in the offset it was recorded with.</summary>
    internal static string Stamp(System.DateTimeOffset moment) =>
        moment.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A tax rate as a percentage without trailing zeros: 15%, 7.5%.</summary>
    internal static string Rate(TaxRate rate) =>
        rate.AsPercent.ToString("0.####", CultureInfo.InvariantCulture) + "%";

    /// <summary>A plain decimal, four places at most and no grouping: 12.5, 0.0833.</summary>
    internal static string Decimal(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// A stored token (<c>BANK_TRANSFER</c>, <c>SALE</c>, <c>RETURN_IN</c>) as words: "Bank transfer", "Sale", "Return in".
    /// </summary>
    internal static string Token(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        var words = token.Replace('_', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
