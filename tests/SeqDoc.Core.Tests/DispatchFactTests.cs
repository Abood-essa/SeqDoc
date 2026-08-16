using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Core.Tests;

public sealed class DispatchFactTests
{
    private static readonly CompilationProfileId Profile = new("profile:v1:test");
    private static readonly ImmutableArray<EvidenceRef> Evidence =
        [new(new EvidenceId("evidence:v1:dispatch"), EvidenceKind.Source, "Program.cs", null, "dispatch", "test", CertaintyLevel.Exact)];

    [Fact]
    public void ExactRequestResponseSelectsTheOnlyCanonicalCandidateAndKeepsSuppliedFactIdentity()
    {
        var suppliedId = new BehaviorFactId("behavior-fact:v1:dispatch:create-order");
        var candidate = Candidate("CreateOrderHandler", "method:v1:Handlers.CreateOrderHandler.Handle");
        var fact = new DispatchFact(
            suppliedId, Profile, "program-index:test", new("method:v1:Endpoints.CreateOrder"), new("operation:v1:send-create-order"),
            DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.ExactSingle, "CreateOrderCommand", "CreateOrderResult",
            [candidate], DispatchPipelineMetadata.Unknown, Evidence, CertaintyLevel.Exact);

        Assert.Equal(suppliedId, fact.Id);
        Assert.Equal(candidate, Assert.Single(fact.Candidates));
        Assert.Equal(candidate, fact.SelectedHandler);
    }

    [Fact]
    public void AmbiguousRequestResponseRetainsCanonicalCandidatesButSelectsNone()
    {
        var first = Candidate("B", "method:v1:Handlers.B.Handle");
        var second = Candidate("A", "method:v1:Handlers.A.Handle");
        var fact = Fact(DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.Ambiguous, [first, second]);

        Assert.Equal(["A", "B"], fact.Candidates.Select(item => item.DisplayName));
        Assert.Null(fact.SelectedHandler);
    }

    [Fact]
    public void NotificationFanOutAndUnsupportedResolutionNeverClaimHandlerExecution()
    {
        var notification = Fact(DispatchBoundaryKind.Notification, DispatchCardinality.OneOrMore,
            DispatchResolution.ExactSingle, [Candidate("N", "method:v1:N.Handle")]);
        var openGeneric = Fact(DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.OpenGeneric, [Candidate("G", "method:v1:G.Handle")]);
        var generatedBody = Fact(DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.GeneratedBodyUnavailable, [Candidate("Generated", "method:v1:Generated.Handle", bodyAvailable: false)]);

        Assert.Null(notification.SelectedHandler);
        Assert.Null(openGeneric.SelectedHandler);
        Assert.Single(generatedBody.Candidates);
        Assert.Equal("Generated", generatedBody.SelectedHandler?.DisplayName);
        Assert.False(generatedBody.SelectedHandler?.BodyAvailable);
        Assert.Throws<ArgumentException>(() => Fact(DispatchBoundaryKind.RequestResponse, DispatchCardinality.OneOrMore,
            DispatchResolution.Unresolved, []));
    }

    [Fact]
    public void PipelineMetadataDistinguishesKnownOrderedStagesFromUnknown()
    {
        var known = new DispatchPipelineMetadata(
            PipelineMetadataKind.Known,
            [new DispatchPipelineStage(2, "Validation", Evidence), new DispatchPipelineStage(1, "Authorization", Evidence)]);
        var fact = Fact(DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.ExactSingle, [Candidate("H", "method:v1:H.Handle")], known);

        Assert.Equal([1, 2], fact.Pipeline.Stages.Select(stage => stage.Ordinal));
        Assert.Empty(Fact(DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.ExactSingle, [Candidate("H", "method:v1:H.Handle")]).Pipeline.Stages);
        Assert.Throws<ArgumentException>(() => new DispatchPipelineMetadata(
            PipelineMetadataKind.Unknown, [new DispatchPipelineStage(1, "not-allowed", Evidence)]));
    }

    [Fact]
    public void DuplicateCandidatesAndUnbackedFactsFailClosed()
    {
        var candidate = Candidate("H", "method:v1:H.Handle");
        Assert.Throws<ArgumentException>(() => Fact(DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.ExactSingle, [candidate, candidate]));
        Assert.Throws<ArgumentException>(() => new DispatchFact(
            new("behavior-fact:v1:dispatch:bad"), Profile, "program-index:test", new("method:v1:Caller"), new("operation:v1:send"),
            DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne, DispatchResolution.Unresolved,
            "Request", "Response", [], DispatchPipelineMetadata.Unknown, [], CertaintyLevel.Unknown));
    }

    [Fact]
    public void BoundaryAndResolutionPartitionsRejectContradictoryShapes()
    {
        var candidate = Candidate("H", "method:v1:H.Handle");

        Assert.Throws<ArgumentException>(() => Fact(DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.Ambiguous, [candidate]));
        Assert.Throws<ArgumentException>(() => Fact(DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.ExactSingle, []));
        Assert.Throws<ArgumentException>(() => Fact(DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne, DispatchResolution.ExactSingle, [candidate, Candidate("H2", "method:v1:H2.Handle")]));
        Assert.Throws<ArgumentException>(() => new DispatchFact(
            new("behavior-fact:v1:dispatch:missing-response"), Profile, "program-index:test", new("method:v1:Caller"),
            new("operation:v1:send"), DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
            DispatchResolution.ExactSingle, "Request", null, [candidate], DispatchPipelineMetadata.Unknown,
            Evidence, CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new DispatchFact(
            new("behavior-fact:v1:dispatch:notification-response"), Profile, "program-index:test", new("method:v1:Caller"),
            new("operation:v1:notify"), DispatchBoundaryKind.Notification, DispatchCardinality.OneOrMore,
            DispatchResolution.ExactSingle, "Notification", "Response", [candidate], DispatchPipelineMetadata.Unknown,
            Evidence, CertaintyLevel.Exact));
    }

    private static DispatchCandidate Candidate(string displayName, string method, bool bodyAvailable = true)
        => new(new MethodId(method), displayName, bodyAvailable, Evidence, CertaintyLevel.Exact);

    private static DispatchFact Fact(
        DispatchBoundaryKind boundary, DispatchCardinality cardinality, DispatchResolution resolution,
        ImmutableArray<DispatchCandidate> candidates, DispatchPipelineMetadata? pipeline = null)
        => new(new BehaviorFactId("behavior-fact:v1:dispatch:test"), Profile, "program-index:test", new("method:v1:Caller"),
            new("operation:v1:send"), boundary, cardinality, resolution, "Request",
            boundary == DispatchBoundaryKind.Notification ? null : "Response",
            candidates, pipeline ?? DispatchPipelineMetadata.Unknown, Evidence, CertaintyLevel.Exact);
}
