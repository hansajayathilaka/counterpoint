using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>bulk_break</c> (docs/01_DATA_MODEL.md §4, task P2-T09). Not append-only — see the
/// schema class remarks.
/// </summary>
internal sealed class BulkBreakConfiguration : IEntityTypeConfiguration<BulkBreak>
{
    public void Configure(EntityTypeBuilder<BulkBreak> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(bulkBreak => bulkBreak.Id);

        entity.Property(bulkBreak => bulkBreak.SourceQtyBase).IsRequired();
        entity.Property(bulkBreak => bulkBreak.ExpectedQtyBase).IsRequired();
        entity.Property(bulkBreak => bulkBreak.ActualQtyBase).IsRequired();
        entity.Property(bulkBreak => bulkBreak.WastageQtyBase).IsRequired().HasDefaultValue(0L);
        entity.Property(bulkBreak => bulkBreak.TotalValue).IsRequired();
        entity.Property(bulkBreak => bulkBreak.Reason).IsRequired();
        entity.Property(bulkBreak => bulkBreak.OccurredAt).IsRequired();

        // No index beyond the primary key - the same choice as goods_receipt, purchase_order and
        // stock_take (docs/01_DATA_MODEL.md §12): a shop posts a handful of bulk breaks a day, so
        // any screen or report reading this table in full is scanning a table small enough not to
        // need one, and stock_movement(ref_doc_type, ref_doc_id) already carries the index the
        // value-conservation report actually runs against.
        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(bulkBreak => bulkBreak.SourceVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(bulkBreak => bulkBreak.DestinationVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(bulkBreak => bulkBreak.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table =>
        {
            // FR-4.9's own distinction from a UOM conversion: this moves stock between two
            // *different* SKUs, never the same one.
            table.HasCheckConstraint(
                "ck_bulk_break_distinct_variants",
                "source_variant_id <> destination_variant_id");

            table.HasCheckConstraint("ck_bulk_break_source_qty_base", "source_qty_base > 0");
            table.HasCheckConstraint("ck_bulk_break_expected_qty_base", "expected_qty_base > 0");
            table.HasCheckConstraint("ck_bulk_break_actual_qty_base", "actual_qty_base > 0");
            table.HasCheckConstraint("ck_bulk_break_wastage_qty_base", "wastage_qty_base >= 0");
        });
    }
}
