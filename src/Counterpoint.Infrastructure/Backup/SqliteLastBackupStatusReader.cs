using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Backup;

/// <summary>
/// Reads the most recent <c>backup_record</c> row off a read connection - the read half of
/// <see cref="SqliteBackupRecordStore"/>, which only ever appends (SRS FR-9.7, UI-09).
/// </summary>
internal sealed class SqliteLastBackupStatusReader : ILastBackupStatusReader
{
    private const string LastBackupSql =
        """
        SELECT taken_at AS TakenAt, usb_status AS UsbStatus, cloud_status AS CloudStatus,
               verified_at AS VerifiedAt, last_error AS LastError
          FROM backup_record
         ORDER BY id DESC
         LIMIT 1;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteLastBackupStatusReader(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<LastBackupStatus?> GetLastAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(LastBackupSql, cancellationToken: cancellationToken);
            var row = await connection.QueryFirstOrDefaultAsync<Row>(command).ConfigureAwait(false);

            return row is null
                ? null
                : new LastBackupStatus(
                    DateTimeOffset.Parse(row.TakenAt, CultureInfo.InvariantCulture),
                    row.UsbStatus,
                    row.CloudStatus,
                    row.VerifiedAt is { } verifiedAt ? DateTimeOffset.Parse(verifiedAt, CultureInfo.InvariantCulture) : null,
                    row.LastError);
        }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="LastBackupSql"/> onto.</summary>
    private sealed class Row
    {
        public string TakenAt { get; set; } = string.Empty;

        public string UsbStatus { get; set; } = string.Empty;

        public string CloudStatus { get; set; } = string.Empty;

        public string? VerifiedAt { get; set; }

        public string? LastError { get; set; }
    }
}
