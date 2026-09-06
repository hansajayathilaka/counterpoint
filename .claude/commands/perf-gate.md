---
description: Run the performance budgets against a seeded database
allowed-tools: Read, Grep, Glob, Bash, Task
model: opus
---

Run the performance gate.

## 0. Where this runs

`/perf-gate` produces real NFR-P1…P7 pass/fail figures **only on the shop terminal**,
as task `HW-T07` (`docs/09_HARDWARE_INTEGRATION.md`). A figure measured on a dev
machine or a CI runner is not a figure — do not write one into
`docs/perf-baseline.md`.

- **On the shop terminal (HW-T07):** run every step below and populate
  `docs/perf-baseline.md` with figures and the exact hardware.
- **Anywhere else (dev, CI):** if the software perf harness from `P1-T16` exists,
  run it as a *relative regression guard* only — report timings against
  `docs/perf-baseline.md` and flag any >20% drift, but do not report pass/fail
  against the budgets and do not edit the baseline. If the harness or the seeded
  database does not exist yet, say exactly what is missing and stop.

## 1. Seed

Ensure the seeded database exists: 20 000 SKUs and 100 000 historical bill lines (`tools/SeedGenerator`, or `bash scripts/seed.sh`). If it does not exist, generate it. AC-18 is measured against this volume, not against an empty database.

## 2. Measure each budget

| Requirement | Budget | Operation |
|---|---|---|
| NFR-P1 | 300 ms | Barcode scan to line on the bill |
| NFR-P2 | 500 ms | Search results begin appearing |
| NFR-P3 | 2 s | Bill save, stock update, print dispatch |
| NFR-P4 | 1 s | Bill lookup by number |
| NFR-P5 | 10 s | Any one-year report |
| NFR-P6 | 10 s | Cold start to the sales screen |
| NFR-P7 | — | No degradation at 500 000+ lines |

## 3. Report

A table: requirement, budget, measured, headroom, pass or fail. Compare against `docs/perf-baseline.md` and flag any regression over 20% even where the budget is still met — that is the early warning.

## 4. If something fails

Diagnose before optimising. The usual causes, in the order worth checking:
- missing or unused index (check the query plan with `EXPLAIN QUERY PLAN`)
- EF change tracking on a read path that should use Dapper
- a report reading raw tables where it should read rollups
- EF model building at startup — the compiled model is not wired up
- rebuilding a whole UI collection instead of updating it incrementally

Do not fix by relaxing a budget. The budgets come from what a cashier can tolerate with a customer waiting.

**Only when running as HW-T07 on the shop terminal:** update `docs/perf-baseline.md`
with the new figures and the exact hardware they were measured on. **A figure
measured on a dev machine is not a figure** — NFR-P6 is about the shop's actual
low-powered terminal. A dev or CI run leaves the baseline untouched and reports
regression only.
