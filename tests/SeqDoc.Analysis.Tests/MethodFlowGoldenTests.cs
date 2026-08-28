using System.Text.Json;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class MethodFlowGoldenTests
{
    private static readonly string[] LoopShapeNames =
        ["ForLoopShape", "WhileLoopShape", "ForEachShape"];

    private static readonly string[] AutoAccessorNames = ["get_Value", "set_Value"];

    private static readonly string[] ExpectedVirtualContainingTypes = ["AddProcessor", "BaseProcessor", "MultiplyProcessor"];

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public async Task BranchingFixtureProducesNormalizedMethodFlows()
    {
        var request = CreateFixtureRequest("Branching");
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var analyzed = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(result.Value!.ProgramIndex, result.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(analyzed.IsSuccess);

        var flows = analyzed.Value!.MethodFlows;
        Assert.NotEmpty(flows);
        Assert.Contains(flows, flow => flow.Outcomes.Any(outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow));
        Assert.Contains(flows, flow => flow.Nodes.Any(node => node.Kind == FlowNodeKind.Loop));
        Assert.All(flows, flow => Assert.Equal(64, flow.FlowFingerprint.Length));

        var projection = CreateGoldenProjection(analyzed.Value, result.Value.ProgramIndex.Methods);
        var goldenPath = Path.Combine(FindRepositoryRoot(), "tests", "SeqDoc.Analysis.Tests", "Golden", "branching-method-flow.json");
        var expected = await File.ReadAllTextAsync(goldenPath);
        Assert.Equal(NormalizeLines(expected), NormalizeLines(projection));
    }

    [Fact]
    public async Task BranchingFixtureClassifiesThrowsAtFlowLevel()
    {
        var request = CreateFixtureRequest("Branching");
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        var analyzed = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(result.Value!.ProgramIndex, result.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(analyzed.IsSuccess);
        var names = result.Value.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);

        var uncaught = analyzed.Value!.MethodFlows.First(flow => names[flow.Method] == "UncaughtThrow");
        Assert.Contains(uncaught.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);

        var rethrowOuter = analyzed.Value!.MethodFlows.First(flow => names[flow.Method] == "RethrowCaughtByOuter");
        Assert.DoesNotContain(rethrowOuter.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);

        var mixed = analyzed.Value!.MethodFlows.First(flow => names[flow.Method] == "MixedSwitchAndThrow");
        Assert.Contains(mixed.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);

        var caught = analyzed.Value!.MethodFlows.First(flow => names[flow.Method] == "CaughtByBaseType");
        Assert.DoesNotContain(caught.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
    }

    [Fact]
    public async Task DispatchFixtureProducesCorrectCallKindsAndChaExtensions()
    {
        var request = CreateFixtureRequest("DispatchAndValues");
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var analyzed = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(result.Value!.ProgramIndex, result.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(analyzed.IsSuccess);
        var names = result.Value.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var flows = analyzed.Value!.MethodFlows;
        var calls = analyzed.Value.CallGraph.CallSites;

        Assert.Contains(calls, site => site.Kind == CallKind.Dynamic);
        var dynamicSite = calls.First(site => site.Kind == CallKind.Dynamic);
        Assert.Equal(CallResolutionKind.Unknown, dynamicSite.Resolution.Kind);
        Assert.Contains(dynamicSite.Resolution.Diagnostics, diagnostic => diagnostic.Code == "BD3001");

        Assert.Contains(calls, site => site.Kind == CallKind.Static);
        Assert.Contains(calls, site => site.Kind == CallKind.Constructor);
        Assert.Contains(calls, site => site.Kind == CallKind.Instance);
        Assert.Contains(calls, site => site.Kind == CallKind.DelegateOrEvent
            && site.ContainingMethod == flows.First(flow => names[flow.Method] == "EventShape").Method);
        var eventShapeSites = calls
            .Where(site => site.Kind == CallKind.DelegateOrEvent
                && site.ContainingMethod == flows.First(flow => names[flow.Method] == "EventShape").Method)
            .ToArray();
        Assert.True(eventShapeSites.Length >= 2, "Event subscription and removal should produce distinct call sites.");
        Assert.Equal(
            eventShapeSites.Select(site => site.DeclaredTarget).Distinct().Count(),
            eventShapeSites.Length);
        var bclConstructor = calls.Single(site => site.Kind == CallKind.Constructor
            && site.ContainingMethod == flows.First(flow => names[flow.Method] == "BclConstructorShape").Method);
        Assert.True(bclConstructor.DeclaredTarget is not null);
        Assert.False(result.Value.ProgramIndex.Methods.Any(method => method.Id == bclConstructor.DeclaredTarget),
            "The BCL constructor must not be indexed as a source method.");

        var defaultShape = flows.First(flow => names[flow.Method] == "DefaultInterfaceShape");
        var defaultCall = Assert.Single(defaultShape.Nodes.OfType<InvocationFlowNode>());
        Assert.True(defaultCall.Target != null);
        var defaultCandidates = calls.Single(site => site.InvocationOperation == defaultCall.Operation).Resolution.Candidates;
        Assert.Contains(defaultCandidates, candidate => names[candidate] == "ProcessDefault");

        var explicitShape = flows.First(flow => names[flow.Method] == "ExplicitInterfaceShape");
        var explicitCall = Assert.Single(explicitShape.Nodes.OfType<InvocationFlowNode>());
        var explicitCandidates = calls.Single(site => site.InvocationOperation == explicitCall.Operation).Resolution.Candidates;
        Assert.Contains(explicitCandidates, candidate => names[candidate].EndsWith("IPaymentProcessor.Process", StringComparison.Ordinal));

        var asyncShape = flows.First(flow => names[flow.Method] == "AsyncShape");
        Assert.Contains(asyncShape.Nodes, node => node.Kind == FlowNodeKind.Await);

        var doWhile = flows.First(flow => names[flow.Method] == "DoWhileShape");
        var doWhileLoop = Assert.Single(doWhile.Nodes.OfType<LoopNode>());
        Assert.NotEmpty(doWhileLoop.Body);
        Assert.DoesNotContain(doWhileLoop.Exits, exit => exit == doWhileLoop.Header);
        foreach (var loopName in LoopShapeNames)
        {
            var loopFlow = flows.First(flow => names[flow.Method] == loopName);
            Assert.Contains(loopFlow.Nodes, node => node.Kind == FlowNodeKind.Loop);
        }

        var virtualClass = flows.First(flow => names[flow.Method] == "VirtualClassShape");
        var virtualCall = Assert.Single(virtualClass.Nodes.OfType<InvocationFlowNode>());
        var virtualCandidateIds = calls.Single(site => site.InvocationOperation == virtualCall.Operation).Resolution.Candidates;
        var containingTypeOf = result.Value.ProgramIndex.Methods.ToDictionary(
            method => method.Id,
            method => result.Value.ProgramIndex.Types.FirstOrDefault(type => type.Id == method.ContainingType)?.MetadataName ?? string.Empty);
        var virtualContainingTypes = virtualCandidateIds
            .Select(candidate => containingTypeOf[candidate])
            .Select(name => name[(name.LastIndexOf('.') + 1)..])
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedVirtualContainingTypes, virtualContainingTypes);

        var generatedNames = result.Value.ProgramIndex.Methods
            .Select(method => method.Name)
            .Where(name => name is "get_Value" or "set_Value")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(AutoAccessorNames, generatedNames);

        Assert.Contains(calls, site => site.ContainingMethod == flows.First(flow => names[flow.Method] == "ReflectionShape").Method
            && site.Resolution.Kind == CallResolutionKind.DirectExact
            && site.Kind == CallKind.Instance);
    }

    [Fact]
    public async Task DispatchFixtureReportsBodylessMethodsAsNonBlockingWarnings()
    {
        var request = CreateFixtureRequest("DispatchAndValues");
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!.BehaviorInput.Diagnostics, diagnostic => diagnostic.Code == "BE1001");

        var analyzed = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(result.Value.ProgramIndex, result.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(analyzed.IsSuccess, "Bodyless-method warnings must not fail analysis.");
    }

    [Fact]
    public async Task DispatchFixtureProducesChaCandidates()
    {
        var request = CreateFixtureRequest("DispatchAndValues");
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var analyzed = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(result.Value!.ProgramIndex, result.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(analyzed.IsSuccess);

        var callSites = analyzed.Value!.CallGraph.CallSites;
        var interfaceChaSites = callSites
            .Where(site => site.Resolution.Kind == CallResolutionKind.Cha
                && site.DeclaredTarget is { } declared
                && result.Value.ProgramIndex.Methods.Any(method => method.Id == declared && method.Name == "Process"))
            .ToArray();
        Assert.True(interfaceChaSites.Length >= 1, $"Expected interface dispatch CHA sites, found {interfaceChaSites.Length}.");
        var interfaceCall = interfaceChaSites[0];
        Assert.Equal(CallKind.Instance, interfaceCall.Kind);
        Assert.True(interfaceCall.Resolution.Candidates.Length >= 2);
        Assert.Equal(
            interfaceCall.Resolution.Candidates.Select(candidate => candidate.Value).Order(StringComparer.Ordinal),
            interfaceCall.Resolution.Candidates.Select(candidate => candidate.Value));

        var directCalls = callSites.Where(site => site.Resolution.Kind == CallResolutionKind.DirectExact).ToArray();
        Assert.Contains(directCalls, site => site.Kind == CallKind.Static);
        Assert.Contains(directCalls, site => site.Kind == CallKind.Constructor);

        var delegateSites = callSites.Where(site => site.Kind == CallKind.DelegateOrEvent).ToArray();
        Assert.True(delegateSites.Length >= 1, "Expected at least one delegate/event call site.");
        Assert.All(delegateSites, site => Assert.Equal(CertaintyLevel.Unknown, site.Resolution.Certainty));

        Assert.Contains(analyzed.Value.RtaFoundation.Instantiations, fact =>
            result.Value.ProgramIndex.Types.Any(type => type.Id == fact.InstantiatedType
                && type.MetadataName.EndsWith("CardPaymentProcessor", StringComparison.Ordinal)));
    }

    private static CompilationAnalysisRequest CreateFixtureRequest(string name)
    {
        var root = FindRepositoryRoot();
        var relativePath = $"tests/fixtures/PassB/{name}/{name}.csproj";
        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
    }

    private static string CreateGoldenProjection(BehaviorSnapshot snapshot, System.Collections.Immutable.ImmutableArray<SeqDoc.Core.ProgramIndex.ProgramMethod> methods)
    {
        var projection = new
        {
            BehaviorFingerprint = snapshot.BehaviorFingerprint,
            CallSiteCount = snapshot.CallGraph.CallSites.Length,
            CallEdgeCount = snapshot.CallGraph.Edges.Length,
            CallSites = snapshot.CallGraph.CallSites.Select(site => new
            {
                ContainingMethod = methods.First(method => method.Id == site.ContainingMethod).Name,
                Kind = site.Kind.ToString(),
                Resolution = site.Resolution.Kind.ToString(),
                CandidateCount = site.Resolution.Candidates.Length,
                CandidateTargets = site.Resolution.Candidates
                    .Select(candidate => methods.FirstOrDefault(method => method.Id == candidate)?.Name ?? candidate.Value)
                    .Order(StringComparer.Ordinal),
                Certainty = site.Resolution.Certainty.ToString(),
            }),
            CallEdges = snapshot.CallGraph.Edges.Select(edge => new
            {
                Caller = edge.Caller.Value,
                CandidateTarget = edge.CandidateTarget.Value,
            }),
            MethodFlows = snapshot.MethodFlows.Select(flow =>
            {
                var name = methods.First(method => method.Id == flow.Method).Name;
                return new
                {
                    Name = name,
                    FlowFingerprint = flow.FlowFingerprint,
                    NodeCount = flow.Nodes.Length,
                    EdgeCount = flow.Edges.Length,
                    RegionCount = flow.Regions.Length,
                    OutcomeCount = flow.Outcomes.Length,
                    ValueNodeCount = flow.ValueGraph.Nodes.Length,
                    ValueEdgeCount = flow.ValueGraph.Edges.Length,
                    ControlDependenceCount = flow.ControlDependences.Length,
                    Nodes = flow.Nodes.Select(node => new
                    {
                        Kind = node.Kind.ToString(),
                        Operation = (node as OperationFlowNode)?.Operation.Value
                            ?? (node as InvocationFlowNode)?.Operation.Value
                            ?? (node as DecisionFlowNode)?.Condition.Value
                            ?? (node as UnknownOperationFlowNode)?.Operation.Value,
                        Target = (node as InvocationFlowNode)?.Target?.Value,
                        IsDispatchable = (node as InvocationFlowNode)?.IsDispatchable,
                        IsRethrow = (node as ThrowFlowNode)?.IsRethrow,
                    }),
                    Edges = flow.Edges.Select(edge => new
                    {
                        Kind = edge.Kind.ToString(),
                        Source = edge.Source.Value,
                        Target = edge.Target.Value,
                    }),
                    Regions = flow.Regions.Select(region => new
                    {
                        Kind = region.Kind.ToString(),
                        Parent = region.Parent?.Value,
                        ExceptionType = region.ExceptionType,
                    }),
                    Outcomes = flow.Outcomes.Select(outcome => new
                    {
                        Kind = outcome.Kind.ToString(),
                        Block = outcome.BlockOrdinal,
                    }),
                    ValueNodes = flow.ValueGraph.Nodes.Select(node => new
                    {
                        Kind = node.Kind.ToString(),
                        Name = node.Name,
                        Parameter = node.ParameterOrdinal,
                        Constant = node.ConstantValue,
                    }),
                    ValueEdges = flow.ValueGraph.Edges.Select(edge => new
                    {
                        Kind = edge.Kind.ToString(),
                        Source = edge.Source.Value,
                        Target = edge.Target.Value,
                    }),
                    ControlDependences = flow.ControlDependences.Select(dependence => new
                    {
                        Decision = dependence.ControllingDecision.Value,
                        Controlled = dependence.ControlledNode.Value,
                        OnTrue = dependence.ControlledOnTrue,
                    }),
                    Summary = flow.Summary is null
                        ? null
                        : new
                        {
                            IsComplete = flow.Summary.IsComplete,
                            Certainty = flow.Summary.Certainty.ToString(),
                            StateReadCount = flow.Summary.StateReads.Length,
                            StateWriteCount = flow.Summary.StateWrites.Length,
                            ParameterFlows = flow.Summary.ParameterFlows.Select(parameterFlow => new
                            {
                                Name = parameterFlow.ParameterName,
                                FlowsToReturn = parameterFlow.FlowsToReturn,
                                InfluencesStateWrite = parameterFlow.InfluencesStateWrite,
                            }),
                        },
                };
            }),
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
