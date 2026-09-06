# Phase 1 — Core Trading

**Duration:** 5 weeks · **Tasks:** 16 · **Exit:** the shop can sell and print. This is a usable till.

## Scope

Catalogue with variants and units of measure, barcodes and search, the stock ledger, the full sales screen, tender and change, receipt printing and reprint, users and authorisation, settings, spreadsheet import, label printing, and local + USB backup.

## Out of scope in this phase

Returns (Phase 2), GRN and stock take (Phase 2), reports beyond the dashboard (Phase 3), X/Z reports (Phase 3), cloud backup (Phase 4), trade pricing and credit accounts (Phase 5).

**Deliberately pulled forward from later phases:** the audit log and hash chain, `number_sequence`, `stock_balance`, owner overrides, and local/USB backup. All are cheap now and expensive to retrofit.

---

### P1-T01 — Full schema migration
**Depends on:** P0-T04 · **Est:** 2d · **SRS:** §11, DM-01…DM-07

**Context.** Phase 0 created a skeleton schema. This lays down the complete model from `01_DATA_MODEL.md` so nothing downstream needs a table added mid-feature.

**Do this.**
1. Migration `0002_FullSchema`: every table in areas A–F of the data model, with all CHECK constraints, foreign keys and indexes exactly as specified.
2. All append-only triggers, the column-scoped `sale_line` and `shift` exceptions, the closed-shift insert guard, and the two-level `category` guard.
3. The `product_search` FTS5 table with its maintenance triggers.
4. `ux_one_open_shift` partial unique index.
5. Map every table in `PosDbContext` with explicit configuration classes (`IEntityTypeConfiguration<T>`), value converters for `Money`/`Quantity` (scaled long ↔ decimal), and `DateTimeOffset` ↔ ISO-8601 text.
6. Generate the EF compiled model (`dotnet ef dbcontext optimize`) and wire `UseModel` — this is a startup-time requirement, not an optimisation (NFR-P6).
7. Write `SchemaConformanceTests`: for every enum in `Domain/Enums`, assert each member inserts successfully into its column and a bogus value is rejected.

**Deliverables.** Migration `0002`, all EF configurations, compiled model, schema conformance tests.

**Risks.** Value converters applied inconsistently, so some columns store scaled longs and others store text. Add a test that reads every money column's SQLite storage class and asserts `INTEGER`.

**Done when.**
- [ ] All tables, indexes and triggers exist after migration; `integrity_check` returns ok
- [ ] Every enum round-trips; every invalid enum value is rejected by a CHECK
- [ ] Every money and quantity column has storage class `INTEGER`
- [ ] The EF compiled model is generated and `UseModel` is wired into the composition root (NFR-P6 cold-start figure is measured on the terminal in `HW-T07`)

---

### P1-T02 — Users, authentication, roles and authorisation
**Depends on:** P1-T01 · **Est:** 2d · **SRS:** FR-1, NFR-S1, NFR-S2, NFR-S9, AC-17

**Context.** Authorisation must live in the application layer, not the UI. Building it now means every subsequent service is written the right way round.

**Do this.**
1. `Application/Security/PasswordHasher` — Argon2id, memory 64 MB, iterations 3, parallelism 1. Pick conservative defaults now and record them in `app_setting`; the final tuning pass against the minimum-spec terminal (login under 200 ms) happens in `HW-T07`.
2. `AuthenticationService`: login, failed-attempt counter, lockout after 5 with exponential backoff (NFR-S9), `last_login`, all logged to `audit_log`.
3. `ISession` — current user, role, shift; scoped as a singleton (single user, C-01).
4. `[RequiresRole(Role.Owner)]` attribute + a DI decorator on every application service that checks before delegating. Throws `NotAuthorisedException`, never returns a partial result.
5. `IOwnerOverrideService.RequestAsync(action, reason)` — re-authenticates an owner, writes an audit row with both user ids, returns an override token consumed by the calling command.
6. User management screen (owner only): create, deactivate, reset password.
7. Login screen; single-instance enforcement via a named mutex.

**Deliverables.** Hasher, auth service, role decorator, override service, login and user admin screens.

**Risks.** Argon2 parameters tuned on a dev machine make login painfully slow on the shop PC — re-checked and adjusted in `HW-T07`.

