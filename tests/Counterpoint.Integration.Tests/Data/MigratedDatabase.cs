using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// A real, encrypted database with the whole migration chain applied, and a connection on which
/// to poke it with raw SQL.
/// </summary>
/// <remarks>
/// <para>
/// A file on disk through <see cref="PosConnectionFactory"/>, never the in-memory provider: the
/// in-memory provider enforces neither foreign keys nor triggers, which is most of what these
/// tests exist to check.
/// </para>
/// <para>
/// The connection is a normal keyed connection from the factory, not the write lease: the runner
/// takes and releases that lease itself, and a test that held it would deadlock the next run.
/// </para>
/// </remarks>
internal sealed class MigratedDatabase : IAsyncDisposable
{
    private readonly TemporaryDataDirectory _fixture;
    private readonly PosConnectionFactory _factory;
    private readonly DbConnection _connection;

    private MigratedDatabase(
        TemporaryDataDirectory fixture,
        PosConnectionFactory factory,
        DbConnection connection,
        MigrationRunResult result)
    {
        _fixture = fixture;
        _factory = factory;
        _connection = connection;
        MigrationResult = result;
    }

    /// <summary>What the migration run that built this database reported.</summary>
    internal MigrationRunResult MigrationResult { get; }

    internal DbConnection Connection => _connection;

    internal PosDataDirectory DataDirectory => _fixture.DataDirectory;

    internal static async Task<MigratedDatabase> CreateAsync(bool seed = true)
    {
        var fixture = new TemporaryDataDirectory();
        var factory = fixture.CreateConnectionFactory();

        try
        {
            var runner = new MigrationRunner(factory, fixture.DataDirectory);
            var result = await runner.ApplyPendingMigrationsAsync();

            var connection = factory.OpenConfiguredConnection();
            var database = new MigratedDatabase(fixture, factory, connection, result);

            if (seed)
            {
                await TradingDaySeed.ApplyAsync(connection);
            }

            return database;
        }
        catch
        {
            await factory.DisposeAsync();
            fixture.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Brings <paramref name="factory"/>'s database up to <paramref name="targetMigration"/> and
    /// no further, so a test can seed it and then migrate it forward with rows already in it.
    /// </summary>
    /// <remarks>
    /// Goes through EF's <see cref="IMigrator"/> rather than <see cref="MigrationRunner"/>, which
    /// deliberately offers no way to stop part-way: a till is only ever brought fully up to date.
    /// </remarks>
    internal static async Task MigrateToAsync(PosConnectionFactory factory, string targetMigration)
    {
        var lease = await factory.AcquireWriteConnectionAsync();
        await using (lease.ConfigureAwait(false))
        {
            var options = new DbContextOptionsBuilder<PosDbContext>()
                .UseSqlite(lease.Connection, contextOwnsConnection: false)
                .Options;

            using var context = new PosDbContext(options);
            await context.Database.GetService<IMigrator>().MigrateAsync(targetMigration);
        }
    }

    internal async Task ExecuteAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs <paramref name="sql"/> and returns the <see cref="SqliteException"/> it raised.</summary>
    internal async Task<SqliteException> ExecuteExpectingAbortAsync(string sql)
    {
        try
        {
            await ExecuteAsync(sql);
        }
        catch (SqliteException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            "Expected SQLite to abort the statement, but it succeeded: " + sql);
    }

    internal async Task<string?> ScalarAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    internal async Task<long> CountAsync(string sql) =>
        Convert.ToInt64(await ScalarAsync(sql), CultureInfo.InvariantCulture);

    internal async Task<IReadOnlyList<string>> ColumnAsync(string sql)
    {
        var values = new List<string>();

        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return values;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _factory.DisposeAsync();
        _fixture.Dispose();
    }
}
