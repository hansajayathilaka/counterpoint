using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
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
