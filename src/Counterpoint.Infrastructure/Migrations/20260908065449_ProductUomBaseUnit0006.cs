using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductUomBaseUnit0006 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_product_uom_one_base",
                table: "product_uom",
                column: "product_id",
                unique: true,
                filter: "is_base = 1");

            // docs/01_DATA_MODEL.md §3, §8: "exactly one base row per product with
            // conversion_factor = 10000" (FR-2.4, FR-2.5, P1-T05's UomConverter). The unique
            // index above is the "at most one" half; this is the "factor is exactly 10000 when
            // it is the base" half. SQLite cannot add a CHECK to an existing table without
            // rebuilding it (and a rebuild silently drops triggers - CLAUDE.md's trap), so this
            // is a BEFORE trigger, matching FullSchema0002's trg_category_two_levels_* rather
            // than a table rebuild. product_uom carries no other trigger today, so neither
            // approach risked collateral loss; the trigger is the one that never risks it again
            // on a later, unrelated ALTER.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_product_uom_base_factor_insert
BEFORE INSERT ON product_uom
WHEN new.is_base = 1 AND new.conversion_factor IS NOT 10000
BEGIN SELECT RAISE(ABORT, 'product_uom: base unit must have conversion_factor = 10000 (FR-2.4)'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_product_uom_base_factor_update
BEFORE UPDATE OF is_base, conversion_factor ON product_uom
WHEN new.is_base = 1 AND new.conversion_factor IS NOT 10000
BEGIN SELECT RAISE(ABORT, 'product_uom: base unit must have conversion_factor = 10000 (FR-2.4)'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_product_uom_base_factor_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_product_uom_base_factor_insert;");

            migrationBuilder.DropIndex(
                name: "ux_product_uom_one_base",
                table: "product_uom");
        }
    }
}
