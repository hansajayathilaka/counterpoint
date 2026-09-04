using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Infrastructure.Data;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Data;

/// <summary>
/// The append-only guarantees of CLAUDE.md invariant 5 and docs/01_DATA_MODEL.md §6 (DM-05),
/// exercised against a real encrypted database with migration <c>Skeleton0001</c> applied.
/// </summary>
/// <remarks>
/// A trigger abort reaches the client as <c>SQLITE_CONSTRAINT</c> (19) with the extended code
/// <c>SQLITE_CONSTRAINT_TRIGGER</c> (1811) and the message the trigger raised, so the tests can
/// assert on all three.
/// </remarks>
public sealed class AppendOnlyTriggerTests
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintTrigger = 1811;

    /// <summary>
    /// The one that matters most. EF Core's SQLite provider rebuilds a table for almost any
    /// alter, and a rebuild drops that table's triggers with no error - so the protection can
    /// disappear in a migration that looks like it only added a column.
    /// </summary>
    [Fact]
    public async Task DM_05_EveryAppendOnlyTriggerSurvivesTheMigrationChain()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        var present = await database.ColumnAsync(
            "SELECT name FROM sqlite_schema WHERE type = 'trigger' ORDER BY name;");

        present.Should().BeEquivalentTo(
            AppendOnlyTables.AllTriggerNames,
            "Data/AppendOnlyTables.cs is the manifest of what must exist after the whole chain");
    }

    /// <summary>Every trigger is on the table the manifest says it is.</summary>
    [Fact]
    public async Task DM_05_EveryTriggerIsOnItsOwnTable()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        var actual = await database.ColumnAsync(
            "SELECT tbl_name || ':' || name FROM sqlite_schema WHERE type = 'trigger';");

        var expected = AppendOnlyTables.ExpectedTriggers
            .SelectMany(entry => entry.Value.Select(name => entry.Key + ":" + name));

        actual.Should().BeEquivalentTo(expected);
    }

    [Theory]

    // stock_movement: the ledger is the truth (CLAUDE.md invariant 3).
    [InlineData(
        "UPDATE stock_movement SET qty_base = 0 WHERE id = 1;",
        "stock_movement is append-only")]
    [InlineData(
        "DELETE FROM stock_movement WHERE id = 1;",
        "stock_movement is append-only")]

    // payment.
    [InlineData(
        "UPDATE payment SET amount = 1 WHERE id = 1;",
        "payment is append-only")]
    [InlineData(
        "DELETE FROM payment WHERE id = 1;",
        "payment is append-only")]

    // audit_log: hash chained, so one edited row breaks the chain.
    [InlineData(
        "UPDATE audit_log SET action = 'NOTHING_HAPPENED' WHERE id = 1;",
        "audit_log is append-only")]
    [InlineData(
        "DELETE FROM audit_log WHERE id = 1;",
        "audit_log is append-only")]

    // sale.
    [InlineData(
        "DELETE FROM sale WHERE id = 1;",
        "sale is append-only")]
    [InlineData(
        "UPDATE sale SET total = 0 WHERE id = 1;",
        "only status, cancelled_by and cancelled_at may be updated")]
    [InlineData(
        "UPDATE sale SET row_hash = 'forged' WHERE id = 1;",
        "only status, cancelled_by and cancelled_at may be updated")]
    [InlineData(
        "UPDATE sale SET cancelled_at = '2026-09-04T10:00:00.000+05:30' WHERE id = 1;",
        "cancellation fields may only be set while cancelling")]

    // sale_line.
    [InlineData(
        "DELETE FROM sale_line WHERE id = 1;",
        "sale_line is append-only")]
    [InlineData(
        "UPDATE sale_line SET description = 'something cheaper' WHERE id = 1;",
        "only qty_returned may be updated")]
    [InlineData(
        "UPDATE sale_line SET unit_price = 1 WHERE id = 1;",
        "only qty_returned may be updated")]
    [InlineData(
        "UPDATE sale_line SET qty_returned = -1 WHERE id = 1;",
        "must be between 0 and qty_base")]
    [InlineData(
        "UPDATE sale_line SET qty_returned = qty_base + 1 WHERE id = 1;",
        "must be between 0 and qty_base")]

    // shift.
    [InlineData(
        "DELETE FROM shift WHERE id = 1;",
        "shift is append-only")]
    [InlineData(
        "UPDATE shift SET opening_float = 0 WHERE id = 1;",
        "only the close fields may be updated")]
    [InlineData(
        "UPDATE shift SET counted_cash = 1 WHERE id = 1;",
        "close fields may only be set while closing the shift")]
    public async Task DM_05_ForbiddenStatementIsAborted(string sql, string expectedMessageFragment)
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        var exception = await database.ExecuteExpectingAbortAsync(sql);

        exception.SqliteErrorCode.Should().Be(SqliteConstraint);
        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        exception.Message.Should().Contain(expectedMessageFragment);
    }

    /// <summary>
    /// <c>REPLACE</c> is a <c>DELETE</c> wearing an <c>INSERT</c>'s clothes, and it must be
    /// refused like one.
    /// </summary>
    /// <remarks>
    /// SQLite fires the <c>BEFORE DELETE</c> trigger for a row that REPLACE conflict resolution
    /// removes <b>only when <c>PRAGMA recursive_triggers</c> is ON</b>, and it is OFF by default.
    /// With it off, every one of these statements succeeded: the bill kept its id, took a new
    /// <c>bill_no</c>, a new total and a forged <c>row_hash</c>, and nothing raised. The pragma is
    /// set in <c>PosConnectionFactory</c>; this is what says why.
    /// </remarks>
    [Theory]
    [InlineData(
        """
        INSERT OR REPLACE INTO sale (id, bill_no, sold_at, business_date, user_id, shift_id,
                                     subtotal, total, status, prev_hash, row_hash)
        VALUES (1, 'INV-2026-999999', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1,
                1, 1, 'COMPLETED', 'forged', 'forged');
        """,
        "sale is append-only")]
    [InlineData(
        """
        INSERT OR REPLACE INTO sale_line (id, sale_id, line_no, product_variant_id, description,
                                          qty, uom_id, qty_base, unit_price, line_total)
        VALUES (1, 1, 1, 1, 'Something cheaper', 1, 1, 1, 1, 1);
        """,
        "sale_line is append-only")]
    [InlineData(
        """
        INSERT OR REPLACE INTO payment (id, sale_id, tender_type, amount, paid_at)
        VALUES (1, 1, 'CASH', 1, '2026-09-04T10:00:00.000+05:30');
        """,
        "payment is append-only")]
    [InlineData(
        """
        INSERT OR REPLACE INTO stock_movement (id, product_variant_id, movement_type, qty_base,
                                               unit_cost, ref_doc_type, balance_after, user_id,
                                               occurred_at)
        VALUES (1, 1, 'ADJUSTMENT', 999, 1, 'X', 999, 1, '2026-09-04T10:00:00.000+05:30');
        """,
        "stock_movement is append-only")]
    [InlineData(
        """
        INSERT OR REPLACE INTO audit_log (id, occurred_at, user_id, action, entity_type,
                                          prev_hash, row_hash)
        VALUES (1, '2026-09-04T10:00:00.000+05:30', 1, 'NOTHING_HAPPENED', 'sale', 'x', 'y');
        """,
        "audit_log is append-only")]
    [InlineData(
        """
        INSERT OR REPLACE INTO shift (id, shift_no, user_id, opened_at, business_date,
                                      opening_float, status)
        VALUES (2, 'SH-000000', 1, '2026-09-03T08:05:00.000+05:30', '2026-09-03', 0, 'OPEN');
        """,
        "shift is append-only")]
    public async Task DM_05_ReplaceCannotOverwriteAnAppendOnlyRow(string sql, string expectedMessageFragment)
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        var exception = await database.ExecuteExpectingAbortAsync(sql);

        exception.SqliteErrorCode.Should().Be(SqliteConstraint);
        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        exception.Message.Should().Contain(expectedMessageFragment);
    }

    /// <summary>The bill the REPLACE above aimed at is untouched, down to its hash.</summary>
    [Fact]
    public async Task DM_05_ARefusedReplaceLeavesTheBillExactlyAsItWas()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        await database.ExecuteExpectingAbortAsync(
            """
            INSERT OR REPLACE INTO sale (id, bill_no, sold_at, business_date, user_id, shift_id,
                                         subtotal, total, status, prev_hash, row_hash)
            VALUES (1, 'INV-2026-999999', '2026-09-04T10:00:00.000+05:30', '2026-09-04', 1, 1,
                    1, 1, 'COMPLETED', 'forged', 'forged');
            """);

        (await database.ScalarAsync("SELECT count(*) FROM sale;")).Should().Be("1");
        (await database.ScalarAsync("SELECT bill_no FROM sale WHERE id = 1;"))
            .Should().Be("INV-2026-000001");
        (await database.ScalarAsync("SELECT total FROM sale WHERE id = 1;")).Should().Be("2875000");
        (await database.ScalarAsync("SELECT row_hash FROM sale WHERE id = 1;"))
            .Should().Be("placeholder-row-hash");
    }

    /// <summary>
    /// The one column-scoped exception on <c>sale_line</c> (AC-06). If this were blocked too,
    /// returns could not be recorded at all.
    /// </summary>
    [Fact]
    public async Task AC_06_QtyReturnedWithinBoundsIsTheOnePermittedSaleLineUpdate()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        await database.ExecuteAsync("UPDATE sale_line SET qty_returned = 10000 WHERE id = 1;");

        (await database.ScalarAsync("SELECT qty_returned FROM sale_line WHERE id = 1;"))
            .Should().Be("10000");

        // And the full quantity, the boundary the trigger must still allow.
        await database.ExecuteAsync("UPDATE sale_line SET qty_returned = qty_base WHERE id = 1;");

        (await database.ScalarAsync("SELECT qty_returned FROM sale_line WHERE id = 1;"))
            .Should().Be("20000");
    }

    /// <summary>
    /// AC-06 is a <em>cumulative</em> bound, so <c>qty_returned</c> must be monotonic. A per-update
    /// range check alone is not enough: a fully returned line could be wound back to 0 and returned
    /// again, without limit, and each individual UPDATE would be inside 0..qty_base. There is no
    /// reversal document for a <c>sale_return</c>, so a decrease is never legitimate.
    /// </summary>
    [Fact]
    public async Task AC_06_QtyReturnedCanNeverBeWoundBackward()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        await database.ExecuteAsync("UPDATE sale_line SET qty_returned = qty_base WHERE id = 1;");

        var toZero = await database.ExecuteExpectingAbortAsync(
            "UPDATE sale_line SET qty_returned = 0 WHERE id = 1;");

        toZero.SqliteErrorCode.Should().Be(SqliteConstraint);
        toZero.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        toZero.Message.Should().Contain("may never decrease");

        // Not just the reset to zero: any decrease at all, including one unit.
        var byOne = await database.ExecuteExpectingAbortAsync(
            "UPDATE sale_line SET qty_returned = qty_base - 1 WHERE id = 1;");
        byOne.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);

        // The line is still recorded as fully returned, so a second return cannot take anything.
        (await database.ScalarAsync("SELECT qty_returned FROM sale_line WHERE id = 1;"))
            .Should().Be("20000");
    }

    /// <summary>
    /// The reason docs/01_DATA_MODEL.md §6 now says <c>IS NOT</c> and not <c>&lt;&gt;</c>.
    /// <c>old.note &lt;&gt; new.note</c> evaluates to NULL when the stored note is NULL, a NULL
    /// <c>WHEN</c> clause does not fire, and the update would go straight through - on the very
    /// rows most likely to have NULLs. <c>shift.note</c> makes the same point one trigger over, in
    /// <see cref="FR_8_4_TheVarianceNoteIsWrittenByTheClosingUpdateAndNowhereElse"/>: it is a close
    /// field, so its NULL-safe guard lives in <c>trg_shift_close_fields_together</c>.
    /// </summary>
    [Fact]
    public async Task DM_05_ANullColumnDoesNotLetAnUpdateSlipPastTheGuard()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        (await database.ScalarAsync("SELECT note FROM sale_line WHERE id = 1;")).Should().BeNull();

        var exception = await database.ExecuteExpectingAbortAsync(
            "UPDATE sale_line SET note = 'adjusted' WHERE id = 1;");

        exception.Message.Should().Contain("only qty_returned may be updated");
    }

    /// <summary>
    /// SRS §8.7 requires a note when the cash variance exceeds the threshold, and RPT-05 prints it
    /// as a Z-report column - so <c>shift.note</c> must be writable by the UPDATE that closes the
    /// shift. It is a close field, guarded by <c>trg_shift_close_fields_together</c>, not an
    /// immutable one: annotating a live OPEN shift is still refused.
    /// </summary>
    [Fact]
    public async Task FR_8_4_TheVarianceNoteIsWrittenByTheClosingUpdateAndNowhereElse()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        // (a) On a live OPEN shift, on its own, the note is not a thing the till may set.
        var onItsOwn = await database.ExecuteExpectingAbortAsync(
            "UPDATE shift SET note = 'annotated mid-shift' WHERE id = 1;");

        onItsOwn.SqliteErrorCode.Should().Be(SqliteConstraint);
        onItsOwn.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        onItsOwn.Message.Should().Contain("close fields may only be set while closing the shift");
        (await database.ScalarAsync("SELECT note FROM shift WHERE id = 1;")).Should().BeNull();

        // (b) On the OPEN -> CLOSED transition, alongside the other close fields, it goes through.
        await database.ExecuteAsync(
            """
            UPDATE shift
               SET status = 'CLOSED',
                   closed_at = '2026-09-04T18:30:00.000+05:30',
                   counted_cash = 8000000,
                   expected_cash = 7875000,
                   variance = 125000,
                   closed_by = 1,
                   note = 'Short by 12.50: change given from the wrong denomination.'
             WHERE id = 1;
            """);

        (await database.ScalarAsync("SELECT note FROM shift WHERE id = 1;"))
            .Should().Be("Short by 12.50: change given from the wrong denomination.");
        (await database.ScalarAsync("SELECT status FROM shift WHERE id = 1;")).Should().Be("CLOSED");

        // And once closed it is frozen like every other close field.
        var rewrite = await database.ExecuteExpectingAbortAsync(
            "UPDATE shift SET note = 'actually it was fine' WHERE id = 1;");
        rewrite.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
    }

    /// <summary>
    /// The same NULL-safety argument on <c>sale</c>. <c>customer_id</c> is NULL on every bill until
    /// P5 brings customers, and <c>note</c> is NULL on almost every bill - so <c>&lt;&gt;</c> here
    /// would have left the two most-NULL columns of the ledger freely editable.
    /// </summary>
    [Fact]
    public async Task DM_05_ANullColumnOnSaleDoesNotLetAnUpdateSlipPastTheGuard()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        (await database.ScalarAsync("SELECT customer_id FROM sale WHERE id = 1;")).Should().BeNull();
        (await database.ScalarAsync("SELECT note FROM sale WHERE id = 1;")).Should().BeNull();

        var customer = await database.ExecuteExpectingAbortAsync(
            "UPDATE sale SET customer_id = 7 WHERE id = 1;");
        customer.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        customer.Message.Should().Contain("only status, cancelled_by and cancelled_at may be updated");

        var note = await database.ExecuteExpectingAbortAsync(
            "UPDATE sale SET note = 'reprice' WHERE id = 1;");
        note.Message.Should().Contain("only status, cancelled_by and cancelled_at may be updated");
    }

    /// <summary>
    /// NULL on the <em>new</em> side, which is the other way this goes wrong.
    /// <c>sale_line.product_variant_id</c> is nullable (an open item has none) and populated on a
    /// scanned line, so blanking it is a real edit that <c>1 &lt;&gt; NULL</c> would not have seen.
    /// </summary>
    [Fact]
    public async Task DM_05_BlankingAPopulatedNullableColumnIsStillAnEdit()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        (await database.ScalarAsync("SELECT product_variant_id FROM sale_line WHERE id = 1;"))
            .Should().Be("1");

        var exception = await database.ExecuteExpectingAbortAsync(
            "UPDATE sale_line SET product_variant_id = NULL WHERE id = 1;");

        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        exception.Message.Should().Contain("only qty_returned may be updated");

        (await database.ScalarAsync("SELECT product_variant_id FROM sale_line WHERE id = 1;"))
            .Should().Be("1");
    }

    /// <summary>
    /// The other half of <c>IS NOT</c>: it must not over-fire. Writing NULL over NULL is not a
    /// change, so the one permitted update must still go through when the statement happens to
    /// mention an untouched nullable column.
    /// </summary>
    [Fact]
    public async Task DM_05_WritingNullOverNullIsNotAChangeAndDoesNotTripTheGuard()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        (await database.ScalarAsync("SELECT note FROM sale_line WHERE id = 1;")).Should().BeNull();

        await database.ExecuteAsync(
            "UPDATE sale_line SET note = NULL, qty_returned = 5000 WHERE id = 1;");

        (await database.ScalarAsync("SELECT qty_returned FROM sale_line WHERE id = 1;"))
            .Should().Be("5000");
    }

    /// <summary>
    /// Every column of a restricted table is either guarded by its trigger or on the short list
    /// docs/01_DATA_MODEL.md §6 permits - no third category.
    /// </summary>
    /// <remarks>
    /// This is the test that survives P1. A migration that adds a column to <c>sale</c> and forgets
    /// to name it in <c>trg_sale_restricted_update</c> leaves that column editable for ever, with
    /// nothing failing to show for it. Here, that migration goes red.
    /// </remarks>
    [Theory]
    [InlineData("sale", "trg_sale_restricted_update", "status,cancelled_by,cancelled_at")]
    [InlineData("sale_line", "trg_sale_line_restricted_update", "qty_returned")]
    [InlineData(
        "shift",
        "trg_shift_restricted_update",

        // note is here, not in the guarded set, because it is a close field: SRS §8.7 requires it
        // on a high-variance close and RPT-05 prints it. trg_shift_close_fields_together is what
        // keeps it to the closing UPDATE.
        "status,closed_at,counted_cash,expected_cash,variance,closed_by,note")]
    public async Task DM_05_ARestrictedUpdateTriggerNamesEveryColumnItDoesNotPermit(
        string table,
        string trigger,
        string permittedColumns)
    {
        await using var database = await SkeletonDatabase.CreateAsync(seed: false);

        var triggerSql = await database.ScalarAsync(
            "SELECT sql FROM sqlite_schema WHERE type = 'trigger' AND name = '" + trigger + "';");
        triggerSql.Should().NotBeNull();

        var guarded = ColumnsReferencedAsOld(triggerSql!);
        var permitted = permittedColumns.Split(',');
        var columns = await database.ColumnAsync(
            "SELECT name FROM pragma_table_info('" + table + "');");

        guarded.Should().NotIntersectWith(
            permitted,
            "a column cannot be both guarded and permitted");

        guarded.Concat(permitted).Should().BeEquivalentTo(
            columns,
            "every column of " + table + " is either guarded by " + trigger +
            " or on the permitted list in docs/01_DATA_MODEL.md §6");
    }

    /// <summary>The bare column names a trigger body compares through <c>old.</c>.</summary>
    private static List<string> ColumnsReferencedAsOld(string triggerSql)
    {
        const string Marker = "old.";

        var columns = new List<string>();
        var index = triggerSql.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var start = index + Marker.Length;
            var end = start;
            while (end < triggerSql.Length &&
                   (char.IsLetterOrDigit(triggerSql[end]) || triggerSql[end] == '_'))
            {
                end++;
            }

            var column = triggerSql[start..end];
            if (column.Length > 0 && !columns.Contains(column, StringComparer.Ordinal))
            {
                columns.Add(column);
            }

            index = triggerSql.IndexOf(Marker, end, StringComparison.OrdinalIgnoreCase);
        }

        return columns;
    }

    /// <summary>A cancelled bill keeps its number and its figures (CLAUDE.md invariant 4).</summary>
    [Fact]
    public async Task DM_05_ASaleMayBeCancelledOnceAndOnlyForwards()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        await database.ExecuteAsync(
            """
            UPDATE sale
               SET status = 'CANCELLED',
                   cancelled_by = 1,
                   cancelled_at = '2026-09-04T10:00:00.000+05:30'
             WHERE id = 1;
            """);

        (await database.ScalarAsync("SELECT status FROM sale WHERE id = 1;")).Should().Be("CANCELLED");
        (await database.ScalarAsync("SELECT bill_no FROM sale WHERE id = 1;")).Should().Be("INV-2026-000001");

        var exception = await database.ExecuteExpectingAbortAsync(
            "UPDATE sale SET status = 'COMPLETED' WHERE id = 1;");

        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        exception.Message.Should().Contain("may only change from COMPLETED to CANCELLED");
    }

    /// <summary>FR-8.5, AC-11: the till may not back-date takings into a shift that is closed.</summary>
    [Fact]
    public async Task FR_8_5_ASaleCannotBePostedIntoAClosedShift()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        var exception = await database.ExecuteExpectingAbortAsync(
            """
            INSERT INTO sale (id, bill_no, sold_at, business_date, customer_id, user_id, shift_id,
                              subtotal, total, status, prev_hash, row_hash)
            VALUES (99, 'INV-2026-000099', '2026-09-04T11:00:00.000+05:30', '2026-09-04', NULL, 1, 2,
                    1000, 1000, 'COMPLETED', 'x', 'y');
            """);

        exception.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
        exception.Message.Should().Contain("cannot post into a closed shift");
    }

    /// <summary>
    /// A shift id that does not exist at all must be refused too. <c>&lt;&gt; 'OPEN'</c> would
    /// return NULL for the missing row and let the bill through; <c>IS NOT 'OPEN'</c> does not.
    /// </summary>
    [Fact]
    public async Task FR_8_5_ASaleCannotBePostedIntoAShiftThatDoesNotExist()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        var exception = await database.ExecuteExpectingAbortAsync(
            """
            INSERT INTO sale (id, bill_no, sold_at, business_date, customer_id, user_id, shift_id,
                              subtotal, total, status, prev_hash, row_hash)
            VALUES (98, 'INV-2026-000098', '2026-09-04T11:00:00.000+05:30', '2026-09-04', NULL, 1, 4242,
                    1000, 1000, 'COMPLETED', 'x', 'y');
            """);

        exception.Message.Should().Contain("cannot post into a closed shift");
    }

    /// <summary>The close fields are settable once; after that the shift is frozen (C-01, FR-8).</summary>
    [Fact]
    public async Task DM_05_AShiftClosesOnceAndIsThenImmutable()
    {
        await using var database = await SkeletonDatabase.CreateAsync();

        await database.ExecuteAsync(
            """
            UPDATE shift
               SET status = 'CLOSED',
                   closed_at = '2026-09-04T18:30:00.000+05:30',
                   counted_cash = 8000000,
                   expected_cash = 7875000,
                   variance = 125000,
                   closed_by = 1
             WHERE id = 1;
            """);

        (await database.ScalarAsync("SELECT status FROM shift WHERE id = 1;")).Should().Be("CLOSED");

        var reopen = await database.ExecuteExpectingAbortAsync(
            "UPDATE shift SET status = 'OPEN' WHERE id = 1;");
        reopen.Message.Should().Contain("a closed shift is immutable");

        // Recounting the drawer after the fact is refused too. SQLite does not order triggers on
        // the same event, so this is caught by whichever of trg_shift_closed_is_final and
        // trg_shift_close_fields_together fires first - the assertion is that it is refused.
        var recount = await database.ExecuteExpectingAbortAsync(
            "UPDATE shift SET counted_cash = 9999999 WHERE id = 1;");
        recount.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);

        // The already-closed shift from the seed is frozen for the same reason.
        var reclose = await database.ExecuteExpectingAbortAsync(
            "UPDATE shift SET closed_at = '2026-09-04T19:00:00.000+05:30' WHERE id = 2;");
        reclose.SqliteExtendedErrorCode.Should().Be(SqliteConstraintTrigger);
    }

    /// <summary>
    /// The manifest is names only. If it ever grows SQL, a later edit would retroactively change
    /// what an already-applied migration did.
    /// </summary>
    [Fact]
    public void TheTriggerManifestCarriesNoSql()
    {
        IEnumerable<string> names = AppendOnlyTables.AllTriggerNames;

        names.Should().OnlyContain(name => name.StartsWith("trg_", StringComparison.Ordinal));
        names.Should().OnlyContain(name => !name.Contains(' ', StringComparison.Ordinal));
        names.Should().HaveCount(18);
    }
}
