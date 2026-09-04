---
name: append-only-ledgers
description: Use when designing or reviewing any system that must keep an auditable financial or inventory history — append-only tables, event ledgers with current-state projections, gapless document numbering and hash chains.
---

# Append-only ledgers and projections

A pattern for systems where history must be provable, not merely stored.

## Ledger and projection

Split every stateful quantity into two things:

- **The ledger** — append-only rows, one per event, each carrying the resulting balance. This is the truth.
- **The projection** — a single current-state row per subject. This is a cache.

The projection is written **in the same transaction** as the ledger row, and must be fully rebuildable from the ledger by a maintenance command. Add a startup consistency check over a random sample and log loudly on mismatch.

Read paths use the projection. Never `SUM()` the ledger on a hot path — it is correct on day one and unusable by year two.

The failure mode to design against is a second code path that writes the projection without a ledger row. Make the projection's writer internal to the ledger service and add an architecture test that no other type touches that table.

## Enforcement belongs in the database

Application-layer discipline erodes. Triggers do not.

```sql
CREATE TRIGGER trg_x_no_update BEFORE UPDATE ON x
BEGIN SELECT RAISE(ABORT, 'x is append-only'); END;

CREATE TRIGGER trg_x_no_delete BEFORE DELETE ON x
BEGIN SELECT RAISE(ABORT, 'x is append-only'); END;
```

Where one column must be mutable, scope the exception with a `WHEN` clause listing every column that must not change — so the exception is explicit and reviewable rather than implied.

**Watch for ORM table rebuilds.** Several ORMs implement column changes on SQLite by creating a new table, copying, dropping and renaming — which silently drops triggers. Recreate them in the migration and test that they survive the full chain.

## Gapless document numbering

Auditors care about gaps. Auto-increment and `MAX(n)+1` both produce them.

```sql
UPDATE number_sequence SET next_val = next_val + 1
WHERE doc_type = ? RETURNING next_val;
```

Allocate inside the business transaction. A cancelled document **keeps its number** with a cancelled status — that is what makes the series gapless rather than merely increasing. Numbers are never reused and never rolled back.

## Tamper evidence

Chain each row to the previous one:

```
row_hash = SHA256(prev_hash ‖ canonical_json(row))
```

Computed inside the transaction. A verification command walks the chain and reports the first break. This does not prevent tampering — it makes tampering detectable, which is the achievable goal for a database the owner controls.

## Corrections post forward

Never edit history to fix an error. Post a compensating entry in the current period, referencing the original. A cancellation is a reversing entry plus a status change, never a delete.
