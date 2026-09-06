# Counterpoint

Offline-first Point of Sale and inventory system for a **single-cashier hardware shop**.
One machine, one till, one active session. The local SQLite database is the single source of truth.

**Stack:** .NET 10 LTS · C# · Avalonia UI · SQLite + SQLCipher · EF Core (writes/migrations) + Dapper (hot reads/reports).

## Read these before writing code

| When | Read |
|---|---|
| Always | This file |
| Any schema, entity or migration work | `docs/01_DATA_MODEL.md` |
| Starting a task | The task's own section in `docs/0{2..7}_PHASE_*.md` |
| Architecture or dependency questions | `docs/POS_Architecture_Design.md` |
| Requirement ids (FR-*, NFR-*, AC-*) | `docs/Counterpoint_Requirements.md` |
| Full conventions and definition of done | `docs/00_ENGINEERING_GUIDE.md` |
| Task order and status | `docs/08_TASK_INDEX.md` and `.claude/state/PROGRESS.md` |
| Any real-device driver or on-site verification | `docs/09_HARDWARE_INTEGRATION.md` (the HW track) |

## What this system is not, and must never become

- Not client/server. No API, no HTTP listener, no LAN component.
- Not multi-user or multi-terminal. Single named-mutex instance.
- Not cloud-dependent. **No code path in a sale, return, price lookup or report may touch the network.**
- Not eventually consistent. Every business operation is one local ACID transaction.

If a task appears to require any of the above, stop and say so. It means the requirement was misread.

## Non-negotiable invariants

Violating one of these is a build-breaking bug, not a style issue.

1. **Money and quantity are `INTEGER` scaled by 10 000**, mapped to C# `decimal` via the `Money` and `Quantity` value objects. `double` and `float` are banned in `Domain`, `Application` and `Infrastructure` — an architecture test enforces this.
2. **Rounding happens at exactly two points**: line total and bill total. Via `IRoundingPolicy`. Nowhere else.
3. **Every stock change goes through `StockLedger.PostAsync`.** `stock_movement` is the append-only truth; `stock_balance` is a projection written in the same transaction and rebuildable from the ledger. Never `SUM(stock_movement)` on a read path.
4. **Document numbers come from `number_sequence`** via `UPDATE ... RETURNING`, inside the business transaction. Never `AUTOINCREMENT`, never `MAX(n)+1`. A cancelled document keeps its number.
5. **Append-only tables** (`sale`, `sale_line`, `payment`, `sale_return`, `sale_return_line`, `stock_movement`, `shift`, `cash_movement`, `audit_log`) are trigger-protected. The only permitted updates are `sale_line.qty_returned`, `sale.status` (completed→cancelled), and the `shift` close fields, each column-scoped by trigger.
6. **`sale` and `audit_log` are hash-chained** (`row_hash = SHA256(prev_hash ‖ canonical_json(row))`), computed inside the transaction.
7. **Never block the sale.** Printer, scanner, drawer, scale, network and backup failures degrade with a warning. The print job is a row in the `print_job` outbox written inside the sale transaction. **No printer call is ever made inside a database transaction.**
8. **Authorisation lives in the Application layer**, not the UI. Cost and margin are excluded at the query projection level for cashier sessions — the DTO has no cost field.
9. **PRAGMAs on every connection**: `journal_mode=WAL`, `synchronous=FULL`, `foreign_keys=ON`, `busy_timeout=5000`. `synchronous=FULL` is a durability requirement (NFR-R2); do not "optimise" it to NORMAL.
10. **`sale_line` snapshots** `description`, `unit_price` and `unit_cost`. Returns refund at the price originally paid; margin reports use the cost at time of sale. Neither is recoverable from the catalogue.

## Project boundaries

```
Domain          -> nothing
Application     -> Domain
Infrastructure  -> Application, Domain
Devices         -> Application, Domain
Reporting       -> Application, Domain
Backup          -> Application, Domain
Ui              -> Application, Domain   (never Infrastructure/Devices/Reporting/Backup)
```

