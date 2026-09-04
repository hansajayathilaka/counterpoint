---
description: Full pre-commit verification — build, tests, architecture, invariants
allowed-tools: Read, Grep, Glob, Bash, Task
model: sonnet
---

Run full verification. This is what "done" means.

1. `bash scripts/verify.sh` — restore, build with warnings as errors, format check, full test suite.
2. Architecture tests specifically: project boundaries hold, no `double`/`float` in Domain/Application/Infrastructure.
3. Trigger-survival check: apply the full migration chain to a scratch database and confirm every append-only table still has both triggers.
4. Confirm the app still starts (`dotnet run --project src/Counterpoint.Ui` headless smoke, or the startup integration test).
5. `git status` — report anything uncommitted or untracked that looks like it should be in the commit.

Report each step as pass or fail with the actual output on failure. **Do not summarise a failure as a pass**, and do not describe a skipped test as a passing one.

If everything passes, say so in one line and propose the commit message in Conventional Commits form, scoped with the task id: `feat(P1-T07): UOM conversion in base units (FR-2.4, FR-2.5)`. This is enforced by commitlint locally and in CI, so get the type right the first time.
