namespace Counterpoint.Application.Import;

/// <summary>What a commit actually wrote - the same counts a dry run over the same file and mapping reported (SRS FR-2.22).</summary>
public sealed record ImportCommitResult(ImportCounts Counts);
