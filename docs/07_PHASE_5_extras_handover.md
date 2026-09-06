# Phase 5 — Extras and Handover

**Duration:** 2 weeks · **Tasks:** 9 · **Exit:** go-live.

## Scope

Trade pricing tiers, quantity breaks, credit customer accounts, quotations, the A4 invoice for trade customers, weighing-scale integration, data migration from the shop's existing records, training, documentation and the handover package.

## The theme of this phase

Everything here is a **Should** or **Could** in the SRS. If time is short, cut from this phase rather than from Phases 2–4. The one exception is P5-T06 (data migration) and P5-T07/T08 (training and documentation), which are go-live blockers.

---

### P5-T01 — Trade pricing and quantity breaks
**Depends on:** P1-T08 · **Est:** 2d · **SRS:** FR-2.14, FR-2.15, FR-2.16

**Context.** The price resolver from P1-T08 already has the precedence chain and the `price_tier` table exists. This activates the trade and quantity-break levels.

**Do this.**
1. Trade tier applied automatically to customers flagged `TRADE` (FR-2.14). Selecting a trade customer mid-bill re-prices existing lines, with a visible notification of what changed.
2. Quantity-break pricing: `min_qty` bands per variant per tier (FR-2.15). The band is re-evaluated when a line's quantity changes.
3. Time-bound promotional prices with `valid_from`/`valid_to`, reverting automatically with no manual action (FR-2.16).
4. The bill shows which price basis applied per line, so the cashier can explain it to the customer.
5. Bulk maintenance of tier prices by category, brand or supplier, with preview (extends FR-2.19).

**Deliverables.** Trade tier, quantity breaks, promotional pricing, price-basis display, bulk tier maintenance.

**Risks.** Re-pricing a bill after a discount has been applied by hand, silently discarding the cashier's discount. Warn and ask before overwriting a manual discount.

**Done when.**
- [ ] Selecting a trade customer re-prices the bill correctly and reports what changed
- [ ] A quantity crossing a break boundary re-prices that line immediately
- [ ] A promotional price starts and ends on its dates with no manual action
- [ ] Precedence tests from P1-T08 still pass with all levels populated
- [ ] Each line shows which price basis was used

---

### P5-T02 — Credit customers and accounts
**Depends on:** P1-T04, P1-T10 · **Est:** 2.5d · **SRS:** FR-6, Q-05, OS-10

**Context.** Basic credit accounts only. Job costing, contractor project accounts and special orders are explicitly out of scope (OS-10) — do not drift into them.

**Do this.**
1. `ON_ACCOUNT` tender type: allowed only for customers with a credit limit, blocked when the sale would exceed it, override by owner.
2. Customer balance maintained transactionally with each on-account sale, return and payment received.
3. Payment received against an account: amount, tender type, allocation to oldest outstanding first, printed receipt.
4. Customer statement: opening balance, transactions, closing balance, for a date range; printable and exportable.
5. Aged receivables report: current, 30, 60, 90+ days.

**Deliverables.** On-account tender, balance maintenance, payment receipt, statement, ageing report.

**Risks.** Customer balance drifting from the transaction history. Balance is a projection over `sale`, `sale_return` and account payments — add a reconciliation command and a startup sample check, exactly as with `stock_balance`.

**Done when.**
- [ ] An on-account sale increases the balance by the bill total, in the same transaction
- [ ] A sale exceeding the credit limit is blocked and proceeds only with an owner override, audited
- [ ] A payment received reduces the balance and prints a receipt
- [ ] The statement reconciles: opening + charges − payments − returns == closing, to the cent
- [ ] The balance projection matches a recomputation from history for every customer

---

### P5-T03 — Quotations
**Depends on:** P1-T09 · **Est:** 1.5d · **SRS:** FR-7.10 (quotation), FR-3

**Context.** A common hardware-shop need: price up a job, hand over a printed quote, convert it to a sale if the customer returns.

**Do this.**
1. Build a quote using the sales screen, save it with a number from `number_sequence`, a validity date and a customer.
2. Quotes reserve **no** stock and post **no** movements. Make this explicit on screen.
3. Print the quote (A4 or thermal), with validity date and current prices.
4. Convert a quote to a sale, re-pricing at current prices with a visible warning where prices have changed since the quote.
5. Quote list with status: open, converted, expired.

**Deliverables.** Quote creation, print, conversion, list.

**Risks.** Reserving stock against quotes. It is not in scope and it breaks the stock model.

**Done when.**
- [ ] A quote saves, prints and appears in the list
- [ ] A quote posts no stock movement
- [ ] Converting a quote produces a normal sale, with a warning on any changed price
- [ ] An expired quote is flagged and can still be converted with confirmation

