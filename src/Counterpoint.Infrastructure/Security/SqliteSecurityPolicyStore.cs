using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// Writes the password and lockout policy into <c>app_setting</c> (docs/01_DATA_MODEL.md §8).
/// </summary>
/// <remarks>
/// Seven <c>INT</c> rows in one transaction, keyed by the constants on
/// <see cref="SecurityPolicyRecorder"/>. <c>updated_by</c> is left null: no user did this, the
/// build did, and inventing a user id for it would put a name against a change nobody made.
/// </remarks>
internal sealed class SqliteSecurityPolicyStore : ISecurityPolicyStore
{
    private const string IntegerValueType = "INT";

    private readonly SqliteUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SqliteSecurityPolicyStore(SqliteUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task RecordAsync(RecordedSecurityPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var now = _timeProvider.GetLocalNow();

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                await UpsertAsync(context, SecurityPolicyRecorder.Argon2MemoryKibKey, policy.Argon2MemoryKib, now, token).ConfigureAwait(false);
                await UpsertAsync(context, SecurityPolicyRecorder.Argon2IterationsKey, policy.Argon2Iterations, now, token).ConfigureAwait(false);
                await UpsertAsync(context, SecurityPolicyRecorder.Argon2ParallelismKey, policy.Argon2Parallelism, now, token).ConfigureAwait(false);
                await UpsertAsync(context, SecurityPolicyRecorder.MinimumPasswordLengthKey, policy.MinimumPasswordLength, now, token).ConfigureAwait(false);
                await UpsertAsync(context, SecurityPolicyRecorder.MaxFailedAttemptsKey, policy.MaxFailedAttempts, now, token).ConfigureAwait(false);
                await UpsertAsync(context, SecurityPolicyRecorder.LockoutBaseSecondsKey, policy.LockoutBaseSeconds, now, token).ConfigureAwait(false);
                await UpsertAsync(context, SecurityPolicyRecorder.LockoutMaximumSecondsKey, policy.LockoutMaximumSeconds, now, token).ConfigureAwait(false);

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);
    }

    private static async Task UpsertAsync(
        PosDbContext context,
        string key,
        int value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);

        var existing = await context.Set<AppSetting>()
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.Add(new AppSetting
            {
                Key = key,
                Value = text,
                ValueType = IntegerValueType,
                UpdatedBy = null,
                UpdatedAt = now,
            });

            return;
        }

        if (string.Equals(existing.Value, text, StringComparison.Ordinal) &&
            string.Equals(existing.ValueType, IntegerValueType, StringComparison.Ordinal))
        {
            // Unchanged. Leaving updated_at alone means the column keeps saying when the value
            // last actually moved, which is the only thing it is useful for.
            return;
        }

        existing.Value = text;
        existing.ValueType = IntegerValueType;
        existing.UpdatedBy = null;
        existing.UpdatedAt = now;
    }
}
