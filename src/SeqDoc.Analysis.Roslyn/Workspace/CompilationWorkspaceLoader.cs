using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SeqDoc.Analysis.Roslyn.Diagnostics;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Workspace;

internal static class CompilationWorkspaceLoader
{
    public static async Task<(LoadedCompilationProfile? Loaded, ImmutableArray<SeqDoc.Core.Diagnostics.AnalysisDiagnostic> Diagnostics)>
        LoadAsync(CompilationAnalysisRequest request, CancellationToken cancellationToken)
    {
        var properties = MsBuildGlobalProperties.CreateForWorkspace(request.Profile);
        var workspace = MSBuildWorkspace.Create(properties);
        var workspaceDiagnostics = new ConcurrentQueue<WorkspaceDiagnostic>();
        workspace.RegisterWorkspaceFailedHandler(args => workspaceDiagnostics.Enqueue(args.Diagnostic));

        try
        {
            Solution solution;
            Microsoft.CodeAnalysis.ProjectId? rootProjectId = null;
            var extension = Path.GetExtension(request.TargetPath);
            if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await workspace.OpenProjectAsync(
                    request.TargetPath,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                solution = project.Solution;
                rootProjectId = project.Id;
            }
            else
            {
                solution = await workspace.OpenSolutionAsync(
                    request.TargetPath,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            var loadedProjects = ImmutableArray.CreateBuilder<LoadedProject>();
            var compilerErrors = new List<(Diagnostic Diagnostic, StableProjectId Project)>();
            foreach (var project in SelectProjects(solution, rootProjectId)
                         .OrderBy(project => project.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.FilePath is null)
                {
                    workspaceDiagnostics.Enqueue(new WorkspaceDiagnostic(
                        WorkspaceDiagnosticKind.Failure,
                        $"Project '{project.Name}' has no physical project path."));
                    continue;
                }

                var relativePath = ToRepositoryRelativePath(request.RepositoryRoot, project.FilePath);
                var stableId = StableIdentity.CreateProjectId(request.Profile.Id, relativePath);
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation is null)
                {
                    workspaceDiagnostics.Enqueue(new WorkspaceDiagnostic(
                        WorkspaceDiagnosticKind.Failure,
                        $"Project '{project.Name}' did not produce a C# compilation."));
                    continue;
                }

                compilerErrors.AddRange(compilation.GetDiagnostics(cancellationToken)
                    .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(diagnostic => (diagnostic, stableId)));
                loadedProjects.Add(new LoadedProject(project, compilation, stableId, relativePath));
            }

            var warningPolicy = new MsBuildWarningPolicy(properties);
            var convertedWorkspaceDiagnostics = CompilerDiagnosticFactory.CreateWorkspace(
                workspaceDiagnostics,
                request.Profile.Id,
                warningPolicy.IsPromoted);
            var convertedCompilerDiagnostics = CompilerDiagnosticFactory.CreateCompiler(
                compilerErrors,
                request.Profile.Id);
            var diagnostics = convertedWorkspaceDiagnostics
                .AddRange(convertedCompilerDiagnostics)
                .OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray();

            if (WorkspaceDiagnosticClassifier.HasFailure(workspaceDiagnostics, warningPolicy.IsPromoted)
                || compilerErrors.Count > 0)
            {
                workspace.Dispose();
                return (null, diagnostics);
            }

            return (new LoadedCompilationProfile(workspace, loadedProjects.ToImmutable(), diagnostics), diagnostics);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    internal static IEnumerable<Project> SelectProjects(
        Solution solution,
        Microsoft.CodeAnalysis.ProjectId? rootProjectId)
    {
        if (rootProjectId is null)
        {
            return solution.Projects
                .Where(project => string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal));
        }

        var selected = new HashSet<Microsoft.CodeAnalysis.ProjectId>();
        var pending = new Stack<Microsoft.CodeAnalysis.ProjectId>();
        pending.Push(rootProjectId);
        while (pending.TryPop(out var projectId))
        {
            if (!selected.Add(projectId) || solution.GetProject(projectId) is not { } project)
            {
                continue;
            }

            foreach (var reference in project.ProjectReferences)
            {
                pending.Push(reference.ProjectId);
            }
        }

        return selected
            .Select(solution.GetProject)
            .Where(project => project is not null
                && string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
            .Select(project => project!);
    }

    private static string ToRepositoryRelativePath(string repositoryRoot, string path)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, path);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Loaded project '{path}' is outside the selected repository root.");
        }

        return RepositoryRelativePath.Normalize(relativePath);
    }
}
