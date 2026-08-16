using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Behavior.Tests;

public sealed class BehaviorAnalyzerTests
{
    private static readonly CompilationProfile Profile = CompilationProfile.Create("Branching.csproj", "Release", "net10.0");

    [Fact]
    public async Task AnalyzeAsyncProducesDeterministicSnapshot()
    {
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            [],
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var analyzer = new BehaviorAnalyzer();
        var first = await analyzer.AnalyzeAsync(request, CancellationToken.None);
        var second = await analyzer.AnalyzeAsync(request, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.NotNull(first.Value);
        Assert.Equal(1, first.Value!.SchemaVersion);
        Assert.Equal(Profile.Id, first.Value.Profile.Id);
        Assert.Equal(first.Value.BehaviorFingerprint, second.Value!.BehaviorFingerprint);
    }

    [Fact]
    public async Task AnalyzeAsyncNormalizesExtractedBodiesIntoMethodFlows()
    {
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(CreateBranchingBody()),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.MethodFlows);
        var flow = result.Value.MethodFlows[0];
        Assert.Equal(64, flow.FlowFingerprint.Length);
        Assert.Contains(flow.Nodes, node => node.Kind == FlowNodeKind.Entry);
        Assert.Contains(flow.Nodes, node => node.Kind == FlowNodeKind.Exit);
        Assert.Contains(flow.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.NormalCompletion);
        Assert.Contains(flow.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.ExplicitReturn);
        Assert.Equal(64, result.Value.BehaviorFingerprint.Length);
    }

    private static ExtractedMethodBody CreateBranchingBody()
    {
        var methodId = new MethodId("method:v1:test");
        var literalId = new OperationId("behavior-operation:v1:literal");
        var conditionId = new OperationId("behavior-operation:v1:condition");
        var returnValueId = new OperationId("behavior-operation:v1:return-value");
        return new ExtractedMethodBody(
            methodId,
            "body-fingerprint",
            [],
            [],
            ImmutableArray.Create(
                new ExtractedOperation(
                    literalId,
                    methodId,
                    ExtractedOperationKind.Literal,
                    null,
                    [],
                    0,
                    "System.Int32",
                    "1",
                    false,
                    true,
                    [],
                    [],
                    [],
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    [],
                    CertaintyLevel.Exact),
                new ExtractedOperation(
                    conditionId,
                    methodId,
                    ExtractedOperationKind.Binary,
                    null,
                    [],
                    1,
                    "System.Boolean",
                    null,
                    false,
                    true,
                    [],
                    [],
                    [],
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    [],
                    CertaintyLevel.Exact),
                new ExtractedOperation(
                    returnValueId,
                    methodId,
                    ExtractedOperationKind.LocalReference,
                    null,
                    [],
                    2,
                    "System.Int32",
                    null,
                    false,
                    true,
                    [],
                    [],
                    [],
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "total",
                    null,
                    [],
                    CertaintyLevel.Exact)),
            ImmutableArray.Create(
                new ExtractedBasicBlock(
                    0,
                    [literalId],
                    null,
                    1,
                    [],
                    [],
                    ExtractedBlockTerminalKind.None,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact),
                new ExtractedBasicBlock(
                    1,
                    [],
                    conditionId,
                    2,
                    [3],
                    [0],
                    ExtractedBlockTerminalKind.Conditional,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact),
                new ExtractedBasicBlock(
                    2,
                    [returnValueId],
                    null,
                    3,
                    [],
                    [1],
                    ExtractedBlockTerminalKind.Return,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact),
                new ExtractedBasicBlock(
                    3,
                    [],
                    null,
                    null,
                    [],
                    [1],
                    ExtractedBlockTerminalKind.Exit,
                    false,
                    [],
                    [],
                    [],
                    CertaintyLevel.Exact)),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                3,
                null,
                [],
                CertaintyLevel.Exact)),
            []);
    }

    [Fact]
    public async Task AnalyzeAsyncCarriesExtractionDiagnosticsIntoSnapshot()
    {
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            [],
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(64, result.Value!.BehaviorFingerprint.Length);
    }

    [Fact]
    public async Task AnalyzeAsyncFailsOnMalformedExtraction()
    {
        var malformedBlock = new ExtractedBasicBlock(
            0,
            [],
            null,
            99,
            [],
            [],
            ExtractedBlockTerminalKind.None,
            false,
            [],
            [],
            [],
            CertaintyLevel.Exact);
        var body = new ExtractedMethodBody(
            new MethodId("method:v1:malformed"),
            "body-fingerprint",
            [],
            [],
            [],
            ImmutableArray.Create(malformedBlock),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                0,
                null,
                [],
                CertaintyLevel.Exact)),
            []);
        var input = new ExtractedBehaviorInput(
            Profile,
            "index-fingerprint",
            ImmutableArray.Create(body),
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(CreateEmptyIndex(), input);

        var result = await new BehaviorAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationOutcome.AnalysisFailure, result.Outcome);
        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BD1009");
    }

    private static ProgramIndexSnapshot CreateEmptyIndex() =>
        new(
            1,
            "test",
            Profile,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "index-fingerprint");
}
