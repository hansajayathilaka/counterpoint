# Autopilot run log

Append-only record of unattended runs. Written by `scripts/autopilot.sh`, never
by hand. Read this to find out what a run actually did, in what order, and why
it stopped.

- `2026-09-04T02:13:30Z` **-** init — log created

## Run 2026-09-04T02:13:30Z

- branch: `claude/autopilot-wxgo5c`
- head: `49cb0ca`
- budget: 2 task(s)

- `2026-09-04T02:13:30Z` **P0-T01** start — Solution scaffold and architecture tests
- `2026-09-04T02:33:48Z` **P0-T01** fix-attempt — 1: revert CI file-existence guard on check-triggers.sh step (dead/unsafe skip); script already tracked, precondition belongs only inside the script
- `2026-09-04T02:36:25Z` **P0-T01** done — 29 files, 3 tests, review clean (1 must-fix addressed)
- `2026-09-04T02:36:44Z` **P0-T02** start — Money and Quantity value objects
- `2026-09-04T05:39:43Z` **P0-T02** done — 17 files, 95 tests total, review clean (2 passes, 0 must/should-fix)

**Run ended 2026-09-04T05:39:45Z** — 2 completed, 0 halted

## Run 2026-09-04T06:22:12Z

- branch: `claude/autopilot-qt1g9i`
- head: `28668ca`
- budget: 2 task(s)

- `2026-09-04T06:22:12Z` **P0-T03** start — Database bootstrap, SQLCipher, connection factory
- `2026-09-04T06:39:57Z` **P0-T03** note — task-implementer committed and self-marked done, exceeding delegation scope; orchestrator reverted status to in-progress pending test-engineer + code-reviewer pass and the outstanding Windows publish criterion
- `2026-09-04T06:51:24Z` **P0-T03** fix-attempt — 1: DefaultTimeout not set on connection string, so ADO.NET's 30s command timeout dominates over PRAGMA busy_timeout=5000 (invariant 9)
- `2026-09-04T07:07:19Z` **P0-T03** fix-attempt — 2: 5 must-fix items from code-reviewer + data-modeler: nested-UOW deadlock, Windows key-store silent re-key, PosDbContext second writer bypassing gate, SnakeCaseNaming wrong pipeline phase, applied_at not ISO-8601
- `2026-09-04T11:18:30Z` **P0-T03** note — 2 review rounds clean (round 2: no new must-fix); 138 tests passing; committed a50cb5f
- `2026-09-04T11:33:09Z` **P0-T04** start — Minimal schema, migrations and append-only triggers
- `2026-09-04T11:33:44Z` **P0-T03** done — 18 files, 138 tests, 2 review rounds clean; Windows publish check deferred to post-merge CI
- `2026-09-04T11:40:51Z` **P0-T04** start — Minimal schema, migrations and append-only triggers
- `2026-09-04T12:01:59Z` **P0-T04** note — data-modeler design pass complete: larger than 1d estimate (18 triggers, custom annotation provider for AUTOINCREMENT suppression, MigrationRunner with VACUUM INTO backup, design-time EF factory, 2 doc corrections to trigger pattern). All within task's own stated scope, nothing borrowed from P1.
- `2026-09-04T12:27:47Z` **P0-T04** note — task-implementer marked ledger done on its own again (same overstep as P0-T03 round 1); orchestrator reverted to in-progress
- `2026-09-04T16:06:25Z` **P0-T04** fix-attempt — 1: shift.note trigger guard wrong (blocks Z-report close note), sale_line.qty_returned lacks monotonicity check (undermines AC-06), doc overclaims trigger-check timing. recursive_triggers=ON already fixed independently by two agents.
- `2026-09-04T16:14:26Z` **P0-T04** done — 54 files, 261 tests, 2 review rounds (3 must-fix closed: shift.note guard, recursive_triggers hole, qty_returned monotonicity)

**Run ended 2026-09-04T16:20:49Z** — 2 completed, 0 halted
- `2026-09-04T16:27:30Z` **P0-T04** note — CI failure post-merge-to-branch: scripts/check-triggers.sh hardcoded -c Debug, CI builds Release only. Fixed in e7300c1 (CONFIGURATION env var), verified against both configs locally before push.

## Run 2026-09-04T16:47:08Z

- branch: `claude/autopilot-lqek25`
- head: `05dce53`
- budget: 1 task(s)

