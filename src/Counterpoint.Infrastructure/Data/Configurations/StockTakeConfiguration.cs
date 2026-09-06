using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>stock_take</c> (docs/01_DATA_MODEL.md §4).</summary>
internal sealed class StockTakeConfiguration : IEntityTypeConfiguration<StockTake>
{
    public void Configure(EntityTypeBuilder<StockTake> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(take => take.Id);

        entity.Property(take => take.Scope).IsRequired();
        entity.Property(take => take.StartedAt).IsRequired();
        entity.Property(take => take.Status).IsRequired();

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(take => take.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_stock_take_status",
            "status IN ('OPEN','POSTED','ABANDONED')"));
    }
}
