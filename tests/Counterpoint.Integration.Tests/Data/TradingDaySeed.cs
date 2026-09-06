using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// One row in every table in the schema - a small but complete trading day - plus the closed
/// shift the "no sale into a closed shift" rule needs something to aim at.
/// </summary>
/// <remarks>
/// <para>
/// Raw SQL rather than the EF model on purpose: what these tests are checking is the database -
/// its CHECK constraints, its foreign keys and its triggers - so the insert has to go through the
/// same door a Dapper write or a repair session would.
/// </para>
/// <para>
/// The hash-chain columns carry placeholders. Real hashing (CLAUDE.md invariant 6) arrives with
/// the sale transaction in P0-T06; these rows only need the columns to be populated.
/// </para>
/// <para>
/// Money and quantity are the ×10 000 scaled integers of docs/01_DATA_MODEL.md §1:
/// <c>1250000</c> is 125.0000.
/// </para>
/// </remarks>
internal static class TradingDaySeed
{
    internal const string OpenShiftId = "1";
    internal const string ClosedShiftId = "2";
    internal const string SaleId = "1";
    internal const string SaleLineId = "1";
    internal const string VariantId = "1";

    /// <summary>Quantity sold on the seeded line, scaled ×10 000. Two pieces.</summary>
    internal const long SoldQtyBase = 20_000;

    private const string OwnerCreatedAt = "2026-09-04T08:00:00.000+05:30";
    private const string BusinessDate = "2026-09-04";

