---
description: Prepare the handover package and check documentation is current
argument-hint: "[phase, or 'final' for go-live]"
allowed-tools: Read, Write, Edit, Grep, Glob, Bash, Task
model: opus
---

Prepare handover. Scope: **$ARGUMENTS**

## 1. Documentation currency

Delegate to **docs-writer**. For every task completed since the last handoff, check whether it made an existing document wrong. Features the manuals do not mention are the usual gap; features the manuals describe but that were cut are worse.

## 2. The package (NFR-M5, AC-20)

- User manual, admin manual, keyboard cheat sheet
- Technical documentation: schema ERD, build and publish instructions, portal deployment, dependency list **with licences**, ADR index, printer and scale quirks
- Runbook: printer fails, scanner fails, disk fills, backups failing for days, database will not open, machine dies
- Source repository, installer, portal infrastructure code

## 3. For `final` only

- Full AC-01…AC-20 run **on the shop's actual hardware**, each result recorded. A dev-machine result does not count.
- Backup chain proven end to end: local, USB, cloud, portal listing, and a real restore of live data **before the first real bill**.
- Signed passphrase custody acknowledgement (Q-E). Confirm an off-premises written copy exists.
- Go-live checklist: opening float, printer and scanner working, first bill checked, cheat sheet and runbook physically at the counter, support contact posted.

## 4. Report the gaps honestly

List what is missing, what is stale, and what was verified on a developer machine but not on the shop's hardware. Do not present a partial handover as complete — the owner is relying on this to not be dependent on the vendor.
