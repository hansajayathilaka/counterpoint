using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <summary>
    /// The FTS5 product search index (FR-2.11, NFR-P2) and the triggers that keep it in step with
    /// the catalogue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A migration of its own, and not by preference. <c>ProductForeignKeys0003</c> rebuilds
    /// <c>product</c>, and EF emits that rebuild at the <em>end</em> of its migration whatever
    /// order the operations were written in, warning as much ("An operation of type 'SqlOperation'
    /// will be attempted while a rebuild of table 'product' is pending").
    /// </para>
    /// <para>
    /// SQLite re-parses every trigger in the schema during the closing
    /// <c>ALTER TABLE ef_temp_product RENAME TO product</c>. A trigger created before that point
    /// which selects <c>FROM product</c> therefore fails the rename outright with
    /// "error in trigger trg_product_search_variant_insert: no such table: main.product", and the
    /// upgrade stops. Splitting is the fix EF's own warning recommends.
    /// </para>
    /// <para>
    /// Nothing append-only is left unprotected by the split: the append-only triggers for
    /// <c>cash_movement</c>, <c>sale_return</c> and <c>sale_return_line</c> are created in
    /// <c>FullSchema0002</c>, alongside the tables they protect. What is deferred here maintains a
    /// rebuildable search index, so the worst a gap between the migrations could cost is a stale
    /// search result.
    /// </para>
    /// </remarks>
    public partial class ProductSearch0004 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CreateProductSearchIndex(migrationBuilder);
            BackfillProductSearchIndex(migrationBuilder);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Kept as a matching pair to <see cref="Up"/>. The working policy is forward-only: Down
        /// exists so `dotnet ef migrations remove` works while a migration is still being written,
        /// and is never run against a real till.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_product_search_product_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_product_search_variant_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_product_search_variant_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_product_search_variant_insert;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS product_search;");
        }

        /// <summary>
        /// The FTS5 index behind product search (FR-2.11, NFR-P2) and the triggers that keep it
        /// in step with the catalogue.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>content=''</c>: contentless, as docs/01_DATA_MODEL.md §3 specifies. The index stores
        /// no copy of the text, only the terms, and its <c>rowid</c> is
        /// <c>product_variant.id</c> - so a search result is already a variant id and needs no
        /// mapping table. A contentless FTS5 table cannot be DELETEd from, so removing a row is
        /// the documented <c>'delete'</c> command, which takes the values the row was indexed
        /// with. SQLite 3.39 is what ships here; <c>contentless_delete=1</c>, which would allow a
        /// plain DELETE, needs 3.43.
        /// </para>
        /// <para>
        /// The consequence, and it is a real one: the <c>'delete'</c> command must be handed the
        /// <em>old</em> values, and <c>brand</c> and <c>category</c> are read back through their
        /// tables. Renaming a brand between the insert and the next edit of one of its products
        /// therefore leaves stale terms in the index. That is a wrong search result, never a wrong
        /// price or a wrong bill, and the index is rebuildable - <c>ReindexSearchCommand</c> in
        /// P1-T06 is the remedy, and the same command is what an <c>'integrity-check'</c> failure
        /// would call for.
        /// </para>
        /// <para>
        /// <c>UPDATE OF</c> rather than a bare <c>UPDATE</c> on both tables. A product's
        /// <c>cost_avg</c> moves on every goods receipt and a variant's <c>price</c> on every
        /// repricing; neither is indexed, and reindexing every variant of a product on a cost
        /// change would put an avoidable write on the stock path.
        /// </para>
        /// </remarks>
        private static void CreateProductSearchIndex(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE VIRTUAL TABLE product_search USING fts5(
  name, name_alt, code, sku, brand, category, location,
  content=''
);");

            // rowid = product_variant.id. The brand and category names are denormalised into the
            // index so that ""Bosch"" or ""Fasteners"" finds the item; they are looked up here
            // rather than joined at query time, which is the whole point of an index.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_product_search_variant_insert
AFTER INSERT ON product_variant
BEGIN
  INSERT INTO product_search(rowid, name, name_alt, code, sku, brand, category, location)
  SELECT new.id, p.name, p.name_alt, p.code, new.sku,
         (SELECT b.name FROM brand b WHERE b.id = p.brand_id),
         (SELECT c.name FROM category c WHERE c.id = p.category_id),
         p.location
    FROM product p
   WHERE p.id = new.product_id;
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_product_search_variant_update
AFTER UPDATE OF sku, product_id ON product_variant
BEGIN
  INSERT INTO product_search(product_search, rowid, name, name_alt, code, sku, brand, category, location)
  SELECT 'delete', old.id, p.name, p.name_alt, p.code, old.sku,
         (SELECT b.name FROM brand b WHERE b.id = p.brand_id),
         (SELECT c.name FROM category c WHERE c.id = p.category_id),
         p.location
    FROM product p
   WHERE p.id = old.product_id;

  INSERT INTO product_search(rowid, name, name_alt, code, sku, brand, category, location)
  SELECT new.id, p.name, p.name_alt, p.code, new.sku,
         (SELECT b.name FROM brand b WHERE b.id = p.brand_id),
         (SELECT c.name FROM category c WHERE c.id = p.category_id),
         p.location
    FROM product p
   WHERE p.id = new.product_id;
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_product_search_variant_delete
AFTER DELETE ON product_variant
BEGIN
  INSERT INTO product_search(product_search, rowid, name, name_alt, code, sku, brand, category, location)
  SELECT 'delete', old.id, p.name, p.name_alt, p.code, old.sku,
         (SELECT b.name FROM brand b WHERE b.id = p.brand_id),
         (SELECT c.name FROM category c WHERE c.id = p.category_id),
         p.location
    FROM product p
   WHERE p.id = old.product_id;
END;");

            // One statement covers every variant of the product: a trigger body cannot loop, but
            // INSERT ... SELECT does not need to.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_product_search_product_update
AFTER UPDATE OF name, name_alt, code, location, brand_id, category_id ON product
BEGIN
  INSERT INTO product_search(product_search, rowid, name, name_alt, code, sku, brand, category, location)
  SELECT 'delete', v.id, old.name, old.name_alt, old.code, v.sku,
         (SELECT b.name FROM brand b WHERE b.id = old.brand_id),
         (SELECT c.name FROM category c WHERE c.id = old.category_id),
         old.location
    FROM product_variant v
   WHERE v.product_id = old.id;

  INSERT INTO product_search(rowid, name, name_alt, code, sku, brand, category, location)
  SELECT v.id, new.name, new.name_alt, new.code, v.sku,
         (SELECT b.name FROM brand b WHERE b.id = new.brand_id),
         (SELECT c.name FROM category c WHERE c.id = new.category_id),
         new.location
    FROM product_variant v
   WHERE v.product_id = new.id;
END;");

            // No trigger for INSERT or DELETE on `product`, and neither is an omission. A product
            // has no variants at the moment it is inserted, so there is nothing to index; and a
            // product that still has variants cannot be deleted at all, because
            // product_variant.product_id is a foreign key with NO ACTION - by the time the
            // product goes, its variants have gone through the trigger above.
        }

        /// <summary>
        /// Indexes the catalogue that already exists, because the triggers above only see what
        /// happens next.
        /// </summary>
        /// <remarks>
        /// Without this, a till upgrading from <c>Skeleton0001</c> with a catalogue already loaded
        /// would come back with search returning nothing - and nothing would fail, because an empty
        /// index is a valid index. It is a no-op on a new database. Deliberately the same SELECT as
        /// <c>trg_product_search_variant_insert</c>, minus the <c>WHERE</c>: the two must agree on
        /// what a row looks like, or the first <c>'delete'</c> issued against a backfilled row
        /// would corrupt the term counts.
        /// </remarks>
        private static void BackfillProductSearchIndex(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO product_search(rowid, name, name_alt, code, sku, brand, category, location)
SELECT v.id, p.name, p.name_alt, p.code, v.sku,
       (SELECT b.name FROM brand b WHERE b.id = p.brand_id),
       (SELECT c.name FROM category c WHERE c.id = p.category_id),
       p.location
  FROM product_variant v
  JOIN product p ON p.id = v.product_id;");
        }
    }
}