    /// <summary>
    /// The fifteen tables of <c>Skeleton0001</c>. Kept separate so a test can seed a database
    /// that stands at <c>Skeleton0001</c> and then migrate it forward with rows already in it -
    /// which is the case that matters, because <c>FullSchema0002</c> rebuilds <c>product</c>.
    /// </summary>
    private static readonly string[] SkeletonStatements =
    [
        "INSERT INTO uom (id, name, symbol, decimal_places) VALUES (1, 'Piece', 'pc', 0);",

        "INSERT INTO tax_class (id, name, rate, active) VALUES (1, 'Standard 15%', 1500, 1);",

        """
        INSERT INTO app_user (id, username, display_name, password_hash, role, active,
                              failed_attempts, locked_until, last_login, created_at)
        VALUES (1, 'owner', 'Shop Owner', '$argon2id$placeholder', 'OWNER', 1,
                0, NULL, NULL, '2026-09-04T08:00:00.000+05:30');
        """,

        """
        INSERT INTO product (id, code, name, name_alt, category_id, brand_id, base_uom_id, type,
                             tax_class_id, cost_avg, reorder_level, reorder_qty, location,
                             non_returnable, min_sell_qty, max_discount_rate, warranty_days,
                             notes, image_path, active, created_at, updated_at)
        VALUES (1, 'P-001', 'Galvanised bolt M8', NULL, NULL, NULL, 1, 'STANDARD',
                1, 900000, 100000, 500000, 'A3',
                0, 0, NULL, NULL,
                NULL, NULL, 1, '2026-09-04T08:00:00.000+05:30', '2026-09-04T08:00:00.000+05:30');
        """,

        """
        INSERT INTO product_variant (id, product_id, sku, attributes, price, active, created_at)
        VALUES (1, 1, 'SKU-001', '{"size":"M8"}', 1250000, 1, '2026-09-04T08:00:00.000+05:30');
        """,

        """
        INSERT INTO stock_balance (product_variant_id, qty_base, cost_avg, updated_at)
        VALUES (1, 980000, 900000, '2026-09-04T09:15:00.000+05:30');
        """,

        """
        INSERT INTO shift (id, shift_no, user_id, opened_at, business_date, opening_float,
                           closed_at, counted_cash, expected_cash, variance, status, closed_by, note)
        VALUES (1, 'SH-000001', 1, '2026-09-04T08:05:00.000+05:30', '2026-09-04', 5000000,
                NULL, NULL, NULL, NULL, 'OPEN', NULL, NULL);
        """,

        // Yesterday's shift, already closed. The "cannot post into a closed shift" trigger and the
        // "a closed shift is immutable" trigger both need one that exists.
        """
        INSERT INTO shift (id, shift_no, user_id, opened_at, business_date, opening_float,
                           closed_at, counted_cash, expected_cash, variance, status, closed_by, note)
        VALUES (2, 'SH-000000', 1, '2026-09-03T08:05:00.000+05:30', '2026-09-03', 5000000,
                '2026-09-03T18:30:00.000+05:30', 7500000, 7500000, 0, 'CLOSED', 1, NULL);
        """,

        """
        INSERT INTO number_sequence (doc_type, prefix, pattern, next_val)
        VALUES ('SALE', 'INV-', '{prefix}{yyyy}-{n:000000}', 2);
        """,

        """
        INSERT INTO sale (id, bill_no, sold_at, business_date, customer_id, user_id, shift_id,
                          subtotal, line_discount, bill_discount, tax, rounding, total, cogs,
                          status, cancelled_by, cancelled_at, note, prev_hash, row_hash)
        VALUES (1, 'INV-2026-000001', '2026-09-04T09:15:00.000+05:30', '2026-09-04', NULL, 1, 1,
                2500000, 0, 0, 375000, 0, 2875000, 1800000,
                'COMPLETED', NULL, NULL, NULL, 'GENESIS', 'placeholder-row-hash');
        """,

        """
        INSERT INTO sale_line (id, sale_id, line_no, product_variant_id, description, qty, uom_id,
                               qty_base, unit_price, discount, tax_rate, tax, line_total,
                               unit_cost, qty_returned, note)
        VALUES (1, 1, 1, 1, 'Galvanised bolt M8', 20000, 1,
                20000, 1250000, 0, 1500, 375000, 2875000,
                900000, 0, NULL);
        """,

        """
        INSERT INTO payment (id, sale_id, sale_return_id, tender_type, amount, reference, paid_at)
        VALUES (1, 1, NULL, 'CASH', 2875000, NULL, '2026-09-04T09:15:01.000+05:30');
        """,

        """
        INSERT INTO stock_movement (id, product_variant_id, movement_type, qty_base, unit_cost,
                                    ref_doc_type, ref_doc_id, balance_after, user_id, occurred_at, note)
        VALUES (1, 1, 'SALE', -20000, 900000,
                'SALE', 1, 980000, 1, '2026-09-04T09:15:00.000+05:30', NULL);
        """,

        """
        INSERT INTO audit_log (id, occurred_at, user_id, action, entity_type, entity_id,
                               before_json, after_json, reason, prev_hash, row_hash)
        VALUES (1, '2026-09-04T09:15:00.000+05:30', 1, 'SALE_COMPLETED', 'sale', 1,
                NULL, NULL, NULL, 'GENESIS', 'placeholder-audit-hash');
        """,

        """
        INSERT INTO print_job (id, doc_type, doc_id, target, payload, copies, is_duplicate,
                               status, attempts, last_error, created_at, printed_at)
        VALUES (1, 'SALE', 1, 'RECEIPT', x'1b4048656c6c6f', 1, 0,
                'PENDING', 0, NULL, '2026-09-04T09:15:00.000+05:30', NULL);
        """,

    ];