**Done when.**
- [ ] Login succeeds/fails correctly; 5 failures lock the account and log every attempt
- [ ] Calling an owner-only service method with a cashier session throws, **with the UI bypassed** (test calls the service directly) — this is AC-17
- [ ] Password hashes are Argon2id; no plaintext or reversible storage anywhere
- [ ] Login completes well under 500 ms on the dev machine (indicative; the terminal figure is `HW-T07`)
- [ ] A second instance of the app refuses to start

---

### P1-T03 — Settings framework
**Depends on:** P1-T01 · **Est:** 1.5d · **SRS:** FR-10.1–10.9, NFR-M1

**Context.** NFR-M1 requires business rules, tax, policy and receipt layout to be changeable without code. Every later feature reads its limits from here, so it must exist before them.

**Do this.**
1. `Application/Settings/ISettings` with typed accessors: `Settings.Sales.MaxLineDiscountRate`, `Settings.Shop.TaxNumber`, etc. Strongly typed, not stringly typed.
2. `SettingDefaults.cs` — the complete default set covering FR-10.1 through FR-10.8.
3. Cached in memory, invalidated on write. Every write goes to `audit_log` (FR-10.9).
4. Settings screens grouped as in FR-10: Shop profile, Financial, Tax, Numbering, Policy, Peripherals, Backup, Receipt template.
5. First-run setup wizard: shop profile, currency and decimals, tax classes, bill number format, printer selection, owner account, backup folder and passphrase.

**Deliverables.** Settings service, defaults, settings UI, first-run wizard.

**Risks.** Settings read at startup and cached forever, so a change needs a restart. Invalidate on write and make screens re-read on open.

**Done when.**
- [ ] Every FR-10.1–10.8 setting is present, editable and persisted
- [ ] Changing the currency decimal places changes displayed and printed amounts without a restart
- [ ] Every settings change writes an audit row with before/after JSON
- [ ] The first-run wizard produces a usable system from an empty database
- [ ] No hard-coded tax rate, discount limit or currency symbol anywhere (grep test)

---

### P1-T04 — Catalogue: categories, brands, UOM, tax classes, suppliers
**Depends on:** P1-T02, P1-T03 · **Est:** 2d · **SRS:** FR-2.20, FR-2.21, FR-6

**Context.** The reference data every product depends on. Small, but blocking.

**Do this.**
1. CRUD services and screens for `category` (two levels, enforced), `brand`, `uom`, `tax_class`, `supplier`, `customer`.
2. Deactivate rather than delete anything referenced by history (FR-2.1 pattern applied throughout).
3. Seed the default UOM set and category set from `01_DATA_MODEL.md` §11.

**Deliverables.** Reference-data services, screens, seed data.

**Risks.** Allowing a third category level "temporarily". The two-level constraint is in the trigger; don't work around it.

**Done when.**
- [ ] All six entities can be created, edited and deactivated
- [ ] Creating a third-level category is rejected with a plain-language message
- [ ] Deleting a category that has products is refused; deactivating succeeds
- [ ] Default seed data present after the first-run wizard

---

### P1-T05 — Product, variant and UOM conversion domain
**Depends on:** P1-T04 · **Est:** 3d · **SRS:** FR-2.1–2.8, FR-3.6, AC-08

**Context.** The defining feature of hardware retail (SRS §2.2). Stock is always held in base units; every sale, receipt and report converts to base units at the boundary and never carries mixed units inward.

**Do this.**
1. `Domain/Catalogue/Product`, `ProductVariant`, `ProductUomConversion`. Product types `STANDARD`, `DECIMAL`, `SERVICE`, `NON_INVENTORY` with behaviour differences:
   - `STANDARD` rejects fractional quantities.
   - `DECIMAL` accepts up to the UOM's decimal places.
   - `SERVICE` and `NON_INVENTORY` post no stock movement.
