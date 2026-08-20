using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.FrameworkModels.MediatR;
using SeqDoc.Rendering.Markdown;
using SeqDoc.Testing;
using Xunit;

namespace SeqDoc.AcceptanceTests;

public sealed class CorpusMediatRTests
{
    private static string SourceRoot => ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.OpenSource).Root +
        Path.DirectorySeparatorChar + "DotNet-eShop";

    [Fact]
    public async Task OrderingDraftRouteReachesExactMediatRHandlerWithoutPipelineClaim()
    {
        const string relativeProject = "src/Ordering.API/Ordering.API.csproj";
        var project = Path.Combine(SourceRoot, relativeProject.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(project), project);
        var profile = CompilationProfile.Create(relativeProject, "Release", "net10.0");
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(SourceRoot, project, profile), CancellationToken.None);
        Assert.True(extraction.IsSuccess, Diagnostics(extraction.Diagnostics));
        var artifacts = extraction.Value!;
        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(artifacts.ProgramIndex, artifacts.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, Diagnostics(behavior.Diagnostics));

        var framework = await new FrameworkModelHost([new AspNetCoreMinimalApiModel(), new MediatRDispatchModel()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, artifacts.ProgramIndex),
                new FrameworkAnalysisContext(profile, artifacts.ProgramIndex, artifacts.CallbackBoundaryFacts),
                artifacts.Operations, artifacts.Symbols), CancellationToken.None);
        var dispatch = Assert.Single(framework.Facts.OfType<DispatchFact>(), fact =>
            fact.RequestType.EndsWith("CreateOrderDraftCommand", StringComparison.Ordinal));
        Assert.Equal("eShop.Ordering.API.Application.Commands.CreateOrderDraftCommand", dispatch.RequestType);
        Assert.Equal("CreateOrderDraftCommandHandler.Handle", Assert.Single(dispatch.Candidates).DisplayName);
        Assert.Empty(dispatch.Pipeline.Stages);

        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile, artifacts.ProgramIndex, behavior.Value!, framework, artifacts.SemanticFacts,
            artifacts.DependencyInjectionFacts, artifacts.StructuralResultFacts, artifacts.NonGetSemanticFacts,
            artifacts.ConditionalDependencyInjectionFacts, artifacts.ConfigurationSemanticFacts,
            artifacts.CallbackBoundaryFacts, artifacts.PredicateSemanticFacts, artifacts.MinimalApiHandlerFacts));
        var matchingGraphs = graphs.Graphs.Where(item => item.HttpMethod == HttpMethodKind.Post
            && item.CanonicalRoute.Trim('/') == "api/orders/draft").ToArray();
        Assert.True(
            matchingGraphs.Length == 1,
            $"Expected exactly one POST /api/orders/draft graph but found {matchingGraphs.Length}." +
             Environment.NewLine + RouteProjectionEvidence(artifacts, framework));
        var graph = matchingGraphs[0];
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.Dispatch
            && node.Detail.Contains("CreateOrderDraftCommand", StringComparison.Ordinal));
        Assert.True(graph.DispatchHandlerExpansion is not null, GraphExpansionEvidence(graph, behavior.Value!));
        var expansion = graph.DispatchHandlerExpansion!;
        Assert.True(expansion.IsComplete, GraphExpansionEvidence(graph, behavior.Value!));
        Assert.True(
            expansion.SourceSteps.Select(step => step.Label).SequenceEqual([
                "Order.NewDraft", "Order.AddOrderItem", "OrderDraftDTO.FromOrder", "Order.GetTotal"]),
            GraphExpansionEvidence(graph, behavior.Value!));
        Assert.True(
            expansion.Loops.Count(loop => loop.MemberSteps.Any(step =>
                step.Label == "Order.AddOrderItem" || step.TargetMethod.Value.Contains("Order.AddOrderItem", StringComparison.Ordinal))) == 1,
            GraphExpansionEvidence(graph, behavior.Value!));
        Assert.True(
            expansion.Return is not null
                && expansion.Return.TypeName == "OrderDraftDTO"
                && !string.IsNullOrWhiteSpace(expansion.Return.Operation.Value)
                && expansion.Return.Operation.Value.StartsWith("behavior-operation:", StringComparison.Ordinal),
            GraphExpansionEvidence(graph, behavior.Value!));
        var plan = DocumentationPlanner.Plan(graph);
        string fileName = DocumentationFileNaming.EntryKey(graph.EntryPoint, graph.OperationKey);
        var built = DocumentationSetBuilder.Build(
            profile.Id.Value,
            artifacts.ProgramIndex.IndexFingerprint,
            [new DocumentSetEntry(fileName, plan.Wording, plan.Diagram)]);
        Assert.True(built.Succeeded, string.Join("; ", built.Errors));

        string outputRoot = Path.Combine(Path.GetTempPath(), $"seqdoc-cr8-eshop-{Guid.NewGuid():N}");
        try
        {
            var activation = OutputSetActivator.Activate(outputRoot, built.Files);
            Assert.True(activation.Succeeded, activation.FailureMessage);

            string markdownPath = Path.Combine(outputRoot, fileName + ".md");
            string mermaidPath = Path.Combine(outputRoot, fileName + ".mmd");
            Assert.True(File.Exists(markdownPath), markdownPath);
            Assert.True(File.Exists(mermaidPath), mermaidPath);
            string markdown = File.ReadAllText(markdownPath);
            string mermaid = File.ReadAllText(mermaidPath);

            Assert.Empty(MermaidValidator.Validate(mermaid));
            Assert.Contains("CreateOrderDraftCommand", markdown, StringComparison.Ordinal);
            Assert.Contains("CreateOrderDraftCommandHandler.Handle", markdown, StringComparison.Ordinal);
            AssertSequence(markdown,
                "OrdersApi.CreateOrderDraftAsync", "CreateOrderDraftCommand", "CreateOrderDraftCommandHandler.Handle",
                "Order.NewDraft", "Order.AddOrderItem", "OrderDraftDTO.FromOrder", "Order.GetTotal");
            Assert.DoesNotContain("eShop.Ordering.API.Application", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("eShop.Ordering.API.Application.CreateOrderDraftCommandHandler.Handle", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("Technical fallback", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("SC-DISPATCH-CALL-WITHHELD", markdown, StringComparison.Ordinal);
            Assert.Contains("CreateOrderDraftCommand", mermaid, StringComparison.Ordinal);
            Assert.Contains("CreateOrderDraftCommandHandler.Handle", mermaid, StringComparison.Ordinal);
            AssertSequence(mermaid,
                "OrdersApi.CreateOrderDraftAsync", "CreateOrderDraftCommand", "CreateOrderDraftCommandHandler.Handle",
                "Order.NewDraft", "Order.AddOrderItem", "OrderDraftDTO.FromOrder", "Order.GetTotal");
            Assert.DoesNotContain("eShop.Ordering.API.Application", mermaid, StringComparison.Ordinal);
            Assert.DoesNotContain("Technical fallback", mermaid, StringComparison.Ordinal);
            Assert.DoesNotContain("SC-DISPATCH-CALL-WITHHELD", mermaid, StringComparison.Ordinal);
            Assert.DoesNotContain("pipeline", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pipeline", mermaid, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SourceRoot, markdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SourceRoot, mermaid, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Condition", markdown + mermaid, StringComparison.Ordinal);
            Assert.DoesNotContain("Continue", markdown + mermaid, StringComparison.Ordinal);
            Assert.DoesNotContain("Path", markdown + mermaid, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static string Diagnostics(IEnumerable<SeqDoc.Core.Diagnostics.AnalysisDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(item => $"{item.Code}: {item.TechnicalCause}"));

    private static string GraphExpansionEvidence(ScenarioGraph graph, BehaviorSnapshot behavior)
    {
        var expansion = graph.DispatchHandlerExpansion;
        var diagnostics = expansion is null
            ? string.Join(" | ", graph.Diagnostics.Select(item => $"{item.Code}:{item.Detail}"))
            : string.Join(" | ", expansion.Diagnostics.Select(item => $"{item.Code}:{item.Detail}"));
        var steps = expansion is null
            ? "<none>"
            : string.Join(" -> ", expansion.SourceSteps.Select(step => $"{step.Label}[{step.TargetMethod.Value}]"));
        var handlerMethod = expansion?.Handler.Method;
        var handlerFlow = handlerMethod is null
            ? null
            : behavior.MethodFlows.SingleOrDefault(flow => flow.Method == handlerMethod.Value);
        var expansionLoops = expansion is null
            ? "<none>"
            : string.Join(" | ", expansion.Loops.Select(loop =>
                $"key={loop.Key}; header={loop.Header.Value}; body=[{string.Join(",", loop.Body.Select(node => node.Value))}]; " +
                $"exits=[{string.Join(",", loop.Exits.Select(node => node.Value))}]; backEdge={loop.BackEdge.Value}; " +
                $"label={loop.Label}; memberSteps=[{string.Join(",", loop.MemberSteps.Select(step => step.Label))}]"));
        var handlerLoops = handlerFlow is null
            ? "<none>"
            : string.Join(" | ", handlerFlow.Nodes.OfType<LoopNode>().Select(loop =>
                $"node={loop.Id.Value}; header={loop.Header?.Value ?? "<none>"}; " +
                $"body=[{string.Join(",", loop.Body.Select(node => node.Value))}]; " +
                $"exits=[{string.Join(",", loop.Exits.Select(node => node.Value))}]"));
        var loopBackEdges = handlerFlow is null
            ? "<none>"
            : string.Join(" | ", handlerFlow.Edges
                .Where(edge => edge.Kind == FlowEdgeKind.LoopBack)
                .Select(edge => $"id={edge.Id.Value}; source={edge.Source.Value}; target={edge.Target.Value}"));
        var invocations = handlerFlow is null
            ? "<none>"
            : string.Join(" | ", handlerFlow.Nodes
                .OfType<InvocationFlowNode>()
                .OrderBy(node => node.Id.Value, StringComparer.Ordinal)
                .Select((node, ordinal) =>
                {
                    var callSiteId = StableIdentity.CreateCallSiteId(
                        new CallSiteIdentityDescriptor(handlerFlow.Method, node.Operation, ordinal));
                    var targetLabel = node.TargetAssemblyName is null
                        ? "<none>"
                        : $"{node.TargetAssemblyName}.{node.TargetContainingTypeName}.{node.TargetMethodName}";
                    return $"flowNodeId={node.Id.Value}; operation={node.Operation.Value}; targetTypedLabel={targetLabel}; " +
                           $"blockOrdinal={node.BlockOrdinal}; evaluationOrdinal={node.EvaluationOrdinal}; " +
                           $"canonicalCallSiteId={callSiteId.Value}";
                }));
        var callSites = handlerMethod is null
            ? "<none>"
            : string.Join(" | ", behavior.CallGraph.CallSites
                .Where(site => site.ContainingMethod == handlerMethod.Value)
                .OrderBy(site => site.Id.Value, StringComparer.Ordinal)
                .Select(site =>
                    $"id={site.Id.Value}; invocation={site.InvocationOperation.Value}; " +
                    $"declaredTarget={site.DeclaredTarget?.Value ?? "<none>"}; " +
                    $"resolution={site.Resolution.Kind}; candidates=[{string.Join(",", site.Resolution.Candidates.Select(candidate => candidate.Value))}]"));
        return $"dispatch expansion evidence; diagnostics={diagnostics}; admitted steps={steps}; " +
            $"expansion loops={expansionLoops}; handler loops={handlerLoops}; loop-back edges={loopBackEdges}; " +
            $"handler invocations={invocations}; handler call sites={callSites}";
    }

    private static void AssertSequence(string text, params string[] labels)
    {
        var position = -1;
        foreach (var label in labels)
        {
            position = text.IndexOf(label, position + 1, StringComparison.Ordinal);
            Assert.True(position >= 0, $"Expected '{label}' after the prior sequence label.");
        }
    }

    private static string RouteProjectionEvidence(
        ProfileAnalysisExtraction artifacts,
        FrameworkAnalysisResult framework)
    {
        var lines = new List<string> { "MinimalApiRouteFacts:" };
        foreach (var fact in framework.Facts.OfType<MinimalApiRouteFact>())
        {
            lines.Add($"  verb={fact.HttpMethod}; route={fact.CanonicalRoute}; root={fact.HandlerRoot}");
        }

        lines.Add("Exact MapPost OperationDescriptors:");
        foreach (var operation in artifacts.Operations
                     .Where(item => item.TargetIdentity?.MethodMetadataName == "MapPost"))
        {
            lines.Add($"  id={operation.Id}; method={operation.Method}; kind={operation.Kind}; " +
                      $"document={operation.Document}; sourceStart={operation.SourceStart}; sourceLength={operation.SourceLength}");
            lines.Add($"    target={FormatTarget(operation.TargetIdentity)}");
            lines.Add($"    constants={FormatConstants(operation.ConstantArguments)}");
            lines.Add($"    routeGroup={FormatRouteGroup(operation.RouteGroup)}");
            lines.Add($"    callback={FormatCallback(operation.CallbackTarget, artifacts.ProgramIndex)}");
        }

        lines.Add("Versioned route OperationDescriptors:");
        foreach (var operation in artifacts.Operations
                     .Where(item => item.TargetIdentity is not null
                         && item.TargetIdentity.MethodMetadataName is "MapGroup" or "HasApiVersion" or "NewVersionedApi" or "MapOrdersApiV1")
                     .OrderBy(item => item.TargetIdentity!.MethodMetadataName, StringComparer.Ordinal)
                     .ThenBy(item => item.SourceStart))
        {
            lines.Add($"  id={operation.Id}; method={operation.Method}; kind={operation.Kind}; " +
                      $"document={operation.Document}; sourceStart={operation.SourceStart}; sourceLength={operation.SourceLength}");
            lines.Add($"    target={FormatTarget(operation.TargetIdentity)}");
            lines.Add($"    constants={FormatConstants(operation.ConstantArguments)}");
            lines.Add($"    routeGroup={FormatRouteGroup(operation.RouteGroup)}");
            lines.Add($"    owner={FormatProgramIndexMethod(artifacts.ProgramIndex, operation.Method)}");
            lines.Add($"    callback={FormatCallback(operation.CallbackTarget, artifacts.ProgramIndex)}");
        }

        lines.Add("Framework diagnostics:");
        foreach (var diagnostic in framework.Diagnostics)
        {
            lines.Add($"  {diagnostic.Code} [{diagnostic.Severity}/{diagnostic.Stage}] " +
                      $"{diagnostic.Summary}; cause={diagnostic.TechnicalCause}; " +
                      $"location={diagnostic.Location.Description}; detail={diagnostic.InternalDetail}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatProgramIndexMethod(ProgramIndexSnapshot index, MethodId method)
    {
        var programMethod = index.Methods.FirstOrDefault(item => item.Id == method);
        if (programMethod is null)
        {
            return "<none>";
        }

        var containingType = index.Types.FirstOrDefault(item => item.Id == programMethod.ContainingType);
        return $"id={programMethod.Id}; display={programMethod.DisplaySignature}; " +
               $"containingType={containingType?.MetadataName ?? "<none>"}";
    }

    private static string FormatTarget(FrameworkMethodIdentity? target)
        => target is null
            ? "<none>"
            : $"{target.AssemblyIdentity}, version={target.AssemblyVersion}, " +
              $"type={target.ContainingMetadataType}, method={target.MethodMetadataName}, " +
              $"arity={target.GenericArity}, return={target.ReturnType}, " +
              $"parameters=[{string.Join(", ", target.Parameters.Select(parameter => parameter.ToString()))}]";

    private static string FormatConstants(IEnumerable<CompilerProvenArgument> constants)
        => string.Join(", ", constants.Select(item =>
            $"ordinal={item.Ordinal}, type={item.FullyQualifiedType}, value={item.Value}"));

    private static string FormatRouteGroup(FrameworkRouteGroupDescriptor? routeGroup)
        => routeGroup is null
            ? "<none>"
            : string.Join(" | ", routeGroup.Steps.Select(step =>
                $"prefix={step.Prefix}, target={FormatTarget(step.TargetIdentity)}"));

    private static string FormatCallback(CallbackTargetDescriptor? callback, ProgramIndexSnapshot index)
        => callback is null
            ? "<none>"
            : $"kind={callback.Kind}, method={callback.TargetMethod}, body={callback.TargetBodyOperation}, " +
              $"programIndex={(callback.TargetMethod is { } target
                  ? FormatProgramIndexMethod(index, target)
                  : "<none>")}";
}
