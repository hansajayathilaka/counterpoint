using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>daily_sales_summary</c> (docs/01_DATA_MODEL.md §7): the day rollup written in the
/// same transaction as the Z report (NFR-P5). Keyed on the business date, not on an id.
/// See Schema/README.md.
/// </summary>
internal sealed class DailySalesSummary
{
    /// <summary><c>YYYY-MM-DD</c> TEXT, not a timestamp. The primary key.</summary>
    public string BusinessDate { get; set; } = string.Empty;

    /// <summary>Plain count. Not scaled.</summary>
    public long BillCount { get; set; }

    public Money Gross { get; set; }

    public Money Discount { get; set; }

    public Money Tax { get; set; }

    public Money Net { get; set; }

    public Money Cogs { get; set; }

    /// <summary>Plain count. Not scaled.</summary>
    public long ReturnCount { get; set; }

    public Money ReturnValue { get; set; }

    public Money TenderCash { get; set; }

    public Money TenderCard { get; set; }

    public Money TenderOther { get; set; }

    public DateTimeOffset BuiltAt { get; set; }
}
