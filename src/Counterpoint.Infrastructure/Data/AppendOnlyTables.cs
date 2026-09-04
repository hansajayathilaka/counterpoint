using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// The triggers that must exist on every append-only table after the full migration chain
/// (CLAUDE.md invariant 5, docs/01_DATA_MODEL.md §6).
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
/// <c>cash_movement</c>, <c>sale_return</c> and <c>sale_return_line</c> are append-only as well,
/// but their tables do not exist until P1/P2. They join this list with their tables.
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
        }.ToImmutableDictionary(System.StringComparer.Ordinal);

    /// <summary>Every expected trigger name, flattened.</summary>
    public static IEnumerable<string> AllTriggerNames =>
        ExpectedTriggers.Values.SelectMany(names => names);
}
