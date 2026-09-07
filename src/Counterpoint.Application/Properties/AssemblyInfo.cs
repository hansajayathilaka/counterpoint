using System.Runtime.CompilerServices;

// The override token's constructor is internal so that only IOwnerOverrideService can mint one -
// a command that is handed a token has to be able to trust where it came from. Testing the
// token's own rules (single use, expiry, action match) therefore needs this one seam rather than
// a public constructor that would let anything forge an override (SRS FR-1.7).
[assembly: InternalsVisibleTo("Counterpoint.Domain.Tests")]

// The concrete implementation behind an owner-only interface is internal so that nothing can
// resolve or construct one without RoleAuthorisation in front of it (SRS NFR-S2, AC-17). A
// composition root still has to build one in order to decorate it, so the three that do - the
// application's own and the two test composition roots - need this seam. Nothing else does:
// everywhere else in the solution is handed the decorated interface.
// "Counterpoint" is Counterpoint.App's assembly name - the composition root.
[assembly: InternalsVisibleTo("Counterpoint")]
[assembly: InternalsVisibleTo("Counterpoint.Integration.Tests")]
[assembly: InternalsVisibleTo("Counterpoint.Acceptance.Tests")]
