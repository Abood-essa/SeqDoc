using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Behavior.Tests;

public sealed class ExtractionValidatorTests
{
    private static readonly CompilationProfile Profile = CompilationProfile.Create("Branching.csproj", "Release", "net10.0");
    private static readonly MethodId Method = new("method:v1:test");

    [Fact]
    public void ValidInputHasNoDiagnostics()
    {
        var input = CreateInput(ImmutableArray.Create(CreateBody()));
        var diagnostics = ExtractionValidator.Validate(input);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DuplicateMethodBodyIsRejected()
    {
        var body = CreateBody();
        var input = CreateInput(ImmutableArray.Create(body, body));
        var diagnostics = ExtractionValidator.Validate(input);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "BD1001");
    }

    [Fact]
    public void UnorderedMethodsAreRejected()
    {
        var first = CreateBody(MethodId: new MethodId("method:v1:aaa"));
        var second = CreateBody(MethodId: new MethodId("method:v1:zzz"));
        var unordered = CreateInput(ImmutableArray.Create(second, first));
        Assert.Contains(ExtractionValidator.Validate(unordered), diagnostic => diagnostic.Code == "BD1002");
    }

    [Fact]
    public void NonContiguousBlockOrdinalsAreRejected()
    {
        var body = CreateBody(Blocks: ImmutableArray.Create(
            new ExtractedBasicBlock(
                2,
                [],
                null,
                null,
                [],
                 [],
                 ExtractedBlockTerminalKind.Exit,
                 false,
                 [],
                 [],
                 [],
                 CertaintyLevel.Exact)));
        var input = CreateInput(ImmutableArray.Create(body));
        Assert.Contains(ExtractionValidator.Validate(input), diagnostic => diagnostic.Code == "BD1006");
    }

    [Fact]
    public void UnknownBlockSuccessorIsRejected()
    {
        var body = CreateBody(Blocks: ImmutableArray.Create(
            new ExtractedBasicBlock(
                0,
                [],
                null,
                5,
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
                null,
                null,
                [],
                [],
                ExtractedBlockTerminalKind.Exit,
                false,
                [],
                [],
                [],
                CertaintyLevel.Exact)));
        var input = CreateInput(ImmutableArray.Create(body));
        Assert.Contains(ExtractedBehaviorInputValidator(input), diagnostic => diagnostic.Code == "BD1009");
    }

    [Fact]
    public void RegionParentMustPrecedeChild()
    {
        var body = CreateBody(Regions: ImmutableArray.Create(
            new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:child"),
                ExtractedRegionKind.Try,
                new FlowRegionId("flow-region:v1:parent"),
                0,
                0,
                1,
                null,
                [],
                CertaintyLevel.Exact)));
        var input = CreateInput(ImmutableArray.Create(body));
        Assert.Contains(ExtractedBehaviorInputValidator(input), diagnostic => diagnostic.Code == "BD1013");
    }

    [Fact]
    public void InvalidRegionBlockRangeIsRejected()
    {
        var body = CreateBody(Regions: ImmutableArray.Create(
            new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:try"),
                ExtractedRegionKind.Try,
                null,
                0,
                2,
                9,
                null,
                [],
                CertaintyLevel.Exact)));
        var input = CreateInput(ImmutableArray.Create(body));
        Assert.Contains(ExtractedBehaviorInputValidator(input), diagnostic => diagnostic.Code == "BD1014");
    }

    private static ImmutableArray<AnalysisDiagnostic> ExtractedBehaviorInputValidator(ExtractedBehaviorInput input) =>
        ExtractionValidator.Validate(input);

    private static ExtractedBehaviorInput CreateInput(ImmutableArray<ExtractedMethodBody> methods) =>
        new(
            Profile,
            "fingerprint",
            methods,
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            [],
            [],
            string.Empty);

    private static ExtractedMethodBody CreateBody(
        MethodId? MethodId = null,
        ImmutableArray<ExtractedBasicBlock>? Blocks = null,
        ImmutableArray<ExtractedExceptionRegion>? Regions = null) =>
        new(
            MethodId ?? Method,
            "body-fingerprint",
            [],
            [],
            [],
            Blocks ?? ImmutableArray.Create(new ExtractedBasicBlock(
                0,
                [],
                null,
                null,
                [],
                [],
                 ExtractedBlockTerminalKind.Exit,
                 false,
                 [],
                 [],
                 [],
                 CertaintyLevel.Exact)),
            Regions ?? ImmutableArray.Create(new ExtractedExceptionRegion(
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
}
