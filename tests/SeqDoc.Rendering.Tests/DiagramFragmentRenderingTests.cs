using System.Collections.Immutable;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

/// <summary>
/// accepted contract contract coverage for mechanical Mermaid serialization of the structured Diagram Plan sequence
/// tree. These tests define the expected renderer contract (from contract stage accepted contract "Required Model"):
///
/// - Mermaid rendering is a depth-first serialization of <c>DiagramPlan.Sequence</c> in exact
///   planner order with deterministic two-space indentation per fragment level (base 4 spaces: a
///   fragment opener/closer at depth d sits at 4 + 2*(d-1) and its contents at 4 + 2*d).
/// - The sequence is ONE ordered element vocabulary (message ref or fragment), not parallel arrays:
///   <c>DiagramSequence.Elements</c> holds <c>DiagramSequenceElement.MessageRef(id)</c> and
///   <c>DiagramSequenceElement.Fragment(fragment)</c> in exact chronology; the renderer serializes
///   message-before, fragment, and message-after in that order and never moves messages ahead of
///   fragments. The legacy two-array <c>MessageRefs</c>/<c>Fragments</c> construction remains for
///   source compatibility and maps to the degenerate messages-then-fragments element order.
/// - An Alt fragment emits <c>alt {fragment.Label}</c> for the single leading non-else arm,
///   <c>else {arm.Label}</c> for every explicit <c>IsElse</c> arm, and one <c>end</c>. Break
///   fragments emit <c>break {label}</c>, the canonical termination note
///   (<c>Note over {first}[,{last}]: Path terminates</c>) when the Break is empty, then <c>end</c>.
///   Loop fragments emit <c>loop {label}</c> ... <c>end</c>.
/// - accepted contract label alignment: the nested exact-byte sample uses the sentence-case technical labels
///   ("Condition"/"Continue") the planner now emits for decisions and arms without a unique typed
///   terminal; renderer structure, indentation, and escaping are unchanged.
/// - Reference coverage fails closed: an unresolved ref, a duplicated ref, or a planned message
///   that no element references throws before/at render instead of being silently skipped or
///   repeated; the exact one-reference-per-message plan renders and validates.
/// - Fragment labels use the existing quote/newline escaping (embedded quotes become <c>#quot;</c>,
///   newlines collapse to a single space) so no label ever creates a second line or a raw quote, and
///   backticks remain fence-safe inside a ```mermaid Markdown fence.
/// - Legacy topology-empty plans (empty <c>Sequence</c>) keep the accepted flat
///   <c>DiagramBranch</c> alt/else output byte-stable.
///
/// The F1 chronology and F3 coverage tests compile only against the reviewed accepted contract contract; the
/// missing <c>DiagramSequenceElement</c> vocabulary is the intentionally absent F1 contract, not
/// test setup.
/// </summary>
public sealed class DiagramFragmentRenderingTests
{
    private const string LoopMermaid =
        "sequenceDiagram\n" +
        "    participant client as \"Client\"\n" +
        "    participant service as \"GadgetService\"\n" +
        "    client->>service: GET api/Test\n" +
        "    loop Retry\n" +
        "      service-->>client: Ok -> HTTP 200\n" +
        "    end";

    private const string InterleavedChronologyMermaid =
        "sequenceDiagram\n" +
        "    participant client as \"Client\"\n" +
        "    participant service as \"GadgetService\"\n" +
        "    client->>service: GET api/Test\n" +
        "    opt Guard\n" +
        "      service-->>client: Inside -> HTTP 200\n" +
        "    end\n" +
        "    service-->>client: Ok -> HTTP 200";

    private const string NestedAltMermaid =
        "sequenceDiagram\n" +
        "    participant client as \"Client\"\n" +
        "    participant service as \"GadgetService\"\n" +
        "    alt Condition\n" +
        "      break Condition\n" +
        "        Note over client,service: Path terminates\n" +
        "      end\n" +
        "    else Continue\n" +
        "      alt Condition\n" +
        "        break Condition\n" +
        "          Note over client,service: Path terminates\n" +
        "        end\n" +
        "      else Continue\n" +
        "        service-->>client: Ok -> HTTP 200\n" +
        "      end\n" +
        "    end";

