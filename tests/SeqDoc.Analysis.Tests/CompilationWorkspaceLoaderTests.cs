using Microsoft.CodeAnalysis;
using SeqDoc.Analysis.Roslyn.Workspace;
using Xunit;

namespace SeqDoc.Analysis.Tests;

public sealed class CompilationWorkspaceLoaderTests
{
    [Fact]
    public void ProjectTargetSelectsOnlyTransitiveCompilationReferences()
    {
        using var workspace = new AdhocWorkspace();
        var dependency = workspace.AddProject("Dependency", LanguageNames.CSharp);
        var analyzerOnly = workspace.AddProject("AnalyzerOnly", LanguageNames.CSharp);
        var root = workspace.AddProject("Root", LanguageNames.CSharp)
            .AddProjectReference(new ProjectReference(dependency.Id));
        Assert.True(workspace.TryApplyChanges(root.Solution));

        var selected = CompilationWorkspaceLoader.SelectProjects(workspace.CurrentSolution, root.Id)
            .Select(project => project.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Dependency", "Root"], selected);
        Assert.DoesNotContain(analyzerOnly.Name, selected);
    }

    [Fact]
    public void SolutionTargetSelectsEveryCSharpProject()
    {
        using var workspace = new AdhocWorkspace();
        workspace.AddProject("First", LanguageNames.CSharp);
        workspace.AddProject("Second", LanguageNames.CSharp);

        var selected = CompilationWorkspaceLoader.SelectProjects(workspace.CurrentSolution, null)
            .Select(project => project.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["First", "Second"], selected);
    }
}
