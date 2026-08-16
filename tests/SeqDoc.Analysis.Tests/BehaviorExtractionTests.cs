using System.Text.Json;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class BehaviorExtractionTests
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public async Task BranchingFixtureProducesDeterministicExtraction()
    {
        var result = await ExtractFixtureAsync("Branching");

        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);
        Assert.NotEmpty(extraction.BehaviorInput.Methods);
        Assert.All(extraction.BehaviorInput.Methods, body => Assert.Equal(64, body.BodyFingerprint.Length));
        Assert.Equal(64, extraction.BehaviorInput.InputFingerprint.Length);
        Assert.Equal(extraction.ProgramIndex.IndexFingerprint, extraction.BehaviorInput.ProgramIndexFingerprint);
        Assert.True(extraction.BehaviorInput.TypeHierarchy.IsComplete);
        Assert.Contains(extraction.BehaviorInput.TypeHierarchy.Types, node => node.MetadataName.EndsWith("FlowShapes", StringComparison.Ordinal));
        var typedInvocation = Assert.Single(
            extraction.BehaviorInput.Methods.SelectMany(body => body.Operations),
            operation => operation.Invocation?.TargetContainingTypeName is not null);
        Assert.False(typedInvocation.Invocation!.IsLoadedProjectTarget);
        Assert.Equal("System.Console", typedInvocation.Invocation.TargetAssemblyName);
        Assert.True(typedInvocation.Invocation.IsPlatformTarget);
        Assert.Contains(
            extraction.BehaviorInput.Methods.SelectMany(body => body.Blocks),
            block => block.Operations.Contains(typedInvocation.Id)
                || (typedInvocation.Parent is { } parent && block.Operations.Contains(parent)));
        Assert.True(typedInvocation.EvaluationOrdinal >= 0);

        var throwBody = Assert.Single(
            extraction.BehaviorInput.Methods,
            body => extraction.ProgramIndex.Methods.Any(method => method.Id == body.Method && method.Name == "ThrowShape"));
        Assert.Contains(throwBody.Regions, region => region.Kind == ExtractedRegionKind.Finally);
        Assert.Contains(throwBody.Regions, region => region.Kind == ExtractedRegionKind.Catch);
        Assert.Contains(throwBody.Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Throw);
        Assert.Contains(throwBody.Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Rethrow);

        var ifElseBody = Assert.Single(
            extraction.BehaviorInput.Methods,
            body => extraction.ProgramIndex.Methods.Any(method => method.Id == body.Method && method.Name == "IfElse"));
        Assert.Contains(ifElseBody.Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Conditional);
        Assert.Contains(ifElseBody.Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Return);
    }

    [Fact]
    public async Task BranchingFixtureMatchesHumanReviewedGolden()
    {
        var result = await ExtractFixtureAsync("Branching");

        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);
        var projection = CreateGoldenProjection(extraction);
        var goldenPath = Path.Combine(FindRepositoryRoot(), "tests", "SeqDoc.Analysis.Tests", "Golden", "branching-behavior-extraction.json");
        var expected = await File.ReadAllTextAsync(goldenPath);
        Assert.Equal(NormalizeLines(expected), NormalizeLines(projection));
    }

    [Fact]
    public async Task BranchingFixtureClassifiesThrowsByCatchCoverage()
    {
        var result = await ExtractFixtureAsync("Branching");

        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);
        ExtractedMethodBody Body(string name) => Assert.Single(
            extraction.BehaviorInput.Methods,
            body => extraction.ProgramIndex.Methods.Any(method => method.Id == body.Method && method.Name == name));

        Assert.Contains(Body("UncaughtThrow").Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Throw && block.EscapingThrow);
        Assert.Contains(Body("WrongCatchType").Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Throw && block.EscapingThrow);
        Assert.Contains(Body("CaughtByBaseType").Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Throw && !block.EscapingThrow);
        Assert.Contains(Body("RethrowCaughtByOuter").Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Rethrow && !block.EscapingThrow);
        var mixed = Body("MixedSwitchAndThrow").Blocks.Where(block => block.Terminal == ExtractedBlockTerminalKind.Throw).ToArray();
        Assert.Contains(mixed, block => block.EscapingThrow);
        Assert.Contains(mixed, block => !block.EscapingThrow);
        Assert.Contains(Body("GenericCaughtThrow").Blocks, block => block.Terminal == ExtractedBlockTerminalKind.Throw && !block.EscapingThrow);
    }

    private static async Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractFixtureAsync(string name)
    {
        var root = FindRepositoryRoot();
        var relativePath = $"tests/fixtures/PassB/{name}/{name}.csproj";
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
        return await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
    }

    private static string CreateGoldenProjection(ProfileAnalysisExtraction extraction)
    {
        var input = extraction.BehaviorInput;
        var projection = new
        {
            InputFingerprint = input.InputFingerprint,
            Methods = input.Methods.Select(body =>
            {
                var name = extraction.ProgramIndex.Methods.First(method => method.Id == body.Method).Name;
                return new
                {
                    Name = name,
                    Method = body.Method.Value,
                    BodyFingerprint = body.BodyFingerprint,
                    ParameterCount = body.Parameters.Length,
                    LocalCount = body.Locals.Length,
                    OperationCount = body.Operations.Length,
                    BlockCount = body.Blocks.Length,
                    RegionCount = body.Regions.Length,
                    Operations = body.Operations.Select(operation => new
                    {
                        Kind = operation.Kind.ToString(),
                        EvaluationOrdinal = operation.EvaluationOrdinal,
                        IsImplicit = operation.IsImplicit,
                        Parent = operation.Parent?.Value,
                        OperandCount = operation.Operands.Length,
                        Type = operation.TypeDescriptor,
                        Constant = operation.ConstantValue,
                        ReferencedMethods = operation.ReferencedMethods.Select(method => method.Value).Order(StringComparer.Ordinal),
                        InvocationTarget = operation.Invocation?.Target?.Value,
                        InvocationTargetContainingType = operation.Invocation?.TargetContainingTypeName,
                        InvocationTargetMethod = operation.Invocation?.TargetMethodName,
                        InvocationIsLoadedProjectTarget = operation.Invocation?.IsLoadedProjectTarget,
                        InvocationTargetAssembly = operation.Invocation?.TargetAssemblyName,
                        InvocationIsPlatformTarget = operation.Invocation?.IsPlatformTarget,
                        AssignmentTarget = operation.Assignment?.Target.Value,
                        AssignmentValue = operation.Assignment?.Value.Value,
                        IsCompound = operation.Assignment?.IsCompound,
                        ConversionFrom = operation.Conversion?.FromType,
                        ConversionTo = operation.Conversion?.ToType,
                        ReturnValue = operation.Return?.Value?.Value,
                        ThrowValue = operation.Throw?.Exception?.Value,
                        IsRethrow = operation.Throw?.IsRethrow,
                        AwaitOperand = operation.Await?.Operand.Value,
                    }),
                    Blocks = body.Blocks.Select(block => new
                    {
                        Ordinal = block.Ordinal,
                        Terminal = block.Terminal.ToString(),
                        EscapingThrow = block.EscapingThrow,
                        BranchCondition = block.BranchCondition?.Value,
                        FallThrough = block.FallThroughSuccessor,
                        Conditionals = block.ConditionalSuccessors.Order(),
                        Predecessors = block.Predecessors.Order(),
                        Operations = block.Operations.Select(id => id.Value),
                        EnteringRegions = block.EnteringRegions.Select(id => id.Value).Order(StringComparer.Ordinal),
                        LeavingRegions = block.LeavingRegions.Select(id => id.Value).Order(StringComparer.Ordinal),
                    }),
                    Regions = body.Regions.Select(region => new
                    {
                        Kind = region.Kind.ToString(),
                        Ordinal = region.Ordinal,
                        Parent = region.Parent?.Value,
                        StartBlock = region.StartBlockOrdinal,
                        EndBlock = region.EndBlockOrdinal,
                        ExceptionType = region.ExceptionType,
                    }),
                };
            }),
            TypedInvocations = input.Methods.SelectMany(body => body.Operations
                .Where(operation => operation.Invocation?.TargetContainingTypeName is not null)
                .Select(operation => new
                {
                    Target = operation.Invocation!.Target!.Value,
                    ContainingType = operation.Invocation.TargetContainingTypeName,
                    Method = operation.Invocation.TargetMethodName,
                    IsLoadedProjectTarget = operation.Invocation.IsLoadedProjectTarget,
                    TargetAssembly = operation.Invocation.TargetAssemblyName,
                    IsPlatformTarget = operation.Invocation.IsPlatformTarget,
                    BlockOrdinal = body.Blocks.First(block =>
                        block.Operations.Contains(operation.Id)
                            || (operation.Parent is { } parent && block.Operations.Contains(parent))).Ordinal,
                    EvaluationOrdinal = operation.EvaluationOrdinal,
                })),
            TypeCount = input.TypeHierarchy.Types.Length,
            InstantiationCount = input.Instantiations.Length,
        };
        return JsonSerializer.Serialize(projection, IndentedJson);
    }

    private static string NormalizeLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
