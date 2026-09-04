---
description: Run the review agents over the current changes or the whole codebase
argument-hint: "[diff | staged | phase P1 | all]"
allowed-tools: Read, Grep, Glob, Bash(git diff:*), Bash(git log:*), Bash(git status:*), Bash(dotnet build:*), Bash(dotnet test:*), Task
model: opus
---

Run review. Scope: **$ARGUMENTS** (default: uncommitted changes).

## Choose the reviewers by scope

| Scope | Agents to run |
|---|---|
| `diff` / `staged` / empty | **code-reviewer** |
| `phase P1` (or any phase) | **code-reviewer** on the phase's changes, then **invariant-auditor** |
| `all` | **invariant-auditor**, then **code-reviewer** on the highest-risk areas it flags |

Additionally, run in parallel where they apply to the changed files:
- **data-modeler** if migrations, entities or queries changed
- **device-integrator** if anything under `src/Counterpoint.Devices` changed
- **test-engineer** if the change adds a business rule but no test named for its SRS id

## Consolidate

Merge the findings into one list, deduplicated, grouped as **Must fix**, **Should fix**, **Consider**. Within "Must fix", order by consequence to the shop — money and stock correctness first, then data integrity, then availability, then everything else.

For each finding: file and line, what is wrong, why it matters, the concrete change, and the invariant or SRS id it violates.

## Be honest about clean code

If the change is clean, say so in a sentence and stop. Do not manufacture findings to look thorough. Equally, do not wave through a money, transaction or ledger issue because the surrounding code is good — those are the three categories that cost the shop real money.

Do not fix anything in this command. Report only.
