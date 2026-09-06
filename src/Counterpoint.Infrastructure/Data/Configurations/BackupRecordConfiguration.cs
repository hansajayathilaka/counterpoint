using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>backup_record</c> (docs/01_DATA_MODEL.md §8).</summary>
internal sealed class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(record => record.Id);

        entity.Property(record => record.Filename).IsRequired();
        entity.Property(record => record.TakenAt).IsRequired();
        entity.Property(record => record.SizeBytes).IsRequired();
        entity.Property(record => record.Checksum).IsRequired();
        entity.Property(record => record.SchemaVer).IsRequired();
        entity.Property(record => record.UsbStatus).IsRequired();
        entity.Property(record => record.CloudStatus).IsRequired();
        entity.Property(record => record.Attempts).HasDefaultValue(0).ValueGeneratedNever();

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_backup_record_usb_status", "usb_status IN ('NA','OK','FAILED')");

            table.HasCheckConstraint(
                "ck_backup_record_cloud_status",
                "cloud_status IN ('PENDING','OK','FAILED','SKIPPED')");
        });
    }
}
