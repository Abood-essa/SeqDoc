using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels.FusionCache;

/// <summary>
/// Builds the deterministic diagnostics the FusionCache <c>GetOrSetAsync</c> model emits when an
/// operation is recognizably the FusionCache family but the exact supported contract cannot be
/// proven. The stable code lives in Core (<see cref="FusionCacheDiagnosticCodes.UnsupportedShape"/>)
/// so the Scenario Graph builder can join the exact code and the exact diagnosed operation without
/// substring search and without depending on this model assembly. Every diagnostic carries the
/// canonical operation+reason detail built by
/// <see cref="FusionCacheDiagnosticCodes.UnsupportedShapeDetail"/> with the typed operation
/// identity, so the Scenario Graph builder binds SEQFC001 to the exact
/// <see cref="CallbackBoundaryFact.OuterInvocationOperation"/> and a foreign-operation diagnostic
/// never degrades an unrelated boundary. Identities derive from the stable operation identity and
/// the failure reason, never from encounter counts, so identical defects always produce identical
/// diagnostic IDs.
/// </summary>
public static class FusionCacheGetOrSetModelDiagnostics
{
    internal static AnalysisDiagnostic UnsupportedShape(
        CompilationProfileId profileId,
        OperationId operationId,
        string reason)
    {
        var operationDetail = FusionCacheDiagnosticCodes.UnsupportedShapeDetail(operationId, reason);
        return new AnalysisDiagnostic(
            CreateDiagnosticId(profileId, operationDetail),
            FusionCacheDiagnosticCodes.UnsupportedShape,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "The FusionCache GetOrSetAsync call has an unsupported shape; no cache-miss contract was admitted.",
            new DiagnosticLocation("fusion cache get-or-set", profileId),
            $"The operation is recognizably the FusionCache GetOrSetAsync family but the exact supported contract cannot be proven: {reason}.",
            "No FusionCache cache-miss fact was emitted; the call is never presented as supported cache work.",
            "Use the exact supported FusionCache 2.6.0 GetOrSetAsync overload with a key, an anonymous value factory, and an options callback, or supply the matching callback-boundary evidence.",
            CertaintyLevel.Exact,
            internalDetail: operationDetail);
    }

    /// <summary>
    /// Creates the deterministic diagnostic ID from the compilation profile and the already-canonical
    /// subject (the exact operation+reason detail). The caller supplies the canonical subject; this
    /// helper never re-formats, hashes, or parses it and never invents a subject from display text,
    /// so the Scenario Graph builder can bind the ID's subject to the same exact operation without
    /// a substring or summary search.
    /// </summary>
    private static DiagnosticId CreateDiagnosticId(CompilationProfileId profileId, string subjectId)
        => StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            FusionCacheDiagnosticCodes.UnsupportedShape,
            AnalysisStage.FrameworkModel,
            profileId,
            subjectId,
            Ordinal: 0));
}
