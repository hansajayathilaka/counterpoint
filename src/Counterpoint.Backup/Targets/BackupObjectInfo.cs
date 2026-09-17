using System;

namespace Counterpoint.Backup.Targets;

/// <summary>One object an <see cref="IBackupTarget"/> reports back from <see cref="IBackupTarget.ListAsync"/>.</summary>
/// <param name="Key">The object's key, exactly as it was uploaded under.</param>
/// <param name="SizeBytes">Size in bytes. <c>long</c>, never <c>double</c> (CLAUDE.md invariant 1).</param>
/// <param name="LastModified">When the object was last written, as the target itself reports it.</param>
public sealed record BackupObjectInfo(string Key, long SizeBytes, DateTimeOffset LastModified);
