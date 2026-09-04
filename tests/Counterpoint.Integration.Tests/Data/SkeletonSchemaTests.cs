using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// What migration <c>Skeleton0001</c> actually put in the file: foreign keys, CHECK constraints,
/// the indexes docs/01_DATA_MODEL.md §12 names, and the storage rules of §1 (DM-03, DM-04).
/// </summary>
public sealed class SkeletonSchemaTests
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintForeignKey = 787;
    private const int SqliteConstraintCheck = 275;
    private const int SqliteConstraintUnique = 2067;

    /// <summary>
    /// Every foreign key the fifteen tables actually carry, as
    /// <c>table.column -&gt; referenced table</c>. Nothing else may be one, and in particular none
    /// of the four dangling columns of docs/01_DATA_MODEL.md §13 may appear here.
    /// </summary>
    private static readonly string[] SkeletonForeignKeys =
    [
        "audit_log.user_id -> app_user",
        "payment.sale_id -> sale",
        "product.base_uom_id -> uom",
        "product.tax_class_id -> tax_class",
        "product_variant.product_id -> product",
        "sale.cancelled_by -> app_user",
        "sale.shift_id -> shift",
        "sale.user_id -> app_user",
        "sale_line.product_variant_id -> product_variant",
        "sale_line.sale_id -> sale",
        "sale_line.uom_id -> uom",
        "shift.closed_by -> app_user",
        "shift.user_id -> app_user",
        "stock_balance.product_variant_id -> product_variant",
        "stock_movement.product_variant_id -> product_variant",
        "stock_movement.user_id -> app_user",
    ];

    /// <summary>DM-04: no orphan lines, enforced by the database rather than by the caller.</summary>
    [Fact]
    public async Task DM_04_ASaleLineWithNoSaleIsRefused()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

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
    /// proves the schema carries exactly the sixteen references §13's diagram draws, so a
    /// reference to a table that does not exist yet cannot hide in it.
    /// </summary>
    [Fact]
    public async Task DM_04_TheSchemaCarriesExactlyTheDocumentedForeignKeys()
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        var actual = await database.ColumnAsync(
            """
            SELECT tables.name || '.' || keys."from" || ' -> ' || keys."table"
              FROM sqlite_schema AS tables
              JOIN pragma_foreign_key_list(tables.name) AS keys
             WHERE tables.type = 'table'
               AND tables.name NOT LIKE 'sqlite_%'
               AND tables.name NOT LIKE '\_\_EF%' ESCAPE '\';
            """);

        actual.Should().BeEquivalentTo(SkeletonForeignKeys);

        // Every table a foreign key points at has to exist. A REFERENCES to a missing table is
        // legal DDL that only fails at INSERT time, which is the landmine §13 is about.
        var referenced = SkeletonForeignKeys
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
        await using var database = await SkeletonDatabase.CreateAsync();

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
        await using var database = await SkeletonDatabase.CreateAsync();

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
        await using var database = await SkeletonDatabase.CreateAsync();

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
        await using var database = await SkeletonDatabase.CreateAsync();

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
        await using var database = await SkeletonDatabase.CreateAsync();

        var indexes = await database.ColumnAsync(
            "SELECT name FROM sqlite_schema WHERE type = 'index' AND name NOT LIKE 'sqlite_%' ORDER BY name;");

        indexes.Should().BeEquivalentTo(
        [
            "ix_audit_entity", "ix_audit_time",
            "ix_movement_ref", "ix_movement_time", "ix_movement_variant_time",
            "ix_payment_return", "ix_payment_sale",
            "ix_print_pending",
            "ix_product_active", "ix_product_brand", "ix_product_category",
            "ix_sale_cust", "ix_sale_date", "ix_sale_line_sale", "ix_sale_line_variant",
            "ix_sale_shift", "ix_sale_soldat",
            "ix_stock_balance_low",
            "ix_variant_product",
            "ux_app_user_username", "ux_one_open_shift", "ux_product_code", "ux_sale_bill_no",
            "ux_sale_line_no", "ux_shift_no", "ux_tax_class_name", "ux_uom_name",
            "ux_variant_sku",
        ]);
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
    public async Task EveryDocumentedEnumMemberIsAccepted(string sql)
    {
        await using var database = await SkeletonDatabase.CreateAsync();

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
    public async Task AnOutOfEnumValueIsRejectedByItsCheckConstraint(string sql, string constraint)
    {
        await using var database = await SkeletonDatabase.CreateAsync();

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
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

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

    /// <summary>Only the three storage classes §1 allows.</summary>
    [Fact]
    public async Task DM_01_EveryColumnIsTextIntegerOrBlob()
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        var types = await database.ColumnAsync(
            """
            SELECT DISTINCT upper(info.type)
              FROM sqlite_schema AS tables
              JOIN pragma_table_info(tables.name) AS info
             WHERE tables.type = 'table'
               AND tables.name NOT LIKE 'sqlite_%'
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
        await using var database = await SkeletonDatabase.CreateAsync();

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
        await using var database = await SkeletonDatabase.CreateAsync();

        (await database.ScalarAsync("SELECT business_date FROM sale WHERE id = 1;"))
            .Should().Be(SkeletonSeed.SeededBusinessDate);

        (await database.ScalarAsync("SELECT business_date FROM shift WHERE id = 1;"))
            .Should().Be(SkeletonSeed.SeededBusinessDate);
    }

    /// <summary>
    /// An explicitly assigned value must reach the database, default or not. Without
    /// <c>ValueGeneratedNever</c>, EF treats an assigned CLR default as "not set" and sends the
    /// column's DEFAULT instead - so an inactive product would silently save as active.
    /// </summary>
    [Fact]
    public async Task AnExplicitValueBeatsTheColumnDefault()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

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
    /// docs/01_DATA_MODEL.md §13: three documented exceptions to "every table has
    /// <c>id INTEGER PRIMARY KEY</c>".
    /// </summary>
    [Fact]
    public async Task OnlyThreeTablesAreKeyedOnSomethingOtherThanId()
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        var model = ModelOf(database);

        var exceptions = model.GetEntityTypes()
            .Where(entityType => entityType.FindPrimaryKey() is { Properties: [{ Name: not "Id" }] })
            .Select(entityType => entityType.GetTableName())
            .ToList();

        exceptions.Should().BeEquivalentTo(["stock_balance", "number_sequence", "schema_version"]);
    }

    /// <summary>
    /// EF defaults a required foreign key to ON DELETE CASCADE. On <c>stock_movement</c> that
    /// would let deleting a variant wipe the stock ledger, which invariant 3 says is the truth.
    /// </summary>
    [Fact]
    public async Task NoForeignKeyCascades()
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        var tableSql = await database.ColumnAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND sql IS NOT NULL;");

        tableSql.Should().OnlyContain(sql => !sql.Contains("ON DELETE", StringComparison.Ordinal));

        var model = ModelOf(database);
        model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Should().OnlyContain(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.NoAction);
    }

    /// <summary>
    /// The four columns whose referenced tables do not exist yet are plain columns. A REFERENCES
    /// to a missing table is legal DDL but fails at INSERT time with "no such table", which is a
    /// landmine rather than a constraint.
    /// </summary>
    [Fact]
    public async Task TheFourDanglingReferencesAreNotForeignKeysYet()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        var productSql = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'product';");
        productSql.Should().Contain("\"category_id\" INTEGER NULL")
            .And.Contain("\"brand_id\" INTEGER NULL")
            .And.NotContain("REFERENCES \"category\"")
            .And.NotContain("REFERENCES \"brand\"");

        var saleSql = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'sale';");
        saleSql.Should().Contain("\"customer_id\" INTEGER NULL")
            .And.NotContain("REFERENCES \"customer\"");

        var paymentSql = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'payment';");
        paymentSql.Should().Contain("\"sale_return_id\" INTEGER NULL")
            .And.NotContain("REFERENCES \"sale_return\"");

        // And a value in one of them inserts happily, which it would not if the REFERENCES stood.
        await database.ExecuteAsync("UPDATE product SET category_id = 7, brand_id = 9 WHERE id = 1;");
        (await database.ScalarAsync("SELECT category_id FROM product WHERE id = 1;")).Should().Be("7");
    }

    /// <summary>
    /// The same four columns, proved by writing nonsense into each of them. This is the assertion
    /// the reading of <c>sqlite_schema</c> above cannot make: a forgotten <c>HasForeignKey</c>
    /// removal produces DDL that looks fine, creates cleanly and then fails at a random INSERT
    /// months later with "no such table: category".
    /// </summary>
    /// <remarks>
    /// <c>sale</c> and <c>payment</c> are append-only, so their two are exercised on INSERT - the
    /// only door the till uses for them anyway. The sale goes into the open shift because
    /// <c>trg_sale_shift_open</c> would otherwise refuse it first, which would prove nothing.
    /// </remarks>
    [Fact]
    public async Task AValueInADanglingColumnIsAcceptedRatherThanFailingAtInsertTime()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        await database.ExecuteAsync(
            "UPDATE product SET category_id = 4242, brand_id = 4243 WHERE id = 1;");

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
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        var inTheModel = ModelOf(database).GetEntityTypes()
            .SelectMany(entityType => entityType.GetIndexes())
            .Select(index => index.GetDatabaseName())
            .ToList();

        var inTheFile = await database.ColumnAsync(
            "SELECT name FROM sqlite_schema WHERE type = 'index' AND name NOT LIKE 'sqlite_%';");

        inTheModel.Should().BeEquivalentTo(inTheFile);
        inTheModel.Should().HaveCount(28, "docs/01_DATA_MODEL.md §12 names twenty-eight indexes");
    }

    /// <summary>
    /// Keeps the model-wide checks above honest. They iterate
    /// <c>Model.GetEntityTypes()</c>, so an entity that never made it into the model would pass
    /// every one of them by not being looked at.
    /// </summary>
    [Fact]
    public async Task TheModelCoversAllFifteenSkeletonTables()
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        var mapped = ModelOf(database).GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .ToList();

        mapped.Should().BeEquivalentTo(
        [
            "app_user", "audit_log", "number_sequence", "payment", "print_job", "product",
            "product_variant", "sale", "sale_line", "schema_version", "shift", "stock_balance",
            "stock_movement", "tax_class", "uom",
        ]);
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
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

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

    private static IModel ModelOf(SkeletonDatabase database)
    {
        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlite(database.Connection, contextOwnsConnection: false)
            .Options;

        using var context = new PosDbContext(options);
        return context.Model;
    }
}
