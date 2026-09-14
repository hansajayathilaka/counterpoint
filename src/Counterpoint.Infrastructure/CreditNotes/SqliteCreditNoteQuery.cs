using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.CreditNotes;

/// <summary>
/// Reads credit notes back off a read connection (task P2-T05 steps 3 and 6) - the same split
/// <c>SqliteReturnableSaleLookup</c> draws for a bill, never inside the write transaction
/// <see cref="SqliteCreditNoteStore"/> uses.
/// </summary>
internal sealed class SqliteCreditNoteQuery : ICreditNoteQuery
{
    private const string ByNumberSql =
        """
        SELECT id AS Id, number AS Number, sale_return_id AS SaleReturnId, customer_id AS CustomerId,
               amount_issued AS AmountIssued, amount_remaining AS AmountRemaining,
               issued_at AS IssuedAt, expires_on AS ExpiresOn, status AS Status
          FROM credit_note
         WHERE number = @Number
         LIMIT 1;
        """;

    private const string ByCustomerSql =
        """
        SELECT id AS Id, number AS Number, sale_return_id AS SaleReturnId, customer_id AS CustomerId,
               amount_issued AS AmountIssued, amount_remaining AS AmountRemaining,
               issued_at AS IssuedAt, expires_on AS ExpiresOn, status AS Status
          FROM credit_note
         WHERE customer_id = @CustomerId
         ORDER BY issued_at DESC;
        """;

    // ix_credit_note_customer backs the WHERE above; ix_redemption_credit_note backs the
    // redemption sum here (docs/01_DATA_MODEL.md's own note on both indexes).
    private const string ReconcileSql =
        """
        SELECT
            (SELECT COALESCE(SUM(amount_issued), 0) FROM credit_note) AS TotalIssued,
            (SELECT COALESCE(SUM(amount_remaining), 0) FROM credit_note) AS TotalOutstanding,
            (SELECT COALESCE(SUM(amount), 0) FROM credit_note_redemption) AS TotalRedeemed;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteCreditNoteQuery(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<CreditNoteRecord?> FindByNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(ByNumberSql, new { Number = number }, cancellationToken: cancellationToken);
            var row = await connection.QueryFirstOrDefaultAsync<CreditNoteRow>(command).ConfigureAwait(false);

            return row is null ? null : ToRecord(row);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CreditNoteRecord>> FindByCustomerIdAsync(
        long customerId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(ByCustomerSql, new { CustomerId = customerId }, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<CreditNoteRow>(command).ConfigureAwait(false);

            return [.. rows.Select(ToRecord)];
        }
    }

    /// <inheritdoc />
    public async Task<CreditNoteReconciliation> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(ReconcileSql, cancellationToken: cancellationToken);
            var row = await connection.QuerySingleAsync<ReconcileRow>(command).ConfigureAwait(false);

            return new CreditNoteReconciliation(
                Money.FromScaled(row.TotalIssued),
                Money.FromScaled(row.TotalRedeemed),
                Money.FromScaled(row.TotalOutstanding));
        }
    }

    private static CreditNoteRecord ToRecord(CreditNoteRow row) => new(
        row.Id,
        row.Number,
        row.SaleReturnId,
        row.CustomerId,
        Money.FromScaled(row.AmountIssued),
        Money.FromScaled(row.AmountRemaining),
        DateTimeOffset.Parse(row.IssuedAt, CultureInfo.InvariantCulture),
        row.ExpiresOn is null ? null : DateOnly.ParseExact(row.ExpiresOn, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.Status);

    /// <summary>The flat shape Dapper maps a row of <see cref="ByNumberSql"/>/<see cref="ByCustomerSql"/> onto.</summary>
    private sealed class CreditNoteRow
    {
        public long Id { get; set; }

        public string Number { get; set; } = string.Empty;

        public long SaleReturnId { get; set; }

        public long? CustomerId { get; set; }

        public long AmountIssued { get; set; }

        public long AmountRemaining { get; set; }

        public string IssuedAt { get; set; } = string.Empty;

        public string? ExpiresOn { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="ReconcileSql"/> onto.</summary>
    private sealed class ReconcileRow
    {
        public long TotalIssued { get; set; }

        public long TotalOutstanding { get; set; }

        public long TotalRedeemed { get; set; }
    }
}
