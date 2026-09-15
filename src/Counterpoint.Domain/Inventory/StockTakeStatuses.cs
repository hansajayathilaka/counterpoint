using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Counterpoint.Domain.Inventory;

/// <summary>
/// What a <see cref="StockTakeStatus"/> is called in the database, and back
/// (docs/01_DATA_MODEL.md §4). The same shape as
/// <c>Counterpoint.Domain.Purchasing.PurchaseOrderStatuses</c>: the three tokens are exactly the
/// ones <c>ck_stock_take_status</c> constrains <c>stock_take.status</c> to, spelled out once so no
/// adapter can spell them differently.
/// </summary>
public static class StockTakeStatuses
{
    public const string OpenToken = "OPEN";

    public const string PostedToken = "POSTED";

    public const string AbandonedToken = "ABANDONED";

    /// <summary>The database token for a stock take status.</summary>
    public static string ToToken(StockTakeStatus status) => status switch
    {
        StockTakeStatus.Open => OpenToken,
        StockTakeStatus.Posted => PostedToken,
        StockTakeStatus.Abandoned => AbandonedToken,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "There are exactly three stock take statuses."),
    };

    /// <summary>Reads a database token back into a stock take status.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The token is not one of the three the schema allows.
    /// </exception>
    public static StockTakeStatus Parse(string token)
    {
        if (TryParse(token, out var status))
        {
            return status;
        }

        throw new ArgumentOutOfRangeException(
            nameof(token),
            token,
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{token}' is not a stock take status. stock_take.status is constrained to "
                + $"'{OpenToken}', '{PostedToken}' or '{AbandonedToken}'."));
    }

    /// <summary>Reads a database token back into a stock take status, without throwing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? token, out StockTakeStatus status)
    {
        switch (token)
        {
            case OpenToken:
                status = StockTakeStatus.Open;
                return true;
            case PostedToken:
                status = StockTakeStatus.Posted;
                return true;
            case AbandonedToken:
                status = StockTakeStatus.Abandoned;
                return true;
            default:
                status = StockTakeStatus.Open;
                return false;
        }
    }
}
