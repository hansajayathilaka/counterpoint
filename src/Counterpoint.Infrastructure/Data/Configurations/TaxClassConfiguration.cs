using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>Maps <c>tax_class</c> (docs/01_DATA_MODEL.md §3).</summary>
internal sealed class TaxClassConfiguration : IEntityTypeConfiguration<TaxClass>
{
    public void Configure(EntityTypeBuilder<TaxClass> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(taxClass => taxClass.Id);

        entity.Property(taxClass => taxClass.Name).IsRequired();
        entity.Property(taxClass => taxClass.Rate).IsRequired();
        entity.Property(taxClass => taxClass.Active).HasDefaultValue(true).ValueGeneratedNever();

        entity.HasIndex(taxClass => taxClass.Name).IsUnique().HasDatabaseName("ux_tax_class_name");
    }
}
