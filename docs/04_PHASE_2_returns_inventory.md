# Phase 2 — Returns and Inventory Control

**Duration:** 4 weeks · **Tasks:** 12 · **Exit:** the shop can control stock and handle returns correctly. With Phase 1 this is the minimum viable system.

## Scope

Linked and unlinked returns, exchanges, credit notes, purchase orders, goods receipt with UOM conversion, stock adjustments and damage, bulk breaking, stock takes, and reorder alerts.

## The theme of this phase

Every operation here changes stock **and** money at the same time. The SRS is explicit (§2.2 item 4) that a return is not a negative sale. Two rules govern the whole phase:

1. Every stock change goes through `StockLedger.PostAsync` (P1-T07). No exceptions.
2. Every money-affecting operation posts a balanced set of rows in one transaction, and a report can reconcile it.

**Blocker:** Q-03 (exact return policy) must be answered before P2-T01.

---

### P2-T01 — Return policy engine
**Depends on:** P1-T03, P1-T08 · **Est:** 2d · **SRS:** FR-5, BR-*, FR-10.5, Q-03, AC-05

**Context.** Return rules are configuration, not code (NFR-M1). Building the policy engine before the return workflow keeps the rules out of the UI and makes each one testable in isolation.

**Do this.**
1. `Domain/Returns/ReturnPolicy` evaluating, from settings: return window in days, receipt required or not, restocking fee, non-returnable categories and the per-product `non_returnable` flag, cash refund limit, default refund method.
2. `ReturnEligibility` result type: `Allowed`, `AllowedWithOverride(reason)`, `Denied(reason)`. Never a bare boolean — the UI needs the reason to display and the audit log needs it recorded.
3. Rules to implement, each with its own test:
   - outside the return window
   - product flagged non-returnable (cut goods, mixed paint) — override only (AC-05)
   - cumulative over-return against a bill line — **never overridable** (AC-06)
   - unlinked return when unlinked returns are disabled
   - cash refund above the configured limit — override only
   - returning a line already fully returned
4. Policy text surfaced in settings and printed on the receipt; a test asserts the printed text and the enforced policy come from the same setting (NFR-L3).

**Deliverables.** Policy engine, eligibility result type, rule tests, policy-text binding.

**Risks.** Over-return being made overridable "for flexibility". It must be structurally impossible — AC-06 is a fraud control, not a policy preference.

**Done when.**
- [ ] Each rule above has a passing test named for its SRS requirement
- [ ] Over-return is denied with no override path anywhere in the codebase
- [ ] Changing the return window in settings changes both enforcement and the printed policy text
- [ ] A non-returnable item is denied and proceeds only with an owner override, which is audited (AC-05)

---

### P2-T02 — Linked returns
**Depends on:** P2-T01, P1-T07, P1-T10 · **Est:** 3d · **SRS:** FR-5.1–5.10, AC-03, AC-06

**Context.** The core return path: find the original bill, pick lines and quantities, choose disposition per line, refund at the price originally paid.

**Do this.**
1. Bill lookup by scanning the receipt QR, typing the bill number, or searching by date/customer/amount (FR-5).
2. Return screen: original lines with quantity sold, already returned, and available to return. Per-line quantity (decimal-aware) and per-line disposition `SELLABLE` | `DAMAGED` (FR-5).
3. Refund calculation at `sale_line.unit_price` — the original price, including any discount that was applied (AC-03). Never today's price.
4. `CreateReturnCommand` transaction:
   - allocate `return_no`
   - insert `sale_return` (+ hash chain) and `sale_return_line`
   - increment `sale_line.qty_returned` (the one permitted update, checked against the cumulative guard first)
   - post `RETURN_IN` movements via `StockLedger` **only for `SELLABLE` lines**; `DAMAGED` lines post a `DAMAGE` movement to a damaged bucket, not to sellable stock
   - insert refund `payment` rows (negative amounts)
   - audit row
   - `print_job` for the return receipt
5. Restocking fee applied per policy, shown separately on the receipt.

**Deliverables.** Bill lookup, return screen, `CreateReturnCommand`, return receipt.

**Risks.** Refunding at the current catalogue price. This is the single most common bug in POS returns and it costs the shop money in both directions. Test it explicitly with a price change between sale and return.

