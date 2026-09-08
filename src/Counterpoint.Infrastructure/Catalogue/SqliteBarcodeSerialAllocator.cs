using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// Allocates internal-barcode serials out of <c>app_setting</c> with one
/// <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>, inside the caller's transaction
/// (SRS FR-2.10). See <see cref="IBarcodeSerialAllocator"/> for why this is not
/// <c>number_sequence</c>.
/// </summary>
/// <remarks>
/// The row is left holding <em>next</em> serial to hand out, and <c>RETURNING value - 1</c> hands
/// back the one just consumed - the same shape <c>SqliteDocumentNumberAllocator</c> uses for
/// <c>number_sequence</c>, so a first call returns 1, not 0.
/// </remarks>
internal sealed class SqliteBarcodeSerialAllocator : IBarcodeSerialAllocator
{
    /// <summary>Not in <c>SettingKeys</c> - see <c>BarcodeMaintenanceService</c>'s own key for why.</summary>
    private const string SerialSettingKey = "catalogue.internal_barcode.next_serial";

    private const string AllocateSql =
        """
        INSERT INTO app_setting(key, value, value_type, updated_by, updated_at)
        VALUES ($key, '2', 'INT', NULL, $now)
        ON CONFLICT(key) DO UPDATE SET
            value = CAST(app_setting.value AS INTEGER) + 1,
            updated_at = $now
        RETURNING CAST(value AS INTEGER) - 1;
        """;

    private readonly SqliteUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SqliteBarcodeSerialAllocator(SqliteUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<long> AllocateAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (connection, transaction, token) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = AllocateSql;

                var key = command.CreateParameter();
                key.ParameterName = "$key";
                key.Value = SerialSettingKey;
                command.Parameters.Add(key);

                var now = command.CreateParameter();
                now.ParameterName = "$now";
                now.Value = _timeProvider.GetLocalNow().ToString("O", CultureInfo.InvariantCulture);
                command.Parameters.Add(now);

                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);

                if (!await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "The internal barcode serial allocator's own INSERT ... RETURNING produced no row.");
                }

                return reader.GetInt64(0);
            },
            cancellationToken);
}
