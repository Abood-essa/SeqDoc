using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// Scenario Graph join for issue 54: a root-local platform (<c>IsPlatformTarget</c>) HttpClient call
/// with a matching <see cref="OutboundHttpRequestFact"/> becomes exactly one
/// <see cref="ScenarioNodeKind.OutboundHttpRequest"/> node plus one <see cref="ScenarioEdgeKind.Call"/>
/// edge; agreeing duplicates merge, conflicting facts withhold with one deterministic
/// <c>SC-HTTP-CONFLICT</c>; guarded calls keep their proven arm and hostile topology withholds under
/// the existing <c>SC013</c>/<c>DP002</c>; cancellation stops both the model and the graph loop.
/// Hand-built facts over the fixture-shaped <see cref="ScenarioTestFactory"/> request per orchestrator
/// resolution 1/3 (producer-discipline is closed in <c>OutboundHttpProjectionTests</c>). HARD RED.
/// </summary>
public sealed class OutboundHttpScenarioTests
{
    private const string PublicKeyToken = "b03f5f7f11d50a3a";
    private const string ReturnType = "System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage>";

    private static FrameworkMethodIdentity GetIdentity()
        => new(
            "System.Net.Http", "System.Net.Http.HttpClient", "GetAsync", 0,
            [new(ParameterRefKind.None, "System.String")], ReturnType, "10.0.0.0", PublicKeyToken);

    private static OutboundHttpRequestFact CreateFact(
        OutboundHttpRequestKind kind = OutboundHttpRequestKind.Get,
        CertaintyLevel certainty = CertaintyLevel.Exact,
        string idSuffix = "root-direct",
        FrameworkMethodIdentity? identity = null,
        string artifact = "outbound-http-request")
        => new()
        {
            Id = new BehaviorFactId($"behavior-fact:v1:outbound-http-request:{idSuffix}"),
            Evidence = [certainty == CertaintyLevel.Exact
                ? ScenarioTestFactory.SourceEvidence(artifact)
                : ScenarioTestFactory.ConservativeEvidence(artifact)],
            Certainty = certainty,
            CallerMethod = ScenarioTestFactory.ActionMethod,
            InvocationOperation = ScenarioTestFactory.RootDirectCallOperation,
            RequestKind = kind,
            FrameworkMethodIdentity = identity ?? GetIdentity(),
        };

