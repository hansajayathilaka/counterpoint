using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>Maps <c>app_user</c> (docs/01_DATA_MODEL.md §8).</summary>
internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(user => user.Id);

        entity.Property(user => user.Username).IsRequired();
        entity.Property(user => user.DisplayName).IsRequired();
        entity.Property(user => user.PasswordHash).IsRequired();
        entity.Property(user => user.Role).IsRequired();
        entity.Property(user => user.Active).HasDefaultValue(true).ValueGeneratedNever();
        entity.Property(user => user.FailedAttempts).HasDefaultValue(0).ValueGeneratedNever();
        entity.Property(user => user.CreatedAt).IsRequired();

        entity.HasIndex(user => user.Username).IsUnique().HasDatabaseName("ux_app_user_username");

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_app_user_role",
            "role IN ('CASHIER','OWNER')"));
    }
}
