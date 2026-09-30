# ADR-0010: `Counterpoint.Ui` depends on `Microsoft.Extensions.Logging.Abstractions`

**Status:** Accepted
**Date:** 2026-09-30
**Deciders:** P3-T06

## Context

A report screen reads the database. When that read fails (a locked or unreadable file, an I/O error) the
owner must see a plain-language sentence, not a stack trace or a driver message (SRS UI-06), and the
failure still has to be recorded where support can find it (engineering guide section 7: the log gets the
detail, the user gets the next step). `Counterpoint.Ui` had no way to write to the log: it references
Application and Domain only (CLAUDE.md "Project boundaries") and had no logging package.

## Options considered

**Swallow the failure and show a sentence.** Nothing is recorded; a recurring fault is invisible.
Rejected.

**Add an `IReportFailureLog` port to Application and implement it in the composition root.** One more
abstraction whose only job is to forward to `ILogger`, reinventing levels, event ids and structured
properties. Rejected, for the reason ADR-0005 rejected a callback.

**Reference `Microsoft.Extensions.Logging.Abstractions`.** The `ILogger` contract only - no sink, no
configuration - exactly what `Counterpoint.Devices` and `Counterpoint.Backup` already take (ADR-0005).
Serilog is plugged in behind it in the composition root. View models take `ILogger<T>? logger = null`
so a test that builds one directly needs nothing.

## Decision

`Counterpoint.Ui` references `Microsoft.Extensions.Logging.Abstractions` (already pinned centrally,
MIT, part of the .NET platform's own package set - NFR-M5 is comfortable with it). Call sites use the
`[LoggerMessage]` source generator. The reference is to the abstractions only: `Counterpoint.Ui` still
references no adapter project.

## Consequences

Easier: a failed report read is shown as a plain sentence and logged with its exception.

Harder: nothing material. The architecture test on project references is unaffected; a package
reference is not a project reference.
