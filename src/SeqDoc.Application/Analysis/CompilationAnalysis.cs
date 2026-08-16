using System.Collections.Immutable;
using SeqDoc.Application.Persistence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Application.Analysis;

/// <summary>
/// Identifies one restored repository target and its exact compilation profile. The optional
/// <see cref="RepositoryOwnedConfigurationFiles"/> inventory names the repository-relative
/// configuration files that may produce checked-in observations; a missing, default, or empty
/// inventory withholds those observations while reads, provider, and profile-known facts still
/// project. Ownership is explicit request authority only; the analyzer never invokes Git or infers
/// tracked status from filesystem presence or implicit SDK globs.
/// </summary>
public sealed record CompilationAnalysisRequest(
    string RepositoryRoot,
    string TargetPath,
    CompilationProfile Profile,
    ImmutableArray<string> RepositoryOwnedConfigurationFiles = default);

/// <summary>Describes a project that passed the authoritative compiler gate.</summary>
public sealed record CompiledProjectSummary(
    ProjectId Id,
    string Name,
    string RepositoryRelativePath,
    string AssemblyIdentity);

/// <summary>Reports the complete project graph that passed compilation validation.</summary>
public sealed record CompilationAnalysisSummary(
    CompilationProfile Profile,
    ImmutableArray<CompiledProjectSummary> Projects);

public interface ICompilationProfileAnalyzer
{
    Task<ApplicationResult<CompilationAnalysisSummary>> AnalyzeAsync(
        CompilationAnalysisRequest request,
        CancellationToken cancellationToken);
}

public interface IProgramIndexBuilder
{
    Task<ApplicationResult<ProgramIndexSnapshot>> BuildAsync(
        CompilationAnalysisRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Additive memory-only analysis candidate for one profile. The persisted snapshot is the unchanged
/// accepted aggregate; the semantic, framework, dependency-injection, structural-result, non-Get,
/// configuration, conditional dependency-injection, scenario-graph, and callback-boundary companions
/// sit beside it and are never persisted or activated.
/// </summary>
public sealed record AnalysisProfileCandidate(
    AnalysisProfileSnapshot Snapshot,
    SemanticFactSet SemanticFacts,
    FrameworkAnalysisResult FrameworkFacts,
    DependencyInjectionFactSet DependencyInjectionFacts,
    StructuralResultFactSet StructuralResultFacts,
    ScenarioGraphSet ScenarioGraphs,
    ConfigurationSemanticFactSet? ConfigurationSemanticFacts = null,
    ConditionalDependencyInjectionFactSet? ConditionalDependencyInjectionFacts = null,
    CallbackBoundaryFactSet? CallbackBoundaryFacts = null,
    PredicateSemanticFactSet? PredicateSemanticFacts = null);

/// <summary>
/// Builds the aggregate Program Index, behavior snapshot, and memory-only companion facts for one
/// profile. Implementations compose extraction, behavior analysis, and framework-model analysis
/// without leaking adapter types. The returned candidate is additive: callers persist only
/// <see cref="AnalysisProfileCandidate.Snapshot"/> and return companion inspection only after
/// activation succeeds.
/// </summary>
public interface IAnalysisBuilder
{
    Task<ApplicationResult<AnalysisProfileCandidate>> BuildAsync(
        CompilationAnalysisRequest request,
        CancellationToken cancellationToken);
}
