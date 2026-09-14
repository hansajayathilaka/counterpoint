using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <summary>
    /// P2-T09 (docs/04_PHASE_2_returns_inventory.md, docs/01_DATA_MODEL.md §4): one new table,
    /// <c>bulk_break</c>, and nothing else — no existing table is touched, so there is no rebuild
    /// and no trigger to lose or re-create here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BULK_BREAK_OUT</c> and <c>BULK_BREAK_IN</c> have been legal
    /// <c>stock_movement.movement_type</c> values since the skeleton migration
    /// (<c>ck_stock_movement_movement_type</c>), and <c>stock_movement.ref_doc_id</c> already
    /// carries no foreign key of its own — so a "balanced pair" of movements could, in principle,
    /// have been posted with today's schema and no migration at all, exactly as P2-T08 posted
    /// <c>ADJUSTMENT</c> and <c>DAMAGE</c> with none.
    /// </para>
    /// <para>
    /// What P2-T08 did not need, and this task does, is a value <em>both</em> rows of the pair
    /// carry identically in <c>ref_doc_id</c> before either exists. Every other multi-row business
    /// event in this schema gets that from a header row inserted first — <c>sale.id</c> for a
    /// bill, <c>goods_receipt.id</c> for a GRN, <c>stock_take.id</c> for a count — and
    /// <c>CancelSaleHandler</c>'s own compensating movements lean on exactly that when they reuse
    /// <c>sale.id</c> for a reversal. A bulk break has no such header today: it is not a sale, a
    /// GRN or a stock take, and <c>stock_movement</c> is append-only, so the <c>BULK_BREAK_OUT</c>
    /// row cannot be written first and then updated with its own id once known. <c>bulk_break</c>
    /// is that header, and nothing more — it is not on CLAUDE.md invariant 5's append-only list
    /// (the same as <c>goods_receipt</c>, <c>purchase_order</c> and <c>stock_take</c>), it mints no
    /// document number through <c>number_sequence</c> (nothing here is printed), and it carries no
    /// index beyond its primary key for the same reason those three tables carry none beyond
    /// their own line tables' foreign key.
    /// </para>
    /// </remarks>
    public partial class BulkBreak0009 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bulk_break",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    source_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    destination_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    source_qty_base = table.Column<long>(type: "INTEGER", nullable: false),
                    expected_qty_base = table.Column<long>(type: "INTEGER", nullable: false),
                    actual_qty_base = table.Column<long>(type: "INTEGER", nullable: false),
                    wastage_qty_base = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    total_value = table.Column<long>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_break", x => x.id);
                    table.CheckConstraint("ck_bulk_break_actual_qty_base", "actual_qty_base > 0");
                    table.CheckConstraint("ck_bulk_break_distinct_variants", "source_variant_id <> destination_variant_id");
                    table.CheckConstraint("ck_bulk_break_expected_qty_base", "expected_qty_base > 0");
                    table.CheckConstraint("ck_bulk_break_source_qty_base", "source_qty_base > 0");
                    table.CheckConstraint("ck_bulk_break_wastage_qty_base", "wastage_qty_base >= 0");
                    table.ForeignKey(
                        name: "fk_bulk_break_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_bulk_break_product_variant_destination_variant_id",
                        column: x => x.destination_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_bulk_break_product_variant_source_variant_id",
                        column: x => x.source_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bulk_break");
        }
    }
}
