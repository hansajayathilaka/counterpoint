using System.Collections.Generic;

namespace Counterpoint.Application.Import;

/// <summary>
/// A spreadsheet or CSV file, read into memory as plain text (SRS FR-2.22, FR-2.23).
/// </summary>
/// <remarks>
/// Deliberately format-agnostic: nothing above <see cref="ISpreadsheetReader"/> and
/// <see cref="ISpreadsheetWriter"/> in <c>Counterpoint.Infrastructure</c> ever knows whether the
/// file on disk was <c>.xlsx</c> or <c>.csv</c> - the Application layer's column-mapping and
/// validation work identically over either, which is the whole point of reading both into one
/// shape (docs/03_PHASE_1_core_trading.md P1-T13).
/// </remarks>
/// <param name="Headers">The first row, as given in the file - not normalised, not deduplicated.</param>
/// <param name="Rows">
/// Every row after the header, in file order. A row's cells line up with <see cref="Headers"/> by
/// position; a short row (fewer cells than headers) pads with null when read, never throws.
/// </param>
public sealed record SpreadsheetTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string?>> Rows);
