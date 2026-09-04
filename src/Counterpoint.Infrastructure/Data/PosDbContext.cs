using System;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// EF Core context for the till's database. EF owns writes and migrations; hot reads and
/// reports go through Dapper (CLAUDE.md "Stack").
/// </summary>
/// <remarks>
/// The model is deliberately almost empty at this point. P0-T04 brings the skeleton schema and
/// migration 0001; the rest arrives in P1-T01.
/// </remarks>
public sealed class PosDbContext : DbContext
{
    public PosDbContext(DbContextOptions<PosDbContext> options)
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
            entity.Property(schemaVersion => schemaVersion.AppliedAt).HasColumnType("TEXT").IsRequired();
        });

        // Applied last so it also catches names EF inferred rather than ones we set by hand.
        SnakeCaseNaming.Apply(modelBuilder.Model);

        base.OnModelCreating(modelBuilder);
    }
}
