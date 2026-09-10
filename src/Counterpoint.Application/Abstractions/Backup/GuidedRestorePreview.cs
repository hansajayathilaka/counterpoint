using System;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// What the guided restore wizard shows before it asks for a passphrase (SRS FR-11.12): the
/// checksum has already been verified, and the data date is known, without decrypting anything.
/// </summary>
/// <param name="TakenAt">The date the data will be restored to.</param>
/// <param name="SchemaVersion">The schema version the backup was taken under.</param>
public sealed record GuidedRestorePreview(DateTimeOffset TakenAt, string SchemaVersion);
