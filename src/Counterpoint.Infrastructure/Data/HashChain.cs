using System;
using System.Security.Cryptography;
using System.Text;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The tamper-evidence primitive behind <c>sale</c> and <c>audit_log</c> (CLAUDE.md
/// invariant 6, SRS NFR-S8).
/// </summary>
/// <remarks>
/// <para>
/// <b>The definition, in full.</b>
/// <c>row_hash = lowercase_hex( SHA256( UTF8( prev_hash ‖ canonical_json(row) ) ) )</c>, where
/// <c>‖</c> is plain string concatenation and <c>prev_hash</c> is the 64 hex characters of the
/// previous row's <c>row_hash</c> in the same chain, ordered by <c>id</c>.
/// </para>
/// <para>
/// <b>Genesis.</b> The first row of an empty chain uses <see cref="GenesisHash"/>: sixty-four
/// zeros. A real hash could not be told apart from a row that had been deleted from the front
/// of the chain; a value no SHA-256 output can be can.
/// </para>
/// <para>
/// <b>What goes into <c>canonical_json</c>.</b> An explicit, declared field order - the DDL
/// column order of the table - built by <see cref="CanonicalJson"/>. Never reflection order,
/// which changes when someone moves a property. Money, quantities and rates are rendered as
/// the scaled integers they are stored as; timestamps in the same fixed-width ISO-8601 form the
/// database holds; nulls as <c>null</c>; no whitespace anywhere; invariant culture throughout.
/// <c>id</c> is excluded, because it is assigned by the insert this hash is part of and so
/// cannot be known while computing it. <c>row_hash</c> is excluded for the same circular
/// reason, and <c>prev_hash</c> is excluded from the JSON because it is already the
/// concatenation's prefix - hashing it twice would add nothing.
/// </para>
/// <para>
/// <b>Where it runs.</b> Inside the business transaction, reading the previous row's hash on
/// the same connection and the same transaction, so the chain cannot fork. The single-writer
/// gate already guarantees that; reading it inside the transaction means the guarantee does not
/// depend on the gate.
/// </para>
/// </remarks>
public static class HashChain
{
    /// <summary>Length of a chain hash in hex characters.</summary>
    public const int HashLength = 64;

    /// <summary>
    /// The <c>prev_hash</c> of the first row in a chain: sixty-four zeros. No SHA-256 output is
    /// all zeros, so a genesis row is distinguishable from a row whose predecessor was removed.
    /// </summary>
    public static string GenesisHash { get; } = new string('0', HashLength);

    /// <summary>
    /// Computes a row hash from the previous row's hash and this row's canonical form.
    /// </summary>
    /// <param name="previousHash">
    /// The previous <c>row_hash</c>, or <see cref="GenesisHash"/> for the first row.
    /// </param>
    /// <param name="canonicalJson">This row's canonical JSON, from <see cref="CanonicalJson"/>.</param>
    /// <returns>Sixty-four lowercase hex characters.</returns>
    public static string Compute(string previousHash, string canonicalJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousHash);
        ArgumentNullException.ThrowIfNull(canonicalJson);

        var bytes = Encoding.UTF8.GetBytes(previousHash + canonicalJson);

        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// True when <paramref name="rowHash"/> is what this row's contents and its predecessor
    /// produce. The chain walker that reports the first break is P3-T08's; this is the check it
    /// will use.
    /// </summary>
    public static bool Verify(string previousHash, string canonicalJson, string rowHash) =>
        string.Equals(Compute(previousHash, canonicalJson), rowHash, StringComparison.Ordinal);
}
