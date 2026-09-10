using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Dapper;

namespace Counterpoint.SeedGenerator;

/// <summary>
/// The hash-chain verification command (P1-T16, CLAUDE.md invariant 6, SRS NFR-S8): walks
/// <c>sale</c> and <c>audit_log</c> in id order and reports the first row whose stored
/// <c>row_hash</c> does not match what its <c>prev_hash</c> and its own content produce.
/// </summary>
/// <remarks>
/// <para>
/// Read-only, off a plain read connection - never the write gate - because verifying does not
/// change anything and must never queue behind a bill in progress.
/// </para>
/// <para>
/// This is <em>the check</em>, not <em>the screen</em>: <c>HashChain.Verify</c>'s own remarks
/// call out that "the chain walker that reports the first break" belongs to the audit log viewer
/// (P3-T08). What is here is exactly enough to let this task's own acceptance tests (AC-15's
/// hundred process kills) and <c>scripts/seed.sh</c>'s operator prove the chain is intact, over
/// however many rows a seeded or a real database holds - P3-T08 builds the reporting screen on
/// top of the same two methods rather than re-deriving them.
/// </para>
/// </remarks>
internal static class HashChainVerifier
{
    /// <summary>One chain's verdict: intact, or the first row where it broke.</summary>
    internal readonly record struct ChainResult(long RowsChecked, long? FirstBrokenRowId)
    {
        internal bool IsIntact => FirstBrokenRowId is null;
    }

    /// <summary>Verifies every <c>sale</c> row's place in its chain, from genesis forward.</summary>
    internal static async Task<ChainResult> VerifySaleChainAsync(
        IPosConnectionFactory connectionFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        var connection = await connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<SaleChainRow>(new CommandDefinition(
                """
                SELECT bill_no AS BillNo, sold_at AS SoldAt, business_date AS BusinessDate,
                       customer_id AS CustomerId, user_id AS UserId, shift_id AS ShiftId,
                       subtotal AS Subtotal, line_discount AS LineDiscount, bill_discount AS BillDiscount,
                       tax AS Tax, rounding AS Rounding, total AS Total, cogs AS Cogs, note AS Note,
                       id AS Id, prev_hash AS PrevHash, row_hash AS RowHash
                  FROM sale
                 ORDER BY id;
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var expectedPrev = HashChain.GenesisHash;
            long checkedCount = 0;

            foreach (var row in rows)
            {
                checkedCount++;

                var sale = new Sale
                {
                    BillNo = row.BillNo,
                    SoldAt = DateTimeOffset.Parse(row.SoldAt, CultureInfo.InvariantCulture),
                    BusinessDate = row.BusinessDate,
                    CustomerId = row.CustomerId,
                    UserId = row.UserId,
                    ShiftId = row.ShiftId,
                    Subtotal = Money.FromScaled(row.Subtotal),
                    LineDiscount = Money.FromScaled(row.LineDiscount),
                    BillDiscount = Money.FromScaled(row.BillDiscount),
                    Tax = Money.FromScaled(row.Tax),
                    Rounding = Money.FromScaled(row.Rounding),
                    Total = Money.FromScaled(row.Total),
                    Cogs = Money.FromScaled(row.Cogs),
                    Note = row.Note,
                };

                var prevMatches = string.Equals(row.PrevHash, expectedPrev, StringComparison.Ordinal);
                var rowMatches = HashChain.Verify(row.PrevHash, SaleHashChain.Canonicalise(sale), row.RowHash);

                if (!prevMatches || !rowMatches)
                {
                    return new ChainResult(checkedCount, row.Id);
                }

                expectedPrev = row.RowHash;
            }

            return new ChainResult(checkedCount, null);
        }
    }

    /// <summary>Verifies every <c>audit_log</c> row's place in its chain, from genesis forward.</summary>
    internal static async Task<ChainResult> VerifyAuditLogChainAsync(
        IPosConnectionFactory connectionFactory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        var connection = await connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<AuditChainRow>(new CommandDefinition(
                """
                SELECT occurred_at AS OccurredAt, user_id AS UserId, action AS Action,
                       entity_type AS EntityType, entity_id AS EntityId, before_json AS BeforeJson,
                       after_json AS AfterJson, reason AS Reason,
                       id AS Id, prev_hash AS PrevHash, row_hash AS RowHash
                  FROM audit_log
                 ORDER BY id;
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var expectedPrev = HashChain.GenesisHash;
            long checkedCount = 0;

            foreach (var row in rows)
            {
                checkedCount++;

                var entry = new AuditLog
                {
                    OccurredAt = DateTimeOffset.Parse(row.OccurredAt, CultureInfo.InvariantCulture),
                    UserId = row.UserId,
                    Action = row.Action,
                    EntityType = row.EntityType,
                    EntityId = row.EntityId,
                    BeforeJson = row.BeforeJson,
                    AfterJson = row.AfterJson,
                    Reason = row.Reason,
                };

                var prevMatches = string.Equals(row.PrevHash, expectedPrev, StringComparison.Ordinal);
                var rowMatches = HashChain.Verify(row.PrevHash, AuditLogHashChain.Canonicalise(entry), row.RowHash);

                if (!prevMatches || !rowMatches)
                {
                    return new ChainResult(checkedCount, row.Id);
                }

                expectedPrev = row.RowHash;
            }

            return new ChainResult(checkedCount, null);
        }
    }

    /// <summary>The flat shape Dapper maps one <c>sale</c> row onto.</summary>
    private sealed class SaleChainRow
    {
        public long Id { get; set; }

        public string BillNo { get; set; } = string.Empty;

        public string SoldAt { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public long? CustomerId { get; set; }

        public long UserId { get; set; }

        public long ShiftId { get; set; }

        public long Subtotal { get; set; }

        public long LineDiscount { get; set; }

        public long BillDiscount { get; set; }

        public long Tax { get; set; }

        public long Rounding { get; set; }

        public long Total { get; set; }

        public long Cogs { get; set; }

        public string? Note { get; set; }

        public string PrevHash { get; set; } = string.Empty;

        public string RowHash { get; set; } = string.Empty;
    }

    /// <summary>The flat shape Dapper maps one <c>audit_log</c> row onto.</summary>
    private sealed class AuditChainRow
    {
        public long Id { get; set; }

        public string OccurredAt { get; set; } = string.Empty;

        public long? UserId { get; set; }

        public string Action { get; set; } = string.Empty;

        public string EntityType { get; set; } = string.Empty;

        public long? EntityId { get; set; }

        public string? BeforeJson { get; set; }

        public string? AfterJson { get; set; }

        public string? Reason { get; set; }

        public string PrevHash { get; set; } = string.Empty;

        public string RowHash { get; set; } = string.Empty;
    }
}
