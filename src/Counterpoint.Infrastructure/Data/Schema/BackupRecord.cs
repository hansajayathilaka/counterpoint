namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>backup_record</c> (docs/01_DATA_MODEL.md §8). See Schema/README.md.</summary>
internal sealed class BackupRecord
{
    public long Id { get; set; }

    public string Filename { get; set; } = string.Empty;

    public DateTimeOffset TakenAt { get; set; }

    /// <summary>Plain byte count. Not scaled.</summary>
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the ciphertext.</summary>
    public string Checksum { get; set; } = string.Empty;

    public string SchemaVer { get; set; } = string.Empty;

    public string? LocalPath { get; set; }

    /// <summary><c>NA</c>, <c>OK</c> or <c>FAILED</c>.</summary>
    public string UsbStatus { get; set; } = string.Empty;

    /// <summary><c>PENDING</c>, <c>OK</c>, <c>FAILED</c> or <c>SKIPPED</c>.</summary>
    public string CloudStatus { get; set; } = string.Empty;

    public string? CloudKey { get; set; }

    /// <summary>Plain count. Not scaled.</summary>
    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }
}