**Done when.**
- [ ] AC-03: a partial return refunds at the original paid price, restocks only `SELLABLE` lines, and prints a correct return receipt
- [ ] AC-06: cumulative over-return across two separate returns against the same line is impossible
- [ ] A price change between sale and return does not affect the refund amount
- [ ] `DAMAGED` disposition does not increase sellable stock
- [ ] Scanning the receipt QR opens the correct bill
- [ ] Return totals reconcile: `sum(return lines) − fee == sum(refund payments)`

---

### P2-T03 — Unlinked returns
**Depends on:** P2-T02 · **Est:** 1.5d · **SRS:** FR-5 (unlinked), FR-10.5, NFR-S2

**Context.** The SRS calls this "the main return-fraud exposure" (§19). It is a deliberately high-friction path.

**Do this.**
1. Disabled by default; enabled only in settings.
2. Always requires an owner override, with a mandatory reason.
3. Refund price defaults to the **current** price and is capped at it; the operator cannot type a higher figure.
4. Refund method restricted per settings — default to credit note rather than cash.
5. Prominently reportable: a dedicated exceptions report line in Phase 3.

**Deliverables.** Unlinked return flow, override gate, exception flagging.

**Risks.** Making it convenient. It should be slightly awkward by design.

**Done when.**
- [ ] Disabled in settings, the option does not appear and the service method refuses
- [ ] Enabled, it requires owner re-authentication and a typed reason, both audited
- [ ] The refund amount cannot exceed the current selling price
- [ ] The return appears flagged in the audit log and is queryable as an exception

---

### P2-T04 — Exchanges
**Depends on:** P2-T02 · **Est:** 2d · **SRS:** FR-5 (exchange), AC-04

**Context.** An exchange is a return and a sale in one transaction with the difference settled once. It must not be two disconnected documents, or reports will not reconcile.

**Do this.**
1. Exchange flow: start from a return, add replacement items, compute the difference.
2. Positive difference → collect tender. Negative difference → refund or credit note per policy.
3. Single transaction creating both the `sale_return` and the new `sale`, cross-linked via `sale_return.exchange_sale_id`.
4. One receipt showing both sides and the net.

**Deliverables.** Exchange flow, linked documents, combined receipt.

**Risks.** Double-counting in reports — the exchange sale appearing as a fresh sale and the return as an unrelated refund. Reports must net them; add a reconciliation test.

**Done when.**
- [ ] AC-04: an exchange with a higher-priced replacement collects the correct difference in one transaction
- [ ] A lower-priced replacement refunds or issues credit correctly per policy
- [ ] Both documents exist, are cross-linked, and stock moves correctly in both directions
- [ ] Daily totals with an exchange reconcile exactly (net sales, tenders, returns)

---

### P2-T05 — Credit notes
**Depends on:** P2-T02 · **Est:** 2d · **SRS:** FR-5 (store credit), FR-3 (tender)

**Context.** Store credit is supported; loyalty is explicitly out of scope (OS-07). Keep it to a numbered voucher with a balance.

**Do this.**
1. Issue a credit note from a return: numbered from `number_sequence`, amount, optional customer, optional expiry.
2. Redeem as a tender type on a sale, partially or fully; `credit_note_redemption` records each use and decrements `amount_remaining`.
3. Statuses: `ACTIVE`, `SPENT`, `EXPIRED`, `VOID`. Expiry evaluated at redemption, not by a background job.
4. Lookup by number or customer; printed credit note document.
5. Outstanding credit notes report line (feeds Phase 3).

**Deliverables.** Issue, redeem, lookup, print, status handling.

**Risks.** Over-redemption through two partial redemptions racing. Single-user makes this unlikely, but still decrement inside the sale transaction with a guard, not before it.

**Done when.**
- [ ] A credit note issued from a return has the correct amount and prints
- [ ] Partial redemption leaves the correct remaining balance
- [ ] Redeeming more than the remaining balance is rejected
- [ ] An expired credit note is refused at redemption with a clear message
- [ ] Total outstanding credit reconciles against issued minus redeemed

---

### P2-T06 — Suppliers and purchase orders
**Depends on:** P1-T04 · **Est:** 2d · **SRS:** FR-4 (purchasing), FR-6

**Context.** Purchase orders are useful but not load-bearing — the GRN is what changes stock. Keep the PO light.

**Do this.**
1. PO creation: supplier, expected date, lines with quantity in any UOM and cost.
2. Statuses `DRAFT` → `SENT` → `PARTIAL` → `RECEIVED` | `CANCELLED`, driven by receipt progress.
3. Suggested-order generation from reorder levels (`stock_balance.qty_base < product.reorder_level` → propose `reorder_qty`).
4. Printed PO document.

