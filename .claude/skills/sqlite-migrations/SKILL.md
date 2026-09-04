---
name: sqlite-migrations
description: Use when creating or reviewing EF Core migrations, triggers, indexes or any schema change in this POS. Covers the trigger-drop trap, append-only enforcement, forward-only policy and migration testing.
---

# SQLite schema and migrations

`docs/01_DATA_MODEL.md` is the specification. Migrations implement it. They must never diverge — update the document in the same change, including the Mermaid ER diagram for the affected subject area.

## The trap that will bite you

**EF Core's SQLite provider cannot alter or drop columns.** It rebuilds the table: create new, copy, drop old, rename. This **silently drops every trigger on that table**.

On this project that means an innocuous column addition can quietly remove the append-only protection from `sale` or `stock_movement`, and nothing fails until someone discovers a history that can be edited.

So: any migration touching an append-only table must recreate its triggers, and the trigger-survival test must run against the **full migration chain**, not just the new migration.

```csharp
// At the end of any migration that rebuilds an append-only table
migrationBuilder.Sql(@"
CREATE TRIGGER trg_stock_movement_no_update
BEFORE UPDATE ON stock_movement
BEGIN SELECT RAISE(ABORT, 'stock_movement is append-only'); END;");
```

Verification:
```sql
SELECT name FROM sqlite_master WHERE type='trigger' ORDER BY name;
```

## Append-only tables

`sale`, `sale_line`, `payment`, `sale_return`, `sale_return_line`, `stock_movement`, `shift`, `cash_movement`, `audit_log`.

Three column-scoped exceptions, each enforced by a `WHEN` clause on the update trigger rather than by convention:
- `sale_line.qty_returned`
- `sale.status` (completed → cancelled, one direction)
- `shift` close fields, settable once

## Policy

- **Forward-only.** Never edit a committed migration. A PreToolUse hook blocks it. If an earlier migration is wrong, add a new one.
- **Every migration gets a test** applying it to an empty database and to a seeded one, asserting `PRAGMA integrity_check` returns `ok`.
- **Constraints belong in the database.** `ux_one_open_shift` (partial unique) is how constraint C-01 is enforced. The closed-shift insert trigger is how AC-11 is enforced. Application-layer-only rules erode over time; database constraints do not.
- Migrations run at startup inside a transaction, **after** an automatic pre-migration backup. A failed migration restores that backup and refuses to start with a plain-language message.

## Storage conventions

| Concept | Storage |
|---|---|
| Money, quantity, rate | `INTEGER` scaled ×10 000 |
| Timestamp | `TEXT` ISO-8601 with offset |
| Business date | `TEXT` `YYYY-MM-DD` |
| Boolean | `INTEGER` 0/1 |
| Enum | `TEXT` uppercase, with a `CHECK` constraint — readable in a raw dump, which matters at handover |

## Connection PRAGMAs — on every connection

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous  = FULL;   -- durability (NFR-R2). Do not weaken to NORMAL.
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
PRAGMA temp_store   = MEMORY;
PRAGMA cache_size   = -20000;
```

`synchronous = FULL` costs one fsync per commit, around 5–15 ms on an SSD. Against the 2-second bill-save budget that is free, and it is what makes AC-15 (power cut) pass.

## Testing

Integration tests use a **real SQLite file** in a temp directory, never the in-memory provider. The in-memory provider does not enforce foreign keys or run triggers — which is precisely what these tests exist to verify.
