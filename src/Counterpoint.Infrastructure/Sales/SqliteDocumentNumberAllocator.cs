using System;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Allocates document numbers with one <c>UPDATE ... RETURNING</c> against
/// <c>number_sequence</c>, inside the caller's transaction (CLAUDE.md invariant 4).
/// </summary>
/// <remarks>
/// <para>
/// One statement, not a read followed by a write: the read-modify-write is atomic in SQLite,
/// so there is no window in which two callers could see the same value. The single-writer gate
/// makes that hard to reach anyway; the statement makes it impossible.
/// </para>
/// <para>
/// The row is left holding the <i>next</i> value, and <c>RETURNING next_val - 1</c> hands back
/// the one just consumed - so a table seeded at <c>next_val = 1</c> issues 1 first.
/// </para>
/// </remarks>
internal sealed class SqliteDocumentNumberAllocator : IDocumentNumberAllocator
{
    private const string AllocateSql =
        """
        UPDATE number_sequence
           SET next_val = next_val + 1
         WHERE doc_type = $doc_type
        RETURNING next_val - 1, prefix, pattern;
        """;

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteDocumentNumberAllocator(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<string> AllocateAsync(
        string docType,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docType);

        // Re-entrant: called from inside the sale's unit of work it joins that transaction, so
        // a rollback takes the allocation with it.
        return _unitOfWork.ExecuteInTransactionAsync(
            async (connection, transaction, token) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = AllocateSql;

                var parameter = command.CreateParameter();
                parameter.ParameterName = "$doc_type";
                parameter.Value = docType;
                command.Parameters.Add(parameter);

                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);

                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"There is no number_sequence row for '{docType}'. Document numbering is seeded on first run; the till must not trade without it."));
                }

                return Format(reader.GetString(2), reader.GetString(1), reader.GetInt64(0), businessDate);
            },
            cancellationToken);
    }

    /// <summary>
    /// Renders the stored pattern. Three tokens, which is all docs/01_DATA_MODEL.md §11 uses:
    /// <c>{prefix}</c>, <c>{yyyy}</c> and <c>{n:...}</c> with any numeric format string.
    /// </summary>
    /// <remarks>
    /// An unrecognised token is an error, not something to print literally: a bill numbered
    /// <c>INV-{yyy}-000001</c> would be a permanent, unfixable record in an append-only table.
    /// </remarks>
    private static string Format(string pattern, string prefix, long value, DateOnly businessDate)
    {
        var text = pattern
            .Replace("{prefix}", prefix, StringComparison.Ordinal)
            .Replace("{yyyy}", businessDate.Year.ToString("0000", CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var start = text.IndexOf("{n:", StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = text.IndexOf('}', start);
            if (end > start)
            {
                var format = text[(start + 3)..end];
                text = string.Concat(
                    text.AsSpan(0, start),
                    value.ToString(format, CultureInfo.InvariantCulture),
                    text.AsSpan(end + 1));
            }
        }
        else
        {
            text = text.Replace("{n}", value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        if (text.Contains('{', StringComparison.Ordinal) || text.Contains('}', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The number pattern '{pattern}' has a token this build does not understand. Supported tokens are {{prefix}}, {{yyyy}} and {{n}} or {{n:format}}."));
        }

        return text;
    }
}