---

### P5-T04 — A4 invoice and document polish
**Depends on:** P1-T11 · **Est:** 1.5d · **SRS:** FR-7.2, FR-7.9, FR-7.10, NFR-L1

**Context.** Trade customers need a formal invoice. Phase 1 built the QuestPDF path; this makes it presentable and legally complete.

**Do this.**
1. A4/A5 invoice layout: logo, shop details, tax registration number, customer details, line table, tax breakdown, terms, payment details — the full NFR-L1 set confirmed against Q-02.
2. Consistent styling across every printed document: quotation, GRN, PO, stock-take sheet, credit note, statement, X/Z reports.
3. Save-as-PDF and a share/export action for any document (FR-7.9).
4. Confirm the legally required fields with the shop's accountant and record the confirmation in `docs/`.

**Deliverables.** A4 invoice, unified document styling, PDF export, accountant confirmation.

**Risks.** Missing a legally required field. Q-02 must be answered by the accountant, not assumed.

**Done when.**
- [ ] The A4 invoice carries every field in the confirmed NFR-L1 list
- [ ] Invoice totals match the thermal receipt to the cent
- [ ] Every document listed in FR-7.10 renders in a consistent style
- [ ] Any document saves as PDF and opens correctly

---

### P5-T05 — Weighing scale integration
**Depends on:** P1-T09 · **Est:** 1.5d · **SRS:** §14.2 (optional), FR-3.6 · **Physical verification:** `HW-T04`

**Context.** Optional in the SRS. Build it only if the shop actually has a scale and its protocol is documented (Q-07/Q-14). Development binds `NullScale`; `HW-T04` binds the real serial port.

**Do this.**
1. `IScale` with `NullScale` for development and a serial implementation via `System.IO.Ports` (Windows-guarded) for the terminal; one protocol adapter per model, written as a **pure parser** over captured serial frames.
2. Read weight into the quantity field for `DECIMAL` products, with a manual-entry fallback always available.
3. Stability detection: only accept a settled reading.
4. Scale absent or failing degrades to manual entry with a warning — never blocks the sale (C-05).

**Deliverables.** Scale abstraction, `NullScale`, protocol adapter(s) with frame-parser tests, sales-screen integration.

**Risks.** Building against an undocumented protocol by guesswork. If the protocol is not documented, defer the task and record it as deferred rather than shipping something unreliable.

**Done when.**
- [ ] The protocol adapter parses captured serial frames to the correct weight and stability flag (unit tests)
- [ ] Unstable readings are rejected until settled
- [ ] With `NullScale` bound, the quantity field falls back to manual entry with a warning and the sale completes
- [ ] Precision matches the UOM's configured decimal places

> Reading a live weight on the actual scale over a real serial port is **`HW-T04`**.

---

### P5-T06 — Data migration
**Depends on:** P1-T13 · **Est:** 2d · **SRS:** Q-08, AC-07 · **Go-live blocker**

**Context.** Real data from the shop's existing records — a spreadsheet, another POS export, or a stock book. Almost always messier than the sample.

**Do this.**
1. Obtain the actual data early and run it through the P1-T13 importer.
2. Clean-up pass with the owner: duplicate items, inconsistent units, missing categories, items with no cost, items that no longer exist.
3. Opening stock posted as `OPENING` movements at agreed costs, dated to the go-live date.
4. Reconciliation: the owner verifies a sample of 50 items — name, unit, price, cost, quantity — and signs off.
5. Full backup taken and verified immediately after migration, before any trading.
6. Migration is repeatable: if the count is wrong, it can be wiped and re-run cleanly before go-live.

**Deliverables.** Migrated catalogue and opening stock, reconciliation record, post-migration backup.

**Risks.** Migrating on go-live morning. Do it at least a week early, trade in parallel on paper for a few days, and compare.

**Done when.**
- [ ] The full catalogue is imported with a validation report and zero unresolved errors
- [ ] Opening stock posts as `OPENING` movements and matches the owner's count
- [ ] The owner has verified and signed off a 50-item sample
- [ ] Stock valuation after migration matches the owner's expectation within an agreed tolerance
- [ ] A verified backup exists from immediately after migration
- [ ] The migration can be re-run from scratch if needed

---

### P5-T07 — Documentation
**Depends on:** all prior phases · **Est:** 2d · **SRS:** AC-20, NFR-M5, FR-11.15, UI-12 · **Go-live blocker**

**Context.** AC-20 makes documentation part of acceptance. It is also what makes NFR-M5 real — the owner is not locked to the vendor only if someone else can pick this up.

