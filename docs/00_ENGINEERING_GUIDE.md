# Engineering Guide

> Copy this file to the repository root as `CLAUDE.md`. It is the standing context for every development session.

---

## 1. What this system is

An offline-first Point of Sale and inventory system for a **single-cashier hardware shop**. One machine, one till, one active session. The local SQLite database is the single source of truth. The internet is used for exactly one thing: shipping encrypted backup copies off-site.

Read `docs/Counterpoint_Requirements.md` (SRS) for what it must do and `docs/POS_Architecture_Design.md` (SAD) for why it is built this way.

### Things this system is not, and must never become

- Not client/server. There is no API, no HTTP listener, no LAN component.
- Not multi-user or multi-terminal. Single named-mutex instance.
- Not cloud-dependent. **No code path in a sale, return, price lookup or report may touch the network.**
- Not eventually consistent. Every business operation is one local ACID transaction.

If a task appears to require any of the above, stop and raise it. It means the requirement was misread.

### Development platform and the hardware boundary

Development and CI run on **Linux**; the product ships on **Windows**. Windows-only
surfaces — raw spooler printing (`winspool`), DPAPI, Windows Credential Manager,
`System.IO.Ports` — sit behind interfaces in `Devices` and `Infrastructure` with a
Linux development implementation: `IReceiptPrinter` → `FileReceiptPrinter` (writes
the ESC/POS byte stream to `artifacts/receipts/*.bin`), `IDatabaseKeyStore` →
`FileKeyStore` (development only, never shipped), `IScale` → `NullScale`. Guard real
Windows implementations with `OperatingSystem.IsWindows()` and
`[SupportedOSPlatform("windows")]`.

**The software is built and accepted against these fakes first.** Real-device
drivers and every physical verification are a separate **hardware-integration
track**, `docs/09_HARDWARE_INTEGRATION.md` (`HW-T01…HW-T10`, all `mode=human`), run
on site after the software is feature-complete and before `P5-T09` go-live. A task
in phases 0–5 keeps only what a byte-stream snapshot or a fake-failure test can
prove on Linux; anything needing a printer, scanner, scale, the terminal or a clean
Windows machine is a checkbox on a named `HW-T*` task.

**Software-complete milestone:** `dotnet test` green (architecture + every `AC*`
class), `P1-T16` / `P2-T12` / `P3-T09` / `P4-T08` all passed in CI, and the app
reaches the sales screen under the EF Core compiled model. The HW track starts
there.

---

## 2. Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 (LTS), `win-x64`, self-contained, ReadyToRun, **trimming disabled** |
| Language | C# 14, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` |
| UI | Avalonia 11.x, MVVM via `CommunityToolkit.Mvvm` |
| Database | SQLite 3 via `SQLitePCLRaw.bundle_e_sqlcipher` |
| Writes / migrations | EF Core 10 (`Microsoft.EntityFrameworkCore.Sqlite`) |
| Hot reads / reports | Dapper |
| PDF | QuestPDF |
| XLSX / CSV | ClosedXML / CsvHelper |
| Barcode & QR raster | ZXing.Net |
| Password hashing | Konscious.Security.Cryptography.Argon2 |
| Receipt templates | Scriban |
| Logging | Serilog, rolling file, 14-day retention |
| Tests | xUnit, FluentAssertions, Verify (snapshot), Bogus (seed data) |

Do not add a dependency without a note in `docs/adr/` saying why. Every dependency must be permissively licensed — the source is handed to the owner (NFR-M5).

---

## 3. Solution layout

