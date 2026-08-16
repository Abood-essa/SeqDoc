using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

public sealed class DispatchScenarioTests
{
    [Fact]
    public void ExactDispatchJoinsOnlyTheMatchingMinimalApiRootAndCreatesTypedBoundaryAndHandler()
    {
        var request = ScenarioTestFactory.CreateMinimalApiRequest(new MinimalApiRouteFact
        {
            Id = new("behavior-fact:v1:minimal-dispatch-route"),
            Evidence = [Evidence("route")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new("entry-point:v1:POST-orders"),
            HandlerRoot = new("method:v1:Program.CreateOrder"),
            HandlerKind = MinimalApiHandlerKind.NamedMethod,
            HttpMethod = HttpMethodKind.Post,
            CanonicalRoute = "/orders",
            OperationKey = "POST /orders",
        });
        var candidate = new DispatchCandidate(new("method:v1:Handlers.CreateOrderHandler.Handle"),
            "CreateOrderCommandHandler", true, [Evidence("handler")], CertaintyLevel.Exact);
        var dispatch = new DispatchFact(new("behavior-fact:v1:dispatch:create-order"), ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.ProgramIndexFingerprint,
            new("method:v1:Program.CreateOrder"), new("operation:v1:send-create-order"),
            DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.ExactSingle, "CreateOrderDraftCommand", "CreateOrderDraft",
            [candidate], DispatchPipelineMetadata.Unknown, [Evidence("dispatch")], CertaintyLevel.Exact);
        request = request with
        {
            FrameworkFacts = request.FrameworkFacts with { Facts = request.FrameworkFacts.Facts.Add(dispatch) },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);
        var boundary = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Dispatch);
        var handler = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Handler);

