---
name: money-arithmetic
description: Use when writing or reviewing any code that handles prices, totals, discounts, tax, costs, refunds or quantities in this POS. Covers the scaled-integer storage convention, decimal arithmetic, rounding points, discount allocation and the reconciliation invariants.
---

# Money and quantity arithmetic

Every money bug in this system is prevented or caused by the rules below.

## Storage

Money and quantity are stored as 64-bit `INTEGER` scaled by 10 000. `12345678` is `1234.5678`.

```
MoneyScale = 10_000    QtyScale = 10_000    RateScale = 10_000
```

- Range at this scale: ±9.2 × 10¹⁴ currency units. `ToScaled()` throws on overflow rather than wrapping.
- Never `REAL` (banned by DM-01), never `TEXT` (kills `SUM()`).
- EF Core maps via a `ValueConverter` to `long`. Every money column must have SQLite storage class `INTEGER` — there is a test for this.

## Arithmetic

All arithmetic happens in C# `decimal`, inside the `Money` and `Quantity` value objects.
SQL does addition and `SUM()` only. **Never multiply or divide money in SQL.**

`double` and `float` are banned in `Domain`, `Application` and `Infrastructure`. An architecture test scans for them, and a PostToolUse hook warns on sight.

## Rounding — exactly two points

1. Line total
2. Bill total

Nowhere else. Always through `IRoundingPolicy` (default: half away from zero, decimal places from settings per FR-10.2). If you find yourself rounding at a third point, the calculation is structured wrong.

```csharp
// Correct
var lineTotal = rounding.Round(unitPrice * qty - discount);

// Wrong - rounds an intermediate, drift accumulates across lines
var taxed = rounding.Round(unitPrice * taxRate) * qty;
```

## Tax

- **Exclusive:** `tax = round(net * rate)` per line.
- **Inclusive:** `tax = round(price - price / (1 + rate))` per line.
- Bill tax is the **sum of line tax**, never recomputed from the bill total. Recomputing from the total is how a bill fails to reconcile by one or two cents.

## Discount allocation

A bill-level discount is spread across lines by `DiscountAllocator` using largest-remainder: allocate proportionally, then give the residual minor unit to the largest line. The allocated parts must sum **exactly** to the discount. There is a property test over 1–50 random lines; do not replace it with an example test.

## The invariants, asserted before persistence

```
sum(line_total) == subtotal
subtotal - bill_discount + tax + rounding == total
sum(payments) == total          // for a completed sale
```

A violation **throws**. It never silently corrects. Silent correction is how a reconciliation break becomes untraceable three months later.

## Historical values are snapshots

`sale_line` stores `unit_price`, `unit_cost` and `description` as they were at the time of sale.

- Returns refund at `sale_line.unit_price` — the price originally paid (AC-03), **not** today's catalogue price.
- Margin reports use `sale_line.unit_cost` — the cost at time of sale, **not** the current moving average.

Neither is recoverable from the catalogue, because the catalogue moves. Test this by changing a price or cost after a sale and confirming the return amount and the margin report do not move.

## Quantities

- Always converted to the product's **base unit** before storage. `qty` and `uom_id` are kept for display and reprinting; `qty_base` is what stock and reports use.
- `STANDARD` products reject fractional quantities. `DECIMAL` products accept up to their UOM's `decimal_places`.
- Conversion must round-trip exactly: `FromBase(ToBase(q)) == q` for representable quantities. There is a property test over 10 000 random quantities.
