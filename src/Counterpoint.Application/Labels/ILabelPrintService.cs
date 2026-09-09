using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Labels;

/// <summary>
/// Prints shelf labels for a chosen list of products (SRS FR-2.10, FR-2.12). Owner only, the
/// same as every other catalogue-administration screen (SRS §3.3 ROLE-2, NFR-S2, AC-17) - a
/// shelf label carries the retail price the owner sets, and printing one is a back-office action,
/// not a cashier one.
/// </summary>
/// <remarks>
/// One entry point serves every source the task asks for: a hand-picked product list, a search
/// result set, or a GRN batch (FR-2.12) all reduce to the same
/// <see cref="IReadOnlyList{T}">IReadOnlyList&lt;LabelPrintRequestItem&gt;</see> - a set of
/// variant ids with a quantity each. The GRN screen that will call this after goods are received
/// is Phase 2's; this interface is the hook it calls into, nothing more.
/// </remarks>
[RequiresRole(Role.Owner)]
public interface ILabelPrintService
{
    /// <summary>
    /// Renders and prints one label per <paramref name="items"/> entry, repeated
    /// <see cref="LabelPrintRequestItem.QuantityPerLabel"/> times each, using the shop's current
    /// <c>ISettings.Label</c> layout.
    /// </summary>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="items"/> is empty, or an entry asks for fewer than 1 copy.
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// A requested variant does not exist or is not sellable.
    /// </exception>
    public Task<PrintOutcome> PrintAsync(
        IReadOnlyList<LabelPrintRequestItem> items,
        CancellationToken cancellationToken = default);
}
