using System.Collections.Immutable;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Wording;

namespace SeqDoc.Rendering.Tests;

/// <summary>
/// Builds hand-authored wording documents and diagram plans so renderer and output-activation tests
/// run as small pure and medium-boundary layers without a compiler session. Evidence is
/// source-shaped and deterministic.
/// </summary>
internal static class PlanTestFactory
{
    internal static readonly EntryPointId EntryPoint = new("entry-point:v1:test");
    internal static readonly CompilationProfileId Profile = new("compilation-profile:v1:test");

    internal static DiagramPlan CreateDiagramPlan(string participantLabel = "Client", string messageLabel = "GET api/Test")
    {
        ImmutableArray<EvidenceRef> participantEvidence = [SourceEvidence("participant")];
        ImmutableArray<EvidenceRef> messageEvidence = [SourceEvidence("message")];
        ImmutableArray<EvidenceRef> branchEvidence = [SourceEvidence("branch")];
        return new DiagramPlan(
            EntryPoint,
            Profile,
            "GET api/Test",
            [
                new DiagramParticipant(
                    new DiagramPlanElementId("diagram-element:v1:participant:client"),
                    "client",
                    participantLabel,
                    DiagramParticipantKind.Client,
                    participantEvidence,
                    CertaintyLevel.Exact),
                new DiagramParticipant(
                    new DiagramPlanElementId("diagram-element:v1:participant:service"),
                    "service",
                    "GadgetService",
                    DiagramParticipantKind.Service,
                    participantEvidence,
                    CertaintyLevel.Exact),
            ],
            [
                new DiagramMessage(
                    new DiagramPlanElementId("diagram-element:v1:message:entry"),
                    "message:entry",
                    "client",
                    "service",
                    messageLabel,
                    DiagramMessageKind.Request,
                    messageEvidence,
                    CertaintyLevel.Exact),
                new DiagramMessage(
                    new DiagramPlanElementId("diagram-element:v1:message:ok"),
                    "message:ok",
                    "service",
                    "client",
                    "Ok -> HTTP 200",
                    DiagramMessageKind.Response,
                    messageEvidence,
                    CertaintyLevel.Exact),
            ],
            [
                new DiagramBranch(
                    new DiagramPlanElementId("diagram-element:v1:branch:success"),
                    "success",
                    "success path",
                    DiagramBranchKind.Success,
                    ["message:ok"],
                    branchEvidence,
                    CertaintyLevel.Exact),
            ],
            "diagram-plan:v1:test");
    }

    internal static WordingDocument CreateWordingDocument(string operationKey = "GET api/Test")
        => new(
            EntryPoint,
            Profile,
            operationKey,
            operationKey,
            [
                new WordingPhrase(
                    new WordingPhraseId("wording-phrase:v1:test:entry"),
                    "entry",
                    WordingPhraseKind.Statement,
                    $"HTTP entry point at route \"{operationKey}\".",
                    [SourceEvidence("entry")],
                    CertaintyLevel.Exact),
            ],
            "wording-document:v1:test");

    internal static EvidenceRef SourceEvidence(string artifact)
        => new(
            new EvidenceId($"evidence:v1:{artifact}"),
            EvidenceKind.Source,
            artifact,
            new SourceRange(
                new DocumentId("document:v1:test"),
                new SourcePosition(1, 0),
                new SourcePosition(1, 10)),
            "test-symbol",
            null,
            CertaintyLevel.Exact);
}
