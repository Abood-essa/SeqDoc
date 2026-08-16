using System.Collections.Immutable;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

/// <summary>
/// DQ-1 write-first regression: the planner's terminating arm emits a typed
/// <see cref="DiagramFragmentKind.Break"/> that is intentionally empty in the typed plan (the Core
/// closed-shape contract rejects arms, message refs, and nested fragments on a Break), and the
/// renderer currently serializes that as `break {label}` / `end` with no content. The block is
/// syntactically valid, so the structural validator cannot see the defect, but Visual Studio Code
/// Mermaid preview and Mermaid Live collapse the empty terminating region. The chosen generic
/// rendering contract keeps the termination meaning (the break opener, its typed label, and the
/// matching end stay present; the fragment is never suppressed) while every emitted break block
/// carries at least one non-comment Mermaid statement the validator accepts. The synthesized
/// content is renderer-generic and reuses only plan-derived data, never project vocabulary. The
/// repair must also leave the shared fragment-content serialization path deterministic: Break
/// shares its renderer case block with Opt and Loop, so the same byte-stable path would serve any
/// future non-empty Break.
/// </summary>
public sealed class MermaidRendererBreakRenderingTests
{
    [Fact]
    public void EmptyTypedBreakRendersTerminationWithAtLeastOneNonCommentStatement()
    {
        // Claim 1: a typed empty Break (the planner's terminating-arm marker) must not render as a
        // `break {label}` / `end` pair with zero non-comment Mermaid statements between them. The
        // concrete regression signature is the collapsed terminating region the owner observed in
        // the external TicketReservation diagrams.
        string mermaid = MermaidRenderer.Render(CreateEmptyBreakPlan());

        // Termination meaning is preserved: the break opener with the typed label and the closing
        // end are still emitted; the repair never suppresses the terminating marker (risk: hiding
        // proven termination semantics).
        Assert.Contains("break Return Not Found", mermaid, StringComparison.Ordinal);
        Assert.EndsWith("end", mermaid.TrimEnd(), StringComparison.Ordinal);

        // The chosen generic contract is the canonical termination note spanning the plan's stable
        // participant keys (single key when one participant). The note derives solely from Break
        // semantics and participants, never project vocabulary, timestamps, paths, or invented
        // calls, and it never becomes a message arrow.
        Assert.Contains("Note over client,service: Path terminates", mermaid, StringComparison.Ordinal);

        // The chosen generic contract: no break block may contain zero non-comment Mermaid
        // statements between break and end.
        Assert.Empty(EmptyBreakBlocks(mermaid));

        // The synthesized content must be real Mermaid sequence-diagram syntax. The structural
        // validator accepts only messages and participant declarations inside a block, so a
        // comment-only or invalid-content workaround cannot satisfy the documentation-set build.
        Assert.Empty(MermaidValidator.Validate(mermaid));
    }

    [Fact]
    public void EmptyBreakRenderingIsByteDeterministicAcrossIndependentRenders()
    {
        // Claim 2 preservation guard: the Core closed-shape contract pins Break as an empty marker
        // today, so a non-empty Break cannot be constructed and no future non-empty Break can be
        // exercised at the plan boundary. Break shares its renderer case block with Opt and Loop,
        // so the same shared content path would serialize any future non-empty Break. The DQ-1
        // repair synthesizes content for the empty Break inside that shared path; that synthesized
        // output must be byte-deterministic or the regenerated diagrams and any future content
        // would vary between runs.
        string first = MermaidRenderer.Render(CreateEmptyBreakPlan());
        string second = MermaidRenderer.Render(CreateEmptyBreakPlan());

        Assert.Equal(first, second);
    }

    [Fact]
    public void NonEmptyFragmentContentRendersMessagesWithoutSynthesizedTerminationNote()
    {
        // Claim 3: the DQ-1 note is emitted only inside an EMPTY Break. The Core closed-shape
        // contract rejects a non-empty Break at the plan boundary (arms, message refs, and nested
        // fragments are all rejected on a Break), so the executable proxy for the shared content
        // path is a non-empty Opt: its planned messages must serialize unchanged and no termination
        // note may be invented for a block that already has content. Non-empty opt/loop output
        // stays byte-for-byte what it was before the repair.
        string mermaid = MermaidRenderer.Render(CreateOptWithContentPlan());

        Assert.Contains("opt Guard", mermaid, StringComparison.Ordinal);
        Assert.Contains("Inside -> HTTP 200", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Note over", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Path terminates", mermaid, StringComparison.Ordinal);
        Assert.Empty(MermaidValidator.Validate(mermaid));
    }

