using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.Infrastructure.CreditNotes;

/// <summary>
/// Issues and redeems credit notes (SRS FR-5 store credit, FR-3 tender, task P2-T05).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IssueAsync"/> is a plain insert, written through EF and the shared
/// <see cref="PosDbContext"/> - the same shape <c>SqliteSaleReturnWriter</c> uses for
/// <c>sale_return</c> - because it is exactly that: one new row, no guard, no conditional.
/// </para>
/// <para>
/// <see cref="RedeemAsync"/> is not. It is the literal statement docs/01_DATA_MODEL.md §6
/// documents for the guarded decrement - <c>UPDATE credit_note SET amount_remaining =
/// amount_remaining - :amt WHERE id = :id AND amount_remaining >= :amt</c>, checked by
/// rows-affected - written as raw ADO/SQL against the write connection and transaction directly,
/// the same low-level shape <c>SqliteDocumentNumberAllocator</c> and
/// <c>SqliteBarcodeSerialAllocator</c> use for their own guarded, atomic read-modify-writes. An
/// EF LINQ expression has no clean way to translate <c>Money - Money</c> through
/// <c>ScaledMoneyConverter</c> into that single SQL statement, and the statement itself is short
/// enough that writing it by hand, once, is safer than fighting the translator for it.
/// </para>
/// </remarks>
internal sealed class SqliteCreditNoteStore : ICreditNoteIssuer, ICreditNoteRedeemer
{
    private const string ActiveStatus = "ACTIVE";
    private const string SpentStatus = "SPENT";
    private const string DateFormat = "yyyy-MM-dd";

    private const string FindForRedemptionSql =
        """
        SELECT id, status, expires_on
          FROM credit_note
         WHERE number = $number
         LIMIT 1;
        """;

    private const string GuardedDecrementSql =
        """
        UPDATE credit_note
           SET amount_remaining = amount_remaining - $amount
         WHERE id = $id AND amount_remaining >= $amount
        RETURNING amount_remaining;
        """;

    private const string InsertRedemptionSql =
        """
        INSERT INTO credit_note_redemption (credit_note_id, sale_id, amount, redeemed_at)
        VALUES ($credit_note_id, $sale_id, $amount, $redeemed_at);
        """;

    private const string MarkSpentSql =
        """
        UPDATE credit_note SET status = $status WHERE id = $id AND status = $active_status;
        """;

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteCreditNoteStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<long> IssueAsync(NewCreditNote note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new CreditNote
                {
                    Number = note.Number,
                    SaleReturnId = note.SaleReturnId,
                    CustomerId = note.CustomerId,
                    AmountIssued = note.Amount,
                    AmountRemaining = note.Amount,
                    IssuedAt = note.IssuedAt,
                    ExpiresOn = note.ExpiresOn?.ToString(DateFormat, CultureInfo.InvariantCulture),
                    Status = ActiveStatus,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RedeemedCreditNote> RedeemAsync(
        string number,
        Money amount,
        long saleId,
        DateTimeOffset redeemedAt,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        if (!amount.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount.Amount, "A credit note redemption must be more than zero.");
        }

        return _unitOfWork.ExecuteInTransactionAsync(
            async (connection, transaction, token) =>
            {
                var (id, status, expiresOn) = await FindForRedemptionAsync(connection, transaction, number, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Credit note '{number}' does not exist."));

                RequireRedeemable(number, status, expiresOn, businessDate);

                var newRemaining = await GuardedDecrementAsync(connection, transaction, id, amount, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Credit note {number} does not have {amount} left to redeem."));

                await InsertRedemptionAsync(connection, transaction, id, saleId, amount, redeemedAt, token)
                    .ConfigureAwait(false);

                var fullySpent = newRemaining.IsZero;
                if (fullySpent)
                {
                    await MarkSpentAsync(connection, transaction, id, token).ConfigureAwait(false);
                }

                return new RedeemedCreditNote(id, amount, newRemaining, fullySpent);
            },
            cancellationToken);
    }

    /// <summary>
    /// Refuses a redemption that is not <c>ACTIVE</c>, or is expired as of
    /// <paramref name="businessDate"/> (task P2-T05 step 3: evaluated here, at redemption, never
    /// by a background job). Deliberately does not write <c>status = 'EXPIRED'</c> on the way out
    /// - the whole sale transaction this call is inside is about to roll back on the exception
    /// below, so a write here would roll back with it and never reach disk; recording EXPIRED
    /// durably needs a caller that keeps its own transaction after the refusal, which no caller
    /// of this method does.
    /// </summary>
    private static void RequireRedeemable(string number, string status, string? expiresOn, DateOnly businessDate)
    {
        if (!string.Equals(status, ActiveStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Credit note {number} is {status}, not ACTIVE, and cannot be redeemed."));
        }

        if (expiresOn is not null)
        {
            var expiry = DateOnly.ParseExact(expiresOn, DateFormat, CultureInfo.InvariantCulture);

            if (expiry < businessDate)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Credit note {number} expired on {expiry:yyyy-MM-dd} and cannot be redeemed."));
            }
        }
    }

    private static async Task<(long Id, string Status, string? ExpiresOn)?> FindForRedemptionAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string number,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = FindForRedemptionSql;
        AddParameter(command, "$number", number);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var id = reader.GetInt64(0);
        var status = reader.GetString(1);
        var expiresOn = reader.IsDBNull(2) ? null : reader.GetString(2);

        return (id, status, expiresOn);
    }

    /// <summary>
    /// The guarded decrement itself. Returns the new <c>amount_remaining</c> when the guard
    /// passed, or null when it did not - the note did not have <paramref name="amount"/> left,
    /// which the caller turns into a clear domain error rather than letting
    /// <c>ck_credit_note_amount_remaining_bounds</c> ever have to say so.
    /// </summary>
    private static async Task<Money?> GuardedDecrementAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        long id,
        Money amount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = GuardedDecrementSql;
        AddParameter(command, "$id", id);
        AddParameter(command, "$amount", amount.ToScaled());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Money.FromScaled(reader.GetInt64(0));
    }

    private static async Task InsertRedemptionAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        long creditNoteId,
        long saleId,
        Money amount,
        DateTimeOffset redeemedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertRedemptionSql;
        AddParameter(command, "$credit_note_id", creditNoteId);
        AddParameter(command, "$sale_id", saleId);
        AddParameter(command, "$amount", amount.ToScaled());
        AddParameter(command, "$redeemed_at", redeemedAt.ToString(Iso8601TimestampConverter.Format, CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task MarkSpentAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        long id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = MarkSpentSql;
        AddParameter(command, "$id", id);
        AddParameter(command, "$status", SpentStatus);
        AddParameter(command, "$active_status", ActiveStatus);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
