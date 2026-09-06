# `Data/Schema` — persistence rows, not domain entities

These classes exist so EF Core can generate and diff the schema in
`docs/01_DATA_MODEL.md`. They are one-to-one with tables: plain mutable auto-properties, no
behaviour, no navigations.

Deliberate rules, all load-bearing:

- **Money is `Money`; rates are `TaxRate` or `Percentage`.** Each maps to an `INTEGER` column
  scaled ×10 000 through its converter in `Data`, registered once as a convention in
  `PosDbContext.ConfigureConventions` so no column can be missed (CLAUDE.md invariant 1). The
  scaling itself lives in `Counterpoint.Domain`, next to the arithmetic, so the two cannot drift.
- **Quantity columns are `long`, and `Quantity` is deliberately not used here.** It carries the
  `uom.id` it was measured in, and an EF value converter is a scalar function of one column with
  no access to its siblings — so reading one back would have to invent a unit. See
  `docs/01_DATA_MODEL.md` §13.
- **Never `decimal`, `double` or `float` on a property.** A bare `decimal` maps to `TEXT` with no
  error to show for it, and money stored as text does not add up.
- **`internal sealed`.** Not public API. The real domain types (`Product`, `Sale`, …) arrive from
  P1-T05 onward; these classes are named after the tables so that diff is a straight move.
- **Property declaration order is the column order in the generated DDL.** Keep it identical to
  the `CREATE TABLE` blocks in `docs/01_DATA_MODEL.md`.
- **No `DbSet<>` for these types.** They are registered in `PosDbContext.OnModelCreating` through
  `ApplyConfigurationsFromAssembly`. Only `SchemaVersion` has a `DbSet`, because the migration
  runner writes through it.

Mapping (keys, indexes, CHECK constraints, defaults, delete behaviour) lives one folder up in
`Data/Configurations`.
