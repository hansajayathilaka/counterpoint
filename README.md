# Counterpoint

Offline-first Point of Sale and inventory system for a single-cashier shop.

**Stack:** .NET 10 LTS · C# · Avalonia UI · SQLite + SQLCipher · EF Core + Dapper
**Target:** Windows desktop, low-powered terminal (dual-core, 4 GB)
**Development:** Linux dev container, including Claude Code on the web

---

## Start here

```bash
# 1. Open in the dev container (VS Code: "Reopen in Container") or in Claude Code web.
#    The container installs .NET 10, sqlite3, fonts and dotnet-ef automatically.

# 2. Create the solution and projects (this is task P0-T01)
bash scripts/bootstrap-solution.sh

# 3. See where things stand
/task-status

# 4. Build the next planned task
/next-task
```

There is **no solution file in the repository yet, on purpose.** `scripts/bootstrap-solution.sh`
creates it with the correct project boundaries and references. That way the first thing that
happens is a verified `dotnet` run rather than a hand-written `.csproj` that may not restore.

If restore fails, adjust the pinned versions in `Directory.Packages.props` — they are a starting
point, not gospel, and package versions move.

---

## Repository layout

```
CLAUDE.md                  Standing context for Claude Code. Read every session.
docs/                      SRS, architecture, data model, 62 planned tasks
.claude/
  agents/                  8 subagents
  commands/                12 slash commands
  skills/                  4 project skills
  hooks/                   invariant guards
  output-styles/           caveman
  state/PROGRESS.md        the build ledger - source of truth for what is done
.claude-plugin/            local plugin marketplace
plugins/                   caveman, pos-guardrails
.devcontainer/             .NET 10 + sqlite3 + fonts, VS Code extensions
scripts/                   bootstrap, verify, seed, trigger check
src/ tests/ tools/         created by bootstrap-solution.sh
installer/ portal/         Inno Setup script, backup portal
```

---

## Commands

| Command | What it does |
|---|---|
| `/autopilot [n]` | **Unattended run.** Picks the next n ready tasks and implements, tests, reviews and commits each one. Orchestrator delegates everything; it has no write tools of its own. |
| `/task-status` | Where the build stands: done, in progress, blocked, next up, AC coverage |
| `/next-task [id]` | Implement the next planned task, review it, verify it, update the ledger |
| `/plan-feature <x>` | Turn a request into properly specified tasks. Plans only, does not build. |
| `/add-feature <x>` | Plan and implement a small feature end to end |
| `/fix-bug <x>` | Reproduce, classify, isolate, fix, prove, close the hole |
| `/test-tasks [phase]` | Sweep every completed task and re-verify its acceptance criteria |
| `/review [scope]` | Run the review agents over a diff, a phase, or everything |
| `/acceptance <AC-n>` | Write or run the test for one acceptance criterion |
| `/migration <x>` | Create a schema migration with triggers and tests intact |
| `/perf-gate` | Run the NFR-P budgets against the seeded database |
| `/verify` | Full pre-commit verification |
| `/handoff [final]` | Prepare the handover package, check docs are current |
| `/caveman <x>` | Explain something in small words (plugin) |
| `/explain-to-owner <x>` | Explain a decision or failure to the shop owner (plugin) |

## Agents

`task-implementer` · `code-reviewer` · `invariant-auditor` · `test-engineer` ·
`data-modeler` · `device-integrator` · `feature-planner` · `bug-hunter` · `docs-writer`

Most commands delegate to these. You can also invoke one directly: *"use the invariant-auditor
to check the stock ledger."*

## Plugins

Installed from the local marketplace:

```
/plugin marketplace add .
/plugin install caveman@counterpoint-marketplace
/plugin install pos-guardrails@counterpoint-marketplace
```

The caveman **output style** is separate from the plugin and lives in
`.claude/output-styles/caveman.md`. Switch to it with `/output-style caveman`.

---

## Unattended runs

```bash
/autopilot            # next 2 ready tasks
/autopilot 4          # next 4
/autopilot 3 --phase P1
/autopilot --dry-run  # show the plan, do nothing
```

Per task it runs: schema (if needed) → implement → tests → `scripts/verify.sh` → review →
bounded fix loop → prove every "Done when" criterion → commit → update the ledger. Then the
next task. It never pushes.

Three properties worth knowing:

- **The orchestrator cannot write code.** `/autopilot` declares no `Edit`, `Write` or
  `MultiEdit` tool, so every artifact comes from a subagent through `Task`. The main agent
  sequences, judges the gates and decides whether to continue.
- **State changes are deterministic.** The ledger is only ever updated through
  `scripts/autopilot.sh mark`, which validates the status, enforces one-task-at-a-time and
  refuses unknown task ids. An agent cannot mark a task done by believing it is done.
- **It stops rather than finishing dirty.** Ten explicit halt conditions, including three
  failed fix attempts on one task, a regression in a previously passing test, an invariant that
  would have to be weakened, and a schema change that drops an append-only trigger.

Tasks marked `mode=human` in `.claude/state/deps.tsv` — physical printing, the installer on a
clean machine, cloud provisioning, the owner's restore drill, training, go-live — are skipped
with a reason rather than half-done. A task with outstanding hardware verification stays
`in-progress`, not `done`.

Run history is appended to `.claude/state/autopilot-log.md`.

---

## The ten invariants

These are in `CLAUDE.md` and enforced by architecture tests, database triggers and hooks.
Violating one is a build-breaking bug, not a style issue.

1. Money and quantity are `INTEGER` scaled ×10 000; no `double` or `float` in the core projects
2. Rounding at exactly two points: line total and bill total
3. Every stock change goes through `StockLedger.PostAsync`; `stock_balance` is a rebuildable projection
4. Document numbers come from `number_sequence` inside the business transaction
5. Append-only tables are trigger-protected, with three column-scoped exceptions
6. `sale` and `audit_log` are hash-chained
7. Never block the sale — printing goes through the `print_job` outbox
8. Authorisation lives in the Application layer, not the UI
9. `synchronous=FULL` and `foreign_keys=ON` on every connection
10. `sale_line` snapshots price, cost and description

---

## Development on Linux, shipping on Windows

Avalonia, EF Core, SQLite/SQLCipher, QuestPDF and the entire domain build and test on Linux,
so CI and Claude Code web are fully useful.

Windows-only surfaces — raw spooler printing, DPAPI, Credential Manager, serial ports — sit
behind interfaces with Linux development implementations (`FileReceiptPrinter` writes the
ESC/POS byte stream to `artifacts/receipts/`, `NullScale`, `FileKeyStore`). Device snapshot
tests compare byte streams and pass on Linux.

**Hardware verification is a separate, manual step.** A printing task is not done because its
snapshot test passes — it is done when a receipt physically prints, cuts, and opens the drawer
on the shop's actual printer.

---

## Before you start Phase 0

Two things block work and neither is a code problem:

- **Buy the actual printer and scanner (Q-H).** Phase 0 exists to retire the ESC/POS risk in
  week one. A skeleton that prints to a simulator proves nothing.
- **Decide the terminal hardware (Q-B).** The §14.1 minimum spec describes machines that
  mostly cannot run Windows 11, and Windows 10 left support in October 2025. The three options
  and their costs are in `docs/POS_Architecture_Design.md` ADR-001b.

The full list of open questions is in `.claude/state/PROGRESS.md`.
