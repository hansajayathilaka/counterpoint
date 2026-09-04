---
description: Plan and implement a small feature end to end
argument-hint: "<what the shop wants>"
allowed-tools: Read, Write, Edit, Grep, Glob, Bash, Task, TodoWrite
model: opus
---

Add a feature: **$ARGUMENTS**

This is for a feature small enough to be one or two tasks. If it is bigger than that, stop and use `/plan-feature` instead — say so rather than trying to do it all here.

## 1. Scope check

Same four checks as `/plan-feature`: out of scope, architectural conflict, ledger/append-only impact, cost. Report them before doing anything else. If any is a problem, stop and tell the human.

## 2. Specify

Delegate to **feature-planner** to write the task in the standard format, including "Done when" criteria. Show it to the human and get agreement on the acceptance criteria **before writing code**. Vague acceptance criteria are how features get built twice.

## 3. Data model first

If the schema changes, delegate to **data-modeler**: migration, trigger recreation where a table rebuild is involved, migration test, and the `docs/01_DATA_MODEL.md` update including the ER diagram.

## 4. Implement

Delegate to **task-implementer**. Then **code-reviewer** on the diff, then **test-engineer** for the tests, including a test named for each new requirement id.

## 5. Close

`bash scripts/verify.sh`, update `.claude/state/PROGRESS.md`, propose a commit message, and note anything that now needs a documentation change so it can be picked up in `/handoff`.
