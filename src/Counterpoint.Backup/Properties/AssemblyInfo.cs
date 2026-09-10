using System.Runtime.CompilerServices;

// The concrete implementation behind an owner-only interface (IManualBackupTrigger,
// IGuidedRestoreService) is internal so that nothing can resolve or construct one without
// RoleAuthorisation in front of it (SRS NFR-S2, AC-17), the same discipline
// Counterpoint.Application's own AssemblyInfo.cs documents for IUserAdministration. A composition
// root still has to build one in order to decorate it, so the three that do - the application's
// own and the two test composition roots - need this seam. Nothing else does: everywhere else in
// the solution is handed the decorated interface.
// "Counterpoint" is Counterpoint.App's assembly name - the composition root.
[assembly: InternalsVisibleTo("Counterpoint")]
[assembly: InternalsVisibleTo("Counterpoint.Integration.Tests")]
[assembly: InternalsVisibleTo("Counterpoint.Acceptance.Tests")]
