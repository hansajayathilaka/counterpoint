using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>brand</c> (docs/01_DATA_MODEL.md §3).</summary>
internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(brand => brand.Id);

        entity.Property(brand => brand.Name).IsRequired();
        entity.Property(brand => brand.Active).HasDefaultValue(true).ValueGeneratedNever();

        entity.HasIndex(brand => brand.Name).IsUnique().HasDatabaseName("ux_brand_name");
    }
}
