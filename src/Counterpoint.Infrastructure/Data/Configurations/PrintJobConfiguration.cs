using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>Maps <c>print_job</c> (docs/01_DATA_MODEL.md §8).</summary>
internal sealed class PrintJobConfiguration : IEntityTypeConfiguration<PrintJob>
{
    public void Configure(EntityTypeBuilder<PrintJob> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(job => job.Id);

        entity.Property(job => job.DocType).IsRequired();
        entity.Property(job => job.Target).IsRequired().HasDefaultValue("RECEIPT").ValueGeneratedNever();
        entity.Property(job => job.Payload).IsRequired().HasColumnType("BLOB");
        entity.Property(job => job.Copies).HasDefaultValue(1).ValueGeneratedNever();
        entity.Property(job => job.IsDuplicate).HasDefaultValue(false).ValueGeneratedNever();
        entity.Property(job => job.Status).IsRequired();
        entity.Property(job => job.Attempts).HasDefaultValue(0).ValueGeneratedNever();
        entity.Property(job => job.CreatedAt).IsRequired();

        // Partial: the outbox worker polls PENDING only, so the index stays O(pending) rather
        // than growing with every receipt ever printed.
        entity.HasIndex(job => job.Status)
            .HasDatabaseName("ix_print_pending")
            .HasFilter("status = 'PENDING'");

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_print_job_doc_type",
                "doc_type IN ('SALE','RETURN','CREDIT_NOTE','X_REPORT','Z_REPORT'," +
                "'GRN','PO','STOCK_TAKE','LABEL','CASH_SLIP')");

            table.HasCheckConstraint(
                "ck_print_job_status",
                "status IN ('PENDING','PRINTED','FAILED')");
        });
    }
}