```
Counterpoint.sln
├─ src/
│  ├─ Counterpoint.Domain/          entities, value objects, business rules. ZERO dependencies.
│  ├─ Counterpoint.Application/     use cases, ports (interfaces), authorisation
│  ├─ Counterpoint.Infrastructure/  EF Core, Dapper, SQLite, migrations, repositories
│  ├─ Counterpoint.Devices/         ESC/POS, drawer, label printer, serial scale, scanner filter
│  ├─ Counterpoint.Reporting/       PDF / XLSX / CSV renderers
│  ├─ Counterpoint.Backup/          snapshot, compress, encrypt, targets
│  ├─ Counterpoint.Ui/              Avalonia views + viewmodels (class library)
│  └─ Counterpoint.App/             the executable: composition root and nothing else
├─ tests/
│  ├─ Counterpoint.Domain.Tests/
│  ├─ Counterpoint.Integration.Tests/
│  ├─ Counterpoint.Device.Tests/
│  └─ Counterpoint.Acceptance.Tests/    one test class per AC-01…AC-20
├─ tools/
│  ├─ SeedGenerator/               20k SKUs + 100k lines for AC-18
│  └─ EscPosCapture/               dumps rendered receipt bytes for inspection
├─ installer/                      Inno Setup script
├─ portal/                         cloud backup portal (separate deployable)
└─ docs/
```

### Dependency rules — enforced by an architecture test

| Project | May reference |
|---|---|
| `Domain` | nothing |
| `Application` | `Domain` |
| `Infrastructure` | `Application`, `Domain` |
| `Devices` | `Application`, `Domain` |
| `Reporting` | `Application`, `Domain` |
| `Backup` | `Application`, `Domain` |
| `Ui` | `Application`, `Domain` **only** |
| `App` | everything — it is the composition root |

`Ui` must never reference `Infrastructure`, `Devices`, `Reporting` or `Backup` directly — it gets them through interfaces registered in the composition root. This is what makes NFR-S2 and AC-17 ("authorisation verified at the business-logic layer, not just the UI") structurally true.

**The composition root is therefore `Counterpoint.App`, not `Counterpoint.Ui`.** A project cannot wire up assemblies it is forbidden to reference, so the wiring lives one level out: `Counterpoint.App` is the `WinExe` (`Program.Main`, `BuildAvaloniaApp`, the `IHost` and `IServiceCollection` setup, `app.manifest`), `Counterpoint.Ui` is a class library of views and viewmodels, and `App` is the **only** project in the solution allowed to see both the UI and the adapters behind it. It contains wiring and nothing else — no rule, no query, no layout.

Write both rules as tests in `Counterpoint.Domain.Tests/ArchitectureTests.cs` in Phase 0. They fail the build if violated.

---

## 4. Non-negotiable invariants

These are the rules that, if broken, cost the shop money or credibility. Treat a violation as a build-breaking bug.

### 4.1 Money and quantity

- **All money and all quantities are stored as 64-bit `INTEGER`, scaled by 10 000.** `MoneyScale = 10_000`, `QtyScale = 10_000`.
- In C#, money is the `Money` value object wrapping `decimal`; quantity is the `Quantity` value object wrapping `decimal` + `UomId`.
- **`double` and `float` are banned in `Domain`, `Application` and `Infrastructure`.** An analyzer rule or an architecture test must enforce this.
- All arithmetic happens in C# `decimal`. SQL does addition and `SUM()` only — never multiplication or division of money.
- Rounding happens at exactly **two** points: line total, and bill total. Nowhere else. It goes through `IRoundingPolicy` (default: half away from zero, configurable per FR-10.2).
- Before any sale, return or GRN is persisted, the domain asserts:
  - `sum(line_total) == subtotal`
  - `subtotal - bill_discount + tax + rounding == total`
  - `sum(payments) == total` (for a completed sale)
  A failure throws. It never silently corrects.

### 4.2 Stock

- `stock_movement` is an **append-only ledger**. It is the truth.
- `stock_balance` is a **projection**, written in the same transaction as every movement. It is a cache and must be rebuildable from the ledger by `RebuildStockBalanceCommand`.
- Never `SUM(stock_movement)` on a read path. Never write `stock_balance` without a matching movement row.
- Every movement carries `balance_after` in base units, computed inside the transaction.

### 4.3 Document numbering

- Numbers come from `number_sequence` via `UPDATE … SET next_val = next_val + 1 … RETURNING next_val`, **inside the business transaction**.
- Never `AUTOINCREMENT`, never `MAX(n)+1`.
- A cancelled document keeps its number with `status = 'cancelled'`. Numbers are never reused and never rolled back. This is what makes the series gapless (AC-19).

### 4.4 Append-only tables

