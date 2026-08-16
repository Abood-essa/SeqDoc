using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels.EntityFramework;

/// <summary>
/// Builds the deterministic diagnostics the Entity Framework query model emits when an admitted
/// pattern cannot produce an exact query fact. Identities derive from stable subjects supplied by
/// the model, never from encounter counts, so identical defects always produce identical diagnostic
/// IDs.
/// </summary>
public static class EntityFrameworkQueryModelDiagnostics
{
    public const string UnsupportedQueryChainCode = "SEQEF001";
    public const string NonEqualityPredicateCode = "SEQEF002";
    public const string DegradedInputCertaintyCode = "SEQEF003";

    internal static AnalysisDiagnostic UnsupportedQueryChain(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(UnsupportedQueryChainCode, profileId, subjectId),
            UnsupportedQueryChainCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "An admitted EF terminal has an unsupported receiver chain.",
            new DiagnosticLocation("entity framework query", profileId),
            "The SingleOrDefaultAsync terminal was reached but its compiler-proven receiver chain is not exactly ordered AsNoTracking and Include steps over an admitted DbSet, so the query meaning cannot be proven.",
            "No EF query fact was emitted; the query is never presented as supported.",
            "Use a DbSet receiver with only AsNoTracking and ordered Include steps before SingleOrDefaultAsync.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic NonEqualityPredicate(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(NonEqualityPredicateCode, profileId, subjectId),
            NonEqualityPredicateCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "The SingleOrDefaultAsync predicate is not an exact equality comparison.",
            new DiagnosticLocation("entity framework query", profileId),
            "The predicate body is not a compiler-proven equality comparison, so the lookup key cannot be linked to a comparison semantic fact.",
            "No EF query fact was emitted; a non-equality lookup is never invented as equality.",
            "Use an equality predicate such as item => item.Id == id for the admitted query shape.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic DegradedInputCertainty(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(DegradedInputCertaintyCode, profileId, subjectId),
            DegradedInputCertaintyCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "Non-exact input certainty degraded framework facts.",
            new DiagnosticLocation("entity framework query", profileId),
            "The operation input was not Exact, so emitted facts and model evidence carry the degraded certainty instead of being promoted to Exact.",
            "Documentation distinguishes degraded facts from exact compiler-proven facts.",
            "Provide exact compiler-proven input to restore Exact certainty.",
            CertaintyLevel.Exact);
    }

    private static DiagnosticId CreateDiagnosticId(string code, CompilationProfileId profileId, string subjectId)
    {
        return StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.FrameworkModel,
            profileId,
            subjectId,
            Ordinal: 0));
    }
}
