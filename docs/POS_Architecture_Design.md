# Software Architecture Document (SAD)
## Point of Sale & Inventory System — Single-Cashier Hardware Shop

| Field | Value |
|---|---|
| Document | SAD — Hardware Shop POS |
| Version | 1.0 (Draft) |
| Date | 3 September 2026 |
| Companion to | SRS v1.0 (Hardware_Shop_POS_Requirements.md) |
| Status | For technical review before Phase 1 |

Every decision below cites the SRS requirement that forces it. Where the SRS is silent or self-contradictory, the gap is listed in §17.

---

# 1. Architectural drivers

Most of the 200-odd functional requirements do not constrain the stack. These nine do:

| # | Driver | Source | What it rules out |
|---|---|---|---|
| D1 | Runs on a dual-core / 4 GB machine, cold start ≤ 10 s, scan-to-line ≤ 300 ms | §14.1, NFR-P1, NFR-P6 | Electron, JVM+Spring, anything with a local HTTP server and a browser on top |
| D2 | Single installer, no DB server to configure, installable by a non-technical owner | NFR-M2 | PostgreSQL/MySQL/SQL Server, Docker on the till |
| D3 | Fully functional offline, indefinitely; network never on the critical path | NFR-R1, C-02, C-05 | Any web-app-with-service-worker design; any cloud auth on login |
| D4 | Power loss must not corrupt the DB and must not lose the last committed bill | NFR-R2, AC-15 | Default async-commit settings; in-memory-then-flush designs |
| D5 | Exact money and 3-dp quantities, reports reconcile "to the cent" | DM-01, DM-02, FR-9.6, AC-12 | Floating-point money anywhere; JS `number` for currency without a decimal library |
| D6 | Raw ESC/POS bytes, drawer kick, serial scale, HID scanner | FR-7.1, FR-7.7, §14.2 | Sandboxed runtimes without raw printer/serial access |
| D7 | Single user, single session, no LAN server | C-01, §3.3 | Client/server split, auth tokens, multi-tenancy, ORM designed for concurrency |
| D8 | Source, schema and build handed to the owner; no vendor lock-in | NFR-M5, NFR-M6 | Proprietary report engines, paid runtimes, closed installers |
| D9 | Append-only, tamper-evident sales and audit records | DM-05, NFR-S8, NFR-L2 | Mutable ORM-managed aggregates with no ledger discipline |

**The system is a modular monolith desktop app over an embedded file database, plus a passive, unrelated cloud object store.** There is no distributed system here. Treating it as one would be the main way to get this wrong.

---

# 2. ADR-001 — Platform, language and UI framework

**Status:** Proposed · **Deciders:** Development lead, owner (cost/handover implications)

## Context

The application must be a native-feeling Windows desktop app, keyboard-driven, low memory, fast cold start, with direct access to printers and serial ports, delivered as one installer with an embedded database, and handed over as source.

## Options considered

### Option A — .NET 10 (LTS), C#, Avalonia UI, SQLite

| Dimension | Assessment |
|---|---|
| Complexity | Low–Medium |
| RAM at idle | ~100–160 MB |
| Cold start | ~1–2 s with ReadyToRun + compiled EF model |
| Installer size | ~70–90 MB self-contained (no runtime prerequisite) |
| Peripheral access | Native: Win32 spooler RAW passthrough, `System.IO.Ports`, HID via keyboard wedge |
| Money type | `decimal` is a built-in 128-bit base-10 type — D5 is solved by the language |
| Team familiarity | The gap. Not Django or NestJS. |
| Cost | Free, MIT/OSS toolchain end to end |

