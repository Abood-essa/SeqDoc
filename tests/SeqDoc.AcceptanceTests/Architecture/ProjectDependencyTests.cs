using System.Collections.Frozen;
using System.Xml.Linq;
using Xunit;

namespace SeqDoc.AcceptanceTests.Architecture;

public sealed class ProjectDependencyTests
{
    private static readonly FrozenDictionary<string, string[]> AllowedProductionReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SeqDoc.Core"] = [],
            ["SeqDoc.Application"] = ["SeqDoc.Core"],
            ["SeqDoc.Analysis.Roslyn"] = ["SeqDoc.Application", "SeqDoc.Core"],
            ["SeqDoc.Analysis.Behavior"] = ["SeqDoc.Application", "SeqDoc.Core"],
            ["SeqDoc.Analysis.Scenarios"] = ["SeqDoc.Core"],
            ["SeqDoc.FrameworkModels"] = ["SeqDoc.Core"],
            ["SeqDoc.Persistence.Sqlite"] = ["SeqDoc.Application", "SeqDoc.Core"],
            ["SeqDoc.Configuration"] = ["SeqDoc.Application", "SeqDoc.Core"],
            ["SeqDoc.Rendering.Markdown"] = ["SeqDoc.Core"],
            ["SeqDoc.Cli"] =
            [
                "SeqDoc.Analysis.Roslyn",
                "SeqDoc.Analysis.Behavior",
                "SeqDoc.Analysis.Scenarios",
                "SeqDoc.Application",
                "SeqDoc.Configuration",
                "SeqDoc.FrameworkModels",
                "SeqDoc.Persistence.Sqlite",
                "SeqDoc.Rendering.Markdown",
            ],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string[]> AllowedProductionPackages =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SeqDoc.Core"] = [],
            ["SeqDoc.Application"] = [],
            ["SeqDoc.Analysis.Roslyn"] =
            [
                "Microsoft.Build.Locator",
                "Microsoft.Build",
                "Microsoft.Build.Framework",
                "Microsoft.CodeAnalysis.CSharp.Workspaces",
                "Microsoft.CodeAnalysis.Workspaces.MSBuild",
            ],
            ["SeqDoc.Analysis.Behavior"] = [],
            ["SeqDoc.Analysis.Scenarios"] = [],
            ["SeqDoc.FrameworkModels"] = [],
            ["SeqDoc.Persistence.Sqlite"] = ["Microsoft.Data.Sqlite"],
            ["SeqDoc.Configuration"] = ["YamlDotNet"],
            ["SeqDoc.Rendering.Markdown"] = [],
            ["SeqDoc.Cli"] = ["Microsoft.Build.Framework", "System.CommandLine"],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    [Fact]
    public void ProductionProjectReferencesMatchApprovedDependencyGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceProjects = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedProductionReferences.Keys.Order(StringComparer.Ordinal),
            sourceProjects.Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal));

        foreach (var projectPath in sourceProjects)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var actualReferences = ReadProjectReferences(projectPath);

            Assert.Equal(AllowedProductionReferences[projectName].Order(StringComparer.Ordinal), actualReferences);
        }
    }

    [Fact]
    public void CoreHasNoPackageOrProjectReferences()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreProject = Path.Combine(repositoryRoot, "src", "SeqDoc.Core", "SeqDoc.Core.csproj");
        var document = XDocument.Load(coreProject);

        Assert.Empty(document.Descendants("PackageReference"));
        Assert.Empty(document.Descendants("ProjectReference"));
    }

    [Fact]
    public void ProductionPackageReferencesStayWithOwningAdapters()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceProjects = Directory.EnumerateFiles(
            Path.Combine(repositoryRoot, "src"),
            "*.csproj",
            SearchOption.AllDirectories);

        foreach (var projectPath in sourceProjects)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var allowedPackages = AllowedProductionPackages[projectName];
            var actualPackages = XDocument.Load(projectPath)
                .Descendants("PackageReference")
                .Where(reference => reference.Attribute("Include") is not null)
                .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty);

            Assert.All(actualPackages, package => Assert.Contains(package, allowedPackages));
        }
    }

    [Fact]
    public void ProductionProjectsDoNotReferenceTestProjects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceProjects = Directory.EnumerateFiles(
            Path.Combine(repositoryRoot, "src"),
            "*.csproj",
            SearchOption.AllDirectories);

        foreach (var projectPath in sourceProjects)
        {
            var references = XDocument.Load(projectPath)
                .Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty);

            Assert.DoesNotContain(references, reference => reference.Contains("tests", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void RenderingProjectNeverReferencesScenarioGraph()
    {
        // The renderer is a pure serializer of wording/diagram plans. It must never inspect Scenario
        // Graph types, so the whole rendering project is free of the ScenarioGraph vocabulary.
        string renderingRoot = Path.Combine(FindRepositoryRoot(), "src", "SeqDoc.Rendering.Markdown");
        var sources = Directory.EnumerateFiles(renderingRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("\\obj\\", StringComparison.Ordinal)
                && !path.Contains("\\bin\\", StringComparison.Ordinal));

        Assert.NotEmpty(sources);
        foreach (string source in sources)
        {
            Assert.DoesNotContain("ScenarioGraph", File.ReadAllText(source), StringComparison.Ordinal);
        }
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => Path.GetFileNameWithoutExtension(reference!))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
