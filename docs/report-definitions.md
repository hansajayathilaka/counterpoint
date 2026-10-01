# Report definitions

**Task P3-T04** (SRS FR-9.1–FR-9.6, NFR-P5). The single, written definition of every figure the
report suite is built on, and the single place each is implemented.

Every report screen in P3-T05/P3-T06 projects the figures below from the shared query layer rather
than writing its own `SUM(...)`. That is what makes SRS **AC-12** ("report totals reconcile … to
the cent") achievable rather than a recurring bug: there is one definition of "net sales", not five.

## 1. Date-range presets (FR-9.1)

`ReportDateRange` (`src/Counterpoint.Application/Reporting/ReportDateRange.cs`) resolves each preset.
The range is **inclusive at both ends** and compared against the `business_date` `TEXT` column.

| Preset | Range |
|---|---|
| `Today` | `today .. today` |
| `Yesterday` | `today-1 .. today-1` |
| `ThisWeek` | Monday of the current week .. `today` (**week starts Monday**) |
| `ThisMonth` | first of the current month .. `today` |
| `LastMonth` | first of the previous month .. last day of the previous month |
| `ThisYear` | 1 January of the current year .. `today` |
| `Custom` | an explicit `from .. to` supplied by the caller |

`ThisWeek`, `ThisMonth` and `ThisYear` are **period-to-date** (they end at `today`); `LastMonth` is
a complete past month. The one implementation decides all of this, so no two reports can disagree
about what "last month" means.

## 2. Canonical figures

All figures are computed by `PeriodFiguresReader`
(`src/Counterpoint.Reporting/Queries/PeriodFiguresReader.cs`) and nowhere else in the report layer.
Money is `INTEGER` scaled by 10 000 (CLAUDE.md invariant 1); every figure is a `Money`.

| Figure | Definition | Where implemented |
|---|---|---|
| **Gross sales** | `SUM(subtotal + line_discount)` over completed sales — sales **before** any discount, **excluding tax**. `sale.subtotal` is already net of the line discount, so the line discount is added back. | `PeriodFiguresReader.ReadTotalsCoreAsync` |
| **Discounts** | `SUM(line_discount + bill_discount)` — line-level plus bill-level together. | `PeriodFiguresReader.ReadTotalsCoreAsync` |
| **Net sales** | `gross sales - discounts - returns`, **excluding tax**. `returns` here is `SUM(sale_return.subtotal)`, the return's own pre-tax subtotal (a restocking fee is not netted off it). Reduces algebraically to `subtotal - bill_discount - return_subtotal`. | `PeriodFiguresReader.ReadTotalsCoreAsync` |
| **Returns value** | `SUM(sale_return.total_refund)` — the cash value actually refunded, which is *not* the same figure net sales subtracts (see above). | `PeriodFiguresReader.ReadTotalsCoreAsync` |
| **COGS** | `SUM(sale.cogs)` (the moving-average cost captured on the sale header at the time of sale) **minus** the returned equivalent, `SUM(sale_return_line.unit_cost x qty_base)` **for `disposition = 'SELLABLE'` lines only**. A `DAMAGED` line's cost is never subtracted back out: `CreateReturnHandler` posts a `RETURN_IN` stock movement for a SELLABLE line alone, so only then is the cost genuinely recovered; a DAMAGED line neither restocks nor posts a write-off movement, so the shop has both refunded the money and lost the goods, and reversing its COGS here would erase that loss. The return side is multiplied in C# through `Money`, never as `scaled x scaled` in SQL (which double-scales). | `PeriodFiguresReader.ReadCogsCoreAsync`, mirrored by `DailyRollupCalculator.ComputeAsync` for the rollup it writes |
| **Gross profit** | `net sales - COGS`. | `ProfitPeriodSummaryQuery` |
| **Margin rate** | `gross profit / net sales`, a `decimal` fraction (`0.25` = 25%); `0` when net sales is zero. | `ProfitPeriodSummaryQuery` |
| **Tender total** | `SUM(payment.amount)` for the range — sales tendered less refunds paid out (a refund's `payment.amount` is stored negative). | `PeriodFiguresReader.ReadTotalsCoreAsync` for the raw segment; the rollup segment's figure is `tender_cash + tender_card + tender_other` on `daily_sales_summary`, written by `DailyRollupCalculator` |

Tax is reported separately (`SUM(sale.tax)` for the range's completed sales); it is excluded from
gross, net and profit.

**Cost is the snapshot cost, never the current one.** COGS reads `sale.cogs` and
`sale_return_line.unit_cost`, both captured at the time of the sale/return (CLAUDE.md invariant 10).
Changing a product's cost later must not move a past period's profit.

The same formulas are materialised into `daily_sales_summary` by the P3-T03 rollup builder
(`DailyRollupCalculator`, `Counterpoint.Infrastructure`). The report layer may not reference that
assembly (CLAUDE.md "Project boundaries"), so the raw formulas are written again in
`PeriodFiguresReader`. The two are kept in step by `ReportQueryLayerTests`
(`Counterpoint.Integration.Tests.Reporting`), whose routing fixtures assert the routed figures equal
the raw-only figures for the same range over a range that spans both sources.

**What that equality does and does not prove.** It is a genuine drift guard, but only for the
columns each fixture actually exercises, and only where the routed path reads at least one rolled-up
date while the raw-only path reads the raw tables for that same date — equality over a range whose
every date falls on one side of the split is no test of the other side at all. The fixtures
therefore cover: gross and net with a **non-zero line discount and a non-zero bill discount on a
rolled-up date** (`ARolledUpDateWithDiscountsAndACancelledBillReconcilesToRaw` — the guard for the
`subtotal + line_discount` gross and the `line_discount + bill_discount` discount terms on both
sides of the boundary), COGS including a returned line, a range that ends **before** the open
shift's date so the rollup segment's `To` bound is exercised
(`ARangeEndingBeforeTheOpenShiftDateIgnoresLaterRollups`), and a **cancelled sale** on a rolled-up
date (`ACancelledBillOnARolledUpDateIsNotCountedFromTheRollup` — the guard for the `status =
'COMPLETED'` filter). What it does not prove is that a *dimension the rollup does not carry* (hour
of day, per-tender-type, per-item net) reconciles; those reports must read raw (§ "Raw-only
opt-in").

## 3. Query routing (task P3-T04 "Do this" #3)

`PeriodFiguresReader` reads from two sources and adds the two segments:

- **Rollup segment** — `daily_sales_summary` rows with `business_date >= From AND business_date <=
  To AND business_date < SplitKey`, where `SplitKey` is the **open shift's `business_date`**. The
  range's own upper bound `To` is carried explicitly: `SplitKey` bounds the raw segment from below,
  and when the range ends before the open shift's date (e.g. "last month" while a shift is open
  today) `SplitKey` is *past* `To`, so without `<= To` the rollup segment would admit every rolled-up
  day between `To + 1` and `SplitKey - 1`. The rollup row for the open shift's own date is
  deliberately **excluded**: a single business date can hold a closed morning shift and an open
  afternoon shift, so that date is read from raw tables in full. A date whose rollup may be **stale**
  — it contains a sale that is not `COMPLETED`, i.e. a cancellation landed after the shift closed and
  nothing rebuilt `daily_sales_summary` — is excluded too, and read from raw instead.
- **Raw segment** — `sale`, `sale_return`, `sale_return_line` and `payment` rows whose business date
  (for `payment`, the date of the joined `sale`/`sale_return`; `payment` has no `business_date`
  column of its own) is `>= SplitKey`, **plus** any date that has *no* rollup row (e.g. a date no Z
  report has rolled up yet, or a tooling-seeded database), **plus** any date whose rollup the rollup
  segment distrusted. Reading the raw tables for those dates keeps the figures reconciling with raw
  data everywhere, instead of silently reading zero for an unrolled date or a stale figure for one
  whose rollup a cancellation has overtaken.

When no shift is open, `SplitKey` is sent one day past `To`, so every in-range date is closed: those
that carry a trustworthy rollup row are read from the rollup segment, and those that do not — or
whose rollup is distrusted — are read from raw. The two segments' predicates are the same three-way
set (before `SplitKey`; carries a rollup row; no non-`COMPLETED` sale on the date), so they are
disjoint by construction and no business date is counted twice.

### Raw-only opt-in

`ReportSourcePolicy.RawTablesRequired` skips the rollups entirely and reads the raw tables for the
whole range. A report must ask for it when it needs a dimension the rollup does not carry:

- `daily_sales_summary` has **no hour-of-day dimension** (RPT-01 by-hour cannot use it);
- it has only **three tender buckets** (`tender_cash`, `tender_card`, `tender_other`) —
  `BANK_TRANSFER`, `CREDIT_NOTE`, `ON_ACCOUNT` and `CHEQUE` all collapse into `OTHER`, so a
  per-tender-type report must read `payment` directly;
- `daily_product_summary.net` is the line net minus return refunds and **does not subtract
  `bill_discount`**, so per-item reports will not reconcile to the day net from rollups alone.

`RawTablesRequired` is an optimisation switch, never a correctness switch: both policies return the
same canonical figures for the same range, and that equality is a test. The precondition it rests on
is stated plainly rather than assumed: the equality holds **because the routing distrusts any rollup
for a date that is not fully `COMPLETED`**, reading such a date from raw instead. It is not a claim
that the rollup table is always correct. A stale `daily_sales_summary` row (a rollup not rebuilt
after a cancellation moved a day's trading) is a P3-T03 concern, not something this layer can
repair; what this layer guarantees is that a report never *adds* such a row's figure on top of the
raw truth.

## 4. Owner-only projections (FR-9.4, AC-17)

- `ISalesPeriodSummaryQuery` -> `SalesPeriodSummary` — **not owner-only**. The DTO has **no cost or
  margin field at all**, so a cashier session never receives one (CLAUDE.md invariant 8).
- `IProfitPeriodSummaryQuery` -> `ProfitPeriodSummary` — **owner-only** (`[RequiresRole(Role.Owner)]`).
  It adds `Cogs`, `GrossProfit` and `MarginRate` on top of the same figures. The concrete query is
  `internal` and is registered only wrapped with `RoleAuthorisation`, so no code path can resolve an
  undecorated profit query from the container. The shared engine `PeriodFiguresReader` — whose
  `ReadWithCogsAsync` returns COGS with no role check of its own — is not registered in the container
  either: each query builds its own from `IReportConnectionFactory`, so no container path resolves a
  COGS-bearing object.

## 5. The P3-T05 sales, profit and returns reports

No new headline definition is introduced: every screen below projects the canonical figures of section 2.
The formulas' arithmetic lives in `CanonicalFigures` (`src/Counterpoint.Reporting/Queries/CanonicalFigures.cs`),
which `PeriodFiguresReader` (whole period), `DailyFiguresReader` (by day, hour, tender) and `ItemFiguresReader`
(by item, category, brand) all call, so slicing a figure never re-derives it.

| Report | Query | Role | Reads |
|---|---|---|---|
| RPT-01 sales summary (by day, hour, tender, bill) | `ISalesSummaryReportQuery`, `ISalesBillQuery` | any signed-in user | totals routed (rollup + raw); breakdowns raw |
| RPT-02 sales by item / category / brand | `ISalesBreakdownQuery` | any signed-in user, **no cost field** | raw `sale_line`, `sale_return_line` |
| RPT-03 profit (day, month, category, brand, item) and the COGS/margin columns of RPT-02 | `IProfitReportQuery` | **owner only** (`[RequiresRole]`) | headline = `ProfitPeriodSummaryQuery`; rows raw |
| Returns report (SRS RPT-14) | `IReturnsReportQuery` | **owner only** (SRS section 9) | raw `sale_return`, `sale_return_line` |

Decisions the screens rest on:

- **Average bill value** = `(gross - discounts) / bill count` - discounted, tax-exclusive, before returns; zero with no bills.
  Returns are separate documents, not bills, so they do not enter it.
- **By hour** uses the wall-clock hour recorded in `sold_at` (bills) and `returned_at` (returns), which carry the shop's offset at the
  time. Net for an hour subtracts the returns taken in that hour, so the hours add up to the period net.
- **Per-row net (item/category/brand)** = `sum(line_total - bill discount share)` less the `line_refund` of return lines dated in the range.
  The share is `BillDiscountSplit` over the stored snapshot columns (the split a receipt and a return use). Rows add up to canonical net sales
  exactly (lines sum to `sale.subtotal`, shares to `sale.bill_discount`, refunds to `sale_return.subtotal`).
- **Per-row COGS** = `sale_line.unit_cost x qty_base` less SELLABLE return lines' `unit_cost x qty_base`, multiplied in C#. It is the same
  snapshot as `sale.cogs`; the sum of rows can differ from the headline by less than 0.0001 per sale (the header is quantised per sale, the
  lines are not). Day and month rows use `sale.cogs` and so match the headline exactly. No report reads `product.cost_avg`.
- **Category and brand** are the product's *current* filing - `sale_line` snapshots neither. Open-item lines and unfiled products share a
  "(No category)" / "(No brand)" bucket; on the item dimension open items are one "(Open items)" row.
- **Returns report**: value = pre-tax `line_refund` (the figure net sales subtracts); the cash figure `total_refund` is shown separately.
  Value return rate = `returns subtotal / (net sales + returns subtotal)`; count return rate = `returns / bills`. Returns are dated by
  their own business date.
- **Bill lists** show completed bills only, oldest first, capped (default 2 000) with a "cut short" flag. Bill net there is before returns
  (`subtotal - bill_discount`); returns against the bill are listed on the bill.

## 6. The P3-T06 stock, tax and cash reports

Task P3-T06 completes SRS section 9. No new headline figure is introduced: the sales-side figures below reuse section 2's
arithmetic (`CanonicalFigures`) and the same raw tables, and every multiplication of a cost or price by a quantity is done in C#
through `Money` and `Quantity`, never as a scaled-times-scaled product in SQL. Nothing in any of these reports rounds (rounding
belongs to a line total and a bill total alone).

| Report | SRS | Query | Role | Reads |
|---|---|---|---|---|
| Tax report | RPT-19 | `ITaxReportQuery` | **owner only** | raw `sale_line`, `sale_return_line` (never rollups) |
| Tender reconciliation | none (the phase plan's "RPT-05"; SRS RPT-05 is the Z report itself) | `ITenderReconciliationQuery` | **owner only** | raw `payment`, `shift` |
| Shift and cash-variance history | RPT-21 | `IShiftVarianceHistoryQuery` | **owner only** | `shift` close fields |
| Stock movement ledger / item stock card | RPT-11 | `IStockCardQuery` | **owner only** | `stock_movement` for one variant |
| Damage and shrinkage | RPT-15 | `IDamageAdjustmentReportQuery` | **owner only** | `stock_movement` (via `IAdjustmentHistoryQuery`), `sale_return_line` |
| Supplier purchase summary | RPT-16 | `ISupplierPurchaseReportQuery` | **owner only** | `goods_receipt`, `goods_receipt_line` |
| Stock valuation | RPT-09 | `IStockValuationQuery` | **owner only** | `stock_balance`, `product_variant.price` |
| Slow-moving and dead stock | RPT-12 | `ISlowMovingStockQuery` | **owner only** (was open before P3-T06) | `stock_balance`, `stock_movement` |
| Fast-moving items | RPT-13 | `IFastMovingReportQuery` | **owner only** | `ISalesBreakdownQuery` (item dimension) |
| Stock on hand | RPT-08 | `IStockOnHandQuery` | both roles, **no cost in the DTO or the SQL** | `stock_balance` |
| Low stock and reorder | RPT-10 | `IReorderListQuery` | both roles, no cost | `stock_balance`, `product_supplier`, `goods_receipt` |

Not built here, and why: RPT-17 supplier payables (the data model has no supplier payment record - nothing to age), RPT-18
customer receivables (P5-T02), RPT-20 / RPT-23 / RPT-25 (P3-T08), RPT-24 backup status (Phase 4), RPT-22 stock-take variance
(already a service from P2-T10). RPT-02's per-line detail and RPT-06's category/brand performance are covered by P3-T05's
sales-by-item report.

### Tax report (RPT-19)

- **Basis.** Taxable value is always **excluding tax**, in both pricing modes: a line's `line_total` (already net of tax whether the
  shop quotes inclusive or exclusive prices) less the line's share of its bill's discount, allocated with `BillDiscountSplit` over
  the stored snapshot columns. The same allocation - the same code, `TaxableLines` - is the X and Z report's tax breakdown, so the
  two cannot drift. `tax.prices_include_tax` only says how shelf prices were quoted and is shown in the header.
- **Accrual point.** Sales are taken at their `business_date` (the point of sale), completed bills only; a cancelled bill never
  counts. Returns are taken at **their own** `business_date`, not the date of the bill they reverse.
- **Rate.** The rows are the distinct `sale_line.tax_rate` values actually charged - the per-line snapshot. The catalogue's current
  `tax_class` is never joined, so re-rating a class cannot move a past period. A zero rate is the exempt / zero-rated row. A
  return line takes the rate of the sale line it reverses (`sale_return_line.sale_line_id`); a return with no such link is listed
  on its own "rate unknown (unlinked returns)" row and is never guessed into a rate.
- **Returns.** Taxable value returned is `sale_return_line.line_refund` (pre-tax, the figure net sales subtracts); tax returned is
  `sale_return_line.tax`. Net tax due = tax on sales - tax on returns, per row and in total.
- **Data-driven (Q-02).** No regime is baked in. The header carries the shop name, tax registration number, tax label and pricing
  basis from settings (`shop.name`, `shop.tax_registration_number`, `tax.label`, `tax.prices_include_tax`).
- **Reconciliation.** The report reads `SUM(sale.tax)`, `SUM(sale_line.tax)` and `SUM(sale_return.tax)` independently of its rows and
  sets `IsReconciled` only when the per-rate totals equal them. A false is a defect, never a rounding artefact.
- **Rollups.** `daily_sales_summary` holds one tax total and no per-rate split, so this report is the documented
  `ReportSourcePolicy.RawTablesRequired` case (section 3).

### Tender reconciliation

- Two independent reads that must agree. **Z side:** each closed shift whose `business_date` is in the range, its tenders by type
  from the very SQL the X and Z reports use (`ShiftTenderBreakdown.Sql`, shared by `XReportFiguresReader`). A Z report is not stored
  as a table, so it is recomputed, not looked up. **Payments side:** `payment` joined to its sale (completed) or return by business
  date - the same definition as the sales summary's by-tender table (`DailyFiguresReader.ReadTendersAsync`). `daily_sales_summary`
  is not read: it has three tender buckets, the ledger has seven tender types.
- The table shows, per tender type, Z sales / refunds / net, payments sales / refunds / net, and the **difference (must be zero)**.
- **The two sides disagree on what a "day" is.** `shift.business_date` is the day the shift was **opened**; a sale's or return's
  `business_date` is the wall-clock date of the event. The Z side takes *every* payment of each closed shift whose date is in the
  range (by `shift_id`, whatever date the documents carry); the payments side takes payments by the document's own date. A shift
  that trades past midnight therefore sits on different days on the two sides, and that is the only way the sides can differ
  (besides a genuine ledger defect). Every such case is **listed with a reason**, never left as a bare number:
  - *Payments side only* - an open shift dated in the range, and any completed sale or return dated in the range whose shift is not
    a closed in-range shift (open, or closed under a business date outside the range). Reason: "Not Z'd yet" or "This shift's Z
    report is dated X, outside this range, but it holds trading inside it".
  - *Z side only* - a closed in-range shift that also holds completed sales or returns dated **outside** the range (opened on the
    range's last day, traded past midnight; or a month ending on the shift's opening date). Reason: "This shift's Z report is dated
    X but it also holds trading dated Y, outside this range". The item counts those sales and returns.
  Each listed shift carries its signed **effect on the difference** (payments side less Z side, from the payments themselves).
  Per tender, `Unexplained` = difference less the listed shifts' effect on that tender; it is zero unless the ledger is
  inconsistent, and only then does the screen say "Ask for support". Widening the range to take in the whole shift removes that shift from the list.
- The report is **tied out** only when the difference is zero for every tender type and **no shift is listed**; a listed shift never
  coexists with a tie-out, even when its effects happen to net to zero.

### Shift and cash-variance history (RPT-21)

- One row per **closed** shift whose `business_date` is in the range, exactly the close fields stored at close (counted, expected,
  variance, note, closed by); an open shift has no variance and is not listed. Nothing is recomputed - expected cash is frozen at
  close.
- Summary, all in C#: net variance, total over (positive variances), total short (negative), mean absolute variance, running
  (cumulative) variance per row, and the count above the note threshold.
- **Threshold** shown is `policy.shift_close_variance_note_threshold` as configured **now** (the setting `CloseShiftHandler`
  reads); a shift is flagged when its absolute variance exceeds it. A threshold changed since a shift closed re-flags history.
- **Trend.** With at least four closed shifts, the mean absolute variance of the later half against the earlier half (the middle
  shift of an odd count is left out): smaller later = improving, larger = worsening, equal = steady; fewer than four = no trend
  claimed.

### Stock movement ledger / item stock card (RPT-11)

- One variant, chosen by SKU or barcode, over a date range. Every `stock_movement` in the range with type, signed quantity, unit
  cost, reference document (bill, receipt, return or stock-take number where it resolves), note, the **running balance**, and the
  `balance_after` the ledger recorded.
- **Order is the ledger's own: `stock_movement.id`.** The ledger is append-only and each movement's `balance_after` follows the one
  posted before it, so posting order is the balance chain. It is **usually chronological** (a movement is stamped as it posts), but
  goods-receipt, adjustment, damage and bulk-break postings accept a caller-supplied date, so a **back-dated posting sits out of
  date order and is flagged** on the card: a row is marked *Back-dated* when the movement posted just before it is dated later,
  and marked *follows movements dated outside this range* when that predecessor is not the row shown above it. This is why a
  range's opening balance can differ from the previous day's close. A day is the wall-clock date at the front of `occurred_at`
  (the ledger has no `business_date`).
- **Opening balance** = the `balance_after` of the movement immediately before the first in-range row in ledger order (0 when none),
  read, not summed; with no rows in range it is the last earlier movement's `balance_after`. Each row's running balance =
  the `balance_after` of **its own predecessor in the ledger** (in the date range or not; `LAG` over the variant's ledger) plus its
  quantity, in scaled integers. Where the rows shown are an unbroken run of the ledger this is exactly opening + cumulative quantity.
- **Reconciles** when every row's running balance equals its recorded `balance_after`, i.e. each row follows from the ledger row
  before it. A healthy ledger therefore always reconciles, whatever slice of it a date range selects (including a sparse one such as
  an old OPENING row and a back-dated receipt with the movements between them outside the range). The card also reports whether the
  rows are **contiguous**; only then does opening + sum(quantity) = closing hold for the rows shown, and the screen says so. A
  mismatching row is flagged on screen; a false means the recorded balance does not follow from the movement before it - the
  ledger chain for that variant is broken and is a defect.
- This report **is** the ledger read, through `ix_movement_variant_time` for one variant. It never `SUM`s the ledger into a balance
  anything else uses and never feeds or rebuilds `stock_balance` (CLAUDE.md invariant 3). A movement-type filter is not offered:
  a running balance over a subset of movement types would not match `balance_after`.

### Damage and shrinkage (RPT-15)

- Sources: ledger **adjustment** and **damage** movements (read through `IAdjustmentHistoryQuery`, now also filterable by calendar
  day), grouped by the mandatory reason (the movement note); and **damaged returns** (`sale_return_line.disposition = 'DAMAGED'`,
  by the return's business date), grouped by the return line's reason. A damaged return is not restocked and posts no ledger
  movement, so nothing is counted twice.
- **Value = quantity x recorded unit cost** (the ledger's cost on the movement; the return line's cost snapshot), multiplied in C#.
  It is the effect on stock value: negative is a loss. A damaged return shows as a loss of the goods' cost. Total loss / total gain
  add up **individual** movements, not the netted rows, so an adjustment that found three and lost five shows both.

### Supplier purchase summary (RPT-16)

- By supplier: per supplier `goods_receipt` count, **value = subtotal + other_cost (landed cost excluding tax)**, tax, total. By
  item: quantity, value = `line_total - tax` (line subtotal plus its freight share), weighted average cost = value / quantity, and
  first, last and change in landed cost per base unit (`unit_cost_base`). Supplier and item value add up to the same figure.
- **Cost movement**: every receipt-line cost point for items whose landed cost differed between receipts in the range.
- A receipt is dated by the wall-clock date of `received_at` (no business date on the table). Supplier **payables** (RPT-17) are not
  reported: the data model records receipts but no supplier payments.

### Stock valuation (RPT-09) - as at now

- Quantity on hand x moving-average `cost_avg`, and the same stock x the variant's retail `price`, per variant and in total, exact
  (no rounding). Optional category filter (a category and its children).
- **"As at" is now, and only now.** The report reads the current `stock_balance` and the current costs; a past date cannot be
  reconstructed from them and the ledger is never summed to fake one. The screen states this and stamps the moment it ran; there
  is no as-at date control.

### Slow-moving and dead stock (RPT-12) - the decision

SRS RPT-12 is "items with no **sale** in N days, with value tied up", and FR-4.20 says "no sale in *N* days, configurable". P2-T11 measured staleness from the last stock movement of
**any** type, so a goods receipt, stock count or adjustment restarted a dead line's clock. **Decision (P3-T06): idle time runs from
the last `SALE` movement.**

- A bill that was later cancelled does not count as a sale. A return, receipt, count, adjustment or bulk break never resets the clock.
- A variant that has **never sold** is measured from its **first** ledger movement (the day its stock first arrived), so a receipt
  from last week is not flagged as dead on day one, while stock that arrived long ago and never sold is.
- Only variants with positive stock on hand, active variants of active products. **Value tied up** = `qty_base x cost_avg`, in C#.
  The report is owner-only because of that cost figure. `LastMovementAt` (any type) is still returned for information.

### Fast-moving items (RPT-13)

A thin top-N projection over `ISalesBreakdownQuery` (item dimension): by units (net units, sold less returned) and by value (net
sales excluding tax). The open-item row is not an item and is omitted; an item with no net units (or net value) is omitted from that
ranking. It reads nothing the sales-by-item report does not, so it reconciles to it.

### Stock on hand (RPT-08) and low stock / reorder (RPT-10)

- Open to both roles; the DTOs and SQL carry **no cost**. Stock on hand lists active stock-tracked variants (types STANDARD and
  DECIMAL) from `stock_balance`, in base units and each alternate unit (base quantity / `product_uom.conversion_factor`, unrounded),
  with location and reorder level; filters: category (and children), brand, supplier link, location text, in-stock only.
- Reorder list: the P2-T11 predicate, unchanged (sum of a product's active variants at or below `reorder_level`, level above zero),
  with supplier and category filters and a grouped-by-preferred-supplier view, "no supplier linked" last.