    private const string LegacyFlatMermaid =
        "sequenceDiagram\n" +
        "    participant client as \"Client\"\n" +
        "    participant service as \"GadgetService\"\n" +
        "    client->>service: GET api/Test\n" +
        "    alt success path\n" +
        "        service-->>client: Ok -> HTTP 200\n" +
        "    end";

    [Fact]
    public void ExactPreplannedLoopSerializesAndValidates()
    {
        string mermaid = MermaidRenderer.Render(CreateLoopPlan());

        Assert.Equal(LoopMermaid, mermaid);
        Assert.Empty(MermaidValidator.Validate(mermaid));
    }

    [Fact]
    public void NestedAltElseTreeSerializesDepthFirstWithTwoSpaceIndentation()
    {
        string mermaid = MermaidRenderer.Render(CreateNestedAltPlan());

        Assert.Equal(NestedAltMermaid, mermaid);
        Assert.Empty(MermaidValidator.Validate(mermaid));
    }

    [Fact]
    public void FragmentLabelsEscapeQuotesCollapseNewlinesAndStayMarkdownFenceSafe()
    {
        var plan = CreateLoopPlan(label: "Retry \"max\"\r\nn=3 ``` tick");
        string mermaid = MermaidRenderer.Render(plan);

        // Existing quote/newline escaping: embedded quotes become #quot; and newlines collapse to a
        // single space, so the whole label stays on one canonical line.
        Assert.DoesNotContain("\r", mermaid, StringComparison.Ordinal);
        Assert.Contains("loop Retry #quot;max#quot; n=3 ``` tick", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("\"max\"", mermaid, StringComparison.Ordinal);
        Assert.Empty(MermaidValidator.Validate(mermaid));

        // Markdown-fence safety: embedding the diagram inside a ```mermaid fence keeps exactly the
        // two fence lines (the opening "```mermaid" and the closing "```"); a backtick inside a
        // label line can never close the fence.
        string fenced = "```mermaid\n" + mermaid + "\n```";
        Assert.Equal(2, fenced.Split('\n').Count(line => line is "```" or "```mermaid"));
    }

    [Fact]
    public void RenderingIsDeterministicForEqualFragmentPlans()
    {
        // Equal independently built fragment plans produce identical Mermaid bytes; combined with the
        // planner-level reversed-topology equality (FragmentPlannerTests) this proves reversed
        // construction yields identical Mermaid output.
        string first = MermaidRenderer.Render(CreateNestedAltPlan());
        string second = MermaidRenderer.Render(CreateNestedAltPlan());

        Assert.Equal(first, second);
    }

    [Fact]
    public void LegacyDiagramPlanKeepsEmptySequenceAndByteStableFlatOutput()
    {
        var plan = PlanTestFactory.CreateDiagramPlan();

        // Compatibility boundary: legacy 7-argument construction yields a non-null empty sequence
        // and empty diagnostics, and the accepted flat DiagramBranch alt/else output stays byte
        // stable.
        Assert.NotNull(plan.Sequence);
        Assert.Empty(plan.Sequence.MessageRefs);
        Assert.Empty(plan.Sequence.Fragments);
        Assert.Empty(plan.Diagnostics);

        string mermaid = MermaidRenderer.Render(plan);
        Assert.Equal(LegacyFlatMermaid, mermaid);
        Assert.Empty(MermaidValidator.Validate(mermaid));
    }

    [Fact]
    public void FragmentRecordsEnforceEvidenceCertaintyAndClosedKindInvariants()
    {
        var exact = PlanTestFactory.SourceEvidence("fragment");
        var conservative = ConservativeEvidence("fragment");

        Assert.ThrowsAny<ArgumentException>(() => new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:test"),
            "fragment:test",
            "Test",
            (DiagramFragmentKind)99,
            [],
            [],
            [],
            [exact],
            CertaintyLevel.Exact));
        Assert.ThrowsAny<ArgumentException>(() => new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:test"),
            "fragment:test",
            "Test",
            DiagramFragmentKind.Loop,
            [],
            [],
            [],
            [],
            CertaintyLevel.Exact));
        Assert.ThrowsAny<ArgumentException>(() => new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:test"),
            "fragment:test",
            "Test",
            DiagramFragmentKind.Loop,
            [],
            [],
            [],
            [exact],
            CertaintyLevel.Unknown));
        Assert.ThrowsAny<ArgumentException>(() => new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:test"),
            "fragment:test",
            "Test",
            DiagramFragmentKind.Loop,
            [],
            [],
            [],
            [conservative],
            CertaintyLevel.Exact));
        Assert.ThrowsAny<ArgumentException>(() => new DiagramFragment(
            new DiagramPlanElementId(""),
            "fragment:test",
            "Test",
            DiagramFragmentKind.Loop,
            [],
            [],
            [],
            [exact],
            CertaintyLevel.Exact));

        Assert.ThrowsAny<ArgumentException>(() => new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:test"),
            "fragment:test:arm",
            "Arm",
            isElse: true,
            messageRefs: [],
            fragments: [],
            evidence: [],
            certainty: CertaintyLevel.Exact));
        Assert.ThrowsAny<ArgumentException>(() => new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:test"),
            "fragment:test:arm",
            "Arm",
            isElse: true,
            messageRefs: [],
            fragments: [],
            evidence: [exact],
            certainty: CertaintyLevel.Unknown));
        Assert.ThrowsAny<ArgumentException>(() => new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:test"),
            "fragment:test:arm",
            "Arm",
            isElse: true,
            messageRefs: [],
            fragments: [],
            evidence: [conservative],
            certainty: CertaintyLevel.Exact));
    }

    [Fact]
    public void OrderedSequenceSerializesMessageFragmentMessageInExactChronology()
    {
        // F1: the sequence is one ordered element vocabulary (message ref or fragment), not
        // parallel arrays. Message-before, the fragment, and message-after must serialize in exact
        // chronological order; the renderer never moves the continuation message ahead of the
        // decision fragment.
        var plan = CreateInterleavedChronologyPlan();
        string mermaid = MermaidRenderer.Render(plan);

        Assert.Equal(InterleavedChronologyMermaid, mermaid);
        Assert.Empty(MermaidValidator.Validate(mermaid));

        int before = mermaid.IndexOf("GET api/Test", StringComparison.Ordinal);
        int fragment = mermaid.IndexOf("opt Guard", StringComparison.Ordinal);
        int after = mermaid.IndexOf("Ok -> HTTP 200", StringComparison.Ordinal);
        Assert.True(before >= 0 && before < fragment && fragment < after,
            "Message-before, the fragment, and message-after must serialize in exact chronological order.");
    }

    [Fact]
    public void MissingMessageRefFailsClosedBeforeOrAtRender()
    {
        // F3: a sequence reference that does not resolve to a planned message must fail closed
        // instead of being silently skipped.
        Assert.ThrowsAny<Exception>(() =>
        {
            var plan = CreateLoopPlanWithRefs([EntryMessage().Id, MissingMessage()]);
            _ = MermaidRenderer.Render(plan);
        });
    }

    [Fact]
    public void DuplicateMessageRefFailsClosedBeforeOrAtRender()
    {
        // F3: a planned message referenced more than once must fail closed instead of rendering
        // repeatedly.
        Assert.ThrowsAny<Exception>(() =>
        {
            var plan = CreateLoopPlanWithRefs([EntryMessage().Id, EntryMessage().Id]);
            _ = MermaidRenderer.Render(plan);
        });
    }

    [Fact]
    public void OmittedMessageCoverageFailsClosedBeforeOrAtRender()
    {
        // F3: exact one-reference-per-message coverage means a planned message that no sequence
        // element references must fail closed instead of being silently dropped.
        Assert.ThrowsAny<Exception>(() =>
        {
            var plan = CreatePlanWithOmittedMessage();
            _ = MermaidRenderer.Render(plan);
        });
    }

    private static DiagramPlan CreateLoopPlan(string label = "Retry")
    {
        var fragmentEvidence = PlanTestFactory.SourceEvidence("fragment");
        var loop = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:loop:retry"),
            "loop:retry",
            label,
            DiagramFragmentKind.Loop,
            [],
            [OkMessage().Id],
            [],
            [fragmentEvidence],
            CertaintyLevel.Exact);

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            Participants(),
            [EntryMessage(), OkMessage()],
            [],
            "diagram-plan:v1:fragment-loop",
            new DiagramSequence([EntryMessage().Id], [loop]),
            []);
    }

    /// <summary>
    /// Loop-shaped plan whose sequence-level message refs are caller-supplied, used by the F3
    /// fail-closed coverage tests. The loop body always references the Ok message so only the
    /// supplied refs are malformed.
    /// </summary>
    private static DiagramPlan CreateLoopPlanWithRefs(ImmutableArray<DiagramPlanElementId> sequenceRefs)
    {
        var fragmentEvidence = PlanTestFactory.SourceEvidence("fragment");
        var loop = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:loop:retry"),
            "loop:retry",
            "Retry",
            DiagramFragmentKind.Loop,
            [],
            [OkMessage().Id],
            [],
            [fragmentEvidence],
            CertaintyLevel.Exact);

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            Participants(),
            [EntryMessage(), OkMessage()],
            [],
            "diagram-plan:v1:fragment-refs",
            new DiagramSequence(sequenceRefs, [loop]),
            []);
    }

    /// <summary>Plan whose Ok message is never referenced by any sequence element.</summary>
    private static DiagramPlan CreatePlanWithOmittedMessage()
    {
        var fragmentEvidence = PlanTestFactory.SourceEvidence("fragment");
        var loop = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:loop:retry"),
            "loop:retry",
            "Retry",
            DiagramFragmentKind.Loop,
            [],
            [EntryMessage().Id],
            [],
            [fragmentEvidence],
            CertaintyLevel.Exact);

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            Participants(),
            [EntryMessage(), OkMessage()],
            [],
            "diagram-plan:v1:fragment-omitted",
            new DiagramSequence([], [loop]),
            []);
    }

    /// <summary>
    /// F1 interleaved chronology plan: the sequence is one ordered element list (message ref,
    /// fragment, message ref) so the continuation message renders after the decision fragment.
    /// </summary>
    private static DiagramPlan CreateInterleavedChronologyPlan()
    {
        var fragmentEvidence = PlanTestFactory.SourceEvidence("fragment");
        var guard = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:opt:guard"),
            "decision:operation:v1:decision.Guard",
            "Guard",
            DiagramFragmentKind.Opt,
            [],
            [InsideMessage().Id],
            [],
            [fragmentEvidence],
            CertaintyLevel.Exact);

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            Participants(),
            [EntryMessage(), InsideMessage(), OkMessage()],
            [],
            "diagram-plan:v1:fragment-interleaved",
            new DiagramSequence(
            [
                DiagramSequenceElement.MessageRef(EntryMessage().Id),
                DiagramSequenceElement.Fragment(guard),
                DiagramSequenceElement.MessageRef(OkMessage().Id),
            ]),
            []);
    }

    private static DiagramPlan CreateNestedAltPlan()
    {
        var fragmentEvidence = PlanTestFactory.SourceEvidence("fragment");
        var breakAbsent = Break(
            "diagram-element:v1:fragment:break:absent",
            "decision:operation:v1:decision.WorkItemAbsent:arm:true:break");
        var breakLocked = Break(
            "diagram-element:v1:fragment:break:locked",
            "decision:operation:v1:decision.WorkItemLocked:arm:true:break");

        var locked = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:alt:locked"),
            "decision:operation:v1:decision.WorkItemLocked",
            "Condition",
            DiagramFragmentKind.Alt,
            [
                new DiagramAltArm(
                    new DiagramPlanElementId("diagram-element:v1:arm:locked:true"),
                    "decision:operation:v1:decision.WorkItemLocked:arm:true",
                    "Condition",
                    isElse: false,
                    messageRefs: [],
                    fragments: [breakLocked],
                    evidence: [fragmentEvidence],
                    certainty: CertaintyLevel.Exact),
                new DiagramAltArm(
                    new DiagramPlanElementId("diagram-element:v1:arm:locked:false"),
                    "decision:operation:v1:decision.WorkItemLocked:arm:false",
                    "Continue",
                    isElse: true,
                    messageRefs: [OkMessage().Id],
                    fragments: [],
                    evidence: [fragmentEvidence],
                    certainty: CertaintyLevel.Exact),
            ],
            [],
            [],
            [fragmentEvidence],
            CertaintyLevel.Exact);

        var absent = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:alt:absent"),
            "decision:operation:v1:decision.WorkItemAbsent",
            "Condition",
            DiagramFragmentKind.Alt,
            [
                new DiagramAltArm(
                    new DiagramPlanElementId("diagram-element:v1:arm:absent:true"),
                    "decision:operation:v1:decision.WorkItemAbsent:arm:true",
                    "Condition",
                    isElse: false,
                    messageRefs: [],
                    fragments: [breakAbsent],
                    evidence: [fragmentEvidence],
                    certainty: CertaintyLevel.Exact),
                new DiagramAltArm(
                    new DiagramPlanElementId("diagram-element:v1:arm:absent:false"),
                    "decision:operation:v1:decision.WorkItemAbsent:arm:false",
                    "Continue",
                    isElse: true,
                    messageRefs: [],
                    fragments: [locked],
                    evidence: [fragmentEvidence],
                    certainty: CertaintyLevel.Exact),
            ],
            [],
            [],
            [fragmentEvidence],
            CertaintyLevel.Exact);

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            Participants(),
            [OkMessage()],
            [],
            "diagram-plan:v1:fragment-nested",
            new DiagramSequence([], [absent]),
            []);
    }

    private static DiagramFragment Break(string id, string key)
        => new(
            new DiagramPlanElementId(id),
            key,
            "Condition",
            DiagramFragmentKind.Break,
            [],
            [],
            [],
            [PlanTestFactory.SourceEvidence("fragment")],
            CertaintyLevel.Exact);

    private static ImmutableArray<DiagramParticipant> Participants()
    {
        var evidence = PlanTestFactory.SourceEvidence("participant");
        return
        [
            new DiagramParticipant(
                new DiagramPlanElementId("diagram-element:v1:participant:client"),
                "client",
                "Client",
                DiagramParticipantKind.Client,
                [evidence],
                CertaintyLevel.Exact),
            new DiagramParticipant(
                new DiagramPlanElementId("diagram-element:v1:participant:service"),
                "service",
                "GadgetService",
                DiagramParticipantKind.Service,
                [evidence],
                CertaintyLevel.Exact),
        ];
    }

    private static DiagramMessage EntryMessage()
        => new(
            new DiagramPlanElementId("diagram-element:v1:message:entry"),
            "message:entry",
            "client",
            "service",
            "GET api/Test",
            DiagramMessageKind.Request,
            [PlanTestFactory.SourceEvidence("message")],
            CertaintyLevel.Exact);

    private static DiagramMessage OkMessage()
        => new(
            new DiagramPlanElementId("diagram-element:v1:message:ok"),
            "message:ok",
            "service",
            "client",
            "Ok -> HTTP 200",
            DiagramMessageKind.Response,
            [PlanTestFactory.SourceEvidence("message")],
            CertaintyLevel.Exact);

    private static DiagramMessage InsideMessage()
        => new(
            new DiagramPlanElementId("diagram-element:v1:message:inside"),
            "message:inside",
            "service",
            "client",
            "Inside -> HTTP 200",
            DiagramMessageKind.Response,
            [PlanTestFactory.SourceEvidence("message")],
            CertaintyLevel.Exact);

    /// <summary>A stable message reference that resolves to no planned message.</summary>
    private static DiagramPlanElementId MissingMessage()
        => new("diagram-element:v1:message:missing");

    private static EvidenceRef ConservativeEvidence(string artifact)
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
            CertaintyLevel.Conservative);
}
