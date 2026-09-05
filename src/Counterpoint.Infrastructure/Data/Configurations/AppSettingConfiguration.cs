using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>app_setting</c> (docs/01_DATA_MODEL.md §8). Keyed on <c>key</c>: one row per setting,
/// the key supplied by the caller rather than generated.
/// </summary>
internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(setting => setting.Key);
        entity.Property(setting => setting.Key).HasColumnType("TEXT").IsRequired().ValueGeneratedNever();

        entity.Property(setting => setting.Value).IsRequired();
        entity.Property(setting => setting.ValueType).IsRequired();
        entity.Property(setting => setting.UpdatedAt).IsRequired();

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(setting => setting.UpdatedBy)
            .OnDelete(DeleteBehavior.NoAction);

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_app_setting_value_type",
            "value_type IN ('STRING','INT','MONEY','BOOL','JSON')"));
    }
}
