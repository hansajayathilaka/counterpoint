# ADR-0004: Microsoft.EntityFrameworkCore.Design is referenced only under `EfTooling=true`

**Status:** Accepted
**Date:** 2026-09-04
**Deciders:** P0-T04

## Context

`dotnet ef migrations add` requires `Microsoft.EntityFrameworkCore.Design` on the project that
owns the `DbContext`. Adding it to `Counterpoint.Infrastructure` unconditionally **breaks the
build**: its transitive graph pulls `System.Security.Cryptography.Xml`, NuGet audit raises eight
`NU1903` warnings, and `TreatWarningsAsErrors` turns them into errors. Pinning the package
forward to 10.0.0 does not help — that version is flagged too.

## Options considered

**Disable NuGet audit for the whole project.** One line, but it also silences audit for the
packages that actually ship in the till, including the SQLCipher provider ADR-0003 is about.
Rejected: it trades a real safety net for tooling convenience.

**Hand-write migrations without the Design package.** Possible — `Migration` and `ModelSnapshot`
live in `Microsoft.EntityFrameworkCore.Relational`, which is already referenced — but the model
snapshot has to be kept correct by hand, and a wrong snapshot is silent until the next migration
generates the wrong diff. Rejected.

**Reference the Design package behind an MSBuild condition.** The package is present only when a
developer sets `EfTooling=true` to regenerate a migration. Audit is disabled only in that same
condition, so the flag and its blast radius move together.

## Decision

`Counterpoint.Infrastructure.csproj` carries both the `PackageReference` and `NuGetAudit=false`
under `Condition="'$(EfTooling)' == 'true'"`. Migrations are generated with:

```
EfTooling=true dotnet ef migrations add <Name> \
  --project src/Counterpoint.Infrastructure \
  --startup-project src/Counterpoint.Infrastructure
```

`PosDbContextDesignTimeFactory` supplies a throwaway unencrypted temp-file context for the
tooling; it is never registered in DI and never reachable at runtime.

## Consequences

- The normal build, CI and the shipped application restore neither the Design package nor
  `System.Security.Cryptography.Xml`, and audit stays on for everything they do restore.
- Regenerating a migration is a two-word-longer command, which has to be documented — it is, in
  `docs/01_DATA_MODEL.md` §13.
- Revisit when EF ships a Design package whose graph no longer trips the audit; the condition can
  then be dropped and the reference made unconditional.
