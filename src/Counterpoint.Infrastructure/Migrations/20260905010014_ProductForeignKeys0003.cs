using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <summary>
    /// Gives <c>product.category_id</c> and <c>product.brand_id</c> the foreign keys the DDL in
    /// docs/01_DATA_MODEL.md §3 writes, and pins the column order while the table is being rebuilt
    /// anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Alone in its own migration, because it is the only step of this upgrade that cannot be
    /// atomic.</b> SQLite cannot add a constraint in place, so EF rebuilds the table - create
    /// <c>ef_temp_product</c>, copy, drop, rename - and that needs <c>PRAGMA foreign_keys = 0</c>,
    /// which SQLite ignores inside a transaction. EF therefore emits it transaction-suppressed,
    /// which splits the migration's SQL into three separate <c>BEGIN…COMMIT</c> groups with the
    /// history row written after the last of them. A power cut between two groups leaves work
    /// durably on disk that the next start will try to do again.
    /// </para>
    /// <para>
    /// Keeping that hazard to this migration is the point: <c>FullSchema0002</c>'s twenty-five
    /// tables and nine triggers commit as one transaction, so they are either all there or none of
    /// them are. Here the only durable half-state is a stray <c>ef_temp_product</c>, and the
    /// <c>DROP TABLE IF EXISTS</c> below clears it so a re-run completes instead of failing on
    /// "table ef_temp_product already exists". The indexes need no such guard: every re-run drops
    /// <c>product</c> and takes its indexes with it before recreating them.
    /// </para>
    /// <para>
    /// <c>PRAGMA defer_foreign_keys = 1</c> does not avoid any of this. <c>DROP TABLE product</c>
    /// increments the deferred-violation counter for every child row and the rename never clears
    /// it, so the COMMIT fails with "FOREIGN KEY constraint failed". It was tried.
    /// </para>
    /// <para>
    /// The <c>AlterColumn</c> wall below is the column order, not a type change. EF writes a
    /// <c>CreateTable</c> in property declaration order but sorts a <em>rebuilt</em> table's
    /// columns alphabetically, so without <c>HasColumnOrder</c> this migration would silently
    /// leave <c>product</c> in a different physical order from the DDL in §3 - and a positional
    /// <c>INSERT INTO product VALUES (...)</c> from a repair session or a bulk import would put a
    /// code in <c>active</c>, accepted without complaint by SQLite's type affinity.
    /// </para>
    /// </remarks>
    public partial class ProductForeignKeys0003 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First statement of the first command group, before EF's rebuild creates it. A run
            // interrupted between this migration's command groups leaves this table behind, and
            // without the drop the retry - which is what a till does on its next start - would
            // fail on "table ef_temp_product already exists" and never get past it.
            migrationBuilder.Sql("DROP TABLE IF EXISTS ef_temp_product;");

            migrationBuilder.AlterColumn<int>(
                name: "warranty_days",
                table: "product",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 16);

            migrationBuilder.AlterColumn<string>(
                name: "updated_at",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .Annotation("Relational:ColumnOrder", 21);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .Annotation("Relational:ColumnOrder", 7);

            migrationBuilder.AlterColumn<long>(
                name: "tax_class_id",
                table: "product",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .Annotation("Relational:ColumnOrder", 8);

            migrationBuilder.AlterColumn<long>(
                name: "reorder_qty",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L)
                .Annotation("Relational:ColumnOrder", 11);

            migrationBuilder.AlterColumn<long>(
                name: "reorder_level",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L)
                .Annotation("Relational:ColumnOrder", 10);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "product",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 17);

            migrationBuilder.AlterColumn<bool>(
                name: "non_returnable",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: false)
                .Annotation("Relational:ColumnOrder", 13);

            migrationBuilder.AlterColumn<string>(
                name: "name_alt",
                table: "product",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 3);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .Annotation("Relational:ColumnOrder", 2);

            migrationBuilder.AlterColumn<long>(
                name: "min_sell_qty",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L)
                .Annotation("Relational:ColumnOrder", 14);

            migrationBuilder.AlterColumn<long>(
                name: "max_discount_rate",
                table: "product",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 15);

            migrationBuilder.AlterColumn<string>(
                name: "location",
                table: "product",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 12);

            migrationBuilder.AlterColumn<string>(
                name: "image_path",
                table: "product",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 18);

            migrationBuilder.AlterColumn<string>(
                name: "created_at",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .Annotation("Relational:ColumnOrder", 20);

            migrationBuilder.AlterColumn<long>(
                name: "cost_avg",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L)
                .Annotation("Relational:ColumnOrder", 9);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .Annotation("Relational:ColumnOrder", 1);

            migrationBuilder.AlterColumn<long>(
                name: "category_id",
                table: "product",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 4);

            migrationBuilder.AlterColumn<long>(
                name: "brand_id",
                table: "product",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true)
                .Annotation("Relational:ColumnOrder", 5);

            migrationBuilder.AlterColumn<long>(
                name: "base_uom_id",
                table: "product",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .Annotation("Relational:ColumnOrder", 6);

            migrationBuilder.AlterColumn<bool>(
                name: "active",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true)
                .Annotation("Relational:ColumnOrder", 19);

            migrationBuilder.AlterColumn<long>(
                name: "id",
                table: "product",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .Annotation("Relational:ColumnOrder", 0);

            migrationBuilder.AddForeignKey(
                name: "fk_product_brand_brand_id",
                table: "product",
                column: "brand_id",
                principalTable: "brand",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_product_category_category_id",
                table: "product",
                column: "category_id",
                principalTable: "category",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_product_brand_brand_id",
                table: "product");

            migrationBuilder.DropForeignKey(
                name: "fk_product_category_category_id",
                table: "product");

            migrationBuilder.AlterColumn<int>(
                name: "warranty_days",
                table: "product",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true)
                .OldAnnotation("Relational:ColumnOrder", 16);

            migrationBuilder.AlterColumn<string>(
                name: "updated_at",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .OldAnnotation("Relational:ColumnOrder", 21);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .OldAnnotation("Relational:ColumnOrder", 7);

            migrationBuilder.AlterColumn<long>(
                name: "tax_class_id",
                table: "product",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .OldAnnotation("Relational:ColumnOrder", 8);

            migrationBuilder.AlterColumn<long>(
                name: "reorder_qty",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L)
                .OldAnnotation("Relational:ColumnOrder", 11);

            migrationBuilder.AlterColumn<long>(
                name: "reorder_level",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L)
                .OldAnnotation("Relational:ColumnOrder", 10);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "product",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .OldAnnotation("Relational:ColumnOrder", 17);

            migrationBuilder.AlterColumn<bool>(
                name: "non_returnable",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: false)
                .OldAnnotation("Relational:ColumnOrder", 13);

            migrationBuilder.AlterColumn<string>(
                name: "name_alt",
                table: "product",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .OldAnnotation("Relational:ColumnOrder", 3);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .OldAnnotation("Relational:ColumnOrder", 2);

            migrationBuilder.AlterColumn<long>(
                name: "min_sell_qty",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L)
                .OldAnnotation("Relational:ColumnOrder", 14);

            migrationBuilder.AlterColumn<long>(
                name: "max_discount_rate",
                table: "product",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true)
                .OldAnnotation("Relational:ColumnOrder", 15);

            migrationBuilder.AlterColumn<string>(
                name: "location",
                table: "product",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .OldAnnotation("Relational:ColumnOrder", 12);

            migrationBuilder.AlterColumn<string>(
                name: "image_path",
                table: "product",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true)
                .OldAnnotation("Relational:ColumnOrder", 18);

            migrationBuilder.AlterColumn<string>(
                name: "created_at",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .OldAnnotation("Relational:ColumnOrder", 20);

            migrationBuilder.AlterColumn<long>(
                name: "cost_avg",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldDefaultValue: 0L)
                .OldAnnotation("Relational:ColumnOrder", 9);

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "product",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT")
                .OldAnnotation("Relational:ColumnOrder", 1);

            migrationBuilder.AlterColumn<long>(
                name: "category_id",
                table: "product",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true)
                .OldAnnotation("Relational:ColumnOrder", 4);

            migrationBuilder.AlterColumn<long>(
                name: "brand_id",
                table: "product",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true)
                .OldAnnotation("Relational:ColumnOrder", 5);

            migrationBuilder.AlterColumn<long>(
                name: "base_uom_id",
                table: "product",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .OldAnnotation("Relational:ColumnOrder", 6);

            migrationBuilder.AlterColumn<bool>(
                name: "active",
                table: "product",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true)
                .OldAnnotation("Relational:ColumnOrder", 19);

            migrationBuilder.AlterColumn<long>(
                name: "id",
                table: "product",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER")
                .OldAnnotation("Relational:ColumnOrder", 0);
        }
    }
}
