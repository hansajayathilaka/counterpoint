---
description: Sweep every completed task and verify its acceptance criteria still hold
argument-hint: "[phase, e.g. P1 — omit for all phases]"
allowed-tools: Read, Write, Edit, Grep, Glob, Bash, Task, TodoWrite
model: opus
---

Verify the acceptance criteria of completed tasks. Scope: **$ARGUMENTS** (empty means every completed task).

This is the regression sweep. It catches the case where task N quietly broke task N−12.

## 1. Inventory

Read `.claude/state/PROGRESS.md`. For every task marked `done` in scope, pull its "Done when" list from the phase document. Build a checklist with TodoWrite.

## 2. Classify each criterion

- **Automated** — a test exists. Run it.
- **Automatable but missing** — should have a test and does not. This is the important category: write the test now, via **test-engineer**.
- **Manual only** — hardware verification (a receipt physically printing, the drawer opening, the scale reading). Cannot be automated. List these separately for the human.

## 3. Run

`bash scripts/verify.sh` for the full suite, then the acceptance and performance suites specifically. A **skipped test counts as a failure** unless it is skipped for a documented hardware-only reason.

## 4. Invariant sweep

Delegate to **invariant-auditor**. Drift accumulates across tasks and no single diff review catches it.

## 5. Report

A table: task id, criteria total, automated passing, newly automated, manual outstanding, failures.

Then, in order of consequence:
- **Regressions** — was passing, now failing. Name the task that most likely caused it.
- **Never actually proven** — marked done without a test backing the criterion.
- **Manual verification outstanding** — for the human to do on real hardware.
- **Invariant drift** — from the auditor.

Do not fix failures inside this command unless they are trivially a broken test. Report them and let the human decide the order — a regression in the money path and a formatting change are not the same priority, and batching them hides that.
