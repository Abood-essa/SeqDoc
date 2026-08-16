using System.Collections.Immutable;
using SeqDoc.Core.Identity;

namespace SeqDoc.Application.Analysis;

public sealed record CompilationProfileResolutionRequest(
    string RepositoryRoot,
    string TargetPath,
    string Configuration,
    string? TargetFramework = null,
    bool AllTargetFrameworks = false,
    string? RuntimeIdentifier = null,
    ImmutableSortedDictionary<string, string>? MsBuildProperties = null,
    ImmutableSortedDictionary<string, string>? AnalysisProperties = null,
    int MaxParallelism = 1);

public sealed record ResolvedCompilationProfiles(
    ImmutableArray<string> AvailableTargetFrameworks,
    ImmutableArray<CompilationProfile> Profiles,
    string ToolchainVersion);

public interface ICompilationProfileResolver
{
    Task<ApplicationResult<ResolvedCompilationProfiles>> ResolveAsync(
        CompilationProfileResolutionRequest request,
        CancellationToken cancellationToken);
}
