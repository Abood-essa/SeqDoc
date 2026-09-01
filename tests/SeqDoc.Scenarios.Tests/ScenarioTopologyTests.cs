using System.Collections.Immutable;
using System.Reflection;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// accepted contract contract coverage for the architecture decision Scenario Graph decision topology. These tests define the
/// expected observable topology contract the accepted contract Builder must add to the Core scenario-graph
/// contracts. Until that product contract exists this file cannot compile; every failure in this file
/// is the intentionally absent topology contract, not test setup (the factory inputs compile against
/// the current product).
///
/// Expected product contract (to be implemented from architecture decision and the accepted contract frozen accepted contract contract):
/// <code>
/// public enum ScenarioTerminalKind { Unknown, Terminates, Rejoins }
/// public sealed record ScenarioDecision(ScenarioDecisionId Id, MethodId Method,
///     FlowNodeId ControllingFlowNode, OperationId Condition,
///     ImmutableArray&lt;EvidenceRef&gt; Evidence, CertaintyLevel Certainty);
/// public sealed record ScenarioArm(ScenarioArmId Id, ScenarioDecisionId Decision,
///     bool IsTrue, ImmutableArray&lt;EvidenceRef&gt; Evidence, CertaintyLevel Certainty);
/// public sealed record ScenarioMembership(ScenarioMembershipId Id, ScenarioArmId Arm,
///     ScenarioNodeId ScenarioNode, ImmutableArray&lt;EvidenceRef&gt; Evidence, CertaintyLevel Certainty);
/// public sealed record ScenarioArmTerminal(ScenarioArmId Arm, ScenarioTerminalKind Kind,
///     ImmutableArray&lt;EvidenceRef&gt; Evidence, CertaintyLevel Certainty);
/// public sealed record ScenarioTopology(ImmutableArray&lt;ScenarioDecision&gt; Decisions,
///     ImmutableArray&lt;ScenarioArm&gt; Arms, ImmutableArray&lt;ScenarioMembership&gt; Memberships,
///     ImmutableArray&lt;ScenarioArmTerminal&gt; Terminals);
/// </code>
/// plus <c>ScenarioDecisionId</c>, <c>ScenarioArmId</c>, and <c>ScenarioMembershipId</c> identity
/// types, canonical order by controlling flow-node identity then polarity then controlled node, and
/// <c>ScenarioGraph.Topology</c>. SC011/SC012/SC013 flow through the existing
/// <see cref="ScenarioGraph.Diagnostics"/> channel and never select a fragment or hide a known node.
/// </summary>
public sealed class ScenarioTopologyTests
{
    /// <summary>
    /// Claim 6: the absent-item true arm is the represented terminal arm (classifies Terminates) and
    /// the continuing-path nodes never rejoin it.
    /// </summary>
    [Fact]
    public void AbsentItemTrueArmTerminatesAndContinuingNodesNeverRejoin()
    {
        var graph = BuildWorkItemGraph();

        var absent = Assert.Single(graph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemAbsentCondition);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == absent.Id && arm.IsTrue);
        Assert.Equal(ScenarioTerminalKind.Terminates, Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == trueArm.Id).Kind);

        var stateNode = Assert.Single(graph.Nodes, node => node.Key == $"state:{ScenarioTestFactory.WorkItemStateAssignmentOperation.Value}");
        var saveNode = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemSaveOperation.Value}");
        Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.Arm == trueArm.Id
            && (membership.ScenarioNode == stateNode.Id || membership.ScenarioNode == saveNode.Id));
    }

    /// <summary>
    /// Claim 7: the locked-item true arm is the represented terminal arm (classifies Terminates) and
    /// the continuing-path nodes never rejoin it.
    /// </summary>
    [Fact]
    public void LockedItemTrueArmTerminatesAndContinuingNodesNeverRejoin()
    {
        var graph = BuildWorkItemGraph();

        var locked = Assert.Single(graph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemLockedCondition);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == locked.Id && arm.IsTrue);
        Assert.Equal(ScenarioTerminalKind.Terminates, Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == trueArm.Id).Kind);

        var stateNode = Assert.Single(graph.Nodes, node => node.Key == $"state:{ScenarioTestFactory.WorkItemStateAssignmentOperation.Value}");
        var saveNode = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemSaveOperation.Value}");
        Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.Arm == trueArm.Id
            && (membership.ScenarioNode == stateNode.Id || membership.ScenarioNode == saveNode.Id));
    }

    /// <summary>
    /// Claim 8: the unlocked continuing path (state assignment and save) is a member of BOTH
    /// decisions' false arms; the success path is guarded, never unconditional.
    /// </summary>
    [Fact]
    public void UnlockedContinuingPathIsMemberOfBothFalseArms()
    {
        var graph = BuildWorkItemGraph();

        var stateNode = Assert.Single(graph.Nodes, node => node.Key == $"state:{ScenarioTestFactory.WorkItemStateAssignmentOperation.Value}");
        var saveNode = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemSaveOperation.Value}");
        foreach (var decision in graph.Topology.Decisions)
        {
            var falseArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && !arm.IsTrue);
            Assert.Contains(graph.Topology.Memberships, membership => membership.Arm == falseArm.Id && membership.ScenarioNode == stateNode.Id);
            Assert.Contains(graph.Topology.Memberships, membership => membership.Arm == falseArm.Id && membership.ScenarioNode == saveNode.Id);
        }
    }

    /// <summary>
    /// Claim 9: nesting under different decisions is valid (the continuing path sits under the absent
    /// false arm AND the locked false arm) and the topology identity plus canonical order are stable
    /// when the request is constructed in reversed order.
    /// </summary>
    [Fact]
    public void NestedMembershipIsValidAndTopologyIsStableUnderReversedConstruction()
    {
        var forward = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateWorkItemTopologyRequest(reverseConstruction: false));
        var reversed = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateWorkItemTopologyRequest(reverseConstruction: true));
        var forwardGraph = Assert.Single(forward.Graphs);
        var reversedGraph = Assert.Single(reversed.Graphs);

        Assert.Equal(CollectTopology(forwardGraph), CollectTopology(reversedGraph));
        Assert.DoesNotContain(forwardGraph.Diagnostics, diagnostic => diagnostic.Code == "SC012");

        var stateNode = Assert.Single(forwardGraph.Nodes, node => node.Key == $"state:{ScenarioTestFactory.WorkItemStateAssignmentOperation.Value}");
        var absent = Assert.Single(forwardGraph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemAbsentCondition);
        var locked = Assert.Single(forwardGraph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemLockedCondition);
        var absentFalse = Assert.Single(forwardGraph.Topology.Arms, arm => arm.Decision == absent.Id && !arm.IsTrue);
        var lockedFalse = Assert.Single(forwardGraph.Topology.Arms, arm => arm.Decision == locked.Id && !arm.IsTrue);
        Assert.Contains(forwardGraph.Topology.Memberships, membership => membership.Arm == absentFalse.Id && membership.ScenarioNode == stateNode.Id);
        Assert.Contains(forwardGraph.Topology.Memberships, membership => membership.Arm == lockedFalse.Id && membership.ScenarioNode == stateNode.Id);

        // Canonical order: decisions by controlling flow-node identity, arms by controlling
        // flow-node identity then semantic polarity (review F7), memberships by arm then controlled
        // node identity.
        Assert.Equal(
            forwardGraph.Topology.Decisions.Select(decision => decision.ControllingFlowNode.Value).OrderBy(value => value, StringComparer.Ordinal),
            forwardGraph.Topology.Decisions.Select(decision => decision.ControllingFlowNode.Value));
        var decisionById = forwardGraph.Topology.Decisions.ToDictionary(decision => decision.Id);
        Assert.Equal(
            forwardGraph.Topology.Arms
                .OrderBy(arm => decisionById[arm.Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
                .ThenBy(arm => arm.IsTrue)
                .Select(arm => arm.Id.Value),
            forwardGraph.Topology.Arms.Select(arm => arm.Id.Value));
    }

    /// <summary>
    /// Claim 10: a material scenario node without an exact eligible Method Flow anchor stays visible
    /// but unscoped: SC011 is emitted and no arm membership is invented for it.
    /// </summary>
    [Fact]
    public void MissingExactAnchorKeepsNodeVisibleAndWithholdsMembership()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateMissingAnchorTopologyRequest());
        var graph = Assert.Single(set.Graphs);

        var missing = Assert.Single(graph.Nodes, node => node.Key == $"query:{ScenarioTestFactory.WorkItemMissingAnchorOperation.Value}");
        Assert.Equal(ScenarioNodeKind.EntityQuery, missing.Kind);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC011");
        Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.ScenarioNode == missing.Id);
    }

    /// <summary>
    /// Claim 11: one node directly controlled by BOTH semantic arms of the SAME decision fails closed
    /// with SC012 and the conflicting membership is withheld; nesting under DIFFERENT decisions
    /// remains valid (covered by the work-item requests above, which emit no SC012).
    /// </summary>
    [Fact]
    public void SameDecisionDualPolarityConflictFailsClosedWithSC012()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateDualPolarityConflictRequest());
        var graph = Assert.Single(set.Graphs);

        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC012");
        var saveNode = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemSaveOperation.Value}");
        Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.ScenarioNode == saveNode.Id);
    }

    /// <summary>
    /// Claim 12 (b): genuine exception-region and foreign-loop topology stays fail-closed. The
    /// unsupported fixture puts the locked decision inside a Catch region AND gives it an outgoing
    /// LoopBack edge to a DIFFERENT decision (the absent header). The decision is not the exact header
    /// of any LoopNode, so neither the catch/filter/finally carve-out nor the same-header iteration
    /// boundary applies: SC013 is emitted and the arm stays Unknown while known nodes stay visible.
    /// An over-broad fix that admits any Try/Catch-region decision or any LoopBack edge fails here.
    /// </summary>
    [Fact]
    public void UnsupportedLoopBackAndExceptionTopologyFailsClosedWithSC013()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateUnsupportedTopologyRequest());
        var graph = Assert.Single(set.Graphs);

        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);

        var locked = Assert.Single(graph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemLockedCondition);
        Assert.DoesNotContain(graph.Topology.Terminals, terminal =>
            graph.Topology.Arms.Any(arm => arm.Id == terminal.Arm && arm.Decision == locked.Id)
            && terminal.Kind != ScenarioTerminalKind.Unknown);
    }

    [Fact]
    public void PlainTryContainingOrdinaryDecisionsClassifiesNormalTerminals()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreatePlainTryTopologyRequest()).Graphs);

        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        foreach (var decision in graph.Topology.Decisions)
        {
            var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
            var falseArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && !arm.IsTrue);
            Assert.Equal(ScenarioTerminalKind.Terminates,
                Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == trueArm.Id).Kind);
            Assert.Equal(decision.Condition == ScenarioTestFactory.WorkItemAbsentCondition
                    ? ScenarioTerminalKind.Rejoins
                    : ScenarioTerminalKind.Terminates,
                Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == falseArm.Id).Kind);
        }
    }

    [Fact]
    public void TryContainedRootCallFlowsFromMethodFlowThroughGraphIntoOnePlannedArm()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateRootDirectCallTryRequest()).Graphs);
        var plan = DocumentationPlanner.Plan(ScenarioTestFactory.WithExactOwnerWording(graph)).Diagram;

        var decision = Assert.Single(graph.Topology.Decisions);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
        var call = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall
            && node.Operation == ScenarioTestFactory.RootDirectCallOperation);
        Assert.Contains(graph.Topology.Memberships, membership => membership.Arm == trueArm.Id && membership.ScenarioNode == call.Id);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");

        var fragment = Assert.Single(plan.Sequence.Fragments);
        var plannedTrueArm = Assert.Single(fragment.Arms, arm => arm.Key.EndsWith(":arm:true", StringComparison.Ordinal));
        var plannedCallRef = Assert.Single(plannedTrueArm.MessageRefs);
        Assert.DoesNotContain(plannedCallRef, plan.Sequence.MessageRefs);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Code == "DP002");
        Assert.Equal(1, plan.Sequence.Fragments.SelectMany(fragment => fragment.Arms)
            .SelectMany(arm => arm.MessageRefs)
            .Count(reference => reference == plannedCallRef));
    }

    /// <summary>Catch, filter, and finally are one fail-closed exception-region partition.</summary>
    [Theory]
    [InlineData("Catch")]
    [InlineData("Filter")]
    [InlineData("Finally")]
    public void OrdinaryDecisionInExceptionRegionRemainsUnknownWithSC013(string regionKind)
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateExceptionRegionTopologyRequest(regionKind)).Graphs);

        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        var locked = Assert.Single(graph.Topology.Decisions,
            decision => decision.Condition == ScenarioTestFactory.WorkItemLockedCondition);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == locked.Id && arm.IsTrue);
        Assert.Equal(ScenarioTerminalKind.Unknown,
            Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == trueArm.Id).Kind);
    }

    [Fact]
    public void PlainTryNormalTransitionIntoFinallyTargetFailsClosedWithSC013()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateFinallyTargetTopologyRequest()).Graphs);

        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        var locked = Assert.Single(graph.Topology.Decisions,
            decision => decision.Condition == ScenarioTestFactory.WorkItemLockedCondition);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == locked.Id && arm.IsTrue);
        var falseArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == locked.Id && !arm.IsTrue);
        Assert.Equal(ScenarioTerminalKind.Unknown,
            Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == trueArm.Id).Kind);
        Assert.Equal(ScenarioTerminalKind.Terminates,
            Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == falseArm.Id).Kind);
    }

    /// <summary>
    /// Claim 11: the architecture decision accepted exact own-header loop classification. A decision that IS
    /// the exact <see cref="LoopNode.Header"/> of an existing loop may classify its normal arms even
    /// when compiler lowering places the header inside a Try region: the body LoopBack to that same
    /// header is a represented iteration boundary (Rejoins), the loop-exit arm rejoins too, the body
    /// interaction keeps its exact true-arm membership, and no SC013 is emitted for that decision.
    /// Review F3 additionally requires the body-arm terminal to aggregate every typed support fact —
    /// the decision, the loop fact, and the LoopBack edge — and degrade to the weakest contributor
    /// instead of inheriting the exact decision certainty alone.
    /// </summary>
    [Fact]
    public void ExactOwnHeaderLoopClassifiesRejoiningAndRetainsBodyMembership()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateExactOwnHeaderLoopRequest());
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");

        var loop = Assert.Single(graph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemLoopCondition);
        var bodyArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == loop.Id && arm.IsTrue);
        var exitArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == loop.Id && !arm.IsTrue);
        var bodyTerminal = Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == bodyArm.Id);
        Assert.Equal(ScenarioTerminalKind.Rejoins, bodyTerminal.Kind);
        Assert.Equal(ScenarioTerminalKind.Rejoins, Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == exitArm.Id).Kind);

        // The iteration boundary is supported by the decision fact, the loop fact, and the LoopBack
        // edge; the loop-back edge is Conservative so the terminal certainty degrades to the weakest
        // contributor rather than staying Exact with the decision.
        Assert.Contains(bodyTerminal.Evidence, evidence => evidence.Artifact == ScenarioTestFactory.WorkItemDecisionEvidence);
        Assert.Contains(bodyTerminal.Evidence, evidence => evidence.Artifact == ScenarioTestFactory.WorkItemLoopEvidence);
        Assert.Contains(bodyTerminal.Evidence, evidence => evidence.Artifact == ScenarioTestFactory.WorkItemLoopBackEvidence);
        Assert.Equal(CertaintyLevel.Conservative, bodyTerminal.Certainty);

        var addNode = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemAddOperation.Value}");
        Assert.Contains(graph.Topology.Memberships, membership => membership.Arm == bodyArm.Id && membership.ScenarioNode == addNode.Id);
        Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.Arm == exitArm.Id && membership.ScenarioNode == addNode.Id);
    }

    /// <summary>
    /// Review F2: the exact own-header carve-out must not trust an incomplete or contradictory loop
    /// snapshot. When the LoopNode's Body does not contain the actual LoopBack source, or its Exits
    /// do not contain the actual normal exit, the decision must fail closed with SC013 and leave the
    /// arm Unknown rather than classify the malformed snapshot as represented iteration. The rows are
    /// equivalence partitions of the same boundary rule, not independent claims.
    /// </summary>
    [Theory]
    [InlineData("foreign-body-source")]
    [InlineData("mismatched-exit")]
    public void MalformedOwnHeaderLoopSnapshotFailsClosedWithSC013(string variant)
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateMalformedOwnHeaderLoopRequest(variant));
        var graph = Assert.Single(set.Graphs);

        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        var loop = Assert.Single(graph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemLoopCondition);
        Assert.Contains(graph.Topology.Terminals, terminal =>
            graph.Topology.Arms.Any(arm => arm.Id == terminal.Arm && arm.Decision == loop.Id)
            && terminal.Kind == ScenarioTerminalKind.Unknown);
    }

    /// <summary>
    /// Review F3: the Try-region carve-out belongs only to the compiler-lowered own-header loop. An
    /// exact own-header decision whose header sits in a genuine Catch, Filter, or Finally region must
    /// stay SC013 with both arms Unknown; an over-broad fix that admits every exception-region header
    /// fails here. The rows are equivalence partitions of the exception-region rule.
    /// </summary>
    [Theory]
    [InlineData("catch")]
    [InlineData("filter")]
    [InlineData("finally")]
    public void ExactOwnHeaderLoopInsideCatchFilterFinallyRegionFailsClosedWithSC013(string regionKind)
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateMalformedOwnHeaderLoopRequest(regionKind));
        var graph = Assert.Single(set.Graphs);

        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        var loop = Assert.Single(graph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemLoopCondition);
        Assert.DoesNotContain(graph.Topology.Terminals, terminal =>
            graph.Topology.Arms.Any(arm => arm.Id == terminal.Arm && arm.Decision == loop.Id)
            && terminal.Kind != ScenarioTerminalKind.Unknown);
    }

    /// <summary>
    /// Review F1: a decision arm whose reachable subgraph contains BOTH a represented return sink and
    /// a rejoin boundary (or an operation-derived duplicate return with a continuation) must fail
    /// closed with SC013 and classify the arm Unknown, independent of which boundary edge the
    /// traversal encounters first.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MixedOrDuplicateReturnBoundaryFailsClosedWithSC013RegardlessOfEdgeOrder(bool reverseBoundaryEdges, bool duplicateReturn)
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateMixedBoundaryTopologyRequest(reverseBoundaryEdges, duplicateReturn));
        var graph = Assert.Single(set.Graphs);

        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        var decision = Assert.Single(graph.Topology.Decisions);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
        Assert.Equal(ScenarioTerminalKind.Unknown, Assert.Single(graph.Topology.Terminals, terminal => terminal.Arm == trueArm.Id).Kind);
    }

    /// <summary>
    /// Review F2: one operation identity carried by two eligible anchors (invocation plus await, or two
    /// invocations) with DISAGREEING control memberships must withhold membership and emit SC011; the
    /// builder must not silently prefer one anchor.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisagreeingOperationAnchorsWithholdMembershipAndEmitSC011(bool duplicateInvocation)
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateDuplicateAnchorTopologyRequest(duplicateInvocation: duplicateInvocation));
        var graph = Assert.Single(set.Graphs);

        var saveNode = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemSaveOperation.Value}");
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC011" && diagnostic.Detail.Contains(ScenarioTestFactory.WorkItemSaveOperation.Value));
        Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.ScenarioNode == saveNode.Id);
    }

    /// <summary>
    /// Review F2 guard: when the eligible anchors AGREE on membership, placement is retained; the fix
    /// must not over-withhold agreeing anchors.
    /// </summary>
    [Fact]
    public void AgreeingOperationAnchorsRetainPlacement()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateDuplicateAnchorTopologyRequest(agreeing: true));
        var graph = Assert.Single(set.Graphs);

        var saveNode = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemSaveOperation.Value}");
        Assert.Contains(graph.Topology.Memberships, membership => membership.ScenarioNode == saveNode.Id);
    }

    /// <summary>
    /// Review F3: the material service-call node must carry its exact call operation and be scoped when
    /// the guarded call anchor is available; it must never stay silently unscoped.
    /// </summary>
    [Fact]
    public void ServiceCallWithAvailableAnchorIsScoped()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateServiceCallScopedRequest());
        var graph = Assert.Single(set.Graphs);

        var serviceNode = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ServiceCall);
        Assert.Equal(ScenarioTestFactory.WorkItemCallOperation, serviceNode.Operation);
        Assert.Contains(graph.Topology.Memberships, membership => membership.ScenarioNode == serviceNode.Id);
    }

    /// <summary>
    /// Review F3: a material outcome node whose exact HTTP outcome operation has no eligible Method
    /// Flow anchor stays visible and unscoped but emits SC011 instead of looking unconditional. The
    /// request carries a decision in the action flow so there are arms to place the node under; a
    /// decision-free flat graph has no arm-membership question and must not emit SC011.
    /// </summary>
    [Fact]
    public void NullOperationOutcomeStaysVisibleAndEmitsSC011()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest(decisionGuarded: true));
        var graph = Assert.Single(set.Graphs);

        var outcome = Assert.Single(graph.Nodes, node => node.Key == "outcome:200:Ok");
        Assert.Equal(ScenarioNodeKind.Outcome, outcome.Kind);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC011");
        Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.ScenarioNode == outcome.Id);
    }

    /// <summary>
    /// Review F4: the topology records must reject empty evidence, Unknown certainty, and certainty
    /// stronger than their strongest evidence — the same invariants ScenarioNode already enforces.
    /// </summary>
    [Fact]
    public void TopologyContractsRejectEmptyEvidenceUnknownCertaintyAndOversizedCertainty()
    {
        var exactEvidence = ImmutableArray.Create(ScenarioTestFactory.SourceEvidence("decision"));
        var conservativeEvidence = ImmutableArray.Create(ScenarioTestFactory.ConservativeEvidence("decision"));

        AssertRejects(() => new ScenarioDecision(new ScenarioDecisionId("d"), ScenarioTestFactory.WorkItemServiceMethod, new FlowNodeId("f"), ScenarioTestFactory.WorkItemAbsentCondition, ImmutableArray<EvidenceRef>.Empty, CertaintyLevel.Exact));
        AssertRejects(() => new ScenarioDecision(new ScenarioDecisionId("d"), ScenarioTestFactory.WorkItemServiceMethod, new FlowNodeId("f"), ScenarioTestFactory.WorkItemAbsentCondition, exactEvidence, CertaintyLevel.Unknown));
        AssertRejects(() => new ScenarioDecision(new ScenarioDecisionId("d"), ScenarioTestFactory.WorkItemServiceMethod, new FlowNodeId("f"), ScenarioTestFactory.WorkItemAbsentCondition, conservativeEvidence, CertaintyLevel.Exact));

        AssertRejects(() => new ScenarioArm(new ScenarioArmId("a"), new ScenarioDecisionId("d"), IsTrue: true, ImmutableArray<EvidenceRef>.Empty, CertaintyLevel.Exact));
        AssertRejects(() => new ScenarioArm(new ScenarioArmId("a"), new ScenarioDecisionId("d"), IsTrue: true, exactEvidence, CertaintyLevel.Unknown));
        AssertRejects(() => new ScenarioArm(new ScenarioArmId("a"), new ScenarioDecisionId("d"), IsTrue: true, conservativeEvidence, CertaintyLevel.Exact));

        AssertRejects(() => new ScenarioMembership(new ScenarioMembershipId("m"), new ScenarioArmId("a"), new ScenarioNodeId("n"), ImmutableArray<EvidenceRef>.Empty, CertaintyLevel.Exact));
        AssertRejects(() => new ScenarioMembership(new ScenarioMembershipId("m"), new ScenarioArmId("a"), new ScenarioNodeId("n"), exactEvidence, CertaintyLevel.Unknown));
        AssertRejects(() => new ScenarioMembership(new ScenarioMembershipId("m"), new ScenarioArmId("a"), new ScenarioNodeId("n"), conservativeEvidence, CertaintyLevel.Exact));

        AssertRejects(() => new ScenarioArmTerminal(new ScenarioArmId("a"), ScenarioTerminalKind.Terminates, ImmutableArray<EvidenceRef>.Empty, CertaintyLevel.Exact));
        AssertRejects(() => new ScenarioArmTerminal(new ScenarioArmId("a"), ScenarioTerminalKind.Terminates, exactEvidence, CertaintyLevel.Unknown));
        AssertRejects(() => new ScenarioArmTerminal(new ScenarioArmId("a"), ScenarioTerminalKind.Terminates, conservativeEvidence, CertaintyLevel.Exact));
    }

    /// <summary>
    /// Review F4: a supported terminating arm's terminal fact must aggregate the decision, traversed
    /// edge, and boundary evidence and degrade to the weakest supported certainty rather than inherit
    /// the exact decision certainty alone.
    /// </summary>
    [Fact]
    public void TerminalEvidenceAggregatesDecisionEdgeAndBoundaryAndDegradesToWeakestCertainty()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateDegradedTerminalEvidenceRequest());
        var graph = Assert.Single(set.Graphs);

        var decision = Assert.Single(graph.Topology.Decisions);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
        var terminal = Assert.Single(graph.Topology.Terminals, candidate => candidate.Arm == trueArm.Id);
        Assert.Equal(ScenarioTerminalKind.Terminates, terminal.Kind);
        Assert.Contains(terminal.Evidence, evidence => evidence.Artifact == ScenarioTestFactory.WorkItemDecisionEvidence);
        Assert.Contains(terminal.Evidence, evidence => evidence.Artifact == ScenarioTestFactory.WorkItemBoundaryEdgeEvidence);
        Assert.Contains(terminal.Evidence, evidence => evidence.Artifact == ScenarioTestFactory.WorkItemBoundaryTerminalEvidence);
        Assert.Equal(CertaintyLevel.Conservative, terminal.Certainty);
    }

    /// <summary>
    /// Review F5: SC012 withholds only the conflicting decision's memberships; valid membership under
    /// another decision remains.
    /// </summary>
    [Fact]
    public void Sc012WithholdsOnlyConflictingDecisionAndRetainsUnrelatedMembership()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateScopedConflictTopologyRequest());
        var graph = Assert.Single(set.Graphs);

        var saveNode = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemSaveOperation.Value}");
        var absent = Assert.Single(graph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemAbsentCondition);
        var locked = Assert.Single(graph.Topology.Decisions, decision => decision.Condition == ScenarioTestFactory.WorkItemLockedCondition);
        var absentArms = graph.Topology.Arms.Where(arm => arm.Decision == absent.Id).Select(arm => arm.Id).ToHashSet();
        var lockedFalse = Assert.Single(graph.Topology.Arms, arm => arm.Decision == locked.Id && !arm.IsTrue);

        Assert.Single(graph.Diagnostics, diagnostic => diagnostic.Code == "SC012");
        Assert.Contains(graph.Topology.Memberships, membership => membership.Arm == lockedFalse.Id && membership.ScenarioNode == saveNode.Id);
        Assert.DoesNotContain(graph.Topology.Memberships, membership => absentArms.Contains(membership.Arm) && membership.ScenarioNode == saveNode.Id);
    }

    /// <summary>
    /// Review F5: every same-decision conflict is reported as its own SC012 in deterministic order;
    /// the first conflict never hides later ones.
    /// </summary>
    [Fact]
    public void MultipleSc012ConflictsAreAllReportedInDeterministicOrder()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateMultipleConflictTopologyRequest());
        var graph = Assert.Single(set.Graphs);

        var conflicts = graph.Diagnostics.Where(diagnostic => diagnostic.Code == "SC012").ToArray();
        Assert.Equal(2, conflicts.Length);
        Assert.Equal(
            conflicts.Select(diagnostic => diagnostic.Id.Value).OrderBy(value => value, StringComparer.Ordinal),
            conflicts.Select(diagnostic => diagnostic.Id.Value));
        foreach (var decision in graph.Topology.Decisions)
        {
            Assert.Contains(conflicts, diagnostic => diagnostic.Detail.Contains(decision.ControllingFlowNode.Value));
        }
    }

    /// <summary>
    /// Review F6: decision and arm identities are derived only from profile, root and containing
    /// method, controlling flow node, and polarity — never the entry point — so changing only the
    /// route identity must not churn them. A membership identity is derived from the profile, root
    /// method, parent arm identity, and the controlled scenario node identity; the parent arm is
    /// route-independent while the controlled node belongs to its own entry-point graph, so the
    /// frozen contract guarantees the same membership count and member arm identities (not the same
    /// membership hash) across a route change.
    /// </summary>
    [Fact]
    public void TopologyIdentitiesRemainEqualWhenOnlyRouteIdentityChanges()
    {
        var original = Assert.Single(ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateWorkItemTopologyRequest()).Graphs);
        var relocated = Assert.Single(ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateWorkItemTopologyRequest(entryPointId: ScenarioTestFactory.WorkItemRelocatedEntryPoint)).Graphs);

        Assert.Equal(
            original.Topology.Decisions.Select(decision => decision.Id.Value).OrderBy(value => value, StringComparer.Ordinal),
            relocated.Topology.Decisions.Select(decision => decision.Id.Value).OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            original.Topology.Arms.Select(arm => arm.Id.Value).OrderBy(value => value, StringComparer.Ordinal),
            relocated.Topology.Arms.Select(arm => arm.Id.Value).OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            original.Topology.Memberships.Select(membership => membership.Arm.Value).OrderBy(value => value, StringComparer.Ordinal),
            relocated.Topology.Memberships.Select(membership => membership.Arm.Value).OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            original.Topology.Memberships.Length,
            relocated.Topology.Memberships.Length);
    }

    /// <summary>
    /// Review F7: canonical topology order is controlling flow-node identity, then semantic polarity
    /// (false before true), then controlled scenario-node identity — the frozen architecture decision rule, never
    /// hashed decision or arm identity order.
    /// </summary>
    [Fact]
    public void CanonicalTopologyOrderIsControllingFlowNodeThenPolarityThenControlledNode()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCanonicalOrderTopologyRequest());
        var graph = Assert.Single(set.Graphs);

        var decisionById = graph.Topology.Decisions.ToDictionary(decision => decision.Id);
        var armById = graph.Topology.Arms.ToDictionary(arm => arm.Id);

        var expectedArms = graph.Topology.Arms
            .OrderBy(arm => decisionById[arm.Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
            .ThenBy(arm => arm.IsTrue)
            .Select(arm => arm.Id.Value);
        Assert.Equal(expectedArms, graph.Topology.Arms.Select(arm => arm.Id.Value));

        var expectedMemberships = graph.Topology.Memberships
            .OrderBy(membership => decisionById[armById[membership.Arm].Decision].ControllingFlowNode.Value, StringComparer.Ordinal)
            .ThenBy(membership => armById[membership.Arm].IsTrue)
            .ThenBy(membership => membership.ScenarioNode.Value, StringComparer.Ordinal)
            .Select(membership => membership.Id.Value);
        Assert.Equal(expectedMemberships, graph.Topology.Memberships.Select(membership => membership.Id.Value));
    }

    /// <summary>
    /// Review F8: legacy source-compatible ScenarioGraph construction must yield a non-null empty
    /// Topology instead of a null default.
    /// </summary>
    [Fact]
    public void LegacyScenarioGraphConstructionYieldsNonNullEmptyTopology()
    {
        var graph = new ScenarioGraph(
            ScenarioTestFactory.WorkItemEntryPoint,
            ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.WorkItemActionMethod,
            HttpMethodKind.Get,
            "api/WorkItems/{id}",
            "GET api/WorkItems/{id}",
            ImmutableArray<ScenarioNode>.Empty,
            ImmutableArray<ScenarioEdge>.Empty,
            ImmutableArray<ScenarioGraphDiagnostic>.Empty,
            "debug");

        Assert.NotNull(graph.Topology);
        Assert.True(graph.Topology.Decisions.IsEmpty);
        Assert.True(graph.Topology.Arms.IsEmpty);
        Assert.True(graph.Topology.Memberships.IsEmpty);
        Assert.True(graph.Topology.Terminals.IsEmpty);
    }

    [Fact]
    public void PublicConstructorsPreserveFourAndSixParameterMetadataSignatures()
    {
        var constructors = typeof(ScenarioTopology).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var legacyTypes = new[]
        {
            typeof(ImmutableArray<ScenarioDecision>), typeof(ImmutableArray<ScenarioArm>),
            typeof(ImmutableArray<ScenarioMembership>), typeof(ImmutableArray<ScenarioArmTerminal>)
        };
        var currentTypes = legacyTypes.Concat(new[]
        {
            typeof(ImmutableArray<ScenarioFlowContainer>), typeof(ImmutableArray<ScenarioFlowPlacement>)
        }).ToArray();

        var legacy = Assert.Single(constructors, constructor => constructor.GetParameters()
            .Select(parameter => parameter.ParameterType).SequenceEqual(legacyTypes));
        Assert.NotNull(Assert.Single(constructors, constructor => constructor.GetParameters()
            .Select(parameter => parameter.ParameterType).SequenceEqual(currentTypes)));

        var topology = (ScenarioTopology)legacy.Invoke(new object[]
        {
            default(ImmutableArray<ScenarioDecision>), default(ImmutableArray<ScenarioArm>),
            default(ImmutableArray<ScenarioMembership>), default(ImmutableArray<ScenarioArmTerminal>)
        });

        Assert.True(topology.Decisions.IsEmpty);
        Assert.True(topology.Arms.IsEmpty);
        Assert.True(topology.Memberships.IsEmpty);
        Assert.True(topology.Terminals.IsEmpty);
        Assert.True(topology.FlowContainers.IsEmpty);
        Assert.True(topology.FlowPlacements.IsEmpty);
    }

    [Fact]
    public void ConfiguredScenarioGraphRejectsUndefinedRootKindAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScenarioGraph(
            ScenarioTestFactory.WorkItemEntryPoint,
            ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.WorkItemActionMethod,
            HttpMethodKind.Get,
            "api/WorkItems/{id}",
            "GET api/WorkItems/{id}",
            ImmutableArray<ScenarioNode>.Empty,
            ImmutableArray<ScenarioEdge>.Empty,
            ImmutableArray<ScenarioGraphDiagnostic>.Empty,
            "debug",
            ScenarioTopology.Empty,
            rootKind: (ScenarioRootKind)999));
    }

    private static ScenarioGraph BuildWorkItemGraph()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateWorkItemTopologyRequest());
        return Assert.Single(set.Graphs);
    }

    private static string CollectTopology(ScenarioGraph graph) => string.Join(
        "\n",
        graph.Topology.Decisions
            .Select(decision => $"d:{decision.Id.Value}:{decision.Condition.Value}")
            .Concat(graph.Topology.Arms.Select(arm => $"a:{arm.Id.Value}:{arm.Decision.Value}:{arm.IsTrue}"))
            .Concat(graph.Topology.Memberships.Select(membership => $"m:{membership.Id.Value}:{membership.Arm.Value}:{membership.ScenarioNode.Value}"))
            .Concat(graph.Topology.Terminals.Select(terminal => $"t:{terminal.Arm.Value}:{terminal.Kind}"))
            .Order(StringComparer.Ordinal));

    private static void AssertRejects(Func<object> construct) => Assert.ThrowsAny<ArgumentException>(construct);
}
