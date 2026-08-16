using System.Collections.Immutable;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Application.Persistence;

/// <summary>Carries the Program Index and optional behavior snapshot for one profile run.</summary>
public sealed record AnalysisProfileSnapshot(
    ProgramIndexSnapshot ProgramIndex,
    BehaviorSnapshot? Behavior);

public sealed record AnalysisPersistenceRequest(ImmutableArray<AnalysisProfileSnapshot> Snapshots);

/// <summary>Reports the result of activating a set of aggregate profile runs.</summary>
public sealed record AnalysisActivation(ImmutableArray<ActivatedProfileRun> Runs);

/// <summary>Carries one active run's Program Index and behavior snapshot when available.</summary>
public sealed record ActiveAnalysisProfile(
    AnalysisRunId RunId,
    ProgramIndexSnapshot ProgramIndex,
    BehaviorSnapshot? Behavior);

public sealed record ActiveAnalysisLookup(bool Found, ActiveAnalysisProfile? ActiveProfile);

public sealed record ActiveAnalyses(ImmutableArray<ActiveAnalysisProfile> Profiles);

/// <summary>
/// Persists the aggregate Program Index and behavior snapshot per profile run and activates all
/// selected profiles atomically. The active-run pointer remains the single activation authority.
/// </summary>
public interface IAnalysisStore
{
    Task<ApplicationResult<AnalysisActivation>> ActivateAsync(
        AnalysisPersistenceRequest request,
        CancellationToken cancellationToken);

    Task<ApplicationResult<ActiveAnalysisLookup>> ReadActiveAsync(
        CompilationProfileId profileId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<ActiveAnalyses>> ReadAllActiveAsync(CancellationToken cancellationToken);
}
