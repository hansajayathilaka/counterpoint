using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// Enforces <see cref="RequiresRoleAttribute"/> (SRS NFR-S2, AC-17, CLAUDE.md invariant 8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the check happens.</b> Here, in the Application layer, in front of the service - not
/// in a viewmodel and not in a view. <see cref="Decorate{TService}"/> returns something that
/// implements the same interface, so the caller cannot tell it apart from the real service and
/// cannot go round it: the composition root only ever registers the wrapper, and
/// <c>Counterpoint.Ui</c> cannot reference the assembly the real one would come from anyway.
/// A test that calls the decorated service directly, with no UI in sight, is what AC-17 asks
/// for and is what proves this.
/// </para>
/// <para>
/// <b>Why a <see cref="DispatchProxy"/>.</b> One wrapper covers every service that carries the
/// attribute, including the ones written after this task, so adding an owner-only method to an
/// interface cannot be shipped with the guard forgotten. A hand-written decorator per service
/// would have to be remembered each time, and a container that does interception would be a
/// heavyweight dependency for a check that is four lines long. The reflective call costs a few
/// microseconds and is only ever paid on administrative paths - the sale path carries no
/// <see cref="RequiresRoleAttribute"/> and is not wrapped.
/// </para>
/// </remarks>
public static class RoleAuthorisation
{
    /// <summary>
    /// Wraps <paramref name="inner"/> so that every call carrying a
    /// <see cref="RequiresRoleAttribute"/> is checked against <paramref name="session"/> before
    /// it reaches the real service.
    /// </summary>
    /// <typeparam name="TService">The service interface. Must be an interface.</typeparam>
    public static TService Decorate<TService>(TService inner, ISession session)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(session);

        if (!typeof(TService).IsInterface)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{typeof(TService).Name} must be an interface for the role check to sit in front of it. A concrete class can be called around the guard."),
                nameof(inner));
        }

        var proxy = DispatchProxy.Create<TService, RoleAuthorisationProxy<TService>>();
        ((RoleAuthorisationProxy<TService>)(object)proxy!).Initialise(inner, session);

        return proxy!;
    }

    /// <summary>
    /// The role <paramref name="interfaceMethod"/> requires, or null when it requires none.
    /// </summary>
    /// <remarks>
    /// Four places are consulted - the interface method, the interface, the implementing method
    /// and the implementing class - and the <em>highest</em> requirement found wins. A method
    /// therefore cannot lower the bar its interface sets simply by not repeating the attribute,
    /// which is the failure mode this lookup exists to rule out.
    /// </remarks>
    public static Role? RequiredRole(MethodInfo interfaceMethod, Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(interfaceMethod);
        ArgumentNullException.ThrowIfNull(implementationType);

        Role?[] candidates =
        [
            From(interfaceMethod),
            From(interfaceMethod.DeclaringType),
            From(ImplementationOf(interfaceMethod, implementationType)),
            From(implementationType),
        ];

        return candidates.Aggregate((Role?)null, Higher);
    }

    private static Role? From(MemberInfo? member) =>
        member?.GetCustomAttribute<RequiresRoleAttribute>(inherit: true)?.Role;

    /// <summary>
    /// The method on the implementation that <paramref name="interfaceMethod"/> resolves to, or
    /// null when the map cannot be taken (a proxy, or a type that does not implement it).
    /// </summary>
    private static MethodInfo? ImplementationOf(MethodInfo interfaceMethod, Type implementationType)
    {
        var declaring = interfaceMethod.DeclaringType;
        if (declaring is null ||
            !declaring.IsInterface ||
            implementationType.IsInterface ||
            !declaring.IsAssignableFrom(implementationType))
        {
            return null;
        }

        var map = implementationType.GetInterfaceMap(declaring);
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            if (map.InterfaceMethods[i] == interfaceMethod)
            {
                return map.TargetMethods[i];
            }
        }

        return null;
    }

    /// <summary>
    /// The stricter of two requirements. <c>Satisfies(left, right)</c> means holding
    /// <c>left</c> is enough for <c>right</c>, so <c>left</c> is the stricter one.
    /// </summary>
    private static Role? Higher(Role? left, Role? right) => (left, right) switch
    {
        (null, _) => right,
        (_, null) => left,
        _ => Roles.Satisfies(left.Value, right.Value) ? left : right,
    };
}