Enforced by `tests/Counterpoint.Domain.Tests/ArchitectureTests.cs`. Do not work around it.

## Working agreement

- **One task at a time.** Tasks are defined in `docs/0{2..7}_PHASE_*.md` with ids like `P1-T07`. Do not build ahead. If a task needs something from a later task, stop and say so.
- Use `/next-task` to start the next planned task. Use `/task-status` to see where things stand.
- Every task ends with `dotnet test` green and the app still starting. The build is never left red between tasks.
- A PR that touches the schema includes the migration, the migration test and the `docs/01_DATA_MODEL.md` update.
- Commit messages **must** follow [Conventional Commits](https://www.conventionalcommits.org/), enforced by a `commit-msg` hook (commitlint) and again in CI on every PR — neither can be skipped by convention alone. The scope carries the task id: `feat(P1-T07): UOM conversion in base units (FR-2.4, FR-2.5)`. Use `fix`, `feat`, `docs`, `refactor`, `perf`, `test`, `build`, `ci` or `chore`; add a `BREAKING CHANGE:` footer only for an intentional major bump. Merges to `main` are released automatically — the next version is computed from these commits (semantic-release), tagged, and published as a GitHub Release. Never hand-edit `<Version>` in `Directory.Build.props`.
- Update `.claude/state/PROGRESS.md` when a task's status changes. It is the ledger the commands read.

## Development platform note

Development and CI run on **Linux** (including Claude Code on the web); the product ships on **Windows**.

- Avalonia, EF Core, SQLite/SQLCipher, QuestPDF and the whole domain build and test on Linux.
- Windows-only surfaces — raw spooler printing (`winspool`), DPAPI, Windows Credential Manager, `System.IO.Ports` — sit behind interfaces in `Devices` and `Infrastructure` with a Linux development implementation:
  - `IReceiptPrinter` -> `FileReceiptPrinter` writes the ESC/POS byte stream to `artifacts/receipts/*.bin`
  - `IDatabaseKeyStore` -> `FileKeyStore` (development only, never shipped)
  - `IScale` -> `NullScale`
- Guard real Windows implementations with `OperatingSystem.IsWindows()` and `[SupportedOSPlatform("windows")]`.
- **Device snapshot tests must pass on Linux.** They compare byte streams, not hardware behaviour.

### Hardware boundary — build the software first

The whole system is built and accepted **against the Linux fakes** (`FileReceiptPrinter`,
`NullScale`, `FileKeyStore`, a temp-directory "USB" target). Real-device drivers
(`winspool`, `System.IO.Ports`, DPAPI) and every physical verification live in a
separate **hardware-integration track**, `docs/09_HARDWARE_INTEGRATION.md`
(`HW-T01…HW-T10`, all `mode=human`), run on site after the software is
feature-complete and before `P5-T09` go-live.

- A software task in phases 0–5 keeps only the checks a byte-stream snapshot or a
  fake-failure test can prove on Linux. Anything that needs a printer, scanner,
  scale, the terminal, or a clean Windows machine is a checkbox on a named `HW-T*`
  task, not a blocker for the software task.
- **`/perf-gate` produces real NFR-P1…P7 figures only in `HW-T07`, on the shop
  terminal.** Do not record dev-machine or CI numbers in `docs/perf-baseline.md` —
  a figure without the shop's hardware behind it is not a figure. Its seven budget
  rows stay blank until then, deliberately.
- **Software-complete milestone:** `dotnet test` green (architecture + every `AC*`),
  `P1-T16` / `P2-T12` / `P3-T09` / `P4-T08` passed, app reaches the sales screen
  under the compiled model. The HW track starts there.

## Commands

`/next-task` `/task-status` `/plan-feature` `/add-feature` `/fix-bug` `/test-tasks` `/review` `/acceptance` `/migration` `/perf-gate` `/verify` `/handoff`

Run `/verify` before declaring any task done.
