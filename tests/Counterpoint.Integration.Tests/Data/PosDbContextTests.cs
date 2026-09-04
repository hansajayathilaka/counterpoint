using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// snake_case naming, ISO-8601 timestamps, and the rule that a context only ever exists on the
/// write connection's open transaction (engineering guide §4.8, §5; DM-06).
/// </summary>
public sealed class PosDbContextTests
{
    /// <summary>The specimen in docs/01_DATA_MODEL.md §1, offset and all.</summary>
    private static readonly DateTimeOffset SpecimenTimestamp =
        new(2026, 9, 3, 14, 22, 31, 123, TimeSpan.FromHours(5.5));

    private const string SpecimenTimestampText = "2026-09-03T14:22:31.123+05:30";

    [Fact]
    public async Task ModelUsesSnakeCaseTablesAndColumns()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            using var context = unitOfWork.CreateDbContext();

            var entityType = context.Model.FindEntityType(typeof(SchemaVersion));
            entityType.Should().NotBeNull();
            entityType!.GetTableName().Should().Be("schema_version");

            entityType!.GetProperties().Select(property => property.GetColumnName())
                .Should().BeEquivalentTo(["version", "applied_at"]);

            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("SaleLine", "sale_line")]
    [InlineData("UomId", "uom_id")]
    [InlineData("QtyBase", "qty_base")]
    [InlineData("schema_version", "schema_version")]
    [InlineData("GRNLine", "grn_line")]
    public void ToSnakeCaseFollowsTheDatabaseNamingRule(string input, string expected) =>
        SnakeCaseNamingConvention.ToSnakeCase(input).Should().Be(expected);

    /// <summary>
    /// The seam is the enforcement. A public constructor would let the composition root, the UI
    /// host or a later task hand EF its own connection, and that connection would write outside
    /// the write gate and outside the open transaction.
    /// </summary>
    [Fact]
    public void PosDbContextCannotBeConstructedFromOutsideInfrastructure() =>
        typeof(PosDbContext)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should().BeEmpty(
                "a context may only be created through IPosDbContextFactory, on the write " +
                "connection's open transaction");

    [Fact]
    public void CreatingAContextOutsideAUnitOfWorkIsRefused()
    {
        using var fixture = new TemporaryDataDirectory();
        using var factory = fixture.CreateConnectionFactory();
        IPosDbContextFactory contextFactory = new SqliteUnitOfWork(factory);

        var create = contextFactory.CreateDbContext;

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("*ExecuteInTransactionAsync*");
    }

    /// <summary>
    /// Inside a unit of work the context must be on that unit of work's connection and enlisted
    /// in its transaction - not a second connection of EF's own.
    /// </summary>
    [Fact]
    public async Task AContextInsideAUnitOfWorkSharesItsConnectionAndTransaction()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await unitOfWork.ExecuteInTransactionAsync((connection, transaction, _) =>
        {
            using var context = unitOfWork.CreateDbContext();

            context.Database.GetDbConnection().Should().BeSameAs(connection);
            context.Database.CurrentTransaction.Should().NotBeNull();
            context.Database.CurrentTransaction!.GetDbTransaction().Should().BeSameAs(transaction);

            return Task.FromResult(0);
        });
    }

    /// <summary>
    /// A context's writes belong to the surrounding business transaction. If EF committed on its
    /// own, a bill that failed after its stock movement would leave the stock moved.
    /// </summary>
    [Fact]
    public async Task ContextWritesRollBackWithTheUnitOfWork()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await CreateSchemaVersionTableAsync(unitOfWork);

        var failing = async () => await unitOfWork.ExecuteInTransactionAsync<int>(async (_, _, token) =>
        {
            using var context = unitOfWork.CreateDbContext();
            context.SchemaVersions.Add(new SchemaVersion { Version = "0001_Lost", AppliedAt = SpecimenTimestamp });
            await context.SaveChangesAsync(token);

            throw new InvalidOperationException("the shop said no");
        });

        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("the shop said no");

        (await ScalarAsync(factory, "SELECT count(*) FROM schema_version;")).Should().Be("0");
    }

    /// <summary>
    /// DM-06: <c>2026-09-03T14:22:31.123+05:30</c>. Asserted against the raw TEXT in the file,
    /// because EF would round-trip its own space-separated format perfectly happily and the
    /// column would still not sort or compare the way every report assumes.
    /// </summary>
    [Fact]
    public async Task TimestampsAreStoredAsIso8601TextWithAnOffset()
    {
        using var fixture = new TemporaryDataDirectory();
        await using var factory = fixture.CreateConnectionFactory();
        var unitOfWork = new SqliteUnitOfWork(factory);

        await CreateSchemaVersionTableAsync(unitOfWork);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();
            context.SchemaVersions.Add(new SchemaVersion
            {
                Version = "0001_Skeleton",
                AppliedAt = SpecimenTimestamp,
            });

            await context.SaveChangesAsync(token);
        });

        (await ScalarAsync(factory, "SELECT applied_at FROM schema_version;"))
            .Should().Be(SpecimenTimestampText, "docs/01_DATA_MODEL.md §1 fixes this exact form");

        // And back again, offset intact rather than shifted into local time.
        var read = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();
            return await context.SchemaVersions.SingleAsync(token);
        });

        read.AppliedAt.Should().Be(SpecimenTimestamp);
        read.AppliedAt.Offset.Should().Be(SpecimenTimestamp.Offset);
    }

    /// <summary>
    /// Fixed width, always. <c>FFF</c> would trim a trailing zero and make
    /// <c>...31.120+05:30</c> shorter than <c>...31.123+05:30</c>, so a TEXT sort would put
    /// them in the wrong order.
    /// </summary>
    [Fact]
    public void EveryTimestampIsTheSameWidth()
    {
        var converter = Iso8601TimestampConverter.Instance;

        var trailingZero = (string)converter.ConvertToProvider(
            new DateTimeOffset(2026, 9, 3, 14, 22, 31, 120, TimeSpan.FromHours(5.5)))!;
        var noFraction = (string)converter.ConvertToProvider(
            new DateTimeOffset(2026, 9, 3, 14, 22, 31, 0, TimeSpan.FromHours(5.5)))!;

        trailingZero.Should().Be("2026-09-03T14:22:31.120+05:30");
        noFraction.Should().Be("2026-09-03T14:22:31.000+05:30");
        trailingZero.Length.Should().Be(SpecimenTimestampText.Length);

        string.CompareOrdinal(noFraction, trailingZero).Should().BeNegative(
            "a TEXT timestamp column is ordered as text, so earlier must sort earlier");
    }

    /// <summary>
    /// P0-T04 owns migration 0001. Until it lands, the one table this context maps is created by
    /// hand so the mapping itself can be exercised end to end against a real encrypted file.
    /// </summary>
    private static Task<int> CreateSchemaVersionTableAsync(SqliteUnitOfWork unitOfWork) =>
        unitOfWork.ExecuteInTransactionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "CREATE TABLE schema_version (" +
                "  version    TEXT NOT NULL PRIMARY KEY," +
                "  applied_at TEXT NOT NULL);";
            return await command.ExecuteNonQueryAsync(token);
        });

    private static async Task<string?> ScalarAsync(PosConnectionFactory factory, string sql)
    {
        await using DbConnection connection = await factory.OpenReadConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync(CancellationToken.None))?.ToString();
    }
}
