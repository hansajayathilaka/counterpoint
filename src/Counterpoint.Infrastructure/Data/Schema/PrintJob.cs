namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>print_job</c>: the print outbox written inside the sale transaction
/// (CLAUDE.md invariant 7). See Schema/README.md.
/// </summary>
internal sealed class PrintJob
{
    public long Id { get; set; }

    public string DocType { get; set; } = string.Empty;

    public long? DocId { get; set; }

    /// <summary><c>RECEIPT</c> | <c>A4</c> | <c>LABEL</c>.</summary>
    public string Target { get; set; } = "RECEIPT";

    /// <summary>Rendered ESC/POS bytes, or a PDF path.</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>Plain count. Not scaled.</summary>
    public int Copies { get; set; }

    /// <summary>FR-7.6.</summary>
    public bool IsDuplicate { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>Plain count. Not scaled.</summary>
    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? PrintedAt { get; set; }
}