    [Fact]
    public void EmptyBreakWithZeroParticipantsFailsClosedWithoutInventingKey()
    {
        // DQ-1 fail-closed edge: a plan with zero participants is constructible, and an empty
        // Break cannot name an anchor for its termination note. The renderer must throw a clear
        // error instead of inventing a participant key (which would break the participant
        // declarations or reference an actor that does not exist).
        var plan = CreateEmptyBreakPlan(participants: []);

        var exception = Assert.Throws<InvalidOperationException>(() => MermaidRenderer.Render(plan));
        Assert.Contains("participant", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DiagramPlan CreateEmptyBreakPlan(ImmutableArray<DiagramParticipant>? participants = null)
    {
        ImmutableArray<EvidenceRef> evidence = [PlanTestFactory.SourceEvidence("fragment")];
        var breakFragment = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:break:absent"),
            "decision:operation:v1:decision.WorkItemAbsent:arm:true:break",
            "Return Not Found",
            DiagramFragmentKind.Break,
            [],
            [],
            [],
            evidence,
            CertaintyLevel.Exact);

        var alt = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:alt:absent"),
            "decision:operation:v1:decision.WorkItemAbsent",
            "Condition",
            DiagramFragmentKind.Alt,
            [
                new DiagramAltArm(
                    new DiagramPlanElementId("diagram-element:v1:arm:absent:true"),
                    "decision:operation:v1:decision.WorkItemAbsent:arm:true",
                    "Return Not Found",
                    isElse: false,
                    messageRefs: [],
                    fragments: [breakFragment],
                    evidence: evidence,
                    certainty: CertaintyLevel.Exact),
                new DiagramAltArm(
                    new DiagramPlanElementId("diagram-element:v1:arm:absent:false"),
                    "decision:operation:v1:decision.WorkItemAbsent:arm:false",
                    "Continue",
                    isElse: true,
                    messageRefs: [OkMessage().Id],
                    fragments: [],
                    evidence: evidence,
                    certainty: CertaintyLevel.Exact),
            ],
            [],
            [],
            evidence,
            CertaintyLevel.Exact);

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            participants ?? Participants(),
            [EntryMessage(), OkMessage()],
            [],
            "diagram-plan:v1:fragment-break-empty",
            new DiagramSequence(
            [
                DiagramSequenceElement.MessageRef(EntryMessage().Id),
                DiagramSequenceElement.Fragment(alt),
            ]),
            []);
    }

    private static ImmutableArray<DiagramParticipant> Participants()
    {
        ImmutableArray<EvidenceRef> evidence = [PlanTestFactory.SourceEvidence("participant")];
        return
        [
            new DiagramParticipant(
                new DiagramPlanElementId("diagram-element:v1:participant:client"),
                "client",
                "Client",
                DiagramParticipantKind.Client,
                evidence,
                CertaintyLevel.Exact),
            new DiagramParticipant(
                new DiagramPlanElementId("diagram-element:v1:participant:service"),
                "service",
                "GadgetService",
                DiagramParticipantKind.Service,
                evidence,
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

    /// <summary>
    /// Non-empty Opt plan used to pin the DQ-1 non-empty preservation claim: the closed-shape
    /// contract rejects a non-empty Break at the plan boundary, so Opt exercises the same shared
    /// content serialization path the renderer keeps for non-empty fragments.
    /// </summary>
    private static DiagramPlan CreateOptWithContentPlan()
    {
        ImmutableArray<EvidenceRef> evidence = [PlanTestFactory.SourceEvidence("fragment")];
        var guard = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:opt:guard"),
            "decision:operation:v1:decision.Guard",
            "Guard",
            DiagramFragmentKind.Opt,
            [],
            [InsideMessage().Id],
            [],
            evidence,
            CertaintyLevel.Exact);

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            Participants(),
            [EntryMessage(), InsideMessage(), OkMessage()],
            [],
            "diagram-plan:v1:fragment-opt-content",
            new DiagramSequence(
            [
                DiagramSequenceElement.MessageRef(EntryMessage().Id),
                DiagramSequenceElement.Fragment(guard),
                DiagramSequenceElement.MessageRef(OkMessage().Id),
            ]),
            []);
    }

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

    /// <summary>
    /// Every break block that has zero non-comment Mermaid statements between its opener and the
    /// matching end. A comment-only or blank interior is still an empty block; only a statement
    /// the structural validator accepts (message or participant line) counts as content.
    /// </summary>
    private static ImmutableArray<string> EmptyBreakBlocks(string mermaid)
    {
        string[] lines = mermaid.Split('\n');
        var stack = new Stack<(string Kind, int Line)>();
        var empty = new List<string>();
        for (int index = 0; index < lines.Length; index++)
        {
            string trimmed = lines[index].Trim();
            string? kind = trimmed.StartsWith("break ", StringComparison.Ordinal) ? "break"
                : trimmed.StartsWith("alt ", StringComparison.Ordinal) ? "alt"
                : trimmed.StartsWith("opt ", StringComparison.Ordinal) ? "opt"
                : trimmed.StartsWith("loop ", StringComparison.Ordinal) ? "loop"
                : null;
            if (kind is not null)
            {
                stack.Push((kind, index));
                continue;
            }

            if (trimmed != "end" || stack.Count == 0)
            {
                continue;
            }

            var (poppedKind, opener) = stack.Pop();
            if (poppedKind == "break" && !BlockHasStatement(lines, opener, index))
            {
                empty.Add(lines[opener].Trim());
            }
        }

        return [.. empty];
    }

    private static bool BlockHasStatement(string[] lines, int opener, int closer)
    {
        for (int index = opener + 1; index < closer; index++)
        {
            string trimmed = lines[index].Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith("%%", StringComparison.Ordinal)
                || trimmed == "end"
                || trimmed.StartsWith("alt ", StringComparison.Ordinal)
                || trimmed.StartsWith("else ", StringComparison.Ordinal)
                || trimmed.StartsWith("opt ", StringComparison.Ordinal)
                || trimmed.StartsWith("loop ", StringComparison.Ordinal)
                || trimmed.StartsWith("break ", StringComparison.Ordinal)
                || trimmed.StartsWith("par ", StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
