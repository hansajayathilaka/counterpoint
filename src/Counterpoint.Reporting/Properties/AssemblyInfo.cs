using System.Runtime.CompilerServices;

// The concrete implementation behind an owner-only report interface (IStockValuationQuery, task
// P2-T11) is internal so that nothing can resolve or construct one without RoleAuthorisation in
// front of it (SRS NFR-S2, AC-17, CLAUDE.md invariant 8) - the same discipline
// Counterpoint.Application's and Counterpoint.Backup's own AssemblyInfo.cs document. A composition
// root still has to be able to name the type to prove the container hands out no undecorated
// route to it, which is what the two test composition roots below use this seam for.
// "Counterpoint" is Counterpoint.App's assembly name - the composition root.
[assembly: InternalsVisibleTo("Counterpoint")]
[assembly: InternalsVisibleTo("Counterpoint.Integration.Tests")]
[assembly: InternalsVisibleTo("Counterpoint.Acceptance.Tests")]
