using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels;

/// <summary>
/// Builds the deterministic diagnostics the host emits when a model violates the artifact contract.
/// The codes are model boundary codes, distinct from codes used by concrete framework models. Every
/// identity is derived from the stable subject the host supplies, never from encounter counts, so
/// identical defects always produce identical diagnostic IDs.
/// </summary>
internal static class FrameworkModelDiagnostics
{
    internal const string FactWithoutEvidenceCode = "SEQFW001";
    internal const string ArtifactWithoutEvidenceCode = "SEQFW002";
    internal const string ConflictingFactCode = "SEQFW003";
    internal const string InvalidArtifactValueCode = "SEQFW004";
    internal const string ArtifactProducerMismatchCode = "SEQFW005";

    internal static AnalysisDiagnostic FactWithoutEvidence(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(FactWithoutEvidenceCode, profileId, subjectId),
            FactWithoutEvidenceCode,
            DiagnosticSeverity.Error,
            AnalysisStage.FrameworkModel,
            "A framework model emitted a behavior fact without required evidence.",
            new DiagnosticLocation("framework model result", profileId),
            "A model returned a behavior fact whose evidence collection is empty or unset.",
            "The invalid fact was excluded from the aggregate so documentation never presents unsupported behavior as proven.",
            "Fix the producing model so every emitted fact retains non-empty direct evidence.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic ArtifactWithoutEvidence(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(ArtifactWithoutEvidenceCode, profileId, subjectId),
            ArtifactWithoutEvidenceCode,
            DiagnosticSeverity.Error,
            AnalysisStage.FrameworkModel,
            "A framework model emitted a resolution, suppression, or summary artifact without required evidence.",
            new DiagnosticLocation("framework model result", profileId),
            "A model returned a hint or rule whose evidence collection is empty or unset.",
            "The invalid artifact was excluded from the aggregate so later passes do not apply unproven guidance.",
            "Fix the producing model so every emitted hint and rule retains non-empty evidence.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic ConflictingFact(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(ConflictingFactCode, profileId, subjectId),
            ConflictingFactCode,
            DiagnosticSeverity.Error,
            AnalysisStage.FrameworkModel,
            "A framework model emitted conflicting payloads for one behavior-fact identity.",
            new DiagnosticLocation("framework model result", profileId),
            "Two facts shared one behavior-fact ID but carried different payloads, so no single payload is trustworthy.",
            "The ambiguous fact was excluded from the aggregate so documentation never presents a randomly chosen payload as proven.",
            "Fix the producing model so each behavior-fact identity is emitted with one consistent payload.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic InvalidArtifactValue(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(InvalidArtifactValueCode, profileId, subjectId),
            InvalidArtifactValueCode,
            DiagnosticSeverity.Error,
            AnalysisStage.FrameworkModel,
            "A framework model emitted a resolution, suppression, or summary artifact with an invalid semantic value.",
            new DiagnosticLocation("framework model result", profileId),
            "A hint or rule had an absent target, a blank source operation, reason, or scope, or a negative ordinal.",
            "The invalid artifact was excluded from the aggregate so later passes do not apply contradictory guidance.",
            "Fix the producing model so every emitted hint and rule carries a target, non-blank semantics, a non-negative ordinal, and evidence.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic ArtifactProducerMismatch(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(ArtifactProducerMismatchCode, profileId, subjectId),
            ArtifactProducerMismatchCode,
            DiagnosticSeverity.Error,
            AnalysisStage.FrameworkModel,
            "A framework model emitted an artifact whose evidence does not match the producing model.",
            new DiagnosticLocation("framework model result", profileId),
            "A model-derived fact, hint, or rule carried evidence that is not FrameworkModel evidence naming the producing model, or its underlying provenance lacked direct source or generated-source evidence.",
            "The invalid artifact was excluded from the aggregate so no fact claims a producer it did not come from.",
            "Fix the producing model so every emitted artifact retains FrameworkModel evidence matching its own descriptor with direct source or generated-source provenance.",
            CertaintyLevel.Exact);
    }

    private static DiagnosticId CreateDiagnosticId(string code, CompilationProfileId profileId, string? subjectId)
    {
        return StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.FrameworkModel,
            profileId,
            subjectId,
            Ordinal: 0));
    }
}
