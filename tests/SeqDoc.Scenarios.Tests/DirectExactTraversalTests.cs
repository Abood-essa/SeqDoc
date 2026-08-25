using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// CT-6 write-first contract.  DirectExactTraversalFixture is deliberately a neutral, reusable
/// call-tree fixture: its partitions vary compiler facts, not product names.  The expected seam is
/// ScenarioGraph.DirectCallExpansion, populated by ScenarioGraphBuilder for a configured root.
/// </summary>
public sealed class DirectExactTraversalTests
{
    [Fact]
    public void ExpansionContractRejectsChildBeforeParentAndCompleteCycleBoundary()
    {
        var evidence = ImmutableArray.Create(ScenarioTestFactory.SourceEvidence("direct-contract"));
        var parent = new ScenarioDirectCallExpansionStep("parent", null, 1,
            new MethodId("caller"), new MethodId("target"), new OperationId("parent-operation"),
            new ScenarioNodeId("parent-node"), 0, evidence, SeqDoc.Core.Evidence.CertaintyLevel.Exact, true);
        var child = new ScenarioDirectCallExpansionStep("child", "parent", 2,
            new MethodId("target"), new MethodId("leaf"), new OperationId("child-operation"),
            new ScenarioNodeId("child-node"), 0, evidence, SeqDoc.Core.Evidence.CertaintyLevel.Exact, true);

        Assert.Throws<ArgumentException>(() => new ScenarioDirectCallExpansion([child, parent], true, []));
        var cycle = new ScenarioDirectCallExpansionStep("cycle", null, 1,
            new MethodId("caller"), new MethodId("target"), new OperationId("cycle-operation"),
            new ScenarioNodeId("cycle-node"), 0, evidence, SeqDoc.Core.Evidence.CertaintyLevel.Exact, true,
            isCycleBoundary: true);
        Assert.Throws<ArgumentException>(() => new ScenarioDirectCallExpansion([cycle], true, []));
    }

    [Fact]
    public void ExactRootAndAvailableChildrenAreTypedDepthFirstStepsAndCalls()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("depth-two");
        var expansion = graph.DirectCallExpansion;

