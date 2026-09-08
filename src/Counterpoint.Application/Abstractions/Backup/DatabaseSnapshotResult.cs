namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>What <see cref="IDatabaseSnapshotSource.CreateRawCopyAsync"/> produced.</summary>
/// <param name="SchemaVersion">
/// The most recently applied migration id (docs/01_DATA_MODEL.md §8, <c>schema_version</c>).
/// </param>
/// <param name="SizeBytes">Plain byte size of the raw, still SQLCipher-encrypted, copy.</param>
public sealed record DatabaseSnapshotResult(string SchemaVersion, long SizeBytes);
