using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>cash_movement</c> (docs/01_DATA_MODEL.md §7). APPEND ONLY: money in and out of the
/// drawer is evidence, and the <c>trg_cash_movement_*</c> triggers are what keep it so.
/// </summary>
internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(movement => movement.Id);

        entity.Property(movement => movement.Direction).IsRequired();
        entity.Property(movement => movement.Amount).IsRequired();
        entity.Property(movement => movement.Reason).IsRequired();
        entity.Property(movement => movement.OccurredAt).IsRequired();

        entity.HasOne<Shift>()
            .WithMany()
            .HasForeignKey(movement => movement.ShiftId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(movement => movement.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_cash_movement_direction", "direction IN ('IN','OUT')");

            // The sign lives in `direction`, never in the amount: a negative "IN" would net out
            // of a Z report silently.
            table.HasCheckConstraint("ck_cash_movement_amount", "amount > 0");
        });
    }
}