        Assert.Equal([1, 2, 1], expansion.Steps.Select(step => step.Depth));
        Assert.All(expansion.Steps, step => Assert.Equal(ScenarioNodeKind.MethodCall,
            graph.Nodes.Single(node => node.Id == step.ScenarioNodeId).Kind));
        Assert.Equal(expansion.Steps.Length, graph.Edges.Count(edge => edge.Kind == ScenarioEdgeKind.Call));
        Assert.Equal(["operation:v1:root.first", "operation:v1:child.first", "operation:v1:root.second"],
            expansion.Steps.Select(step => step.Operation.Value));
        Assert.All(expansion.Steps, step =>
        {
            Assert.NotEmpty(step.Evidence);
            Assert.NotEqual(SeqDoc.Core.Evidence.CertaintyLevel.Unknown, step.Certainty);
        });
    }

    [Fact]
    public void ReversedFactsHaveIdenticalExpansionIdentityDebugProjectionAndPlan()
    {
        var normal = DirectExactTraversalFixture.BuildGraph("depth-two");
        var reversed = DirectExactTraversalFixture.BuildGraph("depth-two-reversed");

        Assert.Equal(normal.DebugProjection, reversed.DebugProjection);
        Assert.Equal(normal.DirectCallExpansion!.Steps.Select(step => step.Id),
            reversed.DirectCallExpansion!.Steps.Select(step => step.Id));
        Assert.Equal(DirectExactTraversalFixture.Plan(normal), DirectExactTraversalFixture.Plan(reversed));
    }

    [Fact]
    public void DuplicateInvocationAnchorsRequireAgreementBeforeOneCanonicalStep()
    {
        var agreeing = DirectExactTraversalFixture.BuildGraph("duplicate-agreeing");
        var disagreeing = DirectExactTraversalFixture.BuildGraph("duplicate-disagreeing");

        Assert.Single(agreeing.DirectCallExpansion!.Steps,
            step => step.Operation.Value == "operation:v1:root.first");
        Assert.Empty(disagreeing.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall));
        Assert.Empty(disagreeing.DirectCallExpansion!.Steps);
        Assert.False(disagreeing.DirectCallExpansion.IsComplete);
        Assert.Contains(disagreeing.DirectCallExpansion.Diagnostics,
            diagnostic => diagnostic.Code == "SC-DIRECT-DUPLICATE" && !diagnostic.Evidence.IsDefaultOrEmpty);
    }

    [Fact]
    public void DeepChainExpandsInDeterministicDepthFirstChronologyWithoutDepthBoundary()
    {
        var expansion = DirectExactTraversalFixture.BuildGraph("deep-chain").DirectCallExpansion;

        Assert.Equal(1024, expansion.Steps.Length);
        Assert.Equal(Enumerable.Range(1, 1024), expansion.Steps.Select(step => step.Depth));
        Assert.Equal(Enumerable.Range(0, 1024).Select(index => $"operation:v1:chain.{index:D3}"),
            expansion.Steps.Select(step => step.Operation.Value));
        Assert.False(expansion.IsComplete);
        Assert.False(expansion.Steps[^1].IsComplete);
        Assert.Equal("operation:v1:chain.1023", expansion.Steps[^1].Operation.Value);
        var budgetDiagnostic = Assert.Single(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-METHOD-BUDGET");
        Assert.Contains("1024", budgetDiagnostic.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(budgetDiagnostic.Evidence);
        Assert.DoesNotContain(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-DEPTH");
    }

    [Fact]
    public void DeepChainReversedFactsProduceIdenticalExpansionAndPlan()
    {
        var normal = DirectExactTraversalFixture.BuildGraph("deep-chain");
        var reversed = DirectExactTraversalFixture.BuildGraph("deep-chain-reversed");

        Assert.Equal(normal.DebugProjection, reversed.DebugProjection);
        Assert.Equal(normal.DirectCallExpansion!.Steps.Select(step => step.Id), reversed.DirectCallExpansion!.Steps.Select(step => step.Id));
        Assert.Equal(normal.DirectCallExpansion.Steps.Select(step => (step.Depth, step.Operation.Value, step.IsComplete, step.IsCycleBoundary)),
            reversed.DirectCallExpansion.Steps.Select(step => (step.Depth, step.Operation.Value, step.IsComplete, step.IsCycleBoundary)));
        Assert.Equal(normal.DirectCallExpansion.Diagnostics.Select(item => $"{item.Code}|{item.Detail}|{string.Join(',', item.Evidence.Select(evidence => evidence.Id.Value))}"),
            reversed.DirectCallExpansion.Diagnostics.Select(item => $"{item.Code}|{item.Detail}|{string.Join(',', item.Evidence.Select(evidence => evidence.Id.Value))}"));
        Assert.Equal(DirectExactTraversalFixture.Plan(normal), DirectExactTraversalFixture.Plan(reversed));
    }

    [Fact]
    public void CallBudgetKeepsExactDeterministicPrefixAndNamesConfiguredLimit()
    {
        var expansion = DirectExactTraversalFixture.BuildGraph("deep-chain", new DiagramBudget(1024, 4, 1024, 256, 45_000)).DirectCallExpansion;

        Assert.Equal(4, expansion.Steps.Length);
        Assert.Equal(["operation:v1:chain.000", "operation:v1:chain.001", "operation:v1:chain.002", "operation:v1:chain.003"],
            expansion.Steps.Select(step => step.Operation.Value));
        var diagnostic = Assert.Single(expansion.Diagnostics, item => item.Code == "SC-DIRECT-CALL-BUDGET");
        Assert.Contains("4", diagnostic.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(diagnostic.Evidence);
        Assert.False(expansion.IsComplete);
    }

    [Fact]
    public void MethodBudgetCountsConfiguredRootAndPreservesIncompleteBoundaryCallSite()
    {
        var expansion = DirectExactTraversalFixture.BuildGraph("deep-chain", new DiagramBudget(3, 1024, 1024, 256, 45_000)).DirectCallExpansion;

        Assert.Equal(3, expansion.Steps.Length);
        Assert.Equal(["operation:v1:chain.000", "operation:v1:chain.001", "operation:v1:chain.002"],
            expansion.Steps.Select(step => step.Operation.Value));
        var boundary = Assert.Single(expansion.Steps.Where(step => !step.IsComplete));
        Assert.Equal("operation:v1:chain.002", boundary.Operation.Value);
        var diagnostic = Assert.Single(expansion.Diagnostics, item => item.Code == "SC-DIRECT-METHOD-BUDGET");
        Assert.Contains("3", diagnostic.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(diagnostic.Evidence);
    }

    [Fact]
    public void SharedCalleeConsumesDistinctMethodOnceButEachOccurrenceConsumesCallBudget()
    {
        var expansion = DirectExactTraversalFixture.BuildGraph("shared-callee", new DiagramBudget(3, 3, 1024, 256, 45_000)).DirectCallExpansion;

        Assert.Equal(3, expansion.Steps.Length);
        Assert.Equal(["operation:v1:root.first", "operation:v1:shared.first", "operation:v1:root.second"],
            expansion.Steps.Select(step => step.Operation.Value));
        Assert.Contains(expansion.Diagnostics, item => item.Code == "SC-DIRECT-CALL-BUDGET");
        Assert.DoesNotContain(expansion.Diagnostics, item => item.Code == "SC-DIRECT-METHOD-BUDGET");
        Assert.Equal(2, expansion.Steps.Count(step => step.TargetMethod == DirectExactTraversalFixture.SharedCallee));
    }

    [Theory]
    [InlineData("direct-recursion")]
    [InlineData("mutual-recursion")]
    public void CyclesAreVisibleAtTheirCallSiteButNeverReentered(string partition)
    {
        var expansion = DirectExactTraversalFixture.BuildGraph(partition).DirectCallExpansion;

        string[] expectedCycleOperations = partition == "direct-recursion"
            ? ["operation:v1:self.call"]
            : ["operation:v1:mutual.back"];
        Assert.Equal(expectedCycleOperations,
            expansion.Steps.Where(step => step.IsCycleBoundary).Select(step => step.Operation.Value));
        Assert.Equal(expectedCycleOperations,
            expansion.Steps.Select(step => step.Operation.Value).Where(operation => expectedCycleOperations.Contains(operation)));
        Assert.All(expansion.Steps.Where(step => step.IsCycleBoundary), step => Assert.False(step.IsComplete));
        Assert.False(expansion.IsComplete);
        Assert.Contains(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-CYCLE");
    }

    [Fact]
    public void SharedCalleeUsesDistinctPathIdentitiesAndChronologicalDescendants()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("shared-callee");
        var expansion = graph.DirectCallExpansion;
        var shared = expansion.Steps.Where(step => step.TargetMethod == DirectExactTraversalFixture.SharedCallee).ToArray();

        Assert.Equal(2, shared.Length);
        Assert.Equal(2, shared.Select(step => step.Id).Distinct().Count());
        Assert.Equal(["operation:v1:root.first", "operation:v1:shared.first", "operation:v1:root.second", "operation:v1:shared.first"],
            expansion.Steps.Select(step => step.Operation.Value));
        var plan = DocumentationPlanner.Plan(graph);
        Assert.Contains(plan.Diagram.Messages, message => message.Source == "fixture_shared"
            && message.Target == "fixture_leaf" && message.Label == "shared.first");
    }

    [Theory]
    [InlineData("cha")]
    [InlineData("incomplete")]
    [InlineData("ambiguous")]
    [InlineData("platform")]
    [InlineData("dynamic")]
    [InlineData("delegate")]
    [InlineData("constructor")]
    [InlineData("nested-function")]
    public void NonExactMaterialPartitionsAreNotTraversed(string partition)
    {
        var graph = DirectExactTraversalFixture.BuildGraph(partition);
        var expansion = graph.DirectCallExpansion;

        Assert.Empty(expansion.Steps.Where(step => step.Depth > 1));
        Assert.Empty(expansion.Diagnostics);
    }

    [Theory]
    [InlineData("body-unavailable", "SC-DIRECT-BODY-UNAVAILABLE")]
    [InlineData("unloaded-project", "SC-DIRECT-SOURCE-UNAVAILABLE")]
    [InlineData("metadata-target", "SC-DIRECT-SOURCE-UNAVAILABLE")]
    [InlineData("generated-target", "SC-DIRECT-SOURCE-UNAVAILABLE")]
    public void ExpansionBoundariesKeepParentVisibleAndWithholdChildren(string partition, string code)
    {
        var graph = DirectExactTraversalFixture.BuildGraph(partition);
        var expansion = graph.DirectCallExpansion;

        Assert.Contains(expansion.Steps, step => step.Depth == 1);
        Assert.DoesNotContain(expansion.Steps, step => step.Depth > 2);
        Assert.False(expansion.IsComplete);
        Assert.Contains(expansion.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("sensitive-aws", "AKIA" + "1234567890ABCDEF")]
    [InlineData("sensitive-github", "ghp_test_credential_value")]
    [InlineData("sensitive-jwt", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("sensitive-openai", "sk-test-credential-value-123")]
    [InlineData("sensitive-generic", "Abcdefghijklmnop1234")]
    public void SensitiveArgumentValuesNeverReachScenarioOrWordingProjection(string partition, string secret)
    {
        var graph = DirectExactTraversalFixture.BuildGraph(partition);
        var projection = DocumentationPlanner.Plan(graph).Diagram.DebugProjection?.ToString() ?? string.Empty;

        Assert.DoesNotContain(secret, projection, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIA", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", projection, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("foreign-behavior-profile")]
    [InlineData("foreign-behavior-fingerprint")]
    public void ForeignBehaviorSnapshotsWithholdConfiguredDirectExpansion(string partition)
    {
        var graph = DirectExactTraversalFixture.BuildGraph(partition);

        Assert.Empty(graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall));
        Assert.Empty(graph.DirectCallExpansion!.Steps);
        Assert.False(graph.DirectCallExpansion.IsComplete);
        var diagnostic = Assert.Single(graph.DirectCallExpansion.Diagnostics,
            item => item.Code == "SC-DIRECT-MISMATCH");
        Assert.NotEmpty(diagnostic.Evidence);
        Assert.Equal("SC-DIRECT-MISMATCH", diagnostic.Code);
    }

    [Theory]
    [InlineData("no-flow", "SC-DIRECT-NO-FLOW")]
    [InlineData("ambiguous-flow", "SC-DIRECT-AMBIGUOUS-FLOW")]
    public void MissingAndAmbiguousTargetFlowsHaveDistinctConservativeDiagnostics(string partition, string code)
    {
        var expansion = DirectExactTraversalFixture.BuildGraph(partition).DirectCallExpansion;

        Assert.Contains(expansion.Steps, step => step.Depth == 1);
        Assert.Contains(expansion.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.DoesNotContain(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-MISMATCH");
    }

    /// <summary>
    /// Generic loaded cross-project traversal is allowed when both projects are loaded in the same
    /// compilation and the target has a MethodFlow. The foreign-project partition places Child
    /// in a different project than Root, and traversal expands into it without a cross-project stop.
    /// </summary>
    [Fact]
    public void CrossProjectTraversalExpandsWhenBothProjectsAreLoaded()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("foreign-project");
        var expansion = graph.DirectCallExpansion;

        // The cross-project boundary is no longer emitted; traversal expands into the child.
        Assert.Contains(expansion.Steps, step => step.Depth == 1);
        Assert.DoesNotContain(expansion.Diagnostics,
            diagnostic => diagnostic.Code == "SC-DIRECT-CROSS-PROJECT");
        // The child body was traversed: Child's call to Grandchild is visible as a MethodCall node.
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
    }

    [Fact]
    public void ExactGuardedChildIsAdmittedOnceUnderItsLocalTrueArmAndNotFlat()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("inherited-arm-and-guarded-child");
        var expansion = graph.DirectCallExpansion;

        var rootGuarded = Assert.Single(expansion.Steps, step => step.Operation.Value == "operation:v1:root.first");
        Assert.NotEmpty(rootGuarded.RootArmIds);
        var child = Assert.Single(expansion.Steps, step => step.Operation.Value == "operation:v1:child.first");
        Assert.DoesNotContain(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-GUARDED");

        Assert.Single(expansion.Steps, step => step.Operation.Value == "operation:v1:child.first");
        Assert.Contains(graph.Topology.Memberships, membership => membership.ScenarioNode == child.ScenarioNodeId
            && rootGuarded.RootArmIds.Contains(membership.Arm));
        Assert.Contains(graph.Topology.Memberships, membership => membership.ScenarioNode == child.ScenarioNodeId
            && !rootGuarded.RootArmIds.Contains(membership.Arm));
        var childDecision = Assert.Single(graph.Topology.Decisions,
            decision => decision.Condition.Value == "operation:v1:Child.local-guard");
        var localTrue = Assert.Single(graph.Topology.Arms,
            arm => arm.Decision == childDecision.Id && arm.IsTrue);
        var localFalse = Assert.Single(graph.Topology.Arms,
            arm => arm.Decision == childDecision.Id && !arm.IsTrue);
        Assert.Contains(graph.Topology.Memberships,
            membership => membership.Arm == localTrue.Id && membership.ScenarioNode == child.ScenarioNodeId);
        Assert.DoesNotContain(graph.Topology.Memberships,
            membership => membership.Arm == localFalse.Id && membership.ScenarioNode == child.ScenarioNodeId);
        Assert.DoesNotContain(graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall), node =>
            node.Id != child.ScenarioNodeId && node.Operation == child.Operation);
    }

    [Fact]
    public void NestedLocalGuardsPreserveParentagePolarityAndWeakestOccurrenceEvidence()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("conservative-nested-local-guards");
        var expansion = graph.DirectCallExpansion!;
        var child = Assert.Single(expansion.Steps, step => step.Operation.Value == "operation:v1:child.first");
        var grandchild = Assert.Single(expansion.Steps, step => step.Operation.Value == "operation:v1:grandchild.first");

        Assert.Equal(child.Id, grandchild.ParentStepId);
        Assert.Equal(3, grandchild.Depth);
        var grandchildMemberships = graph.Topology.Memberships
            .Where(item => item.ScenarioNode == grandchild.ScenarioNodeId).ToArray();
        Assert.NotEmpty(grandchildMemberships);
        Assert.All(grandchildMemberships, item =>
            Assert.Equal(SeqDoc.Core.Evidence.CertaintyLevel.Conservative, item.Certainty));
        Assert.All(grandchildMemberships, item =>
            Assert.Contains(item.Evidence, evidence => evidence.Id.Value == "evidence:v1:ct6-fixture"));
        Assert.All(grandchildMemberships, item =>
            Assert.Contains(item.Evidence, evidence => evidence.Id.Value == "evidence:v1:call-resolution"));
        var grandchildDecision = Assert.Single(graph.Topology.Decisions,
            decision => decision.Condition.Value == "operation:v1:Grandchild.local-guard");
        Assert.Contains(graph.Topology.Arms, arm => arm.Decision == grandchildDecision.Id && arm.IsTrue);
        Assert.Contains(graph.Topology.Arms, arm => arm.Decision == grandchildDecision.Id && !arm.IsTrue);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-GUARDED");
    }

    [Fact]
    public void SharedGuardedOccurrencesHaveDistinctTopologyAndChronologicalChildren()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("shared-guarded-occurrences");
        var expansion = graph.DirectCallExpansion!;
        var shared = expansion.Steps.Where(step => step.TargetMethod == DirectExactTraversalFixture.SharedCallee).ToArray();
        var children = expansion.Steps.Where(step => step.Operation.Value == "operation:v1:shared.first").ToArray();

        Assert.Equal(2, shared.Length);
        Assert.Equal(2, children.Length);
        Assert.Equal(2, children.Select(step => step.Id).Distinct().Count());
        Assert.Equal([shared[0].Id, shared[1].Id], children.Select(step => step.ParentStepId));
        Assert.Equal(["operation:v1:root.first", "operation:v1:shared.first", "operation:v1:root.second", "operation:v1:shared.first"],
            expansion.Steps.Select(step => step.Operation.Value));
        Assert.DoesNotContain(expansion.Diagnostics, diagnostic => diagnostic.Code == "SC-DIRECT-GUARDED");

        var plan = DocumentationPlanner.Plan(graph).Diagram;
        var fragments = plan.Sequence.Fragments.SelectMany(AllFragments).ToArray();
        var decisions = graph.Topology.Decisions
            .Where(decision => decision.Condition == new OperationId("operation:v1:Shared.local-guard"))
            .ToArray();
        Assert.Equal(2, decisions.Length);
        Assert.All(decisions, decision => Assert.False(string.IsNullOrWhiteSpace(decision.OccurrenceScope)));
        var occurrenceFragments = decisions.Select(decision =>
        {
            var occurrenceStep = expansion.Steps.Single(step => step.Id == decision.OccurrenceScope);
            var child = expansion.Steps.Single(step => step.ParentStepId == occurrenceStep.Id
                && step.Operation.Value == "operation:v1:shared.first");
            var arm = Assert.Single(graph.Topology.Arms, candidate => candidate.Decision == decision.Id && candidate.IsTrue);
            Assert.Single(graph.Topology.Memberships,
                candidate => candidate.Arm == arm.Id && candidate.ScenarioNode == child.ScenarioNodeId);
            var edge = Assert.Single(graph.Edges, candidate => candidate.Target == child.ScenarioNodeId);
            var messageId = new DiagramPlanElementId("diagram-element:v1:message:" + edge.Id.Value);
            var fragment = Assert.Single(fragments, candidate => candidate.Key
                == "decision:occurrence:v1:"
                    + decision.Condition.Value.Length + ":" + decision.Condition.Value
                    + decision.OccurrenceScope!.Length + ":" + decision.OccurrenceScope);
            Assert.Contains(messageId, fragment.Arms.SelectMany(candidate => candidate.MessageRefs)
                .Concat(fragment.Arms.SelectMany(candidate => AllMessageRefs(candidate.Fragments))));
            return (fragment, arm, messageId);
        }).ToArray();
        Assert.Equal(2, occurrenceFragments.Length);
        Assert.Equal(2, occurrenceFragments.Select(item => item.fragment.Id).Distinct().Count());
        Assert.Equal(2, occurrenceFragments.Select(item => item.arm.Id).Distinct().Count());
        Assert.Equal(2, occurrenceFragments.Select(item => item.fragment).SelectMany(fragment => fragment.Arms)
            .SelectMany(arm => arm.Fragments)
            .Where(fragment => fragment.Kind == DiagramFragmentKind.Break)
            .Select(fragment => fragment.Id).Distinct().Count());
        Assert.Equal(2, occurrenceFragments.Select(item => item.fragment).SelectMany(fragment => fragment.Arms)
            .SelectMany(arm => arm.MessageRefs).Distinct().Count());
        Assert.Equal(2, occurrenceFragments.Select(item => item.messageId).Distinct().Count());
    }

    [Fact]
    public void GuardedExpansionRemainsDeterministicAcrossEveryReversedFactCollection()
    {
        var normal = DirectExactTraversalFixture.BuildGraph("shared-guarded-occurrences");
        var reversed = DirectExactTraversalFixture.BuildGraph("shared-guarded-occurrences-reversed");

        Assert.Equal(normal.DebugProjection, reversed.DebugProjection);
        Assert.Equal(normal.DirectCallExpansion!.Steps.Select(step => step.Id), reversed.DirectCallExpansion!.Steps.Select(step => step.Id));
        Assert.Equal(DirectExactTraversalFixture.Plan(normal), DirectExactTraversalFixture.Plan(reversed));
        Assert.Equal(normal.Diagnostics.Select(item => item.Code), reversed.Diagnostics.Select(item => item.Code));
        Assert.Equal(DocumentationPlanner.Plan(normal).Diagram.DebugProjection,
            DocumentationPlanner.Plan(reversed).Diagram.DebugProjection);
    }

    [Fact]
    public void SwitchControlledChildAndDescendantsAreWithheldWhileSiblingAndIdentitiesRemainDeterministic()
    {
        var normal = DirectExactTraversalFixture.BuildGraph("switch-controlled-child");
        var reversed = DirectExactTraversalFixture.BuildGraph("switch-controlled-child-reversed");
        var normalPlan = DocumentationPlanner.Plan(normal).Diagram;
        var reversedPlan = DocumentationPlanner.Plan(reversed).Diagram;
        var withheldOperations = new[] { "operation:v1:child.first", "operation:v1:grandchild.first" };
        var withheldNodeIds = normal.Nodes
            .Where(node => node.Operation is { } operation && withheldOperations.Contains(operation.Value))
            .Select(node => node.Id)
            .ToHashSet();

        var boundary = Assert.Single(normal.Diagnostics,
            diagnostic => diagnostic.Code == "SC013"
                && diagnostic.Detail.Contains("switch:", StringComparison.Ordinal));
        Assert.NotEmpty(boundary.Evidence);
        Assert.Equal(SeqDoc.Core.Evidence.CertaintyLevel.Exact, boundary.Certainty);

        Assert.DoesNotContain(normal.DirectCallExpansion!.Steps, step => withheldOperations.Contains(step.Operation.Value));
        Assert.DoesNotContain(normal.Nodes, node => node.Operation is { } operation && withheldOperations.Contains(operation.Value));
        Assert.DoesNotContain(normal.Edges, edge => withheldNodeIds.Contains(edge.Source) || withheldNodeIds.Contains(edge.Target));
        Assert.DoesNotContain(normal.Topology.Memberships, membership =>
            normal.Nodes.Single(node => node.Id == membership.ScenarioNode).Operation is { } operation
            && withheldOperations.Contains(operation.Value));
        Assert.DoesNotContain(normalPlan.Messages, message => withheldOperations.Any(operation => message.Label.Contains(operation, StringComparison.Ordinal)));
        Assert.Equal(1, normal.DirectCallExpansion.Steps.Count(step => step.Operation.Value == "operation:v1:root.first"));
        var parentNode = Assert.Single(normal.Nodes, node => node.Operation?.Value == "operation:v1:root.first");
        Assert.Single(normal.Edges, edge => edge.Kind == ScenarioEdgeKind.Call && edge.Target == parentNode.Id);
        Assert.Equal(1, normalPlan.Messages.Count(message => message.Label == "root.first"));
        Assert.Equal(1, normal.DirectCallExpansion.Steps.Count(step => step.Operation.Value == "operation:v1:root.second"));
        Assert.Equal(1, normal.Nodes.Count(node => node.Operation?.Value == "operation:v1:root.second"));
        Assert.Equal(1, normalPlan.Messages.Count(message => message.Label == "root.second"));
        var graphDebug = normal.DebugProjection?.ToString() ?? string.Empty;
        var diagramDebug = normalPlan.DebugProjection?.ToString() ?? string.Empty;
        Assert.Contains("root.first", graphDebug, StringComparison.Ordinal);
        Assert.Contains("root.first", diagramDebug, StringComparison.Ordinal);
        Assert.DoesNotContain("child.first", diagramDebug, StringComparison.Ordinal);
        Assert.DoesNotContain("grandchild.first", diagramDebug, StringComparison.Ordinal);
        Assert.Contains("child.first", boundary.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("root.second", boundary.Detail, StringComparison.Ordinal);

        Assert.Equal(normal.DebugProjection, reversed.DebugProjection);
        Assert.Equal(normalPlan.DebugProjection, reversedPlan.DebugProjection);
        Assert.Equal(normal.Diagnostics.Select(FormatDiagnostic), reversed.Diagnostics.Select(FormatDiagnostic));
        Assert.Equal(normal.DirectCallExpansion.Steps.Select(step => step.Id), reversed.DirectCallExpansion!.Steps.Select(step => step.Id));
        Assert.Equal(normal.Nodes.Select(node => node.Id), reversed.Nodes.Select(node => node.Id));
        Assert.Equal(normal.Edges.Select(edge => edge.Id), reversed.Edges.Select(edge => edge.Id));
    }

    private static string FormatDiagnostic(ScenarioGraphDiagnostic diagnostic) =>
        $"{diagnostic.Code}|{diagnostic.Detail}|{diagnostic.Certainty}|{string.Join(',', diagnostic.Evidence.Select(item => item.Id.Value))}";

    private static IEnumerable<DiagramFragment> AllFragments(DiagramFragment fragment)
    {
        yield return fragment;
        foreach (var nested in fragment.Fragments.SelectMany(AllFragments))
        {
            yield return nested;
        }
        foreach (var nested in fragment.Arms.SelectMany(arm => arm.Fragments).SelectMany(AllFragments))
        {
            yield return nested;
        }
    }

    private static IEnumerable<DiagramPlanElementId> AllMessageRefs(IEnumerable<DiagramFragment> fragments)
    {
        foreach (var fragment in fragments)
        {
            foreach (var reference in fragment.MessageRefs)
            {
                yield return reference;
            }

            foreach (var arm in fragment.Arms)
            {
                foreach (var reference in arm.MessageRefs)
                {
                    yield return reference;
                }

                foreach (var reference in AllMessageRefs(arm.Fragments))
                {
                    yield return reference;
                }
            }

            foreach (var reference in AllMessageRefs(fragment.Fragments))
            {
                yield return reference;
            }
        }
    }

    [Theory]
    [InlineData("missing-anchor", "SC011")]
    [InlineData("conflicting-anchor", "SC012")]
    [InlineData("loop-and-exception-child", "SC013")]
    [InlineData("catch-placement", "SC013")]
    [InlineData("finally-placement", "SC013")]
    public void UnsupportedTopologyBoundariesWithholdOnlyTheUnprovableClaim(string partition, string diagnosticCode)
    {
        var request = partition switch
        {
            "missing-anchor" => ScenarioTestFactory.CreateMissingAnchorTopologyRequest(),
            "conflicting-anchor" => ScenarioTestFactory.CreateDualPolarityConflictRequest(),
            "loop-and-exception-child" => ScenarioTestFactory.CreateUnsupportedTopologyRequest(),
            "catch-placement" => ScenarioTestFactory.CreateExceptionRegionTopologyRequest("Catch"),
            "finally-placement" => ScenarioTestFactory.CreateFinallyTargetTopologyRequest(),
            _ => throw new ArgumentOutOfRangeException(nameof(partition), partition, null),
        };
        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
        if (diagnosticCode == "SC011")
        {
            var missing = Assert.Single(graph.Nodes,
                node => node.Key == $"query:{ScenarioTestFactory.WorkItemMissingAnchorOperation.Value}");
            Assert.DoesNotContain(graph.Topology.Memberships,
                membership => membership.ScenarioNode == missing.Id);
        }
        else if (diagnosticCode == "SC012")
        {
            var save = Assert.Single(graph.Nodes, node => node.Key == $"mutation:{ScenarioTestFactory.WorkItemSaveOperation.Value}");
            Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.ScenarioNode == save.Id);
        }
        else
        {
            Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
            Assert.Contains(graph.Topology.Terminals, terminal => terminal.Kind == ScenarioTerminalKind.Unknown);
        }
    }

    [Fact]
    public void NestedGuardedCallsHaveOneValidPlanReferenceEach()
    {
        var graph = DirectExactTraversalFixture.BuildGraph("nested-local-guards");
        var plan = DocumentationPlanner.Plan(graph).Diagram;

        var references = plan.Sequence.Elements.SelectMany(CollectReferences).ToArray();
        Assert.NotEmpty(plan.Sequence.Fragments);
        Assert.Equal(plan.Messages.Select(message => message.Id).OrderBy(id => id.Value, StringComparer.Ordinal),
            references.OrderBy(id => id.Value, StringComparer.Ordinal));
        Assert.Equal(references.Length, references.Distinct().Count());
        Assert.Contains(plan.Sequence.Fragments.SelectMany(fragment => fragment.Arms),
            arm => arm.Fragments.Length > 0);
    }

    private static IEnumerable<DiagramPlanElementId> CollectReferences(DiagramSequenceElement element)
    {
        if (element.IsMessageRef)
        {
            yield return element.MessageRefId!.Value;
            yield break;
        }

        foreach (var reference in CollectReferences(element.NestedFragment!))
        {
            yield return reference;
        }
    }

    private static IEnumerable<DiagramPlanElementId> CollectReferences(DiagramFragment fragment)
    {
        foreach (var reference in fragment.MessageRefs)
        {
            yield return reference;
        }

        foreach (var arm in fragment.Arms)
        {
            foreach (var reference in arm.MessageRefs)
            {
                yield return reference;
            }

            foreach (var nested in arm.Fragments)
            {
                foreach (var reference in CollectReferences(nested))
                {
                    yield return reference;
                }
            }
        }

        foreach (var nested in fragment.Fragments)
        {
            foreach (var reference in CollectReferences(nested))
            {
                yield return reference;
            }
        }
    }
}
