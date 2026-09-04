using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>Maps <c>uom</c> (docs/01_DATA_MODEL.md §3).</summary>
internal sealed class UomConfiguration : IEntityTypeConfiguration<Uom>
{
    public void Configure(EntityTypeBuilder<Uom> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(uom => uom.Id);

        entity.Property(uom => uom.Name).IsRequired();
        entity.Property(uom => uom.Symbol).IsRequired();
        entity.Property(uom => uom.DecimalPlaces).HasDefaultValue(0).ValueGeneratedNever();

        entity.HasIndex(uom => uom.Name).IsUnique().HasDatabaseName("ux_uom_name");

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_uom_decimal_places",
            "decimal_places BETWEEN 0 AND 4"));
    }
}