2. `Domain/Catalogue/UomConverter`: `ToBase(qty, uomId, product)` and `FromBase(qtyBase, uomId, product)`. Exactly one base UOM with factor 1.0000; enforced in the domain and by trigger.
3. Price resolution: `ProductUom.selling_price` if set, else base price × factor (FR-2.5). A distinct price per unit is the point — do not collapse it.
4. `VariantMatrixGenerator` (FR-2.6): given a parent and attribute axes (e.g. size × length × finish), generate the cartesian product of variants in one operation, skipping combinations that already exist, with a preview before commit.
5. Product editor screen with a variant grid and a UOM grid.

**Deliverables.** Product/variant domain, converter, matrix generator, product editor.

**Risks.** Rounding drift in conversion. Conversions must be exact decimal arithmetic; assert `FromBase(ToBase(q)) == q` for representable quantities. Also: someone stores stock in a non-base unit "just for coils". Never.

**Done when.**
- [ ] 1 box = 100 pcs; selling 2 boxes decrements 200 base units
- [ ] 1 coil = 90 m; selling 3.5 m decrements 3.5 base units and selling 1 coil decrements 90
- [ ] A `STANDARD` product rejects a quantity of 2.5 with a clear message
- [ ] A `DECIMAL` product accepts 2.755 m and stores `27550`
- [ ] The matrix generator creates 60 variants from 10 lengths × 2 threads × 3 finishes in one operation (SRS §2.2 case)
- [ ] Round-trip conversion property test passes for 10 000 random quantities

---

### P1-T06 — Barcodes and product search
**Depends on:** P1-T05 · **Est:** 2d · **SRS:** FR-2.9–2.12, FR-2.24, NFR-P1, NFR-P2

**Context.** The hottest path in the system. NFR-P1's 300 ms budget is generous for the query; it is spent on UI work if you are careless.

**Do this.**
1. Multiple barcodes per variant, one primary (FR-2.9). Unique across the whole catalogue.
2. Internal barcode generation for loose items (FR-2.10) — Code128, prefix configurable, check-digit validated.
3. `ProductLookupService.ByBarcodeAsync` — Dapper, one prepared statement, joined to `stock_balance` and price in a single query. No EF change tracking on this path.
4. `ProductSearchService` — FTS5 prefix query across name, name_alt, code, sku, brand, category, location; `LIMIT 50`; ranked with exact-code matches first.
5. Duplicate detection on create (FR-2.24): same barcode → hard block; similar name + brand + size → warn.
6. `ReindexSearchCommand` for maintenance.

**Deliverables.** Barcode management, internal barcode generator, lookup and search services, duplicate detection.

**Risks.** Rebuilding the whole result collection on every keystroke. Debounce at 120 ms, cancel the in-flight query, and update the collection incrementally.

**Done when.**
- [ ] Benchmark: barcode lookup returns in <300 ms end to end with 20 000 SKUs seeded — **asserted in CI**
- [ ] Benchmark: search results within 500 ms of the last keystroke with 50 000 SKUs
- [ ] Multiple barcodes resolve to the same SKU
- [ ] Adding a duplicate barcode is blocked; a similar name produces a warning that can be overridden
- [ ] Search finds items by name fragment, code, brand, category and rack location

---

### P1-T07 — Stock ledger and balance projection
**Depends on:** P1-T05 · **Est:** 2d · **SRS:** FR-4, DM-05, SAD §3

**Context.** The ledger/projection split is the backbone of inventory correctness. Every later stock feature posts through this one service.

**Do this.**
1. `Application/Inventory/StockLedger.PostAsync(movements[], refDocType, refDocId)` — the **only** way stock ever changes. Inside the caller's transaction:
   - compute `balance_after` per movement
   - insert `stock_movement` rows
   - upsert `stock_balance`
   - recompute moving-average cost on inbound movements: `newAvg = (oldQty×oldAvg + inQty×inCost) / (oldQty + inQty)`, guarded against division by zero and negative resulting quantity.
2. Outbound movements use the current `cost_avg` as `unit_cost` (the COGS snapshot).
3. `RebuildStockBalanceCommand` — replays the ledger into the projection.
4. Startup consistency check: sample 200 random variants, compare projection to ledger sum, log a `Warning` and raise a dashboard flag on mismatch.
5. Stock enquiry screen (F11): current qty in base and alternate units, cost (owner only), last movements.

**Deliverables.** `StockLedger`, moving-average cost, rebuild command, consistency check, stock enquiry screen.

