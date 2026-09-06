# Phase 3 — Reports and Cash Discipline

**Duration:** 3 weeks · **Tasks:** 9 · **Exit:** the owner has visibility and the day closes cleanly and immutably.

## Scope

Full shift and cash management, X and Z reports, the complete report suite from SRS §9, rollup tables, exports, the audit log viewer and exception reporting.

## The theme of this phase

**Reconciliation.** FR-9.6 and AC-12 are not guidelines: net sales, tenders and Z reports must tie exactly for the same period. Every report in this phase is built against one shared query layer so there is one definition of "net sales", not five.

**Blocker:** Q-A (500 vs 1 000 bills/day design target) decides how aggressive the rollup strategy needs to be.

---

### P3-T01 — Shift lifecycle and cash management
**Depends on:** P1-T14 · **Est:** 2d · **SRS:** FR-8.1, FR-8.2, FR-8.6, FR-8.7

**Context.** Phase 1 built shift open and recovery. This adds the cash movements that make the drawer reconcilable.

**Do this.**
1. Cash in (float top-up, owner deposit) and cash out (petty expense, supplier payment, banking), each with a reason from a configurable list and an optional printed slip (FR-8.2).
2. Expected drawer calculation, defined once and used by both the X and Z reports:
   `expected = opening_float + cash sales − cash refunds + cash in − cash out`
3. Owner authorisation for cash out above a configurable threshold.
4. Cash movement history within the shift, visible to the cashier for their own shift.
5. `no sale` drawer open — owner authorised, audited (FR-7.7).

**Deliverables.** Cash movement service and UI, expected-cash calculator, cash slips, no-sale flow.

**Risks.** Two implementations of the expected-cash formula, one in X and one in Z. It must be a single method with a single test.

**Done when.**
- [ ] Cash in and out are recorded with reason, user and timestamp
- [ ] Expected cash matches a hand-worked example including refunds, cash in and cash out
- [ ] Cash out above the threshold requires owner authorisation and is audited
- [ ] A no-sale drawer open writes an audit row
- [ ] The expected-cash formula exists in exactly one place (verified by review and by both reports using the same service)

---

### P3-T02 — X report
**Depends on:** P3-T01 · **Est:** 1d · **SRS:** FR-8.3, RPT-04

**Context.** A mid-shift snapshot that changes nothing. The important property is that it is non-clearing — taking one must be free of side effects.

**Do this.**
1. X report content: sales count and value, returns, discounts, tax breakdown, tenders by type, cash movements, expected drawer, current shift duration.
2. Rendered to the thermal printer and to screen; exportable.
3. Available to the cashier for their own shift.

**Deliverables.** X report query, layout, print.

**Risks.** Accidentally mutating shift state. Add a test that takes 10 X reports and asserts the shift row is byte-identical afterwards.

**Done when.**
- [ ] X report figures match hand-computed values on a seeded shift
- [ ] Taking an X report changes no data whatsoever (asserted)
- [ ] It prints correctly on the thermal printer
- [ ] A cashier can take one for their own shift but not for another user's

---

### P3-T03 — Z report, shift close and rollups
**Depends on:** P3-T02 · **Est:** 2.5d · **SRS:** FR-8.4, FR-8.5, FR-8.8, AC-11, NFR-P5

**Context.** The most consequential transaction in the system after a sale. It locks a period permanently and it is where the rollup tables are written.

**Do this.**
1. Close flow: display expected cash, prompt for a physical count (denomination breakdown optional), compute variance, require a note when variance exceeds a configurable threshold.
2. Close transaction, all in one:
   - write `closed_at`, `counted_cash`, `expected_cash`, `variance`, `status = CLOSED`
   - build `daily_sales_summary` and `daily_product_summary` for the business date
   - audit row
   - `print_job` for the Z report
   - trigger a backup (FR-11.1)
3. The closed-shift insert trigger (already in the schema) prevents any later posting into that shift.
4. A Z report can never be deleted or re-run (FR-8.8) — the service has no such method, and re-close is rejected.
5. Variance history retained and charted over time (FR-8.6).

**Deliverables.** Z report, close transaction, rollup builders, variance history.

**Risks.** Rollups drifting from the raw data if a correction is later posted into an earlier date. Corrections go into the *current* period per FR-8.8; add a rollup-verification command that recomputes and compares, run monthly.

