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
}
