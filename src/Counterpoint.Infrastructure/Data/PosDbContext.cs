using System;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// EF Core context for the till's database. EF owns writes and migrations; hot reads and
/// reports go through Dapper (CLAUDE.md "Stack").
/// </summary>
/// <remarks>
/// <para>
/// The model is deliberately almost empty at this point. P0-T04 brings the skeleton schema and
/// migration 0001; the rest arrives in P1-T01.
/// </para>
/// <para>
/// The constructor is internal on purpose: a context is only ever built by
/// <see cref="IPosDbContextFactory"/>, against the write connection and the transaction the
/// unit of work already holds. Anything else would be a second writer on the same file.
/// </para>
/// </remarks>
public sealed class PosDbContext : DbContext
{
    internal PosDbContext(DbContextOptions<PosDbContext> options)
        : base(options)
    {
    }

    public DbSet<SchemaVersion> SchemaVersions => Set<SchemaVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SchemaVersion>(entity =>
        {
            entity.ToTable("schema_version");
            entity.HasKey(schemaVersion => schemaVersion.Version);
            entity.Property(schemaVersion => schemaVersion.Version).HasColumnType("TEXT").IsRequired();
            entity.Property(schemaVersion => schemaVersion.AppliedAt).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // snake_case runs as a model-finalizing convention, after EF has resolved table sharing
        // and inferred the names we did not spell out. Calling it at the end of OnModelCreating
        // is too early - see SnakeCaseNamingConvention.
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());

        // Every timestamp in the schema is ISO-8601 TEXT with an offset (DM-06), not EF's
        // default space-separated SQLite form. Set once here so no column can be missed.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<Iso8601TimestampConverter>()
            .HaveColumnType("TEXT");

        base.ConfigureConventions(configurationBuilder);
    }
}
