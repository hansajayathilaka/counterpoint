using System;
using Counterpoint.Domain.ValueObjects;
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
/// The model covers every table in areas A to F of docs/01_DATA_MODEL.md, as created by
/// <c>Skeleton0001</c> and <c>FullSchema0002</c>. The mapping lives one class per table in
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
    /// through it so the ISO-8601 timestamp conversion applies. Every other table is a persistence
    /// row registered by configuration only - they are internal, and the real domain types arrive
    /// from P1-T05 onward.
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

        // NFR-P6, not an optimisation. Building this model from OnModelCreating takes the best
        // part of a second on the shop PC and it happens before the sales screen can draw; the
        // compiled model is built at compile time instead, so start-up only reads it. Regenerate
        // it in the same change as any model change:
        //
        //   EfTooling=true dotnet ef dbcontext optimize \
        //     --project src/Counterpoint.Infrastructure \
        //     --startup-project src/Counterpoint.Infrastructure \
        //     --output-dir Data/CompiledModels \
        //     --namespace Counterpoint.Infrastructure.Data.CompiledModels
        //
        // A stale compiled model would be used in preference to the real one and nothing would
        // say so, which is why FullSchemaTests.NFR_P6_TheCompiledModelMatchesTheDatabaseTheMigrationsBuilt
        // compares it against the database the migrations built, on every test run.
        optionsBuilder.UseModel(CompiledModels.PosDbContextModel.Instance);

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

        // Money and the two rate types are INTEGER scaled x10 000 (docs/01_DATA_MODEL.md §1,
        // CLAUDE.md invariant 1). Registered here rather than per property for the same reason as
        // the timestamps: a column that got missed would map its decimal to TEXT, silently, and
        // money stored as text does not add up. Nullable columns are covered too.
        //
        // Quantity is deliberately absent. It carries the uom.id it was measured in, and an EF
        // value converter is a scalar function with no access to the sibling uom_id column, so
        // reading one back would have to invent a unit. Quantity columns stay `long` until a
        // mapping exists that can supply the unit honestly - see docs/01_DATA_MODEL.md §13.
        configurationBuilder.Properties<Money>()
            .HaveConversion<ScaledMoneyConverter>()
            .HaveColumnType("INTEGER");

        configurationBuilder.Properties<TaxRate>()
            .HaveConversion<ScaledTaxRateConverter>()
            .HaveColumnType("INTEGER");

        configurationBuilder.Properties<Percentage>()
            .HaveConversion<ScaledPercentageConverter>()
            .HaveColumnType("INTEGER");

        base.ConfigureConventions(configurationBuilder);
    }
}