**Done when.**
- [ ] AC-11: X and Z reports are produced; Z variance is computed correctly against a deliberately mis-counted drawer, and the shift locks
- [ ] Attempting to post a sale into a closed shift is rejected by the database
- [ ] Re-running or deleting a Z report is impossible through any code path
- [ ] Rollup rows match a recomputation from raw data exactly
- [ ] A backup is taken automatically on close
- [ ] Variance history displays across 30 seeded shifts

---

### P3-T04 — Report query layer
**Depends on:** P3-T03 · **Est:** 2d · **SRS:** FR-9.1–9.6, NFR-P5

**Context.** One shared layer so that "net sales" has exactly one definition. This is what makes AC-12 achievable rather than a recurring bug.

**Do this.**
1. `Reporting/Queries/` with Dapper queries returning DTOs. Date-range filter with the presets from FR-9.1 (today, yesterday, this week, this month, last month, this year, custom).
2. Canonical definitions, written down in `docs/report-definitions.md` and implemented once:
   - **Gross sales** = sum of line totals before discount, excluding tax
   - **Net sales** = gross − discounts − returns, excluding tax
   - **COGS** = sum of `sale_line.unit_cost × qty_base` − returned equivalent
   - **Gross profit** = net sales − COGS
   - **Tender total** = sum of `payment.amount` for the period
3. Query routing: ranges wholly in closed periods read rollups; ranges touching the open shift read raw tables and union.
4. Owner-only projections exclude cost and margin fields entirely for cashier sessions (AC-17).
5. Every query parameterised, no string concatenation.

**Deliverables.** Query layer, canonical definitions document, routing logic.

**Risks.** Rollup/raw union double-counting at the boundary. The boundary is the business date of the open shift; test a range that spans it.

**Done when.**
- [ ] Every definition in `report-definitions.md` has exactly one implementation
- [ ] A range spanning closed and open periods returns the same figures as the same range computed from raw data only
- [ ] A one-year range returns in under 10 s on the seeded database (NFR-P5)
- [ ] Cashier-session DTOs contain no cost or margin field

---

### P3-T05 — Sales, returns and profit reports
**Depends on:** P3-T04 · **Est:** 2.5d · **SRS:** §9 RPT-01, RPT-02, RPT-03, FR-9.4

**Context.** The reports the owner actually opens.

**Do this.**
1. **RPT-01 Sales summary**: by day, by bill, by hour-of-day; bill count, average bill value, gross, discount, tax, net.
2. **RPT-02 Sales by item / category / brand**: quantity, net, COGS, margin, ranked.
3. **RPT-03 Profit report**: net sales, COGS, gross profit and margin percentage by period, category and item. Owner only.
4. Returns report: by reason, by item, by disposition, unlinked vs linked, with rate against sales.
5. Drill-down from summary to bill list to individual bill.

**Deliverables.** Four report screens with drill-down.

**Risks.** Margin computed from the current cost instead of the snapshot cost. Use `sale_line.unit_cost` always.

**Done when.**
- [ ] Each report's totals match a hand-computed figure on a fixed seeded dataset
- [ ] Profit uses the COGS snapshot, verified by changing a product's cost after a sale and confirming the report does not move
- [ ] Cashier sessions cannot open RPT-03 (service-level test)
- [ ] Drill-down from a summary row reaches the correct bill

---

### P3-T06 — Stock, tax and cash reports
**Depends on:** P3-T04, P2-T11 · **Est:** 2d · **SRS:** §9 remaining RPT-*, RPT-05, NFR-L1

**Context.** The rest of the §9 catalogue, including what the accountant and the tax authority need.

**Do this.**
1. Stock valuation, stock movement / item stock card, low stock and reorder, slow-moving, damage and adjustment summary.
2. Tax report: taxable and non-taxable sales, tax collected by rate, for the period — in the format the shop's accountant needs (Q-02).
3. **RPT-05 Tender / cash reconciliation**: tenders by type for the period, tied to Z reports.
4. Shift and variance history report.
5. Supplier purchase summary.

**Deliverables.** The remaining §9 reports.

**Risks.** Tax reported on a different basis (accrual point, inclusive handling) from what the accountant expects. Confirm Q-02 before building, not after.

