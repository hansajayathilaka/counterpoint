using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// The wrapper <see cref="RoleAuthorisation.Decorate{TService}"/> puts in front of a service.
/// Build one through that method; it is public only because a
/// <see cref="DispatchProxy"/> subclass has to be visible to the proxy type the runtime
/// generates from it.
/// </summary>
/// <typeparam name="TService">The service interface being guarded.</typeparam>
/// <remarks>
/// Not sealed, and not because subclassing it is a good idea: the runtime generates a type that
/// <em>derives</em> from this one, and <see cref="DispatchProxy.Create{T, TProxy}"/> refuses a
/// sealed base.
/// </remarks>
public class RoleAuthorisationProxy<TService> : DispatchProxy
    where TService : class
{
    private TService? _inner;
    private ISession? _session;

    /// <summary>Supplies the service and the session. Called once, immediately after creation.</summary>
    internal void Initialise(TService inner, ISession session)
    {
        _inner = inner;
        _session = session;
    }

    /// <summary>
    /// Checks the role, then delegates.
    /// </summary>
    /// <remarks>
    /// The order is the contract: if the check fails, <c>targetMethod.Invoke</c> is never
    /// reached, so the guarded service does not run at all and cannot have written anything.
    /// The throw is synchronous even for a method that returns a <see cref="System.Threading.Tasks.Task"/> -
    /// a refusal is not a result, and handing back a faulted task would let a caller that forgot
    /// to await it carry on as though the call had been allowed.
    /// </remarks>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var inner = _inner ?? throw new InvalidOperationException(
            "This role-authorisation proxy was never initialised. Build it with RoleAuthorisation.Decorate.");

        var session = _session ?? throw new InvalidOperationException(
            "This role-authorisation proxy was never initialised. Build it with RoleAuthorisation.Decorate.");

        if (RoleAuthorisation.RequiredRole(targetMethod, inner.GetType()) is { } required)
        {
            Require(session, required, targetMethod);
        }

        try
        {
            return targetMethod.Invoke(inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // Rethrow what the service actually threw, with its own stack trace intact. A caller
            // must not have to know it was talking to a proxy in order to catch a business rule.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void Require(ISession session, Role required, MethodInfo method)
    {
        var user = session.CurrentUser;

        if (user is null)
        {
            throw new NotAuthorisedException(
                "You must be signed in to do that. Sign in and try again.");
        }

        if (!Roles.Satisfies(user.Role, required))
        {
            throw new NotAuthorisedException(string.Create(
                CultureInfo.InvariantCulture,
                $"{user.DisplayName} is signed in as {Roles.ToToken(user.Role).ToLowerInvariant()}. {Describe(method)} needs the {Roles.ToToken(required).ToLowerInvariant()}."));
        }
    }

    /// <summary>
    /// Names the refused operation without leaking anything: the interface and method the caller
    /// already knows it asked for, and nothing about the data behind it.
    /// </summary>
    private static string Describe(MethodInfo method) => string.Create(
        CultureInfo.InvariantCulture,
        $"{method.DeclaringType?.Name ?? "This service"}.{method.Name}");
}
