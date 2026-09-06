# Task Index

62 software tasks across six phases, plus a 10-task **hardware-integration track**
(`HW-T01…HW-T10`, `docs/09_HARDWARE_INTEGRATION.md`) run on site after the software
is feature-complete. Build in the order listed. Each task is one branch and one PR.

The software tasks are built and accepted against the Linux device fakes. Device
tasks (`P0-T05`, `P0-T07`, `P1-T11`, `P1-T12`, `P5-T05`) keep only the checks a
byte-stream snapshot or a fake-failure test can prove; their physical verification
is a checkbox on a named `HW-T*` task.

---

## Full list

| Task | Title | Depends on | Est | Key SRS |
|---|---|---|---|---|
| **P0-T01** | Solution scaffold and architecture tests | — | 1d | NFR-M5 |
| **P0-T02** | Money and Quantity value objects | P0-T01 | 1d | DM-01, DM-02 |
| **P0-T03** | Database bootstrap, SQLCipher, connection factory | P0-T01 | 1.5d | NFR-M2, R2, S3, S6 |
| **P0-T04** | Minimal schema, migrations, append-only triggers | P0-T03 | 1d | DM-03…DM-05 |
| **P0-T05** | ESC/POS renderer and one printed receipt | P0-T03 | 1.5d | FR-7.1, FR-7.7 |
| **P0-T06** | One sale, end to end | P0-T02, T04, T05 | 1.5d | FR-3, NFR-P3 |
| **P0-T07** | Backup snapshot, encryption, installer | P0-T06 | 1.5d | FR-11.1–11.4, NFR-M2 |
| **P1-T01** | Full schema migration | P0-T04 | 2d | §11 |
| **P1-T02** | Users, authentication, roles, authorisation | P1-T01 | 2d | FR-1, NFR-S1/S2/S9 |
| **P1-T03** | Settings framework | P1-T01 | 1.5d | FR-10, NFR-M1 |
| **P1-T04** | Catalogue reference data | P1-T02, T03 | 2d | FR-2.20, FR-2.21, FR-6 |
| **P1-T05** | Product, variant and UOM conversion domain | P1-T04 | 3d | FR-2.1–2.8 |
| **P1-T06** | Barcodes and product search | P1-T05 | 2d | FR-2.9–2.12, NFR-P1/P2 |
| **P1-T07** | Stock ledger and balance projection | P1-T05 | 2d | FR-4, DM-05 |
| **P1-T08** | Pricing and discount engine | P1-T05, T03 | 2d | FR-2.13–2.19, FR-3.7–3.10 |
| **P1-T09** | Sales screen and bill building | P1-T06, T07, T08 | 4d | FR-3, UI-01–UI-12 |
| **P1-T10** | Tender, change and sale completion | P1-T09 | 2d | FR-3.16–3.22, AC-02 |
| **P1-T11** | Receipt templates and printing | P0-T05, P1-T10 | 3d | FR-7, §10 |
| **P1-T12** | Label printing | P1-T06 | 1.5d | FR-2.10, FR-2.12 |
| **P1-T13** | Spreadsheet import | P1-T05, T07 | 2.5d | FR-2.22, AC-07 |
| **P1-T14** | Shift open (minimal) and dashboard | P1-T10 | 1.5d | FR-8.1, FR-9.7 |
| **P1-T15** | Local and USB backup | P0-T07, P1-T14 | 1.5d | FR-11.1–11.4 |
| **P1-T16** | Phase 1 acceptance and performance gate | all P1 | 2d | AC-02/07/13/15/16/17/19 |
| **P2-T01** | Return policy engine | P1-T03, T08 | 2d | FR-5, FR-10.5 |
| **P2-T02** | Linked returns | P2-T01, P1-T07, T10 | 3d | AC-03, AC-06 |
| **P2-T03** | Unlinked returns | P2-T02 | 1.5d | FR-5, NFR-S2 |
| **P2-T04** | Exchanges | P2-T02 | 2d | AC-04 |
| **P2-T05** | Credit notes | P2-T02 | 2d | FR-5 |
| **P2-T06** | Suppliers and purchase orders | P1-T04 | 2d | FR-4, FR-6 |
| **P2-T07** | Goods receipt (GRN) | P2-T06, P1-T07 | 3d | AC-08 |
| **P2-T08** | Adjustments and damage | P1-T07 | 1.5d | FR-4 |
| **P2-T09** | Bulk breaking | P1-T05, T07 | 2d | FR-4.9, AC-09 |
| **P2-T10** | Stock take | P1-T07 | 2.5d | AC-10 |
| **P2-T11** | Reorder alerts and stock reports (interim) | P2-T07 | 1d | FR-4, FR-9.7 |
| **P2-T12** | Phase 2 acceptance gate | all P2 | 1.5d | AC-03…AC-10 |
| **P3-T01** | Shift lifecycle and cash management | P1-T14 | 2d | FR-8.1, FR-8.2, FR-8.6 |
| **P3-T02** | X report | P3-T01 | 1d | FR-8.3, RPT-04 |
| **P3-T03** | Z report, shift close and rollups | P3-T02 | 2.5d | FR-8.4/8.5/8.8, AC-11 |
| **P3-T04** | Report query layer | P3-T03 | 2d | FR-9.1–9.6 |
| **P3-T05** | Sales, returns and profit reports | P3-T04 | 2.5d | RPT-01…RPT-03 |
| **P3-T06** | Stock, tax and cash reports | P3-T04, P2-T11 | 2d | §9, RPT-05 |
| **P3-T07** | Export and print for all reports | P3-T05, T06 | 1.5d | FR-9.2, NFR-M6 |
| **P3-T08** | Audit log viewer and exception reporting | P3-T04 | 1.5d | NFR-S8, NFR-L2 |
| **P3-T09** | Phase 3 acceptance gate | all P3 | 1.5d | AC-11, AC-12, AC-18 |
| **P4-T01** | Backup target abstraction | P1-T15 | 1.5d | FR-11.5, Q-09 |
| **P4-T02** | Least-privilege and immutability setup | P4-T01 | 1d | NFR-S4 |
| **P4-T03** | Upload worker with retry and backoff | P4-T01 | 2d | FR-11.5–11.8 |
| **P4-T04** | Retention and pruning | P4-T02, T03 | 1d | FR-11.9 |
| **P4-T05** | Backup web portal | P4-T02 | 2.5d | FR-11.10, FR-11.11 |
| **P4-T06** | Guided restore from cloud | P4-T05, P1-T15 | 2d | FR-11.12/11.13, AC-14 |
| **P4-T07** | Monthly restore self-test | P4-T06 | 1d | FR-11.14 |
| **P4-T08** | Resilience hardening | P4-T03 | 1.5d | NFR-R1–R5, AC-13/15/16 |
| **P4-T09** | Restore drill with the owner | P4-T06, T07 | 0.5d | FR-11.15, NFR-R5 |
| **P5-T01** | Trade pricing and quantity breaks | P1-T08 | 2d | FR-2.14–2.16 |
| **P5-T02** | Credit customers and accounts | P1-T04, T10 | 2.5d | FR-6, Q-05 |
| **P5-T03** | Quotations | P1-T09 | 1.5d | FR-7.10 |
| **P5-T04** | A4 invoice and document polish | P1-T11 | 1.5d | FR-7.2, NFR-L1 |
| **P5-T05** | Weighing scale integration | P1-T09 | 1.5d | §14.2 |
| **P5-T06** | Data migration | P1-T13 | 2d | Q-08, AC-07 |
| **P5-T07** | Documentation | all | 2d | AC-20, NFR-M5 |
| **P5-T08** | Training | P5-T06, T07 | 1d | NFR-U1 |
| **P5-T09** | Go-live and handover | all | 1d | §16 |

