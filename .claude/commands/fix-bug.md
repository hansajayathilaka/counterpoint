---
description: Reproduce, diagnose and fix a defect, with a regression test
argument-hint: "<what went wrong — symptom, steps, or an error message>"
allowed-tools: Read, Write, Edit, Grep, Glob, Bash, Task, TodoWrite
model: opus
---

Fix a bug: **$ARGUMENTS**

Delegate to the **bug-hunter** agent, which works in this order and does not skip ahead:

1. **Reproduce as a failing test first.** No reproduction means no understanding, and any fix is a guess.
2. **Classify** — money, stock, data integrity, availability, or cosmetic. The first three change the urgency and require a blast-radius assessment: which bills or which stock records are affected, over what period, and whether the shop has been losing money.
3. **Isolate** the actual cause, not the nearest place a change hides the symptom. Distinguish "this line is wrong" from "this design permits this to go wrong."
4. **Fix and prove** — the failing test passes, the whole suite passes, and for money or stock defects the reconciliation and conservation tests pass specifically.
5. **Close the hole.** If the design permitted the bug, add the trigger, architecture test or hook rule that prevents a recurrence, and say what you added.
6. **Report** — what was wrong, why, what changed, what proves it, and where else the same class of bug might live.

If existing data was corrupted, propose a correction script **separately** for the human to review. Never silently mutate history — corrections post forward, per FR-8.8.

Then run **code-reviewer** on the fix.
