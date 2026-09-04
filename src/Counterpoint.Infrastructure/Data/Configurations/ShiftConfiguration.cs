using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>shift</c> (docs/01_DATA_MODEL.md §7). APPEND ONLY apart from the close fields.
/// </summary>
internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(shift => shift.Id);

        entity.Property(shift => shift.ShiftNo).IsRequired();
        entity.Property(shift => shift.OpenedAt).IsRequired();

        // TEXT YYYY-MM-DD, deliberately not a timestamp. See SaleConfiguration.
        entity.Property(shift => shift.BusinessDate).IsRequired().HasColumnType("TEXT");

        entity.Property(shift => shift.OpeningFloat).IsRequired();
        entity.Property(shift => shift.Status).IsRequired();

        entity.HasIndex(shift => shift.ShiftNo).IsUnique().HasDatabaseName("ux_shift_no");

        // C-01: at most one open shift, enforced by the database rather than by application code,
        // because application code cannot make it true across a crash.
        entity.HasIndex(shift => shift.Status)
            .IsUnique()
            .HasDatabaseName("ux_one_open_shift")
            .HasFilter("status = 'OPEN'");

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(shift => shift.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(shift => shift.ClosedBy)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_shift_status",
            "status IN ('OPEN','CLOSED')"));
    }
}
