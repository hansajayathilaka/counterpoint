# Counterpoint — Development Plan

Companion to `Counterpoint_Requirements.md` (SRS v1.0) and `POS_Architecture_Design.md` (SAD v1.0).

**Stack (decided):** .NET 10 LTS · C# · Avalonia UI · SQLite + SQLCipher · EF Core (writes/migrations) + Dapper (hot reads/reports).

---

## Document set

| # | Document | Purpose |
|---|---|---|
| 00 | [`00_ENGINEERING_GUIDE.md`](00_ENGINEERING_GUIDE.md) | Solution layout, coding rules, invariants, definition of done. **Copy this to `CLAUDE.md` at the repo root.** |
| 01 | [`01_DATA_MODEL.md`](01_DATA_MODEL.md) | Full schema, DDL, ER diagrams, state machines, enumerations |
| 02 | [`02_PHASE_0_walking_skeleton.md`](02_PHASE_0_walking_skeleton.md) | Scaffold, DB, one sale, one receipt, one installer |
| 03 | [`03_PHASE_1_core_trading.md`](03_PHASE_1_core_trading.md) | Catalogue, UOM, stock ledger, sales, tender, printing, local backup |
| 04 | [`04_PHASE_2_returns_inventory.md`](04_PHASE_2_returns_inventory.md) | Returns, exchanges, credit notes, GRN, adjustments, stock take |
| 05 | [`05_PHASE_3_reports_cash.md`](05_PHASE_3_reports_cash.md) | Shifts, X/Z, full report suite, rollups, audit surfacing |
| 06 | [`06_PHASE_4_backup_resilience.md`](06_PHASE_4_backup_resilience.md) | Cloud backup pipeline, encryption, portal, guided restore |
| 07 | [`07_PHASE_5_extras_handover.md`](07_PHASE_5_extras_handover.md) | Trade pricing, credit accounts, A4 invoices, labels, migration, training |
| 08 | [`08_TASK_INDEX.md`](08_TASK_INDEX.md) | Flat list of all tasks with dependencies and a suggested build order |
| 09 | [`09_HARDWARE_INTEGRATION.md`](09_HARDWARE_INTEGRATION.md) | The HW track: real-device drivers and on-site verification, run after the software is feature-complete |

---

## How to use this with Claude Code

1. Create the repo, put `00_ENGINEERING_GUIDE.md` at the root as `CLAUDE.md`, and put the SRS, SAD and this plan in `docs/`.
2. Work **one task at a time**, in the order given in `08_TASK_INDEX.md`. Each task is sized to be a single focused session and a single PR.
3. Prompt shape that works well:

   > Read `docs/00_ENGINEERING_GUIDE.md`, `docs/01_DATA_MODEL.md` and task **P1-T07** in `docs/03_PHASE_1_core_trading.md`. Implement it. Do not implement anything from other tasks. Stop when every checkbox under "Done when" passes, and show me the test run.

4. Do not let a task expand. If a task turns out to need something from a later task, stop and say so rather than building ahead — the dependency order exists because retrofitting the ledger, numbering and audit machinery is the expensive failure mode in this system.
5. Every task ends with tests passing and the app still starting. The build is never left red between tasks.

---

## Phase summary

| Phase | Name | Tasks | Est. | Exit condition |
|---|---|---|---|---|
| **0** | Walking skeleton | 7 | 1 wk | One product, one sale, one printed receipt, one encrypted backup, one installer — end to end |
| **1** | Core trading | 16 | 5 wk | The shop can sell and print. Minimum viable till. |
| **2** | Returns & inventory control | 12 | 4 wk | The shop can control stock and handle returns correctly |
| **3** | Reports & cash discipline | 9 | 3 wk | The owner has visibility and the day closes cleanly |
| **4** | Backup & resilience | 9 | 2 wk | The business is protected off-site and restore is proven |
| **5** | Extras & handover | 9 | 2 wk | Feature-complete software |
| **HW** | [Hardware integration](09_HARDWARE_INTEGRATION.md) | 10 | ~1 wk on site | Every peripheral and the terminal verified; NFR-P1–P7 figures recorded |
| **6** | Warranty | — | 3 mo | Stable operation |

**Phases 0–5 are built and accepted against the Linux device fakes.** The HW track
runs on site once the software is feature-complete (all phase gates green in CI),
before `P5-T09` go-live. Phase 4 must not be deferred past go-live.

---

## Task template

Every task in phases 0–5 follows the same shape:

```
### <PHASE>-T<NN> — <title>
**Depends on:** <task ids>  ·  **Est:** <days>  ·  **SRS:** <requirement ids>

**Context.**      Why this task exists and what it must be consistent with.
**Do this.**      Numbered, concrete implementation steps.
**Deliverables.** Files and artifacts that must exist when it is done.
**Risks.**        What goes wrong here, and the mitigation.
**Done when.**    Checkable acceptance criteria. All must pass.
```

`Done when` items are written to be verifiable by a test or a demonstrable action, never by opinion.

---

## Open items that block work

These are carried from SAD §16 and must be answered before the phase noted.

| # | Question | Blocks | Default if unanswered |
|---|---|---|---|
| Q-A | 500 or 1,000 bills/day design target? | Phase 3 | 500/day, rollups make 1,000 safe |
| Q-B | Windows 11 hardware, Win10 ESU, or Linux terminal? | Phase 5 (packaging) | Win11-capable hardware |
| Q-C | Second language on screen/receipt? | Phase 1 (print IR) | English only |
| Q-D | Google Drive or operator-controlled bucket? | Phase 4 | S3-compatible with versioning |
| Q-E | Owner signs the lost-passphrase acknowledgement | Phase 4 | Required before go-live |
| Q-H | Exact printer + scanner models | HW track (and Phase 5 packaging) | Software builds against the Linux fakes until then; order the hardware so it is on site before the HW track and go-live |
| Q-01/02 | Currency, tax regime, legally required bill fields | Phase 1 | Configurable, tax-inclusive |
| Q-03 | Return policy specifics | Phase 2 | 14 days, receipt required, cut goods non-returnable |
| Q-12 | Cashier discount limits | Phase 1 | 5% line / 5% bill |
| Q-16 | Bill number format | Phase 0 | `INV-YYYY-NNNNNN` from 1 |
