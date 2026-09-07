# ADR-0007: The Argon2id password hasher lives in `Counterpoint.Application`

**Status:** Accepted
**Date:** 2026-09-07
**Deciders:** P1-T02

## Context

P1-T02 requires password hashing (SRS FR-1.3, NFR-S1) and CLAUDE.md requires authorisation to
live in the Application layer (invariant 8, NFR-S2, AC-17). `Konscious.Security.Cryptography.Argon2`
was already pinned in `Directory.Packages.props` as the engineering guide's chosen hashing
library, but no project referenced it, so the question of *which* project does was still open.
`Counterpoint.Application` had no package references at all until now, and `Counterpoint.Domain`
must keep none — an architecture test asserts it.

## Options considered

**Put it in `Counterpoint.Domain`.** It is a business rule in the sense that the shop's password
policy is one. Rejected outright: `Domain_ReferencesNoNuGetPackage` fails, and it should — Domain
is a framework-free description of the shop's rules and a hashing library is a framework.

**Put it behind a port in `Counterpoint.Application` and implement it in
`Counterpoint.Infrastructure`.** This is the pattern every other capability uses
(`IAuditTrail` → `SqliteAuditTrail`, `IUserStore` → `SqliteUserStore`) and it would keep
`Application` package-free. But the pattern exists to keep *I/O and platform* out of the
Application layer, and Argon2id is neither: it opens no connection, touches no file, calls no
Windows API and has no development-versus-production variant to substitute. The only thing the
indirection would buy is a second implementation nobody wants, and it would cost something real —
`AuthenticationService` and `UserAdministrationService` would no longer be able to enforce the
password policy without an adapter being wired correctly first.

**Reference the package from `Counterpoint.Application` directly.** Argon2id becomes what it
actually is: arithmetic over byte arrays, in the layer that owns the rules about when to run it.
`IPasswordHasher` still exists, because tests substitute cheap work factors and because the
interface is what the services depend on — but its one implementation lives next to it.

## Decision

`Counterpoint.Application` references `Konscious.Security.Cryptography.Argon2` (1.3.1, MIT —
permissively licensed, so NFR-M5's "the source is handed to the owner" is satisfied), and
`PasswordHasher` lives in `Application/Security/`.

It beat the port-and-adapter option because the boundary that option protects is "no I/O, no
platform, no framework in the Application layer", and this dependency crosses none of them. A
port with exactly one possible implementation is indirection, not architecture.

`Counterpoint.Domain` keeps its zero dependencies. The `Role` enum, which is the part of this
that really is the shop's vocabulary, went there instead.

## Consequences

Easier: the password policy (minimum length, work factors) is enforced in the same layer that
decides who may sign in, and a unit test in `Counterpoint.Domain.Tests` can exercise the whole of
it without a database.

Harder: `Counterpoint.Application` is no longer package-free, so the next package proposed for it
has to argue the same case rather than pointing at this one. That is the intended friction.

We would revisit this if the hashing implementation ever needed a platform-specific variant — a
native libargon2 binding for speed, say — because that is exactly the I/O-shaped boundary the
port-and-adapter option was for.