- `2026-09-04T16:47:16Z` **P1-T01** start — Full schema migration
- `2026-09-05T00:55:43Z` **P1-T01** fix-attempt — 1: non-atomic product-FK migration (unbootable-till risk), column reorder on rebuild, missing foreign_key_check, missing negative-CHECK tests, misleading comments
- `2026-09-05T10:19:43Z` **P1-T01** done — 132 files, 343 tests (+82), 2 review rounds, 1 fix round; outstanding: NFR-P6 hardware perf gate

**Run ended 2026-09-05T10:20:15Z** — 0 completed, 0 halted, 1 in-progress (P1-T01: 3/4 Done-when proven, NFR-P6 hardware perf gate outstanding)

## Run 2026-09-06T09:59:38Z

- branch: `claude/autopilot-bwlq75`
- head: `236e098`
- budget: 2 task(s)

- `2026-09-06T09:59:47Z` **P1-T01** done — closed out: 4th Done-when (compiled model + UseModel) verified present and tested
- `2026-09-06T09:59:49Z` **P0-T05** start — ESC/POS renderer and one rendered receipt
- `2026-09-06T10:27:58Z` **P0-T05** note — task-implementer done, 374 tests passing, verify.sh green; checked out into checkpoint commit 3749cbf; code-reviewer + device-integrator review in flight
- `2026-09-06T10:28:03Z` **P0-T05** note — task-implementer done, 374 tests passing, verify.sh green; checkpoint commit 3749cbf pushed; code-reviewer + device-integrator review in flight
- `2026-09-06T10:34:00Z` **P0-T05** fix-attempt — 1: rounding applied past the two invariant-2 points in SpecimenReceipt.FormatAmount; FileReceiptPrinter can throw outside its try (Done-when 3); drawer pulse timing and cutter command family (ESC i/m) not capability-flaggable per task's own Risks section; raster barcode mode silently drops the HRI bill-number text; HW-T01 checkbox list doesn't literally reference this task's snapshot
- `2026-09-06T10:46:18Z` **P0-T05** done — 42 device tests (+8 in fix round), 385 total, 2 review rounds clean after 1 fix round

**Run ended 2026-09-06T10:47:07Z** — 2 completed, 0 halted

## Run 2026-09-06T17:28:29Z

- branch: `claude/autopilot-vw0cgr`
- head: `64440f8`
- budget: 2 task(s)

- `2026-09-06T17:28:34Z` **P0-T06** start — One sale, end to end
- `2026-09-06T18:23:45Z` **P0-T06** fix-attempt — 1: rounding-residual quantisation, missing pre-persist assertions + zero-tax test gap, shift_no via COUNT(*)+1, opening stock_balance seeded without a stock_movement
- `2026-09-07T00:58:30Z` **P0-T06** done — 5 files, 288+ tests total (279 integration), 2 review rounds (code-reviewer, data-modeler), 1 fix round closing 4 must-fix items
- `2026-09-07T00:59:07Z` **P1-T02** start — Users, authentication, roles, authorisation
- `2026-09-07T01:57:18Z` **P1-T02** fix-attempt — 1: sale.user_id stamped from the shift's owner rather than the signed-in cashier (live misattribution into an append-only hash-chained table); UserAdministrationService resolvable undecorated, bypassing the AC-17 role guard
- `2026-09-07T02:10:54Z` **P1-T02** fix-round — closed 2 must-fix review findings: CompleteSaleHandler refuses a bill whose command.UserId is not the signed-in user (FR-1.1, FR-1.6); UserAdministrationService made internal and built inside the IUserAdministration factory, guarded by ArchitectureTests.ConcreteOwnerOnlyApplicationServicesAreNotPublic (AC-17). 536 tests green, verify.sh ALL CHECKS PASSED.
- `2026-09-07T02:15:30Z` **P1-T02** done — 75 files, 536 tests, 2 review rounds (code-reviewer, data-modeler), 1 fix round closing 2 must-fix items (sale.user_id misattribution, undecorated-service DI bypass)

**Run ended 2026-09-07T02:16:11Z** — 2 completed, 0 halted

## Run 2026-09-07T10:16:10Z

- branch: `claude/autopilot-t11ufa`
- head: `b57cb33`
- budget: 1 task(s)