**Risks.** A second code path that writes `stock_balance` directly. Make the setter internal to `StockLedger` and add an architecture test that no other type references the `stock_balance` table.

**Done when.**
- [ ] Every stock change in the system goes through `StockLedger` (verified by test)
- [ ] Moving-average cost matches a hand-worked example to 4 dp across a receipt-sell-receipt sequence
- [ ] `RebuildStockBalanceCommand` reproduces the projection exactly after 10 000 random movements
- [ ] Direct `UPDATE stock_movement` still raises
- [ ] Stock enquiry shows correct quantities in both base and alternate units

---

### P1-T08 — Pricing and discount engine
**Depends on:** P1-T05, P1-T03 · **Est:** 2d · **SRS:** FR-2.13–2.19, FR-3.7–3.10, Q-12

**Context.** Price resolution has an order of precedence that must be single-sourced, because Phase 5 adds trade tiers and quantity breaks on top of it.

**Do this.**
1. `Domain/Pricing/PriceResolver` with explicit precedence: promotional price (in date window) → quantity break for the tier → tier price → `product_uom.selling_price` → variant base price × conversion factor. Return the price **and the reason**, so the UI can show why.
2. Line discount and bill discount, as amount or percentage, capped by `product.max_discount_rate` then the global cashier limit (Q-12). Over the cap requires an owner override.
3. Bill discount allocated across lines by `DiscountAllocator` (P0-T02) so line totals still sum to the subtotal.
4. Tax computation for inclusive and exclusive modes; per-line tax from the product's tax class; bill-level tax is the sum of line tax, never recomputed from the total.
5. Price change writes `price_change_log` (FR-2.17); price at or below cost warns and requires confirmation (FR-2.18).
6. Bulk price update by category/brand/supplier with preview (FR-2.19).

**Deliverables.** Price resolver, discount rules, tax computation, price history, bulk update.

**Risks.** Tax-inclusive rounding. Compute line tax from the inclusive price as `price − price/(1+rate)`, round once at the line, and let the bill tax be the sum. Never divide the bill total.

**Done when.**
- [ ] Precedence test covers all five levels with a documented expected winner for each
- [ ] Tax-inclusive and tax-exclusive bills both reconcile: `sum(line_total) == subtotal`, `subtotal − discount + tax + rounding == total`
- [ ] A 12% line discount with a 5% cap requires an owner override, and the override is audited
- [ ] Setting a price below cost warns and requires confirmation, and is logged
- [ ] Bill discount allocation sums exactly to the discount (property test, 1–50 lines)

---

### P1-T09 — Sales screen and bill building
**Depends on:** P1-T06, P1-T07, P1-T08 · **Est:** 4d · **SRS:** FR-3.1–3.15, UI-01–UI-12, NFR-U1, NFR-U2

**Context.** The screen the cashier lives on. Keyboard-first is not a preference here; the mouse must be optional throughout (UI-01).

**Do this.**
1. Sales view: scan/search box always focused, line grid, prominent running total (UI-03), status bar showing user, shift, last backup, cloud status, printer status (UI-09).
2. Function keys exactly per UI-02: F1 Help, F2 New, F3 Search, F4 Return, F5 Hold, F6 Recall, F7 Discount, F8 Customer, F9 Pay, F10 Reprint, F11 Stock, F12 Day Close, Esc Cancel. Fixed, visible on screen.
3. Scan behaviour: same code again increments quantity (configurable, FR-3.2); quantity, unit and price editable inline; unit switch per line changes price per FR-2.5.
4. Scanner keystroke filter: inter-key timing under 30 ms plus configurable prefix/suffix. Scans route to the scan box **regardless of focus** so a mis-focused dialog never eats a scan.
5. Held bills (F5/F6): JSON payload to `held_bill`, labelled, recalled, listed.
6. Open item lines (FR-2.8) with manual description and price, flagged and reportable.
7. Negative stock policy from settings (Q-11): warn and allow, logged — or block.
8. Numeric input accepts keypad and number row, rejects invalid characters silently (UI-10).
9. Layout usable at 1366×768 (UI-04); large, high-contrast text.

**Deliverables.** Sales view + viewmodel, keyboard map, scanner filter, held bills, open items.

