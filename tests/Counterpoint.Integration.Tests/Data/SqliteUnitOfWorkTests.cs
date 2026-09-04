using System;
using System.Data.Common;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// One business operation, one local ACID transaction. Smoke cover only - the full suite is a
/// separate pass.
/// </summary>
public sealed class SqliteUnitOfWorkTests
{
    [Fact]
    public async Task CommitsOnSuccess()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (note TEXT NOT NULL);", token);
            await ExecuteAsync(connection, transaction, "INSERT INTO smoke (note) VALUES ('kept');", token);
            return 0;
        });

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(1);
    }

    [Fact]
    public async Task RollsBackOnFailureAndReleasesTheWriteGate()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE smoke (note TEXT NOT NULL);", token);
            return 0;
        });

        var failing = async () => await unitOfWork.ExecuteInTransactionAsync<int>(
            async (connection, transaction, token) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO smoke (note) VALUES ('lost');", token);
                throw new InvalidOperationException("the shop said no");
            });

        await failing.Should().ThrowAsync<InvalidOperationException>();

        (await CountAsync(factory, "SELECT count(*) FROM smoke;")).Should().Be(0);

        // The gate must be free again, or the next bill would hang forever.
        await using var lease = await factory.AcquireWriteConnectionAsync();
        lease.Connection.Should().NotBeNull();
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> CountAsync(PosConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenReadConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
