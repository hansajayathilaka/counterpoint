# Architecture Decision Records

One file per decision: `NNNN-short-title.md`.

The two foundational decisions — the stack (ADR-001) and the database (ADR-002) — already live
in `docs/POS_Architecture_Design.md`. Record new ones here using the same format:

```markdown
# ADR-NNNN: Title

**Status:** Proposed | Accepted | Superseded by ADR-MMMM
**Date:** YYYY-MM-DD
**Deciders:**

## Context
What forced a decision. The constraints that actually narrow the field, not background.

## Options considered
Each with honest pros and cons. An option listed only to be dismissed is not a real option -
either argue it properly or leave it out.

## Decision
What was chosen, and the specific reason it beat the runner-up.

## Consequences
What becomes easier. What becomes harder. What would make us revisit this.
```

Also record here:
- `printer-quirks.md` — where the actual printer deviates from ESC/POS, and how we handle it
- any new dependency, with why it was needed and its licence