`sale`, `sale_line`, `payment`, `sale_return`, `sale_return_line`, `stock_movement`, `shift`, `cash_movement`, `audit_log`.

- Protected by `BEFORE UPDATE` / `BEFORE DELETE` triggers that `RAISE(ABORT, …)`.
- The **only** permitted exceptions, each enforced by a column-scoped trigger:
  - `sale_line.qty_returned` (incremented by a return)
  - `sale.status` (`completed` → `cancelled`, one direction only)
  - `shift` close fields (`closed_at`, `counted_cash`, `expected_cash`, `variance`, `status`), settable once
- No repository, service or EF configuration may expose an update or delete path to these tables outside those exceptions.

### 4.5 Tamper evidence

`sale` and `audit_log` carry `prev_hash` and `row_hash`:
`row_hash = SHA256(prev_hash ‖ canonical_json(row))`, computed inside the transaction.
`VerifyChainCommand` walks each chain and reports the first break.

### 4.6 Never block the sale

Printer, scanner, drawer, scale, network and backup failures **degrade with a warning and never block, delay or roll back a sale** (C-05, FR-7.8, AC-16).

Concretely: the print job is a row in the `print_job` outbox written inside the sale transaction. A background worker prints it afterwards. **No printer call is ever made inside a database transaction.**

### 4.7 Authorisation

- Every `Application` service method that a cashier may not perform is decorated `[RequiresRole(Role.Owner)]` and checked by a decorator before any repository call.
- Cost, margin and profit are excluded at the **query projection level** for cashier sessions — the DTO has no cost field, so there is nothing to leak (AC-17).
- Owner overrides (unlinked refund, over-limit discount, non-returnable override, no-sale drawer, restore, price below cost) all go through one `IOwnerOverrideService.RequestAsync(action, reason)` which re-authenticates and writes an audit row with both user IDs and before/after JSON.

### 4.8 Database connection settings

