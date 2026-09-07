# ADR-0006: `Counterpoint.Devices` depends on `Microsoft.Extensions.Hosting.Abstractions`

**Status:** Accepted
**Date:** 2026-09-06
**Deciders:** P0-T06

## Context

CLAUDE.md invariant 7 and SAD §8 put the receipt on the far side of the sale transaction: the
sale writes a `print_job` row and a background worker prints it afterwards. That worker has to
be *something* — a long-running loop the composition root starts and stops with the
application, that never takes the process down when a printer misbehaves.

`Counterpoint.Devices` had one package reference (`Microsoft.Extensions.Logging.Abstractions`,
ADR-0005) and the engineering guide's rule for a new one is: record why.

## Options considered

**Roll our own loop: a `Task` started in `Program.Main` with a `CancellationTokenSource`.** No
dependency, but it reimplements start/stop ordering, graceful shutdown on SIGTERM, and the
"stop the host if a background task faults" policy — each of which is exactly the class of thing
that is fine until the day it is not, on a machine nobody can attach a debugger to.

**Put the worker in `Counterpoint.App` (the composition root) instead of `Devices`.** The
composition root already references `Microsoft.Extensions.Hosting`, so no new dependency
anywhere. Rejected: the outbox worker is device behaviour — retry budget, failure classification,
job naming — and it needs a device test of its own. Burying it in the executable makes it
untestable and puts logic in the one project that is meant to hold none.

**Reference `Microsoft.Extensions.Hosting.Abstractions`.** `BackgroundService`, `IHostedService`
and `AddHostedService` only: no host builder, no configuration, no logging provider, no lifetime
implementation. The composition root supplies the actual `IHost`.

## Decision

`Counterpoint.Devices` references `Microsoft.Extensions.Hosting.Abstractions` (10.0.0, MIT, part
of the .NET platform's own package set) and `Microsoft.Extensions.DependencyInjection` for its
`AddCounterpointDevices` registration extension — the same arrangement
`Counterpoint.Infrastructure` already has.

`PrintWorker` is registered as a singleton *and* handed to the host as an `IHostedService`, so a
test can build the same container and drive a single `DrainAsync` pass without a polling loop
running underneath it. Its poll interval is injectable for the same reason.

## Consequences

Easier: the worker starts and stops with the application, shuts down cleanly, and is covered by
an integration test that never sleeps.

Harder: nothing material. The abstractions package carries no transitive weight beyond
`Microsoft.Extensions.DependencyInjection.Abstractions`, which was already in the graph.

We would revisit this if the product dropped `Microsoft.Extensions.*` hosting, which would also
invalidate the composition-root design in SAD §5.
