using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads a completed bill back exactly as it needs to print again (SRS FR-3.36, FR-7.5): the
/// header, every line and every applied payment, reassembled from <c>sale</c>, <c>sale_line</c>
/// and <c>payment</c> - the same append-only rows the original receipt was rendered from
/// (CLAUDE.md invariant 10).
/// </summary>
/// <remarks>
/// A read connection, not the write one, and open only for the read (the same NFR-P3 discipline
/// <see cref="ISaleLookup"/> keeps for cancellation). <see cref="SaleReceipt.Change"/> comes back
/// as <c>Money.Zero</c>: what a cash tender ran over by was never a durable fact (SRS FR-3.26),
/// so a reprint cannot show it and does not pretend to.
/// </remarks>
public interface ISaleReceiptLookup
{
    /// <summary>The sale, reassembled for printing, or null when no such sale exists.</summary>
    public Task<SaleReceipt?> FindReceiptAsync(long saleId, CancellationToken cancellationToken = default);
}