**Deliverables.** PO service, screens, suggested ordering, PO print.

**Risks.** Building a full procurement module. OS-10 excludes special orders and contractor accounts — stay inside the line.

**Done when.**
- [ ] A PO can be created, printed and sent
- [ ] Receiving against a PO advances its status correctly, including partial receipt
- [ ] Suggested ordering proposes the right items and quantities from reorder levels
- [ ] Cancelling a PO does not affect stock

---

### P2-T07 — Goods receipt (GRN)
**Depends on:** P2-T06, P1-T07 · **Est:** 3d · **SRS:** FR-4.1–4.8, AC-08

**Context.** The main inbound stock path, and where moving-average cost is set. UOM conversion happens here, so a box→piece receipt must land in base units correctly.

**Do this.**
1. GRN entry: supplier, invoice number, date, optional PO link. Lines with quantity in any of the product's UOMs, unit cost per that UOM.
2. Convert to base units and per-base cost on entry, storing both (`qty`, `uom_id`, `qty_base`, `unit_cost`, `unit_cost_base`).
3. Optional other costs (freight) apportioned across lines by value, feeding into landed cost.
4. Commit transaction: insert GRN + lines, post `GRN` movements via `StockLedger`, recompute moving-average cost, update `product_supplier.last_cost`, audit, `print_job`.
5. Offer a selling-price update prompt where the new cost exceeds the current selling price (ties to FR-2.18).
6. Print labels for the received batch (hooks into P1-T12, FR-2.12).

**Deliverables.** GRN entry screen, conversion, cost apportionment, ledger posting, GRN print, label batch.

**Risks.** Moving-average cost computed on the pre-conversion quantity. Assert with the AC-08 case explicitly.

**Done when.**
- [ ] AC-08: a GRN including a box→piece conversion increases stock in base units correctly and updates moving-average cost correctly
- [ ] Freight apportionment sums exactly to the entered freight amount
- [ ] Receiving against a PO updates `qty_received_base` and the PO status
- [ ] A GRN whose cost exceeds the selling price prompts a price review
- [ ] Labels for the batch print correctly

---

### P2-T08 — Adjustments and damage
**Depends on:** P1-T07 · **Est:** 1.5d · **SRS:** FR-4 (adjustments, damage), NFR-S2

**Context.** The manual stock-change path, and therefore a shrinkage risk. Owner only, reason mandatory, fully audited.

**Do this.**
1. Adjustment screen: select variants, enter a signed quantity or a target quantity, mandatory reason from a configurable list plus free text.
2. `ADJUSTMENT` and `DAMAGE` movement types via `StockLedger`, valued at current `cost_avg`.
3. Owner role required; every adjustment audited with before/after quantity.
4. Adjustment history report line (feeds Phase 3 exceptions).

**Deliverables.** Adjustment screen, damage flow, audit entries.

**Risks.** Adjustments used as a shortcut for receipts, hiding cost. Warn when an inbound adjustment exceeds a configurable value threshold and suggest a GRN instead.

**Done when.**
- [ ] A cashier cannot reach the adjustment path (service-level test, AC-17 pattern)
- [ ] Every adjustment produces a movement with the correct sign, cost and `balance_after`
- [ ] Reason is mandatory and appears in the audit log
- [ ] Damage writes off at current average cost and is separately reportable

---

### P2-T09 — Bulk breaking
**Depends on:** P1-T05, P1-T07 · **Est:** 2d · **SRS:** FR-4.9, AC-09

**Context.** Converting one packaging form into another — a coil into loose metres, a box into pieces. Distinct from UOM conversion: this moves stock between two *different SKUs*, and the value must be conserved.

**Do this.**
1. Bulk-break screen: source variant and quantity, destination variant, expected output quantity (allowing for wastage), reason.
2. Transaction posts a **balanced pair**: `BULK_BREAK_OUT` on the source (negative, at source `cost_avg`) and `BULK_BREAK_IN` on the destination (positive), carrying the total cost across and recomputing the destination's moving average. Both share one `ref_doc_id`.
3. Wastage handled explicitly as a `DAMAGE` movement, not by silently losing value.
4. A validation report: every bulk-break pair nets to zero in value (allowing for declared wastage).

**Deliverables.** Bulk-break screen, paired posting, wastage handling, value-conservation report.

