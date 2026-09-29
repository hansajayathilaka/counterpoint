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
