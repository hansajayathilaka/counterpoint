---
description: Write or run the automated test for a specific acceptance criterion
argument-hint: "<AC id, e.g. AC-12 — or 'all'>"
allowed-tools: Read, Write, Edit, Grep, Glob, Bash, Task
model: opus
---

Acceptance criterion: **$ARGUMENTS**

1. Look it up in `docs/Counterpoint_Requirements.md` section 16 and find its owning task in the coverage map in `docs/08_TASK_INDEX.md`.
2. If a test exists (`tests/Counterpoint.Acceptance.Tests/AC*`), run it and report.
3. If not, delegate to **test-engineer** to write it, named in the house style: `AC03_PartialReturnRefundsAtOriginalPrice`.
4. If the criterion cannot be fully automated (hardware, a human following a manual), automate the part that can be and state precisely what remains manual and who must do it.

For the generative ones — AC-12 reconciliation, AC-15 power loss, AC-19 gapless numbering — a single example is not a test. AC-12 needs 500 simulated trading days; AC-15 needs 100 process kills at random points in the transaction; AC-19 needs 500 consecutive bills including cancellations. Anything less will pass and prove nothing.

Report pass or fail with the evidence, and update the coverage map if a criterion moved from unproven to proven.
