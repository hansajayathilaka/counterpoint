using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Rewrites every table, column, key, foreign key and index name in the model to
/// <c>snake_case</c> (engineering guide §5, "Database naming").
/// </summary>
/// <remarks>
/// Hand-rolled rather than taken from a naming-convention package: it is thirty lines, it is
/// the one place the rule is expressed, and it keeps a dependency out of the write path.
/// </remarks>
internal static class SnakeCaseNaming
{
    public static void Apply(IMutableModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is not null)
            {
                entityType.SetTableName(ToSnakeCase(tableName));
            }

            var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);

            foreach (var property in entityType.GetProperties())
            {
                var columnName = table.HasValue
                    ? property.GetColumnName(table.Value) ?? property.Name
                    : property.Name;

                property.SetColumnName(ToSnakeCase(columnName));
            }

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
}
