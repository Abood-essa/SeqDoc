using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// Write-first contract tests for the bounded dispatch-to-handler expansion.  The fixture deliberately
/// describes ordinary source code, while all joins below remain compiler-neutral facts.  These tests
/// propose the smallest public seam: one builder returns one typed expansion, including its refusal
/// diagnostics, rather than exposing a general call-graph walker.
/// </summary>
public sealed class DispatchHandlerFlowTests
{
    [Fact]
    public void ExactSingleSourceHandlerExpandsCallsLoopMembershipNestedTotalAndReturn()
    {
        var expansion = Expand(DispatchHandlerFlowFixture.ExactRequest());

        Assert.Equal(DispatchHandlerFlowFixture.Handler, expansion.Handler.Method);
        Assert.Equal(["Aggregate.Create", "Dto.FromDomain"],
            expansion.DirectCalls.Where(call => call.ParentDepth == 0).Select(call => call.TargetMethod.Value));
        Assert.Equal(["Aggregate.Create", "Aggregate.Add", "Dto.FromDomain", "Aggregate.Total"],
            expansion.SourceSteps.Select(step => step.Label));
        var handlerFlow = DispatchHandlerFlowFixture.ExactRequest().Behavior.MethodFlows.Single(flow => flow.Method == DispatchHandlerFlowFixture.Handler);
        Assert.All(handlerFlow.Nodes.OfType<InvocationFlowNode>(), node => Assert.True(node.IsSourceBacked));
        Assert.Equal(("Aggregate", "Create"), (handlerFlow.Nodes.OfType<InvocationFlowNode>().First(node => node.Operation.Value == "Aggregate.Create").TargetContainingTypeName!, handlerFlow.Nodes.OfType<InvocationFlowNode>().First(node => node.Operation.Value == "Aggregate.Create").TargetMethodName!));
        Assert.Equal(["Aggregate.Total"],
            expansion.DirectCalls.Where(call => call.ParentDepth == 1).Select(call => call.TargetMethod.Value));
        var loop = Assert.Single(expansion.Loops);
        Assert.Equal(["Aggregate.Add"], loop.MemberSteps.Select(step => step.TargetMethod.Value));
        Assert.Equal("Dto", expansion.Return!.TypeName);
        Assert.Equal("return", expansion.Return.Operation.Value);
        Assert.DoesNotContain(expansion.DirectCalls, call => call.ParentDepth > 1);
    }

    [Fact]
    public void AdmissionRequiresExactSingleMatchingBodyAvailableCandidateAndMatchingSnapshot()
    {
        var accepted = Expand(DispatchHandlerFlowFixture.ExactRequest());
        Assert.True(accepted.IsComplete);

        foreach (var request in new[]
        {
            DispatchHandlerFlowFixture.ForeignProfileRequest(),
            DispatchHandlerFlowFixture.ForeignFingerprintRequest(),
            DispatchHandlerFlowFixture.AmbiguousRequest(),
            DispatchHandlerFlowFixture.BodyUnavailableRequest(),
        })
        {
            var refused = Expand(request);
            Assert.False(refused.IsComplete);
            Assert.Empty(refused.DirectCalls);
            Assert.Contains(refused.Diagnostics, item => item.Code is "SC-DISPATCH-MISMATCH"
                or "SC-DISPATCH-AMBIGUOUS" or "SC-DISPATCH-BODY-UNAVAILABLE");
        }
    }

    [Fact]
    public void OnlyCompleteDirectExactCallsRemainInSourceOrderAndLookalikesAreWithheld()
    {
        var expansion = Expand(DispatchHandlerFlowFixture.WithUnresolvedAndLookalikeCallsRequest());

        Assert.Equal(["Aggregate.Create", "Aggregate.Add", "Dto.FromDomain", "Aggregate.Total"],
            expansion.SourceSteps.Select(step => step.TargetMethod.Value));
        Assert.DoesNotContain(expansion.SourceSteps, step => step.Operation.Value.Contains("Lookalike", StringComparison.Ordinal));
        Assert.Contains(expansion.Diagnostics, item => item.Code == "SC-DISPATCH-CALL-WITHHELD");
        Assert.DoesNotContain(expansion.Diagnostics, item => item.Code != "SC-DISPATCH-CALL-WITHHELD");
        var lookalike = DispatchHandlerFlowFixture.WithUnresolvedAndLookalikeCallsRequest().Behavior.MethodFlows
            .Single(flow => flow.Method == DispatchHandlerFlowFixture.Handler).Nodes.OfType<InvocationFlowNode>()
            .Single(node => node.Operation.Value == "Lookalike.Add");
        Assert.True(lookalike.IsInsideNestedFunction);
        Assert.True(expansion.IsComplete, ExpansionEvidence(expansion));
    }