        Assert.Contains(graph.Edges, edge => edge.Source == boundary.Id && edge.Target == handler.Id
            && edge.Kind == ScenarioEdgeKind.Dispatch);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Call);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
    }

    [Fact]
    public void AmbiguousOrUnresolvedDispatchRetainsBoundaryAndStableDiagnosticWithoutHandler()
    {
        var first = Candidate("B", "method:v1:B.Handle");
        var second = Candidate("A", "method:v1:A.Handle");
        var ambiguous = Dispatch(DispatchResolution.Ambiguous, [first, second]);
        var request = ScenarioTestFactory.CreateMinimalApiDispatchRequest(ambiguous);

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Dispatch);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Handler);
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC-DISPATCH-AMBIGUOUS");
        Assert.Contains("A", diagnostic.Detail, StringComparison.Ordinal);
        Assert.Contains("B", diagnostic.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(graph.DebugProjection, "CreateOrderDraftCommandHandler", StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignCallerFactIsIgnoredAndSparseMinimalApiBehaviorRemainsUnchanged()
    {
        var foreign = Dispatch(DispatchResolution.ExactSingle,
            [Candidate("Foreign", "method:v1:Foreign.Handler")], new MethodId("method:v1:OtherRoot"));
        var request = ScenarioTestFactory.CreateMinimalApiDispatchRequest(foreign);
        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Dispatch);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Handler);
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ForeignProfileOrProgramIndexDispatchFactIsIgnored(bool foreignProfile, bool foreignFingerprint)
    {
        var dispatch = Dispatch(DispatchResolution.ExactSingle,
            [Candidate("Foreign", "method:v1:Foreign.Handler")]);
        var request = ScenarioTestFactory.CreateMinimalApiDispatchRequest(
            dispatch, foreignProfile, foreignFingerprint);

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Dispatch);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Handler);
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
    }

    [Fact]
    public void GeneratedBodyUnavailableRetainsTypedCandidateStateAtScenarioBoundary()
    {
        var dispatch = Dispatch(DispatchResolution.GeneratedBodyUnavailable,
            [new DispatchCandidate(new("method:v1:Generated.Handle"), "GeneratedHandler", false,
                [Evidence("generated")], CertaintyLevel.Exact)]);
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateMinimalApiDispatchRequest(dispatch)).Graphs);
        var handler = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Handler);

        Assert.Equal("GeneratedHandler", handler.Presentation?.HandlerTypeName);
        Assert.False(handler.Presentation?.HandlerBodyAvailable);
        Assert.Equal("handler body unavailable", handler.Detail);
    }

    [Fact]
    public void MultipleDispatchOperationsForOneCallerMaterializeEveryBoundaryAndHandler()
    {
        var first = Dispatch(DispatchResolution.ExactSingle,
            [Candidate("FirstHandler", "method:v1:Handlers.First.Handle")]);
        var second = new DispatchFact(
            new("behavior-fact:v1:dispatch:second"), ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.ProgramIndexFingerprint, first.CallerMethod,
            new("operation:v1:send-create-order-0"), DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.ExactSingle, "SecondRequest", "SecondResponse",
            [Candidate("SecondHandler", "method:v1:Handlers.Second.Handle")],
            DispatchPipelineMetadata.Unknown, [Evidence("dispatch-second")], CertaintyLevel.Exact);
        var request = ScenarioTestFactory.CreateMinimalApiDispatchRequest(first) with
        {
            FrameworkFacts = ScenarioTestFactory.CreateMinimalApiDispatchRequest(first).FrameworkFacts with
            {
                Facts = ScenarioTestFactory.CreateMinimalApiDispatchRequest(first).FrameworkFacts.Facts.Add(second),
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);
        var dispatches = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.Dispatch).ToArray();
        var handlers = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.Handler).ToArray();

        Assert.Equal(["operation:v1:send-create-order", "operation:v1:send-create-order-0"],
            dispatches.Select(node => node.Operation!.Value.Value).OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(["method:v1:Handlers.First.Handle", "method:v1:Handlers.Second.Handle"],
            handlers.Select(node => node.Method!.Value.Value).OrderBy(value => value, StringComparer.Ordinal));
        Assert.All(dispatches, dispatch => Assert.Contains(graph.Edges,
            edge => edge.Kind == ScenarioEdgeKind.Dispatch && edge.Source == dispatch.Id
                && handlers.Any(handler => handler.Id == edge.Target)));
    }

    [Fact]
    public void ExactSingleRejectsConservativeCandidateButGeneratedBodyUnavailableMaySelectIt()
    {
        var conservative = Candidate("ConservativeHandler", "method:v1:Handlers.Conservative.Handle", certainty: CertaintyLevel.Conservative);
        Assert.Throws<ArgumentException>(() => new DispatchFact(
            new("behavior-fact:v1:dispatch:conservative-exact"), ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.ProgramIndexFingerprint, new("method:v1:Program.CreateOrder"),
            new("operation:v1:send-conservative-exact"), DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.ExactSingle, "Request", "Response",
            [conservative], DispatchPipelineMetadata.Unknown, [Evidence("conservative-exact")], CertaintyLevel.Exact));

        var generated = new DispatchFact(
            new("behavior-fact:v1:dispatch:conservative-generated"), ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.ProgramIndexFingerprint, new("method:v1:Program.CreateOrder"),
            new("operation:v1:send-conservative-generated"), DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.GeneratedBodyUnavailable, "Request", "Response",
            [new DispatchCandidate(conservative.Method, conservative.DisplayName, false, conservative.Evidence, conservative.Certainty)],
            DispatchPipelineMetadata.Unknown, [Evidence("conservative-generated", CertaintyLevel.Conservative)], CertaintyLevel.Conservative);
        Assert.NotNull(generated.SelectedHandler);
        var selected = generated.SelectedHandler!;
        Assert.Equal(conservative.Method, selected.Method);
        Assert.Equal(conservative.Certainty, selected.Certainty);
        Assert.False(selected.BodyAvailable);
    }

    [Fact]
    public void ConservativeDispatchPropagatesCertaintyToBoundaryHandlerAndEdge()
    {
        var ambiguous = new DispatchFact(
            new("behavior-fact:v1:dispatch:conservative-ambiguous"), ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.ProgramIndexFingerprint, new("method:v1:Program.CreateOrder"),
            new("operation:v1:send-conservative-ambiguous"), DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.Ambiguous, "Request", "Response",
            [Candidate("A", "method:v1:A.Handle", certainty: CertaintyLevel.Conservative),
             Candidate("B", "method:v1:B.Handle", certainty: CertaintyLevel.Conservative)],
            DispatchPipelineMetadata.Unknown, [Evidence("conservative-ambiguous", CertaintyLevel.Conservative)], CertaintyLevel.Conservative);
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateMinimalApiDispatchRequest(ambiguous)).Graphs);
        var boundary = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Dispatch);
        Assert.Equal(CertaintyLevel.Conservative, boundary.Certainty);
        Assert.Equal(CertaintyLevel.Conservative,
            Assert.Single(graph.Edges, edge => edge.Target == boundary.Id).Certainty);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-DISPATCH-AMBIGUOUS");

        var generated = new DispatchFact(
            new("behavior-fact:v1:dispatch:conservative-generated"), ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.ProgramIndexFingerprint, ambiguous.CallerMethod,
            new("operation:v1:send-conservative-generated"), DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.GeneratedBodyUnavailable, "Request", "Response",
            [Candidate("Generated", "method:v1:Generated.Handle", false, CertaintyLevel.Conservative)],
            DispatchPipelineMetadata.Unknown, [Evidence("conservative-generated", CertaintyLevel.Conservative)], CertaintyLevel.Conservative);
        var generatedGraph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateMinimalApiDispatchRequest(generated)).Graphs);
        var handler = Assert.Single(generatedGraph.Nodes, node => node.Kind == ScenarioNodeKind.Handler);
        Assert.Equal(CertaintyLevel.Conservative, handler.Certainty);
        Assert.Equal(CertaintyLevel.Conservative,
            Assert.Single(generatedGraph.Edges, edge => edge.Target == handler.Id).Certainty);
    }

    [Fact]
    public void AmbiguousDiagnosticsUseCanonicalMethodIdsForDistinctCandidateSets()
    {
        var first = Dispatch(DispatchResolution.Ambiguous,
            [Candidate("SameName", "method:v1:Handlers.First.Handle"), Candidate("SameName", "method:v1:Handlers.Second.Handle")]);
        var second = new DispatchFact(
            new("behavior-fact:v1:dispatch:diagnostic-second"), ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.ProgramIndexFingerprint, first.CallerMethod,
            new("operation:v1:send-diagnostic-second"), DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.Ambiguous, "Request", "Response",
            [Candidate("SameName", "method:v1:Handlers.Third.Handle"), Candidate("SameName", "method:v1:Handlers.Fourth.Handle")],
            DispatchPipelineMetadata.Unknown, [Evidence("diagnostic-second")], CertaintyLevel.Exact);
        var baseRequest = ScenarioTestFactory.CreateMinimalApiDispatchRequest(first);
        var request = baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.Add(second),
            },
        };

        var diagnostics = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs).Diagnostics
            .Where(item => item.Code == "SC-DISPATCH-AMBIGUOUS").ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.NotEqual(diagnostics[0].Id, diagnostics[1].Id);
        Assert.Contains("method:v1:Handlers.First.Handle", diagnostics[0].Detail + diagnostics[1].Detail, StringComparison.Ordinal);
        Assert.Contains("method:v1:Handlers.Fourth.Handle", diagnostics[0].Detail + diagnostics[1].Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("SameName", diagnostics[0].Detail, StringComparison.Ordinal);
    }

    private static DispatchCandidate Candidate(string name, string method, bool bodyAvailable = true,
        CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(new MethodId(method), name, bodyAvailable, [Evidence(name, certainty)], certainty);

    private static DispatchFact Dispatch(DispatchResolution resolution, ImmutableArray<DispatchCandidate> candidates,
        MethodId? caller = null)
        => new(new("behavior-fact:v1:dispatch:test"), ScenarioTestFactory.Profile.Id,
            ScenarioTestFactory.ProgramIndexFingerprint, caller ?? new("method:v1:Program.CreateOrder"),
            new("operation:v1:send-create-order"), DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, resolution, "CreateOrderDraftCommand", "CreateOrderDraft",
            candidates, DispatchPipelineMetadata.Unknown, [Evidence("dispatch")], CertaintyLevel.Exact);

    private static EvidenceRef Evidence(string key, CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(new EvidenceId($"evidence:v1:dispatch:{key}"), EvidenceKind.Source, "Program.cs", null, key, "test", certainty);
}