    /// <summary>
    /// The rest of the model, added by <c>FullSchema0002</c> and <c>ProductSearch0004</c>. Order
    /// matters: every row is inserted after the row it points at, because PRAGMA foreign_keys is
    /// ON for this connection like every other.
    /// </summary>
    private static readonly string[] FullSchemaStatements =
    [
        "INSERT INTO category (id, name, parent_id, active) VALUES (1, 'Fasteners', NULL, 1);",
        "INSERT INTO category (id, name, parent_id, active) VALUES (2, 'Bolts', 1, 1);",

        "INSERT INTO brand (id, name, active) VALUES (1, 'Bosch', 1);",

        """
        INSERT INTO supplier (id, name, contact, phone, address, tax_no, payment_terms, active)
        VALUES (1, 'Ceylon Hardware Imports', 'Mr Silva', '0112345678', 'Colombo 10',
                'VAT-1234', '30 days', 1);
        """,

        """
        INSERT INTO customer (id, name, phone, address, tax_no, type, credit_limit, balance,
                              active, created_at)
        VALUES (1, 'Perera Construction', '0771234567', 'Kandy Road', NULL, 'TRADE',
                50000000, 0, 1, '2026-09-04T08:00:00.000+05:30');
        """,

        // The base unit row: conversion_factor is 1.0000 scaled, which is what "base" means.
        """
        INSERT INTO product_uom (id, product_id, uom_id, conversion_factor, selling_price, is_base)
        VALUES (1, 1, 1, 10000, NULL, 1);
        """,

        """
        INSERT INTO barcode (id, product_variant_id, barcode, is_primary)
        VALUES (1, 1, '5901234123457', 1);
        """,

        """
        INSERT INTO price_tier (id, product_variant_id, tier, min_qty, price, valid_from, valid_to)
        VALUES (1, 1, 'RETAIL', 0, 1250000, NULL, NULL);
        """,

        """
        INSERT INTO price_change_log (id, product_variant_id, old_price, new_price, changed_at,
                                      user_id, reason)
        VALUES (1, 1, 1200000, 1250000, '2026-09-01T09:00:00.000+05:30', 1, 'Supplier increase');
        """,

        """
        INSERT INTO product_supplier (id, product_id, supplier_id, supplier_ref, last_cost)
        VALUES (1, 1, 1, 'CHI-M8-GALV', 900000);
        """,

        """
        INSERT INTO purchase_order (id, po_no, supplier_id, ordered_at, expected_at, status,
                                    user_id, note)
        VALUES (1, 'PO-2026-000001', 1, '2026-08-20T10:00:00.000+05:30',
                '2026-09-01T10:00:00.000+05:30', 'RECEIVED', 1, NULL);
        """,

        """
        INSERT INTO purchase_order_line (id, purchase_order_id, product_variant_id, qty, uom_id,
                                         unit_cost, qty_received_base)
        VALUES (1, 1, 1, 1000000, 1, 900000, 1000000);
        """,

        """
        INSERT INTO goods_receipt (id, grn_no, supplier_id, purchase_order_id, supplier_inv_no,
                                   received_at, subtotal, tax, other_cost, total, user_id, note)
        VALUES (1, 'GRN-2026-000001', 1, 1, 'SI-88231', '2026-09-01T11:00:00.000+05:30',
                900000000, 135000000, 0, 1035000000, 1, NULL);
        """,

        """
        INSERT INTO goods_receipt_line (id, goods_receipt_id, product_variant_id, qty, uom_id,
                                        qty_base, unit_cost, unit_cost_base, tax, line_total)
        VALUES (1, 1, 1, 1000000, 1, 1000000, 900000, 900000, 135000000, 900000000);
        """,

        """
        INSERT INTO stock_take (id, scope, started_at, completed_at, status, user_id)
        VALUES (1, 'LOCATION:A3', '2026-09-04T07:30:00.000+05:30', NULL, 'OPEN', 1);
        """,

        """
        INSERT INTO stock_take_line (id, stock_take_id, product_variant_id, system_qty,
                                     counted_qty, variance, counted_at)
        VALUES (1, 1, 1, 980000, NULL, NULL, NULL);
        """,

        """
        INSERT INTO held_bill (id, label, payload, created_at, user_id)
        VALUES (1, 'Mr Perera', '{"lines":[]}', '2026-09-04T09:40:00.000+05:30', 1);
        """,

        """
        INSERT INTO sale_return (id, return_no, returned_at, business_date, original_sale_id,
                                 exchange_sale_id, customer_id, user_id, shift_id, subtotal, tax,
                                 restocking_fee, total_refund, refund_method, authorised_by,
                                 reason, prev_hash, row_hash)
        VALUES (1, 'RTN-2026-000001', '2026-09-04T10:30:00.000+05:30', '2026-09-04', 1,
                NULL, 1, 1, 1, 1250000, 187500,
                0, 1437500, 'CREDIT_NOTE', NULL,
                'Wrong size', 'GENESIS', 'placeholder-return-hash');
        """,

        """
        INSERT INTO sale_return_line (id, sale_return_id, sale_line_id, product_variant_id,
                                      qty_base, unit_price, unit_cost, tax, line_refund, reason,
                                      disposition)
        VALUES (1, 1, 1, 1,
                10000, 1250000, 900000, 187500, 1437500, 'Wrong size',
                'SELLABLE');
        """,

        """
        INSERT INTO credit_note (id, number, sale_return_id, customer_id, amount_issued,
                                 amount_remaining, issued_at, expires_on, status)
        VALUES (1, 'CN-2026-000001', 1, 1, 1437500,
                1437500, '2026-09-04T10:30:00.000+05:30', '2027-09-04', 'ACTIVE');
        """,

        """
        INSERT INTO credit_note_redemption (id, credit_note_id, sale_id, amount, redeemed_at)
        VALUES (1, 1, 1, 0, '2026-09-04T10:35:00.000+05:30');
        """,

        """
        INSERT INTO cash_movement (id, shift_id, direction, amount, reason, user_id, occurred_at)
        VALUES (1, 1, 'OUT', 500000, 'Tea and milk', 1, '2026-09-04T10:00:00.000+05:30');
        """,

        """
        INSERT INTO daily_sales_summary (business_date, bill_count, gross, discount, tax, net,
                                         cogs, return_count, return_value, tender_cash,
                                         tender_card, tender_other, built_at)
        VALUES ('2026-09-04', 1, 2875000, 0, 375000, 2500000,
                1800000, 1, 1437500, 2875000,
                0, 0, '2026-09-04T18:30:00.000+05:30');
        """,

        """
        INSERT INTO daily_product_summary (business_date, product_variant_id, qty_base, net, cogs)
        VALUES ('2026-09-04', 1, 20000, 2500000, 1800000);
        """,

        """
        INSERT INTO app_setting (key, value, value_type, updated_by, updated_at)
        VALUES ('rounding.mode', 'HALF_AWAY_FROM_ZERO', 'STRING', 1,
                '2026-09-04T08:00:00.000+05:30');
        """,

        """
        INSERT INTO backup_record (id, filename, taken_at, size_bytes, checksum, schema_ver,
                                   local_path, usb_status, cloud_status, cloud_key, attempts,
                                   last_error, verified_at)
        VALUES (1, 'counterpoint-20260903.cpb', '2026-09-03T19:00:00.000+05:30', 1048576,
                'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
                'ProductSearch0004',
                '/var/backups/counterpoint-20260903.cpb', 'OK', 'SKIPPED', NULL, 0,
                NULL, NULL);
        """,
    ];

    /// <summary>Business date shared by the open shift and the seeded sale.</summary>
    internal static string SeededBusinessDate => BusinessDate;

    /// <summary>When the seeded owner account was created.</summary>
    internal static string SeededOwnerCreatedAt => OwnerCreatedAt;

    /// <summary>Seeds every table in the schema.</summary>
    internal static Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken = default) =>
        ExecuteAsync(connection, [.. SkeletonStatements, .. FullSchemaStatements], cancellationToken);

    /// <summary>
    /// Seeds only the tables <c>Skeleton0001</c> created, for a database that has not yet been
    /// migrated past it.
    /// </summary>
    internal static Task ApplySkeletonAsync(DbConnection connection, CancellationToken cancellationToken = default) =>
        ExecuteAsync(connection, SkeletonStatements, cancellationToken);

    private static async Task ExecuteAsync(
        DbConnection connection,
        IReadOnlyList<string> statements,
        CancellationToken cancellationToken)
    {
        foreach (var statement in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
