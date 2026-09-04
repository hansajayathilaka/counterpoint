using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// Smoke cover for the encrypted database bootstrap (P0-T03, NFR-R2, NFR-S3).
/// </summary>
public sealed class PosConnectionFactoryTests
{
    [Fact]
    public async Task DatabaseIsCreatedClosedReopenedAndReadWithTheStoredKey()
    {
        using var fixture = new TemporaryDataDirectory();

        await using (var factory = fixture.CreateConnectionFactory())
        {
            await using var lease = await factory.AcquireWriteConnectionAsync();
            await ExecuteAsync(lease.Connection, "CREATE TABLE smoke (note TEXT NOT NULL);");
            await ExecuteAsync(lease.Connection, "INSERT INTO smoke (note) VALUES ('one till, one database');");
        }

        // A brand new factory: new process in all but name, same key store, same file.
        await using var reopened = fixture.CreateConnectionFactory();
        await using var connection = await reopened.OpenReadConnectionAsync();

        var note = await ScalarAsync(connection, "SELECT note FROM smoke;");

        note.Should().Be("one till, one database");
    }

    [Fact]
    public async Task NFR_S3_OpeningWithTheWrongKeyFails()
    {
        using var fixture = new TemporaryDataDirectory();

        await using (var factory = fixture.CreateConnectionFactory())
        {
            await using var lease = await factory.AcquireWriteConnectionAsync();
            await ExecuteAsync(lease.Connection, "CREATE TABLE smoke (note TEXT NOT NULL);");
        }

        // A plain SQLite tool identifies a database by the 16-byte "SQLite format 3\0" header.
        // SQLCipher encrypts page 1 including that header, so the tool sees only noise.
        var header = new byte[16];
        using (var file = File.OpenRead(fixture.DataDirectory.DatabaseFilePath))
        {
            file.ReadExactly(header);
        }

        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3");

        var wrongKey = new byte[DatabaseKey.SizeInBytes];
        Array.Fill(wrongKey, (byte)0x2A);

        await using var impostor = fixture.CreateConnectionFactory(new FixedKeyStore(wrongKey));

        var open = async () => await impostor.OpenReadConnectionAsync();

        await open.Should().ThrowAsync<SqliteException>(
            "an encrypted database must be unreadable without its key");
    }

    [Fact]
    public async Task NFR_R2_EveryConnectionCarriesTheRequiredPragmas()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        await using var connection = await factory.OpenReadConnectionAsync();

        (await ScalarAsync(connection, "PRAGMA journal_mode;")).Should().Be("wal");

        // 2 == FULL. NFR-R2 durability; NORMAL would lose the last committed bill on power loss.
        (await ScalarAsync(connection, "PRAGMA synchronous;")).Should().Be("2");

        (await ScalarAsync(connection, "PRAGMA foreign_keys;")).Should().Be("1");
        (await ScalarAsync(connection, "PRAGMA busy_timeout;")).Should().Be("5000");
    }

    [Fact]
    public async Task WriteConnectionIsSerialisedBehindTheGate()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();

        var lease = await factory.AcquireWriteConnectionAsync();

        var second = factory.AcquireWriteConnectionAsync().AsTask();
        second.IsCompleted.Should().BeFalse("only one writer may hold the connection at a time");

        await lease.DisposeAsync();

        var granted = await second;
        granted.Connection.Should().BeSameAs(lease.Connection, "there is exactly one write connection");
        await granted.DisposeAsync();
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
