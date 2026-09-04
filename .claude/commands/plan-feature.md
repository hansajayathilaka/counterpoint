---
description: Turn a feature request into properly specified tasks in the plan
argument-hint: "<what the shop wants, in plain words>"
allowed-tools: Read, Write, Edit, Grep, Glob, Task, WebSearch
model: opus
---

Plan a new feature: **$ARGUMENTS**

Delegate to the **feature-planner** agent.

Before it writes anything, it must check, and report on, all four of these:

1. **Is it out of scope?** `docs/Counterpoint_Requirements.md` section 5 excludes multi-terminal, e-commerce, accounting integration, loyalty, job costing, live cloud dashboards and mobile apps. If the request is one of these, say so plainly with the consequence, and do not quietly design around it.
2. **Does it conflict with an architectural decision?** If it needs a server, a network call on the sale path, or a second terminal, it breaks constraints C-01/C-02 and invalidates the architecture. Name the ADR and the cost of revisiting it.
3. **Does it touch an append-only table or the stock ledger?** Those changes need the **data-modeler** agent involved before any task is written.
4. **What does it cost?** Developer-days, what it delays, and what it adds to the maintenance burden after handover. The shop owner is paying for this; give them a real number.

If it clears those, write the tasks into the right phase document, add rows to `docs/08_TASK_INDEX.md` and `.claude/state/PROGRESS.md`, and update `docs/01_DATA_MODEL.md` if the schema changes.

Then stop and show the human the plan for approval. **Do not start implementing.**