- `2026-09-07T10:16:10Z` **P1-T03** start — Settings framework
- `2026-09-07T11:58:21Z` **P1-T03** fix-attempt — 1: backup-passphrase-before-auth-check, wizard-discards-starting-number, specimen-tax-double-count
- `2026-09-07T16:02:13Z` **P1-T03** fix-attempt — 1-result: fixed backup-passphrase-authorization (structural RequiresRole gate + SetInitialPassphrase seam), wizard-starting-number (InitialiseAsync), specimen-tax-double-count (PricesIncludeTax branch); verify.sh ALL CHECKS PASSED
- `2026-09-07T16:02:21Z` **P1-T03** done — 3 review rounds (code-reviewer, data-modeler, device-integrator), 1 fix round closing 2 must-fix authorization/starting-number defects + 1 tax-calculation defect; 604+ tests

**Run ended 2026-09-07T16:03:38Z** — 1 completed, 0 halted
- `2026-09-08T00:51:50Z` **P1-T03** fix-attempt — PR#11 follow-up: closed 3 should-fix items from earlier review (dedupe demo tax class abfbcc0, multi-line hard-coded-value scan 6e937c2, disable no-op starting-number box 051c6ad) + filled Q-16 ledger answer; code-reviewer pass clean, verify.sh green

## Run 2026-09-08T01:43:03Z

- branch: `claude/autopilot-ve7l2i`
- head: `b0e199e`
- budget: 1 task(s)

- `2026-09-08T01:43:21Z` **P0-T07** start — Backup snapshot, encryption, publish
- `2026-09-08T02:36:42Z` **P0-T07** fix-attempt — 1: VACUUM INTO uses write-gated connection instead of read connection, blocking sale completion during a snapshot (CLAUDE.md invariant 7); both code-reviewer and data-modeler confirmed independently
- `2026-09-08T02:51:01Z` **P0-T07** progress — Committed 40b955a, pushed task/p0-t07-backup-snapshot-publish, opened draft PR #13. 4/6 Done-when proven locally (630 tests, verify.sh clean, 2 review rounds + 1 fix-loop verification). Waiting on this PR's own CI run (windows-publish job) to confirm the last 2: Windows self-contained publish w/ SQLCipher asset, and installer artifact build.
- `2026-09-08T02:59:14Z` **P0-T07** done — 30 files, 630 tests (9 new + 2 concurrency regression), 2 review rounds (code-reviewer + data-modeler) + 1 fix-loop verification round closing 1 must-fix (VACUUM INTO write-gate contention); PR #13 CI green including windows-publish (SQLCipher asset + installer artifact confirmed)

**Run ended 2026-09-08T03:00:30Z** — 1 completed, 0 halted

## Run 2026-09-08T04:47:17Z

- branch: `claude/autopilot-3d7m8e`
- head: `3d68e9b`
- budget: 2 task(s)

- `2026-09-08T04:47:20Z` **P1-T04** start — Catalogue: categories, brands, UOM, tax classes, suppliers
- `2026-09-08T05:24:00Z` **P1-T04** note — orchestrator decision: uom lacks active column (P1-T01 gap); adding it now via migration is within P1-T04's own done-when criteria, not scope creep into P1-T05
- `2026-09-08T06:49:13Z` **P1-T04** done — 6 files/89 total files, 417 tests total (34+ new), 2 review rounds (code-reviewer, data-modeler) clean, 0 fix loops needed
- `2026-09-08T06:50:43Z` **P1-T05** start — Product, variant and UOM conversion domain
- `2026-09-08T07:52:18Z` **P1-T05** fix-attempt — 1: unhandled ArgumentOutOfRangeException on blank/zero UOM conversion-factor input in ProductTabViewModel (UI-06 violation)
- `2026-09-08T08:03:05Z` **P1-T05** done — 56 files, 437 tests total (~50 new incl. two 10k-sample property tests), 2 review rounds (code-reviewer, data-modeler) + 1 fix round (1 must-fix closed)
- `2026-09-08T08:03:49Z` **P1-T05** done — PR #15 opened against main (stacked on #14): https://github.com/hansajayathilaka/counterpoint/pull/15

**Run ended 2026-09-08T08:03:59Z** — 2 completed, 0 halted

## Run 2026-09-08T13:21:41Z

- branch: `claude/autopilot-na6wpm`
- head: `0254bc7`
- budget: 2 task(s)

