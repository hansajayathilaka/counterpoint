using System;

namespace Counterpoint.Backup.Restore;

/// <summary>What <see cref="RestoreService.PreviewAsync"/> read, before any passphrase is asked for.</summary>
/// <param name="TakenAt">The date the data will be restored to.</param>
/// <param name="SchemaVersion">The schema version the backup was taken under.</param>
public sealed record BackupPreview(DateTimeOffset TakenAt, string SchemaVersion);
