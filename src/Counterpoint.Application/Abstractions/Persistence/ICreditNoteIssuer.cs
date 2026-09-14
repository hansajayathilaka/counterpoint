using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Writes the <c>credit_note</c> row a return issues (SRS FR-5 store credit, task P2-T05).
/// </summary>
/// <remarks>
/// Joins the transaction already open on the caller's flow, exactly as
/// <see cref="ISaleReturnWriter"/> does - a return that issues a credit note and the note itself
/// commit together or not at all.
/// </remarks>
public interface ICreditNoteIssuer
{
    /// <summary>Inserts a new, <c>ACTIVE</c> credit note and returns its id.</summary>
    public Task<long> IssueAsync(NewCreditNote note, CancellationToken cancellationToken = default);
}
