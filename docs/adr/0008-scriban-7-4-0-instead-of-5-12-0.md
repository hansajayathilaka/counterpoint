# ADR-0008: Scriban 7.4.0 instead of 5.12.0

**Status:** Accepted
**Date:** 2026-09-09
**Deciders:** P1-T11

## Context

P1-T11 needed a template engine for the owner-editable receipt layout (SRS FR-7.3, NFR-M1).
`Directory.Packages.props` already pinned `Scriban` at 5.12.0 in anticipation of the task.

Restoring `Counterpoint.Devices` with that pin fails: NuGet's vulnerability audit reports
fourteen distinct advisories against Scriban 5.12.0 (a mix of moderate, high and one critical -
`GHSA-5wr9-m6jw-xx44`), and this repository treats an audit warning as a build error
(`TreatWarningsAsErrors`, `Directory.Build.props`), the same policy that produced ADR-0003.

## Options considered

**Suppress the advisories (`NoWarn` or `<NuGetAuditSuppress>`).** Rejected outright: a shop
owner's receipt-layout edit runs through this library on every sale; silencing a critical
advisory rather than moving off the affected version is exactly the kind of invisible risk
ADR-0003 already rejected for a different package.

**Patch within the 5.x line (5.12.1).** Tried first, as the smallest possible change. Restore
still reports all fourteen advisories - the line was never patched for them.

**Move to the last 6.x release (6.6.0).** Tried next. Still twelve of the fourteen advisories
apply; 6.x did not clear them either.

**Move to the latest stable (7.4.0).** Restores clean: zero audit warnings. `Template.Parse`,
`ScriptObject.Import`, `TemplateContext` and `StandardMemberRenamer.Rename` - the only Scriban
surface `Counterpoint.Devices.Printing.Templates.ScribanReceiptTemplateEngine` uses - are
unchanged across the 5 → 7 line for this basic parse-and-render usage.

## Decision

`Directory.Packages.props` pins `Scriban` at 7.4.0. `Counterpoint.Devices` is the only project
that references it.

## Consequences

The receipt template engine restores and builds with no suppressed advisory. A future bump still
follows the one-package-per-PR rule this file's header states; this ADR is the note that rule
asks for.
