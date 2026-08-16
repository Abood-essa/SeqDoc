using System.Collections.Immutable;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Application.Persistence;

public sealed record ProgramIndexPersistenceRequest(ImmutableArray<ProgramIndexSnapshot> Snapshots);

public sealed record ActivatedProfileRun(
    CompilationProfileId ProfileId,
    AnalysisRunId RunId,
    string IndexFingerprint);

public sealed record ProgramIndexActivation(ImmutableArray<ActivatedProfileRun> Runs);

public sealed record ActiveProgramIndex(AnalysisRunId RunId, ProgramIndexSnapshot Snapshot);

public sealed record ActiveProgramIndexLookup(bool Found, ActiveProgramIndex? ActiveIndex);

public sealed record ActiveProgramIndexes(ImmutableArray<ActiveProgramIndex> Indexes);

public interface IProgramIndexStore
{
    Task<ApplicationResult<ProgramIndexActivation>> ActivateAsync(
        ProgramIndexPersistenceRequest request,
        CancellationToken cancellationToken);

    Task<ApplicationResult<ActiveProgramIndexLookup>> ReadActiveAsync(
        CompilationProfileId profileId,
        CancellationToken cancellationToken);

    Task<ApplicationResult<ActiveProgramIndexes>> ReadAllActiveAsync(CancellationToken cancellationToken);
}
