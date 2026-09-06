using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The canonical form of an <c>audit_log</c> row, and its place in the chain
/// (CLAUDE.md invariant 6, SRS NFR-S8).
/// </summary>
/// <remarks>
/// Same rules as <see cref="SaleHashChain"/>: the <c>CREATE TABLE audit_log</c> column order of
/// docs/01_DATA_MODEL.md §8, less <c>id</c>, <c>prev_hash</c> and <c>row_hash</c>. Two chains,
/// one definition of what a link is.
/// </remarks>
internal static class AuditLogHashChain
{
    /// <summary>The canonical JSON of one audit entry.</summary>
    internal static string Canonicalise(AuditLog entry) => new CanonicalJson()
        .Add("occurred_at", entry.OccurredAt)
        .Add("user_id", entry.UserId)
        .Add("action", entry.Action)
        .Add("entity_type", entry.EntityType)
        .Add("entity_id", entry.EntityId)
        .Add("before_json", entry.BeforeJson)
        .Add("after_json", entry.AfterJson)
        .Add("reason", entry.Reason)
        .ToString();

    /// <summary>The row hash of one audit entry, given its predecessor's.</summary>
    internal static string RowHash(string previousHash, AuditLog entry) =>
        HashChain.Compute(previousHash, Canonicalise(entry));

    /// <summary>True when a stored audit entry still hashes to what it claims.</summary>
    internal static bool Verify(AuditLog entry) =>
        HashChain.Verify(entry.PrevHash, Canonicalise(entry), entry.RowHash);
}
