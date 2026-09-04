using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Rewrites every table, column, key, foreign key and index name in the model to
/// <c>snake_case</c> (engineering guide §5, "Database naming").
/// </summary>
/// <remarks>
/// <para>
/// A model-finalizing convention, not a pass at the end of <c>OnModelCreating</c>. Naming has to
/// run after EF has resolved table sharing, otherwise an owned type's key and foreign key
/// columns are renamed before they are mapped onto the owner's table and model building fails.
/// It also has to see the model EF finished building rather than the one we described, or
/// anything EF inferred - complex-type properties in particular - keeps its PascalCase name with
/// no error to show for it.
/// </para>
/// <para>
/// Registered from <see cref="PosDbContext.ConfigureConventions"/>. Hand-rolled rather than
/// taken from a naming-convention package: it is the one place the rule is expressed, and it
/// keeps a dependency out of the write path.
/// </para>
/// </remarks>
internal sealed class SnakeCaseNamingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        // Tables first, every one of them, before a single column is touched. A column's name is
        // resolved against the table it lands in, and an owned type shares its owner's table: if
        // the owner were renamed while the owned type still carried the old table name, EF would
        // stop recognising the two as the same table and give the owned key its own column
        // instead of the owner's. Model building then fails outright - "the keys ... are both
        // mapped to ... but with different columns".
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is not null)
            {
                entityType.SetTableName(ToSnakeCase(tableName));
            }
        }

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            RenameColumns(entityType);

            foreach (var key in entityType.GetKeys())
            {
                var keyName = key.GetName();
                if (keyName is not null)
                {
                    key.SetName(ToSnakeCase(keyName));
                }
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                var constraintName = foreignKey.GetConstraintName();
                if (constraintName is not null)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(constraintName));
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (indexName is not null)
                {
                    index.SetDatabaseName(ToSnakeCase(indexName));
                }
            }
        }
    }

    /// <summary>
    /// <c>SaleLine</c> -&gt; <c>sale_line</c>, <c>UomId</c> -&gt; <c>uom_id</c>,
    /// <c>QtyBase</c> -&gt; <c>qty_base</c>. Already-snake_case names pass through unchanged,
    /// so the convention is safe to apply on top of an explicit <c>ToTable</c>.
    /// </summary>
    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (!char.IsUpper(current))
            {
                builder.Append(current);
                continue;
            }

            if (i > 0 && NeedsSeparatorBefore(name, i))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static bool NeedsSeparatorBefore(string name, int index)
    {
        var previous = name[index - 1];
        if (previous == '_')
        {
            return false;
        }

        // Lower-to-upper is always a word boundary ("saleLine"). Inside a run of capitals only
        // the last one starts a new word ("GRNLine" -> "grn_line").
        return !char.IsUpper(previous)
            || (index + 1 < name.Length && char.IsLower(name[index + 1]));
    }

    /// <summary>
    /// Renames the scalar columns of <paramref name="type"/> and then of every complex type it
    /// contains, to any depth. <c>Money</c> and <c>Quantity</c> (CLAUDE.md invariant 1) map this
    /// way, so a complex property whose columns were skipped would reach the database as
    /// <c>UnitPrice</c> rather than <c>unit_price</c> and nothing would say so.
    /// </summary>
    private static void RenameColumns(IConventionTypeBase type)
    {
        foreach (var property in type.GetProperties())
        {
            RenameColumn(property);
        }

        foreach (var complexProperty in type.GetComplexProperties())
        {
            RenameColumns(complexProperty.ComplexType);
        }
    }

    private static void RenameColumn(IConventionProperty property)
    {
        var builder = property.Builder;

        // Start from the name EF would have derived on its own. Reading the current name and
        // rewriting it would compound: a second pass over "unit_price" is harmless, but a name
        // this convention already set on a shared table can be the owner's column, and renaming
        // that again would break the mapping. HasNoAnnotation leaves an explicit HasColumnName
        // alone - a name somebody typed by hand outranks a convention.
        builder.HasNoAnnotation(RelationalAnnotationNames.ColumnName);

        var defaultName = StoreObjectIdentifier.Create(property.DeclaringType, StoreObjectType.Table) is { } table
            ? property.GetDefaultColumnName(table)
            : property.GetDefaultColumnName();

        if (defaultName is not null)
        {
            builder.HasColumnName(ToSnakeCase(defaultName));
        }
    }
}
