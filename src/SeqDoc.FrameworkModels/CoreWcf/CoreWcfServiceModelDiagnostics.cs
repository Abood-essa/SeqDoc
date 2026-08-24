using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels.CoreWcf;

/// <summary>
/// Builds the deterministic diagnostics the CoreWCF/WCF service contract model emits when an admitted
/// pattern cannot produce an exact result. Identities derive from stable subjects supplied by the
/// model, never from encounter counts, so identical defects always produce identical diagnostic IDs.
/// </summary>
internal static class CoreWcfServiceModelDiagnostics
{
    internal const string EligibilityShapeUnavailableCode = "SEQWCF001";
    internal const string OperationImplementationUnavailableCode = "SEQWCF002";
    internal const string AmbiguousOperationImplementationCode = "SEQWCF003";
    internal const string DegradedInputCertaintyCode = "SEQWCF004";

    internal static AnalysisDiagnostic EligibilityShapeUnavailable(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(EligibilityShapeUnavailableCode, profileId, subjectId),
            EligibilityShapeUnavailableCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "Service contract operation eligibility shape is unavailable or incomplete.",
            new DiagnosticLocation("core wcf service operation", profileId),
            "The compiler-proven method/type shape and interface-member-implementation mapping required to establish service contract operation eligibility was not supplied, so an exact root cannot be proven and none is emitted.",
            "No service operation entry point was emitted for the unproven symbol.",
            "Project the controlled compiler-shape facts for the method and retry analysis.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic OperationImplementationUnavailable(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(OperationImplementationUnavailableCode, profileId, subjectId),
            OperationImplementationUnavailableCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "An admitted service contract operation has no compiler-proven source body.",
            new DiagnosticLocation("core wcf service operation", profileId),
            "The method implements an exact [OperationContract] interface member but the Program Index reports no source body for it (for example a generated or metadata-only client proxy), so its dispatch behavior cannot be proven.",
            "No service operation entry point was emitted for the bodyless implementation.",
            "Analyze the real source implementation of the service contract instead of a generated or metadata-only proxy.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic AmbiguousOperationImplementation(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(AmbiguousOperationImplementationCode, profileId, subjectId),
            AmbiguousOperationImplementationCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "A method implements more than one admitted service contract operation.",
            new DiagnosticLocation("core wcf service operation", profileId),
            "The method's exact interface-member-implementation mapping proves more than one admitted [ServiceContract]/[OperationContract] pair, so no single operation identity can be selected without guessing.",
            "No service operation entry point was emitted for the ambiguous implementation.",
            "Give each admitted service contract operation its own implementing method.",
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
            new DiagnosticLocation("core wcf service operation", profileId),
            "The symbol input was not Exact, so the emitted fact and model evidence carry the degraded certainty instead of being promoted to Exact.",
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
