using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>supplier</c> (docs/01_DATA_MODEL.md §4).</summary>
internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(supplier => supplier.Id);

        entity.Property(supplier => supplier.Name).IsRequired();
        entity.Property(supplier => supplier.Active).HasDefaultValue(true).ValueGeneratedNever();
    }
}
