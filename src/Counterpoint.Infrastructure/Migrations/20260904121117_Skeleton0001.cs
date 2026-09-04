using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Skeleton0001 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    username = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    failed_attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    locked_until = table.Column<string>(type: "TEXT", nullable: true),
                    last_login = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.id);
                    table.CheckConstraint("ck_app_user_role", "role IN ('CASHIER','OWNER')");
                });

            migrationBuilder.CreateTable(
                name: "number_sequence",
                columns: table => new
                {
                    doc_type = table.Column<string>(type: "TEXT", nullable: false),
                    prefix = table.Column<string>(type: "TEXT", nullable: false),
                    pattern = table.Column<string>(type: "TEXT", nullable: false),
                    next_val = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequence", x => x.doc_type);
                    table.CheckConstraint("ck_number_sequence_doc_type", "doc_type IN ('SALE','RETURN','CREDIT_NOTE','GRN','PO','SHIFT','STOCK_TAKE','QUOTE')");
                });

            migrationBuilder.CreateTable(
                name: "print_job",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    doc_type = table.Column<string>(type: "TEXT", nullable: false),
                    doc_id = table.Column<long>(type: "INTEGER", nullable: true),
                    target = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "RECEIPT"),
                    payload = table.Column<byte[]>(type: "BLOB", nullable: false),
                    copies = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    is_duplicate = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    printed_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_print_job", x => x.id);
                    table.CheckConstraint("ck_print_job_doc_type", "doc_type IN ('SALE','RETURN','CREDIT_NOTE','X_REPORT','Z_REPORT','GRN','PO','STOCK_TAKE','LABEL','CASH_SLIP')");
                    table.CheckConstraint("ck_print_job_status", "status IN ('PENDING','PRINTED','FAILED')");
                });

            migrationBuilder.CreateTable(
                name: "schema_version",
                columns: table => new
                {
                    version = table.Column<string>(type: "TEXT", nullable: false),
                    applied_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schema_version", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "tax_class",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    rate = table.Column<long>(type: "INTEGER", nullable: false),
                    active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_class", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "uom",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    symbol = table.Column<string>(type: "TEXT", nullable: false),
                    decimal_places = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uom", x => x.id);
                    table.CheckConstraint("ck_uom_decimal_places", "decimal_places BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: true),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    entity_type = table.Column<string>(type: "TEXT", nullable: false),
                    entity_id = table.Column<long>(type: "INTEGER", nullable: true),
                    before_json = table.Column<string>(type: "TEXT", nullable: true),
                    after_json = table.Column<string>(type: "TEXT", nullable: true),
                    reason = table.Column<string>(type: "TEXT", nullable: true),
                    prev_hash = table.Column<string>(type: "TEXT", nullable: false),
                    row_hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "shift",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    shift_no = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    opened_at = table.Column<string>(type: "TEXT", nullable: false),
                    business_date = table.Column<string>(type: "TEXT", nullable: false),
                    opening_float = table.Column<long>(type: "INTEGER", nullable: false),
                    closed_at = table.Column<string>(type: "TEXT", nullable: true),
                    counted_cash = table.Column<long>(type: "INTEGER", nullable: true),
                    expected_cash = table.Column<long>(type: "INTEGER", nullable: true),
                    variance = table.Column<long>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    closed_by = table.Column<long>(type: "INTEGER", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift", x => x.id);
                    table.CheckConstraint("ck_shift_status", "status IN ('OPEN','CLOSED')");
                    table.ForeignKey(
                        name: "fk_shift_app_user_closed_by",
                        column: x => x.closed_by,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_shift_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "product",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    name_alt = table.Column<string>(type: "TEXT", nullable: true),
                    category_id = table.Column<long>(type: "INTEGER", nullable: true),
                    brand_id = table.Column<long>(type: "INTEGER", nullable: true),
                    base_uom_id = table.Column<long>(type: "INTEGER", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    tax_class_id = table.Column<long>(type: "INTEGER", nullable: false),
                    cost_avg = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    reorder_level = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    reorder_qty = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    location = table.Column<string>(type: "TEXT", nullable: true),
                    non_returnable = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    min_sell_qty = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    max_discount_rate = table.Column<long>(type: "INTEGER", nullable: true),
                    warranty_days = table.Column<int>(type: "INTEGER", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    image_path = table.Column<string>(type: "TEXT", nullable: true),
                    active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product", x => x.id);
                    table.CheckConstraint("ck_product_type", "type IN ('STANDARD','DECIMAL','SERVICE','NON_INVENTORY')");
                    table.ForeignKey(
                        name: "fk_product_tax_class_tax_class_id",
                        column: x => x.tax_class_id,
                        principalTable: "tax_class",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_product_uom_base_uom_id",
                        column: x => x.base_uom_id,
                        principalTable: "uom",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sale",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    bill_no = table.Column<string>(type: "TEXT", nullable: false),
                    sold_at = table.Column<string>(type: "TEXT", nullable: false),
                    business_date = table.Column<string>(type: "TEXT", nullable: false),
                    customer_id = table.Column<long>(type: "INTEGER", nullable: true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    shift_id = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal = table.Column<long>(type: "INTEGER", nullable: false),
                    line_discount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    bill_discount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    tax = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    rounding = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    total = table.Column<long>(type: "INTEGER", nullable: false),
                    cogs = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    cancelled_by = table.Column<long>(type: "INTEGER", nullable: true),
                    cancelled_at = table.Column<string>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    prev_hash = table.Column<string>(type: "TEXT", nullable: false),
                    row_hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale", x => x.id);
                    table.CheckConstraint("ck_sale_status", "status IN ('COMPLETED','CANCELLED')");
                    table.ForeignKey(
                        name: "fk_sale_app_user_cancelled_by",
                        column: x => x.cancelled_by,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_shift_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shift",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "product_variant",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    sku = table.Column<string>(type: "TEXT", nullable: false),
                    attributes = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    price = table.Column<long>(type: "INTEGER", nullable: false),
                    active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variant", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_variant_product_product_id",
                        column: x => x.product_id,
                        principalTable: "product",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    sale_id = table.Column<long>(type: "INTEGER", nullable: true),
                    sale_return_id = table.Column<long>(type: "INTEGER", nullable: true),
                    tender_type = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    reference = table.Column<string>(type: "TEXT", nullable: true),
                    paid_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment", x => x.id);
                    table.CheckConstraint("ck_payment_one_document", "(sale_id IS NOT NULL) <> (sale_return_id IS NOT NULL)");
                    table.CheckConstraint("ck_payment_tender_type", "tender_type IN ('CASH','CARD','BANK_TRANSFER','CREDIT_NOTE','ON_ACCOUNT','CHEQUE')");
                    table.ForeignKey(
                        name: "fk_payment_sale_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sale",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sale_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    sale_id = table.Column<long>(type: "INTEGER", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    qty = table.Column<long>(type: "INTEGER", nullable: false),
                    uom_id = table.Column<long>(type: "INTEGER", nullable: false),
                    qty_base = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_price = table.Column<long>(type: "INTEGER", nullable: false),
                    discount = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    tax_rate = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    tax = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    line_total = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    qty_returned = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_sale_line_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_line_sale_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sale",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_line_uom_uom_id",
                        column: x => x.uom_id,
                        principalTable: "uom",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stock_balance",
                columns: table => new
                {
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    qty_base = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    cost_avg = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_balance", x => x.product_variant_id);
                    table.ForeignKey(
                        name: "fk_stock_balance_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stock_movement",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    movement_type = table.Column<string>(type: "TEXT", nullable: false),
                    qty_base = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    ref_doc_type = table.Column<string>(type: "TEXT", nullable: false),
                    ref_doc_id = table.Column<long>(type: "INTEGER", nullable: true),
                    balance_after = table.Column<long>(type: "INTEGER", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movement", x => x.id);
                    table.CheckConstraint("ck_stock_movement_movement_type", "movement_type IN ('GRN','SALE','RETURN_IN','ADJUSTMENT','DAMAGE','STOCK_TAKE','BULK_BREAK_OUT','BULK_BREAK_IN','OPENING','TRANSFER_OUT','TRANSFER_IN')");
                    table.ForeignKey(
                        name: "fk_stock_movement_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movement_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ux_app_user_username",
                table: "app_user",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entity",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_time",
                table: "audit_log",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_payment_return",
                table: "payment",
                column: "sale_return_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_sale",
                table: "payment",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_print_pending",
                table: "print_job",
                column: "status",
                filter: "status = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "ix_product_active",
                table: "product",
                column: "active");

            migrationBuilder.CreateIndex(
                name: "ix_product_brand",
                table: "product",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_category",
                table: "product",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_code",
                table: "product",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_variant_product",
                table: "product_variant",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_variant_sku",
                table: "product_variant",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_cust",
                table: "sale",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_date",
                table: "sale",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "ix_sale_shift",
                table: "sale",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_soldat",
                table: "sale",
                column: "sold_at");

            migrationBuilder.CreateIndex(
                name: "ux_sale_bill_no",
                table: "sale",
                column: "bill_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_line_sale",
                table: "sale_line",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_line_variant",
                table: "sale_line",
                columns: new[] { "product_variant_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sale_line_no",
                table: "sale_line",
                columns: new[] { "sale_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_one_open_shift",
                table: "shift",
                column: "status",
                unique: true,
                filter: "status = 'OPEN'");

            migrationBuilder.CreateIndex(
                name: "ux_shift_no",
                table: "shift",
                column: "shift_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_balance_low",
                table: "stock_balance",
                column: "qty_base");

            migrationBuilder.CreateIndex(
                name: "ix_movement_ref",
                table: "stock_movement",
                columns: new[] { "ref_doc_type", "ref_doc_id" });

            migrationBuilder.CreateIndex(
                name: "ix_movement_time",
                table: "stock_movement",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_movement_variant_time",
                table: "stock_movement",
                columns: new[] { "product_variant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_tax_class_name",
                table: "tax_class",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_uom_name",
                table: "uom",
                column: "name",
                unique: true);

            CreateAppendOnlyTriggers(migrationBuilder);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Kept as EF generated it. The working policy is forward-only: Down exists so
        /// `dotnet ef migrations remove` works while a migration is still being written, and is
        /// never run against a real till.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "number_sequence");

            migrationBuilder.DropTable(
                name: "payment");

            migrationBuilder.DropTable(
                name: "print_job");

            migrationBuilder.DropTable(
                name: "sale_line");

            migrationBuilder.DropTable(
                name: "schema_version");

            migrationBuilder.DropTable(
                name: "stock_balance");

            migrationBuilder.DropTable(
                name: "stock_movement");

            migrationBuilder.DropTable(
                name: "sale");

            migrationBuilder.DropTable(
                name: "product_variant");

            migrationBuilder.DropTable(
                name: "shift");

            migrationBuilder.DropTable(
                name: "product");

            migrationBuilder.DropTable(
                name: "app_user");

            migrationBuilder.DropTable(
                name: "tax_class");

            migrationBuilder.DropTable(
                name: "uom");
        }

        /// <summary>
        /// The append-only triggers of CLAUDE.md invariant 5 and docs/01_DATA_MODEL.md §6.
        /// EF cannot express a trigger, so this is raw SQL.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Written out literally, and deliberately not shared with any other migration. Migrations
        /// are immutable history: a shared SQL constant edited in a later task would retroactively
        /// change what this migration did. <c>Data/AppendOnlyTables.cs</c> holds the expected
        /// trigger *names* only, which is what the survival check compares against.
        /// </para>
        /// <para>
        /// EF Core's SQLite provider rebuilds a table (create-copy-drop-rename) for almost any
        /// alter, and a rebuild silently drops that table's triggers. Any later migration that
        /// alters an append-only table must re-create its triggers in the same migration.
        /// </para>
        /// <para>
        /// The <c>WHEN</c> guards use <c>IS NOT</c> rather than <c>&lt;&gt;</c> on purpose:
        /// <c>&lt;&gt;</c> against a NULL column yields NULL, a NULL <c>WHEN</c> does not fire,
        /// and an UPDATE that touched a nullable column of a row where it was NULL would slip
        /// straight past the guard. <c>IS NOT</c> is NULL-safe and identical for NOT NULL columns.
        /// </para>
        /// </remarks>
        private static void CreateAppendOnlyTriggers(MigrationBuilder migrationBuilder)
        {
            // ---- stock_movement: the stock ledger is the truth (invariant 3) ----------------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_stock_movement_no_update
BEFORE UPDATE ON stock_movement
BEGIN SELECT RAISE(ABORT, 'stock_movement is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_stock_movement_no_delete
BEFORE DELETE ON stock_movement
BEGIN SELECT RAISE(ABORT, 'stock_movement is append-only'); END;");

            // ---- payment --------------------------------------------------------------------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_payment_no_update
BEFORE UPDATE ON payment
BEGIN SELECT RAISE(ABORT, 'payment is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_payment_no_delete
BEFORE DELETE ON payment
BEGIN SELECT RAISE(ABORT, 'payment is append-only'); END;");

            // ---- audit_log: hash chained, so a single edited row breaks the chain -----------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_audit_log_no_update
BEFORE UPDATE ON audit_log
BEGIN SELECT RAISE(ABORT, 'audit_log is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_audit_log_no_delete
BEFORE DELETE ON audit_log
BEGIN SELECT RAISE(ABORT, 'audit_log is append-only'); END;");

            // ---- sale: append-only except status, cancelled_by, cancelled_at ----------------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_no_delete
BEFORE DELETE ON sale
BEGIN SELECT RAISE(ABORT, 'sale is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_restricted_update
BEFORE UPDATE ON sale
WHEN  old.id            IS NOT new.id
   OR old.bill_no       IS NOT new.bill_no
   OR old.sold_at       IS NOT new.sold_at
   OR old.business_date IS NOT new.business_date
   OR old.customer_id   IS NOT new.customer_id
   OR old.user_id       IS NOT new.user_id
   OR old.shift_id      IS NOT new.shift_id
   OR old.subtotal      IS NOT new.subtotal
   OR old.line_discount IS NOT new.line_discount
   OR old.bill_discount IS NOT new.bill_discount
   OR old.tax           IS NOT new.tax
   OR old.rounding      IS NOT new.rounding
   OR old.total         IS NOT new.total
   OR old.cogs          IS NOT new.cogs
   OR old.note          IS NOT new.note
   OR old.prev_hash     IS NOT new.prev_hash
   OR old.row_hash      IS NOT new.row_hash
BEGIN SELECT RAISE(ABORT, 'sale: only status, cancelled_by and cancelled_at may be updated'); END;");

            // COMPLETED -> CANCELLED, one direction, once. A cancelled bill keeps its number
            // (invariant 4).
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_cancel_only_forward
BEFORE UPDATE OF status ON sale
WHEN NOT (old.status = 'COMPLETED' AND new.status = 'CANCELLED')
BEGIN SELECT RAISE(ABORT, 'sale.status may only change from COMPLETED to CANCELLED'); END;");

            // Without this, cancelled_at could be rewritten on a live COMPLETED bill: the trigger
            // above only fires when status is named in the SET clause.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_cancel_fields_together
BEFORE UPDATE ON sale
WHEN (old.cancelled_by IS NOT new.cancelled_by OR old.cancelled_at IS NOT new.cancelled_at)
     AND NOT (old.status = 'COMPLETED' AND new.status = 'CANCELLED')
BEGIN SELECT RAISE(ABORT, 'sale: cancellation fields may only be set while cancelling'); END;");

            // FR-8.5, AC-11: no sale may be posted into a closed shift.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_shift_open
BEFORE INSERT ON sale
WHEN (SELECT status FROM shift WHERE id = new.shift_id) IS NOT 'OPEN'
BEGIN SELECT RAISE(ABORT, 'cannot post into a closed shift'); END;");

            // ---- sale_line: append-only except qty_returned ---------------------------------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_line_no_delete
BEFORE DELETE ON sale_line
BEGIN SELECT RAISE(ABORT, 'sale_line is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_line_restricted_update
BEFORE UPDATE ON sale_line
WHEN  old.id                 IS NOT new.id
   OR old.sale_id            IS NOT new.sale_id
   OR old.line_no            IS NOT new.line_no
   OR old.product_variant_id IS NOT new.product_variant_id
   OR old.description        IS NOT new.description
   OR old.qty                IS NOT new.qty
   OR old.uom_id             IS NOT new.uom_id
   OR old.qty_base           IS NOT new.qty_base
   OR old.unit_price         IS NOT new.unit_price
   OR old.discount           IS NOT new.discount
   OR old.tax_rate           IS NOT new.tax_rate
   OR old.tax                IS NOT new.tax
   OR old.line_total         IS NOT new.line_total
   OR old.unit_cost          IS NOT new.unit_cost
   OR old.note               IS NOT new.note
BEGIN SELECT RAISE(ABORT, 'sale_line: only qty_returned may be updated'); END;");

            // AC-06: no cumulative over-return. The application checks this inside the return
            // transaction; the database is what makes it true. Monotonic, because there is no
            // reversal document for a sale_return: winding qty_returned back down would let the
            // same line be returned twice over and defeat the cumulative bound.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_line_qty_returned_bounds
BEFORE UPDATE OF qty_returned ON sale_line
WHEN new.qty_returned < 0
  OR new.qty_returned > new.qty_base
  OR new.qty_returned < old.qty_returned
BEGIN SELECT RAISE(ABORT, 'sale_line.qty_returned must be between 0 and qty_base and may never decrease'); END;");

            // ---- shift: append-only except the close fields, settable once ------------------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_shift_no_delete
BEFORE DELETE ON shift
BEGIN SELECT RAISE(ABORT, 'shift is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_shift_restricted_update
BEFORE UPDATE ON shift
WHEN  old.id            IS NOT new.id
   OR old.shift_no      IS NOT new.shift_no
   OR old.user_id       IS NOT new.user_id
   OR old.opened_at     IS NOT new.opened_at
   OR old.business_date IS NOT new.business_date
   OR old.opening_float IS NOT new.opening_float
BEGIN SELECT RAISE(ABORT, 'shift: only the close fields may be updated'); END;");

            // "Settable once": a closed shift is frozen, so status can never go CLOSED -> OPEN
            // either.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_shift_closed_is_final
BEFORE UPDATE ON shift
WHEN old.status = 'CLOSED'
BEGIN SELECT RAISE(ABORT, 'a closed shift is immutable'); END;");

            // note is a close field, not an immutable one: SRS §8.7 requires a note when the cash
            // variance exceeds the threshold, and RPT-05 prints it on the Z report. It is written
            // by the same UPDATE that closes the shift, so it is guarded here - never settable on
            // a live OPEN shift, never editable afterwards (trg_shift_closed_is_final).
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_shift_close_fields_together
BEFORE UPDATE ON shift
WHEN (old.closed_at     IS NOT new.closed_at
   OR old.counted_cash  IS NOT new.counted_cash
   OR old.expected_cash IS NOT new.expected_cash
   OR old.variance      IS NOT new.variance
   OR old.closed_by     IS NOT new.closed_by
   OR old.note          IS NOT new.note)
   AND NOT (old.status = 'OPEN' AND new.status = 'CLOSED')
BEGIN SELECT RAISE(ABORT, 'shift close fields may only be set while closing the shift'); END;");
        }
    }
}