**Done when.**
- [ ] Every report listed in SRS §9 exists and returns correct figures on seeded data
- [ ] The tax report reconciles to the sum of `sale_line.tax` for the period
- [ ] RPT-05 tender totals equal the sum of Z report tenders for the same period
- [ ] The stock card for one item reconstructs its balance history exactly from the ledger

---

### P3-T07 — Export and print for all reports
**Depends on:** P3-T05, P3-T06 · **Est:** 1.5d · **SRS:** FR-9.2, NFR-M6

**Context.** Uniform behaviour across every report, built once.

**Do this.**
1. A shared export service: CSV (CsvHelper), XLSX (ClosedXML), PDF (QuestPDF), driven by the report DTO and column metadata — not written per report.
2. Print any report to the A4 printer with a consistent header (shop name, report name, period, run time, run by).
3. Full data export (NFR-M6): products, stock, sales, returns, customers, suppliers to CSV, plus a plain unencrypted SQLite copy on demand.

**Deliverables.** Export service, report print layout, full-export command.

**Risks.** Money exported as a scaled integer or as a locale-formatted string that Excel misreads. Export as a plain decimal number with a period separator and let the sheet format it.

**Done when.**
- [ ] Every report exports to CSV, XLSX and PDF with matching totals
- [ ] Exported money values open as numbers in Excel, not text
- [ ] The full data export completes and re-imports into a fresh database
- [ ] Report print headers carry shop name, period, run time and user

---

### P3-T08 — Audit log viewer and exception reporting
**Depends on:** P3-T04 · **Est:** 1.5d · **SRS:** FR-9 (exceptions), NFR-S8, NFR-L2

**Context.** The owner's shrinkage and error control. The SRS positions cash variance patterns as the shop's main indicator of loss (FR-8.6); exceptions are the rest of that picture.

**Do this.**
1. Audit log viewer: filter by date, user, action, entity. Read-only, with no delete or edit affordance anywhere.
2. Exceptions report gathering: bill cancellations, over-limit discounts, unlinked returns, non-returnable overrides, no-sale drawer opens, negative-stock sales, adjustments above threshold, price changes, cash variances beyond threshold, open-item sales.
3. `VerifyChainCommand` exposed in the UI: verifies the `sale` and `audit_log` hash chains and reports the first break with its row id.
4. Exception counts on the owner dashboard.

**Deliverables.** Audit viewer, exceptions report, chain verification UI.

**Risks.** The exceptions report becoming noise. Make each category thresholded in settings so the owner can tune it.

**Done when.**
- [ ] Every exception category listed above appears with correct counts on seeded data
- [ ] The audit viewer has no path to modify or delete a row
- [ ] Chain verification passes on clean data and correctly identifies a deliberately tampered row
- [ ] A cashier cannot open the audit viewer

---

### P3-T09 — Phase 3 acceptance gate
**Depends on:** all P3 tasks · **Est:** 1.5d · **SRS:** AC-11, AC-12, AC-18, NFR-P5

**Context.** AC-12 is the credibility test for the whole system with the owner's accountant. It gets a generative test, not a single example.

**Do this.**
1. Acceptance tests `AC11`, `AC12`, `AC18`.
2. **Reconciliation generative test**: simulate 500 random trading days of sales, returns, exchanges, cancellations, cash movements and shift closes. For every day and every month, assert:
   - RPT-01 net sales == sum of tenders in RPT-05 == sales minus returns in RPT-02
   - rollups == recomputation from raw tables
   - `sum(payment.amount)` == `sum(sale.total) − sum(sale_return.total_refund)`
3. Extend the `P1-T16` performance harness with the report operations (NFR-P5) over a one-year range at the Q-A target volume; keep it a CI regression guard, not an absolute-budget gate.
4. Re-run all Phase 1 and 2 acceptance tests and the regression guard.

**Deliverables.** Reconciliation test suite, the report operations added to the performance harness.

**Risks.** The generative test failing on one day in 500 and being dismissed as a rounding artefact. It is not. Every failure here is a real defect in discount allocation, tax rounding or the rollup boundary.

**Done when.**
- [ ] AC-12 holds to the cent across 500 simulated days
- [ ] The performance harness covers every report over a one-year range and shows no >20% regression
- [ ] Rollups match raw recomputation for every simulated month
- [ ] Phase 1 and 2 gates still green

> AC-18 as an absolute pass/fail — every NFR-P1…P7 budget met on the shop terminal — is proven in **`HW-T07`**, not here.
