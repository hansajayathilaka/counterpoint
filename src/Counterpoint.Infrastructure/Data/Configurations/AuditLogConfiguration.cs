using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>Maps <c>audit_log</c> (docs/01_DATA_MODEL.md §8). APPEND ONLY, hash chained.</summary>
internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(log => log.Id);

        entity.Property(log => log.OccurredAt).IsRequired();
        entity.Property(log => log.Action).IsRequired();
        entity.Property(log => log.EntityType).IsRequired();
        entity.Property(log => log.PrevHash).IsRequired();
        entity.Property(log => log.RowHash).IsRequired();

        entity.HasIndex(log => log.OccurredAt).HasDatabaseName("ix_audit_time");
        entity.HasIndex(log => new { log.EntityType, log.EntityId }).HasDatabaseName("ix_audit_entity");

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