**Risks.** Cost invented at the destination rather than carried across, which quietly corrupts margin reporting for every subsequent sale of that item.

**Done when.**
- [ ] AC-09: a bulk break (1 coil → 90 m) posts a balanced pair of movements with cost carried across
- [ ] The destination's moving-average cost after the break matches a hand-worked figure
- [ ] Declared wastage posts as `DAMAGE` and the three movements together conserve value
- [ ] The value-conservation report finds no unbalanced pairs across 1 000 random breaks

---

### P2-T10 — Stock take
**Depends on:** P1-T07 · **Est:** 2.5d · **SRS:** FR-4 (stock take), AC-10

**Context.** A count sheet is generated with frozen system quantities, counted offline, then posted as one batch of corrections. Freezing matters: trading continues during the count.

**Do this.**
1. Start a stock take with a scope: all, category, brand or rack location. Freeze `system_qty` per line at generation time.
2. Printed count sheet (FR-7.10) and on-screen entry, scanner-driven, with partial saving across sessions.
3. Variance report: system vs counted, quantity and value, sorted by value impact.
4. Post corrections as one batch of `STOCK_TAKE` movements through `StockLedger`, in one transaction, owner-authorised.
5. Handle items sold during the count: the correction applies to the *current* balance, using the variance against the frozen figure — document this behaviour on screen so the owner understands what is being posted.
6. Abandon path that posts nothing.

**Deliverables.** Stock take lifecycle, count sheet, entry screen, variance report, batch posting.

**Risks.** Posting an absolute quantity rather than a variance, wiping out sales made during the count. Post variances.

**Done when.**
- [ ] AC-10: a stock take across one category produces a correct variance report and posts corrections in one batch
- [ ] Items sold during the count end at the arithmetically correct final balance
- [ ] Partial counts can be saved and resumed
- [ ] Abandoning a stock take leaves stock untouched
- [ ] The posting is a single transaction — killing the app mid-post leaves stock either fully corrected or untouched

---

### P2-T11 — Reorder alerts and stock reports (interim)
**Depends on:** P2-T07 · **Est:** 1d · **SRS:** FR-4 (reorder), FR-9.7

**Context.** The full report suite is Phase 3. This is the operational minimum the shop needs to buy stock correctly once GRN exists.

**Do this.**
1. Low-stock / reorder list: below reorder level, with suggested order quantity and preferred supplier.
2. Stock valuation at moving-average cost (owner only).
3. Slow-moving and non-moving items by last movement date.
4. Dashboard low-stock count wired to the real query.

**Deliverables.** Three stock reports, dashboard wiring.

**Risks.** Writing these as one-off queries in the UI. Put them in `Reporting` with the same shape the Phase 3 suite will use, so they are not rewritten.

**Done when.**
- [ ] The reorder list matches a hand-computed expectation on seeded data
- [ ] Valuation ties to `sum(stock_balance.qty_base × cost_avg)` exactly
- [ ] A cashier cannot open the valuation report (service-level test)
- [ ] All three run in under 10 s on the seeded database

---

### P2-T12 — Phase 2 acceptance gate
**Depends on:** all P2 tasks · **Est:** 1.5d · **SRS:** AC-03, AC-04, AC-05, AC-06, AC-08, AC-09, AC-10

**Context.** Lock down the return and inventory behaviour before reports are built on top of it. Every figure Phase 3 reports depends on these transactions being right.

**Do this.**
1. Automated acceptance tests `AC03`, `AC04`, `AC05`, `AC06`, `AC08`, `AC09`, `AC10`.
2. A stock-conservation invariant test: after 10 000 random operations (sales, returns, GRNs, adjustments, breaks, stock takes), `stock_balance` equals the ledger sum for every variant.
3. A value-conservation test: total inventory value change equals receipts − COGS + adjustments + write-offs, to the cent.
4. Re-run the Phase 1 performance gate — returns add joins to the hot paths.

**Deliverables.** Acceptance suite extension, invariant tests, refreshed performance baseline.

**Risks.** The stock-conservation test failing intermittently, which means a code path bypasses `StockLedger`. Find it; do not retry the test.

**Done when.**
- [ ] All seven acceptance tests pass in CI
- [ ] Stock conservation holds over 10 000 random operations
- [ ] Value conservation reconciles to the cent
- [ ] Phase 1 performance budgets still met
- [ ] A full simulated trading day including returns, an exchange, a GRN and a stock take completes with correct end-state figures
