# ADR-0003: Microsoft.EntityFrameworkCore.Sqlite.Core instead of Microsoft.EntityFrameworkCore.Sqlite

**Status:** Accepted
**Date:** 2026-09-04
**Deciders:** P0-T03

## Context

P0-T03 plans "add `SQLitePCLRaw.bundle_e_sqlcipher`, `Microsoft.EntityFrameworkCore.Sqlite`,
`Dapper`". Those first two cannot be used together.

`Microsoft.EntityFrameworkCore.Sqlite` is a metapackage: it pulls `Microsoft.Data.Sqlite`, which
pulls `SQLitePCLRaw.bundle_e_sqlite3`. That bundle ships a **plain** SQLite native library and
registers it as the process-wide SQLitePCLRaw provider. Two consequences:

1. Both bundles ship an assembly called `SQLitePCLRaw.batteries_v2`, so which provider
   `Batteries_V2.Init()` selects becomes a build-ordering accident. If `e_sqlite3` wins,
   `PRAGMA key` is an unrecognised no-op and the till's database is written **unencrypted** while
   every test still passes. That is a silent breach of NFR-S3.
2. `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 carries advisory GHSA-2m69-gcr7-jv3q, so restore fails
   outright under the repository's NuGet audit settings.

## Options considered

**Keep `Microsoft.EntityFrameworkCore.Sqlite` and pin the transitive bundle away.**
Central transitive pinning could force a non-vulnerable `SQLitePCLRaw.lib.e_sqlite3`, but the
duplicate-provider problem remains, and the failure mode is an unencrypted database rather than a
build error. Rejected: the risk is invisible.

**Drop EF Core's SQLite provider and use Dapper only.**
Removes the conflict, but also removes migrations, which DM-05 and P0-T04 depend on. Rejected.

**Use `Microsoft.EntityFrameworkCore.Sqlite.Core`.**
Same package family, same version, same public API - it is the published variant that omits the
bundled native library so the application chooses its own. It is the combination SQLCipher and
`Microsoft.Data.Sqlite.Core` are documented to be used with.

## Decision

`Directory.Packages.props` pins `Microsoft.EntityFrameworkCore.Sqlite.Core` 10.0.0 in place of
`Microsoft.EntityFrameworkCore.Sqlite` 10.0.0. `SQLitePCLRaw.bundle_e_sqlcipher` is then the only
provider in the graph, so `Batteries_V2.Init()` can only select SQLCipher.

This is a substitution, not a new dependency: nothing else in the solution referenced the
displaced package. Licence is unchanged (MIT, Microsoft).

## Consequences

- The native SQLCipher library must be present in a self-contained publish. That is exactly the
  week-one packaging risk P0-T03 exists to retire, and it is now the *only* provider, so a missing
  native asset fails loudly at start-up rather than quietly falling back to plain SQLite.
- `PosConnectionFactory` owns provider initialisation (`Batteries_V2.Init()`, once per process).
- Revisit if EF Core ever ships a first-party encrypted-SQLite provider, or if the bundling story
  changes in a future EF release.
