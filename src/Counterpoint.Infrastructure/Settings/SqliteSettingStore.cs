using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Settings;

/// <summary>
/// <c>app_setting</c>, read off a read connection and written through the unit of work
/// (SRS FR-10, docs/01_DATA_MODEL.md §8).
/// </summary>
/// <remarks>
/// <para>
/// Reads take a read connection: settings are loaded at start-up and after every save, and
/// neither should queue behind whatever the till is writing. Writes join the caller's
/// transaction, so a row and the <c>audit_log</c> entry that explains it commit together or not
/// at all (FR-10.9).
/// </para>
/// <para>
/// <b>Only the keys it is handed.</b> There is no "replace everything" path, because
/// <c>app_setting</c> is shared: P1-T02's <c>security.*</c> rows live in the same table and a
/// wholesale rewrite would erase them. <c>updated_at</c> moves only when the value actually
/// changes, so the column keeps saying when the setting last moved rather than when Save was
/// last pressed.
/// </para>
/// </remarks>
internal sealed class SqliteSettingStore : ISettingStore
{
    private const string SelectAllSql = "SELECT key, value, value_type FROM app_setting;";

    private readonly IPosConnectionFactory _connectionFactory;
    private readonly SqliteUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SqliteSettingStore(
        IPosConnectionFactory connectionFactory,
        SqliteUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _connectionFactory = connectionFactory;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, StoredSetting>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = new Dictionary<string, StoredSetting>(StringComparer.Ordinal);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = SelectAllSql;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                settings[reader.GetString(0)] = new StoredSetting(reader.GetString(1), reader.GetString(2));
            }
        }

        return settings;
    }

    /// <inheritdoc />
    public Task WriteAsync(
        IReadOnlyList<SettingWrite> writes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writes);

        if (writes.Count == 0)
        {
            return Task.CompletedTask;
        }

        var now = _timeProvider.GetLocalNow();

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                foreach (var write in writes)
                {
                    await UpsertAsync(context, write, now, token).ConfigureAwait(false);
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }

    private static async Task UpsertAsync(
        PosDbContext context,
        SettingWrite write,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.Set<AppSetting>()
            .FirstOrDefaultAsync(setting => setting.Key == write.Key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.Add(new AppSetting
            {
                Key = write.Key,
                Value = write.Value,
                ValueType = write.ValueType,
                UpdatedBy = write.UpdatedBy,
                UpdatedAt = now,
            });

            return;
        }

        if (string.Equals(existing.Value, write.Value, StringComparison.Ordinal)
            && string.Equals(existing.ValueType, write.ValueType, StringComparison.Ordinal))
        {
            return;
        }

        existing.Value = write.Value;
        existing.ValueType = write.ValueType;
        existing.UpdatedBy = write.UpdatedBy;
        existing.UpdatedAt = now;
    }
}