**Pros:** best fit on every hardware and correctness driver; single-file self-contained publish satisfies D2 and D8 directly; `decimal`, `DateTimeOffset`, and a mature migration story remove whole classes of bug from a money-handling app; .NET 10 is supported to November 2028 (.NET 8's support ends November 2026 — do not start there).

**Cons:** C#/XAML learning curve if the team is TypeScript-centric; Avalonia's third-party control ecosystem is thinner than WPF's.

### Option B — Tauri 2, Rust core, React + TypeScript UI, SQLite

| Dimension | Assessment |
|---|---|
| Complexity | Medium–High (two languages, IPC boundary) |
| RAM at idle | ~180–300 MB (WebView2 process tree) |
| Cold start | ~1.5–3 s |
| Installer size | ~15 MB + WebView2 bootstrapper on Windows 10 |
| Peripheral access | Workable but hand-rolled: RAW spooler via `winapi`, `serialport` crate |
| Money type | Must import `rust_decimal`; the TS side must never touch a currency value as a `number` |
| Team familiarity | High on the UI half, low on the Rust half |
| Cost | Free |

**Pros:** the UI half reuses existing React/TS strength and the receipt/report layout work can share code with the backup portal; smallest binary.
**Cons:** money correctness becomes a discipline problem across an IPC boundary rather than a type-system guarantee; ESC/POS and serial work is significantly more DIY; WebView2 dependency is one more thing that can be broken on a locked-down till.

### Option C — Electron + Node/TypeScript, better-sqlite3

**Rejected.** 350–600 MB RSS and 3–6 s cold start on a 4 GB dual-core violates D1 outright, and a Chromium runtime on a machine whose only job is to sell bolts is indefensible. Familiarity is not worth it here.

### Option D — Java 21 + JavaFX, or Python + PySide6

**Rejected.** JavaFX: heavier memory, jlink packaging complexity, weaker Win32 peripheral story. PySide6: startup time and PyInstaller packaging are both hostile to D1/D2, and Qt's licensing needs care against D8.

## Decision

**Option A: .NET 10 LTS + C# + Avalonia UI + SQLite.**

Take Option B only if the team's centre of gravity is TypeScript *and* someone owns the Rust half for the full maintenance life. Do not take Option C.

## ADR-001b — Avalonia vs WPF

Sub-decision, and the reason it matters is not aesthetic.

Your §14.1 minimum terminal (dual-core 2.0 GHz, 4 GB) describes hardware that in most cases **cannot run Windows 11** — Microsoft's floor is an 8th-gen Intel / Zen+ CPU with TPM 2.0. Windows 10 left mainstream support in October 2025. So NFR-M4 ("Windows 10/11") on §14.1 hardware means, in practice, *an unsupported OS running a system of financial record*.

There are three ways out, and you should pick one before Phase 1:

1. Procure Windows 11-capable hardware (a refurbished 8th-gen i3 / 8 GB SFF machine is inexpensive and comfortably exceeds the spec). Then WPF is fine.
2. Buy Windows 10 ESU for the remaining life of the machine. Cost per year, and it ends.
3. Run a Debian/Mint terminal on the existing low-power hardware.

**Avalonia keeps option 3 open at near-zero cost today**, because it is the same C#, XAML and MVVM as WPF from the developer's point of view. WPF closes it permanently. Given that your explicit goal is "very low-powered computers with minimal hardware," choose Avalonia. The printing and serial layers stay behind interfaces (§8) so a Linux implementation is a swap of two adapters, not a rewrite.

Choose WPF only if the shop commits in writing to Windows 11 hardware.

## Consequences

- Easier: correctness of money, packaging, peripheral access, resource footprint, long-term OS optionality.
- Harder: onboarding if the team is TS-first; hiring a maintainer locally who knows C#/Avalonia rather than JS.
- Revisit if: a second terminal or LAN sync is ever requested (that breaks C-01 and needs a real server tier — see §16 R4).

---

# 3. ADR-002 — Database

**Decision: SQLite 3 (bundled), WAL journalling, SQLCipher for encryption at rest, accessed through EF Core for writes and Dapper for hot reads.**

SQLite is not a compromise here; it is the correct answer. It is a single file, zero-administration (D2), transactional and crash-safe (D4), enforces foreign keys (DM-04), supports triggers for append-only enforcement (DM-05), ships FTS5 for name search (FR-2.11), and is the most widely deployed and best-tested database in existence — which matters for D8, since the owner can open their own data with free tools forever.

## Configuration (non-negotiable, set on every connection)

```sql
PRAGMA journal_mode = WAL;        -- readers never block the writer
PRAGMA synchronous  = FULL;       -- NFR-R2: commit survives power loss.
                                  -- NORMAL is faster but can lose the last
                                  -- transactions on power cut. Not acceptable
                                  -- for a bill the customer has paid for.
PRAGMA foreign_keys = ON;         -- DM-04
PRAGMA busy_timeout = 5000;
PRAGMA temp_store   = MEMORY;
PRAGMA cache_size   = -20000;     -- ~20 MB page cache; safe within 4 GB
PRAGMA wal_autocheckpoint = 1000;
```

`synchronous = FULL` costs one fsync per commit — roughly 5–15 ms on an SSD. Against NFR-P3's 2-second budget that is free, and it is what makes AC-15 pass.

## Encryption at rest (NFR-S3)

Use `SQLitePCLRaw.bundle_e_sqlcipher`. The database key is a 256-bit random value generated at install, wrapped with Windows DPAPI (machine+user scope) and held in Windows Credential Manager (NFR-S6). BitLocker/full-disk encryption is documented as an *additional* recommendation, not the primary control, because a POS box in a shop back room is physically reachable.

On Linux (if ADR-001b option 3 is taken), the equivalent is `libsecret` + a LUKS-encrypted volume.

## Money and quantity representation — the most important schema decision

SQLite has no decimal type, and `REAL` is banned by DM-01.

**Store all monetary values and all quantities as 64-bit signed `INTEGER` scaled by 10⁴, mapped to C# `decimal` at the boundary by an EF Core `ValueConverter`.**

| Concern | Resolution |
|---|---|
| Range | ±9.2 × 10¹⁸ ÷ 10⁴ = ±9.2 × 10¹⁴ currency units. Sufficient for any currency this shop will use. |
| Precision | Money to 4 dp covers unit costs and moving-average cost without drift; quantities to 4 dp exceed the 3 dp of Q-07. |
| SQL aggregation | `SUM()` over integers is exact and fast — this is what makes FR-9.6 / AC-12 reconcile *to the cent* rather than *to within a cent*. |
| Arithmetic | All multiplication, proportional discount allocation and tax splitting happen in C# `decimal`, never in SQL. |
| Rounding | One `IRoundingPolicy` implementation, half-away-from-zero by default, configurable per FR-10.2. Applied at exactly two points: line total, and bill total. Never anywhere else. |
| Invariant | `sum(line_total) == subtotal` and `subtotal - discount + tax + rounding == total` are asserted in the domain before the sale is persisted. A violation throws; it never silently rounds. |

Do **not** store money as `TEXT` (kills aggregation) or as `REAL` (kills DM-01).

## Current-state projection vs. ledger

`StockMovement` is an append-only event ledger (DM-05). Summing it on every barcode scan would blow NFR-P1 within a year.

Maintain a `stock_balance` projection table `(product_variant_id PK, qty_base, cost_avg, updated_at)`, written **inside the same transaction** as every movement insert. The ledger remains authoritative; the projection is a cache that can be rebuilt from the ledger by a maintenance command. Add a startup consistency check that verifies the projection against the ledger for a random sample of 200 SKUs and logs a discrepancy loudly.

## Gapless bill numbering (AC-19)

Do not use `AUTOINCREMENT` or `MAX(bill_no)+1`.

```sql
CREATE TABLE number_sequence (
  doc_type TEXT PRIMARY KEY,   -- 'SALE','RETURN','CREDIT_NOTE','GRN','PO'
  prefix   TEXT NOT NULL,
  next_val INTEGER NOT NULL
);
```

Allocation happens with `UPDATE number_sequence SET next_val = next_val + 1 WHERE doc_type = ? RETURNING next_val` inside the sale's own transaction. A cancelled bill keeps its number with `status = 'cancelled'` — that is precisely what makes the series gapless and auditable. Numbers are never reused and never rolled back.

## Tamper evidence (NFR-L2, NFR-S8)

Both `sale` and `audit_log` carry `prev_hash` and `row_hash`, where `row_hash = SHA256(prev_hash || canonical_json(row))`. A verification command walks the chain and reports the first break. Combined with `BEFORE UPDATE`/`BEFORE DELETE` triggers that `RAISE(ABORT, 'append-only')` on the six append-only tables, this gives DM-05 defence at the storage layer rather than by developer good intentions.

## Search (FR-2.11, NFR-P2)

An FTS5 external-content table over `product` and `product_variant` (name, name_alt, code, brand, category, rack location), kept current by triggers. At 50,000 SKUs (NFR-C1) this returns in low single-digit milliseconds — the 500 ms budget is spent almost entirely on UI debounce, which should be set to ~120 ms.

## Reporting at scale

NFR-C2 (1,000 bills/day, 5 years online) implies roughly 9–11 million sale lines. That is well within SQLite's capability but past the point where a naive `GROUP BY` over a year satisfies NFR-P5 on a dual-core machine.

Write two rollup tables at Z-report close, in the same transaction that locks the shift:

- `daily_sales_summary(date, bill_count, gross, discount, tax, net, cogs, tender_cash, tender_card, …)`
- `daily_product_summary(date, product_variant_id, qty_base, net, cogs)`

Long-range reports read rollups; sub-day and drill-down reports read the raw tables. Rollups are derived and fully rebuildable — never a second source of truth. Note that A-06/Q-06 (200–500 bills/day) and NFR-C2 (1,000/day) disagree; see §17 Q-A.

---

# 4. ADR-003 — Cloud backup tier

**Decision: an `IBackupTarget` abstraction with two shipped implementations — Google Drive (owner's own account, the SRS default per Q-09) and S3-compatible object storage (GCS / R2 / B2). Plus a ~200-line read-only web portal.**

The cloud tier is deliberately the least interesting component in the system (§4.4 of the SRS). Keep it that way.

| Target | When to choose it | Notes |
|---|---|---|
| Owner's Google Drive | Default. Zero infrastructure, zero recurring bill, owner already controls the account, satisfies NFR-M5 completely. | OAuth device-code flow at setup; refresh token in Windows Credential Manager. Drive's own web UI arguably satisfies FR-11.10, but build the portal anyway for the integrity-status column FR-11.10 requires. |
| GCS bucket + Cloud Run portal | If you want an operator-controlled, auditable store with object retention. | Scale-to-zero; effectively free at this volume. |
| Any S3-compatible | Escape hatch. Same code path as GCS. | — |

## Ransomware consideration — flag this to the owner

If the till machine is compromised, credentials on it can delete every cloud backup. Two mitigations, both cheap:

1. The terminal's credential must be **write/create only** — no delete, no overwrite. On GCS: a service account with `storage.objects.create` alone. On Drive: a dedicated folder with a scoped app credential.
2. Enable **object versioning plus a retention/lock policy** (e.g. 35 days) on the bucket, so a delete cannot take effect within the retention window. This is not available on consumer Drive, which is a genuine point in GCS's favour.

Retention pruning (FR-11.9, grandfather-father-son) then runs **from the portal service**, not from the terminal.

## Portal

A single small service: sign in (owner only), list objects with date/size/checksum/verification status, issue a short-lived signed download URL. Read-only with respect to business data (FR-11.11) — it never sees plaintext, because the files are encrypted before they leave the shop.

Stack: whatever the team already ships fastest — a Next.js or NestJS service on Cloud Run is entirely reasonable here, and this is the one place in the system where existing web experience directly applies. Resist adding a dashboard; OS-13 excludes it, and every feature added here erodes the "the cloud cannot break trading" guarantee.

---

# 5. Component architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│ HardwarePos.Ui (Avalonia, MVVM)                                      │
│  SalesView · ReturnsView · BackOffice · Reports · Settings           │
│  — no business logic, no SQL, no direct device calls —               │
└──────────────────────────┬───────────────────────────────────────────┘
                           │ (ViewModels call use cases)
┌──────────────────────────▼───────────────────────────────────────────┐
│ HardwarePos.Application — use cases & authorisation (NFR-S2)         │
│  SaleService · ReturnService · StockService · GrnService             │
│  ShiftService · ReportService · BackupService · CatalogueService     │
│  Ports: IReceiptPrinter · IScale · IBackupTarget · IClock · IAuditor │
└──────────────────────────┬───────────────────────────────────────────┘
┌──────────────────────────▼───────────────────────────────────────────┐
│ HardwarePos.Domain — entities, value objects, business rules         │
│  Money · Quantity · Uom conversion · pricing · return policy         │
│  No I/O. No framework types. 100% unit-testable. Where BR-* lives.   │
└──────────────────────────┬───────────────────────────────────────────┘
┌──────────┬───────────────┴───────────┬──────────────┬────────────────┐
│ Infra    │ Devices                   │ Reporting    │ Backup         │
│ EF Core  │ EscPos · Drawer · Scale   │ QuestPDF     │ Snapshot       │
│ Dapper   │ Label · ScannerFilter     │ ClosedXML    │ AES-GCM        │
│ SQLite   │ Win32 RAW spooler         │ CSV          │ Drive / S3     │
└──────────┴───────────────────────────┴──────────────┴────────────────┘
```

## Solution layout

```
HardwarePos.sln
├─ src/
│  ├─ HardwarePos.Domain/            no dependencies at all
│  ├─ HardwarePos.Application/       depends on Domain only
│  ├─ HardwarePos.Infrastructure/    EF Core, Dapper, SQLite, migrations
│  ├─ HardwarePos.Devices/           ESC/POS, drawer, serial scale, labels
│  ├─ HardwarePos.Reporting/         PDF, XLSX, CSV renderers
│  ├─ HardwarePos.Backup/            snapshot → compress → encrypt → upload
│  └─ HardwarePos.Ui/                Avalonia views + viewmodels
├─ tests/
│  ├─ HardwarePos.Domain.Tests/      fast, no I/O, the bulk of the suite
│  ├─ HardwarePos.Integration.Tests/ real SQLite file per test
│  ├─ HardwarePos.Device.Tests/      golden-byte tests against captured ESC/POS
│  └─ HardwarePos.Acceptance.Tests/  one test per AC-01…AC-20
├─ tools/SeedGenerator/              20k SKUs + 100k lines for AC-18
└─ portal/                           separate deployable, separate repo
```

**The rule that keeps this maintainable:** the UI project may not reference `Infrastructure`. If a ViewModel needs data, it goes through an Application service. This is what makes NFR-S2 and AC-17 ("verified at the business-logic layer as well as the UI") true by construction rather than by review.

## Threading model

Single-process, single-instance (enforced with a named mutex; C-01, FR-11 restore safety).

- **UI thread** — rendering only.
- **Write connection** — exactly one, long-lived, serialised behind a `SemaphoreSlim`. All writes go through it. This eliminates SQLite `BUSY` races entirely.
- **Read connections** — a small pool (2–3), WAL means they never block the writer.
- **Background workers** (`IHostedService`): print outbox, backup scheduler, upload retry, low-stock evaluation, monthly restore self-test (FR-11.14). All are `Task`-based, all are cancellable, none may take the write lock for more than a few hundred milliseconds.

## Dependencies

| Concern | Package | Licence |
|---|---|---|
| DI, config, hosting, background services | `Microsoft.Extensions.*` | MIT |
| ORM + migrations | `Microsoft.EntityFrameworkCore.Sqlite` | MIT |
| Hot-path reads and report queries | `Dapper` | Apache 2.0 |
| Encrypted SQLite | `SQLitePCLRaw.bundle_e_sqlcipher` | Apache 2.0 |
| MVVM | `CommunityToolkit.Mvvm` | MIT |
| UI | `Avalonia` 11.x | MIT |
| PDF (invoices, reports, Z reports) | `QuestPDF` | Community licence — free under the revenue threshold; **verify current terms before commit** |
| XLSX import/export | `ClosedXML` | MIT |
| CSV | `CsvHelper` | MS-PL/Apache |
| Barcode & QR bitmaps | `ZXing.Net` | Apache 2.0 |
| Password hashing | `Konscious.Security.Cryptography.Argon2` | MIT |
| Logging | `Serilog` + rolling file sink | Apache 2.0 |
| Templating (receipts) | `Scriban` | BSD-2 |
| Tests | `xUnit`, `FluentAssertions`, `Verify` | MIT |

Every one is permissively licensed and source-available — NFR-M5 and NFR-S3 both survive handover.

---

# 6. Performance design against the NFR-P budget

| Req | Budget | How it is met | Where it could fail |
|---|---|---|---|
| NFR-P1 | Scan → line ≤ 300 ms | Prepared Dapper statement on `barcode(barcode)` unique index → `stock_balance` lookup → in-memory price resolution. Real cost ≈ 2–5 ms. | Rebuilding the whole bill's UI collection on each scan. Use an observable collection with incremental add only. |
| NFR-P2 | Search ≤ 500 ms | FTS5 `MATCH` with prefix tokens, `LIMIT 50`. ≈ 1–4 ms at 50k SKUs. | Debounce set too long, or querying on every keystroke without cancellation. |
| NFR-P3 | Bill save ≤ 2 s | One transaction: sale + lines + payments + movements + balance projection + number allocation + audit + print-outbox row. ~10–30 ms with `synchronous=FULL`. Printing is **not** in this path (§8). | Printing synchronously. Do not. |
| NFR-P4 | Bill lookup ≤ 1 s | Unique index on `sale(bill_no)`. | — |
| NFR-P5 | 1-year report ≤ 10 s | Rollup tables (§3). | Reports written against raw tables "just for now". |
| NFR-P6 | Cold start ≤ 10 s | Self-contained + ReadyToRun publish; **EF Core compiled model** (`optionsBuilder.UseModel(CompiledModel.Instance)`) — this alone removes 1–2 s of first-query model building; defer report/settings module load until navigated to. Target ≈ 2 s. | Eager-loading the catalogue into memory at startup. Unnecessary — SQLite is already the cache. |
| NFR-P7 | No degradation at 500k+ lines | Covering indexes on `(datetime)`, `(sale_id)`, `(product_variant_id, datetime)`; rollups; `ANALYZE` after bulk import; monthly `PRAGMA optimize`. | Missing index on `sale_line(product_variant_id)` — the return-history and product-history screens will crawl. |

Run the seed generator (20,000 SKUs, 100,000 lines) from week one and make AC-18 a CI gate, not a Phase 6 discovery.

---

# 7. Transaction and consistency design

Every business operation is one SQLite transaction, one Application-service method, one audit entry.

**Sale commit (FR-3, AC-02):**
```
BEGIN IMMEDIATE
  allocate bill_no from number_sequence
  insert sale (+ prev_hash / row_hash)
  insert sale_line[]                       -- qty in base units
  insert payment[]                         -- tenders must sum to total
  insert stock_movement[]                  -- append-only ledger
  update stock_balance[]                   -- projection
  insert audit_log
  insert print_job                         -- outbox, not a printer call
COMMIT
→ then: dispatch drawer kick + print asynchronously
```

If anything in that block throws, nothing happened except a consumed bill number — which is correct and auditable.

**Return (FR-5, AC-03/06):** the cumulative guard against over-return is `sale_line.qty_returned + requested <= sale_line.qty`, checked and incremented **inside** the same transaction. `qty_returned` is the one deliberate exception to append-only on `sale_line`; document it in the schema comments and cover it with a `BEFORE UPDATE` trigger that permits *only* that column to change.

**Bulk break (FR-4.9, AC-09):** a single transaction posting a balanced negative/positive movement pair with the cost carried across, tagged with a shared `ref_doc_id`. A report asserts the pair always nets to zero in value.

**Shift close (FR-8.4/8.5):** compute expected cash, record counted and variance, write both rollup tables, set `shift.status = 'closed'`, trigger a backup (FR-11.1). Post-close, a `BEFORE INSERT` trigger on `sale` rejects any row referencing a closed shift — AC-11 and FR-8.5 enforced in the database, not the UI.

---

# 8. Peripheral layer

All four device families sit behind interfaces in `Application`, implemented in `Devices`. This is what makes a Linux terminal a two-adapter job and what makes device tests possible without hardware.

## Receipt printing (FR-7, C-05, AC-16)

**Never call the printer inside the sale transaction.** The pipeline is:

```
sale committed → print_job row (status=pending)
                     ↓
        PrintWorker (background)
                     ↓
   Scriban template + bill data → line IR → EscPosRenderer → byte[]
                     ↓
   Win32 OpenPrinter / StartDocPrinter(datatype="RAW") / WritePrinter
                     ↓
   success → status=printed    failure → status=failed, retry ×3,
                                          status-bar warning, reprint queue
```

This is FR-7.8 and AC-16 satisfied structurally: the sale is already durable before the printer is ever touched.

- **Templates** (FR-7.3, FR-10.8): Scriban text templates stored in the DB, rendering to a small intermediate representation (`Line{ text, align, bold, doubleHeight }`, `Barcode{…}`, `Cut`, `Kick`). The renderer turns IR into ESC/POS bytes. The owner edits the template; nobody recompiles.
- **Bill QR/barcode** (FR-7.4): prefer the printer's native `GS k` command; fall back to a ZXing-generated raster via `GS v 0` for printers with poor native support. Detect once at setup, store the choice in settings.
- **Drawer** (FR-7.7): `ESC p 0 25 250` appended to the receipt, or sent standalone for an authorised no-sale.
- **A4/A5 invoice, reports, PDF export** (FR-7.2, FR-7.9, FR-9.2): QuestPDF to a Windows/CUPS print queue or to a file. Completely separate path from ESC/POS — do not try to share a layout engine between an 80 mm thermal receipt and an A4 invoice.

**Second-language warning (UI-07, FR-2.3 `name_alt`):** thermal printers have no font for Sinhala, Tamil or most non-Latin scripts. If Q-13 is ever answered "yes", those lines must be **rendered to a bitmap and printed as raster graphics**, which is 3–5× slower per line and needs a font shaping step. Design the IR now so a `RasterText` node can be added later without reworking the renderer; do not implement it until the client asks.

## Scanner (§14.2, UI-01)

USB HID keyboard-wedge input arrives as ordinary keystrokes. Distinguish a scan from typing with an inter-keystroke timing filter (scanner bursts are < 30 ms/char) plus a configurable prefix/suffix. Scans must be routed to the scan field regardless of focus, so a mis-focused window never loses a scan mid-bill.

## Scale (optional, §14.2)

`System.IO.Ports` behind `IScale`, with one protocol adapter per supported model. Deliberately last in the build order; it is optional in the SRS and every vendor speaks a different dialect.

## Label printer (FR-2.10, FR-2.12)

Separate `ILabelPrinter`. Most shelf-label printers speak TSPL/ZPL/EPL rather than ESC/POS — do not assume the receipt renderer will drive it.

---

# 9. Backup pipeline (FR-11)

```
trigger: scheduled | shift close | manual
   │
   ├─ VACUUM INTO '%temp%/pos-{ts}.db'      ← consistent snapshot, no downtime,
   │                                          trading continues during backup
   ├─ zstd compress                          (~4–8× on this schema)
   ├─ AES-256-GCM encrypt (FR-11.4)
   │     key = Argon2id(owner passphrase, per-file salt)
   │     header: magic | version | salt | nonce | schema_version | taken_at
   ├─ SHA-256 over the ciphertext            (FR-11.8)
   ├─ write → local folder                   ← exists even with no internet
   ├─ write → USB if attached                (FR-11.3)
   ├─ insert backup_record row
   └─ enqueue upload
          └─ UploadWorker: exponential backoff, resumes across restarts,
             never blocks the UI (FR-11.5/11.6), surfaces state in the
             status bar and dashboard (FR-11.7, UI-09)
```

`VACUUM INTO` is the right primitive: it produces a compact, consistent copy without taking the database offline, so a backup at shift close does not make the cashier wait.

**Key handling:** the passphrase is Argon2id-stretched per file with a fresh salt. The passphrase is never written to disk anywhere — this is the point of FR-11.4 and Q-10, and it means **a lost passphrase is a lost backup set**. The restore wizard, the admin manual and the on-screen setup step must all say this in plain language.

**Restore (FR-11.12, AC-14):** wizard → pick source (local / USB / downloaded) → verify checksum → prompt passphrase → decrypt to a scratch file → open and run integrity checks (`PRAGMA integrity_check`, hash-chain verification, row counts) → display the exact date/time the data will be restored to → require typed confirmation → **back up the current DB first** → swap → restart.

**Monthly self-test (FR-11.14):** the same path into a scratch location, unattended, result written to `backup_record.verified_at` and surfaced on the dashboard. An unverified backup is a rumour.

---

# 10. Security design

| Req | Implementation |
|---|---|
| NFR-S1 | Argon2id password hashing (memory 64 MB, iterations 3, parallelism 1 — tuned to stay under 200 ms on the minimum spec). Cashier may use a short PIN; PIN accounts get a stricter lockout. |
| NFR-S2, AC-17 | Authorisation is an attribute on Application service methods, checked against the current session's role before any repository call. Cost, margin and owner reports are filtered in the **query projection** — the cashier's DTOs simply do not contain a cost field, so there is nothing to leak. |
| NFR-S3 | SQLCipher; key in DPAPI + Credential Manager. |
| NFR-S4, S5 | AES-256-GCM before upload; TLS enforced by the SDK; certificate validation never disabled. |
| NFR-S6 | Windows Credential Manager for Drive/S3 tokens. Config files hold references, never secrets. |
| NFR-S7 | The schema has no column capable of holding a PAN. `payment.reference` is length-capped at 20 and validated to reject anything matching a 13–19 digit Luhn-valid sequence. Refuse to store it rather than trusting the cashier. |
| NFR-S8 | `audit_log` is trigger-protected append-only plus hash-chained. No service method exposes update or delete. |
| NFR-S9 | Failed-attempt counter with exponential lockout after 5, logged with timestamp and username. |

Owner override actions (unlinked refunds FR-5, discounts above limit Q-12, non-returnable override AC-05, no-sale drawer opens, restores) all follow one pattern: a modal owner re-authentication that writes an audit row containing the action, the reason, both user IDs and the before/after JSON. That single mechanism covers roughly a dozen scattered SRS requirements.

---

# 11. Packaging, install and update

- **Publish:** `dotnet publish -r win-x64 --self-contained -p:PublishReadyToRun=true`. No .NET runtime prerequisite, no admin-installed dependency — NFR-M2.
- **Installer:** Inno Setup. One `.exe`, per-machine install to `%ProgramFiles%`, data directory under `%ProgramData%\HardwarePos\` (never `%ProgramFiles%`, or Windows file virtualisation will produce baffling bugs), desktop and startup shortcuts, first-run setup wizard (shop profile, tax, printer, backup passphrase, admin account).
- **Trimming:** do not enable IL trimming. It saves ~30 MB and reliably breaks EF Core and reflection-based serialisation. Not worth it.
- **Updates (NFR-M3):** installer over the top; on launch, compare `schema_version` → if a migration is pending, take a full pre-migration backup → run EF Core migrations in a transaction → verify → proceed. If the migration fails, restore the pre-migration backup automatically and refuse to start with a plain-language message. Optionally add Velopack later for delta updates; it is not needed for a single terminal.
- **Migrations:** EF Core migrations checked into source, forward-only, each with a matching integration test that runs it against a seeded database.
- **Handover (NFR-M5/M6):** source repo, schema ERD, build instructions, `Export All Data` command producing CSV + a plain unencrypted SQLite copy on demand.

---

# 12. Testing strategy

| Layer | Approach |
|---|---|
| Domain | Pure unit tests. Money arithmetic, UOM conversion, pricing tiers, return-policy evaluation, rounding. Property-based tests on rounding: for any bill, `sum(lines) == subtotal` and tenders reconcile. This is the layer where bugs cost money, so it gets the most tests. |
| Persistence | Integration tests on a real SQLite file per test. Verify triggers actually reject update/delete on append-only tables, foreign keys bite, and every migration applies cleanly to a seeded DB. |
| Devices | Golden-byte tests: render a known bill, compare the ESC/POS byte stream to a committed snapshot (`Verify`). Catches template regressions without a printer on the desk. |
| Acceptance | **One automated test named for each of AC-01 … AC-20.** AC-13 (network unplugged) and AC-15 (power cut) are partly manual; simulate AC-15 by killing the process mid-transaction in a loop and asserting integrity after each kill. |
| Performance | The seed generator plus a benchmark suite asserting NFR-P1…P7 in CI, failing the build on regression. |
| Reconciliation | A generative test: simulate 500 random trading days of sales, returns, voids and shifts, then assert AC-12 — RPT-01 net sales == tenders in RPT-05 == sales minus returns in RPT-02 — exactly. This one test protects the system's credibility with the owner's accountant. |

---

# 13. Build order

The SRS phasing is sound. Two adjustments:

| Phase | SRS content | Adjustment |
|---|---|---|
| **0.5 — Walking skeleton (1 wk)** | *new* | Solution scaffold, SQLite + migrations + SQLCipher, one product, one sale, one printed receipt, one backup, installer. End to end and thin. This retires the ESC/POS and packaging risks in week one instead of week fourteen. |
| 1 — Core trading | Products, UOM, variants, barcodes, stock ledger, sales, cash, printing, users, local backup | Build `stock_balance`, `number_sequence` and the audit/hash-chain machinery here. They are far more expensive to retrofit than to include. |
| 2 — Returns & inventory | Returns, exchanges, credit notes, GRN, adjustments, stock take, reorder | As specified. |
| 3 — Reports & cash | Report suite, X/Z, shift & cash, audit, exceptions | Write rollup tables at the same time as the Z report. |
| **4 — Backup & resilience** | Cloud pipeline, encryption, retention, portal, restore drill | **Do not defer.** The SRS is right about this. Pull the local+USB half of it into Phase 1; only the cloud half belongs here. |
| 5 — Extras & handover | Trade pricing, credit customers, A4 invoices, labels, migration, training | Label printing (FR-2.10 / Q-15 says Phase 1) needs its own printer adapter — decide which phase it truly belongs to. |
| 6 — Warranty | — | As specified. |

---

# 14. Risks

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| R1 | Windows 10 out of support on hardware that cannot take Windows 11 (§14.1 vs NFR-M4) | A system of financial record on an unpatched OS | Resolve before Phase 1 per ADR-001b. Avalonia keeps the Linux route open. |
| R2 | Lost backup passphrase | Total, unrecoverable data loss — the worst outcome in the system | Q-10 answered in writing before go-live; printed passphrase card kept off-premises; restore drill performed *with the owner* at handover, not demonstrated at them. |
| R3 | Printer model behaves off-spec despite claiming ESC/POS | Receipts garbled, cuts fail, drawer does not open | Q-14 answered before Phase 1; buy the actual model early and test against it; keep the renderer capability-flagged. |
| R4 | Scope creep toward a second terminal or live cloud dashboard | Breaks C-01/C-02 and invalidates this entire architecture | Say so explicitly at sign-off. A second till is not a feature, it is a different system with sync, conflict resolution and a server tier. |
| R5 | Ransomware or theft of the till machine deleting cloud backups | Backups gone at the moment they are needed | Write-only credentials + object versioning + retention lock (§4). |
| R6 | Team unfamiliarity with C#/Avalonia | Slower Phase 1 | The walking skeleton in Phase 0.5 surfaces this in week one while it is still cheap to switch to Option B. |
| R7 | SQLite file on a network drive or synced folder (OneDrive/Drive desktop) | Corruption — SQLite's locking is not safe over SMB or sync clients | Installer refuses a data directory that is a network path or inside a known sync root. Enforce it in code. |

---

# 15. What this architecture deliberately does not have

No API layer. No message broker. No Docker on the till. No ORM-managed concurrency. No caching tier. No microservices. No live cloud anything.

Each of those would be a reasonable default in a different system, and every one of them here would add a failure mode to a shop counter that must keep selling bolts when the internet is down and the power has just come back on. The SRS is unusually disciplined about this; the architecture should be too.

---

# 16. Technical questions to close before Phase 1

Extending §18 of the SRS with the questions this design raises:

| # | Question | Recommended default |
|---|---|---|
| Q-A | NFR-C2 says 1,000 bills/day and 5 years online (~10M lines); A-06/Q-06 say 200–500. Which is the design target? | Design for 500/day, with rollups making 1,000 non-breaking |
| Q-B | Windows 11-capable hardware, Windows 10 ESU, or a Linux terminal? (R1) | Procure Win11-capable refurb hardware; build on Avalonia regardless |
| Q-C | Which language, if any, for UI-07? Anything non-Latin changes the print pipeline (§8) | English only at launch; keep the IR extensible |
| Q-D | Google Drive or an operator-controlled bucket? (ransomware protection differs materially) | GCS/R2 with versioning + retention lock; Drive if the owner insists on sole custody |
| Q-E | Does the owner accept that a lost passphrase means unrecoverable backups? Signed acknowledgement | Yes, signed at sign-off |
| Q-F | Currency and decimal places — does 4 dp of internal scale suffice? | Yes for LKR, INR, USD, GBP, EUR |
| Q-G | Who maintains this after warranty, and in what language do they work? Directly affects ADR-001 | Confirm before committing to C# |
| Q-H | Confirm the exact receipt printer and scanner model (Q-14) before Phase 1 estimates are trusted | — |

---

## Sign-off

| Role | Name | Signature | Date |
|---|---|---|---|
| Development Lead | | | |
| Shop Owner (for R1, R2, R4, Q-E) | | | |

*End of document.*
