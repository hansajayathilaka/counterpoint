namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>number_sequence</c>: the only source of document numbers (CLAUDE.md invariant 4).
/// The primary key is <c>doc_type</c>, a TEXT column - never an integer id. See Schema/README.md.
/// </summary>
internal sealed class NumberSequence
{
    public string DocType { get; set; } = string.Empty;

    /// <summary>For example <c>INV-</c>.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>For example <c>{prefix}{yyyy}-{n:000000}</c>.</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Plain counter. Not scaled. Allocated by <c>UPDATE ... RETURNING</c>.</summary>
    public long NextVal { get; set; }
}
