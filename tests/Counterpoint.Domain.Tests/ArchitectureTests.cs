using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Counterpoint.Application.Security;
using FluentAssertions;

namespace Counterpoint.Domain.Tests;

/// <summary>
/// Structural rules that keep the solution honest. SRS NFR-M5, SAD §5,
/// CLAUDE.md "Project boundaries" and invariant 1 ("no double/float").
///
/// These tests deliberately avoid taking project references on the projects they
/// police. Boundaries are read from the .csproj graph; the no-floating-point scan
/// loads the built assemblies by path (see the build-order-only ProjectReference
/// for Counterpoint.Infrastructure in this project's .csproj).
/// </summary>
public sealed class ArchitectureTests
{
    private const string SolutionFileName = "Counterpoint.sln";

    /// <summary>Projects that must never appear in the Ui reference graph (CLAUDE.md).</summary>
    private static readonly string[] ProjectsUiMayNotReach =
    [
        "Counterpoint.Infrastructure",
        "Counterpoint.Devices",
        "Counterpoint.Reporting",
        "Counterpoint.Backup",
    ];

    /// <summary>
    /// The one project allowed to see both the UI and the adapters behind it: the composition
    /// root. Everything meets everything else exactly there, through interfaces.
    /// </summary>
    private const string CompositionRoot = "Counterpoint.App";

    /// <summary>Assemblies subject to invariant 1: money is decimal, never binary floating point.</summary>
    private static readonly string[] NoFloatingPointAssemblies =
    [
        "Counterpoint.Domain",
        "Counterpoint.Application",
        "Counterpoint.Infrastructure",
    ];

    [Fact]
    public void Ui_ReferencesOnlyApplicationAndDomain()
    {
        var uiProject = SrcProject("Counterpoint.Ui");

        var reachable = ReachableProjects(uiProject);

        reachable.Should().BeEquivalentTo(
            ["Counterpoint.Application", "Counterpoint.Domain"],
            "Counterpoint.Ui gets Infrastructure, Devices, Reporting and Backup through "
            + "interfaces registered in the composition root. A direct or transitive "
            + "reference would let authorisation and business rules leak into the UI "
            + "(CLAUDE.md invariant 8, SRS NFR-S2, AC-17).");

        foreach (var forbidden in ProjectsUiMayNotReach)
        {
            reachable.Should().NotContain(
                forbidden,
                "Counterpoint.Ui must not reference {0}, directly or transitively",
                forbidden);
        }
    }

    [Fact]
    public void OnlyTheCompositionRootReachesBothTheUiAndTheAdaptersBehindIt()
    {
        var offenders = new List<string>();

        foreach (var project in SrcProjects())
        {
            var name = Path.GetFileNameWithoutExtension(project.Name);
            if (name.Equals(CompositionRoot, StringComparison.Ordinal))
            {
                continue;
            }

            var reachable = ReachableProjects(project);
            if (!reachable.Contains("Counterpoint.Ui"))
            {
                continue;
            }

            offenders.AddRange(ProjectsUiMayNotReach
                .Where(reachable.Contains)
                .Select(adapter => $"{name} reaches both Counterpoint.Ui and {adapter}"));
        }

        offenders.Should().BeEmpty(
            "{0} is the composition root and the only project that may see the UI and the "
            + "adapters at the same time. Anywhere else, that combination is a route for "
            + "business rules or authorisation to reach the screen without passing through "
            + "the Application layer (CLAUDE.md \"Project boundaries\", SRS NFR-S2, AC-17). "
            + "Offenders: {1}",
            CompositionRoot,
            string.Join("; ", offenders));

        // And the composition root really is one: it has to reach both, or the rule above is
        // vacuously true because nothing reaches the UI at all.
        var root = ReachableProjects(SrcProject(CompositionRoot));

        root.Should().Contain("Counterpoint.Ui")
            .And.Contain("Counterpoint.Infrastructure")
            .And.Contain("Counterpoint.Devices",
                "{0} is where the screen is handed its Application services and where those "
                + "services are handed their adapters", CompositionRoot);
    }

