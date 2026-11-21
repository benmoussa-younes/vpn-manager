using System.Reflection;
using VpnManager.Core.Presentation;
using VpnManager.Core.Services;

namespace VpnManager.Core.Tests;

/// <summary>
/// Structural proofs the acceptance criteria ask for, expressed as tests so they
/// run in the gate rather than in an ad-hoc shell command.
/// </summary>
public sealed class ArchitectureGuardTests
{
    /// <summary>The elevation program name, assembled at run time so it never appears here literally.</summary>
    private static string ElevationToken => string.Concat("su", "do");

    /// <summary>The polkit elevation program name, assembled at run time so it never appears here literally.</summary>
    private static string PrivilegedToken => string.Concat("pk", "exec");

    /// <summary>The alternative elevation program name, assembled at run time.</summary>
    private static string DoasToken => string.Concat("do", "as");

    [Fact]
    public void The_core_library_does_not_reference_avalonia()
    {
        var references = typeof(TunnelService).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, name => name.Contains("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_core_library_references_only_the_core_packages_it_uses()
    {
        var references = typeof(TunnelService).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("Core", references);
        Assert.Contains("Core.IO", references);
        // The passive-view contracts are UI-free, so the headless kernel may adopt
        // them; the shell itself must stay out.
        Assert.Contains("Core.Mvp", references);
        Assert.DoesNotContain(references, name => name is "Core.UI" or "Core.CommandLine" or "Core.IO.Pipeline" or "Core.Serialization" or "Core.Units");
    }

    [Fact]
    public void The_core_library_has_no_direct_process_start()
    {
        var offenders = RepositoryLayout.SourceFiles("src/VpnManager.Core")
            .Where(path => File.ReadAllText(path).Contains("ProcessStartInfo", StringComparison.Ordinal))
            .Select(RepositoryLayout.RelativePath)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_core_library_never_requests_elevation_or_starts_a_shell()
    {
        var forbidden = new[] { ElevationToken, PrivilegedToken, DoasToken, "/bin/sh", "/bin/bash", "/usr/bin/env", "UseShellExecute = true", "\"bash\"", "\"sh\" -c" };

        foreach (var path in RepositoryLayout.SourceFiles("src/VpnManager.Core"))
        {
            var text = File.ReadAllText(path);

            foreach (var token in forbidden)
            {
                Assert.False(
                    text.Contains(token, StringComparison.Ordinal),
                    $"{RepositoryLayout.RelativePath(path)} contains a forbidden token");
            }
        }
    }

    [Fact]
    public void The_application_never_requests_elevation_or_starts_a_shell()
    {
        var forbidden = new[] { ElevationToken, DoasToken, "/bin/sh", "/bin/bash", "UseShellExecute = true" };

        foreach (var path in RepositoryLayout.SourceFiles("src/VpnManager.App"))
        {
            var text = File.ReadAllText(path);

            foreach (var token in forbidden)
            {
                Assert.False(
                    text.Contains(token, StringComparison.Ordinal),
                    $"{RepositoryLayout.RelativePath(path)} contains a forbidden token");
            }
        }
    }

    [Fact]
    public void Only_the_privileged_action_service_names_the_elevation_launcher()
    {
        var offenders = RepositoryLayout.SourceFiles("src/VpnManager.App")
            .Where(path => File.ReadAllText(path).Contains(PrivilegedToken, StringComparison.Ordinal))
            .Select(RepositoryLayout.RelativePath)
            .ToArray();

        // ADR 0007: one narrow seam may name the launcher; the rest of the app
        // never elevates.
        Assert.Equal(["src/VpnManager.App/Privileges/PkexecRootActionService.cs"], offenders);
    }

    [Fact]
    public void No_presentation_or_view_source_reads_a_raw_key()
    {
        var offenders = RepositoryLayout.SourceFiles("src/VpnManager.Core/Presentation")
            .Concat(RepositoryLayout.SourceFiles("src/VpnManager.App"))
            .Where(path => File.ReadAllText(path).Contains(".Reveal(", StringComparison.Ordinal))
            .Select(RepositoryLayout.RelativePath)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_only_writers_of_a_raw_key_are_the_configuration_writer_and_the_validator()
    {
        var offenders = RepositoryLayout.SourceFiles("src/VpnManager.Core")
            .Where(path => File.ReadAllText(path).Contains(".Reveal(", StringComparison.Ordinal))
            .Select(RepositoryLayout.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "src/VpnManager.Core/Configuration/WireGuardConfigWriter.cs",
                "src/VpnManager.Core/Runtime/WireGuardCommand.cs",
                "src/VpnManager.Core/Validation/TunnelValidator.cs",
            ],
            offenders);
    }

    [Fact]
    public void The_view_contract_is_free_of_avalonia_types()
    {
        var viewContract = typeof(ITunnelListView);

        Assert.DoesNotContain(
            viewContract.GetMethods().SelectMany(method => method.GetParameters()).Select(parameter => parameter.ParameterType.Namespace ?? string.Empty),
            name => name.Contains("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            viewContract.GetMethods().Select(method => method.ReturnType.Namespace ?? string.Empty),
            name => name.Contains("Avalonia", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_suite_is_not_empty()
    {
        var testMethods = typeof(ArchitectureGuardTests).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Count(method => method.GetCustomAttributes<FactAttribute>(inherit: false).Any());

        Assert.True(testMethods > 100, $"only {testMethods} test methods were discovered: a silently empty suite must not look green.");
    }
}

/// <summary>Finds the repository root from the test output directory.</summary>
internal static class RepositoryLayout
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    internal static string Root() => RootPath.Value;

    internal static IEnumerable<string> SourceFiles(string relativeDirectory) =>
        Directory.EnumerateFiles(Path.Combine(Root(), relativeDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);

    internal static string RelativePath(string path) =>
        Path.GetRelativePath(Root(), path).Replace('\\', '/');

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VpnManager.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
