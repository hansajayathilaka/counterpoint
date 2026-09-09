using System.Collections.Generic;

namespace Counterpoint.Application.Import;

/// <summary>
/// A dry run's result: counts over every row, and a bounded sample of each bucket - never every
/// row, so previewing a 20 000-line file does not have to hold 20 000
/// <see cref="ImportRowResult"/> instances in the DTO handed back to a screen (SRS FR-2.22).
/// Nothing is written to the database to produce this.
/// </summary>
public sealed record ImportPreviewReport(
    ImportCounts Counts,
    IReadOnlyList<ImportRowResult> CreateSample,
    IReadOnlyList<ImportRowResult> UpdateSample,
    IReadOnlyList<ImportRowResult> SkipSample,
    IReadOnlyList<ImportRowResult> ErrorSample);
