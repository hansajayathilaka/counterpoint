using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// One row in every table migration <c>Skeleton0001</c> creates, plus the closed shift the
/// "no sale into a closed shift" rule needs something to aim at.
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
internal static class SkeletonSeed
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

    private static readonly string[] Statements =
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

    /// <summary>Business date shared by the open shift and the seeded sale.</summary>
    internal static string SeededBusinessDate => BusinessDate;

    /// <summary>When the seeded owner account was created.</summary>
    internal static string SeededOwnerCreatedAt => OwnerCreatedAt;

    internal static async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        foreach (var statement in Statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
