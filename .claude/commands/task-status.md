---
description: Show where the build stands — completed, in progress, blocked, next up
allowed-tools: Read, Grep, Glob, Bash(git log:*), Bash(git status:*), Bash(dotnet test:*)
model: sonnet
---

Report the current state of the build. Be brief and factual.

Read `.claude/state/PROGRESS.md` and `docs/08_TASK_INDEX.md`, then report:

1. **Phase progress** — one line per phase: `Phase 1: 11/16 done`.
2. **In progress** — which task, and what remains on its "Done when" list.
3. **Next up** — the next 3 tasks whose dependencies are satisfied.
4. **Blocked** — any task whose dependencies are not met, and what is blocking it.
5. **Open questions** — unanswered items from `docs/README.md` and which upcoming task each one blocks. Flag anything blocking work in the next two weeks.
6. **Build health** — last commit, working tree clean or not, and the current test status if a solution exists.
7. **Acceptance coverage** — from the map in `docs/08_TASK_INDEX.md`, which of AC-01…AC-20 have passing automated tests and which do not yet.

End with a single sentence on the most useful thing to do next. No filler, no encouragement.
