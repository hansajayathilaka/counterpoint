using System;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// Marks an Application service - or one method of one - as needing a role
/// (SRS FR-1.2, NFR-S2, AC-17).
/// </summary>
/// <remarks>
/// <para>
/// The attribute states the requirement; <see cref="RoleAuthorisation"/> enforces it. Put it on
/// the <b>interface</b> where you can: the interface is what the UI is handed, so a requirement
/// declared there is visible at the only place the two meet.
/// </para>
/// <para>
/// It has no effect on a class nobody wrapped, which is why the composition root wraps every
/// service that carries one, why those implementations are <c>internal</c> so that the wrapper
/// cannot be gone round, and why
/// <c>ArchitectureTests.ConcreteOwnerOnlyApplicationServicesAreNotPublic</c> keeps that true as
/// services multiply. Hiding a button is not authorisation and never counts as one of these
/// (NFR-S2).
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = false)]
public sealed class RequiresRoleAttribute : Attribute
{
    public RequiresRoleAttribute(Role role) => Role = role;

    /// <summary>The role the caller must hold.</summary>
    public Role Role { get; }
}
