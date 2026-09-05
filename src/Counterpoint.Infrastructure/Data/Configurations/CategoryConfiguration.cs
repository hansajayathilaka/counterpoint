using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;


/// <summary>
/// Maps <c>category</c> (docs/01_DATA_MODEL.md §3). The self-reference is what makes a tree
/// possible; the <c>trg_category_two_levels_*</c> triggers are what keep it two deep (FR-2.20).
/// </summary>
/// <remarks>
/// <c>UNIQUE (name, parent_id)</c> is a named index rather than an inline constraint, so the
/// model names it exactly as the database does (§13). SQLite treats NULLs as distinct in a unique
/// index, so two top-level categories may not share a name only because a NULL parent compares
/// unequal to another NULL - that is the DDL's behaviour and it is reproduced faithfully here.
/// </remarks>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(category => category.Id);

        entity.Property(category => category.Name).IsRequired();
        entity.Property(category => category.Active).HasDefaultValue(true).ValueGeneratedNever();

        entity.HasIndex(category => new { category.Name, category.ParentId })
            .IsUnique()
            .HasDatabaseName("ux_category_name_parent");

        entity.HasOne<Category>()
            .WithMany()
            .HasForeignKey(category => category.ParentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
