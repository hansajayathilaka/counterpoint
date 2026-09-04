using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>schema_version</c> (docs/01_DATA_MODEL.md §8): one row per applied migration, the
/// documented authority on the database's version. The third and last table keyed on something
/// other than <c>id</c>.
/// </summary>
internal sealed class SchemaVersionConfiguration : IEntityTypeConfiguration<SchemaVersion>
{
    public void Configure(EntityTypeBuilder<SchemaVersion> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // Named explicitly. This is the one mapped type with a DbSet, and EF takes the table name
        // from the DbSet - which would give the plural "schema_versions".
        entity.ToTable("schema_version");

        entity.HasKey(schemaVersion => schemaVersion.Version);
        entity.Property(schemaVersion => schemaVersion.Version).HasColumnType("TEXT").IsRequired();
        entity.Property(schemaVersion => schemaVersion.AppliedAt).IsRequired();
    }
}