- `2026-09-08T13:21:51Z` **P1-T06** start — Barcodes and product search
- `2026-09-08T14:19:26Z` **P1-T06** done — 35 files, 33+2 tests, 2 review rounds clean (0 must-fix)
- `2026-09-08T14:20:24Z` **P1-T07** start — Stock ledger and balance projection
- `2026-09-08T14:51:11Z` **P1-T07** note — corrected premature self-mark by task-implementer; resuming normal B4-B8 loop (test-engineer, verify, review) before re-judging done
- `2026-09-08T15:08:59Z` **P1-T07** fix-attempt — 1: SqliteStockConsistencyCheck.CheckAsync reports mismatch quantities as raw scaled longs instead of descaled Quantity values (code-reviewer must-fix)
- `2026-09-08T15:17:39Z` **P1-T07** done — 26 files, 40+ tests, 1 fix-attempt closing a real scaling bug, review clean after re-check

**Run ended 2026-09-08T15:18:38Z** — 2 completed, 0 halted
- `2026-09-08T18:28:35Z` **P1-T07** note — CI fix after main merge: PR #17's Build-and-test job failed because P1-T06 (merged to main while this PR was open) introduced FR-2.24 duplicate-name detection that legitimately fired on RebuildStockBalanceCommandTests' generated near-identical product names; fixed by setting ConfirmDuplicate:true in the test fixture (commit 53895ea)

## Run 2026-09-08T18:45:08Z

- branch: `claude/pensive-hypatia-ybtaco`
- head: `482a677`
- budget: 2 task(s)

- `2026-09-08T18:45:18Z` **P1-T08** start — Pricing and discount engine
- `2026-09-08T18:46:06Z` **P1-T08** start — task-implementer delegated
- `2026-09-08T19:40:10Z` **P1-T08** fix-attempt — 1: ProductRecord.CostAvg leaked cost through undecorated IProductStore/IStockEnquiry paths (CLAUDE.md invariant 8); moving to a narrow owner-gated cost read
- `2026-09-08T19:48:54Z` **P1-T08** done — 39+10+4 files, 814 tests, 2 review rounds (code-reviewer+data-modeler), 1 must-fix closed (cost leak via ProductRecord), PR #18
- `2026-09-08T19:49:19Z` **P1-T12** start — Label printing
- `2026-09-08T20:30:36Z` **P1-T12** done — 34+3 files, 819 tests, 2 review rounds (code-reviewer+device-integrator), 0 must-fix, PR #19

**Run ended 2026-09-08T20:30:47Z** — 2 completed, 0 halted

## Run 2026-09-09T02:21:42Z

- branch: `claude/awesome-brahmagupta-xno8kf`
- head: `f78943d`
- budget: 1 task(s)

- `2026-09-09T02:21:46Z` **P1-T09** start — Sales screen and bill building
- `2026-09-09T03:27:27Z` **P1-T09** note — implementer committed and self-marked done outside the loop; reopening to run test-engineer + review before accepting
- `2026-09-09T03:35:39Z` **P1-T09** fix-attempt — 1: code-reviewer found Block negative-stock policy bypassed by two same-variant lines in one bill (stale on-hand snapshot per line, no running balance across the bill) - CompleteSaleHandler.PriceAsync/PriceCatalogueLineAsync
- `2026-09-09T04:07:07Z` **P1-T09** done — 3 files in fix + 42 files in feat, 832 tests, 2 review rounds (code-reviewer x2, data-modeler), 1 fix round

**Run ended 2026-09-09T04:08:07Z** — 1 completed, 0 halted

## Run 2026-09-09T06:41:58Z

- branch: `claude/autopilot-3gidzm`
- head: `f7e1802`
- budget: 2 task(s)

- `2026-09-09T06:42:02Z` **P1-T10** start — Tender, change and sale completion
- `2026-09-09T07:39:42Z` **P1-T10** done — 23 tests (855 total), review clean (0 must-fix), PR #21
- `2026-09-09T07:40:39Z` **P1-T13** start — Spreadsheet import
- `2026-09-09T12:11:49Z` **P1-T13** fix-attempt — 1: blank-cost cost_avg corruption on re-import (must-fix), stale docs/01_DATA_MODEL.md JSON-unused claim (must-fix)
- `2026-09-09T12:25:27Z` **P1-T13** done — 30 tests (512 total), 1 fix-attempt closed 2 must-fix (cost_avg corruption, stale doc), review clean, PR #22

**Run ended 2026-09-09T12:26:25Z** — 2 completed, 0 halted

## Run 2026-09-09T19:19:11Z

- branch: `claude/autopilot-i75n9e`
- head: `b8d0c7c`
- budget: 2 task(s)

