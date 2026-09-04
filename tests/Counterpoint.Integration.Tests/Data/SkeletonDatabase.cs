using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// A real, encrypted database with migration <c>Skeleton0001</c> applied, and a connection on
/// which to poke it with raw SQL.
/// </summary>
/// <remarks>
/// The connection is a normal keyed connection from the factory, not the write lease: the runner
/// takes and releases that lease itself, and a test that held it would deadlock the next run.
/// </remarks>
internal sealed class SkeletonDatabase : IAsyncDisposable
{
    private readonly TemporaryDataDirectory _fixture;
    private readonly PosConnectionFactory _factory;
    private readonly DbConnection _connection;

    private SkeletonDatabase(
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

    internal static async Task<SkeletonDatabase> CreateAsync(bool seed = true)
    {
        var fixture = new TemporaryDataDirectory();
        var factory = fixture.CreateConnectionFactory();

        try
        {
            var runner = new MigrationRunner(factory, fixture.DataDirectory);
            var result = await runner.ApplyPendingMigrationsAsync();

            var connection = factory.OpenConfiguredConnection();
            var database = new SkeletonDatabase(fixture, factory, connection, result);

            if (seed)
            {
                await SkeletonSeed.ApplyAsync(connection);
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
