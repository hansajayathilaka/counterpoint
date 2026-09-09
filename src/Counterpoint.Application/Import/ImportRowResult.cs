using System.Collections.Generic;

namespace Counterpoint.Application.Import;

/// <summary>One spreadsheet row's outcome, for the validation report and the dry-run sample (SRS FR-2.22).</summary>
/// <param name="RowNumber">The row's 1-based position in the file, header row included (so row 2 is the first data row).</param>
/// <param name="Code">The row's code column, as read, or null when the row could not even supply one.</param>
/// <param name="Outcome">What this row would do.</param>
/// <param name="Errors">
/// Every validation problem found on this row. Empty unless <see cref="Outcome"/> is
/// <see cref="ImportRowOutcome.Error"/> - a row can fail more than one check at once, and the
/// report names all of them, not just the first (SRS FR-2.22 "reports all 10").
/// </param>
public sealed record ImportRowResult(
    int RowNumber,
    string? Code,
    ImportRowOutcome Outcome,
    IReadOnlyList<string> Errors);
