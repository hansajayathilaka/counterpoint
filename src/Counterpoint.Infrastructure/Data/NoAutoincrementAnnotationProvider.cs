using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Sqlite.Metadata.Internal;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Strips SQLite's <c>AUTOINCREMENT</c> from every generated column definition.
/// </summary>
/// <remarks>
/// <para>
/// CLAUDE.md invariant 4 and docs/01_DATA_MODEL.md both specify a bare
/// <c>id INTEGER PRIMARY KEY</c>. EF's SQLite provider adds <c>AUTOINCREMENT</c> to any
/// single-column integer key that is value-generated on add, which costs a
/// <c>sqlite_sequence</c> write per insert on the sale path and contradicts the documented
/// schema.
/// </para>
/// <para>
/// Suppressing it here rather than by editing the migration is deliberate: EF's SQLite provider
/// rebuilds a table from the *model*, so an edit to migration 0001 alone would let
/// <c>AUTOINCREMENT</c> back in at the first <c>ALTER</c> in a later migration. The annotation is
/// produced by <see cref="SqliteAnnotationProvider"/> on the relational column rather than stored
/// on the property, which is why neither <c>ValueGeneratedNever</c> (it also stops EF generating
/// ids) nor a property annotation removes it.
/// </para>
/// <para>
/// The key stays <c>ValueGenerated.OnAdd</c>, so EF still reads the new rowid back after insert.
/// </para>
/// </remarks>
#pragma warning disable EF1001 // SqliteAnnotationProvider is the documented base for this override.
internal sealed class NoAutoincrementAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
    : SqliteAnnotationProvider(dependencies)
{
    private const string AutoincrementAnnotation = "Sqlite:Autoincrement";

    public override IEnumerable<IAnnotation> For(IColumn column, bool designTime) =>
        base.For(column, designTime).Where(annotation => annotation.Name != AutoincrementAnnotation);
}
#pragma warning restore EF1001
