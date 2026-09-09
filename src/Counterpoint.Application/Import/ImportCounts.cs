namespace Counterpoint.Application.Import;

/// <summary>
/// How many rows fell into each bucket. Identical between <c>PreviewAsync</c> and
/// <c>CommitAsync</c> for the same file and mapping - the same plan is built both times, and only
/// <c>CommitAsync</c> writes it (SRS FR-2.22 "dry run and commit produce identical counts").
/// </summary>
public sealed record ImportCounts(int TotalRows, int Creates, int Updates, int Skips, int Errors);
