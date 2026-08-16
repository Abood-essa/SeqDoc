using SeqDoc.Core.Behavior;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Application.Analysis;

/// <summary>Carries the evidence-backed Program Index and extracted behavior input into the analyzer.</summary>
public sealed record BehaviorAnalysisRequest(
    ProgramIndexSnapshot ProgramIndex,
    ExtractedBehaviorInput BehaviorInput);

/// <summary>
/// Consumes only Core-owned contracts and produces a validated behavior snapshot. Implementations
/// must remain free of Roslyn, MSBuild, SQLite, CLI, and renderer dependencies.
/// </summary>
public interface IBehaviorAnalyzer
{
    Task<ApplicationResult<BehaviorSnapshot>> AnalyzeAsync(
        BehaviorAnalysisRequest request,
        CancellationToken cancellationToken);
}