    private static ScenarioAnalysisRequest PlatformRequest(bool decisionGuarded, params BehaviorFact[] facts)
    {
        var baseRequest = ScenarioTestFactory.CreateRootDirectCallRequest(
            decisionGuarded: decisionGuarded, exclusion: "platform");
        return baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(facts),
                ProfileId = baseRequest.Profile.Id,
                ProgramIndexFingerprint = baseRequest.ProgramIndex.IndexFingerprint,
            },
        };
    }

    [Fact]
    public void RootPlatformCallJoinsByExactOperationAndCallSite()
    {
        var request = PlatformRequest(decisionGuarded: false, CreateFact(OutboundHttpRequestKind.Get));

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        var node = Assert.Single(graph.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
        Assert.Equal(15, (int)node.Kind);
        Assert.Equal(ScenarioTestFactory.RootDirectCallOperation, node.Operation);
        Assert.Equal(ScenarioTestFactory.ActionMethod, node.Method);
        Assert.Equal(OutboundHttpRequestKind.Get, node.Presentation?.OutboundHttpRequestKind);

        // The generic direct-call node for the same platform call site is not also produced.
        Assert.DoesNotContain(graph.Nodes, n =>
            n.Kind == ScenarioNodeKind.MethodCall && n.Operation == ScenarioTestFactory.RootDirectCallOperation);

        var edge = Assert.Single(graph.Edges, e => e.Target == node.Id);
        Assert.Equal(ScenarioEdgeKind.Call, edge.Kind);
        Assert.NotEmpty(node.Evidence);
        Assert.Equal(CertaintyLevel.Exact, node.Certainty);

        // A callee-anchored supported call never materializes a node.
        var calleeFact = CreateFact(idSuffix: "callee") with
        {
            CallerMethod = ScenarioTestFactory.RootDirectCallTarget,
            InvocationOperation = new OperationId("operation:v1:callee.get"),
        };
        var calleeGraph = Assert.Single(ScenarioGraphBuilder.Build(
            PlatformRequest(decisionGuarded: false, calleeFact)).Graphs);
        Assert.DoesNotContain(calleeGraph.Nodes, n =>
            n.Kind == ScenarioNodeKind.OutboundHttpRequest && n.Operation == new OperationId("operation:v1:callee.get"));
    }

    [Fact]
    public void AgreeingFactsMergeAndConflictingFactsWithholdDeterministically()
    {
        // (a) two agreeing duplicate GET facts -> one node, one edge, unioned evidence, weakest certainty.
        var agreeingForward = Assert.Single(ScenarioGraphBuilder.Build(PlatformRequest(
            false, CreateFact(idSuffix: "dup-1"), CreateFact(idSuffix: "dup-2", artifact: "outbound-http-request-2"))).Graphs);
        var agreeingReversed = Assert.Single(ScenarioGraphBuilder.Build(PlatformRequest(
            false, CreateFact(idSuffix: "dup-2", artifact: "outbound-http-request-2"), CreateFact(idSuffix: "dup-1"))).Graphs);

        var forwardNode = Assert.Single(agreeingForward.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
        var reversedNode = Assert.Single(agreeingReversed.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
        Assert.Equal(forwardNode.Id, reversedNode.Id);
        Assert.Single(agreeingForward.Edges, e => e.Target == forwardNode.Id);
        Assert.Equal(
            forwardNode.Evidence.Select(x => x.Id.Value).OrderBy(x => x, StringComparer.Ordinal),
            reversedNode.Evidence.Select(x => x.Id.Value).OrderBy(x => x, StringComparer.Ordinal));
        Assert.DoesNotContain(agreeingForward.Diagnostics, d => d.Code == "SC-HTTP-CONFLICT");

        // (b) two conflicting facts (different request kind / identity row) -> no node, one SC-HTTP-CONFLICT.
        BehaviorFact[] conflict =
        [
            CreateFact(OutboundHttpRequestKind.Get, idSuffix: "conflict-1"),
            CreateFact(OutboundHttpRequestKind.Post, idSuffix: "conflict-2",
                identity: GetIdentity() with { MethodMetadataName = "PostAsync" }),
        ];
        var conflictForward = Assert.Single(ScenarioGraphBuilder.Build(PlatformRequest(false, conflict)).Graphs);
        var conflictReversed = Assert.Single(ScenarioGraphBuilder.Build(
            PlatformRequest(false, conflict.Reverse().ToArray())).Graphs);

        Assert.DoesNotContain(conflictForward.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
        var forwardDiagnostic = Assert.Single(conflictForward.Diagnostics, d => d.Code == "SC-HTTP-CONFLICT");
        var reversedDiagnostic = Assert.Single(conflictReversed.Diagnostics, d => d.Code == "SC-HTTP-CONFLICT");
        Assert.Equal(forwardDiagnostic.Detail, reversedDiagnostic.Detail);

        // Empty evidence / unknown certainty fails closed (no node).
        var badEvidence = CreateFact(idSuffix: "bad") with { Evidence = ImmutableArray<EvidenceRef>.Empty };
        var badGraph = Assert.Single(ScenarioGraphBuilder.Build(PlatformRequest(false, badEvidence)).Graphs);
        Assert.DoesNotContain(badGraph.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
    }

    [Fact]
    public void GuardedCallRetainsArmAndHostileTopologyWithholds()
    {
        var request = PlatformRequest(decisionGuarded: true, CreateFact(OutboundHttpRequestKind.Get));

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        var node = Assert.Single(graph.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
        var decision = Assert.Single(graph.Topology.Decisions);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
        var falseArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && !arm.IsTrue);
        Assert.Single(graph.Topology.Memberships, m => m.Arm == trueArm.Id && m.ScenarioNode == node.Id);
        Assert.DoesNotContain(graph.Topology.Memberships, m => m.Arm == falseArm.Id && m.ScenarioNode == node.Id);
        Assert.DoesNotContain(graph.Nodes, n =>
            n.Kind == ScenarioNodeKind.MethodCall && n.Operation == ScenarioTestFactory.RootDirectCallOperation);

        // Hostile / unplaceable topology (ambiguous resolution -> zero placeable candidates): the node is
        // withheld under the existing SC013 / DP002 topology diagnostic, never placed on the first candidate.
        var hostile = ScenarioTestFactory.CreateRootDirectCallRequest(exclusion: "ambiguous");
        var hostileRequest = hostile with
        {
            FrameworkFacts = hostile.FrameworkFacts with
            {
                Facts = hostile.FrameworkFacts.Facts.Add(CreateFact(OutboundHttpRequestKind.Get)),
                ProfileId = hostile.Profile.Id,
                ProgramIndexFingerprint = hostile.ProgramIndex.IndexFingerprint,
            },
        };
        var hostileGraph = Assert.Single(ScenarioGraphBuilder.Build(hostileRequest).Graphs);
        Assert.DoesNotContain(hostileGraph.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
        // The withhold diagnostic names the outbound-HTTP operation id, not merely a bare SC013/DP002.
        Assert.Contains(
            hostileGraph.Diagnostics,
            d => (d.Code is "SC013" or "DP002")
                && (d.Detail ?? string.Empty).Contains(
                    ScenarioTestFactory.RootDirectCallOperation.Value, StringComparison.Ordinal));
    }

    [Fact]
    public void CancellationStopsScenarioProjection()
    {
        // Scenario half of resolution 2: the new additive optional CancellationToken parameter on
        // ScenarioGraphBuilder.Build is observed once per admitted outbound-HTTP operation, before any
        // node/edge/diagnostic is emitted. (The model half — HttpClientOutboundModel.AnalyzeOperationAsync
        // honouring its CancellationToken at loop granularity — is proven in
        // SeqDoc.FrameworkModels.Tests/OutboundHttp/HttpClientOutboundModelTests.cs; SeqDoc.Scenarios.Tests
        // has no reference to SeqDoc.FrameworkModels.)
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = PlatformRequest(decisionGuarded: false, CreateFact(OutboundHttpRequestKind.Get));
        Assert.ThrowsAny<OperationCanceledException>(() => ScenarioGraphBuilder.Build(request, cts.Token));
    }
}