**Risks.** Building UI state that duplicates domain logic. The viewmodel holds a `Bill` aggregate from `Domain` and asks it to recalculate; it never computes a total itself.

**Done when.**
- [ ] A 5-item bill completes with no mouse and ≤4 keystrokes beyond the scans (NFR-U2)
- [ ] Every function key in UI-02 works and is visible on screen
- [ ] Scanning a code twice increments quantity; the setting flips it to two lines
- [ ] A scan lands correctly while a non-modal panel has focus
- [ ] Hold and recall preserve lines, quantities, discounts and customer exactly
- [ ] The screen is fully usable at 1366×768
- [ ] Total is the most prominent element on screen

---

### P1-T10 — Tender, change and sale completion
**Depends on:** P1-T09 · **Est:** 2d · **SRS:** FR-3.16–3.22, FR-8.2, AC-02, NFR-P3

**Context.** The commit point. The transaction shape was proven in P0-T06; this makes it real.

**Do this.**
1. Payment dialog (F9): cash with change calculation and quick-tender buttons, card, bank transfer, cheque, split tender across multiple types (FR-3.x).
2. Validation: tenders must sum to the total; change is cash only; over-tender on a non-cash type is rejected.
3. `CompleteSaleCommand` extended to the full transaction shape, including COGS snapshot per line and `business_date` derived from the shop's configured day-boundary.
4. Drawer kick on cash tender (FR-7.7).
5. Bill cancellation (owner override, audited, keeps the number with `status = CANCELLED`, reverses stock via compensating movements — never by deleting).

**Deliverables.** Tender dialog, completion command, cancellation flow.

**Risks.** Cancellation implemented as a delete. It must post reversing `stock_movement` rows and leave the original sale row intact.

**Done when.**
- [ ] AC-02: a 10-line bill with a decimal-quantity item, a unit switch, a discount and split tender completes and prints correctly
- [ ] Tenders that do not sum to the total are rejected with a clear message
- [ ] Cash tender opens the drawer; card tender does not (configurable)
- [ ] Cancelling a bill reverses stock exactly, keeps the bill number, and writes an audit row
- [ ] Bill save + stock update + print dispatch measured under 2 s with 100 000 historical lines (NFR-P3)

---

### P1-T11 — Receipt templates and printing
**Depends on:** P0-T05, P1-T10 · **Est:** 3d · **SRS:** FR-7.1–7.10, §10, NFR-M1 · **Physical verification:** `HW-T01`

**Context.** Phase 0 rendered a hard-coded specimen. This makes the layout owner-editable without a code change (FR-7.3, FR-10.8) and adds the bill QR that Phase 2 needs for return-by-scan. It renders through `FileReceiptPrinter`; `HW-T01` runs it on the real printer.

**Do this.**
1. Scriban templates stored in `app_setting`, rendering to the receipt IR from P0-T05. Ship the §10.1 specimen as the default.
2. Template model: shop profile, bill, lines, tenders, tax breakdown, policy text, cashier, date, duplicate flag.
3. Bill QR/barcode of the bill number (FR-7.4) — native `GS k` where the printer supports it, ZXing raster fallback.
4. Reprint (F10) marked `DUPLICATE` and logged (FR-7.6); configurable copy count (FR-7.5).
5. Print queue UI: pending and failed jobs, retry, with the status-bar indicator.
6. A4/A5 invoice via QuestPDF to a Windows printer (FR-7.2) and save-as-PDF (FR-7.9).
7. Template preview in settings that renders to screen without printing.

**Deliverables.** Template engine, default template, QR/barcode, reprint, print queue UI, A4 invoice.

**Risks.** Trying to share one layout engine between 80 mm thermal and A4. They are separate renderers over the same data model. Keep them separate.

**Done when.**
- [ ] Changing the shop name, footer or policy text in settings changes the rendered receipt byte stream with no rebuild
- [ ] The bill QR encodes the correct bill number (decode it in the test; scanning it back is `HW-T01`)
- [ ] A reprint renders `DUPLICATE` and writes an audit row
- [ ] `FileReceiptPrinter` set to throw mid-transaction: the sale completes and the job is queued (AC-16, fake; real printer in `HW-T01`)
- [ ] An A4 invoice renders with the same totals as the thermal receipt, to the cent
- [ ] Snapshot tests cover the default template's byte output