**Total:** ~112 developer-days ≈ 17 weeks for one developer, matching the SRS's indicative 16–18 weeks. The HW track adds ~1 week on site after that, once the hardware has arrived.

---

## Hardware-integration track (HW)

Run on site after the software is feature-complete (`P1-T16`, `P2-T12`, `P3-T09`,
`P4-T08` green in CI) and before `P5-T09` go-live. All `mode=human`. Full detail in
[`09_HARDWARE_INTEGRATION.md`](09_HARDWARE_INTEGRATION.md).

| Task | Title | Depends on | Picks up from |
|---|---|---|---|
| **HW-T01** | Windows spooler receipt printing and cash drawer | P0-T05, P1-T11 | P0-T05, P1-T11 physical checks; AC-16 hardware path |
| **HW-T02** | Barcode scanner bring-up | P1-T09 | P1-T09 scan-path physical check |
| **HW-T03** | Label printer driver and verification | P1-T12 | P1-T12 physical checks |
| **HW-T04** | Serial weighing scale driver | P5-T05 | P5-T05 physical checks |
| **HW-T05** | Windows key store and data-directory ACLs on the terminal | P0-T03, P0-T07 | P0-T03, P0-T07 key-store / ACL checks |
| **HW-T06** | Installer and clean-machine commissioning | P0-T07 | P0-T07 installer / clean-machine checks |
| **HW-T07** | On-terminal performance gate (NFR-P1…P7) | P1-T16, P3-T09, HW-T06 | NFR-P6 from P1-T01; perf items in P1-T16, P3-T09; AC-18 |
| **HW-T08** | Failure injection on real peripherals | P4-T08, HW-T01/02/04 | P4-T08 physical failure paths |
| **HW-T09** | Offline trading-day dress rehearsal | P1-T16, P4-T08, HW-T07, HW-T08 | AC-13, AC-15 hardware paths |
| **HW-T10** | Full AC-01…AC-20 run on the shop terminal | P5-T08, HW-T01…T09, all phase gates | P5-T09's "on the shop's actual hardware" acceptance record |

---

## Critical path

```
P0-T01 → P0-T03 → P0-T04 → P1-T01 → P1-T05 → P1-T07 → P1-T09 → P1-T10
       → P2-T02 → P3-T03 → P3-T04 → P3-T09 → P4-T06 → [software-complete]
       → HW-T01 → HW-T07 → HW-T09 → HW-T10 → P5-T09
```

