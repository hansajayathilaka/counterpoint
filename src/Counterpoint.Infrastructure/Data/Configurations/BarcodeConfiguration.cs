using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>barcode</c> (docs/01_DATA_MODEL.md §3). The unique index on the symbol is the single
/// hottest lookup in the system (NFR-P1, §12) - it is what a scan resolves against.
/// </summary>
/// <remarks>
/// The property is <c>Value</c> because C# forbids a member named after its enclosing type, so
/// the column name is spelled out. The snake_case convention leaves an explicit
/// <c>HasColumnName</c> alone.
/// </remarks>
internal sealed class BarcodeConfiguration : IEntityTypeConfiguration<Barcode>
{
    public void Configure(EntityTypeBuilder<Barcode> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(barcode => barcode.Id);

        entity.Property(barcode => barcode.Value).HasColumnName("barcode").IsRequired();
        entity.Property(barcode => barcode.IsPrimary).HasDefaultValue(false).ValueGeneratedNever();

        entity.HasIndex(barcode => barcode.Value).IsUnique().HasDatabaseName("ux_barcode");
        entity.HasIndex(barcode => barcode.ProductVariantId).HasDatabaseName("ix_barcode_variant");

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(barcode => barcode.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