---

### P1-T12 — Label printing
**Depends on:** P1-T06 · **Est:** 1.5d · **SRS:** FR-2.10, FR-2.12, Q-15 · **Physical verification:** `HW-T03`

**Context.** Q-15 places label printing in Phase 1. Note that most shelf-label printers speak TSPL/ZPL/EPL, not ESC/POS — this is a separate device abstraction.

**Do this.**
1. `Devices/Labels/ILabelPrinter` with a TSPL implementation (adjust once Q-14 names the model) and a file-writing dev implementation that emits the byte stream to `artifacts/labels/*.bin`.
2. Label layout: name, code, barcode, unit, price. Size configurable.
3. Print for a selected product list, a search result set, or a whole GRN batch (FR-2.12 — the GRN hook lands in Phase 2).
4. Preview and quantity-per-label.

**Deliverables.** Label printer abstraction, TSPL renderer, file dev implementation, layout, selection UI, byte-stream snapshot tests.

**Risks.** No label printer available at build time — that is expected. Implement against the spec and snapshot-test the byte stream; `HW-T03` prints and scans a real label.

**Done when.**
- [ ] The TSPL byte stream for a known product matches a committed `.verified.txt`
- [ ] A list of 50 products renders in one batch
- [ ] Label size and content are configurable in settings

> Printing on the actual label printer and scanning the barcode back move to **`HW-T03`**.
- [ ] Snapshot test covers the generated command stream

---

### P1-T13 — Spreadsheet import
**Depends on:** P1-T05, P1-T07 · **Est:** 2.5d · **SRS:** FR-2.22, FR-2.23, AC-07, Q-08

**Context.** How the shop's existing catalogue gets in. A bad import is very hard to unwind, so a dry run is mandatory, not optional.

**Do this.**
1. `ImportService`: read XLSX/CSV (ClosedXML/CsvHelper) into a staging structure.
2. Column-mapping UI: map spreadsheet columns to fields, remembered as a named profile.
3. Validation pass producing a report: missing required fields, unknown category/brand/UOM (offer to create), duplicate codes or barcodes, invalid numbers, price below cost.
4. **Dry-run preview**: counts of create/update/skip/error and a sample of each. Nothing is written.
5. Commit in one transaction; opening stock posts as `OPENING` movements through `StockLedger`.
6. Full catalogue export with current stock and prices (FR-2.23).

**Deliverables.** Import service, mapping UI, validation report, dry run, export.

**Risks.** Partial import on failure. One transaction for the whole file; if it cannot fit, batch it but record a resumable import id and make re-running idempotent on product code.

**Done when.**
- [ ] AC-07: 100 SKUs import from a spreadsheet with a validation report and correct resulting stock and prices
- [ ] A file with 10 deliberate errors reports all 10 and writes nothing
- [ ] Dry run and commit produce identical counts
- [ ] Re-importing the same file updates rather than duplicating
- [ ] Export round-trips: export, re-import, no changes detected

---

### P1-T14 — Shift open (minimal) and dashboard
**Depends on:** P1-T10 · **Est:** 1.5d · **SRS:** FR-8.1, FR-8.7, FR-9.7, UI-09

**Context.** Sales need a shift to belong to. Full cash management and X/Z come in Phase 3; this is the minimum to make `sale.shift_id` meaningful and to recover an open shift after a crash.

**Do this.**
1. Open shift with an opening float; enforce one open shift (the partial unique index already does).
2. Recover an open shift cleanly on restart; warn if the app is closed with a shift open (FR-8.7).
3. Dashboard (FR-9.7): today's sales, bill count, average bill, cash in drawer, low-stock count, last backup status.
4. Status bar per UI-09.

**Deliverables.** Shift open/recover, dashboard, status bar.

**Risks.** Dashboard queries scanning the whole sale table on every refresh. Scope them to `business_date = today` and refresh on a timer, not on every keystroke.

**Done when.**
- [ ] A sale cannot be made without an open shift
- [ ] Killing the app with an open shift and restarting recovers the shift with correct totals
- [ ] The dashboard figures match hand-computed values for a seeded day
- [ ] Dashboard refresh does not measurably affect scan latency

