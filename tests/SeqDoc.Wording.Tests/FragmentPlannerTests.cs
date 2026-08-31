using System.Collections.Immutable;
using System.Text;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Wording;
using Xunit;

namespace SeqDoc.Wording.Tests;

/// <summary>
/// accepted contract contract coverage for the structured Diagram Plan fragment contract. These tests define the
/// expected observable fragment tree the DocumentationPlanner must derive from the reviewed accepted contract
/// Scenario Graph topology, plus the evidence/certainty invariants and the depth-limit diagnostic.
/// Until that product contract exists this file cannot compile; every failure in this file is the
/// intentionally absent accepted contract contract, not test setup (the factory inputs compile against the
/// current product).
///
/// Expected product contract (implemented from contract stage accepted contract "Required Model"; see
/// <see cref="FragmentScenarioTestFactory"/> for the full record shapes):
/// <code>
/// public enum DiagramFragmentKind { Alt, Opt, Break, Loop }
/// public sealed record DiagramAltArm(...);
/// public sealed record DiagramFragment(...);
/// public sealed record DiagramSequence(
///     ImmutableArray&lt;DiagramPlanElementId&gt; MessageRefs,
///     ImmutableArray&lt;DiagramFragment&gt; Fragments);
/// public sealed record DiagramPlanDiagnostic(
///     DiagnosticId Id, string Code, string Summary, string Detail);
/// </code>
/// plus <c>DiagramPlan.Sequence</c> and <c>DiagramPlan.Diagnostics</c>; legacy 7-argument
/// construction yields a non-null empty sequence and empty diagnostics.
///
/// Pinned planner rules:
/// - Fragment identity keys are stable semantic keys (decision condition) and arm keys are semantic
///   polarity keys; never labels or traversal order.
/// - Visual arm order is failure/terminating-first and the plan records it explicitly (IsElse).
/// - A Terminates arm becomes an Alt arm holding exactly one Break fragment with no continuation.
/// - One material arm + one empty Rejoins arm becomes an Opt with no arms (no invented else).
/// - accepted contract aligned primary fragment/arm/Break labels: exact typed terminal wording when the arm has
///   one unique typed result/outcome, otherwise the sentence-case technical labels "Condition"/
///   "Continue"; never the raw operation id or "Terminates"/"Rejoins". Identities and structural
///   expectations are unchanged.
/// - Unknown/SC013 topology produces no fragment; known messages stay flat.
/// - Depth greater than 3 emits a stable DP001 diagnostic and a non-truncated flat fallback.
///
/// The F1-F7 review regressions pin additional contracts: malformed Alt roles and kind-specific
/// fragment shapes fail closed at construction; fragment/arm/Break evidence combines decision, arm,
/// membership, and terminal support with certainty degraded to the weakest contributor; equal
/// membership sets and ambiguous minimal parents stay flat and never nest; fragment/arm/break IDs
/// come from the stable Diagram Plan identity family; and the debug projection exposes ordered
/// element/ref placement.
/// </summary>
public sealed class FragmentPlannerTests
{
    [Fact]
    public void NestedAltElseTreeDerivesFromAbsentLockedTopology()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph())).Diagram;

        // Unscoped pre-decision facts stay flat at the sequence level in semantic edge order.
        AssertRefsEqual(
            Refs(plan, "scenario-edge:v1:workitem:entry", "scenario-edge:v1:workitem:call", "scenario-edge:v1:workitem:query1"),
            plan.Sequence.MessageRefs);

        var absent = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, absent.Kind);
        Assert.Equal("decision:" + FragmentScenarioTestFactory.AbsentCondition.Value, absent.Key, StringComparer.Ordinal);
        // Exact-wording contract: a rendered decision always carries exact compiler-evidenced owner
        // predicate wording (never the generic "Condition"/"Continue" tokens); the terminating arm
        // uses the exact predicate of the true arm and the rejoining arm the exact complement.
        Assert.Equal("reservation is null", absent.Label, StringComparer.Ordinal);
        Assert.Equal(2, absent.Arms.Length);

        // Failure-first visual order: the terminating (true) arm is first; the continuing arm is else.
        var terminatingArm = absent.Arms[0];
        var continuingArm = absent.Arms[1];
        Assert.False(terminatingArm.IsElse);
        Assert.True(continuingArm.IsElse);
        Assert.EndsWith(":arm:true", terminatingArm.Key, StringComparison.Ordinal);
        Assert.EndsWith(":arm:false", continuingArm.Key, StringComparison.Ordinal);
        Assert.Equal("reservation is null", terminatingArm.Label, StringComparer.Ordinal);
        Assert.Equal("reservation != null", continuingArm.Label, StringComparer.Ordinal);

        // The terminating arm creates a Break and holds no continuation messages.
        var breakFragment = Assert.Single(terminatingArm.Fragments);
        Assert.Equal(DiagramFragmentKind.Break, breakFragment.Kind);
        Assert.Equal("reservation is null", breakFragment.Label, StringComparer.Ordinal);
        Assert.Empty(breakFragment.MessageRefs);
        Assert.Empty(terminatingArm.MessageRefs);

        // The continuing arm nests the locked decision (child membership set contained in the parent arm).
        var locked = Assert.Single(continuingArm.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, locked.Kind);
        Assert.Equal("decision:" + FragmentScenarioTestFactory.LockedCondition.Value, locked.Key, StringComparer.Ordinal);
        Assert.Equal("reservation is null", locked.Label, StringComparer.Ordinal);
        var lockedTerminating = locked.Arms[0];
        var lockedContinuing = locked.Arms[1];
        Assert.Equal(DiagramFragmentKind.Break, Assert.Single(lockedTerminating.Fragments).Kind);
        Assert.Equal("reservation is null", lockedTerminating.Label, StringComparer.Ordinal);
        Assert.Equal("reservation != null", lockedContinuing.Label, StringComparer.Ordinal);
        Assert.Empty(lockedTerminating.MessageRefs);
        AssertRefsEqual(
            Refs(plan, "scenario-edge:v1:workitem:query2", "scenario-edge:v1:workitem:save"),
            lockedContinuing.MessageRefs);
        Assert.Empty(lockedContinuing.Fragments);

        // Every known message appears exactly once across the whole tree (nothing duplicated or lost).
        Assert.Equal(
            plan.Messages.Select(message => message.Key).Order(StringComparer.Ordinal),
            AllRefs(plan).Select(id => MessageKeyOf(plan, id)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TerminatingArmCreatesBreakAndNeverContainsContinuation()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph())).Diagram;

        var alts = CollectAlts(plan.Sequence.Fragments);
        Assert.Equal(2, alts.Count);
        foreach (var alt in alts)
        {
            // The visual-first arm is exactly one Break fragment with no messages of its own.
            var terminating = alt.Arms[0];
            Assert.Empty(terminating.MessageRefs);
            var breakFragment = Assert.Single(terminating.Fragments);
            Assert.Equal(DiagramFragmentKind.Break, breakFragment.Kind);
            Assert.Empty(breakFragment.MessageRefs);
            Assert.Empty(breakFragment.Fragments);
            Assert.Empty(SubtreeRefs(terminating));
        }
    }

    [Fact]
    public void OneSidedDecisionProducesOptWithoutElse()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateOneSidedOptGraph())).Diagram;

        AssertRefsEqual(
            Refs(plan, "scenario-edge:v1:opt:entry", "scenario-edge:v1:opt:call", "scenario-edge:v1:opt:query1"),
            plan.Sequence.MessageRefs);
        var opt = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Opt, opt.Kind);
        Assert.Equal("decision:" + FragmentScenarioTestFactory.LockedCondition.Value, opt.Key, StringComparer.Ordinal);
        Assert.Equal("reservation != null", opt.Label, StringComparer.Ordinal);
        Assert.True(opt.Arms.IsEmpty, "An Opt must never materialize an invented else arm.");
        AssertRefsEqual(
            Refs(plan, "scenario-edge:v1:opt:query2", "scenario-edge:v1:opt:save"),
            opt.MessageRefs);
        Assert.Empty(opt.Fragments);
    }

    [Fact]
    public void TypedOwnerAltUsesPredicateAndSafeComplement()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateBothMaterialAltGraph(
            predicateRole: ScenarioPredicateWordingRole.Owner)).Diagram;
        var alt = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal("reservation is null", alt.Label);
        Assert.Equal("reservation is null", alt.Arms[0].Label);
        Assert.Equal("reservation != null", alt.Arms[1].Label);
    }

    [Fact]
    public void TypedOwnerOptUsesMaterialArmPolarity()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateOneSidedOptGraph(
            predicateRole: ScenarioPredicateWordingRole.Owner)).Diagram;
        Assert.Equal("reservation != null", Assert.Single(plan.Sequence.Fragments).Label);
    }

    [Fact]
    public void SubordinateWithoutValidOwnerGroupIsWithheldWithTechnicalFallback()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph(
            predicateRole: ScenarioPredicateWordingRole.Subordinate));

        // A subordinate decision without a valid exact owner group never renders the generic
        // "Continue evaluating condition" label: it is withheld, its exclusively-guarded messages
        // are withheld with DP002, and the boundary is retained as Conservative evidence-backed
        // technical-fallback phrases.
        Assert.DoesNotContain(
            plan.Diagram.Sequence.Fragments,
            fragment => fragment.Label.Contains("Continue", StringComparison.Ordinal)
                || fragment.Label.Contains("Condition", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Sequence.Fragments.SelectMany(AllFragments),
            fragment => fragment.Label.Contains("Continue", StringComparison.Ordinal)
                || fragment.Label.Contains("Condition", StringComparison.Ordinal));
        var fallbacks = plan.Wording.Phrases
            .Where(phrase => phrase.Key.StartsWith("fallback:DP005", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(fallbacks);
        Assert.All(fallbacks, phrase =>
        {
            Assert.Equal(CertaintyLevel.Conservative, phrase.Certainty);
            Assert.NotEmpty(phrase.Evidence);
        });
        Assert.Contains(plan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP002");
    }

    [Fact]
    public void BothMaterialDecisionProducesFailureFirstAltWithStableSemanticIdentities()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateBothMaterialAltGraph())).Diagram;

        var alt = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, alt.Kind);
        Assert.Equal(2, alt.Arms.Length);

        // Visual order is failure-first: the terminating failure arm renders first, success is else.
        var failureArm = alt.Arms[0];
        var successArm = alt.Arms[1];
        Assert.False(failureArm.IsElse);
        Assert.True(successArm.IsElse);
        // Exact-wording contract: without typed terminal facts the arms carry the exact predicate
        // wording and its complement, never the generic "Condition"/"Continue" tokens.
        Assert.Equal("reservation is null", failureArm.Label, StringComparer.Ordinal);
        Assert.Equal("reservation != null", successArm.Label, StringComparer.Ordinal);
        AssertRefsEqual(
            Refs(plan, "scenario-edge:v1:both:fail-result", "scenario-edge:v1:both:fail-outcome"),
            failureArm.MessageRefs);
        AssertRefsEqual(
            Refs(plan, "scenario-edge:v1:both:ok-result", "scenario-edge:v1:both:ok-outcome"),
            successArm.MessageRefs);

        // Semantic identities are polarity keys, stable independent of the visual position.
        Assert.EndsWith(":arm:true", failureArm.Key, StringComparison.Ordinal);
        Assert.EndsWith(":arm:false", successArm.Key, StringComparison.Ordinal);
        Assert.EndsWith(":arm:true", alt.Arms[0].Key, StringComparison.Ordinal);

        // Repeated planning of an unchanged graph keeps identities and visual order byte-for-byte equal.
        var repeated = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateBothMaterialAltGraph())).Diagram;
        AssertSequenceEqual(plan.Sequence, repeated.Sequence);
    }

    [Fact]
    public void DecisionWithoutExactPredicateWordingIsWithheldWithFallbackAndDp002()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph());

        // A decision without exact compiler-evidenced predicate wording never renders a generic
        // "Condition" fragment: it is withheld, its guarded messages are withheld with DP002, and
        // each withheld boundary is retained as a Conservative evidence-backed fallback phrase.
        Assert.Empty(plan.Diagram.Sequence.Fragments);
        Assert.DoesNotContain(
            plan.Diagram.Sequence.Fragments.SelectMany(AllFragments),
            fragment => fragment.Label.Contains("Condition", StringComparison.Ordinal)
                || fragment.Label.Contains("Continue", StringComparison.Ordinal));
        Assert.Contains(plan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP002");
        var fallbacks = plan.Wording.Phrases
            .Where(phrase => phrase.Key.StartsWith("fallback:DP005", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, fallbacks.Length);
        Assert.All(fallbacks, phrase =>
        {
            Assert.Equal(WordingPhraseKind.TechnicalFallback, phrase.Kind);
            Assert.Equal(CertaintyLevel.Conservative, phrase.Certainty);
            Assert.NotEmpty(phrase.Evidence);
        });
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Key.Contains("query2", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Key.Contains("save", StringComparison.Ordinal));
    }

    [Fact]
    public void OpaquePredicateIsWithheldInsteadOfRenderingGenericCondition()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateBothMaterialAltGraph(
            predicateRole: ScenarioPredicateWordingRole.Owner,
            predicatePartition: "unsupported"));

        // An owner predicate whose normalized expression contains an opaque value formats to the
        // generic "Condition" token, so the decision is withheld rather than presented as useful
        // behavior; the boundary stays in technical fallback.
        Assert.Empty(plan.Diagram.Sequence.Fragments);
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("Condition", StringComparison.Ordinal));
        Assert.NotEmpty(
            plan.Wording.Phrases.Where(phrase => phrase.Key.StartsWith("fallback:DP005", StringComparison.Ordinal)));
    }

    [Fact]
    public void SourceConditionCallbackRegionIsWithheldWithDp003AndTechnicalFallback()
    {
        var plan = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreateCompositionEmptyTopologyGraph(sourceConditionRegion: true));

        // A source-condition callback region has no exact framework-condition wording: the generic
        // "Condition" Opt is never rendered, the region refs are withheld with DP003, and the
        // boundary is retained as a Conservative technical-fallback phrase. The guarded query never
        // renders as unconditional behavior.
        Assert.DoesNotContain(
            plan.Diagram.Sequence.Fragments.SelectMany(AllFragments),
            fragment => fragment.Label == "Condition");
        Assert.Contains(plan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP003");
        var fallback = Assert.Single(
            plan.Wording.Phrases,
            phrase => phrase.Key.StartsWith("fallback:DP003", StringComparison.Ordinal));
        Assert.Equal(CertaintyLevel.Conservative, fallback.Certainty);
        Assert.NotEmpty(fallback.Evidence);
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Key.Contains("query", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownSc013TopologyStaysFlatWithoutLoopOrBreak()
    {
        var graph = FragmentScenarioTestFactory.CreateUnknownSc013Graph();
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");

        var plan = DocumentationPlanner.Plan(graph).Diagram;

        // No fragment at all — especially no automatic Loop or Break from the unknown topology.
        Assert.Empty(plan.Sequence.Fragments);
        AssertRefsEqual(
            Refs(plan, "scenario-edge:v1:unknown:entry", "scenario-edge:v1:unknown:call", "scenario-edge:v1:unknown:query1", "scenario-edge:v1:unknown:save"),
            plan.Sequence.MessageRefs);
        Assert.All(plan.Sequence.MessageRefs, id => Assert.Contains(plan.Messages, message => message.Id == id));
    }

    [Fact]
    public void GuardedMessageOwnedOnlyByUnsupportedDecisionIsWithheldWithDP002()
    {
        var graph = FragmentScenarioTestFactory.CreateGuardedUnsupportedDecisionGraph();
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");

        var plan = DocumentationPlanner.Plan(graph).Diagram;

        // No fragment is produced for the unsupported decision.
        Assert.Empty(plan.Sequence.Fragments);

        // The guarded save is withheld with DP002: it never falls back to an unconditional
        // top-level message and is not even planned into the diagram.
        var saveRef = new DiagramPlanElementId("diagram-element:v1:message:scenario-edge:v1:guarded:save");
        Assert.DoesNotContain(plan.Messages, message => message.Id == saveRef);
        Assert.DoesNotContain(plan.Sequence.MessageRefs, reference => reference == saveRef);

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal("DP002", diagnostic.Code, StringComparer.Ordinal);

        // The truly unscoped query (no arm membership) keeps the accepted flat behavior.
        AssertRefsEqual(
            Refs(plan, "scenario-edge:v1:guarded:entry", "scenario-edge:v1:guarded:call", "scenario-edge:v1:guarded:query1"),
            plan.Sequence.MessageRefs);
        Assert.All(plan.Sequence.MessageRefs, id => Assert.Contains(plan.Messages, message => message.Id == id));
    }

    [Fact]
    public void FragmentVocabularyIsClosedAndExcludesPar()
    {
        string[] names = Enum.GetNames<DiagramFragmentKind>();
        Assert.Equal(4, names.Length);
        Assert.Contains("Alt", names);
        Assert.Contains("Opt", names);
        Assert.Contains("Break", names);
        Assert.Contains("Loop", names);
        Assert.DoesNotContain(names, name => name.Equals("Par", StringComparison.OrdinalIgnoreCase));

        // An out-of-range kind (the only way "par" could be expressed) is rejected at construction.
        var evidence = ScenarioGraphTestFactory.SourceEvidence("fragment");
        Assert.ThrowsAny<ArgumentException>(() => new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:par"),
            "par",
            "Par",
            (DiagramFragmentKind)99,
            [],
            [],
            [],
            [evidence],
            CertaintyLevel.Exact));
    }

    [Fact]
    public void DepthLimitProducesDeterministicDiagnosticAndNonTruncatedFlatFallback()
    {
        var first = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateDeepNestedGraph())).Diagram;
        var second = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateDeepNestedGraph())).Diagram;

        var diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal("DP001", diagnostic.Code, StringComparer.Ordinal);
        Assert.Contains("depth", diagnostic.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(diagnostic.Id.Value, Assert.Single(second.Diagnostics).Id.Value, StringComparer.Ordinal);

        // Non-truncated flat fallback: no partial fragment tree and every known message exactly once.
        Assert.Empty(first.Sequence.Fragments);
        AssertRefsEqual(
            Refs(
                first,
                "scenario-edge:v1:deep:entry",
                "scenario-edge:v1:deep:call",
                "scenario-edge:v1:deep:q2",
                "scenario-edge:v1:deep:q3",
                "scenario-edge:v1:deep:q4",
                "scenario-edge:v1:deep:q5",
                "scenario-edge:v1:deep:save"),
            first.Sequence.MessageRefs);
        Assert.Equal(first.Messages.Select(message => message.Key).Order(StringComparer.Ordinal),
            AllRefs(first).Select(id => MessageKeyOf(first, id)).Order(StringComparer.Ordinal));
        AssertSequenceEqual(first.Sequence, second.Sequence);
    }

    [Fact]
    public void ReversedTopologyConstructionYieldsEqualFragmentTree()
    {
        var forward = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph(reverseConstruction: false))).Diagram;
        var reversed = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph(reverseConstruction: true))).Diagram;

        // The fragment tree and planning diagnostics are derived from stable semantic keys and
        // membership containment, never from topology array order. Rendering-level determinism for
        // equal plans is asserted in DiagramFragmentRenderingTests, so the reversed-construction
        // Mermaid bytes are byte-stable by composition.
        AssertSequenceEqual(forward.Sequence, reversed.Sequence);
        AssertDiagnosticsEqual(forward.Diagnostics, reversed.Diagnostics);
    }

    [Fact]
    public void ScenarioGraphToPlanBoundaryProducesValidResolvableSequenceTree()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph()));

        // Every message reference in the sequence tree resolves to a planned message.
        Assert.All(AllRefs(plan.Diagram), id => Assert.Contains(plan.Diagram.Messages, message => message.Id == id));

        // No message is referenced more than once (the tree is a non-overlapping partition of the known messages).
        string[] refValues = AllRefs(plan.Diagram).Select(id => id.Value).ToArray();
        Assert.Equal(refValues.Length, refValues.Distinct(StringComparer.Ordinal).Count());

        // The tree stays within the default maximum fragment depth.
        Assert.InRange(MaxDepth(plan.Diagram.Sequence), 1, 3);
    }

    [Fact]
    public void PredicateOwnerGroupCollapsesNestedSubordinatesWithoutLosingTopology()
    {
        var plan = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreatePredicateOwnerGroupGraph()).Diagram;

        var owner = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, owner.Kind);
        Assert.Equal("decision:" + FragmentScenarioTestFactory.AbsentCondition.Value, owner.Key);
        Assert.DoesNotContain(
            AllFragments(owner),
            fragment => fragment.Label == "Continue evaluating condition");
        Assert.Contains(owner.Evidence, evidence => evidence.Artifact == "subordinate-decision");
        Assert.Contains(owner.Evidence, evidence => evidence.Artifact == "subordinate-membership");
        Assert.Contains(owner.Arms.SelectMany(arm => arm.Fragments), fragment => fragment.Kind == DiagramFragmentKind.Break);
        Assert.Equal(
            plan.Messages.Select(message => message.Key).Order(StringComparer.Ordinal),
            AllRefs(plan).Select(id => MessageKeyOf(plan, id)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PredicateOwnerGroupWithSubordinateTerminationWithholdsSubordinatesAndRendersExactOwner()
    {
        var plan = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreatePredicateOwnerGroupGraph(subordinateHasTerminatingArm: true));

        // Any subordinate termination makes the group unsafe: the terminating subordinate never
        // renders a generic label and its boundary is retained as a Conservative fallback phrase.
        // The exact owner still renders with compiler-evidenced wording and claims the shared
        // guarded messages exactly once; no evidence-free diagnostic is emitted.
        var owner = Assert.Single(plan.Diagram.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, owner.Kind);
        Assert.Equal("reservation is null", owner.Label, StringComparer.Ordinal);
        Assert.DoesNotContain(
            plan.Diagram.Sequence.Fragments.SelectMany(AllFragments),
            fragment => fragment.Label == "Continue evaluating condition");
        Assert.DoesNotContain(plan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP004");
        Assert.DoesNotContain(plan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP001");
        Assert.NotEmpty(
            plan.Wording.Phrases.Where(phrase => phrase.Key.StartsWith("fallback:DP005", StringComparison.Ordinal)));
        Assert.All(
            plan.Wording.Phrases.Where(phrase => phrase.Key.StartsWith("fallback:DP005", StringComparison.Ordinal)),
            phrase => Assert.Equal(CertaintyLevel.Conservative, phrase.Certainty));
        Assert.Equal(
            plan.Diagram.Messages.Select(message => message.Key).Order(StringComparer.Ordinal),
            AllRefs(plan.Diagram).Select(id => MessageKeyOf(plan.Diagram, id)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AmbiguousPredicateOwnersRenderExactFragmentsWithoutDp004OrGenericLabels()
    {
        var plan = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreatePredicateOwnerGroupGraph(ambiguousOwners: true));

        // Two owners share one predicate id: the group cannot be absorbed, so each exact owner
        // renders its own fragment; the remaining subordinate is withheld instead of ever rendering
        // the generic "Continue evaluating condition" label, with the boundary retained as a
        // Conservative fallback phrase.
        Assert.NotEmpty(plan.Diagram.Sequence.Fragments);
        Assert.DoesNotContain(plan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP004");
        Assert.DoesNotContain(
            plan.Diagram.Sequence.Fragments.SelectMany(AllFragments),
            fragment => fragment.Label == "Continue evaluating condition");
        Assert.NotEmpty(
            plan.Wording.Phrases.Where(phrase => phrase.Key.StartsWith("fallback:DP005", StringComparison.Ordinal)));
    }

    [Fact]
    public void PredicateOwnerGroupIsDeterministicWhenTopologyArraysAreReversed()
    {
        var forward = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreatePredicateOwnerGroupGraph()).Diagram;
        var reversed = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreatePredicateOwnerGroupGraph(reverseConstruction: true)).Diagram;

        AssertSequenceEqual(forward.Sequence, reversed.Sequence);
        Assert.Equal(forward.DebugProjection, reversed.DebugProjection);
    }

    [Fact]
    public void TopologyEmptyLegacyGraphKeepsFlatBranchesAndEmptySequence()
    {
        // Compatibility boundary: a legacy topology-empty graph keeps the accepted flat failure/success
        // branch output while the new sequence is non-null and empty.
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCompleteGetGraph()).Diagram;

        Assert.NotNull(plan.Sequence);
        Assert.Empty(plan.Sequence.MessageRefs);
        Assert.Empty(plan.Sequence.Fragments);
        Assert.Empty(plan.Diagnostics);
        Assert.NotEmpty(plan.Branches);
        Assert.Contains(plan.Branches, branch => branch.Kind == DiagramBranchKind.Failure);
        Assert.Contains(plan.Branches, branch => branch.Kind == DiagramBranchKind.Success);
    }

    [Fact]
    public void CompositionEmptyTopologyRendersConfigurationAltWithCacheMissOpt()
    {
        // accepted contract planner slice: a topology-empty graph with a typed service composition and one
        // framework cache-miss region renders a structured sequence (entry request, configuration
        // Alt) instead of the legacy flat branches. The service participant comes from the
        // composition contract role, never the first implementation name; arm labels are
        // namespace-free humanized implementation roles; the SQL arm nests one Opt labeled
        // "On cache miss" holding the EF query; the JSON arm holds only its service call.
        var forward = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreateCompositionEmptyTopologyGraph(reverseConstruction: false)).Diagram;
        var reversed = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreateCompositionEmptyTopologyGraph(reverseConstruction: true)).Diagram;

        // Determinism: reversed nodes/edges/member construction yields the same sequence and debug.
        AssertSequenceEqual(forward.Sequence, reversed.Sequence);
        Assert.Equal(forward.DebugProjection, reversed.DebugProjection);

        // Participants are concise and namespace-free; the service participant is the contract role.
        Assert.DoesNotContain(forward.Participants, participant => participant.Label.Contains("Acme.", StringComparison.Ordinal));
        var serviceParticipant = Assert.Single(forward.Participants, participant => participant.Key == "service");
        Assert.Equal("Customer service", serviceParticipant.Label, StringComparer.Ordinal);

        // Legacy flat branches never appear when a composition exists.
        Assert.Empty(forward.Branches);
        Assert.Empty(forward.Diagnostics);

        // The entry request stays outside the Alt at the sequence level; the one fragment is the Alt.
        AssertRefsEqual(
            Refs(forward, "scenario-edge:v1:composition:entry"),
            forward.Sequence.MessageRefs);
        var alt = Assert.Single(forward.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, alt.Kind);
        Assert.Equal(2, alt.Arms.Length);
        Assert.Equal("Use SQL database", alt.Label, StringComparer.Ordinal);

        // Arm labels are the readable humanized implementation roles without namespaces; semantic
        // polarity keys stay in the arm keys and the JSON arm is the explicit else.
        var sqlArm = alt.Arms[0];
        var jsonArm = alt.Arms[1];
        Assert.Equal("SQL customer service", sqlArm.Label, StringComparer.Ordinal);
        Assert.Equal("JSON customer service", jsonArm.Label, StringComparer.Ordinal);
        Assert.False(sqlArm.IsElse);
        Assert.True(jsonArm.IsElse);
        Assert.EndsWith(":arm:true", sqlArm.Key, StringComparison.Ordinal);
        Assert.EndsWith(":arm:false", jsonArm.Key, StringComparison.Ordinal);

        // SQL arm: the SQL call plus a nested Opt labeled exactly "On cache miss" holding the query;
        // no region member ref stays outside the Opt.
        AssertRefsEqual(Refs(forward, "scenario-edge:v1:composition:call-sql"), sqlArm.MessageRefs);
        var cacheMiss = Assert.Single(sqlArm.Fragments);
        Assert.Equal(DiagramFragmentKind.Opt, cacheMiss.Kind);
        Assert.Equal("On cache miss", cacheMiss.Label, StringComparer.Ordinal);
        AssertRefsEqual(Refs(forward, "scenario-edge:v1:composition:query-sql"), cacheMiss.MessageRefs);
        Assert.Empty(cacheMiss.Arms);
        Assert.Empty(cacheMiss.Fragments);

        // JSON arm: the JSON call only, with no query or cache-miss content.
        AssertRefsEqual(Refs(forward, "scenario-edge:v1:composition:call-json"), jsonArm.MessageRefs);
        Assert.Empty(jsonArm.Fragments);

        // Exact message coverage: every planned message is referenced exactly once.
        Assert.Equal(
            forward.Messages.Select(message => message.Key).Order(StringComparer.Ordinal),
            AllRefs(forward).Select(id => MessageKeyOf(forward, id)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UnsupportedCacheShapeKeepsConfigurationAltWithTechnicalFallback()
    {
        // accepted contract planner slice: when the Scenario Graph withholds the exact FusionCache contract
        // (SC014, unsupported: true), the planner still renders the configuration Alt with both
        // service arms, never invents a cache-miss Opt or a query message, and surfaces exactly one
        // Conservative evidence-backed FusionCache fallback phrase instead of presenting the
        // unsupported cache work.
        var plan = DocumentationPlanner.Plan(
            FragmentScenarioTestFactory.CreateCompositionEmptyTopologyGraph(unsupported: true));

        // The sequence holds exactly one Alt with two service arms (SQL true, JSON else).
        var alt = Assert.Single(plan.Diagram.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, alt.Kind);
        Assert.Equal(2, alt.Arms.Length);

        // Arm message refs keep the respective service calls.
        Assert.Contains(alt.Arms[0].MessageRefs, reference => reference.Value.Contains("call-sql", StringComparison.Ordinal));
        Assert.Contains(alt.Arms[1].MessageRefs, reference => reference.Value.Contains("call-json", StringComparison.Ordinal));

        // No cache-miss Opt anywhere in the whole fragment tree: the unsupported shape must never
        // be promoted to a nested cache-miss region.
        Assert.DoesNotContain(
            plan.Diagram.Sequence.Fragments.SelectMany(AllFragments),
            fragment => fragment.Kind == DiagramFragmentKind.Opt);

        // No EntityQuery/query semantic message survives the withheld cache region.
        Assert.DoesNotContain(
            plan.Diagram.Messages,
            message => message.Key.Contains("query", StringComparison.Ordinal));

        // Exactly one Conservative evidence-backed technical fallback names the unsupported
        // FusionCache contract.
        var fallback = Assert.Single(
            plan.Wording.Phrases,
            phrase => phrase.Kind == WordingPhraseKind.TechnicalFallback);
        Assert.Equal(CertaintyLevel.Conservative, fallback.Certainty);
        Assert.NotEmpty(fallback.Evidence);
        Assert.Contains("FusionCache", fallback.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("first-arm-else")]
    [InlineData("missing-explicit-else")]
    [InlineData("multiple-non-else")]
    public void MalformedAltRoleShapesFailConstruction(string partition)
    {
        // F2: an Alt must have exactly one leading non-else arm followed by explicit else arms.
        // The renderer never guesses alt/else from array index, so the conflicting shapes below
        // must fail construction instead of being silently reinterpreted.
        ImmutableArray<DiagramAltArm> arms = partition switch
        {
            // The first arm is already IsElse: no leading non-else arm exists.
            "first-arm-else" =>
            [
                AltArm(isElse: true),
                AltArm(isElse: true),
            ],
            // No arm is marked IsElse: the explicit else is missing entirely.
            "missing-explicit-else" =>
            [
                AltArm(isElse: false),
                AltArm(isElse: false),
            ],
            // Two non-else arms precede an else arm: multiple leading arms.
            "multiple-non-else" =>
            [
                AltArm(isElse: false),
                AltArm(isElse: false),
                AltArm(isElse: true),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(partition)),
        };

        Assert.ThrowsAny<ArgumentException>(() => new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:alt:roles"),
            "decision:operation:v1:decision.Roles",
            "Roles",
            DiagramFragmentKind.Alt,
            arms,
            [],
            [],
            [ScenarioGraphTestFactory.SourceEvidence("fragment")],
            CertaintyLevel.Exact));
    }

    [Theory]
    [InlineData("alt-zero-arms")]
    [InlineData("alt-one-arm")]
    [InlineData("alt-direct-message-refs")]
    [InlineData("alt-direct-fragments")]
    [InlineData("opt-with-arms")]
    [InlineData("opt-empty-content")]
    [InlineData("break-with-arms")]
    [InlineData("break-with-message-refs")]
    [InlineData("break-with-fragments")]
    [InlineData("loop-with-arms")]
    [InlineData("loop-with-fragments")]
    public void FragmentKindSpecificStructureRejectsMalformedShapes(string partition)
    {
        // F4: the closed per-kind contract rejects shapes the kind does not admit instead of
        // silently ignoring populated fields. Alt owns its arms (at least two) and no direct
        // content; Opt owns message refs/fragments and no arms; Break must be empty; Loop owns
        // message refs only (no arms, no nested fragments) for an exact preplanned loop.
        var evidence = ScenarioGraphTestFactory.SourceEvidence("fragment");
        var messageRef = new DiagramPlanElementId("diagram-element:v1:message:content");
        var nestedBreak = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:break:content"),
            "break:content",
            "Terminates",
            DiagramFragmentKind.Break,
            [],
            [],
            [],
            [evidence],
            CertaintyLevel.Exact);

        (DiagramFragmentKind Kind, ImmutableArray<DiagramAltArm> Arms, ImmutableArray<DiagramPlanElementId> Refs, ImmutableArray<DiagramFragment> Fragments) shape = partition switch
        {
            "alt-zero-arms" => (DiagramFragmentKind.Alt, [], [], []),
            "alt-one-arm" => (DiagramFragmentKind.Alt, [AltArm(isElse: false)], [], []),
            "alt-direct-message-refs" => (DiagramFragmentKind.Alt, [], [messageRef], []),
            "alt-direct-fragments" => (DiagramFragmentKind.Alt, [], [], [nestedBreak]),
            "opt-with-arms" => (DiagramFragmentKind.Opt, [AltArm(isElse: false), AltArm(isElse: true)], [], []),
            "opt-empty-content" => (DiagramFragmentKind.Opt, [], [], []),
            "break-with-arms" => (DiagramFragmentKind.Break, [AltArm(isElse: false), AltArm(isElse: true)], [], []),
            "break-with-message-refs" => (DiagramFragmentKind.Break, [], [messageRef], []),
            "break-with-fragments" => (DiagramFragmentKind.Break, [], [], [nestedBreak]),
            "loop-with-arms" => (DiagramFragmentKind.Loop, [AltArm(isElse: false), AltArm(isElse: true)], [], []),
            "loop-with-fragments" => (DiagramFragmentKind.Loop, [], [], [nestedBreak]),
            _ => throw new ArgumentOutOfRangeException(nameof(partition)),
        };

        Assert.ThrowsAny<ArgumentException>(() => new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:shape"),
            "fragment:shape",
            "Shape",
            shape.Kind,
            shape.Arms,
            shape.Refs,
            shape.Fragments,
            [evidence],
            CertaintyLevel.Exact));
    }

    [Fact]
    public void LoopMayOwnDirectMessageAndNestedLoopWithEvidence()
    {
        var evidence = ScenarioGraphTestFactory.SourceEvidence("nested-loop");
        var inner = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:loop:inner"),
            "loop:inner", "each inner iteration", DiagramFragmentKind.Loop, default,
            [new DiagramPlanElementId("diagram-element:v1:message:inner")], default, [evidence],
            CertaintyLevel.Exact);
        var outer = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:loop:outer"),
            "loop:outer", "each outer iteration", DiagramFragmentKind.Loop, default,
            [new DiagramPlanElementId("diagram-element:v1:message:outer")], [inner], [evidence],
            CertaintyLevel.Exact);

        Assert.Empty(outer.Arms);
        Assert.Equal(CertaintyLevel.Exact, outer.Certainty);
        Assert.Same(evidence, Assert.Single(outer.Evidence));
        Assert.Equal(DiagramFragmentKind.Loop, Assert.Single(outer.Fragments).Kind);
        Assert.Equal("diagram-element:v1:message:outer", Assert.Single(outer.MessageRefs).Value);
    }

    [Fact]
    public void MixedCertaintyTopologyCombinesAllSupportingEvidenceAndDegradesToWeakest()
    {
        // F5: fragment, arm, and Break evidence combines every supporting fact (decision, arm,
        // membership, terminal) and certainty degrades to the weakest contributor; a Conservative
        // membership must never be promoted to the decision's Exact certainty.
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateMixedCertaintyGraph())).Diagram;

        var alt = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, alt.Kind);

        Assert.Contains(alt.Evidence, evidence => evidence.Artifact == "decision");
        Assert.Contains(alt.Evidence, evidence => evidence.Artifact == "arm");
        Assert.Contains(alt.Evidence, evidence => evidence.Artifact == "membership");
        Assert.Contains(alt.Evidence, evidence => evidence.Artifact == "terminal");
        Assert.Equal(CertaintyLevel.Conservative, alt.Certainty);

        // The failing arm (visual first, terminating) degrades through its Conservative memberships.
        var failingArm = alt.Arms[0];
        Assert.Contains(failingArm.Evidence, evidence => evidence.Artifact == "decision");
        Assert.Contains(failingArm.Evidence, evidence => evidence.Artifact == "arm");
        Assert.Contains(failingArm.Evidence, evidence => evidence.Artifact == "membership");
        Assert.Contains(failingArm.Evidence, evidence => evidence.Artifact == "terminal");
        Assert.Equal(CertaintyLevel.Conservative, failingArm.Certainty);

        // The Break inside the terminating arm keeps the combined support and degrades too.
        var breakFragment = Assert.Single(failingArm.Fragments);
        Assert.Equal(DiagramFragmentKind.Break, breakFragment.Kind);
        Assert.Contains(breakFragment.Evidence, evidence => evidence.Artifact == "arm");
        Assert.Contains(breakFragment.Evidence, evidence => evidence.Artifact == "terminal");
        Assert.Equal(CertaintyLevel.Conservative, breakFragment.Certainty);

        // The untouched success arm keeps Exact; nothing weaker contributed to it.
        Assert.Equal(CertaintyLevel.Exact, alt.Arms[1].Certainty);
    }

    [Fact]
    public void EqualMembershipSetsStayFlatAndNeverNestUnrelatedDecisions()
    {
        // F6: equal membership sets do not prove guard containment. Two unrelated one-sided
        // decisions guarding the exact same message set must never nest (the current planner nests
        // the lexicographically later decision inside the earlier one); both fail flat and every
        // shared message is emitted exactly once at the enclosing sequence level. The unclaimed
        // entry/call messages precede the shared guarded messages in planner edge order, and F3
        // coverage keeps every planned message referenced exactly once.
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateEqualMembershipGraph())).Diagram;

        Assert.Empty(plan.Sequence.Fragments);
        AssertRefsEqual(
            Refs(
                plan,
                "scenario-edge:v1:equal:entry",
                "scenario-edge:v1:equal:call",
                "scenario-edge:v1:equal:q1",
                "scenario-edge:v1:equal:q2",
                "scenario-edge:v1:equal:q3"),
            plan.Sequence.MessageRefs);
    }

    [Fact]
    public void AmbiguousMultipleMinimalParentsFailFlatAtEnclosingLevel()
    {
        // F6 guard: when a child membership set is contained in two minimal parent arms (neither
        // arm's set contains the other), the child has no unique parent and stays flat at the
        // enclosing sequence level instead of nesting under either parent.
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateAmbiguousParentGraph())).Diagram;
        string childKey = "decision:" + FragmentScenarioTestFactory.GuardCCondition.Value;

        string[] rootKeys = plan.Sequence.Fragments.Select(fragment => fragment.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.Contains(childKey, rootKeys, StringComparer.Ordinal);

        foreach (var fragment in plan.Sequence.Fragments)
        {
            if (fragment.Key == childKey)
            {
                continue;
            }

            Assert.DoesNotContain(SubtreeKeys(fragment), key => key == childKey);
        }
    }

    [Fact]
    public void FragmentArmBreakIdsUseStableDiagramPlanIdentityFamily()
    {
        // F7: fragment, arm, and break IDs come from the Diagram Plan stable identity family
        // (profile + entry point + element kind + semantic key), not manual concatenation. The
        // same topology under a different profile/entry point must yield different IDs, and every
        // ID is a hashed diagram-element identity.
        var basePlan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph())).Diagram;
        var otherPlan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph(
                profileId: new CompilationProfileId("compilation-profile:v1:other"),
                entryPointId: new EntryPointId("entry-point:v1:other")))).Diagram;

        var baseAbsent = basePlan.Sequence.Fragments.Single();
        var otherAbsent = otherPlan.Sequence.Fragments.Single();
        var baseBreak = Assert.Single(baseAbsent.Arms[0].Fragments);
        var otherBreak = Assert.Single(otherAbsent.Arms[0].Fragments);

        Assert.Matches("^diagram-element:v1:[0-9a-f]{64}$", baseAbsent.Id.Value);
        Assert.Matches("^diagram-element:v1:[0-9a-f]{64}$", baseAbsent.Arms[0].Id.Value);
        Assert.Matches("^diagram-element:v1:[0-9a-f]{64}$", baseBreak.Id.Value);

        Assert.NotEqual(baseAbsent.Id.Value, otherAbsent.Id.Value);
        Assert.NotEqual(baseAbsent.Arms[0].Id.Value, otherAbsent.Arms[0].Id.Value);
        Assert.NotEqual(baseBreak.Id.Value, otherBreak.Id.Value);
    }

    [Fact]
    public void FragmentArmBreakIdsRemainStableUnderReversedConstruction()
    {
        // F7 guard: IDs follow stable semantic keys, so reversed topology construction and
        // label/visual presentation never change fragment, arm, or break identities.
        var forward = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph(reverseConstruction: false))).Diagram;
        var reversed = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph(reverseConstruction: true))).Diagram;

        Assert.Equal(FragmentIdLines(forward), FragmentIdLines(reversed));
    }

    [Fact]
    public void DebugProjectionExposesOrderedElementAndRefPlacement()
    {
        // F1/F7: the debug projection exposes the ordered sequence placement — the unclaimed
        // message references first in planner edge order, then the root fragment — so renderer
        // chronology is inspectable without invoking the renderer. Each element line records the
        // element kind and its exact position in the ordered sequence.
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph())).Diagram;

        string[] elementLines = plan.DebugProjection
            .Split('\n')
            .Where(line => line.StartsWith("element ", StringComparison.Ordinal))
            .ToArray();

        Assert.True(elementLines.Length >= 4, "The projection must list every ordered sequence element.");
        Assert.Contains("scenario-edge:v1:workitem:entry", elementLines[0], StringComparison.Ordinal);
        Assert.Contains("scenario-edge:v1:workitem:call", elementLines[1], StringComparison.Ordinal);
        Assert.Contains("scenario-edge:v1:workitem:query1", elementLines[2], StringComparison.Ordinal);
        Assert.Contains("kind=fragment", elementLines[3], StringComparison.Ordinal);
        Assert.True(
            elementLines[0].Contains("position=0", StringComparison.Ordinal)
            && elementLines[1].Contains("position=1", StringComparison.Ordinal)
            && elementLines[2].Contains("position=2", StringComparison.Ordinal)
            && elementLines[3].Contains("position=3", StringComparison.Ordinal),
            "The projection must record the exact ordered position of each element.");
    }

    private static ImmutableArray<DiagramPlanElementId> Refs(DiagramPlan plan, params string[] edgeIds)
        => edgeIds.Select(id => new DiagramPlanElementId("diagram-element:v1:message:" + id)).ToImmutableArray();

    private static string MessageKeyOf(DiagramPlan plan, DiagramPlanElementId id)
        => plan.Messages.Single(message => message.Id == id).Key;

    private static IEnumerable<DiagramPlanElementId> AllRefs(DiagramPlan plan)
    {
        foreach (DiagramPlanElementId id in plan.Sequence.MessageRefs)
        {
            yield return id;
        }

        foreach (DiagramFragment fragment in plan.Sequence.Fragments)
        {
            foreach (DiagramPlanElementId id in AllRefs(fragment))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<DiagramPlanElementId> AllRefs(DiagramFragment fragment)
    {
        foreach (DiagramPlanElementId id in fragment.MessageRefs)
        {
            yield return id;
        }

        foreach (DiagramAltArm arm in fragment.Arms)
        {
            foreach (DiagramPlanElementId id in arm.MessageRefs)
            {
                yield return id;
            }

            foreach (DiagramFragment nested in arm.Fragments)
            {
                foreach (DiagramPlanElementId id in AllRefs(nested))
                {
                    yield return id;
                }
            }
        }

        foreach (DiagramFragment nested in fragment.Fragments)
        {
            foreach (DiagramPlanElementId id in AllRefs(nested))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<DiagramPlanElementId> SubtreeRefs(DiagramAltArm arm)
        => arm.MessageRefs.Concat(arm.Fragments.SelectMany(AllRefs));

    private static int MaxDepth(DiagramSequence sequence)
        => sequence.Fragments.Length == 0
            ? 0
            : sequence.Fragments.Max(MaxDepth);

    private static int MaxDepth(DiagramFragment fragment)
    {
        int nestedMax = fragment.Fragments.Length == 0 ? 0 : fragment.Fragments.Max(MaxDepth);
        foreach (DiagramAltArm arm in fragment.Arms)
        {
            int armMax = arm.Fragments.Length == 0 ? 0 : arm.Fragments.Max(MaxDepth);
            nestedMax = Math.Max(nestedMax, armMax);
        }

        return 1 + nestedMax;
    }

    private static List<DiagramFragment> CollectAlts(IEnumerable<DiagramFragment> fragments)
    {
        var alts = new List<DiagramFragment>();
        foreach (DiagramFragment fragment in fragments)
        {
            if (fragment.Kind == DiagramFragmentKind.Alt)
            {
                alts.Add(fragment);
            }

            alts.AddRange(CollectAlts(fragment.Fragments));
            foreach (DiagramAltArm arm in fragment.Arms)
            {
                alts.AddRange(CollectAlts(arm.Fragments));
            }
        }

        return alts;
    }

    /// <summary>
    /// The fragment itself plus every descendant reached through Alt arms and nested fragments, so
    /// whole-tree assertions (for example "no Opt anywhere") never depend on the current shape.
    /// </summary>
    private static IEnumerable<DiagramFragment> AllFragments(DiagramFragment fragment)
    {
        yield return fragment;
        foreach (DiagramAltArm arm in fragment.Arms)
        {
            foreach (DiagramFragment nested in arm.Fragments)
            {
                foreach (DiagramFragment descendant in AllFragments(nested))
                {
                    yield return descendant;
                }
            }
        }

        foreach (DiagramFragment nested in fragment.Fragments)
        {
            foreach (DiagramFragment descendant in AllFragments(nested))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<DiagramFragment> AllFragments(IEnumerable<DiagramFragment> fragments)
        => fragments.SelectMany(AllFragments);

    /// <summary>
    /// Content equality for message-reference arrays. xunit's <c>Assert.Equal</c> compares
    /// <c>ImmutableArray</c> by underlying array reference (the type implements
    /// <c>IEquatable&lt;ImmutableArray&lt;T&gt;&gt;</c> with reference semantics), so equal-content
    /// arrays produced by independent planning calls must be compared element-wise by their stable
    /// reference values.
    /// </summary>
    private static void AssertRefsEqual(ImmutableArray<DiagramPlanElementId> expected, ImmutableArray<DiagramPlanElementId> actual)
        => Assert.Equal(
            expected.Select(reference => reference.Value).ToArray(),
            actual.Select(reference => reference.Value).ToArray());

    /// <summary>
    /// Content equality for fragment trees. Records holding <c>ImmutableArray</c> members compare
    /// those members by reference, so the canonical tree shape string compares kind, semantic keys,
    /// labels, arm polarity, and message references recursively.
    /// </summary>
    private static void AssertSequenceEqual(DiagramSequence expected, DiagramSequence actual)
    {
        AssertRefsEqual(expected.MessageRefs, actual.MessageRefs);
        Assert.Equal(
            expected.Fragments.Select(FragmentShape).ToArray(),
            actual.Fragments.Select(FragmentShape).ToArray());
    }

    /// <summary>Content equality for planning diagnostics by stable id, code, summary, and detail.</summary>
    private static void AssertDiagnosticsEqual(ImmutableArray<DiagramPlanDiagnostic> expected, ImmutableArray<DiagramPlanDiagnostic> actual)
        => Assert.Equal(
            expected.Select(diagnostic => $"{diagnostic.Id.Value}|{diagnostic.Code}|{diagnostic.Summary}|{diagnostic.Detail}").ToArray(),
            actual.Select(diagnostic => $"{diagnostic.Id.Value}|{diagnostic.Code}|{diagnostic.Summary}|{diagnostic.Detail}").ToArray());

    private static string FragmentShape(DiagramFragment fragment)
    {
        var builder = new StringBuilder();
        AppendFragmentShape(builder, fragment, 0);
        return builder.ToString();
    }

    private static void AppendFragmentShape(StringBuilder builder, DiagramFragment fragment, int depth)
    {
        builder.Append(new string(' ', depth * 2))
            .Append(fragment.Kind).Append('|').Append(fragment.Key).Append('|').Append(fragment.Label)
            .Append("|refs=").Append(string.Join(",", fragment.MessageRefs.Select(reference => reference.Value))).Append('\n');
        foreach (DiagramAltArm arm in fragment.Arms)
        {
            builder.Append(new string(' ', (depth + 1) * 2))
                .Append("arm|").Append(arm.Key).Append('|').Append(arm.Label).Append('|').Append(arm.IsElse)
                .Append("|refs=").Append(string.Join(",", arm.MessageRefs.Select(reference => reference.Value))).Append('\n');
            foreach (DiagramFragment nested in arm.Fragments)
            {
                AppendFragmentShape(builder, nested, depth + 2);
            }
        }

        foreach (DiagramFragment nested in fragment.Fragments)
        {
            AppendFragmentShape(builder, nested, depth + 1);
        }
    }

    /// <summary>A well-formed single Alt arm with the requested explicit IsElse role.</summary>
    private static DiagramAltArm AltArm(bool isElse)
        => new(
            new DiagramPlanElementId("diagram-element:v1:arm:role"),
            "decision:operation:v1:decision.Roles:arm:true",
            isElse ? "Else" : "Leading",
            isElse,
            [],
            [],
            [ScenarioGraphTestFactory.SourceEvidence("arm")],
            CertaintyLevel.Exact);

    /// <summary>All nested fragment keys in a fragment's subtree (excluding the fragment itself).</summary>
    private static IEnumerable<string> SubtreeKeys(DiagramFragment fragment)
    {
        foreach (DiagramAltArm arm in fragment.Arms)
        {
            foreach (DiagramFragment nested in arm.Fragments)
            {
                yield return nested.Key;
                foreach (string key in SubtreeKeys(nested))
                {
                    yield return key;
                }
            }
        }

        foreach (DiagramFragment nested in fragment.Fragments)
        {
            yield return nested.Key;
            foreach (string key in SubtreeKeys(nested))
            {
                yield return key;
            }
        }
    }

    /// <summary>Canonical depth-first fragment/arm/break ID lines for one plan tree.</summary>
    private static string[] FragmentIdLines(DiagramPlan plan)
    {
        var lines = new List<string>();
        foreach (DiagramFragment fragment in plan.Sequence.Fragments)
        {
            AppendFragmentIdLines(lines, fragment);
        }

        return lines.ToArray();
    }

    private static void AppendFragmentIdLines(List<string> lines, DiagramFragment fragment)
    {
        lines.Add($"fragment {fragment.Id.Value}");
        foreach (DiagramAltArm arm in fragment.Arms)
        {
            lines.Add($"arm {arm.Id.Value}");
            foreach (DiagramFragment nested in arm.Fragments)
            {
                AppendFragmentIdLines(lines, nested);
            }
        }

        foreach (DiagramFragment nested in fragment.Fragments)
        {
            AppendFragmentIdLines(lines, nested);
        }
    }
}
