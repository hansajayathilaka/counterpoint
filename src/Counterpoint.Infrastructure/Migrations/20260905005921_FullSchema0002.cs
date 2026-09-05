using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counterpoint.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FullSchema0002 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_setting",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    value_type = table.Column<string>(type: "TEXT", nullable: false),
                    updated_by = table.Column<long>(type: "INTEGER", nullable: true),
                    updated_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_setting", x => x.key);
                    table.CheckConstraint("ck_app_setting_value_type", "value_type IN ('STRING','INT','MONEY','BOOL','JSON')");
                    table.ForeignKey(
                        name: "fk_app_setting_app_user_updated_by",
                        column: x => x.updated_by,
                        principalTable: "app_user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "backup_record",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    filename = table.Column<string>(type: "TEXT", nullable: false),
                    taken_at = table.Column<string>(type: "TEXT", nullable: false),
                    size_bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    checksum = table.Column<string>(type: "TEXT", nullable: false),
                    schema_ver = table.Column<string>(type: "TEXT", nullable: false),
                    local_path = table.Column<string>(type: "TEXT", nullable: true),
                    usb_status = table.Column<string>(type: "TEXT", nullable: false),
                    cloud_status = table.Column<string>(type: "TEXT", nullable: false),
                    cloud_key = table.Column<string>(type: "TEXT", nullable: true),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    verified_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backup_record", x => x.id);
                    table.CheckConstraint("ck_backup_record_cloud_status", "cloud_status IN ('PENDING','OK','FAILED','SKIPPED')");
                    table.CheckConstraint("ck_backup_record_usb_status", "usb_status IN ('NA','OK','FAILED')");
                });

            migrationBuilder.CreateTable(
                name: "barcode",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    barcode = table.Column<string>(type: "TEXT", nullable: false),
                    is_primary = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barcode", x => x.id);
                    table.ForeignKey(
                        name: "fk_barcode_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "brand",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brand", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cash_movement",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    shift_id = table.Column<long>(type: "INTEGER", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    occurred_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_movement", x => x.id);
                    table.CheckConstraint("ck_cash_movement_amount", "amount > 0");
                    table.CheckConstraint("ck_cash_movement_direction", "direction IN ('IN','OUT')");
                    table.ForeignKey(
                        name: "fk_cash_movement_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_cash_movement_shift_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shift",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "category",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    parent_id = table.Column<long>(type: "INTEGER", nullable: true),
                    active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                    table.ForeignKey(
                        name: "fk_category_category_parent_id",
                        column: x => x.parent_id,
                        principalTable: "category",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "customer",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    phone = table.Column<string>(type: "TEXT", nullable: true),
                    address = table.Column<string>(type: "TEXT", nullable: true),
                    tax_no = table.Column<string>(type: "TEXT", nullable: true),
                    type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "RETAIL"),
                    credit_limit = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    balance = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer", x => x.id);
                    table.CheckConstraint("ck_customer_type", "type IN ('RETAIL','TRADE')");
                });

            migrationBuilder.CreateTable(
                name: "daily_product_summary",
                columns: table => new
                {
                    business_date = table.Column<string>(type: "TEXT", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    qty_base = table.Column<long>(type: "INTEGER", nullable: false),
                    net = table.Column<long>(type: "INTEGER", nullable: false),
                    cogs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_product_summary", x => new { x.business_date, x.product_variant_id });
                    table.ForeignKey(
                        name: "fk_daily_product_summary_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "daily_sales_summary",
                columns: table => new
                {
                    business_date = table.Column<string>(type: "TEXT", nullable: false),
                    bill_count = table.Column<long>(type: "INTEGER", nullable: false),
                    gross = table.Column<long>(type: "INTEGER", nullable: false),
                    discount = table.Column<long>(type: "INTEGER", nullable: false),
                    tax = table.Column<long>(type: "INTEGER", nullable: false),
                    net = table.Column<long>(type: "INTEGER", nullable: false),
                    cogs = table.Column<long>(type: "INTEGER", nullable: false),
                    return_count = table.Column<long>(type: "INTEGER", nullable: false),
                    return_value = table.Column<long>(type: "INTEGER", nullable: false),
                    tender_cash = table.Column<long>(type: "INTEGER", nullable: false),
                    tender_card = table.Column<long>(type: "INTEGER", nullable: false),
                    tender_other = table.Column<long>(type: "INTEGER", nullable: false),
                    built_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_sales_summary", x => x.business_date);
                });

            migrationBuilder.CreateTable(
                name: "held_bill",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    label = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_held_bill", x => x.id);
                    table.ForeignKey(
                        name: "fk_held_bill_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "price_change_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    old_price = table.Column<long>(type: "INTEGER", nullable: false),
                    new_price = table.Column<long>(type: "INTEGER", nullable: false),
                    changed_at = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_change_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_price_change_log_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_price_change_log_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "price_tier",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    tier = table.Column<string>(type: "TEXT", nullable: false),
                    min_qty = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    price = table.Column<long>(type: "INTEGER", nullable: false),
                    valid_from = table.Column<string>(type: "TEXT", nullable: true),
                    valid_to = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_tier", x => x.id);
                    table.CheckConstraint("ck_price_tier_tier", "tier IN ('RETAIL','TRADE')");
                    table.ForeignKey(
                        name: "fk_price_tier_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "product_uom",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    uom_id = table.Column<long>(type: "INTEGER", nullable: false),
                    conversion_factor = table.Column<long>(type: "INTEGER", nullable: false),
                    selling_price = table.Column<long>(type: "INTEGER", nullable: true),
                    is_base = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_uom", x => x.id);
                    table.CheckConstraint("ck_product_uom_conversion_factor", "conversion_factor > 0");
                    table.ForeignKey(
                        name: "fk_product_uom_product_product_id",
                        column: x => x.product_id,
                        principalTable: "product",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_product_uom_uom_uom_id",
                        column: x => x.uom_id,
                        principalTable: "uom",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stock_take",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    scope = table.Column<string>(type: "TEXT", nullable: false),
                    started_at = table.Column<string>(type: "TEXT", nullable: false),
                    completed_at = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_take", x => x.id);
                    table.CheckConstraint("ck_stock_take_status", "status IN ('OPEN','POSTED','ABANDONED')");
                    table.ForeignKey(
                        name: "fk_stock_take_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    contact = table.Column<string>(type: "TEXT", nullable: true),
                    phone = table.Column<string>(type: "TEXT", nullable: true),
                    address = table.Column<string>(type: "TEXT", nullable: true),
                    tax_no = table.Column<string>(type: "TEXT", nullable: true),
                    payment_terms = table.Column<string>(type: "TEXT", nullable: true),
                    active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sale_return",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    return_no = table.Column<string>(type: "TEXT", nullable: false),
                    returned_at = table.Column<string>(type: "TEXT", nullable: false),
                    business_date = table.Column<string>(type: "TEXT", nullable: false),
                    original_sale_id = table.Column<long>(type: "INTEGER", nullable: true),
                    exchange_sale_id = table.Column<long>(type: "INTEGER", nullable: true),
                    customer_id = table.Column<long>(type: "INTEGER", nullable: true),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    shift_id = table.Column<long>(type: "INTEGER", nullable: false),
                    subtotal = table.Column<long>(type: "INTEGER", nullable: false),
                    tax = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    restocking_fee = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    total_refund = table.Column<long>(type: "INTEGER", nullable: false),
                    refund_method = table.Column<string>(type: "TEXT", nullable: false),
                    authorised_by = table.Column<long>(type: "INTEGER", nullable: true),
                    reason = table.Column<string>(type: "TEXT", nullable: true),
                    prev_hash = table.Column<string>(type: "TEXT", nullable: false),
                    row_hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_return", x => x.id);
                    table.CheckConstraint("ck_sale_return_refund_method", "refund_method IN ('CASH','CARD','CREDIT_NOTE','EXCHANGE','ON_ACCOUNT')");
                    table.ForeignKey(
                        name: "fk_sale_return_app_user_authorised_by",
                        column: x => x.authorised_by,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_return_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_return_customer_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_return_sale_exchange_sale_id",
                        column: x => x.exchange_sale_id,
                        principalTable: "sale",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_return_sale_original_sale_id",
                        column: x => x.original_sale_id,
                        principalTable: "sale",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_return_shift_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shift",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stock_take_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    stock_take_id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    system_qty = table.Column<long>(type: "INTEGER", nullable: false),
                    counted_qty = table.Column<long>(type: "INTEGER", nullable: true),
                    variance = table.Column<long>(type: "INTEGER", nullable: true),
                    counted_at = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_take_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_take_line_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_take_line_stock_take_stock_take_id",
                        column: x => x.stock_take_id,
                        principalTable: "stock_take",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "product_supplier",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_id = table.Column<long>(type: "INTEGER", nullable: false),
                    supplier_id = table.Column<long>(type: "INTEGER", nullable: false),
                    supplier_ref = table.Column<string>(type: "TEXT", nullable: true),
                    last_cost = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_supplier", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_supplier_product_product_id",
                        column: x => x.product_id,
                        principalTable: "product",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_product_supplier_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "supplier",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "purchase_order",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    po_no = table.Column<string>(type: "TEXT", nullable: false),
                    supplier_id = table.Column<long>(type: "INTEGER", nullable: false),
                    ordered_at = table.Column<string>(type: "TEXT", nullable: false),
                    expected_at = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order", x => x.id);
                    table.CheckConstraint("ck_purchase_order_status", "status IN ('DRAFT','SENT','PARTIAL','RECEIVED','CANCELLED')");
                    table.ForeignKey(
                        name: "fk_purchase_order_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_purchase_order_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "supplier",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "credit_note",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    sale_return_id = table.Column<long>(type: "INTEGER", nullable: false),
                    customer_id = table.Column<long>(type: "INTEGER", nullable: true),
                    amount_issued = table.Column<long>(type: "INTEGER", nullable: false),
                    amount_remaining = table.Column<long>(type: "INTEGER", nullable: false),
                    issued_at = table.Column<string>(type: "TEXT", nullable: false),
                    expires_on = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note", x => x.id);
                    table.CheckConstraint("ck_credit_note_status", "status IN ('ACTIVE','SPENT','EXPIRED','VOID')");
                    table.ForeignKey(
                        name: "fk_credit_note_customer_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customer",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_credit_note_sale_return_sale_return_id",
                        column: x => x.sale_return_id,
                        principalTable: "sale_return",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "sale_return_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    sale_return_id = table.Column<long>(type: "INTEGER", nullable: false),
                    sale_line_id = table.Column<long>(type: "INTEGER", nullable: true),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    qty_base = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_price = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    tax = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    line_refund = table.Column<long>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    disposition = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sale_return_line", x => x.id);
                    table.CheckConstraint("ck_sale_return_line_disposition", "disposition IN ('SELLABLE','DAMAGED')");
                    table.CheckConstraint("ck_sale_return_line_qty_base", "qty_base > 0");
                    table.ForeignKey(
                        name: "fk_sale_return_line_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_return_line_sale_line_sale_line_id",
                        column: x => x.sale_line_id,
                        principalTable: "sale_line",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sale_return_line_sale_return_sale_return_id",
                        column: x => x.sale_return_id,
                        principalTable: "sale_return",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    grn_no = table.Column<string>(type: "TEXT", nullable: false),
                    supplier_id = table.Column<long>(type: "INTEGER", nullable: false),
                    purchase_order_id = table.Column<long>(type: "INTEGER", nullable: true),
                    supplier_inv_no = table.Column<string>(type: "TEXT", nullable: true),
                    received_at = table.Column<string>(type: "TEXT", nullable: false),
                    subtotal = table.Column<long>(type: "INTEGER", nullable: false),
                    tax = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    other_cost = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    total = table.Column<long>(type: "INTEGER", nullable: false),
                    user_id = table.Column<long>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipt_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_goods_receipt_purchase_order_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_order",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_goods_receipt_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "supplier",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    purchase_order_id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    qty = table.Column<long>(type: "INTEGER", nullable: false),
                    uom_id = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    qty_received_base = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_order_line_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_purchase_order_line_purchase_order_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_order",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_purchase_order_line_uom_uom_id",
                        column: x => x.uom_id,
                        principalTable: "uom",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "credit_note_redemption",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    credit_note_id = table.Column<long>(type: "INTEGER", nullable: false),
                    sale_id = table.Column<long>(type: "INTEGER", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    redeemed_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_redemption", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_note_redemption_credit_note_credit_note_id",
                        column: x => x.credit_note_id,
                        principalTable: "credit_note",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_credit_note_redemption_sale_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sale",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_line",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false),
                    goods_receipt_id = table.Column<long>(type: "INTEGER", nullable: false),
                    product_variant_id = table.Column<long>(type: "INTEGER", nullable: false),
                    qty = table.Column<long>(type: "INTEGER", nullable: false),
                    uom_id = table.Column<long>(type: "INTEGER", nullable: false),
                    qty_base = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_cost_base = table.Column<long>(type: "INTEGER", nullable: false),
                    tax = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    line_total = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipt_line_goods_receipt_goods_receipt_id",
                        column: x => x.goods_receipt_id,
                        principalTable: "goods_receipt",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_goods_receipt_line_product_variant_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variant",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_goods_receipt_line_uom_uom_id",
                        column: x => x.uom_id,
                        principalTable: "uom",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_barcode_variant",
                table: "barcode",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "ux_barcode",
                table: "barcode",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_brand_name",
                table: "brand",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_category_name_parent",
                table: "category",
                columns: new[] { "name", "parent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_credit_note_number",
                table: "credit_note",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_phone",
                table: "customer",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ux_grn_no",
                table: "goods_receipt",
                column: "grn_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grn_line_grn",
                table: "goods_receipt_line",
                column: "goods_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_tier_lookup",
                table: "price_tier",
                columns: new[] { "product_variant_id", "tier", "min_qty" });

            migrationBuilder.CreateIndex(
                name: "ux_product_supplier",
                table: "product_supplier",
                columns: new[] { "product_id", "supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_product_uom",
                table: "product_uom",
                columns: new[] { "product_id", "uom_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_po_no",
                table: "purchase_order",
                column: "po_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_date",
                table: "sale_return",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "ix_return_sale",
                table: "sale_return",
                column: "original_sale_id");

            migrationBuilder.CreateIndex(
                name: "ux_return_no",
                table: "sale_return",
                column: "return_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_take_line_take",
                table: "stock_take_line",
                column: "stock_take_id");

            // Raw SQL, because EF cannot express a trigger. Nothing here touches `product`, and
            // that is deliberate: this migration must stay one transaction, so the `product`
            // rebuild that the category and brand foreign keys cost lives on its own in
            // ProductForeignKeys0003 (see that migration for why the split is not cosmetic).
            //
            // The append-only triggers land in the same migration as the tables they protect, on
            // purpose: `cash_movement`, `sale_return` and `sale_return_line` must never exist
            // unprotected, not even between two migrations of one upgrade.
            CreateAppendOnlyTriggers(migrationBuilder);
            CreateCategoryDepthTriggers(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_setting");

            migrationBuilder.DropTable(
                name: "backup_record");

            migrationBuilder.DropTable(
                name: "barcode");

            migrationBuilder.DropTable(
                name: "brand");

            migrationBuilder.DropTable(
                name: "cash_movement");

            migrationBuilder.DropTable(
                name: "category");

            migrationBuilder.DropTable(
                name: "credit_note_redemption");

            migrationBuilder.DropTable(
                name: "daily_product_summary");

            migrationBuilder.DropTable(
                name: "daily_sales_summary");

            migrationBuilder.DropTable(
                name: "goods_receipt_line");

            migrationBuilder.DropTable(
                name: "held_bill");

            migrationBuilder.DropTable(
                name: "price_change_log");

            migrationBuilder.DropTable(
                name: "price_tier");

            migrationBuilder.DropTable(
                name: "product_supplier");

            migrationBuilder.DropTable(
                name: "product_uom");

            migrationBuilder.DropTable(
                name: "purchase_order_line");

            migrationBuilder.DropTable(
                name: "sale_return_line");

            migrationBuilder.DropTable(
                name: "stock_take_line");

            migrationBuilder.DropTable(
                name: "credit_note");

            migrationBuilder.DropTable(
                name: "goods_receipt");

            migrationBuilder.DropTable(
                name: "stock_take");

            migrationBuilder.DropTable(
                name: "sale_return");

            migrationBuilder.DropTable(
                name: "purchase_order");

            migrationBuilder.DropTable(
                name: "customer");

            migrationBuilder.DropTable(
                name: "supplier");
        }

        /// <summary>
        /// The append-only triggers for the three tables docs/01_DATA_MODEL.md §8 lists as
        /// append-only but whose tables only arrive here: <c>cash_movement</c>,
        /// <c>sale_return</c> and <c>sale_return_line</c> (CLAUDE.md invariant 5).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Written out literally and not shared with <c>Skeleton0001</c> or with any later
        /// migration. Migrations are immutable history: a shared SQL constant edited in a later
        /// task would retroactively change what this one did. <c>Data/AppendOnlyTables.cs</c>
        /// holds the expected trigger <em>names</em> only, and that is what the survival check
        /// compares against.
        /// </para>
        /// <para>
        /// None of the three has a column-scoped exception. A cash movement is money in or out of
        /// the drawer; a return and its lines are a document that, once written, is the evidence.
        /// Where a sale can be cancelled and a shift can be closed, there is no correcting update
        /// to any of these - a mistake is fixed with another document, never by editing this one.
        /// </para>
        /// <para>
        /// <c>IS NOT</c> is not needed here because there is no <c>WHEN</c> guard: these tables
        /// refuse every update and every delete outright.
        /// </para>
        /// </remarks>
        private static void CreateAppendOnlyTriggers(MigrationBuilder migrationBuilder)
        {
            // ---- cash_movement --------------------------------------------------------------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_cash_movement_no_update
BEFORE UPDATE ON cash_movement
BEGIN SELECT RAISE(ABORT, 'cash_movement is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_cash_movement_no_delete
BEFORE DELETE ON cash_movement
BEGIN SELECT RAISE(ABORT, 'cash_movement is append-only'); END;");

            // ---- sale_return: hash chained, like sale ----------------------------------------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_return_no_update
BEFORE UPDATE ON sale_return
BEGIN SELECT RAISE(ABORT, 'sale_return is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_return_no_delete
BEFORE DELETE ON sale_return
BEGIN SELECT RAISE(ABORT, 'sale_return is append-only'); END;");

            // ---- sale_return_line ------------------------------------------------------------
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_return_line_no_update
BEFORE UPDATE ON sale_return_line
BEGIN SELECT RAISE(ABORT, 'sale_return_line is append-only'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_return_line_no_delete
BEFORE DELETE ON sale_return_line
BEGIN SELECT RAISE(ABORT, 'sale_return_line is append-only'); END;");

            // FR-8.5, AC-11 for the return document, the same rule Skeleton0001 put on `sale`:
            // takings and refunds may not be posted into a shift that has been counted and closed.
            // `IS NOT 'OPEN'` rather than `<> 'OPEN'` so a shift_id that matches no row - where
            // the subquery returns NULL - is refused too, instead of slipping through on a NULL
            // WHEN clause.
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_sale_return_shift_open
BEFORE INSERT ON sale_return
WHEN (SELECT status FROM shift WHERE id = new.shift_id) IS NOT 'OPEN'
BEGIN SELECT RAISE(ABORT, 'cannot post into a closed shift'); END;");
        }

        /// <summary>
        /// FR-2.20: the category tree is two levels deep and no more. A category whose
        /// <c>parent_id</c> is not null may not itself be a parent.
        /// </summary>
        /// <remarks>
        /// Two triggers rather than one, because the ways to break the rule differ by statement.
        /// An INSERT can only reach for a parent that is already a child. An UPDATE can also push
        /// a category that already has children underneath another one, and can point a category
        /// at itself - which the "is my parent a child" test alone would not catch, because a
        /// BEFORE UPDATE trigger reads the row as it stands before the change.
        /// </remarks>
        private static void CreateCategoryDepthTriggers(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_category_two_levels_insert
BEFORE INSERT ON category
WHEN new.parent_id IS NOT NULL
 AND (new.parent_id = new.id
   OR (SELECT parent_id FROM category WHERE id = new.parent_id) IS NOT NULL)
BEGIN SELECT RAISE(ABORT, 'category: two levels only (FR-2.20)'); END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_category_two_levels_update
BEFORE UPDATE OF parent_id ON category
WHEN new.parent_id IS NOT NULL
 AND (new.parent_id = new.id
   OR (SELECT parent_id FROM category WHERE id = new.parent_id) IS NOT NULL
   OR EXISTS (SELECT 1 FROM category WHERE parent_id = new.id))
BEGIN SELECT RAISE(ABORT, 'category: two levels only (FR-2.20)'); END;");
        }
    }
}