**Do this.**
1. **User manual** (cashier): sale, decimal quantities, unit switching, discount, hold/recall, payment, reprint, return, exchange, stock enquiry, shift open and close. Plain language, screenshots.
2. **Admin manual** (owner): products and variants, UOM setup, pricing, GRN, adjustments, stock take, all reports, settings, users, backup, and the full restore procedure with screenshots (FR-11.15).
3. **Keyboard cheat sheet**: one printable page, the ten most common tasks and the full function-key map (UI-12).
4. **Technical documentation**: schema ERD (export the diagrams from `01_DATA_MODEL.md`), build and publish instructions, deployment of the portal, dependency list with licences, ADR index, known quirks (printer, scale).
5. **Runbook**: what to do when the printer fails, the scanner fails, the disk fills, a backup fails for days, the database will not open, or the machine dies.

**Deliverables.** Four documents plus the runbook, in the repository and printed for the shop.

**Risks.** Documentation written from the developer's mental model rather than the cashier's. Have someone who has not used the system follow the user manual to complete a sale, a return and a day close.

**Done when.**
- [ ] A person who has never seen the system completes a sale, a return and a day close using only the user manual
- [ ] The restore procedure was executed successfully from the manual alone in P4-T09
- [ ] The cheat sheet fits one page and covers the full UI-02 key map
- [ ] Technical docs let a new developer build and run from a clean checkout, verified by someone doing it
- [ ] All AC-20 items exist: user manual, admin manual, cheat sheet, source code, schema documentation

---

### P5-T08 — Training
**Depends on:** P5-T06, P5-T07 · **Est:** 1d · **SRS:** NFR-U1 · **Go-live blocker**

**Context.** NFR-U1 sets the bar: a cashier with no prior POS experience completes a normal sale after 30 minutes of training. That is a measurable claim — measure it.

**Do this.**
1. Cashier session: sales, decimal quantities, unit switching, holds, payment, reprint, returns, shift open and close, what to do when the printer fails.
2. Owner session: everything above plus products, pricing, GRN, stock take, reports, X/Z, settings, backup and restore.
3. Practice on a **training database** with realistic data — never on live data.
4. Time the cashier from zero to an unaided correct sale. If it exceeds 30 minutes, the UI or the manual needs work, not the cashier.
5. Leave the cheat sheet at the counter and the runbook by the machine.

**Deliverables.** Two completed training sessions, a timed NFR-U1 result, materials left on site.

**Risks.** Training on live data and leaving test transactions in the real ledger. Use a separate training database and delete it afterwards.

**Done when.**
- [ ] The cashier completes an unaided correct sale within 30 minutes of starting training (recorded)
- [ ] The cashier can complete a return and a day close unaided
- [ ] The owner can run a GRN, a stock take, the Z report and a backup unaided
- [ ] No training transactions exist in the live database
- [ ] Cheat sheet and runbook are physically at the counter

---

### P5-T09 — Go-live and handover
**Depends on:** all prior tasks, the full HW track (`HW-T01…HW-T10`) · **Est:** 1d · **SRS:** §16 (all AC), NFR-M5, AC-20

**Context.** Final sign-off plus the handover that makes the owner independent of the vendor. The on-hardware acceptance itself is done in `HW-T10`; this task confirms its record and closes out.

**Do this.**
1. Confirm the **`HW-T10` acceptance record** — the full AC-01…AC-20 run on the shop terminal — is complete and every criterion passed. Do not start go-live if any HW-track exit-review item is open.
2. Handover package: source repository access, build instructions, schema documentation, dependency and licence list, portal infrastructure code and credentials, installer, and the signed passphrase custody record.
3. Confirm the backup chain end to end on the live system: local, USB, cloud, portal listing, and one test restore.
4. Go-live checklist: opening float set, printer and scanner working, first bill printed and checked, backup verified, cheat sheet and runbook in place, support contact posted.
5. Sign-off per SRS §16 and the SAD sign-off block.
6. Agree the Phase 6 warranty arrangement: response expectations, what counts as a defect versus a change request, and how changes are requested.

**Deliverables.** Completed acceptance record, handover package, go-live checklist, signed acceptance.

**Risks.** Going live on the shop's busiest day, or without a verified backup. Pick a quiet day, and do not open the till until a restore has been proven on the live data.

**Done when.**
- [ ] The `HW-T10` record shows all 20 acceptance criteria passing on the shop terminal, and the HW-track exit review is fully signed off
- [ ] The handover package is delivered and the owner confirms they hold everything in NFR-M5
- [ ] A restore of live data has been proven before the first real bill
- [ ] The shop trades a full day on the system with the developer available but not intervening
- [ ] Sign-off is signed by both parties
- [ ] Phase 6 warranty terms are agreed in writing
