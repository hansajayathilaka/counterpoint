using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Counterpoint.Domain.Purchasing;

/// <summary>
/// What a <see cref="PurchaseOrderStatus"/> is called in the database, and back
/// (docs/01_DATA_MODEL.md §4). The same shape as
/// <c>Counterpoint.Domain.Catalogue.ProductTypes</c>: the five tokens are exactly the ones
/// <c>ck_purchase_order_status</c> constrains <c>purchase_order.status</c> to, spelled out once
/// so no adapter can spell them differently.
/// </summary>
public static class PurchaseOrderStatuses
{
    public const string DraftToken = "DRAFT";

    public const string SentToken = "SENT";

    public const string PartialToken = "PARTIAL";

    public const string ReceivedToken = "RECEIVED";

    public const string CancelledToken = "CANCELLED";

    /// <summary>The database token for a purchase order status.</summary>
    public static string ToToken(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => DraftToken,
        PurchaseOrderStatus.Sent => SentToken,
        PurchaseOrderStatus.Partial => PartialToken,
        PurchaseOrderStatus.Received => ReceivedToken,
        PurchaseOrderStatus.Cancelled => CancelledToken,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "There are exactly five purchase order statuses."),
    };

    /// <summary>Reads a database token back into a purchase order status.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The token is not one of the five the schema allows.
    /// </exception>
    public static PurchaseOrderStatus Parse(string token)
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
                $"'{token}' is not a purchase order status. purchase_order.status is constrained "
                + $"to '{DraftToken}', '{SentToken}', '{PartialToken}', '{ReceivedToken}' or "
                + $"'{CancelledToken}'."));
    }

    /// <summary>Reads a database token back into a purchase order status, without throwing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? token, out PurchaseOrderStatus status)
    {
        switch (token)
        {
            case DraftToken:
                status = PurchaseOrderStatus.Draft;
                return true;
            case SentToken:
                status = PurchaseOrderStatus.Sent;
                return true;
            case PartialToken:
                status = PurchaseOrderStatus.Partial;
                return true;
            case ReceivedToken:
                status = PurchaseOrderStatus.Received;
                return true;
            case CancelledToken:
                status = PurchaseOrderStatus.Cancelled;
                return true;
            default:
                status = PurchaseOrderStatus.Draft;
                return false;
        }
    }

    /// <summary>
    /// True for <see cref="PurchaseOrderStatus.Received"/> and
    /// <see cref="PurchaseOrderStatus.Cancelled"/> - nothing changes a purchase order's status
    /// once it is here (FR-4.10).
    /// </summary>
    public static bool IsTerminal(PurchaseOrderStatus status) =>
        status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled;
}
