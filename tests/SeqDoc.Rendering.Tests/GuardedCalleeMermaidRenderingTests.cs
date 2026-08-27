using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Configuration;
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

    [Fact]
    public void ProductionGuardedOccurrencesRemainCompleteAndDeterministicThroughDecomposition()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(CreateNestedLocalGuardsRequest()).Graphs);
        var plan = DocumentationPlanner.Plan(graph).Diagram;
        var budget = new DiagramBudget(1024, 4096, 1024, 256, MermaidRenderer.Render(plan).Length - 1);
        var entry = new DocumentSetEntry("guarded-callee-test", PlanTestFactory.CreateWordingDocument(), plan);

        var first = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry], budget,
            new DiagramDecompositionOptions(Enabled: true));
        var second = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry], budget,
            new DiagramDecompositionOptions(Enabled: true));

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.True(second.Succeeded, string.Join("; ", second.Errors));
        Assert.Equal(first.Files.Select(file => (file.RelativePath, file.Content)),
            second.Files.Select(file => (file.RelativePath, file.Content)));
        Assert.Equal(1, first.Diagnostics.Count(item => item.Code == "DP-DIAGRAM-DECOMPOSED"));
        Assert.All(first.Diagnostics, item => Assert.Equal("DP-DIAGRAM-DECOMPOSED", item.Code));

        var mermaid = first.Files.Where(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal))
            .Select(file => Encoding.UTF8.GetString(file.Content)).ToArray();
        var messageLines = mermaid.SelectMany(text => text.Split('\n')
            .Where(line => line.Contains("->>", StringComparison.Ordinal))).ToArray();
        Assert.Equal(plan.Messages.Length, messageLines.Length);
        Assert.Equal(messageLines.Length, messageLines.Distinct().Count());
        Assert.All(mermaid, text => Assert.Empty(MermaidValidator.Validate(text)));

        var files = first.Files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var file in first.Files.Where(file => file.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
        {
            string markdown = Encoding.UTF8.GetString(file.Content);
            foreach (Match match in Regex.Matches(markdown, @"\]\(([^)#]+)", RegexOptions.CultureInvariant))
            {
                Assert.Contains(ResolveMarkdownTarget(file.RelativePath, match.Groups[1].Value), files);
            }
        }
    }

    private static string ResolveMarkdownTarget(string source, string link)
    {
        string directory = source.Contains('/', StringComparison.Ordinal)
            ? source[..(source.LastIndexOf('/') + 1)] : string.Empty;
        var segments = (directory + link).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resolved = new List<string>();
        foreach (string segment in segments)
        {
            if (segment == ".") { continue; }
            if (segment == "..") { resolved.RemoveAt(resolved.Count - 1); }
            else { resolved.Add(segment); }
        }
        return string.Join('/', resolved);
    }

    [Fact]
    public void NullScopeServiceDecisionKeepsLegacyDiagramIdentity()
    {
        var plan = DocumentationPlanner.Plan(CreateNullScopeLegacyGraph()).Diagram;
        var fragment = Assert.Single(plan.Sequence.Fragments, item => item.Key == "decision:operation:v1:legacy-condition");
        var arm = Assert.Single(fragment.Arms, item => !item.IsElse);
        Assert.Equal("decision:operation:v1:legacy-condition", fragment.Key);
        Assert.Equal("decision:operation:v1:legacy-condition:arm:false", arm.Key);
        Assert.Equal("diagram-element:v1:bfd95b808fa3e23dce5c5a1aeb74529f99b1faf535b68b4e7a7ea16530545cc6", fragment.Id.Value);
        Assert.Equal("diagram-element:v1:dfdda9ec96e2b89f8ac9be591689397dc72e848f9f7ffad0f8fb0e6e10dbba0d", arm.Id.Value);
    }

    [Fact]
    public void OccurrenceIdentitySeparatesDelimiterCollidingConditionScopeTuples()
    {
        var plan = DocumentationPlanner.Plan(CreateDelimiterCollisionGraph()).Diagram;
        var fragments = plan.Sequence.Fragments.SelectMany(AllFragments).Where(item =>
            (item.Kind is DiagramFragmentKind.Alt or DiagramFragmentKind.Opt)
            && item.Key.StartsWith("decision:occurrence:v1:", StringComparison.Ordinal)).ToArray();
        var distinctFragments = fragments.GroupBy(fragment => fragment.Key, StringComparer.Ordinal)
            .Select(group => group.First()).ToArray();
        Assert.Equal(2, distinctFragments.Length);
        Assert.Equal(2, distinctFragments.Select(fragment => fragment.Id).Distinct().Count());
        var arms = distinctFragments.SelectMany(fragment => fragment.Arms).Where(arm => !arm.IsElse).ToArray();
        Assert.Equal(2, arms.Length);
        Assert.Equal(2, arms.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, arms.Select(item => item.Id).Distinct().Count());
        var breaks = distinctFragments.SelectMany(fragment => fragment.Arms)
            .SelectMany(arm => arm.Fragments)
            .SelectMany(AllFragments).Where(item => item.Kind == DiagramFragmentKind.Break).ToArray();
        Assert.Equal(2, breaks.Length);
        Assert.Equal(2, breaks.Select(item => item.Id).Distinct().Count());
        var repeated = DocumentationPlanner.Plan(CreateDelimiterCollisionGraph()).Diagram;
        Assert.Equal(plan.DebugProjection, repeated.DebugProjection);
    }

    private static IEnumerable<DiagramFragment> AllFragments(DiagramFragment fragment)
    {
        yield return fragment;
        foreach (var nested in fragment.Fragments.SelectMany(child => AllFragments(child)))
        {
            yield return nested;
        }
        foreach (var nested in fragment.Arms.SelectMany(arm => arm.Fragments).SelectMany(child => AllFragments(child)))
        {
            yield return nested;
        }
    }

    private static ScenarioGraph CreateNullScopeLegacyGraph()
    {
        var evidence = SourceEvidence("legacy-null-scope");
        var entry = new EntryPointId("entry-point:v1:legacy");
        var root = new MethodId("method:v1:Fixture.Root");
        var child = new MethodId("method:v1:Fixture.Child");
        var entryNode = new ScenarioNode(new("scenario-node:v1:entry"), ScenarioNodeKind.EntryPoint, entry.Value, root, null, "entry", [evidence], CertaintyLevel.Exact);
        var actionNode = new ScenarioNode(new("scenario-node:v1:action"), ScenarioNodeKind.Action, "action:root", root, null, "action", [evidence], CertaintyLevel.Exact);
        var callNode = new ScenarioNode(new("scenario-node:v1:call"), ScenarioNodeKind.MethodCall, "call:child", child, new OperationId("operation:v1:root.child"), "child", [evidence], CertaintyLevel.Exact);
        var wording = new ScenarioPredicateWording(new SemanticFactId("semantic-fact:v1:legacy"),
            new PredicateExpression(PredicateExpressionKind.BooleanTruth,
                [new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Boolean", displayName: "enabled")], "System.Boolean"),
            ScenarioPredicateWordingRole.Owner, [evidence], CertaintyLevel.Exact);
        var decision = new ScenarioDecision(new("scenario-decision:v1:legacy"), root, new("flow-node:v1:legacy-decision"),
            new("operation:v1:legacy-condition"), [evidence], CertaintyLevel.Exact, wording);
        var trueArm = new ScenarioArm(new("scenario-arm:v1:legacy:true"), decision.Id, true, [evidence], CertaintyLevel.Exact);
        var falseArm = new ScenarioArm(new("scenario-arm:v1:legacy:false"), decision.Id, false, [evidence], CertaintyLevel.Exact);
        var topology = new ScenarioTopology([decision], [trueArm, falseArm],
            [new ScenarioMembership(new("scenario-membership:v1:legacy"), trueArm.Id, callNode.Id, [evidence], CertaintyLevel.Exact)],
            [new ScenarioArmTerminal(trueArm.Id, ScenarioTerminalKind.Rejoins, [evidence], CertaintyLevel.Exact),
             new ScenarioArmTerminal(falseArm.Id, ScenarioTerminalKind.Terminates, [evidence], CertaintyLevel.Exact)]);
        var edges = ImmutableArray.Create(
            new ScenarioEdge(new("scenario-edge:v1:entry"), entryNode.Id, actionNode.Id, ScenarioEdgeKind.Entry, "entry", [evidence], CertaintyLevel.Exact),
            new ScenarioEdge(new("scenario-edge:v1:call"), actionNode.Id, callNode.Id, ScenarioEdgeKind.Call, "call", [evidence], CertaintyLevel.Exact));
        return new ScenarioGraph(entry, Profile.Id, root, HttpMethodKind.Unknown, "", "Fixture.Root()",
            [entryNode, actionNode, callNode], edges, [], "legacy", topology, rootKind: ScenarioRootKind.ConfiguredMethod);
    }

    private static ScenarioGraph CreateDelimiterCollisionGraph()
    {
        var baseGraph = CreateNullScopeLegacyGraph();
        var evidence = SourceEvidence("delimiter-collision");
        var secondChild = new ScenarioNode(new ScenarioNodeId("scenario-node:v1:collision-call"), ScenarioNodeKind.MethodCall,
            "call:collision", new MethodId("method:v1:Fixture.OtherChild"),
            new OperationId("operation:v1:root.other"), "other", [evidence], CertaintyLevel.Exact);
        var secondDecision = new ScenarioDecision(new ScenarioDecisionId("scenario-decision:v1:collision-second"), baseGraph.RootMethod,
            new FlowNodeId("flow-node:v1:collision-second"), new OperationId("operation:v1:a"), [evidence], CertaintyLevel.Exact,
            baseGraph.Topology.Decisions[0].PredicateWording, "b:occurrence:v1:c");
        var firstDecision = new ScenarioDecision(baseGraph.Topology.Decisions[0].Id, baseGraph.RootMethod,
            baseGraph.Topology.Decisions[0].ControllingFlowNode,
            new OperationId("operation:v1:a:occurrence:v1:b"), [evidence], CertaintyLevel.Exact,
            baseGraph.Topology.Decisions[0].PredicateWording, "c");
        var firstTrue = new ScenarioArm(new ScenarioArmId("scenario-arm:v1:collision-first:true"), firstDecision.Id, true, [evidence], CertaintyLevel.Exact);
        var firstFalse = new ScenarioArm(new ScenarioArmId("scenario-arm:v1:collision-first:false"), firstDecision.Id, false, [evidence], CertaintyLevel.Exact);
        var secondTrue = new ScenarioArm(new ScenarioArmId("scenario-arm:v1:collision-second:true"), secondDecision.Id, true, [evidence], CertaintyLevel.Exact);
        var secondFalse = new ScenarioArm(new ScenarioArmId("scenario-arm:v1:collision-second:false"), secondDecision.Id, false, [evidence], CertaintyLevel.Exact);
        var memberships = ImmutableArray.Create(
            new ScenarioMembership(new ScenarioMembershipId("scenario-membership:v1:collision-first"), firstTrue.Id,
                baseGraph.Nodes[^1].Id, [evidence], CertaintyLevel.Exact),
            new ScenarioMembership(new ScenarioMembershipId("scenario-membership:v1:collision-second"), secondTrue.Id,
                secondChild.Id, [evidence], CertaintyLevel.Exact));
        var topology = new ScenarioTopology(
            [firstDecision, secondDecision],
            [firstTrue, firstFalse, secondTrue, secondFalse],
            memberships,
            [
                new ScenarioArmTerminal(firstTrue.Id, ScenarioTerminalKind.Rejoins, [evidence], CertaintyLevel.Exact),
                new ScenarioArmTerminal(firstFalse.Id, ScenarioTerminalKind.Terminates, [evidence], CertaintyLevel.Exact),
                new ScenarioArmTerminal(secondTrue.Id, ScenarioTerminalKind.Rejoins, [evidence], CertaintyLevel.Exact),
                new ScenarioArmTerminal(secondFalse.Id, ScenarioTerminalKind.Terminates, [evidence], CertaintyLevel.Exact),
            ]);
        var secondEdge = new ScenarioEdge(new ScenarioEdgeId("scenario-edge:v1:collision-call"), baseGraph.Nodes[1].Id,
            secondChild.Id, ScenarioEdgeKind.Call, "other", [evidence], CertaintyLevel.Exact);
        return new ScenarioGraph(baseGraph.EntryPoint, baseGraph.Profile, baseGraph.RootMethod, baseGraph.HttpMethod,
            baseGraph.CanonicalRoute, baseGraph.OperationKey, baseGraph.Nodes.Add(secondChild), baseGraph.Edges.Add(secondEdge),
            baseGraph.Diagnostics, baseGraph.DebugProjection, topology, baseGraph.Composition, baseGraph.CallbackRegions,
            baseGraph.HandlerTopology, baseGraph.DispatchHandlerExpansion, baseGraph.RootKind, baseGraph.DirectCallExpansion);
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
