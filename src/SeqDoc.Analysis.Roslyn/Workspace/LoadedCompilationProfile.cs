using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SeqDoc.Core.Identity;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Workspace;

internal sealed record LoadedProject(
    Project Project,
    Compilation Compilation,
    StableProjectId StableId,
    string RepositoryRelativePath);

internal sealed class LoadedCompilationProfile : IDisposable
{
    public LoadedCompilationProfile(
        MSBuildWorkspace workspace,
        ImmutableArray<LoadedProject> projects,
        ImmutableArray<SeqDoc.Core.Diagnostics.AnalysisDiagnostic> diagnostics)
    {
        Workspace = workspace;
        Projects = projects;
        Diagnostics = diagnostics;
    }

    public MSBuildWorkspace Workspace { get; }

    public ImmutableArray<LoadedProject> Projects { get; }

    public ImmutableArray<SeqDoc.Core.Diagnostics.AnalysisDiagnostic> Diagnostics { get; }

    public void Dispose() => Workspace.Dispose();
}
