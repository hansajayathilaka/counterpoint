using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.Infrastructure.Backup;

/// <summary>
/// Appends to <c>backup_record</c> through the unit of work, in its own transaction
/// (docs/01_DATA_MODEL.md §8, SRS FR-11.1-11.4).
/// </summary>
/// <remarks>
/// A plain insert, not hash-chained like <c>sale</c> and <c>audit_log</c>:
/// <c>backup_record</c> is not one of CLAUDE.md's append-only, trigger-protected tables. It still
/// goes through the single write connection, because every write to this database file does.
/// </remarks>
internal sealed class SqliteBackupRecordStore : IBackupRecordStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteBackupRecordStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task RecordAsync(NewBackupRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                context.Add(new BackupRecord
                {
                    Filename = record.Filename,
                    TakenAt = record.TakenAt,
                    SizeBytes = record.SizeBytes,
                    Checksum = record.Checksum,
                    SchemaVer = record.SchemaVer,
                    LocalPath = record.LocalPath,
                    UsbStatus = record.UsbStatus,
                    CloudStatus = record.CloudStatus,
                });

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }
}
