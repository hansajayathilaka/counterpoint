# `Data/Schema` — persistence rows, not domain entities

These classes exist so EF Core can generate and diff the schema in
`docs/01_DATA_MODEL.md`. They are one-to-one with tables: plain mutable auto-properties, no
behaviour, no navigations, no value objects.

Deliberate rules, all load-bearing:

- **`long`, never `decimal`/`double`/`float`.** Money, quantity and rate columns are `INTEGER`
  scaled ×10 000 (CLAUDE.md invariant 1). The scaling and the `Money`/`Quantity` value objects
  live in `Counterpoint.Domain`; nothing here knows about them. A bare `decimal` would map to
  `TEXT` with no error to show for it.
- **`internal sealed`.** Not public API. P1-T01 brings real domain types (`Product`, `Sale`, …)
  and moves the mapping onto them; these classes are named after the tables so that diff is a
  straight move.
- **Property declaration order is the column order in the generated DDL.** Keep it identical to
  the `CREATE TABLE` blocks in `docs/01_DATA_MODEL.md`.
- **No `DbSet<>` for these types.** They are registered in `PosDbContext.OnModelCreating` through
  `ApplyConfigurationsFromAssembly`. Only `SchemaVersion` has a `DbSet`, because the migration
  runner writes through it.

Mapping (keys, indexes, CHECK constraints, defaults, delete behaviour) lives one folder up in
`Data/Configurations`.