---

### P1-T15 — Local and USB backup
**Depends on:** P0-T07, P1-T14 · **Est:** 1.5d · **SRS:** FR-11.1–11.4, FR-11.7, FR-11.12

**Context.** Pulled forward from Phase 4 deliberately. The moment there is real catalogue data in the system, an unbacked-up till is an unacceptable risk. Cloud upload stays in Phase 4; local and USB do not.

**Do this.**
1. Scheduled daily backup at a configurable time, plus on shift close, plus manual (FR-11.1, FR-11.2).
2. Write to the local backup folder and to the USB path if configured and present (FR-11.3). USB absent is a warning, not an error.
3. Local retention pruning per the grandfather-father-son default.
4. Guided restore from local or USB: verify checksum, prompt passphrase, show the data date, require typed confirmation, back up the current DB first (FR-11.12).
5. Last-backup indicator on the dashboard and status bar with an escalating warning (FR-11.7).

**Deliverables.** Backup scheduler, USB writer, retention, restore wizard, indicators.

**Risks.** Backup running during trading and stalling the till. `VACUUM INTO` on a background thread does not block writers, but the compress/encrypt step must be throttled and must never take the write lock.

**Done when.**
- [ ] A scheduled backup runs unattended and appears in `backup_record`
- [ ] A backup taken while a sale is being rung up does not delay the sale (measured)
- [ ] Restore from the USB path reproduces the database exactly, with the current DB backed up first
- [ ] Pointing the USB path at a missing location produces a warning and the local backup still succeeds (a real USB stick pulled mid-write is `HW-T08`)
- [ ] The dashboard warns after the configured number of days without a backup

---

### P1-T16 — Phase 1 acceptance and software performance harness
**Depends on:** all P1 tasks · **Est:** 2d · **SRS:** AC-02, AC-07, AC-13, AC-15, AC-16, AC-17, AC-19, NFR-P1–P7

**Context.** Turn the SRS acceptance criteria that are reachable in Phase 1 into automated tests that run in CI against the Linux fakes. Everything after this phase is protected by them. The absolute NFR-P1…P7 budgets are a separate, on-terminal gate — `HW-T07`; this task builds the harness they reuse.

**Do this.**
1. Seed generator (`tools/SeedGenerator`, `bash scripts/seed.sh`): 20 000 SKUs, 100 000 historical bill lines, realistic distribution (AC-18 baseline).
2. Acceptance tests: `AC02`, `AC07`, `AC13` (network disabled at the OS/DI level), `AC15` (kill the process mid-transaction ×100, assert integrity every time), `AC16` (`FileReceiptPrinter` throws), `AC17`, `AC19` (500 consecutive bills including cancellations, assert gapless).
3. A software performance harness driving NFR-P1, P2, P3, P4, P6 operations against the seeded database. In CI it is a **relative regression guard**: it records timings and fails the build on a >20% drift from `docs/perf-baseline.md`. It does **not** assert the absolute budgets — that is `HW-T07` on the terminal. The same harness is what `/perf-gate` runs there.
4. Hash-chain verification command run over the seeded data.

**Deliverables.** Seed generator, acceptance test suite, the reusable performance harness wired into CI as a regression guard.

**Risks.** Discovering at this gate that scan latency degrades with history. If so, fix indexes before Phase 2 — it only gets worse. (Absolute-budget failures are found later, in `HW-T07`; the regression guard is the early warning.)

**Done when.**
- [ ] All listed acceptance tests pass in CI against the fakes
- [ ] The performance harness runs in CI over the seeded database and fails on a >20% regression
- [ ] `AC13` passes with the network disabled in-process (a physical cable-out trading day is `HW-T09`)
- [ ] 100 mid-transaction process kills leave the database intact every time (AC-15; on-terminal power cuts are `HW-T09`)
- [ ] 500 bills including cancellations produce a gapless series (AC-19)
- [ ] `docs/perf-baseline.md` still shows the budgets as unmeasured, with a note that `HW-T07` populates them

> Absolute NFR-P1…P7 pass/fail on the shop terminal, and the offline trading-day run on real hardware, move to **`HW-T07`** and **`HW-T09`**.
