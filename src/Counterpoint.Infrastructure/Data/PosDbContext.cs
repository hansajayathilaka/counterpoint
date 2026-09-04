using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// EF Core context for the till's database. EF owns writes and migrations; hot reads and
/// reports go through Dapper (CLAUDE.md "Stack").
/// </summary>
/// <remarks>
/// <para>
/// The model covers the fifteen skeleton tables of migration <c>Skeleton0001</c> (P0-T04); the
/// rest of docs/01_DATA_MODEL.md arrives in P1-T01. The mapping lives one class per table in
/// <c>Data/Configurations</c>, over the persistence rows in <c>Data/Schema</c>.
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

    /// <summary>
    /// The only <see cref="DbSet{TEntity}"/> in the context. <see cref="MigrationRunner"/> writes
    /// through it so the ISO-8601 timestamp conversion applies. The other fourteen tables are
    /// persistence rows registered by configuration only - they are internal, and P1-T01 replaces
    /// them with real domain types.
    /// </summary>
    public DbSet<SchemaVersion> SchemaVersions => Set<SchemaVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PosDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Applied to every construction path - the unit of work, the migration runner and the
    /// design-time factory - so none of them can forget it.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // docs/01_DATA_MODEL.md: every table is a bare `id INTEGER PRIMARY KEY`. See
        // NoAutoincrementAnnotationProvider for why this is a service replacement and not an
        // edit to the generated migration.
        optionsBuilder.ReplaceService<IRelationalAnnotationProvider, NoAutoincrementAnnotationProvider>();

        base.OnConfiguring(optionsBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // snake_case runs as a model-finalizing convention, after EF has resolved table sharing
        // and inferred the names we did not spell out. Calling it at the end of OnModelCreating
        // is too early - see SnakeCaseNamingConvention.
        configurationBuilder.Conventions.Add(_ => new SnakeCaseNamingConvention());

        // Off on purpose. EF creates an index behind every foreign key, which would add a dozen
        // indexes docs/01_DATA_MODEL.md does not have - paid for on every insert on the sale
        // path. From here on every index in this schema is one somebody chose: see §12.
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();

        // Every timestamp in the schema is ISO-8601 TEXT with an offset (DM-06), not EF's
        // default space-separated SQLite form. Set once here so no column can be missed.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<Iso8601TimestampConverter>()
            .HaveColumnType("TEXT");

        base.ConfigureConventions(configurationBuilder);
    }
}
