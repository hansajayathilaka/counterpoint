using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Counterpoint.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Counterpoint.Infrastructure.DesignTime;

/// <summary>
/// Design-time only. Teaches <c>dotnet ef</c> how to write a <see cref="Money"/>,
/// <see cref="TaxRate"/> or <see cref="Percentage"/> as C# source, which
/// <c>dotnet ef dbcontext optimize</c> needs before it can scaffold the compiled model (NFR-P6).
/// </summary>
/// <remarks>
/// <para>
/// This file compiles only when <c>EfTooling=true</c> (see the project file and docs/adr/0004).
/// It is not part of the shipped application and nothing at run time references it.
/// </para>
/// <para>
/// Why it is needed. A column default set with <c>HasDefaultValue(Money.Zero)</c> is held on the
/// model in the model's CLR type, not the provider's - EF rejects <c>HasDefaultValue(0L)</c> on a
/// <c>Money</c> property outright. The migration scaffolder never meets that value: it reads the
/// relational model, which has already run the value converter and hands back <c>0L</c>. The
/// compiled-model scaffolder writes annotations out verbatim, so it meets a <c>Money</c>, has no
/// literal syntax for it, and stops with "Cannot scaffold C# literals of type ...". Supplying one
/// here is the smallest fix that keeps the model, the migration and the compiled model saying the
/// same thing.
/// </para>
/// <para>
/// Each literal round-trips through the same scaled-integer conversion the database uses, so a
/// default in the compiled model cannot drift from the <c>DEFAULT</c> in the DDL.
/// </para>
/// </remarks>
public sealed class PosDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        // EF registers its own ICSharpHelper before user services are configured. Take that
        // registration, build it, and wrap it - rather than naming the implementation type, which
        // is internal to the tooling and has moved between releases.
        var registered = serviceCollection.LastOrDefault(service => service.ServiceType == typeof(ICSharpHelper))
            ?? throw new InvalidOperationException(
                "No ICSharpHelper is registered, so there is nothing to wrap. The EF Core design-time " +
                "services changed shape; PosDesignTimeServices needs revisiting.");

        serviceCollection.AddSingleton(provider => ScaledValueObjectLiterals.Wrap(Build(provider, registered)));
    }

    private static ICSharpHelper Build(IServiceProvider provider, ServiceDescriptor registered) =>
        registered switch
        {
            { ImplementationInstance: ICSharpHelper instance } => instance,
            { ImplementationFactory: { } factory } => (ICSharpHelper)factory(provider),
            { ImplementationType: { } type } => (ICSharpHelper)ActivatorUtilities.CreateInstance(provider, type),
            _ => throw new InvalidOperationException("The registered ICSharpHelper cannot be constructed."),
        };
}

/// <summary>
/// Forwards every <see cref="ICSharpHelper"/> call to the tooling's own implementation, except a
/// top-level <c>UnknownLiteral</c> for one of the three scaled value objects of
/// docs/01_DATA_MODEL.md §1.
/// </summary>
/// <remarks>
/// A <see cref="DispatchProxy"/> rather than a hand-written decorator on purpose: the interface
/// carries a few dozen members that this class has no opinion about, and a decorator listing them
/// would go stale - silently, and only at design time - the next time EF adds one. Not sealed:
/// <see cref="DispatchProxy.Create{T,TProxy}"/> derives from it at run time and refuses a sealed
/// base.
/// </remarks>
internal class ScaledValueObjectLiterals : DispatchProxy
{
    private ICSharpHelper _inner = null!;

    internal static ICSharpHelper Wrap(ICSharpHelper inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var proxy = Create<ICSharpHelper, ScaledValueObjectLiterals>();
        ((ScaledValueObjectLiterals)(object)proxy)._inner = inner;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (targetMethod.Name == nameof(ICSharpHelper.UnknownLiteral)
            && args is [var value]
            && ScaledLiteral(value) is { } literal)
        {
            return literal;
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // Reflection wraps whatever the helper threw. Rethrow the original so the tooling's
            // own diagnostics reach the developer unchanged.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static string? ScaledLiteral(object? value) => value switch
    {
        Money money =>
            "global::Counterpoint.Domain.ValueObjects.Money.FromScaled(" + Scaled(money.ToScaled()) + ")",
        TaxRate rate =>
            "global::Counterpoint.Domain.ValueObjects.TaxRate.FromScaled(" + Scaled(rate.ToScaled()) + ")",
        Percentage percentage =>
            "global::Counterpoint.Domain.ValueObjects.Percentage.FromScaled(" + Scaled(percentage.ToScaled()) + ")",
        _ => null,
    };

    private static string Scaled(long scaled) =>
        scaled.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L";
}
