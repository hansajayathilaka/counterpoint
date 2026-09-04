using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Counterpoint.Infrastructure.Data.Configurations;

/// <summary>
/// Maps <c>number_sequence</c> (docs/01_DATA_MODEL.md §8). One of three tables keyed on something
/// other than <c>id</c>: the document type is the key, one row per type.
/// </summary>
internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.HasKey(sequence => sequence.DocType);
        entity.Property(sequence => sequence.DocType).HasColumnType("TEXT").IsRequired();

        entity.Property(sequence => sequence.Prefix).IsRequired();
        entity.Property(sequence => sequence.Pattern).IsRequired();
        entity.Property(sequence => sequence.NextVal).IsRequired();

        entity.ToTable(table => table.HasCheckConstraint(
            "ck_number_sequence_doc_type",
            "doc_type IN ('SALE','RETURN','CREDIT_NOTE','GRN','PO','SHIFT','STOCK_TAKE','QUOTE')"));
    }
}
