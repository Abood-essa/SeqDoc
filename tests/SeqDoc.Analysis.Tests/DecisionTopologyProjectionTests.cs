using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// accepted contract compiler boundary proof through the frozen DecisionTopology fixture. The fixture's service
/// method keeps a result-factory invocation and its return in each controlled terminal arm, so the
/// compiler-to-Method-Flow projection must expose represented return terminals and every guarded node.
/// The baseline extractor never controls a ReturnFlowNode and only the first node per block, so the
/// terminal-coverage assertions fail RED until the architecture decision repair lands. This file deliberately does
/// not edit the preserved existing MethodFlowGoldenTests or its branching golden.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class DecisionTopologyProjectionTests
{
    [Fact]
    public async Task WorkItemServiceFlowControlsEveryTerminalArmNodeIncludingRepresentedReturns()
    {
        var request = CreateFixtureRequest();
        var (result, analyzed) = await AnalyzeFixtureAsync(request);

        var names = result.Value!.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var serviceFlow = analyzed.Value!.MethodFlows.First(flow => names[flow.Method] == "ProcessAsync");
        var dependences = serviceFlow.ControlDependences;
        Assert.Equal(64, serviceFlow.FlowFingerprint.Length);

        // Fixture shape: two early-return decisions and at least the three terminal return statements
        // (absent, locked, success) are projected into the flow.
        Assert.True(serviceFlow.Nodes.OfType<DecisionFlowNode>().Count() >= 2);
        var returnIds = serviceFlow.Nodes.OfType<ReturnFlowNode>().Select(node => node.Id).ToHashSet();
        Assert.True(returnIds.Count >= 3);

        // RED: the represented return terminals of the guarded arms must be controlled once the
        // repair lands; the baseline extractor never matches a ReturnFlowNode.
        Assert.Contains(dependences, dependence => returnIds.Contains(dependence.ControlledNode));

        // RED (stronger): every decision arm that carries a result-factory invocation must also
        // control the represented terminal of that same arm, so the factory and its return stay in one
        // controlled block. The compiler may lay a guarded terminal arm out on the true or the false
        // successor (this fixture projects both factories and their returns on the false arms), so
        // the join is proven per (decision, semantic polarity) rather than assuming one fixed arm.
        var factoryIds = serviceFlow.Nodes.OfType<InvocationFlowNode>()
            .Where(node => node.Target is { } target && IsResultFactoryMethod(names, target))
            .Select(node => node.Id)
            .ToHashSet();
        Assert.NotEmpty(factoryIds);
        var guardedFactoryArms = dependences
            .Where(dependence => factoryIds.Contains(dependence.ControlledNode))
            .Select(dependence => (dependence.ControllingDecision, dependence.ControlledOnTrue))
            .Distinct()
            .ToArray();
        Assert.NotEmpty(guardedFactoryArms);
        foreach (var (decisionId, onTrue) in guardedFactoryArms)
        {
            Assert.Contains(dependences, dependence => dependence.ControllingDecision == decisionId
                && dependence.ControlledOnTrue == onTrue
                && returnIds.Contains(dependence.ControlledNode));
        }

        // The result-factory invocations themselves are guarded (first-node projection already works).
        foreach (var factoryId in factoryIds)
        {
            Assert.Contains(dependences, dependence => dependence.ControlledNode == factoryId);
        }

        // Synthetic structural nodes never become control-dependence targets.
        Assert.DoesNotContain(dependences, dependence => serviceFlow.Nodes.OfType<EntryFlowNode>().Any(node => node.Id == dependence.ControlledNode));
        Assert.DoesNotContain(dependences, dependence => serviceFlow.Nodes.OfType<ExitFlowNode>().Any(node => node.Id == dependence.ControlledNode));
        Assert.DoesNotContain(dependences, dependence => serviceFlow.Nodes.OfType<LoopNode>().Any(node => node.Id == dependence.ControlledNode));
        Assert.DoesNotContain(dependences, dependence => serviceFlow.Nodes.OfType<UnknownOperationFlowNode>().Any(node => node.Id == dependence.ControlledNode));

        // The continuing path still projects the exact save operation. The EF framework target is not
        // a source method in the Program Index, so the projection is proven by an awaited invocation
        // whose target is not one of the source result factories (the query and the save are both
        // awaited continuation calls).
        var awaitedContinuationInvocations = serviceFlow.Nodes.OfType<InvocationFlowNode>()
            .Where(node => node.Target is { } target && !IsResultFactoryMethod(names, target)
                && serviceFlow.Nodes.OfType<AwaitFlowNode>().Any(awaitNode => awaitNode.Operand == node.Operation))
            .ToArray();
        Assert.NotEmpty(awaitedContinuationInvocations);
    }

    [Fact]
    public async Task WorkItemServiceFlowIsDeterministicAcrossRepeatedProjection()
    {
        var request = CreateFixtureRequest();
        var (result, analyzed) = await AnalyzeFixtureAsync(request);
        var (_, secondAnalyzed) = await AnalyzeFixtureAsync(request);

        var names = result.Value!.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var firstFlow = analyzed.Value!.MethodFlows.First(flow => names[flow.Method] == "ProcessAsync");
        var secondFlow = secondAnalyzed.Value!.MethodFlows.First(flow => names[flow.Method] == "ProcessAsync");

        Assert.Equal(firstFlow.FlowFingerprint, secondFlow.FlowFingerprint);
        Assert.Equal(
            firstFlow.ControlDependences
                .OrderBy(dependence => dependence.ControllingDecision.Value, StringComparer.Ordinal)
                .ThenBy(dependence => dependence.ControlledNode.Value, StringComparer.Ordinal)
                .Select(dependence => $"{dependence.ControllingDecision.Value}:{dependence.ControlledNode.Value}:{dependence.ControlledOnTrue}"),
            secondFlow.ControlDependences
                .OrderBy(dependence => dependence.ControllingDecision.Value, StringComparer.Ordinal)
                .ThenBy(dependence => dependence.ControlledNode.Value, StringComparer.Ordinal)
                .Select(dependence => $"{dependence.ControllingDecision.Value}:{dependence.ControlledNode.Value}:{dependence.ControlledOnTrue}"));
    }

    private static bool IsResultFactoryMethod(Dictionary<MethodId, string> names, MethodId target)
        => names.TryGetValue(target, out var name)
            && name is "Success" or "NotFound" or "Conflict";

    private static async Task<(ApplicationResult<ProfileAnalysisExtraction> Result, ApplicationResult<BehaviorSnapshot> Analyzed)> AnalyzeFixtureAsync(CompilationAnalysisRequest request)
    {
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        var analyzed = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(result.Value!.ProgramIndex, result.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(analyzed.IsSuccess);
        return (result, analyzed);
    }

    private static CompilationAnalysisRequest CreateFixtureRequest()
    {
        var root = FindRepositoryRoot();
        var relativePath = "tests/fixtures/AdvancedAnalysis/DecisionTopology/DecisionTopology.csproj";
        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
    }

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
