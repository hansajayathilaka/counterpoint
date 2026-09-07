using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Security;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// <c>app_user</c>, read off a read connection and written through the unit of work
/// (SRS FR-1.1, FR-1.4, NFR-S9).
/// </summary>
/// <remarks>
/// <para>
/// Reads take a read connection so that checking a password does not queue behind whatever the
/// till is writing; writes join the caller's transaction so the row and the <c>audit_log</c> entry
/// that explains it commit together or not at all.
/// </para>
/// <para>
/// <c>app_user</c> is not one of the append-only tables (CLAUDE.md invariant 5), so these are
/// ordinary updates - but each one is narrow on purpose. Nothing here writes a whole row back,
/// because a whole-row write is how a stale copy of <c>failed_attempts</c> silently unlocks an
/// account somebody else just locked.
/// </para>
/// </remarks>
internal sealed class SqliteUserStore : IUserStore
{
    private const string SelectColumns =
        """
        SELECT id, username, display_name, password_hash, role, active,
               failed_attempts, locked_until, last_login
          FROM app_user
        """;

    private const string ByUsernameSql = SelectColumns + """

         WHERE username = $username
         LIMIT 1;
        """;

    private const string ByIdSql = SelectColumns + """

         WHERE id = $id
         LIMIT 1;
        """;

    private const string ActiveOwnersSql = SelectColumns + """

         WHERE role = 'OWNER' AND active = 1
         ORDER BY id;
        """;

    private const string AllSql = SelectColumns + """

         ORDER BY username;
        """;

    private readonly IPosConnectionFactory _connectionFactory;
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteUserStore(IPosConnectionFactory connectionFactory, SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _connectionFactory = connectionFactory;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<UserRecord?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);

        var rows = await QueryAsync(ByUsernameSql, ("$username", username), cancellationToken)
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0];
    }

    /// <inheritdoc />
    public async Task<UserRecord?> FindByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(ByIdSql, ("$id", userId), cancellationToken).ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRecord>> ListActiveOwnersAsync(
        CancellationToken cancellationToken = default) =>
        await QueryAsync(ActiveOwnersSql, parameter: null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(AllSql, parameter: null, cancellationToken).ConfigureAwait(false);

        // Projected here, so the password hash never leaves this method (CLAUDE.md invariant 8's
        // pattern: keep it off the shape, not just off the screen).
        return [.. rows.Select(row => new UserSummary(
            row.Id,
            row.Username,
            row.DisplayName,
            row.Role,
            row.Active,
            row.LockedUntil,
            row.LastLogin))];
    }

    /// <inheritdoc />
    public Task<long> CreateAsync(NewUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new AppUser
                {
                    Username = user.Username,
                    DisplayName = user.DisplayName,
                    PasswordHash = user.PasswordHash,
                    Role = Roles.ToToken(user.Role),
                    Active = true,
                    FailedAttempts = 0,
                    LockedUntil = null,
                    LastLogin = null,
                    CreatedAt = user.CreatedAt,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetPasswordHashAsync(
        long userId,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return UpdateAsync(
            userId,
            row => row.PasswordHash = passwordHash,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetActiveAsync(long userId, bool active, CancellationToken cancellationToken = default) =>
        UpdateAsync(userId, row => row.Active = active, cancellationToken);

    /// <inheritdoc />
    public Task RecordSuccessfulSignInAsync(
        long userId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            userId,
            row =>
            {
                row.FailedAttempts = 0;
                row.LockedUntil = null;
                row.LastLogin = at;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task ClearFailedAttemptsAsync(long userId, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            userId,
            row =>
            {
                row.FailedAttempts = 0;
                row.LockedUntil = null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task RecordFailedSignInAsync(
        long userId,
        int failedAttempts,
        DateTimeOffset? lockedUntil,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failedAttempts);

        return UpdateAsync(
            userId,
            row =>
            {
                row.FailedAttempts = failedAttempts;
                row.LockedUntil = lockedUntil;
            },
            cancellationToken);
    }

    /// <summary>
    /// Loads the row inside the caller's transaction, applies the change, saves. EF writes only
    /// the columns the delegate actually touched.
    /// </summary>
    private Task<object?> UpdateAsync(long userId, Action<AppUser> change, CancellationToken cancellationToken) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<AppUser>()
                    .FirstOrDefaultAsync(user => user.Id == userId, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"There is no app_user row with id {userId}."));

                change(row);

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);

    private async Task<IReadOnlyList<UserRecord>> QueryAsync(
        string sql,
        (string Name, object Value)? parameter,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            if (parameter is { } supplied)
            {
                var bound = command.CreateParameter();
                bound.ParameterName = supplied.Name;
                bound.Value = supplied.Value;
                command.Parameters.Add(bound);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var rows = new List<UserRecord>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(Read(reader));
            }

            return rows;
        }
    }

    /// <summary>
    /// Maps one row. The timestamps are read back with the same fixed-width ISO-8601 format EF
    /// wrote them with (<see cref="Iso8601TimestampConverter.Format"/>, DM-06) - this read goes
    /// round EF, so it has to agree with EF rather than guess.
    /// </summary>
    private static UserRecord Read(DbDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        Roles.Parse(reader.GetString(4)),
        reader.GetInt64(5) != 0,
        (int)reader.GetInt64(6),
        Timestamp(reader, 7),
        Timestamp(reader, 8));

    private static DateTimeOffset? Timestamp(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.ParseExact(
                reader.GetString(ordinal),
                Iso8601TimestampConverter.Format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);
}
