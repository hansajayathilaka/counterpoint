# ADR-0005: Devices depends on `Microsoft.Extensions.Logging.Abstractions`, not on a logger

**Status:** Accepted
**Date:** 2026-09-06
**Deciders:** P0-T05

## Context

CLAUDE.md invariant 7 says a printer failure "degrades with a warning" and never blocks the
sale. A warning that goes nowhere is not a warning, so `FileReceiptPrinter` — and every device
adapter after it — needs somewhere to write one. `Counterpoint.Devices` had no package
references at all, and the engineering guide's dependency rule for new packages is: record why.

## Options considered

**Pass a callback (`Action<string, Exception?>`).** No dependency, but it reinvents log levels,
event ids and structured properties, and every composition root has to adapt Serilog to it by
hand. Rejected: it is a logging abstraction with worse ergonomics and no tooling.

**Reference Serilog directly in `Devices`.** Ties a device adapter to one logging
implementation, so a future host (or a test) cannot substitute another. Rejected — the guide
already picks Serilog *in the composition root*, which is where that choice belongs.

**Reference `Microsoft.Extensions.Logging.Abstractions`.** The `ILogger<T>` contract only: no
sink, no configuration, no implementation. `Serilog.Extensions.Hosting` (already pinned) plugs
Serilog in behind it at start-up, and tests use `NullLogger<T>` or a recording logger.

## Decision

`Counterpoint.Devices` references `Microsoft.Extensions.Logging.Abstractions` (10.0.0, MIT,
part of the .NET platform's own package set, which NFR-M5's "hand the source over" requirement
is comfortable with). Log call sites use the `[LoggerMessage]` source generator so there is no
allocation on the hot print path and no `CA1848`/`CA2254` suppression.

## Consequences

Easier: device adapters degrade audibly; the print warning reaches the same Serilog file sink
as the rest of the system without `Devices` knowing Serilog exists.

Harder: nothing material. The abstraction package carries no transitive weight.

We would revisit this only if the project dropped `Microsoft.Extensions.*` hosting altogether,
which would also invalidate the composition-root design in SAD §5.