Set on **every** connection, without exception:

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous  = FULL;    -- NFR-R2. Do not "optimise" this to NORMAL.
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
PRAGMA temp_store   = MEMORY;
PRAGMA cache_size   = -20000;
```

One long-lived **write** connection, serialised behind a `SemaphoreSlim`. A small pool of read connections. All writes go through `IUnitOfWork`.

### 4.9 Data directory

`%ProgramData%\Counterpoint\` — never `%ProgramFiles%` (file virtualisation), never a network path, never inside a OneDrive/Google Drive sync root (SQLite corruption). The installer and the app both validate this and refuse to start otherwise.

---

## 5. Coding conventions

- `Domain` has no framework types: no EF attributes, no `DbContext`, no `IServiceProvider`, no `Task` in business rules that don't need it.
- Entities are constructed through factory methods or constructors that enforce invariants. No public parameterless constructors with settable state.
- Value objects: `Money`, `Quantity`, `UomConversion`, `BillNumber`, `TaxRate`, `Percentage`. Immutable `readonly record struct` where possible.
- Application services return **DTOs**, never entities, never `IQueryable`.
- Async everywhere that touches I/O; `ConfigureAwait(false)` in non-UI projects.
- One public type per file. File name matches type name.
- Exceptions: `DomainException` (a business rule said no — show the message to the user in plain language per UI-06) vs everything else (a bug — log with correlation id, show "something went wrong, reference #####").
- No magic numbers. Business constants live in `Domain/Constants.cs` or in `app_setting`.
- Naming in the domain uses the **shop's** vocabulary, not the developer's: `Bill`, not `Invoice`; `Return`, not `Refund` (a refund is one *kind* of return); `GoodsReceipt`, not `PurchaseInvoice` (NFR-U3).

### Database naming

`snake_case` tables and columns, singular table names (`sale`, `sale_line`). EF Core maps via a naming convention configured once in `PosDbContext.OnModelCreating`. Every table has `id INTEGER PRIMARY KEY`. Money columns end in a unit-free noun (`total`, `unit_price`); quantity columns end in `_qty` or `qty_base`.

---

## 6. Testing standard

| Layer | Rule |
|---|---|
| `Domain` | Pure unit tests, no I/O. Every business rule (BR-*) has at least one test named for it. Property-based tests on rounding and discount allocation. |
| `Infrastructure` | Integration tests against a **real SQLite file** per test (temp directory, deleted after). No in-memory provider — it does not enforce foreign keys or triggers, which is exactly what we are testing. |
| `Devices` | Snapshot tests: render a known bill, compare the ESC/POS byte stream against a committed `.verified.txt`. |
| `Acceptance` | One test class per AC-01…AC-20, named `AC03_PartialReturnRefundsAtOriginalPrice`. These are the contract. |
| Performance | A software perf harness runs against the seeded database in CI as a **relative regression guard** — it fails the build on a >20% drift from `docs/perf-baseline.md`, not on an absolute NFR budget. The absolute NFR-P1…P7 pass/fail is measured on the shop terminal in `HW-T07` (`docs/09_HARDWARE_INTEGRATION.md`). |

**Definition of done for any task:**
1. All `Done when` checkboxes pass. Checkboxes that need a printer, scanner, scale,
   the shop terminal or a clean Windows machine belong to an `HW-T*` task, not this
   one — see `docs/09_HARDWARE_INTEGRATION.md`.
2. `dotnet test` green, including the architecture tests. The software perf harness,
   once it exists (`P1-T16`), must not regress >20% from `docs/perf-baseline.md`.
3. `dotnet build` with zero warnings.
4. The app still starts to the sales screen (on the dev machine this is indicative
   only; NFR-P6 is measured on the terminal in `HW-T07`).
5. Any new business rule has a test named after its SRS requirement id.
6. Any new schema change has an EF migration **and** a migration test.

---

## 7. Logging and diagnostics

- Serilog to `%ProgramData%\Counterpoint\logs\pos-.log`, daily rolling, 14 days.
- Log at `Information`: app start/stop, shift open/close, backup taken/uploaded, restore, migrations, device connect/disconnect, owner overrides.
- Log at `Warning`: print failure, upload failure, projection mismatch, negative stock sale, slow query (>250 ms).
- **Never log**: passwords, PINs, the backup passphrase, the DB key, OAuth tokens, full payment references.
- User-facing errors are plain language with a next step (UI-06). The log gets the stack trace; the cashier gets "The printer did not respond. The bill is saved — press F10 to reprint when it's back."

---

## 8. Performance budget (assert, don't hope)

| Operation | Budget | Note |
|---|---|---|
| Barcode scan → line on bill | 300 ms | Prepared statement on `barcode` unique index |
| Search results begin | 500 ms | FTS5, 120 ms UI debounce |
| Bill save + stock + print dispatch | 2 s | Printing is out of band |
| Bill lookup by number | 1 s | Unique index |
| Any 1-year report | 10 s | Rollup tables from Phase 3 |
| Cold start to sales screen | 10 s | Use an **EF Core compiled model**; target 2 s |

These budgets are verified on the shop terminal in `HW-T07`, against the seeded
database (20 000 SKUs, 100 000 lines) — not per task, and not on a dev machine. In
CI the software perf harness only guards against regression. `docs/perf-baseline.md`
stays blank until `HW-T07` populates it.

Performance work that matters, in order: compiled EF model, ReadyToRun publish, deferred module loading, covering indexes, rollups. Performance work that does not matter: caching the catalogue in memory (SQLite already is the cache).

---

## 9. Git and PR discipline

- One task = one branch = one PR. Branch `task/P1-T07-uom-conversion`.
- Commit messages follow **Conventional Commits**, scoped with the task id: `feat(P1-T07): UOM conversion in base units (FR-2.4, FR-2.5)`. A `commit-msg` hook (commitlint) and a CI job both reject non-conforming messages — this is enforced, not a convention people can forget.
- A PR that touches the schema must include the migration, the migration test and a `docs/01_DATA_MODEL.md` update in the same PR.
- Never merge with a failing acceptance test, even one from a later phase that has started passing early.
- Merging to `main` triggers the release pipeline: it derives the next semver version from the commits since the last release (`fix` → patch, `feat` → minor, `BREAKING CHANGE:` footer → major), tags it, updates `Directory.Build.props` and `CHANGELOG.md`, and publishes a GitHub Release. There is no manual version bump.
