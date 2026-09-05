using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.CompiledModels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// What <c>FullSchema0002</c>, <c>ProductForeignKeys0003</c> and <c>ProductSearch0004</c> add on
/// top of the skeleton: the storage
/// rule for money and quantity (DM-01), the two-level category rule (FR-2.20), the FTS5 search
/// index (FR-2.11), and the compiled model that start-up reads instead of building one (NFR-P6).
/// </summary>
public sealed class FullSchemaTests
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintTrigger = 1811;

    /// <summary>
    /// The risk the task calls out by name: value converters applied inconsistently, so some
    /// columns hold scaled integers and others hold text. This reads what SQLite actually stored,
    /// column by column, rather than what the DDL says it should have.
    /// </summary>
    /// <remarks>
    /// SQLite columns are typed by value, not by declaration - a <c>decimal</c> that slipped past
    /// a converter would be written into an <c>INTEGER</c> column as TEXT without a murmur, and
    /// money stored as text does not add up. The database is fully seeded, so every money and
    /// quantity column here has a real value in it.
    /// </remarks>
    [Fact]
    public async Task DM_01_EveryValueInAnIntegerColumnIsStoredAsAnInteger()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        var columns = await database.ColumnAsync(
            """
            SELECT tables.name || '.' || info.name
              FROM sqlite_schema AS tables
              JOIN pragma_table_info(tables.name) AS info
             WHERE tables.type = 'table'
               AND upper(info.type) = 'INTEGER'
               AND tables.name NOT LIKE 'sqlite_%'
               AND tables.name NOT LIKE 'product\_search%' ESCAPE '\'
               AND tables.name <> '__EFMigrationsHistory'
             ORDER BY 1;
            """);

        columns.Should().NotBeEmpty();

        var offenders = new List<string>();
        foreach (var column in columns)
        {
            var (table, name) = (column.Split('.')[0], column.Split('.')[1]);

            // 'null' is fine - the column is simply not set on the seeded row.
            var classes = await database.ColumnAsync(
                "SELECT DISTINCT typeof(\"" + name + "\") FROM \"" + table + "\";");

            offenders.AddRange(
                classes.Where(storageClass => storageClass is not ("integer" or "null"))
                       .Select(storageClass => column + " holds " + storageClass));
        }

        offenders.Should().BeEmpty(
            "money and quantity are INTEGER scaled x10 000 (docs/01_DATA_MODEL.md §1)");
    }

    /// <summary>
    /// The model half of the same rule. Every scaled value object must reach the provider as a
    /// <c>long</c>; a converter missed on one property would map that column to TEXT.
    /// </summary>
    [Fact]
    public async Task DM_01_EveryScaledValueObjectIsMappedThroughItsConverter()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var scaled = ModelOf(database).GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.ClrType == typeof(Money) || property.ClrType == typeof(Money?)
                || property.ClrType == typeof(TaxRate) || property.ClrType == typeof(TaxRate?)
                || property.ClrType == typeof(Percentage) || property.ClrType == typeof(Percentage?))
            .ToList();

        scaled.Should().NotBeEmpty("every money column in the schema maps to a Money");

        scaled.Should().OnlyContain(property => property.GetValueConverter() != null);
        scaled.Should().OnlyContain(property => property.GetValueConverter()!.ProviderClrType == typeof(long));
        scaled.Should().OnlyContain(property => property.GetColumnType() == "INTEGER");
    }

    /// <summary>
    /// A round trip through the database, because the two tests above are both static. A price of
    /// 125.0000 has to come back as 125.0000 and be stored as 1250000.
    /// </summary>
    [Fact]
    public async Task DM_01_AMoneyValueRoundTripsThroughItsScaledColumn()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        (await database.ScalarAsync("SELECT price FROM product_variant WHERE id = 1;"))
            .Should().Be("1250000");

        (await database.ScalarAsync("SELECT typeof(price) FROM product_variant WHERE id = 1;"))
            .Should().Be("integer");

        Money.FromScaled(1_250_000).Amount.Should().Be(125.0000m);
    }

    /// <summary>
    /// FR-2.20: two levels, no more. Enforced by the database, because a category tree that grows
    /// a third level is not something the catalogue screen can be trusted to prevent for ever.
    /// </summary>
    [Theory]

    // A parent that is itself a child.
    [InlineData("INSERT INTO category (id, name, parent_id) VALUES (10, 'M8', 2);")]

    // The same thing reached by moving an existing category underneath a child.
    [InlineData("UPDATE category SET parent_id = 2 WHERE id = 3;")]

    // A category that already has children cannot become a child itself.
    [InlineData("UPDATE category SET parent_id = 3 WHERE id = 1;")]

    // And a category may not be its own parent, which would be a cycle rather than a third level.
    [InlineData("UPDATE category SET parent_id = 3 WHERE id = 3;")]
    public async Task FR_2_20_AThirdCategoryLevelIsRefused(string sql)
    {
        await using var database = await MigratedDatabase.CreateAsync();

        // A spare top-level category for the UPDATE cases to move about.
        await database.ExecuteAsync("INSERT INTO category (id, name, parent_id) VALUES (3, 'Tools', NULL);");

        var exception = await database.ExecuteExpectingAbortAsync(sql);

        exception.SqliteErrorCode.Should().Be(SqliteConstraint);
        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        exception.Message.Should().Contain("two levels only");
    }

    /// <summary>
    /// The other half: two levels must still work, or the guard would have made the feature
    /// useless rather than safe.
    /// </summary>
    [Fact]
    public async Task FR_2_20_TwoLevelsAreAllowed()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        await database.ExecuteAsync("INSERT INTO category (id, name, parent_id) VALUES (3, 'Tools', NULL);");
        await database.ExecuteAsync("INSERT INTO category (id, name, parent_id) VALUES (4, 'Hand tools', 3);");

        // And a child may be moved to another top-level parent.
        await database.ExecuteAsync("UPDATE category SET parent_id = 1 WHERE id = 4;");

        (await database.ScalarAsync("SELECT parent_id FROM category WHERE id = 4;")).Should().Be("1");
    }

    /// <summary>
    /// FR-2.11, NFR-P2: the FTS5 index is maintained by triggers, so a variant is searchable the
    /// moment it is catalogued - by its own name, its brand, its category and its SKU.
    /// </summary>
    [Fact]
    public async Task FR_2_11_ANewVariantIsSearchableImmediately()
    {
        await using var database = await MigratedDatabase.CreateAsync();
        await CatalogueOneSearchableProductAsync(database);

        // Double-quoted where the term contains a hyphen: FTS5 reads a bare hyphen as query
        // syntax, not as part of a code.
        foreach (var term in new[] { "hammer", "claw", "Bosch", "Fasteners", "\"SKU-CLAW\"", "\"P-CLAW\"" })
        {
            (await database.CountAsync(
                "SELECT count(*) FROM product_search WHERE product_search MATCH '" + term + "';"))
                .Should().Be(1, term + " should find the new variant");
        }

        (await database.ScalarAsync(
            "SELECT rowid FROM product_search WHERE product_search MATCH 'claw';"))
            .Should().Be("50", "the FTS5 rowid is the product_variant id");
    }

    /// <summary>
    /// Renaming the product reindexes every one of its variants: the old term stops matching and
    /// the new one starts. This is the case the contentless <c>'delete'</c> command exists for.
    /// </summary>
    [Fact]
    public async Task FR_2_11_RenamingAProductReindexesIt()
    {
        await using var database = await MigratedDatabase.CreateAsync();
        await CatalogueOneSearchableProductAsync(database);

        await database.ExecuteAsync("UPDATE product SET name = 'Rubber mallet' WHERE id = 50;");

        (await database.CountAsync(
            "SELECT count(*) FROM product_search WHERE product_search MATCH 'hammer';"))
            .Should().Be(0);
        (await database.CountAsync(
            "SELECT count(*) FROM product_search WHERE product_search MATCH 'mallet';"))
            .Should().Be(1);

        await AssertSearchIndexIsSoundAsync(database);
    }

    /// <summary>Editing the SKU, and deleting the variant outright, both reach the index.</summary>
    [Fact]
    public async Task FR_2_11_EditingAndDeletingAVariantReachTheIndex()
    {
        await using var database = await MigratedDatabase.CreateAsync();
        await CatalogueOneSearchableProductAsync(database);

        await database.ExecuteAsync("UPDATE product_variant SET sku = 'SKU-MALLET' WHERE id = 50;");

        (await database.CountAsync(
            "SELECT count(*) FROM product_search WHERE product_search MATCH '\"SKU-CLAW\"';")).Should().Be(0);
        (await database.CountAsync(
            "SELECT count(*) FROM product_search WHERE product_search MATCH '\"SKU-MALLET\"';")).Should().Be(1);

        await database.ExecuteAsync("DELETE FROM product_variant WHERE id = 50;");

        (await database.CountAsync(
            "SELECT count(*) FROM product_search WHERE product_search MATCH 'hammer';")).Should().Be(0);

        await AssertSearchIndexIsSoundAsync(database);
    }

    /// <summary>
    /// A cost change must not reindex anything. Every goods receipt moves <c>cost_avg</c>, and a
    /// bare <c>AFTER UPDATE</c> would delete and rewrite every variant of the product each time -
    /// an avoidable write on the stock path, for a column the index does not carry.
    /// </summary>
    [Fact]
    public async Task NFR_P2_ACostChangeDoesNotTouchTheSearchIndex()
    {
        await using var database = await MigratedDatabase.CreateAsync();
        await CatalogueOneSearchableProductAsync(database);

        var triggerSql = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'trigger' AND name = 'trg_product_search_product_update';");

        triggerSql.Should().Contain("UPDATE OF name, name_alt, code, location, brand_id, category_id");
        triggerSql.Should().NotContain("cost_avg");

        await database.ExecuteAsync("UPDATE product SET cost_avg = 4242 WHERE id = 50;");

        (await database.CountAsync(
            "SELECT count(*) FROM product_search WHERE product_search MATCH 'hammer';")).Should().Be(1);

        await AssertSearchIndexIsSoundAsync(database);
    }

    /// <summary>
    /// NFR-P6: the compiled model is the one in use, not a copy sitting unused in the source tree.
    /// </summary>
    [Fact]
    public async Task NFR_P6_TheContextUsesTheCompiledModel()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        ModelOf(database).Should().BeSameAs(PosDbContextModel.Instance);
    }

    /// <summary>
    /// The failure mode that makes a compiled model dangerous: it is used in preference to the
    /// real one, and a stale one is not an error. Comparing it column by column against the
    /// database the migrations built is what says "these two agree".
    /// </summary>
    /// <remarks>
    /// The index and table comparisons in <see cref="SchemaConformanceTests"/> run against this
    /// same model; this adds the columns, which is where a forgotten regeneration would hide.
    /// </remarks>
    [Fact]
    public async Task NFR_P6_TheCompiledModelMatchesTheDatabaseTheMigrationsBuilt()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var inTheModel = ModelOf(database).GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties()
                .Select(property => entityType.GetTableName() + "." + property.GetColumnName()))
            .OrderBy(column => column, StringComparer.Ordinal)
            .ToList();

        var inTheFile = await database.ColumnAsync(
            """
            SELECT tables.name || '.' || info.name
              FROM sqlite_schema AS tables
              JOIN pragma_table_info(tables.name) AS info
             WHERE tables.type = 'table'
               AND tables.name NOT LIKE 'sqlite_%'
               AND tables.name NOT LIKE 'product\_search%' ESCAPE '\'
               AND tables.name NOT LIKE '\_\_EF%' ESCAPE '\'
             ORDER BY 1;
            """);

        inTheModel.Except(inTheFile, StringComparer.Ordinal).Should().BeEmpty("model has a column the file does not");
        inTheFile.Except(inTheModel, StringComparer.Ordinal).Should().BeEmpty("file has a column the model does not");
    }

    /// <summary>
    /// A product with a brand and a category, and one variant, so the search index has something
    /// with every indexed column populated. Ids in the fifties keep it clear of the seed.
    /// </summary>
    private static async Task CatalogueOneSearchableProductAsync(MigratedDatabase database)
    {
        await database.ExecuteAsync(
            """
            INSERT INTO product (id, code, name, name_alt, category_id, brand_id, base_uom_id,
                                 type, tax_class_id, location, created_at, updated_at)
            VALUES (50, 'P-CLAW', 'Claw hammer', 'Sammattiya', 1, 1, 1,
                    'STANDARD', 1, 'B7', '2026-09-04T08:00:00.000+05:30',
                    '2026-09-04T08:00:00.000+05:30');
            """);

        await database.ExecuteAsync(
            """
            INSERT INTO product_variant (id, product_id, sku, attributes, price, created_at)
            VALUES (50, 50, 'SKU-CLAW', '{}', 4500000, '2026-09-04T08:00:00.000+05:30');
            """);
    }

    /// <summary>
    /// FTS5's own audit of a contentless index. A <c>'delete'</c> issued with values that do not
    /// match what was indexed corrupts the term counts silently, and this is what notices.
    /// </summary>
    private static Task AssertSearchIndexIsSoundAsync(MigratedDatabase database) =>
        database.ExecuteAsync("INSERT INTO product_search(product_search) VALUES('integrity-check');");

    private static IModel ModelOf(MigratedDatabase database)
    {
        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlite(database.Connection, contextOwnsConnection: false)
            .Options;

        using var context = new PosDbContext(options);
        return context.Model;
    }
}
