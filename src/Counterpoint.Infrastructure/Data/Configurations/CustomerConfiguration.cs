using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>Maps <c>customer</c> (docs/01_DATA_MODEL.md §5).</summary>
/// <remarks>
/// <c>sale.customer_id</c> stays a plain nullable column for now: that foreign key belongs to
/// P5-T02 with credit accounts, and adding it would rebuild <c>sale</c> - which drops its
/// append-only triggers (§13). The table exists here so the rest of the model can point at it.
/// </remarks>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(customer => customer.Id);

        entity.Property(customer => customer.Name).IsRequired();
        entity.Property(customer => customer.Type).IsRequired().HasDefaultValue("RETAIL").ValueGeneratedNever();
        entity.Property(customer => customer.CreditLimit).HasDefaultValue(Money.Zero).ValueGeneratedNever();
        entity.Property(customer => customer.Balance).HasDefaultValue(Money.Zero).ValueGeneratedNever();
        entity.Property(customer => customer.Active).HasDefaultValue(true).ValueGeneratedNever();
        entity.Property(customer => customer.CreatedAt).IsRequired();

        entity.HasIndex(customer => customer.Phone).HasDatabaseName("ix_customer_phone");

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_customer_type",
            "type IN ('RETAIL','TRADE')"));
    }
}
