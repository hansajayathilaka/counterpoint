using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// What the migration chain actually put in the file: foreign keys, CHECK constraints, the
/// indexes docs/01_DATA_MODEL.md §12 names, and the storage rules of §1 (DM-03, DM-04).
/// </summary>
/// <remarks>
/// The enum theories here are the conformance check §10 asks for. They are written against §10's
/// token lists rather than generated from C# enums because there are no C# enums yet: mirroring
/// them in <c>Domain/Enums</c> needs a name-to-token mapping (<c>NonInventory</c> is
/// <c>NON_INVENTORY</c>, and CA1707 forbids the underscore in an identifier), and that mapping
/// belongs with the domain types in P1-T05. Until then §10 is transcribed here, member by member.
/// </remarks>
public sealed class SchemaConformanceTests
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintForeignKey = 787;
    private const int SqliteConstraintCheck = 275;
    private const int SqliteConstraintUnique = 2067;

    /// <summary>
    /// Every foreign key in the schema, as <c>table.column -&gt; referenced table</c>. Nothing
    /// else may be one, and in particular neither of the two columns docs/01_DATA_MODEL.md §13
    /// still lists as dangling - <c>sale.customer_id</c> and <c>payment.sale_return_id</c> - may
    /// appear here.
    /// </summary>
    private static readonly string[] DocumentedForeignKeys =
    [
        "app_setting.updated_by -> app_user",
        "audit_log.user_id -> app_user",
        "barcode.product_variant_id -> product_variant",
        "cash_movement.shift_id -> shift",
        "cash_movement.user_id -> app_user",
        "category.parent_id -> category",
        "credit_note.customer_id -> customer",
        "credit_note.sale_return_id -> sale_return",
        "credit_note_redemption.credit_note_id -> credit_note",
        "credit_note_redemption.sale_id -> sale",
        "daily_product_summary.product_variant_id -> product_variant",
        "goods_receipt.purchase_order_id -> purchase_order",
        "goods_receipt.supplier_id -> supplier",
        "goods_receipt.user_id -> app_user",
        "goods_receipt_line.goods_receipt_id -> goods_receipt",
        "goods_receipt_line.product_variant_id -> product_variant",
        "goods_receipt_line.uom_id -> uom",
        "held_bill.user_id -> app_user",
        "payment.sale_id -> sale",
        "price_change_log.product_variant_id -> product_variant",
        "price_change_log.user_id -> app_user",
        "price_tier.product_variant_id -> product_variant",
        "product.base_uom_id -> uom",
        "product.brand_id -> brand",
        "product.category_id -> category",
        "product.tax_class_id -> tax_class",
        "product_supplier.product_id -> product",
        "product_supplier.supplier_id -> supplier",
        "product_uom.product_id -> product",
        "product_uom.uom_id -> uom",
        "product_variant.product_id -> product",
        "purchase_order.supplier_id -> supplier",
        "purchase_order.user_id -> app_user",
        "purchase_order_line.product_variant_id -> product_variant",
        "purchase_order_line.purchase_order_id -> purchase_order",
        "purchase_order_line.uom_id -> uom",
        "sale.cancelled_by -> app_user",
        "sale.shift_id -> shift",
        "sale.user_id -> app_user",
        "sale_line.product_variant_id -> product_variant",
        "sale_line.sale_id -> sale",
        "sale_line.uom_id -> uom",
        "sale_return.authorised_by -> app_user",
        "sale_return.customer_id -> customer",
        "sale_return.exchange_sale_id -> sale",
        "sale_return.original_sale_id -> sale",
        "sale_return.shift_id -> shift",
        "sale_return.user_id -> app_user",
        "sale_return_line.product_variant_id -> product_variant",
        "sale_return_line.sale_line_id -> sale_line",
        "sale_return_line.sale_return_id -> sale_return",
        "shift.closed_by -> app_user",
        "shift.user_id -> app_user",
        "stock_balance.product_variant_id -> product_variant",
        "stock_movement.product_variant_id -> product_variant",
        "stock_movement.user_id -> app_user",
        "stock_take.user_id -> app_user",
        "stock_take_line.product_variant_id -> product_variant",
        "stock_take_line.stock_take_id -> stock_take",
    ];

    /// <summary>
    /// Every table docs/01_DATA_MODEL.md areas A to F define. <c>product_search</c> and its four
    /// FTS5 shadow tables are excluded wherever this list is used: they are the virtual table's
    /// own storage, created and named by SQLite, not schema anyone wrote.
    /// </summary>
    private static readonly string[] DocumentedTables =
    [
        "app_setting", "app_user", "audit_log", "backup_record", "barcode", "brand",
        "cash_movement", "category", "credit_note", "credit_note_redemption", "customer",
        "daily_product_summary", "daily_sales_summary", "goods_receipt", "goods_receipt_line",
        "held_bill", "number_sequence", "payment", "price_change_log", "price_tier", "print_job",
        "product", "product_supplier", "product_uom", "product_variant", "purchase_order",
        "purchase_order_line", "sale", "sale_line", "sale_return", "sale_return_line",
        "schema_version", "shift", "stock_balance", "stock_movement", "stock_take",
        "stock_take_line", "supplier", "tax_class", "uom",
    ];

    /// <summary>Every index docs/01_DATA_MODEL.md §12 names, and nothing else.</summary>
    private static readonly string[] DocumentedIndexes =
    [
        "ix_audit_entity", "ix_audit_time",
        "ix_barcode_variant",
        "ix_customer_phone",
        "ix_grn_line_grn",
        "ix_movement_ref", "ix_movement_time", "ix_movement_variant_time",
        "ix_payment_return", "ix_payment_sale",
        "ix_price_tier_lookup",
        "ix_print_pending",
        "ix_product_active", "ix_product_brand", "ix_product_category",
        "ix_return_date", "ix_return_sale",
        "ix_sale_cust", "ix_sale_date", "ix_sale_line_sale", "ix_sale_line_variant",
        "ix_sale_shift", "ix_sale_soldat",
        "ix_stock_balance_low",
        "ix_stock_take_line_take",
        "ix_variant_product",
        "ux_app_user_username", "ux_barcode", "ux_brand_name", "ux_category_name_parent",
        "ux_credit_note_number", "ux_grn_no", "ux_one_open_shift", "ux_po_no", "ux_product_code",
        "ux_product_supplier", "ux_product_uom", "ux_return_no", "ux_sale_bill_no",
        "ux_sale_line_no", "ux_shift_no", "ux_tax_class_name", "ux_uom_name", "ux_variant_sku",
    ];

    /// <summary>DM-04: no orphan lines, enforced by the database rather than by the caller.</summary>
    [Fact]
    public async Task DM_04_ASaleLineWithNoSaleIsRefused()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        var exception = await database.ExecuteExpectingAbortAsync(
            """
            INSERT INTO sale_line (id, sale_id, line_no, product_variant_id, description, qty,
                                   uom_id, qty_base, unit_price, line_total)
            VALUES (99, 4242, 1, 1, 'Orphan', 10000, 1, 10000, 1, 1);
            """);

        exception.SqliteErrorCode.Should().Be(SqliteConstraint);
        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintForeignKey);
    }

    /// <summary>
    /// DM-04 for the rest of them. One foreign key tested by hand proves the pragma is on; this
    /// proves the schema carries exactly the references docs/01_DATA_MODEL.md draws and no
    /// others, so a reference to a table that does not exist yet cannot hide in it.
    /// </summary>
    [Fact]
    public async Task DM_04_TheSchemaCarriesExactlyTheDocumentedForeignKeys()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var actual = await database.ColumnAsync(
            """
            SELECT tables.name || '.' || keys."from" || ' -> ' || keys."table"
              FROM sqlite_schema AS tables
              JOIN pragma_foreign_key_list(tables.name) AS keys
             WHERE tables.type = 'table'
               AND tables.name NOT LIKE 'sqlite_%'
               AND tables.name NOT LIKE '\_\_EF%' ESCAPE '\';
            """);

        actual.Should().BeEquivalentTo(DocumentedForeignKeys);

        // Every table a foreign key points at has to exist. A REFERENCES to a missing table is
        // legal DDL that only fails at INSERT time, which is the landmine §13 is about.
        var referenced = DocumentedForeignKeys
            .Select(entry => entry.Split(" -> ")[1])
            .Distinct(StringComparer.Ordinal);

        var tables = await database.ColumnAsync(
            "SELECT name FROM sqlite_schema WHERE type = 'table';");

        referenced.Should().BeSubsetOf(tables);
    }

    /// <summary>
    /// Two more of the sixteen, exercised rather than read out of <c>sqlite_schema</c>. The stock
    /// ledger is CLAUDE.md invariant 3's source of truth, so a movement against a variant that was
    /// never catalogued would corrupt every valuation built on it.
    /// </summary>
    [Theory]
    [InlineData(
        "INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost," +
        " ref_doc_type, balance_after, user_id, occurred_at) " +
        "VALUES (99, 4242, 'GRN', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData(
        "INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost," +
        " ref_doc_type, balance_after, user_id, occurred_at) " +
        "VALUES (99, 1, 'GRN', 1, 1, 'X', 1, 4242, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData(
        "INSERT INTO payment (id, sale_id, tender_type, amount, paid_at) " +
        "VALUES (99, 4242, 'CASH', 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData(
        "INSERT INTO sale_line (id, sale_id, line_no, product_variant_id, description, qty," +
        " uom_id, qty_base, unit_price, line_total) " +
        "VALUES (99, 1, 2, 4242, 'Ghost variant', 10000, 1, 10000, 1, 1);")]
    public async Task DM_04_ARowPointingAtSomethingThatIsNotThereIsRefused(string sql)
    {
        await using var database = await MigratedDatabase.CreateAsync();

        var exception = await database.ExecuteExpectingAbortAsync(sql);

        exception.SqliteErrorCode.Should().Be(SqliteConstraint);
        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintForeignKey);
    }

    /// <summary>
    /// CLAUDE.md invariant 4: document numbers come from <c>number_sequence</c>, never from a
    /// rowid. AUTOINCREMENT would also add a <c>sqlite_sequence</c> write to every insert on the
    /// sale path.
    /// </summary>
    [Fact]
    public async Task DM_03_NoTableUsesAutoincrement()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        (await database.CountAsync(
            "SELECT count(*) FROM sqlite_schema WHERE sql LIKE '%AUTOINCREMENT%';"))
            .Should().Be(0);

        (await database.CountAsync(
            "SELECT count(*) FROM sqlite_schema WHERE name = 'sqlite_sequence';"))
            .Should().Be(0);
    }

    /// <summary>C-01: at most one open shift, and the database is what makes it true.</summary>
    [Fact]
    public async Task C_01_ASecondOpenShiftIsRefused()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        var exception = await database.ExecuteExpectingAbortAsync(
            """
            INSERT INTO shift (id, shift_no, user_id, opened_at, business_date, opening_float, status)
            VALUES (3, 'SH-000002', 1, '2026-09-04T14:00:00.000+05:30', '2026-09-04', 0, 'OPEN');
            """);

        exception.SqliteErrorCode.Should().Be(SqliteConstraint);
        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintUnique);
        exception.Message.Should().Contain("UNIQUE constraint failed: shift.status");

        // A CLOSED shift alongside the open one is fine: the index is filtered, not a plain
        // unique constraint on status.
        await database.ExecuteAsync(
            """
            INSERT INTO shift (id, shift_no, user_id, opened_at, business_date, opening_float,
                               closed_at, counted_cash, expected_cash, variance, status, closed_by)
            VALUES (4, 'SH-000003', 1, '2026-09-02T08:00:00.000+05:30', '2026-09-02', 0,
                    '2026-09-02T18:00:00.000+05:30', 0, 0, 0, 'CLOSED', 1);
            """);
    }

    /// <summary>The partial index must carry its filter, or the outbox scan degrades to a table scan.</summary>
    [Fact]
    public async Task NFR_P3_ThePrintOutboxIndexIsPartial()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        (await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'index' AND name = 'ix_print_pending';"))
            .Should().Contain("WHERE status = 'PENDING'");

        (await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'index' AND name = 'ux_one_open_shift';"))
            .Should().Contain("WHERE status = 'OPEN'");
    }

    /// <summary>
    /// Every index in the file is one somebody chose. EF's ForeignKeyIndexConvention is removed,
    /// so an unexpected <c>IX_*</c> here means it came back.
    /// </summary>
    [Fact]
    public async Task EveryIndexIsOneTheDataModelNames()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        var indexes = await database.ColumnAsync(
            "SELECT name FROM sqlite_schema WHERE type = 'index' AND name NOT LIKE 'sqlite_%' ORDER BY name;");

        indexes.Should().BeEquivalentTo(DocumentedIndexes);
    }

    /// <summary>
    /// docs/01_DATA_MODEL.md §10 is the single source of truth for these enumerations, and the
    /// CHECK constraints must not drift from it. Every documented member has to be accepted -
    /// a constraint that only rejects is half a constraint, and the half that is missing is the
    /// one that stops a legal tender type being refused at the till.
    /// </summary>
    [Theory]

    // ProductType.
    [InlineData(
        "INSERT INTO product (id, code, name, base_uom_id, type, tax_class_id, created_at, updated_at)" +
        " VALUES (91, 'E-1', 'E', 1, 'STANDARD', 1, '2026-09-04T08:00:00.000+05:30', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData(
        "INSERT INTO product (id, code, name, base_uom_id, type, tax_class_id, created_at, updated_at)" +
        " VALUES (92, 'E-2', 'E', 1, 'DECIMAL', 1, '2026-09-04T08:00:00.000+05:30', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData(
        "INSERT INTO product (id, code, name, base_uom_id, type, tax_class_id, created_at, updated_at)" +
        " VALUES (93, 'E-3', 'E', 1, 'SERVICE', 1, '2026-09-04T08:00:00.000+05:30', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData(
        "INSERT INTO product (id, code, name, base_uom_id, type, tax_class_id, created_at, updated_at)" +
        " VALUES (94, 'E-4', 'E', 1, 'NON_INVENTORY', 1, '2026-09-04T08:00:00.000+05:30', '2026-09-04T08:00:00.000+05:30');")]

    // Role.
    [InlineData(
        "INSERT INTO app_user (id, username, display_name, password_hash, role, created_at)" +
        " VALUES (91, 'a', 'A', 'h', 'CASHIER', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData(
        "INSERT INTO app_user (id, username, display_name, password_hash, role, created_at)" +
        " VALUES (92, 'b', 'B', 'h', 'OWNER', '2026-09-04T08:00:00.000+05:30');")]

    // SaleStatus. Both are insertable; the COMPLETED -> CANCELLED rule is a trigger, not a CHECK.
    [InlineData(
        "INSERT INTO sale (id, bill_no, sold_at, business_date, user_id, shift_id, subtotal, total," +
        " status, prev_hash, row_hash) VALUES (91, 'B-91', '2026-09-04T10:00:00.000+05:30'," +
        " '2026-09-04', 1, 1, 1, 1, 'COMPLETED', 'x', 'y');")]
    [InlineData(
        "INSERT INTO sale (id, bill_no, sold_at, business_date, user_id, shift_id, subtotal, total," +
        " status, prev_hash, row_hash) VALUES (92, 'B-92', '2026-09-04T10:00:00.000+05:30'," +
        " '2026-09-04', 1, 1, 1, 1, 'CANCELLED', 'x', 'y');")]

    // MovementType, all eleven.
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (91, 1, 'GRN', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (92, 1, 'SALE', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (93, 1, 'RETURN_IN', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (94, 1, 'ADJUSTMENT', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (95, 1, 'DAMAGE', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (96, 1, 'STOCK_TAKE', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (97, 1, 'BULK_BREAK_OUT', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (98, 1, 'BULK_BREAK_IN', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (99, 1, 'OPENING', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (100, 1, 'TRANSFER_OUT', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost, ref_doc_type, balance_after, user_id, occurred_at) VALUES (101, 1, 'TRANSFER_IN', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');")]

    // TenderType, all six.
    [InlineData("INSERT INTO payment (id, sale_id, tender_type, amount, paid_at) VALUES (91, 1, 'CASH', 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO payment (id, sale_id, tender_type, amount, paid_at) VALUES (92, 1, 'CARD', 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO payment (id, sale_id, tender_type, amount, paid_at) VALUES (93, 1, 'BANK_TRANSFER', 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO payment (id, sale_id, tender_type, amount, paid_at) VALUES (94, 1, 'CREDIT_NOTE', 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO payment (id, sale_id, tender_type, amount, paid_at) VALUES (95, 1, 'ON_ACCOUNT', 1, '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO payment (id, sale_id, tender_type, amount, paid_at) VALUES (96, 1, 'CHEQUE', 1, '2026-09-04T08:00:00.000+05:30');")]

    // PrintStatus and PrintDocType.
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (91, 'SALE', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (92, 'RETURN', x'00', 'PRINTED', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (93, 'CREDIT_NOTE', x'00', 'FAILED', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (94, 'X_REPORT', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (95, 'Z_REPORT', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (96, 'GRN', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (97, 'PO', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (98, 'STOCK_TAKE', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (99, 'LABEL', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO print_job (id, doc_type, payload, status, created_at) VALUES (100, 'CASH_SLIP', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');")]

    // number_sequence doc types, all eight.
    [InlineData("INSERT INTO number_sequence (doc_type, prefix, pattern, next_val) VALUES ('RETURN', 'RTN-', '{n}', 1);")]
    [InlineData("INSERT INTO number_sequence (doc_type, prefix, pattern, next_val) VALUES ('CREDIT_NOTE', 'CN-', '{n}', 1);")]
    [InlineData("INSERT INTO number_sequence (doc_type, prefix, pattern, next_val) VALUES ('GRN', 'GRN-', '{n}', 1);")]
    [InlineData("INSERT INTO number_sequence (doc_type, prefix, pattern, next_val) VALUES ('PO', 'PO-', '{n}', 1);")]
    [InlineData("INSERT INTO number_sequence (doc_type, prefix, pattern, next_val) VALUES ('SHIFT', 'SH-', '{n}', 1);")]
    [InlineData("INSERT INTO number_sequence (doc_type, prefix, pattern, next_val) VALUES ('STOCK_TAKE', 'ST-', '{n}', 1);")]
    [InlineData("INSERT INTO number_sequence (doc_type, prefix, pattern, next_val) VALUES ('QUOTE', 'QT-', '{n}', 1);")]

    // uom.decimal_places, both ends of the documented range.
    [InlineData("INSERT INTO uom (id, name, symbol, decimal_places) VALUES (91, 'Zero', 'z', 0);")]
    [InlineData("INSERT INTO uom (id, name, symbol, decimal_places) VALUES (92, 'Four', 'f', 4);")]

    // PriceTierName, both.
    [InlineData("INSERT INTO price_tier (id, product_variant_id, tier, min_qty, price) VALUES (91, 1, 'RETAIL', 0, 1);")]
    [InlineData("INSERT INTO price_tier (id, product_variant_id, tier, min_qty, price) VALUES (92, 1, 'TRADE', 0, 1);")]

    // customer.type, both.
    [InlineData("INSERT INTO customer (id, name, type, created_at) VALUES (91, 'Walk-in', 'RETAIL', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO customer (id, name, type, created_at) VALUES (92, 'Trade Co', 'TRADE', '2026-09-04T08:00:00.000+05:30');")]

    // purchase_order.status, all five.
    [InlineData("INSERT INTO purchase_order (id, po_no, supplier_id, ordered_at, status, user_id) VALUES (91, 'PO-91', 1, '2026-09-04T08:00:00.000+05:30', 'DRAFT', 1);")]
    [InlineData("INSERT INTO purchase_order (id, po_no, supplier_id, ordered_at, status, user_id) VALUES (92, 'PO-92', 1, '2026-09-04T08:00:00.000+05:30', 'SENT', 1);")]
    [InlineData("INSERT INTO purchase_order (id, po_no, supplier_id, ordered_at, status, user_id) VALUES (93, 'PO-93', 1, '2026-09-04T08:00:00.000+05:30', 'PARTIAL', 1);")]
    [InlineData("INSERT INTO purchase_order (id, po_no, supplier_id, ordered_at, status, user_id) VALUES (94, 'PO-94', 1, '2026-09-04T08:00:00.000+05:30', 'RECEIVED', 1);")]
    [InlineData("INSERT INTO purchase_order (id, po_no, supplier_id, ordered_at, status, user_id) VALUES (95, 'PO-95', 1, '2026-09-04T08:00:00.000+05:30', 'CANCELLED', 1);")]

    // stock_take.status, all three.
    [InlineData("INSERT INTO stock_take (id, scope, started_at, status, user_id) VALUES (91, 'ALL', '2026-09-04T08:00:00.000+05:30', 'OPEN', 1);")]
    [InlineData("INSERT INTO stock_take (id, scope, started_at, status, user_id) VALUES (92, 'ALL', '2026-09-04T08:00:00.000+05:30', 'POSTED', 1);")]
    [InlineData("INSERT INTO stock_take (id, scope, started_at, status, user_id) VALUES (93, 'ALL', '2026-09-04T08:00:00.000+05:30', 'ABANDONED', 1);")]

    // RefundMethod, all five.
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (91, 'RTN-91', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CASH', 'x', 'y');")]
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (92, 'RTN-92', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CARD', 'x', 'y');")]
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (93, 'RTN-93', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CREDIT_NOTE', 'x', 'y');")]
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (94, 'RTN-94', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'EXCHANGE', 'x', 'y');")]
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (95, 'RTN-95', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'ON_ACCOUNT', 'x', 'y');")]

    // Disposition, both. The return document they hang off is created by the statement itself.
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (81, 'RTN-81', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CASH', 'x', 'y'); INSERT INTO sale_return_line (id, sale_return_id, product_variant_id, qty_base, unit_price, unit_cost, line_refund, reason, disposition) VALUES (81, 81, 1, 10000, 1, 1, 1, 'Faulty', 'SELLABLE');")]
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (82, 'RTN-82', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CASH', 'x', 'y'); INSERT INTO sale_return_line (id, sale_return_id, product_variant_id, qty_base, unit_price, unit_cost, line_refund, reason, disposition) VALUES (82, 82, 1, 10000, 1, 1, 1, 'Broken', 'DAMAGED');")]

    // credit_note.status, all four. Each needs a return to hang off.
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (71, 'RTN-71', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CREDIT_NOTE', 'x', 'y'); INSERT INTO credit_note (id, number, sale_return_id, amount_issued, amount_remaining, issued_at, status) VALUES (71, 'CN-71', 71, 1, 1, '2026-09-04T10:00:00.000+05:30', 'ACTIVE');")]
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (72, 'RTN-72', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CREDIT_NOTE', 'x', 'y'); INSERT INTO credit_note (id, number, sale_return_id, amount_issued, amount_remaining, issued_at, status) VALUES (72, 'CN-72', 72, 1, 0, '2026-09-04T10:00:00.000+05:30', 'SPENT');")]
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (73, 'RTN-73', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CREDIT_NOTE', 'x', 'y'); INSERT INTO credit_note (id, number, sale_return_id, amount_issued, amount_remaining, issued_at, status) VALUES (73, 'CN-73', 73, 1, 1, '2026-09-04T10:00:00.000+05:30', 'EXPIRED');")]
    [InlineData("INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id, subtotal, total_refund, refund_method, prev_hash, row_hash) VALUES (74, 'RTN-74', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1, 'CREDIT_NOTE', 'x', 'y'); INSERT INTO credit_note (id, number, sale_return_id, amount_issued, amount_remaining, issued_at, status) VALUES (74, 'CN-74', 74, 1, 1, '2026-09-04T10:00:00.000+05:30', 'VOID');")]

    // cash_movement.direction, both.
    [InlineData("INSERT INTO cash_movement (id, shift_id, direction, amount, reason, user_id, occurred_at) VALUES (91, 1, 'IN', 1, 'Float top-up', 1, '2026-09-04T10:00:00.000+05:30');")]
    [InlineData("INSERT INTO cash_movement (id, shift_id, direction, amount, reason, user_id, occurred_at) VALUES (92, 1, 'OUT', 1, 'Petty cash', 1, '2026-09-04T10:00:00.000+05:30');")]

    // app_setting.value_type, all five.
    [InlineData("INSERT INTO app_setting (key, value, value_type, updated_at) VALUES ('a', 'x', 'STRING', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO app_setting (key, value, value_type, updated_at) VALUES ('b', '1', 'INT', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO app_setting (key, value, value_type, updated_at) VALUES ('c', '10000', 'MONEY', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO app_setting (key, value, value_type, updated_at) VALUES ('d', '1', 'BOOL', '2026-09-04T08:00:00.000+05:30');")]
    [InlineData("INSERT INTO app_setting (key, value, value_type, updated_at) VALUES ('e', '{}', 'JSON', '2026-09-04T08:00:00.000+05:30');")]

    // backup_record.usb_status and cloud_status, every documented member of each.
    [InlineData("INSERT INTO backup_record (id, filename, taken_at, size_bytes, checksum, schema_ver, usb_status, cloud_status) VALUES (91, 'f', '2026-09-04T08:00:00.000+05:30', 1, 'c', 'v', 'NA', 'PENDING');")]
    [InlineData("INSERT INTO backup_record (id, filename, taken_at, size_bytes, checksum, schema_ver, usb_status, cloud_status) VALUES (92, 'f', '2026-09-04T08:00:00.000+05:30', 1, 'c', 'v', 'OK', 'OK');")]
    [InlineData("INSERT INTO backup_record (id, filename, taken_at, size_bytes, checksum, schema_ver, usb_status, cloud_status) VALUES (93, 'f', '2026-09-04T08:00:00.000+05:30', 1, 'c', 'v', 'FAILED', 'FAILED');")]
    [InlineData("INSERT INTO backup_record (id, filename, taken_at, size_bytes, checksum, schema_ver, usb_status, cloud_status) VALUES (94, 'f', '2026-09-04T08:00:00.000+05:30', 1, 'c', 'v', 'NA', 'SKIPPED');")]
    public async Task EveryDocumentedEnumMemberIsAccepted(string sql)
    {
        await using var database = await MigratedDatabase.CreateAsync();

        await database.ExecuteAsync(sql);
    }

    [Theory]
    [InlineData(
        "INSERT INTO app_user (id, username, display_name, password_hash, role, created_at) " +
        "VALUES (9, 'x', 'X', 'h', 'MANAGER', '2026-09-04T08:00:00.000+05:30');",
        "ck_app_user_role")]
    [InlineData(
        "INSERT INTO uom (id, name, symbol, decimal_places) VALUES (9, 'Nine', 'n', 9);",
        "ck_uom_decimal_places")]
    [InlineData(
        "INSERT INTO number_sequence (doc_type, prefix, pattern, next_val) " +
        "VALUES ('INVOICE', 'X-', '{n}', 1);",
        "ck_number_sequence_doc_type")]
    [InlineData(
        "INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost," +
        " ref_doc_type, balance_after, user_id, occurred_at) " +
        "VALUES (9, 1, 'SHRINKAGE', 1, 1, 'X', 1, 1, '2026-09-04T08:00:00.000+05:30');",
        "ck_stock_movement_movement_type")]
    [InlineData(
        "INSERT INTO payment (id, sale_id, sale_return_id, tender_type, amount, paid_at) " +
        "VALUES (9, 1, 1, 'CASH', 1, '2026-09-04T08:00:00.000+05:30');",
        "ck_payment_one_document")]
    [InlineData(
        "INSERT INTO payment (id, sale_id, sale_return_id, tender_type, amount, paid_at) " +
        "VALUES (9, NULL, NULL, 'CASH', 1, '2026-09-04T08:00:00.000+05:30');",
        "ck_payment_one_document")]
    [InlineData(
        "INSERT INTO payment (id, sale_id, tender_type, amount, paid_at) " +
        "VALUES (9, 1, 'BITCOIN', 1, '2026-09-04T08:00:00.000+05:30');",
        "ck_payment_tender_type")]
    [InlineData(
        "INSERT INTO print_job (id, doc_type, payload, status, created_at) " +
        "VALUES (9, 'SALE', x'00', 'QUEUED', '2026-09-04T08:00:00.000+05:30');",
        "ck_print_job_status")]
    [InlineData(
        "INSERT INTO print_job (id, doc_type, payload, status, created_at) " +
        "VALUES (9, 'POSTER', x'00', 'PENDING', '2026-09-04T08:00:00.000+05:30');",
        "ck_print_job_doc_type")]
    [InlineData(
        "INSERT INTO price_tier (id, product_variant_id, tier, min_qty, price) " +
        "VALUES (9, 1, 'WHOLESALE', 0, 1);",
        "ck_price_tier_tier")]
    [InlineData(
        "INSERT INTO customer (id, name, type, created_at) " +
        "VALUES (9, 'X', 'STAFF', '2026-09-04T08:00:00.000+05:30');",
        "ck_customer_type")]
    [InlineData(
        "INSERT INTO purchase_order (id, po_no, supplier_id, ordered_at, status, user_id) " +
        "VALUES (9, 'PO-9', 1, '2026-09-04T08:00:00.000+05:30', 'APPROVED', 1);",
        "ck_purchase_order_status")]
    [InlineData(
        "INSERT INTO stock_take (id, scope, started_at, status, user_id) " +
        "VALUES (9, 'ALL', '2026-09-04T08:00:00.000+05:30', 'CLOSED', 1);",
        "ck_stock_take_status")]
    [InlineData(
        "INSERT INTO sale_return (id, return_no, returned_at, business_date, user_id, shift_id," +
        " subtotal, total_refund, refund_method, prev_hash, row_hash) " +
        "VALUES (9, 'RTN-9', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1, 1, 1," +
        " 'GIFT_CARD', 'x', 'y');",
        "ck_sale_return_refund_method")]
    [InlineData(
        "INSERT INTO cash_movement (id, shift_id, direction, amount, reason, user_id, occurred_at) " +
        "VALUES (9, 1, 'SIDEWAYS', 1, 'r', 1, '2026-09-04T10:00:00.000+05:30');",
        "ck_cash_movement_direction")]

    // Not an enum, but the same argument: the sign lives in `direction`, so a negative amount
    // would net out of a Z report without anyone seeing it.
    [InlineData(
        "INSERT INTO cash_movement (id, shift_id, direction, amount, reason, user_id, occurred_at) " +
        "VALUES (9, 1, 'IN', -1, 'r', 1, '2026-09-04T10:00:00.000+05:30');",
        "ck_cash_movement_amount")]
    [InlineData(
        "INSERT INTO product_uom (id, product_id, uom_id, conversion_factor) VALUES (9, 1, 1, 0);",
        "ck_product_uom_conversion_factor")]
    [InlineData(
        "INSERT INTO app_setting (key, value, value_type, updated_at) " +
        "VALUES ('k', 'v', 'DECIMAL', '2026-09-04T08:00:00.000+05:30');",
        "ck_app_setting_value_type")]
    [InlineData(
        "INSERT INTO backup_record (id, filename, taken_at, size_bytes, checksum, schema_ver," +
        " usb_status, cloud_status) " +
        "VALUES (9, 'f', '2026-09-04T08:00:00.000+05:30', 1, 'c', 'v', 'MAYBE', 'OK');",
        "ck_backup_record_usb_status")]
    [InlineData(
        "INSERT INTO backup_record (id, filename, taken_at, size_bytes, checksum, schema_ver," +
        " usb_status, cloud_status) " +
        "VALUES (9, 'f', '2026-09-04T08:00:00.000+05:30', 1, 'c', 'v', 'OK', 'UPLOADING');",
        "ck_backup_record_cloud_status")]

    // ProductType. Every member of this one is accepted above; this is the other half.
    [InlineData(
        "INSERT INTO product (id, code, name, base_uom_id, type, tax_class_id, created_at, updated_at)" +
        " VALUES (9, 'E-9', 'E', 1, 'KIT', 1, '2026-09-04T08:00:00.000+05:30'," +
        " '2026-09-04T08:00:00.000+05:30');",
        "ck_product_type")]

    // SaleStatus. The COMPLETED -> CANCELLED rule is a trigger; this is the column constraint
    // underneath it, which is what stops a third status ever existing.
    [InlineData(
        "INSERT INTO sale (id, bill_no, sold_at, business_date, user_id, shift_id, subtotal, total," +
        " status, prev_hash, row_hash) VALUES (9, 'B-9', '2026-09-04T10:00:00.000+05:30'," +
        " '2026-09-04', 1, 1, 1, 1, 'VOIDED', 'x', 'y');",
        "ck_sale_status")]
    [InlineData(
        "INSERT INTO credit_note (id, number, sale_return_id, amount_issued, amount_remaining," +
        " issued_at, status) " +
        "VALUES (9, 'CN-9', 1, 1, 1, '2026-09-04T10:00:00.000+05:30', 'REDEEMED');",
        "ck_credit_note_status")]
    [InlineData(
        "INSERT INTO sale_return_line (id, sale_return_id, product_variant_id, qty_base," +
        " unit_price, unit_cost, line_refund, reason, disposition) " +
        "VALUES (9, 1, 1, 10000, 1, 1, 1, 'Faulty', 'SCRAP');",
        "ck_sale_return_line_disposition")]

    // Not an enum, but the same class of guard: a return line for nothing at all.
    [InlineData(
        "INSERT INTO sale_return_line (id, sale_return_id, product_variant_id, qty_base," +
        " unit_price, unit_cost, line_refund, reason, disposition) " +
        "VALUES (9, 1, 1, 0, 1, 1, 1, 'Faulty', 'SELLABLE');",
        "ck_sale_return_line_qty_base")]
    public async Task AnOutOfEnumValueIsRejectedByItsCheckConstraint(string sql, string constraint)
    {
        await using var database = await MigratedDatabase.CreateAsync();

        var exception = await database.ExecuteExpectingAbortAsync(sql);

        exception.SqliteErrorCode.Should().Be(SqliteConstraint);
        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintCheck);
        exception.Message.Should().Contain(constraint);
    }

    /// <summary>
    /// CLAUDE.md invariant 1. A <c>decimal</c> property would map to TEXT with no error to show
    /// for it, and money stored as text does not add up.
    /// </summary>
    [Fact]
    public async Task DM_01_NoMappedPropertyIsDecimalOrFloatingPoint()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var model = ModelOf(database);

        var offenders = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property =>
                property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?) ||
                property.ClrType == typeof(double) || property.ClrType == typeof(double?) ||
                property.ClrType == typeof(float) || property.ClrType == typeof(float?))
            .Select(property => property.DeclaringType.ShortName() + "." + property.Name)
            .ToList();

        offenders.Should().BeEmpty(
            "money and quantity are INTEGER scaled x10 000 (docs/01_DATA_MODEL.md §1)");
    }

    /// <summary>
    /// Only the three storage classes §1 allows. <c>product_search</c> and its shadow tables are
    /// excluded: an FTS5 virtual table declares its columns without a type, and SQLite - not this
    /// repository - decides what they hold.
    /// </summary>
    [Fact]
    public async Task DM_01_EveryColumnIsTextIntegerOrBlob()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var types = await database.ColumnAsync(
            """
            SELECT DISTINCT upper(info.type)
              FROM sqlite_schema AS tables
              JOIN pragma_table_info(tables.name) AS info
             WHERE tables.type = 'table'
               AND tables.name NOT LIKE 'sqlite_%'
               AND tables.name NOT LIKE 'product\_search%' ESCAPE '\'
               AND tables.name <> '__EFMigrationsHistory';
            """);

        types.Should().BeSubsetOf(["TEXT", "INTEGER", "BLOB"]);
    }

    /// <summary>
    /// DM-06 for a nullable timestamp. The global convention covers <c>DateTimeOffset</c>; this
    /// proves it reaches <c>DateTimeOffset?</c> too, which is where a missed converter would hide.
    /// </summary>
    [Fact]
    public async Task DM_06_ANullableTimestampRoundTripsWithItsOffset()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        (await database.ScalarAsync("SELECT cancelled_at FROM sale WHERE id = 1;")).Should().BeNull();

        await database.ExecuteAsync(
            """
            UPDATE sale
               SET status = 'CANCELLED',
                   cancelled_by = 1,
                   cancelled_at = '2026-09-03T14:22:31.123+05:30'
             WHERE id = 1;
            """);

        (await database.ScalarAsync("SELECT cancelled_at FROM sale WHERE id = 1;"))
            .Should().Be("2026-09-03T14:22:31.123+05:30");
    }

    /// <summary>
    /// Business dates are TEXT <c>YYYY-MM-DD</c>, not timestamps. Routing them through the
    /// timestamp converter would corrupt every rollup's grouping key.
    /// </summary>
    [Fact]
    public async Task DM_01_BusinessDatesAreStoredAsPlainDates()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        (await database.ScalarAsync("SELECT business_date FROM sale WHERE id = 1;"))
            .Should().Be(TradingDaySeed.SeededBusinessDate);

        (await database.ScalarAsync("SELECT business_date FROM shift WHERE id = 1;"))
            .Should().Be(TradingDaySeed.SeededBusinessDate);
    }

    /// <summary>
    /// An explicitly assigned value must reach the database, default or not. Without
    /// <c>ValueGeneratedNever</c>, EF treats an assigned CLR default as "not set" and sends the
    /// column's DEFAULT instead - so an inactive product would silently save as active.
    /// </summary>
    [Fact]
    public async Task AnExplicitValueBeatsTheColumnDefault()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        await database.ExecuteAsync(
            """
            INSERT INTO product (id, code, name, base_uom_id, type, tax_class_id, active,
                                 created_at, updated_at)
            VALUES (9, 'P-009', 'Discontinued elbow', 1, 'STANDARD', 1, 0,
                    '2026-09-04T08:00:00.000+05:30', '2026-09-04T08:00:00.000+05:30');
            """);

        (await database.ScalarAsync("SELECT active FROM product WHERE id = 9;")).Should().Be("0");

        var defaults = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'product';");
        defaults.Should().Contain("DEFAULT 1", "the column keeps its documented DEFAULT");
    }

    /// <summary>
    /// docs/01_DATA_MODEL.md §13: the documented exceptions to "every table has
    /// <c>id INTEGER PRIMARY KEY</c>". Each one is keyed on the thing it is one of - a variant, a
    /// document type, a schema version, a setting key, a business day - so there is no second
    /// identity to keep in step.
    /// </summary>
    [Fact]
    public async Task OnlySixTablesAreKeyedOnSomethingOtherThanId()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var model = ModelOf(database);

        var exceptions = model.GetEntityTypes()
            .Where(entityType => entityType.FindPrimaryKey() is { Properties: not [{ Name: "Id" }] })
            .Select(entityType => entityType.GetTableName())
            .ToList();

        exceptions.Should().BeEquivalentTo(
        [
            "stock_balance", "number_sequence", "schema_version", "app_setting",
            "daily_sales_summary", "daily_product_summary",
        ]);
    }

    /// <summary>
    /// EF defaults a required foreign key to ON DELETE CASCADE. On <c>stock_movement</c> that
    /// would let deleting a variant wipe the stock ledger, which invariant 3 says is the truth.
    /// </summary>
    [Fact]
    public async Task NoForeignKeyCascades()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var tableSql = await database.ColumnAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND sql IS NOT NULL;");

        tableSql.Should().OnlyContain(sql => !sql.Contains("ON DELETE", StringComparison.Ordinal));

        var model = ModelOf(database);
        model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Should().OnlyContain(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.NoAction);
    }

    /// <summary>
    /// docs/01_DATA_MODEL.md §13 listed four columns written as <c>REFERENCES</c> in the DDL whose
    /// tables did not exist. <c>FullSchema0002</c> creates <c>category</c> and <c>brand</c> and
    /// makes those two real; the other two stay plain nullable columns, because a REFERENCES to a
    /// missing table is legal DDL that only fails at INSERT time - a landmine, not a constraint.
    /// </summary>
    /// <remarks>
    /// <c>sale.customer_id</c> and <c>payment.sale_return_id</c> are left alone deliberately, and
    /// not only because P5-T02 and P2-T02 own them. Both tables are append-only, adding a foreign
    /// key to either rebuilds it, and a rebuild drops its triggers - so those two are the two
    /// changes that have to carry a full trigger re-creation with them.
    /// </remarks>
    [Fact]
    public async Task TheTwoRemainingDanglingReferencesAreNotForeignKeysYet()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        var saleSql = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'sale';");
        saleSql.Should().Contain("\"customer_id\" INTEGER NULL")
            .And.NotContain("REFERENCES \"customer\"");

        var paymentSql = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'payment';");
        paymentSql.Should().Contain("\"sale_return_id\" INTEGER NULL")
            .And.NotContain("REFERENCES \"sale_return\"");

        // And the two that were resolved really did become constraints, rather than quietly
        // staying plain columns.
        var productSql = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'product';");
        productSql.Should().Contain("REFERENCES \"category\"").And.Contain("REFERENCES \"brand\"");
    }

    /// <summary>
    /// The same two columns, proved by writing nonsense into each of them. This is the assertion
    /// the reading of <c>sqlite_schema</c> above cannot make: a stray <c>HasForeignKey</c>
    /// produces DDL that looks fine, creates cleanly and then fails at a random INSERT months
    /// later with "no such table: customer".
    /// </summary>
    /// <remarks>
    /// Both tables are append-only, so both are exercised on INSERT - the only door the till uses
    /// for them anyway. The sale goes into the open shift because <c>trg_sale_shift_open</c> would
    /// otherwise refuse it first, which would prove nothing.
    /// </remarks>
    [Fact]
    public async Task AValueInADanglingColumnIsAcceptedRatherThanFailingAtInsertTime()
    {
        await using var database = await MigratedDatabase.CreateAsync();

        await database.ExecuteAsync(
            """
            INSERT INTO sale (id, bill_no, sold_at, business_date, customer_id, user_id, shift_id,
                              subtotal, total, status, prev_hash, row_hash)
            VALUES (97, 'INV-2026-000097', '2026-09-04T11:00:00.000+05:30', '2026-09-04', 4242, 1, 1,
                    1000, 1000, 'COMPLETED', 'x', 'y');
            """);

        (await database.ScalarAsync("SELECT customer_id FROM sale WHERE id = 97;")).Should().Be("4242");

        // sale_return_id, with sale_id NULL so ck_payment_one_document is satisfied.
        await database.ExecuteAsync(
            """
            INSERT INTO payment (id, sale_id, sale_return_id, tender_type, amount, paid_at)
            VALUES (97, NULL, 4242, 'CASH', 1000, '2026-09-04T11:00:00.000+05:30');
            """);

        (await database.ScalarAsync("SELECT sale_return_id FROM payment WHERE id = 97;"))
            .Should().Be("4242");

        // And the file is still sound: a dangling REFERENCES would show up here.
        (await database.ScalarAsync("PRAGMA foreign_key_check;")).Should().BeNull();
        (await database.ScalarAsync("PRAGMA integrity_check;")).Should().Be("ok");
    }

    /// <summary>
    /// The model has to name an index exactly as the database does, or the next migration's diff
    /// drops and recreates it (docs/01_DATA_MODEL.md §13). Reading the file only proves half of
    /// that; this is the other half.
    /// </summary>
    [Fact]
    public async Task EveryIndexInTheModelIsNamedAsTheDatabaseNamesIt()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var inTheModel = ModelOf(database).GetEntityTypes()
            .SelectMany(entityType => entityType.GetIndexes())
            .Select(index => index.GetDatabaseName())
            .ToList();

        var inTheFile = await database.ColumnAsync(
            "SELECT name FROM sqlite_schema WHERE type = 'index' AND name NOT LIKE 'sqlite_%';");

        inTheModel.Should().BeEquivalentTo(inTheFile);
        inTheModel.Should().BeEquivalentTo(DocumentedIndexes);
    }

    /// <summary>
    /// Keeps the model-wide checks above honest. They iterate
    /// <c>Model.GetEntityTypes()</c>, so an entity that never made it into the model would pass
    /// every one of them by not being looked at.
    /// </summary>
    /// <remarks>
    /// <c>product_search</c> is not here and cannot be: EF has no way to express an FTS5 virtual
    /// table, so it is created and maintained by raw SQL in <c>ProductSearch0004</c>.
    /// </remarks>
    [Fact]
    public async Task TheModelCoversEveryDocumentedTable()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var mapped = ModelOf(database).GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .ToList();

        mapped.Should().BeEquivalentTo(DocumentedTables);
    }

    /// <summary>
    /// <c>schema_version</c>, singular. EF's table-naming convention pluralises a
    /// <c>DbSet&lt;SchemaVersion&gt;</c> to <c>SchemaVersions</c>, and the snake_case convention
    /// then writes <c>schema_versions</c> - a table docs/01_DATA_MODEL.md §8 does not have, that
    /// nothing would read, and that would leave the file with no recorded schema version at all.
    /// </summary>
    [Fact]
    public async Task DM_05_TheSchemaVersionTableIsSingular()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        (await database.CountAsync(
            "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = 'schema_version';"))
            .Should().Be(1);

        (await database.CountAsync(
            "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = 'schema_versions';"))
            .Should().Be(0);

        ModelOf(database).GetEntityTypes()
            .Single(entityType => entityType.ClrType == typeof(SchemaVersion))
            .GetTableName().Should().Be("schema_version");
    }

    /// <summary>
    /// docs/01_DATA_MODEL.md §13: property declaration order is the DDL column order, so the
    /// physical column order in the file has to match it too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the guard for a failure that is otherwise completely silent. EF writes a
    /// <c>CreateTable</c> in declaration order, but sorts a <em>rebuilt</em> table's columns
    /// alphabetically after the key - and a rebuild is what adding a foreign key costs on SQLite.
    /// <c>product</c> went through exactly that in <c>ProductForeignKeys0003</c>; the two foreign
    /// keys still outstanding (<c>sale.customer_id</c> in P5-T02, <c>payment.sale_return_id</c> in
    /// P2-T02) will do the same to two append-only tables, so this test has to survive them.
    /// </para>
    /// <para>
    /// It is not tidiness. SQLite's type affinity accepts a positional
    /// <c>INSERT INTO product VALUES (...)</c> written against the order in §3 without complaint,
    /// so a reordered table quietly puts a product code in <c>active</c> and a name in
    /// <c>base_uom_id</c> - and a repair session with <c>sqlite3</c> or a bulk import is exactly
    /// that statement.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DM_01_EveryTableKeepsItsDocumentedColumnOrder()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var model = ModelOf(database);
        var offenders = new List<string>();

        foreach (var entityType in model.GetEntityTypes())
        {
            var table = entityType.GetTableName()!;

            var declared = entityType.ClrType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)

                // Reflection does not promise declaration order; the metadata token does, and it
                // is what every "source order" helper in the framework leans on.
                .OrderBy(property => property.MetadataToken)
                .Select(property => entityType.FindProperty(property.Name))
                .Where(property => property is not null)
                .Select(property => property!.GetColumnName())
                .ToList();

            var actual = await database.ColumnAsync(
                "SELECT name FROM pragma_table_info('" + table + "');");

            if (!declared.SequenceEqual(actual, StringComparer.Ordinal))
            {
                offenders.Add(
                    table + ": declared [" + string.Join(", ", declared) +
                    "] but stored [" + string.Join(", ", actual) + "]");
            }
        }

        offenders.Should().BeEmpty(
            "docs/01_DATA_MODEL.md §13 - a rebuilt table that reorders its columns turns a " +
            "positional INSERT into silent corruption");
    }

    /// <summary>
    /// The same rule for <c>product</c> specifically, spelled out against the DDL in
    /// docs/01_DATA_MODEL.md §3 rather than against the model - so that a mistake made in both
    /// places at once still fails.
    /// </summary>
    [Fact]
    public async Task DM_01_ProductKeepsTheColumnOrderTheDataModelDocuments()
    {
        await using var database = await MigratedDatabase.CreateAsync(seed: false);

        var actual = await database.ColumnAsync("SELECT name FROM pragma_table_info('product');");

        actual.Should().Equal(
            "id", "code", "name", "name_alt", "category_id", "brand_id", "base_uom_id", "type",
            "tax_class_id", "cost_avg", "reorder_level", "reorder_qty", "location",
            "non_returnable", "min_sell_qty", "max_discount_rate", "warranty_days", "notes",
            "image_path", "active", "created_at", "updated_at");
    }

    private static IModel ModelOf(MigratedDatabase database)
    {
        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlite(database.Connection, contextOwnsConnection: false)
            .Options;

        using var context = new PosDbContext(options);
        return context.Model;
    }
}
