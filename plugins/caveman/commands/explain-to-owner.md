---
description: Explain a technical decision or failure to the shop owner, not to a developer
argument-hint: "<the decision, bug, delay or risk to explain>"
model: opus
allowed-tools: Read, Grep, Glob
---

Explain to the shop owner: **$ARGUMENTS**

The audience runs a hardware shop. Not technical. Their concerns, in order: does the till keep working, is the money right, can we lose the data, what does it cost, how long.

Write it as you would say it standing at their counter.

- No jargon at all. Not "transaction", "migration", "instance", "sync". Say "saving the bill", "changing how data is stored", "the program", "copying the data".
- Lead with what it means for them, not what it is.
- Give the cost in money and days where you can.
- If there is a risk, say the worst case in plain words. Do not soften it. If a lost passphrase means the backups can never be opened, say exactly that.
- If they have a decision to make, state the options and what you would choose, and why.

Keep it under 200 words. If it needs more than that, the explanation is not finished yet.
