---
description: Create a schema migration correctly, with triggers and tests intact
argument-hint: "<what the schema change is>"
allowed-tools: Read, Write, Edit, Grep, Glob, Bash, Task
model: opus
---

Schema change: **$ARGUMENTS**

Delegate to the **data-modeler** agent.

## The checklist it must satisfy

1. **Update `docs/01_DATA_MODEL.md` first** — the DDL, the Mermaid ER diagram for that subject area, and the enumeration table if enums changed. The document is the specification; the migration implements it.
2. **Storage conventions** — money and quantity as `INTEGER` ×10 000, timestamps as ISO-8601 text with offset, enums as uppercase `TEXT` with a `CHECK` constraint.
3. **Constraints in the database** — foreign keys, `CHECK`, partial unique indexes. Not application-layer only.
4. **The trigger trap.** EF Core's SQLite provider rebuilds tables for some alters and **silently drops their triggers**. If this migration alters an append-only table, it must recreate both triggers, and the trigger-survival test must be run against the full migration chain, not just this migration.
5. **Forward-only.** Never edit a committed migration. If an earlier one is wrong, add a new one.
6. **A migration test** applying it to an empty database and to a seeded one, asserting `PRAGMA integrity_check` returns `ok`.
7. **Index justification** for any new index, and an honest note if a new query will not use one.

## Then

Run the full integration suite — trigger and foreign-key behaviour is exactly what those tests exist to catch. Then run **invariant-auditor** limited to invariants 3, 4 and 5.

Report the migration name, what it changes, and confirmation that every append-only table still has both triggers after the whole chain applies.
