# Phase 0 — Walking Skeleton

**Duration:** 1 week · **Tasks:** 7 · **Exit:** one product, one sale, one printed receipt, one encrypted backup, one installer — end to end on the real hardware.

## Why this phase exists

The SRS phasing goes straight into core trading. That defers three risks — ESC/POS behaviour on the actual printer, SQLCipher packaging, and self-contained installer size and start time — to the point where they are most expensive to discover. This phase retires all three in week one with throwaway-thin functionality.

**Nothing here is production quality except the plumbing.** One hard-coded product, no catalogue screen, no auth beyond a stub. Resist the urge to build features.

**Blocker:** Q-H (exact printer and scanner models). Buy them before this phase starts. A skeleton that prints to a simulator proves nothing.

---

### P0-T01 — Solution scaffold and architecture tests
**Depends on:** — · **Est:** 1d · **SRS:** NFR-M5, SAD §5

**Context.** The project boundaries in `00_ENGINEERING_GUIDE.md` §3 are what keep authorisation and business rules out of the UI later (AC-17). If they are not enforced mechanically from day one, they will erode.

**Do this.**
1. `dotnet new sln -n Counterpoint`. Create all seven `src` projects and four `tests` projects per the layout in the engineering guide.
2. `Directory.Build.props` at the root: `net10.0`, `nullable enable`, `TreatWarningsAsErrors`, `LangVersion latest`, `InvariantGlobalization false`, deterministic builds.
3. Set project references exactly per the dependency table. `Domain` gets zero package references.
4. Write `ArchitectureTests.cs`:
   - `Ui` does not reference `Infrastructure`, `Devices`, `Reporting` or `Backup`.
   - `Domain` references no NuGet package.
   - No type in `Domain`, `Application` or `Infrastructure` has a `double` or `float` field, property, parameter or return type. (Reflection scan over all public and non-public members.)
5. Add `.editorconfig`, `.gitignore`, GitHub Actions workflow running `dotnet build` + `dotnet test` on push.
6. Add `docs/` with the SRS, SAD and this plan; copy the engineering guide to `CLAUDE.md`.

**Deliverables.** Compiling solution, green CI, `CLAUDE.md`, `ArchitectureTests.cs`.

**Risks.** Skipping the architecture test because "we'll be careful." The test costs two hours and prevents a two-week untangling in Phase 3.

**Done when.**
- [ ] `dotnet build` succeeds with zero warnings
- [ ] `dotnet test` runs and the three architecture tests pass
- [ ] A deliberate violation (add an `Infrastructure` reference to `Ui`) fails the test suite, then is reverted
- [ ] CI is green on the default branch

---

### P0-T02 — Money and Quantity value objects
**Depends on:** P0-T01 · **Est:** 1d · **SRS:** DM-01, DM-02, FR-10.2

**Context.** Every money bug in this system is prevented or caused here. Build this before anything that touches a price.

**Do this.**
1. `Domain/ValueObjects/Money.cs` — `readonly record struct Money` wrapping `decimal`.
   - `Money.FromScaled(long)`, `Money.FromDecimal(decimal)`, `.ToScaled()` using `MoneyScale = 10_000`.
   - Operators `+ - * (by decimal) / (by decimal) < > == !=`, `Zero`, `IsNegative`, `Abs`, `Negate`.
   - `ToScaled()` throws `OverflowException` outside ±9.2 × 10¹⁴.
2. `Domain/ValueObjects/Quantity.cs` — `decimal` value + `UomId`, `QtyScale = 10_000`. Arithmetic between quantities requires the same `UomId` or throws.
3. `Domain/ValueObjects/Percentage.cs` and `TaxRate.cs`, scaled ×10 000.
4. `Domain/Services/IRoundingPolicy.cs` + `HalfAwayFromZeroRounding` implementation, parameterised by decimal places from settings.
5. `Domain/Services/DiscountAllocator.cs` — distributes a bill-level discount across lines proportionally such that the allocated parts **sum exactly** to the discount. Largest-remainder method; the residual cent goes to the largest line.

