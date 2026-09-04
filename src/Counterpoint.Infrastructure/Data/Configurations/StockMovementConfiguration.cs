using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>Maps <c>stock_movement</c> (docs/01_DATA_MODEL.md §4). APPEND ONLY.</summary>
internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(movement => movement.Id);

        entity.Property(movement => movement.MovementType).IsRequired();
        entity.Property(movement => movement.QtyBase).IsRequired();
        entity.Property(movement => movement.UnitCost).IsRequired();
        entity.Property(movement => movement.RefDocType).IsRequired();
        entity.Property(movement => movement.BalanceAfter).IsRequired();
        entity.Property(movement => movement.OccurredAt).IsRequired();

        entity.HasIndex(movement => new { movement.ProductVariantId, movement.OccurredAt })
            .HasDatabaseName("ix_movement_variant_time");
        entity.HasIndex(movement => new { movement.RefDocType, movement.RefDocId })
            .HasDatabaseName("ix_movement_ref");
        entity.HasIndex(movement => movement.OccurredAt).HasDatabaseName("ix_movement_time");

        entity.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(movement => movement.ProductVariantId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(movement => movement.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_stock_movement_movement_type",
            "movement_type IN ('GRN','SALE','RETURN_IN','ADJUSTMENT','DAMAGE'," +
            "'STOCK_TAKE','BULK_BREAK_OUT','BULK_BREAK_IN','OPENING','TRANSFER_OUT','TRANSFER_IN')"));
    }
}