- `2026-09-09T19:19:13Z` **P1-T11** start — Receipt templates and printing
- `2026-09-09T20:16:51Z` **P1-T11** fix-attempt — 1: uncaught exception from owner-template directive/IR errors can roll back the sale transaction
- `2026-09-09T20:27:50Z` **P1-T11** done — 64 files, 532+292+62+12 tests, 1 must-fix closed, 2 should-fix carried to PR #23
- `2026-09-09T20:29:16Z` **P1-T14** start — Shift open (minimal) and dashboard
- `2026-09-10T00:56:12Z` **P1-T14** fix-attempt — 1: FR-8.7's warn-on-close half was never implemented, only recovery-on-restart
- `2026-09-10T01:05:36Z` **P1-T14** done — 33 files, 539 tests, 1 should-fix closed (FR-8.7 warn-on-close), 2 consider items carried to PR #24

**Run ended 2026-09-10T01:05:52Z** — 2 completed, 0 halted

## Run 2026-09-10T05:21:44Z

- branch: `claude/elegant-goldberg-j128w7`
- head: `dcdf0d9`
- budget: 1 task(s)

- `2026-09-10T05:21:46Z` **P1-T15** start — Local and USB backup
- `2026-09-10T06:23:37Z` **P1-T15** done — 48 files, 6 new test files (940 tests total), 2 review rounds (code-reviewer, data-modeler) clean of must-fix; 2 should-fix carried to PR (restore schema-version guard, WarnAfterDays round-trip fixture gap); retention pruning deferred to P4-T04 per its own dedicated spec

**Run ended 2026-09-10T06:24:20Z** — 1 completed, 0 halted

## Run 2026-09-10T13:39:32Z

- branch: `claude/autopilot-svbklg`
- head: `9e8e68c`
- budget: 2 task(s)

- `2026-09-10T13:39:36Z` **P1-T16** start — Phase 1 acceptance and software performance harness
- `2026-09-10T14:35:04Z` **P1-T16** note — implementer overstepped: committed work and hand-edited ledger to done directly, skipping test-engineer/verify/review gates; orchestrator reset to in-progress to run those gates independently
- `2026-09-10T14:48:14Z` **P1-T16** fix-attempt — 1: seed generator's stock_balance has no backing OPENING stock_movement row (invariant 3 - projection not rebuildable from ledger); balance_after hardcoded 0 on all seeded movements
- `2026-09-10T15:14:03Z` **P1-T16** fix-attempt — 1: closed - added OPENING stock_movement per SKU + real running balance_after in seed generator; verified 0 mismatches at 3000-SKU/20000-line scale
- `2026-09-10T15:20:39Z` **P1-T16** done — 20 files, 575 tests (+6 architecture, +54 trigger-survival), 2 review rounds (code-reviewer, data-modeler), 1 fix round closing a stock-ledger reconciliation must-fix, PR #26
- `2026-09-10T15:21:15Z` **P2-T01** start — Return policy engine
- `2026-09-10T15:54:22Z` **P2-T01** done — 20 files, 32 tests (24 domain + 8 integration), review clean, PR #27

**Run ended 2026-09-10T15:55:03Z** — 2 completed, 0 halted

## Run 2026-09-12T04:56:19Z

- branch: `main`
- head: `51fa3cf`
- budget: 1 task(s)

- `2026-09-12T04:56:21Z` **P2-T06** start — Suppliers and purchase orders
- `2026-09-12T05:59:02Z` **P2-T06** review — code-reviewer clean (0 must-fix, 0 should-fix, 1 consider); data-modeler 0 must-fix, 1 should-fix (FindReceiptProgressAsync inner join could silently drop a line with a removed UOM option, mis-deriving status), 2 consider
- `2026-09-12T06:00:00Z` **P2-T06** done — 44 files, 24 new tests, review clean

**Run ended 2026-09-12T06:00:56Z** — 1 completed (P2-T06, PR #28); P2-T01 already merged as PR #27 by an earlier session before this continuation

## Run 2026-09-12T10:53:30Z

- branch: `claude/autopilot-8hb4e2`
- head: `157cfa4`
- budget: 2 task(s)

- `2026-09-12T10:54:04Z` **P2-T02** start — Linked returns
- `2026-09-12T13:21:34Z` **P2-T02** done — 38 files, 615 tests (606 integration + 75 device incl. new), review clean (no Must-fix), PR #29 opened