**Deliverables.** The value objects, the rounding policy, the allocator, and their tests.

**Risks.** Implementing scaling with `double` intermediates. Use `decimal` throughout and `Math.Round(value, digits, MidpointRounding.AwayFromZero)`.

**Done when.**
- [ ] Round-trip property test: for 10 000 random decimals in range, `FromScaled(x.ToScaled()) == x` to 4 dp
- [ ] Property test: `DiscountAllocator` output always sums exactly to the input discount, for random line sets of size 1–50
- [ ] Rounding test covers `.5` cases in both signs
- [ ] Overflow throws rather than wrapping
- [ ] No `double` anywhere in `Domain` (architecture test still passes)

---

### P0-T03 — Database bootstrap, SQLCipher, connection factory
**Depends on:** P0-T01 · **Est:** 1.5d · **SRS:** NFR-M2, NFR-R2, NFR-S3, NFR-S6, DM-04

**Context.** SQLCipher packaging in a self-contained publish is the most likely week-one surprise. Prove it now, including from the installed location, not just from `bin/Debug`.

**Do this.**
1. Add `SQLitePCLRaw.bundle_e_sqlcipher`, `Microsoft.EntityFrameworkCore.Sqlite`, `Dapper`.
2. `Infrastructure/Data/PosConnectionFactory.cs`:
   - Resolves the data directory to `%ProgramData%\Counterpoint\`. **Refuses to start** if it is a UNC path, a mapped drive, or under a known sync root (`OneDrive`, `Google Drive`, `Dropbox`) — log and throw a plain-language error.
   - Opens with the SQLCipher key, then applies all PRAGMAs from the engineering guide §4.8 on every connection.
   - Exposes one write connection guarded by `SemaphoreSlim(1,1)` and a read connection factory.
3. `Infrastructure/Security/DatabaseKeyStore.cs`: generate a 256-bit key on first run, protect with DPAPI (`ProtectedData`, `CurrentUser`), store in Windows Credential Manager. Retrieve on start.
4. `PosDbContext` with a `snake_case` naming convention applied in `OnModelCreating`; no entities yet beyond `schema_version`.
5. `IUnitOfWork` with `ExecuteInTransactionAsync(Func<...>)` using `BEGIN IMMEDIATE`.
6. Integration test base class that creates a real encrypted SQLite file in a temp directory and deletes it after.

**Deliverables.** Connection factory, key store, `PosDbContext`, `IUnitOfWork`, integration test harness.

**Risks.** `e_sqlcipher` native assets not copied into a self-contained publish. Verify by publishing and running from `%ProgramFiles%`, not just from the IDE. Also: `synchronous = FULL` will be "optimised away" by someone later — put the reason in a code comment and in a test that asserts the pragma value.

**Done when.**
- [ ] A database file is created, closed, reopened and read with the stored key
- [ ] Opening the file with the wrong key fails; opening it with a plain SQLite tool fails
- [ ] Test asserts `journal_mode=wal`, `synchronous=2`, `foreign_keys=1` on a freshly opened connection
- [ ] Pointing the data directory at a OneDrive path produces a clear refusal, not a crash
- [ ] Works from a **published, self-contained** build run from `%ProgramFiles%`

---

### P0-T04 — Minimal schema, migrations and append-only triggers
**Depends on:** P0-T03 · **Est:** 1d · **SRS:** DM-03, DM-04, DM-05, NFR-M3

**Context.** Prove the migration and trigger mechanism now with a handful of tables. Phase 1 adds the rest of the schema through the same pipe.

**Do this.**
1. EF migration `0001_Skeleton` creating: `app_user`, `uom`, `tax_class`, `product`, `product_variant`, `stock_balance`, `stock_movement`, `sale`, `sale_line`, `payment`, `shift`, `number_sequence`, `audit_log`, `print_job`, `schema_version`. Use the DDL in `01_DATA_MODEL.md` verbatim, including CHECK constraints and indexes.
2. Add append-only triggers for `stock_movement`, `sale`, `payment`, `audit_log` via `migrationBuilder.Sql(...)`.
3. `MigrationRunner`: on startup, compare `schema_version`; if a migration is pending, take a **pre-migration backup copy of the file** first, run migrations in a transaction, write the new version, verify with `PRAGMA integrity_check`.
4. Migration test: apply all migrations to an empty DB, then to a seeded DB, assert both succeed and `integrity_check` returns `ok`.

**Deliverables.** Migration `0001`, `MigrationRunner`, migration tests.

**Risks.** EF Core's SQLite provider cannot drop or alter columns; it rebuilds tables, which **silently drops triggers**. Every migration that rebuilds a table must recreate its triggers. Add a test that asserts every append-only table still has both triggers after the full migration chain.

**Done when.**
- [ ] Migrations apply cleanly to an empty database
- [ ] `UPDATE stock_movement SET qty_base = 0` raises `SQLITE_CONSTRAINT` with the append-only message
- [ ] `DELETE FROM audit_log` raises
- [ ] Inserting a `sale_line` with a non-existent `sale_id` fails on the foreign key
- [ ] Trigger-survival test passes after the full migration chain
- [ ] A pre-migration backup file exists after a migration run

---

### P0-T05 — ESC/POS renderer and one printed receipt
**Depends on:** P0-T03 · **Est:** 1.5d · **SRS:** FR-7.1, FR-7.7, §10, §14.2

**Context.** This is the highest-uncertainty external interface in the project. The goal is a real receipt out of the real printer, with a real drawer kick, this week.

**Do this.**
1. `Devices/Printing/RawPrinter.cs` — P/Invoke `OpenPrinter` / `StartDocPrinter(datatype "RAW")` / `WritePrinter` / `EndDocPrinter`. Enumerate installed printers.
2. `Devices/Printing/EscPos.cs` — command constants: init, align, bold, double height/width, feed, partial cut, drawer kick (`ESC p 0 25 250`), codepage select, `GS k` barcode, `GS v 0` raster.
3. `Devices/Printing/ReceiptIr.cs` — intermediate representation: `TextLine(text, align, bold, doubleHeight)`, `Columns(left, right)`, `Divider`, `Barcode(data, symbology)`, `QrCode(data)`, `Feed(n)`, `Cut`, `Kick`. Design it so a `RasterText` node can be added later without touching the renderer's callers (Q-C).
4. `Devices/Printing/EscPosRenderer.cs` — IR → `byte[]`. 80 mm = 48 characters at font A; wrap and truncate correctly; right-align amounts in a fixed money column.
5. Hard-code one specimen receipt matching SRS §10.1 and print it.

**Deliverables.** Raw printer, ESC/POS command set, IR, renderer, one specimen receipt printed on the shop's actual printer.

**Risks.** The printer claims ESC/POS but differs on cut, codepage or barcode commands. Capability-flag anything the model gets wrong and record it in `docs/adr/printer-quirks.md`. If barcodes render badly with `GS k`, fall back to a ZXing raster — decide this now, not in Phase 3.

**Done when.**
- [ ] A receipt matching the §10.1 layout prints on the actual hardware, correctly aligned, and cuts
- [ ] The cash drawer opens on the kick command
- [ ] Snapshot test: the specimen receipt's byte stream matches a committed `.verified.txt`
- [ ] Disconnecting the printer and printing produces a caught, logged error — not an unhandled exception
- [ ] Printer quirks documented

---

### P0-T06 — One sale, end to end
**Depends on:** P0-T02, P0-T04, P0-T05 · **Est:** 1.5d · **SRS:** FR-3, DM-03, NFR-P3

**Context.** The thinnest possible vertical slice through every layer: Avalonia view → viewmodel → application service → domain → transaction → outbox → printer. It establishes the transaction shape that Phase 1 fills out.

**Do this.**
1. Seed one `uom`, one `tax_class`, one `product` + `product_variant` + `barcode`, one `OWNER` user, one open `shift`, and the `SALE` number sequence.
2. `Application/Sales/CompleteSaleCommand` + handler executing the exact transaction shape from SAD §7: allocate `bill_no` → insert `sale` (with hash chain) → `sale_line` → `payment` → `stock_movement` → update `stock_balance` → `audit_log` → `print_job`. One `BEGIN IMMEDIATE`.
3. `Devices/Printing/PrintWorker` — `BackgroundService` polling `print_job` where `status = 'PENDING'`, rendering, printing, marking `PRINTED` or retrying to `FAILED`.
4. Minimal Avalonia window: a scan box, a line list, a total, and a Pay button. No styling.
5. Composition root wiring everything with `Microsoft.Extensions.DependencyInjection` and `IHostedService` for the worker.

**Deliverables.** One working sale path, print outbox worker, minimal shell window.

**Risks.** Calling the printer inside the transaction. Assert it: the sale transaction must complete with the printer physically unplugged.

**Done when.**
- [ ] Typing the seeded barcode adds a line; Pay produces a persisted sale and a printed receipt
- [ ] The same flow completes successfully **with the printer unplugged**; the sale is saved and the job sits in `PENDING`/`FAILED` (AC-16 in miniature)
- [ ] `bill_no` is `INV-2026-000001`; a second sale is `…000002`
- [ ] `stock_balance` decreased and one `stock_movement` row exists with correct `balance_after`
- [ ] `sale.row_hash` verifies against `prev_hash`
- [ ] Bill save measured under 2 s (NFR-P3)

---

### P0-T07 — Backup snapshot, encryption, installer
**Depends on:** P0-T06 · **Est:** 1.5d · **SRS:** FR-11.1–11.4, NFR-M2, NFR-P6

**Context.** Packaging is the other week-one risk: self-contained size, cold start, native SQLCipher assets, data directory permissions. Prove it before there is anything to lose.

**Do this.**
1. `Backup/SnapshotService`: `VACUUM INTO` a temp file, zstd compress, AES-256-GCM encrypt with a key from Argon2id over a passphrase, write a header (magic, version, salt, nonce, schema version, taken-at), SHA-256 the ciphertext, write to the local backup folder, insert `backup_record`.
2. `Backup/RestoreService` (local only for now): decrypt, verify checksum, `PRAGMA integrity_check`, open and count rows.
3. `dotnet publish -r win-x64 --self-contained -p:PublishReadyToRun=true`, trimming off.
4. Inno Setup script: install to `%ProgramFiles%\Counterpoint`, create `%ProgramData%\Counterpoint\{db,backups,logs}` with appropriate ACLs, desktop shortcut, uninstaller.
5. Measure and record cold start on the target hardware.

**Deliverables.** Snapshot/restore services, publish profile, `installer/Counterpoint.iss`, a measured start-time figure.

**Risks.** Cold start over 10 s on the minimum spec. If so, apply the EF compiled model now (`dotnet ef dbcontext optimize`) rather than in Phase 3 — it is usually 1–2 s on its own.

**Done when.**
- [ ] A backup file is produced, encrypted, checksummed and recorded
- [ ] Restore into a scratch location reproduces the sale from P0-T06 exactly
- [ ] A wrong passphrase fails cleanly with a plain-language message
- [ ] The installer runs on a clean Windows machine with no .NET installed and the app starts
- [ ] Cold start to the sales window is measured on the **actual shop hardware** and is under 10 s (record the number in `docs/perf-baseline.md`)
- [ ] Installer size recorded

---

## Phase 0 exit review

Before starting Phase 1, confirm and write down:

| # | Question | Why it matters |
|---|---|---|
| 1 | Does the actual printer behave? Any quirks documented? | Rework cost rises steeply after Phase 1 |
| 2 | Cold start figure on real hardware? | If >6 s now, it will exceed 10 s by Phase 3 |
| 3 | Installer size and clean-machine install verified? | NFR-M2 |
| 4 | Is the team comfortable in C#/Avalonia after a week? | **Last cheap moment to switch to Tauri (SAD ADR-001 Option B)** |
| 5 | Q-01, Q-02, Q-12, Q-16 answered? | Blocks Phase 1 pricing and numbering |