Anything on this path that slips slips the project. Notably: **P1-T05 (UOM and variants)** and **P1-T09 (sales screen)** are the two largest single tasks and the two most likely to overrun. The HW tail cannot start until the hardware is on site (`Q-H`) — order it early even though it is not needed until then.

---

## Tasks that can run in parallel with a second developer

| Track | Tasks |
|---|---|
| Devices (software: renderers, IR, templates, outbox, snapshot tests) | P0-T05, P1-T11, P1-T12, P5-T05 |
| Backup & portal | P0-T07, P1-T15, P4-T01…T07 |
| Reporting | P3-T04…T08 |
| Import/export & migration | P1-T13, P3-T07, P5-T06 |
| Documentation | P5-T07, ongoing from Phase 1 |
| Hardware integration (on site, after software-complete) | HW-T01…HW-T10 |

The catalogue → stock → sales chain (P1-T05 → T07 → T09 → T10) is inherently sequential and should stay with one developer.

---

## Acceptance criteria coverage map

Every SRS acceptance criterion, and where it is first proven.

| AC | Criterion | Proven in |
|---|---|---|
| AC-01 | All Must requirements implemented | P5-T09 |
| AC-02 | 10-line bill, decimal qty, unit switch, discount, split tender | P1-T10, gated P1-T16 |
| AC-03 | Partial return at original price, selective restock | P2-T02 |
| AC-04 | Exchange with higher-priced replacement | P2-T04 |
| AC-05 | Non-returnable cut item blocked, owner override logged | P2-T01 |
| AC-06 | Cumulative over-return impossible | P2-T02 |
| AC-07 | 100 SKUs imported with validation report | P1-T13 |
| AC-08 | GRN with box→piece conversion, moving-average cost | P2-T07 |
| AC-09 | Bulk break posts a balanced pair with cost carried | P2-T09 |
| AC-10 | Stock take variance report and batch correction | P2-T10 |
| AC-11 | X and Z reports, variance, shift locks | P3-T03 |
| AC-12 | Report totals reconcile to the cent | P3-T09 |
| AC-13 | Full trading day offline, zero functional loss | P1-T16 (network-disabled test), hardened P4-T08; on hardware **HW-T09** |
| AC-14 | Backup uploaded, downloaded, verified, restored to clean machine | P4-T06 (clean VM); replacement hardware in **HW-T08** |
| AC-15 | Power cut mid-bill leaves DB uncorrupted | P1-T16 (process-kill on Linux), hardened P4-T08; on the terminal **HW-T09** |
| AC-16 | Printer disconnected mid-transaction, sale completes | P1-T11 (fake-failure test), hardened P4-T08; real printer **HW-T01** |
| AC-17 | Cashier cannot view cost, verified at business-logic layer | P1-T02 |
| AC-18 | Performance targets with 20k SKUs, 100k lines | Software regression guard P1-T16 / P3-T09; absolute budgets on the terminal **HW-T07** |
| AC-19 | Gapless numbering across 500 bills incl. cancellations | P1-T16 |
| AC-20 | Manuals, cheat sheet, source, schema docs delivered | P5-T07 |

**Every acceptance criterion has an owning task.** If a criterion has no test by the end of its owning phase, that phase is not done. AC-13, AC-15, AC-16 and AC-18 are proven in software against the fakes at the phase gate and **re-verified on the shop hardware** in the HW track (`HW-T07…HW-T10`); both are required before go-live.

---

## Things that must not be deferred

Ordered by how expensive they are to retrofit.

| # | Item | Task | Why |
|---|---|---|---|
| 1 | `stock_movement` ledger + `stock_balance` projection | P1-T07 | Retrofitting an event ledger over mutable balances means rebuilding every inventory feature |
| 2 | `number_sequence` allocation inside the transaction | P0-T06 | Gapless numbering (AC-19) cannot be reconstructed after the fact |
| 3 | Audit log + hash chain | P0-T06, P1-T02 | Tamper evidence (NFR-L2) is worthless if it starts halfway through the history |
| 4 | COGS snapshot on `sale_line` | P1-T10 | Historic margin is unrecoverable once catalogue costs move |
| 5 | Authorisation in the application layer | P1-T02 | AC-17 fails if it was ever only in the UI |
| 6 | Local + USB backup | P1-T15 | The window between "real data exists" and "backup exists" is pure risk |
| 7 | Print outbox pattern | P0-T06 | Retrofitting means touching every document type |
| 8 | Architecture tests | P0-T01 | Boundaries erode silently without them |

---

## Suggested prompt for starting a task

> Read `CLAUDE.md`, `docs/01_DATA_MODEL.md`, and task **P2-T07** in `docs/04_PHASE_2_returns_inventory.md`.
> Implement exactly that task. Do not start any other task.
> Follow the invariants in `CLAUDE.md` §4 — in particular, every stock change goes through `StockLedger.PostAsync`, and money never touches a `double`.
> When you are done, run `dotnet test` and show me the output, then walk me through each "Done when" checkbox and how it is satisfied.
