using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

/// <summary>
/// I22 guarded nested callee topology acceptance: the nested-local-guards scenario
/// (Root → Child → Grandchild → Leaf, with one exact local guard on Child and Grandchild) is
/// driven end to end through the production graph builder, DocumentationPlanner, and Mermaid
/// renderer. The emitted diagram must contain the exact nested fragment structure — a guarded
/// fragment nested inside an arm of the root guard fragment — with every message rendered exactly
/// once, well-formed participants, and no validator findings. Wording-layer planner behavior is
/// covered by FragmentPlannerTests; this test pins only the rendered acceptance shape.
/// </summary>
public sealed class GuardedCalleeMermaidRenderingTests
{
    private static readonly CompilationProfile Profile =
        CompilationProfile.Create("tests/fixtures/Fixture/Fixture.csproj", "Release", "net10.0");

    [Fact]
    public void NestedLocalGuardsRenderOneNestedFragmentInsideAnArmWithExactMessageCoverage()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(CreateNestedLocalGuardsRequest()).Graphs);
        var plan = DocumentationPlanner.Plan(graph).Diagram;

        // No withheld boundaries and no depth fallback: the guarded chain plans exactly.
        Assert.Empty(plan.Diagnostics);

        string mermaid = MermaidRenderer.Render(plan);

        // The rendered shape must pass the existing structural validator unchanged.
        Assert.Empty(MermaidValidator.Validate(mermaid));

        var lines = mermaid.Split('\n');

        // Participant lines are well-formed canonical aliases: four-space indent, safe alias,
        // "as" display form.
        var participantLines = lines.Where(line => line.TrimStart().StartsWith("participant ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(plan.Participants.Length, participantLines.Length);
        Assert.All(participantLines, line => Assert.Matches("^ {4}participant [A-Za-z0-9_]+ as .+$", line));

        // Every message line appears exactly once: no duplicated or unconditional re-emission of a
        // locally guarded call.
        var messageLines = lines.Where(line => line.Contains("->>", StringComparison.Ordinal)).ToArray();
        Assert.Equal(plan.Messages.Length, messageLines.Length);
        Assert.Equal(messageLines.Length, messageLines.Distinct().Count());
        Assert.Equal(3, plan.Messages.Length);

        // Nested fragment structure: exactly one root fragment opener at depth one and exactly one
        // guarded fragment opener inside an arm of that root fragment (renderer indentation
        // contract: depth d openers sit at 4 + 2*(d-1) spaces).
        Assert.Equal(1, lines.Count(line => Regex.IsMatch(line, "^ {4}(alt|opt|loop) ")));
        Assert.Equal(1, lines.Count(line => Regex.IsMatch(line, "^ {6}(alt|opt|loop) ")));

        // Chronology: the root fragment opens before its nested fragment, and both precede the
        // final message line so the guarded chain renders in expansion order.
        int rootOpen = Array.FindIndex(lines, line => Regex.IsMatch(line, "^ {4}(alt|opt|loop) "));
        int nestedOpen = Array.FindIndex(lines, line => Regex.IsMatch(line, "^ {6}(alt|opt|loop) "));
        int lastMessage = Array.FindLastIndex(lines, line => line.Contains("->>", StringComparison.Ordinal));
        Assert.True(rootOpen >= 0 && rootOpen < nestedOpen && nestedOpen < lastMessage,
            "The root fragment must open before its nested fragment and both must precede the final message.");
    }

    /// <summary>
    /// Builds the nested-local-guards request: Root calls Child, Child guards and calls Grandchild,
    /// Grandchild guards and calls Leaf. Each single-call guard has an empty false arm represented
    /// by one exact sink throw boundary, and every local guard carries exact Owner predicate
    /// wording so the planner support gates behave uniformly.
    /// </summary>
    private static ScenarioAnalysisRequest CreateNestedLocalGuardsRequest()
    {
        var evidence = SourceEvidence("ct6-fixture");
        var methods = new List<TestMethod>
        {
            TestMethod.Create("Root"), TestMethod.Create("Child"), TestMethod.Create("Grandchild"), TestMethod.Create("Leaf"),
        };
        var index = CreateIndex(methods);
        var flows = methods.Select(method => CreateFlow(method, evidence)).ToImmutableArray();
        var sites = flows.SelectMany(flow => flow.Nodes.OfType<InvocationFlowNode>().Select(invocation =>
            new CallSite(
                new($"call-site:v1:{invocation.Method.Value}:{invocation.Operation.Value}"),
                invocation.Method,
                invocation.Operation,
                CallKind.Instance,
                invocation.Target!.Value,
                new CallTargetResolution(
                    CallResolutionKind.DirectExact,
                    [invocation.Target!.Value],
                    "source",
                    IsComplete: true,
                    [],
                    ImmutableArray.Create(evidence),
                    CertaintyLevel.Exact),
                [SourceEvidence("call-site")],
                CertaintyLevel.Exact))).ToImmutableArray();
        var behavior = new BehaviorSnapshot(
            1,
            "ct6-test",
            Profile,
            index.IndexFingerprint,
            flows,
            new CallGraph(sites.Select(site => new CallGraphEdge(site.ContainingMethod, site.Id, site.Resolution.Candidates[0])).ToImmutableArray(), sites),
            new RtaFoundation([], true),
            [],
            [],
            "ct6-behavior");
        var project = new ProjectId("project:v1:fixture");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            new FrameworkAnalysisResult(true, [], [], [], [], [], []),
            new SemanticFactSet(1, "ct6-test", Profile, index.IndexFingerprint, [], [], [], [], "ct6-semantic"),
            new DependencyInjectionFactSet(1, "ct6-test", Profile, index.IndexFingerprint, [], [], [], "ct6-di"),
            new StructuralResultFactSet(1, "ct6-test", Profile, index.IndexFingerprint, [], [], [], "ct6-structural"),
            new NonGetSemanticFactSet(1, "ct6-test", Profile, index.IndexFingerprint, [], [], [], [], [], [], [], [], "ct6-non-get"),
            ConfiguredRoots: [TestMethod.Create("Root").Id],
            PredicateSemanticFacts: CreateGuardedPredicateFacts(index.IndexFingerprint));
    }

    private static PredicateSemanticFactSet CreateGuardedPredicateFacts(string fingerprint)
    {
        var predicates = new List<PredicateSemanticFact>();
        var mappings = new List<PredicateDecisionMappingFact>();
        foreach (var name in (string[])["Child", "Grandchild"])
        {
            var methodId = new MethodId($"method:v1:Fixture.{name}");
            var condition = new OperationId($"operation:v1:{name}.local-guard");
            var predicateId = new SemanticFactId($"semantic-fact:v1:predicate:{name}");
            predicates.Add(new PredicateSemanticFact(
                predicateId,
                methodId,
                new OperationId($"operation:v1:predicate:source.{name}"),
                new PredicateExpression(
                    PredicateExpressionKind.Comparison,
                    [
                        new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Object", displayName: "reservation"),
                        new PredicateExpression(PredicateExpressionKind.NullConstant, [], "System.Object"),
                    ],
                    "System.Boolean",
                    PredicateComparisonOperatorKind.Equal),
                Profile.Id,
                fingerprint,
                [SourceEvidence("predicate")],
                CertaintyLevel.Exact));
            mappings.Add(new PredicateDecisionMappingFact(
                new SemanticFactId($"semantic-fact:v1:predicate-mapping:{name}"),
                predicateId,
                methodId,
                [condition],
                Profile.Id,
                fingerprint,
                [SourceEvidence("predicate-mapping")],
                CertaintyLevel.Exact));
        }

        return new PredicateSemanticFactSet(
            1,
            "ct6-test",
            Profile,
            fingerprint,
            predicates.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            mappings.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            [],
            "ct6-predicates");
    }

    private static MethodFlowSnapshot CreateFlow(TestMethod method, EvidenceRef evidence)
    {
        var entry = new EntryFlowNode(
            new($"flow-node:v1:{method.Id.Value}:entry"), method.Id, [evidence], CertaintyLevel.Exact);
        var exit = new ExitFlowNode(
            new($"flow-node:v1:{method.Id.Value}:exit"), method.Id, [evidence], CertaintyLevel.Exact);
        var nodes = new List<FlowNode> { entry, exit };
        var edges = new List<FlowEdge>();
        var dependences = new List<ControlDependence>();

        DecisionFlowNode? decision = null;
        if (method.HasLocalGuard)
        {
            var condition = new OperationId($"operation:v1:{method.Name}.local-guard");
            decision = new DecisionFlowNode(
                new($"flow-node:v1:{method.Id.Value}:local-decision"), method.Id, condition, [evidence], CertaintyLevel.Exact);
            nodes.Add(decision);
            edges.Add(new FlowEdge(
                new($"flow-edge:v1:{method.Id.Value}:entry-decision"), method.Id, entry.Id, decision.Id,
                FlowEdgeKind.Normal, null, [evidence], CertaintyLevel.Exact));
        }

        InvocationFlowNode? invocation = null;
        if (method.CallTarget is not null)
        {
            var operation = new OperationId($"{method.Name[..1].ToLowerInvariant()}{method.Name[1..]}.first");
            invocation = new InvocationFlowNode(
                new($"flow-node:v1:{method.Id.Value}:{operation.Value}"), method.Id, operation, method.CallTarget,
                false, false, false, false, false, [evidence], CertaintyLevel.Exact,
                $"Fixture.{method.CallTarget.Value.Value.Split('.').Last()}", operation.Value, false, true, true, 0, 0, "Fixture", false);
            nodes.Add(invocation);
            if (decision is not null)
            {
                dependences.Add(new ControlDependence(decision.Id, invocation.Id, true, [evidence], CertaintyLevel.Exact));
                edges.Add(new FlowEdge(
                    new($"flow-edge:v1:{method.Id.Value}:d-{operation.Value}-t"), method.Id, decision.Id, invocation.Id,
                    FlowEdgeKind.True, null, [evidence], CertaintyLevel.Exact));
                edges.Add(new FlowEdge(
                    new($"flow-edge:v1:{method.Id.Value}:i-{operation.Value}-x"), method.Id, invocation.Id, exit.Id,
                    FlowEdgeKind.Normal, null, [evidence], CertaintyLevel.Exact));
            }
            else
            {
                edges.Add(new FlowEdge(
                    new($"flow-edge:v1:{method.Id.Value}:e-{operation.Value}"), method.Id, entry.Id, invocation.Id,
                    FlowEdgeKind.Normal, null, [evidence], CertaintyLevel.Exact));
            }
        }

        edges.Add(new FlowEdge(
            new($"flow-edge:v1:{method.Id.Value}:last-x"), method.Id, invocation?.Id ?? entry.Id, exit.Id,
            FlowEdgeKind.Normal, null, [evidence], CertaintyLevel.Exact));

        // Single-call guarded methods have an empty false arm; represent it with one exact sink
        // throw boundary after the final continuation edge so both arms classify as represented
        // terminal shapes instead of failing closed on a missing arm edge.
        if (decision is not null)
        {
            var falseThrow = new ThrowFlowNode(
                new($"flow-node:v1:{method.Id.Value}:false-throw"), method.Id, null, false, [evidence], CertaintyLevel.Exact);
            nodes.Add(falseThrow);
            edges.Add(new FlowEdge(
                new($"flow-edge:v1:{method.Id.Value}:d-false-throw"), method.Id, decision.Id, falseThrow.Id,
                FlowEdgeKind.False, null, [evidence], CertaintyLevel.Exact));
        }

        return new MethodFlowSnapshot(
            method.Id,
            "body",
            nodes.ToImmutableArray(),
            edges.ToImmutableArray(),
            [],
            [],
            new LocalValueGraph([], []),
            dependences.ToImmutableArray(),
            null,
            [],
            $"flow:{method.Id.Value}");
    }

    private static ProgramIndexSnapshot CreateIndex(List<TestMethod> methods)
    {
        var projects = ImmutableArray.Create(
            new ProgramProject(
                new ProjectId("project:v1:fixture"), "Fixture", "Fixture.csproj", Profile.Id, Profile.TargetFramework,
                ProjectKind.Library, "project", [], [SourceEvidence("project")]));
        var types = methods.Select(method => new ProgramType(
            new($"symbol:v1:{method.Id.Value}:type"), new ProjectId("project:v1:fixture"),
            new SymbolId("symbol:v1:namespace"), $"Fixture.{method.Name}", ProgramTypeKind.Class, null, [], "type",
            [SourceEvidence("type")]));
        var programMethods = methods.Select(method => new ProgramMethod(
            method.Id, new($"symbol:v1:{method.Id.Value}"), new($"symbol:v1:{method.Id.Value}:type"), method.Name,
            $"Fixture.{method.Name}()", [], "System.Void", "signature", "body",
            [SourceEvidence("method")])).ToImmutableArray();
        return new ProgramIndexSnapshot(
            1, "ct6-test", Profile, projects, [], [], types.ToImmutableArray(), [], programMethods,
            [], [], [], [], [], "ct6-input", "ct6-index");
    }

    private static EvidenceRef SourceEvidence(string artifact)
        => new(
            new EvidenceId($"evidence:v1:{artifact}"),
            EvidenceKind.Source,
            artifact,
            new SourceRange(
                new DocumentId("document:v1:test"),
                new SourcePosition(1, 0),
                new SourcePosition(1, 10)),
            "test-symbol",
            null,
            CertaintyLevel.Exact);

    /// <summary>Nested-local-guards chain shapes keyed by fixture method name.</summary>
    private sealed record TestMethod(MethodId Id, string Name, bool HasLocalGuard, MethodId? CallTarget)
    {
        internal static TestMethod Create(string name) => name switch
        {
            "Root" => new TestMethod(new MethodId($"method:v1:Fixture.{name}"), name, HasLocalGuard: false, CallTarget: new MethodId("method:v1:Fixture.Child")),
            "Child" => new TestMethod(new MethodId($"method:v1:Fixture.{name}"), name, HasLocalGuard: true, CallTarget: new MethodId("method:v1:Fixture.Grandchild")),
            "Grandchild" => new TestMethod(new MethodId($"method:v1:Fixture.{name}"), name, HasLocalGuard: true, CallTarget: new MethodId("method:v1:Fixture.Leaf")),
            _ => new TestMethod(new MethodId($"method:v1:Fixture.{name}"), name, HasLocalGuard: false, CallTarget: null),
        };
    }
}
