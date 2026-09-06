using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The triggers that must exist on every append-only table after the full migration chain
/// (CLAUDE.md invariant 5, docs/01_DATA_MODEL.md §8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Names only, no SQL, on purpose.</b> Migrations are immutable history: if the
/// <c>CREATE TRIGGER</c> text lived here and a later task edited it, that edit would
/// retroactively change what an already-applied migration did. Each migration writes its own
/// trigger SQL out literally; this manifest is only the list of names to check afterwards.
/// </para>
/// <para>
/// It exists because EF Core's SQLite provider rebuilds a table (create-copy-drop-rename) for
/// almost any alter, and a rebuild <b>silently drops that table's triggers</b>. A migration that
/// alters <c>sale</c> to add the <c>customer</c> foreign key, say, would leave the bill ledger
/// editable with nothing to show for it. <see cref="MigrationRunner"/> checks this list after
/// applying migrations, and an integration test checks it too.
/// </para>
/// <para>
/// <c>cash_movement</c>, <c>sale_return</c> and <c>sale_return_line</c> joined the list with
/// their tables in <c>FullSchema0002</c> (P1-T01). Every table CLAUDE.md invariant 5 names is
/// now here.
/// </para>
/// <para>
/// The search-index triggers on <c>product</c> and <c>product_variant</c> are deliberately absent.
/// They are not protection - they maintain a rebuildable FTS5 index - so losing one to a table
/// rebuild costs a stale search result, not an editable ledger, and
/// <c>ReindexSearchCommand</c> repairs it.
/// <c>AppendOnlyTriggerTests.DM_05_TheFileCarriesExactlyTheTriggersTheSchemaDefines</c> asserts
/// the whole trigger set including those; this manifest stays the short list of what must never
/// go missing.
/// </para>
/// </remarks>
internal static class AppendOnlyTables
{
    /// <summary>Table name to the triggers expected on it, as of the latest migration.</summary>
    public static readonly ImmutableDictionary<string, ImmutableArray<string>> ExpectedTriggers =
        new Dictionary<string, ImmutableArray<string>>(System.StringComparer.Ordinal)
        {
            ["stock_movement"] =
            [
                "trg_stock_movement_no_update",
                "trg_stock_movement_no_delete",
            ],
            ["payment"] =
            [
                "trg_payment_no_update",
                "trg_payment_no_delete",
            ],
            ["audit_log"] =
            [
                "trg_audit_log_no_update",
                "trg_audit_log_no_delete",
            ],
            ["sale"] =
            [
                "trg_sale_no_delete",
                "trg_sale_restricted_update",
                "trg_sale_cancel_only_forward",
                "trg_sale_cancel_fields_together",
                "trg_sale_shift_open",
            ],
            ["sale_line"] =
            [
                "trg_sale_line_no_delete",
                "trg_sale_line_restricted_update",
                "trg_sale_line_qty_returned_bounds",
            ],
            ["shift"] =
            [
                "trg_shift_no_delete",
                "trg_shift_restricted_update",
                "trg_shift_closed_is_final",
                "trg_shift_close_fields_together",
            ],
            ["cash_movement"] =
            [
                "trg_cash_movement_no_update",
                "trg_cash_movement_no_delete",
            ],
            ["sale_return"] =
            [
                "trg_sale_return_no_update",
                "trg_sale_return_no_delete",
                "trg_sale_return_shift_open",
            ],
            ["sale_return_line"] =
            [
                "trg_sale_return_line_no_update",
                "trg_sale_return_line_no_delete",
            ],
        }.ToImmutableDictionary(System.StringComparer.Ordinal);

    /// <summary>Every expected trigger name, flattened.</summary>
    public static IEnumerable<string> AllTriggerNames =>
        ExpectedTriggers.Values.SelectMany(names => names);
}
