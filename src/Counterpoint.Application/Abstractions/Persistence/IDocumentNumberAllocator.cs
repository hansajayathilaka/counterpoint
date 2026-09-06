using System;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Hands out the next document number for a document type (SRS FR-3.29, CLAUDE.md invariant 4).
/// </summary>
/// <remarks>
/// <para>
/// The number comes from the <c>number_sequence</c> row for the type, allocated with a single
/// <c>UPDATE ... RETURNING</c> <b>inside the business transaction</b>. Never a row-id counter
/// and never "one more than the highest so far": both would either reuse a number after a
/// rollback or race two writers onto the same one, and a bill series with a gap or a duplicate
/// is not a bill series (AC-19).
/// </para>
/// <para>
/// A failed sale therefore consumes its number and leaves a hole nowhere: the allocation is
/// rolled back with everything else. A <i>cancelled</i> sale keeps its number - that is a
/// different case, and it is the sale's status that records it.
/// </para>
/// </remarks>
public interface IDocumentNumberAllocator
{
    /// <summary>
    /// Allocates the next number for <paramref name="docType"/> and renders it through the
    /// pattern stored against that type - <c>{prefix}{yyyy}-{n:000000}</c> for a sale, which
    /// gives <c>INV-2026-000001</c> (Q-16).
    /// </summary>
    /// <param name="docType">
    /// The <c>number_sequence.doc_type</c> key, for example <c>SALE</c>.
    /// </param>
    /// <param name="businessDate">
    /// The document's business date. It supplies the year the pattern prints, so a bill dated
    /// to yesterday's trading day cannot pick up today's year.
    /// </param>
    /// <param name="cancellationToken">Cancels the allocation.</param>
    public Task<string> AllocateAsync(
        string docType,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}