    [Fact]
    public void CompleteLoopUsesExactMembershipButIncompleteLoopIsWithheldAndDiagnosed()
    {
        var complete = Expand(DispatchHandlerFlowFixture.ExactRequest());
        var loop = Assert.Single(complete.Loops);
        Assert.Contains(loop.MemberSteps, step => step.Operation.Value == "Aggregate.Add");
        Assert.DoesNotContain(loop.MemberSteps, step => step.Operation.Value == "Lookalike.Add");

        var incomplete = Expand(DispatchHandlerFlowFixture.IncompleteLoopRequest());
        Assert.Empty(incomplete.Loops);
        Assert.Contains(incomplete.Diagnostics, item => item.Code == "SC-DISPATCH-LOOP-INCOMPLETE");
    }

    [Fact]
    public void ReturnIsExactAndDoesNotInventHttpStatusOrSerialization()
    {
        var expansion = Expand(DispatchHandlerFlowFixture.ExactRequest());
        Assert.NotNull(expansion.Return);
        Assert.Equal("Dto", expansion.Return.TypeName);
        Assert.DoesNotContain(expansion.SourceSteps, step => step.Operation.Value.Contains("HTTP", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("status", expansion.DebugProjection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serialization", expansion.DebugProjection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReturnRequiresEffectiveHandlerTypeAndPreservesCompleteCallsWhenItDoesNotMatch()
    {
        var mismatch = Expand(DispatchHandlerFlowFixture.ReturnTypeMismatchRequest());

        Assert.Null(mismatch.Return);
        Assert.Contains(mismatch.Diagnostics, item => item.Code == "SC-DISPATCH-RETURN-MISMATCH"
            && item.Summary == "The dispatch response type does not exactly match the selected compiler handler return type.");
        Assert.Equal(["Aggregate.Create", "Aggregate.Add", "Dto.FromDomain", "Aggregate.Total"],
            mismatch.SourceSteps.Select(step => step.TargetMethod.Value));

        var taskMatch = Expand(DispatchHandlerFlowFixture.TaskReturnRequest());
        Assert.Equal("Dto", taskMatch.Return!.TypeName);
        Assert.DoesNotContain(taskMatch.Diagnostics, item => item.Code == "SC-DISPATCH-RETURN-MISMATCH");
    }

    [Fact]
    public void LoopBackFromOutsideRetainedBodyWithholdsLoopAndKeepsExactDiagnostic()
    {
        var expansion = Expand(DispatchHandlerFlowFixture.ForeignLoopBackRequest());

        Assert.Empty(expansion.Loops);
        Assert.Contains(expansion.Diagnostics, item => item.Code == "SC-DISPATCH-LOOP-INCOMPLETE");
        Assert.Equal(["Aggregate.Create", "Aggregate.Add", "Dto.FromDomain", "Aggregate.Total"],
            expansion.SourceSteps.Select(step => step.TargetMethod.Value));
    }

    [Fact]
    public void CanonicalTargetIdentitiesProduceDistinctStableMinimalAliases()
    {
        var normal = Expand(DispatchHandlerFlowFixture.CanonicalParticipantRequest());
        var reversed = Expand(DispatchHandlerFlowFixture.ReversedCanonicalParticipantRequest());

        Assert.Equal(["Alpha.Widget", "Beta.Widget"], normal.Participants
            .Where(participant => participant.Identity?.EndsWith(".Widget", StringComparison.Ordinal) == true)
            .OrderBy(participant => participant.Identity, StringComparer.Ordinal)
            .Select(participant => participant.Label));
        Assert.Equal(normal.DebugProjection, reversed.DebugProjection);
        Assert.Equal(2, normal.Participants.Count(participant =>
            participant.Identity?.EndsWith(".Widget", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void ParticipantsAreConciseAndProjectionIsDeterministicForReversedFacts()
    {
        var normal = Expand(DispatchHandlerFlowFixture.ExactRequest());
        var reversed = Expand(DispatchHandlerFlowFixture.ReversedExactRequest());

        Assert.Equal(["request", "dispatch", "handler", "aggregate", "dto"],
            normal.Participants.Select(participant => participant.Key));
        Assert.All(normal.Participants, participant => Assert.DoesNotContain('.', participant.Label));
        Assert.Equal(normal.DebugProjection, reversed.DebugProjection);
    }

    private static ScenarioDispatchHandlerExpansion Expand(ScenarioAnalysisRequest request)
    {
        var dispatch = Assert.Single(request.FrameworkFacts.Facts.OfType<DispatchFact>());
        return ScenarioDispatchHandlerExpansionBuilder.Build(request, dispatch);
    }

    private static string ExpansionEvidence(ScenarioDispatchHandlerExpansion expansion)
        => $"dispatch expansion incomplete; diagnostics={string.Join(" | ", expansion.Diagnostics.Select(item => $"{item.Code}:{item.Detail}"))}; " +
           $"admitted steps={string.Join(" -> ", expansion.SourceSteps.Select(step => $"{step.Label}[{step.TargetMethod.Value}]"))}";

    internal static EvidenceRef Evidence(string name)
        => new(new EvidenceId($"evidence:v1:dispatch-handler:{name}"), EvidenceKind.Source,
            "DispatchHandlerFlow.cs", null, name, "test", CertaintyLevel.Exact);
}

/// <summary>Neutral fixture factory vocabulary for the source shape in tests/fixtures/LongHorizon.</summary>
internal static class DispatchHandlerFlowFixture
{
    internal static readonly MethodId Handler = new("method:v1:Dispatch.Handler.Handle");

    internal static ScenarioAnalysisRequest ExactRequest()
        => Create("exact");

    internal static ScenarioAnalysisRequest ReversedExactRequest()
        => Create("reversed");

    internal static ScenarioAnalysisRequest ForeignProfileRequest()
        => Create("foreign-profile");

    internal static ScenarioAnalysisRequest ForeignFingerprintRequest()
        => Create("foreign-fingerprint");

    internal static ScenarioAnalysisRequest AmbiguousRequest()
        => Create("ambiguous");

    internal static ScenarioAnalysisRequest BodyUnavailableRequest()
        => Create("body-unavailable");

    internal static ScenarioAnalysisRequest WithUnresolvedAndLookalikeCallsRequest()
        => Create("unresolved-lookalike");

    internal static ScenarioAnalysisRequest IncompleteLoopRequest()
        => Create("incomplete-loop");

    internal static ScenarioAnalysisRequest ForeignLoopBackRequest()
        => Create("foreign-loop-back");

    internal static ScenarioAnalysisRequest ReturnTypeMismatchRequest()
        => Create("return-mismatch");

    internal static ScenarioAnalysisRequest TaskReturnRequest()
        => Create("task-return");

    internal static ScenarioAnalysisRequest CanonicalParticipantRequest()
        => Create("canonical-participants");

    internal static ScenarioAnalysisRequest ReversedCanonicalParticipantRequest()
        => Create("canonical-participants-reversed");

    private static ScenarioAnalysisRequest Create(string partition)
    {
        // The product builder consumes the existing neutral request/fact pipeline.  The partition
        // names are stable fixture inputs; their concrete facts are added with the Method Flow seam.
        var request = ScenarioTestFactory.CreateMinimalApiDispatchRequest(
            new DispatchFact(new BehaviorFactId($"behavior-fact:v1:dispatch-handler:{partition}"),
                ScenarioTestFactory.Profile.Id, ScenarioTestFactory.ProgramIndexFingerprint,
                new("method:v1:Program.Create"), new("operation:v1:send"),
                DispatchBoundaryKind.RequestResponse, DispatchCardinality.ExactlyOne,
                partition is "ambiguous" ? DispatchResolution.Ambiguous
                    : partition is "body-unavailable" ? DispatchResolution.GeneratedBodyUnavailable
                    : DispatchResolution.ExactSingle,
                "Request", "Dto",
                partition is "ambiguous"
                    ? ImmutableArray.Create(
                        new DispatchCandidate(Handler, "DispatchHandler.Handle", true, [DispatchHandlerFlowTests.Evidence("candidate")], CertaintyLevel.Exact),
                        new DispatchCandidate(new MethodId("method:v1:OtherHandler.Handle"), "OtherHandler.Handle", true, [DispatchHandlerFlowTests.Evidence("candidate-other")], CertaintyLevel.Exact))
                    : ImmutableArray.Create(
                        new DispatchCandidate(Handler, "DispatchHandler.Handle", partition is not "body-unavailable",
                            [new EvidenceRef(new EvidenceId("evidence:v1:dispatch-handler:candidate"), EvidenceKind.Source,
                                "DispatchHandlerFlow.cs", null, "handler", "test", CertaintyLevel.Exact)], CertaintyLevel.Exact)),
                DispatchPipelineMetadata.Unknown, [DispatchHandlerFlowTests.Evidence(partition)], CertaintyLevel.Exact),
            foreignProfile: partition is "foreign-profile",
            foreignFingerprint: partition is "foreign-fingerprint");
        return request;
    }
}
