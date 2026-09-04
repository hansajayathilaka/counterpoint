---
description: Implement the next planned task from the phase documents
argument-hint: "[optional task id, e.g. P1-T07]"
allowed-tools: Read, Grep, Glob, Bash(bash scripts/*), Bash(git status:*), Bash(git log:*), Task, TodoWrite
model: opus
---

Implement the next planned task.

Task requested: **$ARGUMENTS** (if empty, pick the next one yourself)

## Step 1 — work out which task

Read `.claude/state/PROGRESS.md`.

- If a task id was given, use it.
- If not, the next task is the first `todo` whose dependencies are all `done`.
- If a task is already `in-progress`, resume that one instead and say so. Do not start a second.
- If the next task's dependencies are not all done, stop and say which dependency is blocking.

## Step 2 — confirm before building

Show the human, briefly:
- task id, title, estimate, dependencies
- what it will produce
- anything in its **Risks** section that they should know now
- any open question from `docs/README.md` that blocks it (Q-A … Q-H, Q-01 … Q-16)

If a blocking open question is unanswered, ask it now rather than assuming a default silently.

## Step 3 — implement

Mark the task `in-progress` in `.claude/state/PROGRESS.md`, then delegate to the **task-implementer** agent with the task id.

Then, before reporting back, hand the diff to the **code-reviewer** agent. Fix anything in its "Must fix" list. Report "Should fix" and "Consider" items to the human rather than silently acting on them.

## Step 4 — close out

- Run `bash scripts/verify.sh`.
- Walk each "Done when" checkbox and state what proves it.
- Update `.claude/state/PROGRESS.md` to `done` with the date.
- Propose a commit message in Conventional Commits form, scoped with the task id: `feat(P1-T07): UOM conversion in base units (FR-2.4, FR-2.5)`. Pick the type (`feat`, `fix`, `refactor`, `test`, `docs`, ...) honestly — it drives the automated release version.

Stop there. Do not roll straight into the following task.