    [Fact]
    public void Domain_ReferencesNoNuGetPackage()
    {
        var domainProject = SrcProject("Counterpoint.Domain");
        var document = XDocument.Load(domainProject.FullName);

        var packages = document.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();

        packages.Should().BeEmpty(
            "Counterpoint.Domain has zero dependencies by design - it must stay a pure, "
            + "framework-free description of the shop's rules (CLAUDE.md \"Project boundaries\")");

        ReachableProjects(domainProject).Should().BeEmpty(
            "Counterpoint.Domain references nothing at all, not even another Counterpoint project");

        // Belt and braces: prove it against the compiled assembly, not just the project file.
        var nonFrameworkReferences = LoadProbeAssembly("Counterpoint.Domain")
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => !IsFrameworkAssembly(name))
            .ToArray();

        nonFrameworkReferences.Should().BeEmpty(
            "the built Counterpoint.Domain assembly must bind to nothing but the base class library");
    }

    /// <summary>
    /// Every concrete Application service sitting behind an interface that carries
    /// <see cref="RequiresRoleAttribute"/> must be invisible outside its own assembly
    /// (SRS NFR-S2, AC-17, CLAUDE.md invariant 8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RequiresRoleAttribute"/>'s own remarks say the attribute "has no effect on a
    /// class nobody wrapped … an architecture-style test would be the way to keep that true as
    /// services multiply". This is that test.
    /// </para>
    /// <para>
    /// A public class with a public constructor can be registered in the container by its
    /// concrete type, resolved by anything holding an <c>IServiceProvider</c>, or simply
    /// <c>new</c>ed up from its individually registered dependencies - each of those reaches the
    /// service with no <see cref="RoleAuthorisation"/> in front of it, and none of them is a
    /// compile error. Keeping the implementation internal leaves the decorated interface the
    /// composition root registers as the only way in.
    /// </para>
    /// </remarks>
    [Fact]
    public void ConcreteOwnerOnlyApplicationServicesAreNotPublic()
    {
        // The assembly is taken from the attribute's own type so that the attribute instances
        // found on it compare equal to typeof(RequiresRoleAttribute) - a second copy loaded by
        // path would not.
        var application = typeof(RequiresRoleAttribute).Assembly;

        var offenders = GetLoadableTypes(application)
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsVisible)
            .Where(type => type.GetInterfaces().Any(RequiresARole))
            .Select(Describe)
            .ToArray();

        offenders.Should().BeEmpty(
            "a service behind an interface carrying [RequiresRole] must be internal, so that the "
            + "role-decorated interface the composition root registers is the only way to reach "
            + "it (SRS NFR-S2, AC-17). Offenders: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// True when <paramref name="contract"/> declares a role requirement, on the interface itself
    /// or on any one of its members - the two places
    /// <see cref="RoleAuthorisation.RequiredRole"/> looks that an interface can carry.
    /// </summary>
    private static bool RequiresARole(Type contract) =>
        contract.IsDefined(typeof(RequiresRoleAttribute), inherit: true) ||
        contract.GetMembers().Any(member => member.IsDefined(typeof(RequiresRoleAttribute), inherit: true));

    [Fact]
    public void NoDoubleOrFloatInDomainApplicationOrInfrastructure()
    {
        const BindingFlags AllMembers =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        var offenders = new List<string>();

        foreach (var assemblyName in NoFloatingPointAssemblies)
        {
            var assembly = LoadProbeAssembly(assemblyName);

            // GetTypes() includes nested types, and every accessibility level.
            foreach (var type in GetLoadableTypes(assembly))
            {
                foreach (var field in type.GetFields(AllMembers))
                {
                    if (IsFloatingPoint(field.FieldType))
                    {
                        offenders.Add($"{Describe(type)}.{field.Name} (field: {Name(field.FieldType)})");
                    }
                }

                foreach (var property in type.GetProperties(AllMembers))
                {
                    if (IsFloatingPoint(property.PropertyType))
                    {
                        offenders.Add($"{Describe(type)}.{property.Name} (property: {Name(property.PropertyType)})");
                    }
                }

                foreach (var method in type.GetMethods(AllMembers))
                {
                    if (IsFloatingPoint(method.ReturnType))
                    {
                        offenders.Add($"{Describe(type)}.{method.Name} (returns {Name(method.ReturnType)})");
                    }

                    offenders.AddRange(FloatingPointParameters(type, method.Name, method.GetParameters()));
                }

                foreach (var constructor in type.GetConstructors(AllMembers))
                {
                    offenders.AddRange(FloatingPointParameters(type, ".ctor", constructor.GetParameters()));
                }
            }
        }

        offenders.Should().BeEmpty(
            "money and quantity are scaled integers mapped to decimal via the Money and "
            + "Quantity value objects. double and float are banned in Domain, Application "
            + "and Infrastructure (CLAUDE.md invariant 1, SRS DM-01/DM-02). Offenders: "
            + string.Join("; ", offenders));
    }

    private static IEnumerable<string> FloatingPointParameters(
        Type type, string memberName, ParameterInfo[] parameters) =>
        parameters
            .Where(p => IsFloatingPoint(p.ParameterType))
            .Select(p => string.Create(
                CultureInfo.InvariantCulture,
                $"{Describe(type)}.{memberName}(parameter '{p.Name}': {Name(p.ParameterType)})"));

    /// <summary>
    /// True for double/float in any wrapping: nullable, array, by-ref, pointer,
    /// or as a generic argument such as <c>IReadOnlyList&lt;double&gt;</c>.
    /// </summary>
    private static bool IsFloatingPoint(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type == typeof(double) || type == typeof(float))
        {
            return true;
        }

        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            return IsFloatingPoint(type.GetElementType());
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return IsFloatingPoint(underlying);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsFloatingPoint);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }

    private static string Describe(Type type) => type.FullName ?? type.Name;

    private static string Name(Type type) => type.FullName ?? type.Name;

    private static bool IsFrameworkAssembly(string name) =>
        name.Equals("mscorlib", StringComparison.Ordinal) ||
        name.Equals("netstandard", StringComparison.Ordinal) ||
        name.Equals("System", StringComparison.Ordinal) ||
        name.StartsWith("System.", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.CSharp", StringComparison.Ordinal);

    /// <summary>
    /// Loads an assembly that sits beside the test assembly. Fails loudly rather than
    /// letting a rule pass by accident because the assembly was never built.
    /// </summary>
    private static Assembly LoadProbeAssembly(string simpleName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");

        File.Exists(path).Should().BeTrue(
            "{0} must be present in the test output directory for the architecture rules to "
            + "be checkable; expected it at {1}", simpleName, path);

        return Assembly.LoadFrom(path);
    }

    /// <summary>Every project under <c>src/</c>, found on disk rather than listed here.</summary>
    private static FileInfo[] SrcProjects()
    {
        var source = new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, "src"));

        source.Exists.Should().BeTrue("the src directory must exist at {0}", source.FullName);

        return source.GetFiles("*.csproj", SearchOption.AllDirectories);
    }

    private static FileInfo SrcProject(string projectName)
    {
        var path = Path.Combine(RepositoryRoot().FullName, "src", projectName, projectName + ".csproj");
        var file = new FileInfo(path);

        file.Exists.Should().BeTrue("the project file {0} must exist", path);

        return file;
    }

    /// <summary>
    /// Every Counterpoint project reachable from <paramref name="project"/> through the
    /// ProjectReference graph, excluding the project itself.
    /// </summary>
    private static HashSet<string> ReachableProjects(FileInfo project)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<FileInfo>();
        pending.Enqueue(project);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project.FullName };

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            foreach (var referenced in DirectProjectReferences(current))
            {
                if (!visited.Add(referenced.FullName))
                {
                    continue;
                }

                reachable.Add(Path.GetFileNameWithoutExtension(referenced.Name));
                pending.Enqueue(referenced);
            }
        }

        return reachable;
    }

    private static FileInfo[] DirectProjectReferences(FileInfo project)
    {
        var directory = project.Directory
            ?? throw new InvalidOperationException($"{project.FullName} has no containing directory.");

        return XDocument.Load(project.FullName)
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => new FileInfo(
                Path.GetFullPath(
                    Path.Combine(directory.FullName, include!.Replace('\\', Path.DirectorySeparatorChar)))))
            .ToArray();
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
