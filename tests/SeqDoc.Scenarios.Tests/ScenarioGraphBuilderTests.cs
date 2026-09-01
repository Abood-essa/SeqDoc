using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

public sealed class ScenarioGraphBuilderTests
{
    [Fact]
    public void ConfiguredMethodHasASeparateTypedRootDiscriminator()
    {
        Assert.NotEqual(ScenarioActionKind.ControllerAction, ScenarioActionKind.ConfiguredMethod);
        Assert.NotEqual(ScenarioActionKind.MinimalApiHandler, ScenarioActionKind.ConfiguredMethod);
    }

    [Fact]
    public void ConfiguredMethodWithoutBodyProducesOnlyStructuralNodesAndExplicitDiagnostic()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateConfiguredRootRequest()).Graphs,
            candidate => candidate.RootKind == ScenarioRootKind.ConfiguredMethod);

        Assert.Equal(ScenarioRootKind.ConfiguredMethod, graph.RootKind);
        Assert.Equal([ScenarioNodeKind.EntryPoint, ScenarioNodeKind.Action],
            graph.Nodes.Select(node => node.Kind));
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC002");
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
    }

    [Fact]
    public void ConfiguredBodyAvailableRootReusesGuardedDirectCallsThroughDiagramPlan()
    {
        var request = ScenarioTestFactory.CreateRootDirectCallTryRequest() with
        {
            FrameworkFacts = new FrameworkAnalysisResult(true, [], [], [], [], [], []),
            ProgramIndex = ScenarioTestFactory.CreateRootDirectCallRequest().ProgramIndex with
            {
                Methods = ScenarioTestFactory.CreateRootDirectCallRequest().ProgramIndex.Methods
                    .Select(method => method.Id == ScenarioTestFactory.ActionMethod
                        ? method with { BodyFingerprint = "root-direct" }
                        : method)
                    .ToImmutableArray(),
            },
            ConfiguredRoots = [ScenarioTestFactory.ActionMethod],
        };
        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);
        var plan = DocumentationPlanner.Plan(ScenarioTestFactory.WithExactOwnerWording(graph));

        Assert.Equal(ScenarioRootKind.ConfiguredMethod, graph.RootKind);
        Assert.Equal(["ValidateAsync", "SendAsync"], graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall)
            .OrderBy(node => node.SequenceOrdinal)
            .Select(node => node.Presentation!.TargetMemberName));
        var decision = Assert.Single(graph.Topology.Decisions);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
        var guarded = Assert.Single(graph.Nodes, node => node.Presentation?.TargetMemberName == "SendAsync");
        Assert.Contains(graph.Topology.Memberships, membership => membership.Arm == trueArm.Id && membership.ScenarioNode == guarded.Id);
        var fragment = Assert.Single(plan.Diagram.Sequence.Fragments);
        var guardedRef = Assert.Single(fragment.Arms.Where(arm => arm.Key.EndsWith(":arm:true", StringComparison.Ordinal))
            .SelectMany(arm => arm.MessageRefs));
        Assert.Equal(1, plan.Diagram.Sequence.Fragments.SelectMany(item => item.Arms).SelectMany(arm => arm.MessageRefs)
            .Count(reference => reference == guardedRef));
        Assert.DoesNotContain(guardedRef, plan.Diagram.Sequence.MessageRefs);
        Assert.DoesNotContain(plan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP002");
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
        Assert.DoesNotContain(graph.Nodes, node => node.Method == ScenarioTestFactory.NestedDirectCallTarget);
    }

    [Fact]
    public void ConfiguredRootOrderIsDeterministicAndFrameworkRootsAreNotDuplicated()
    {
        var forward = ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateConfiguredRootRequest(includeFrameworkRoot: true));
        var reversed = ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateConfiguredRootRequest(includeFrameworkRoot: true, reverseConstruction: true));

        Assert.Equal(forward.DebugProjection, reversed.DebugProjection);
        Assert.Equal(forward.Graphs.Select(graph => graph.EntryPoint), reversed.Graphs.Select(graph => graph.EntryPoint));
        Assert.Single(forward.Graphs, graph => graph.RootKind == ScenarioRootKind.HttpEntryPoint);
        Assert.Single(forward.Graphs, graph => graph.RootKind == ScenarioRootKind.ConfiguredMethod);
    }

    /// <summary>CT-3 claim: exact root-local calls are projected without turning the unresolved service composition into a false service claim.</summary>
    [Fact]
    public void RootDirectCallsAreTypedRootOnlyNodesAndRetainSC001()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateRootDirectCallRequest()).Graphs);

        var call = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        Assert.Equal(ScenarioTestFactory.RootDirectCallOperation, call.Operation);
        Assert.Equal(ScenarioTestFactory.RootDirectCallTarget, call.Method);
        Assert.Equal("Payments.TransferGateway", call.Presentation!.TargetContainingTypeName);
        Assert.Equal("SendAsync", call.Presentation.TargetMemberName);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ServiceCall);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");

        // The projection is root-only: a target flow is present in the behavior snapshot but is not traversed.
        Assert.DoesNotContain(graph.Nodes, node => node.Method == ScenarioTestFactory.NestedDirectCallTarget);
    }

    /// <summary>CT-3 claims: compiler ordinals and exact topology membership govern order and branch presentation.</summary>
    [Fact]
    public void RootDirectCallsUseCompilerOrderAndExactDecisionArmMembership()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateRootDirectCallRequest(decisionGuarded: true)).Graphs);

        var calls = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall).ToArray();
        Assert.Equal(["ValidateAsync", "SendAsync"], calls.Select(node => node.Presentation!.TargetMemberName));

        var decision = Assert.Single(graph.Topology.Decisions);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
        var guarded = Assert.Single(calls, node => node.Presentation!.TargetMemberName == "SendAsync");
        var unguarded = Assert.Single(calls, node => node.Presentation!.TargetMemberName == "ValidateAsync");
        Assert.Contains(graph.Topology.Memberships, membership => membership.Arm == trueArm.Id && membership.ScenarioNode == guarded.Id);
        Assert.DoesNotContain(graph.Topology.Memberships, membership => membership.ScenarioNode == unguarded.Id);
    }

    /// <summary>CT-3 negative equivalence partitions: material non-exact or non-source calls are withheld together.</summary>
    [Theory]
    [InlineData("platform")]
    [InlineData("unresolved")]
    [InlineData("ambiguous")]
    [InlineData("dynamic")]
    [InlineData("delegate")]
    [InlineData("constructor")]
    [InlineData("nested")]
    [InlineData("conservative-resolution")]
    public void RootDirectCallMaterialNegativesAreWithheld(string exclusion)
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateRootDirectCallRequest(exclusion: exclusion)).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
    }

    [Fact]
    public void RootDirectCallDuplicateAnchorsProduceOneStableNodeAndProjection()
    {
        var first = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateRootDirectCallRequest(duplicateAnchor: true)).Graphs);
        var second = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateRootDirectCallRequest(duplicateAnchor: true, reverseConstruction: true)).Graphs);

        Assert.Single(first.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        Assert.Equal(first.DebugProjection, second.DebugProjection);
        Assert.Equal(first.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall).Select(node => node.Id),
            second.Nodes.Where(node => node.Kind == ScenarioNodeKind.MethodCall).Select(node => node.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalCompositionWithPersistenceFactsWithholdsInternalAssignments(bool reverseConstruction)
    {
        var request = ScenarioTestFactory.CreateConditionalDiRequest(reverseConstruction: reverseConstruction);
        var methods = new[] { ScenarioTestFactory.ServiceMethod, ScenarioTestFactory.OtherServiceMethod };
        var assignments = methods.Select((method, index) => new StateAssignmentSemanticFact(
            new SemanticFactId($"state-assignment:v1:composition:{index}"), method,
            new OperationId($"operation:v1:composition:assignment:{index}"),
            "GetMeaning.Models.Gadget.Status", "GetMeaning.Models.GadgetStatus",
            StateAssignmentValueKind.EnumConstant, "Cancelled",
            [ScenarioTestFactory.SourceEvidence($"composition-assignment:{index}")], CertaintyLevel.Exact, 1)).ToImmutableArray();
        var mutations = methods.SelectMany((method, index) => new[]
        {
            CreateCompositionMutation(method, index, EntityFrameworkMutationKind.Add, "GetMeaning.Data.GadgetDbContext", "GetMeaning.Models.Gadget", 2),
            CreateCompositionMutation(method, index, EntityFrameworkMutationKind.SaveChangesAsync, "GetMeaning.Data.GadgetDbContext", string.Empty, 3),
        }).ToImmutableArray();
        var adjusted = request with
        {
            NonGetSemanticFacts = request.NonGetSemanticFacts with
            {
                StateAssignments = reverseConstruction ? assignments.Reverse().ToImmutableArray() : assignments,
                EntityFrameworkMutations = reverseConstruction ? mutations.Reverse().ToImmutableArray() : mutations,
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(adjusted).Graphs);
        Assert.NotNull(graph.Composition);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.StateAssignment);
        var reversed = ScenarioGraphBuilder.Build(adjusted with
        {
            NonGetSemanticFacts = adjusted.NonGetSemanticFacts with
            {
                StateAssignments = adjusted.NonGetSemanticFacts.StateAssignments.Reverse().ToImmutableArray(),
                EntityFrameworkMutations = adjusted.NonGetSemanticFacts.EntityFrameworkMutations.Reverse().ToImmutableArray(),
            },
        });
        Assert.Equal(graph.DebugProjection, Assert.Single(reversed.Graphs).DebugProjection);
    }

    private static EntityFrameworkMutationFact CreateCompositionMutation(
        MethodId method,
        int index,
        EntityFrameworkMutationKind kind,
        string context,
        string entity,
        int ordinal)
        => new()
        {
            Id = new BehaviorFactId($"ef-mut:v1:composition:{index}:{kind}"),
            Method = method,
            Operation = new OperationId($"operation:v1:composition:{index}:{kind}"),
            MutationKind = kind,
            SequenceOrdinal = ordinal,
            DbContextType = context,
            EntityType = entity,
            Evidence = [ScenarioTestFactory.SourceEvidence($"composition-mutation:{index}:{kind}")],
            Certainty = CertaintyLevel.Exact,
        };

    /// <summary>
    /// CR-2 write-first contract: one exact source predicate may own two lowered decisions. Only the
    /// first ordinal receives the complete presentation tree; later lowered decisions retain a typed
    /// subordinate marker. Every profile/fingerprint/method/unmapped mismatch fails closed. This is
    /// intentionally written before the Scenario predicate request/decision contract exists.
    /// </summary>
    [Fact]
    public void ExactPredicateMappingHasOneStableOwnerAndRejectsForeignJoins()
    {
        var request = ScenarioTestFactory.CreateGetRequest(decisionGuarded: true);
        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        var owner = Assert.Single(graph.Topology.Decisions,
            decision => decision.Condition == ScenarioTestFactory.PredicateTestIds.OwnerCondition).PredicateWording!;
        var subordinate = Assert.Single(graph.Topology.Decisions,
            decision => decision.Condition == ScenarioTestFactory.PredicateTestIds.SubordinateCondition).PredicateWording!;
        Assert.Equal(ScenarioPredicateWordingRole.Owner, owner.Role);
        Assert.Equal(ScenarioPredicateWordingRole.Subordinate, subordinate.Role);
        Assert.Equal("reservation is null && status == Cancelled", PredicateWordingFormatter.Format(owner.Root));
        Assert.Null(typeof(ScenarioPredicateWording).GetProperty("Text"));
    }

    [Theory]
    [InlineData("foreign-profile")]
    [InlineData("foreign-fingerprint")]
    [InlineData("foreign-method")]
    [InlineData("unmapped-lowered")]
    public void PredicateJoinMismatchesFailClosedWithoutPredicateWording(string mismatch)
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateGetRequest(decisionGuarded: true, predicateJoinMismatch: mismatch)).Graphs);

        Assert.All(graph.Topology.Decisions, decision => Assert.Null(decision.PredicateWording));
    }

    [Fact]
    public void NormalAggregateCompositionUsesTypedConfigurationLabel()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest()).Graphs);
        var plan = DocumentationPlanner.Plan(graph);

        Assert.Contains(plan.Diagram.Sequence.Fragments, fragment => fragment.Label == "Use memory storage");
        Assert.DoesNotContain(plan.Diagram.Sequence.Fragments, fragment => fragment.Label == "Condition");
    }

    [Fact]
    public void GetScenarioJoinsEntryActionServiceQueryResultAndBothOutcomes()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest());
        var graph = Assert.Single(set.Graphs);

        Assert.Equal(ScenarioTestFactory.GetEntryPoint, graph.EntryPoint);
        Assert.Equal(ScenarioTestFactory.ActionMethod, graph.RootMethod);
        Assert.Equal(ScenarioRootKind.HttpEntryPoint, graph.RootKind);
        Assert.Equal("GET api/Gadgets/{id}", graph.OperationKey);
        Assert.Empty(graph.Diagnostics);

        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntryPoint);
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
        var service = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ServiceCall);
        Assert.Equal(ScenarioTestFactory.ServiceMethod, service.Method);
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.Equal(2, graph.Nodes.Count(node => node.Kind == ScenarioNodeKind.Result));
        var outcomes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Contains(outcomes, node => node.Detail.Contains("200", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("404", StringComparison.Ordinal));

        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Entry);
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Call);
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Query);
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.ResultSuccess);
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.ResultFailure);
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeSuccess);
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeFailure);

        // Every node and edge carries non-empty evidence and exact certainty.
        foreach (var node in graph.Nodes)
        {
            Assert.NotEmpty(node.Evidence);
            Assert.True(node.Certainty >= node.Evidence.Max(item => item.Certainty));
        }

        foreach (var edge in graph.Edges)
        {
            Assert.NotEmpty(edge.Evidence);
            Assert.True(edge.Certainty >= edge.Evidence.Max(item => item.Certainty));
        }
    }

    [Fact]
    public void NormalizedEntryCarriesTypedActionKind()
    {
        var controller = Assert.Single(ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest()).Graphs);
        Assert.Equal(
            ScenarioActionKind.ControllerAction,
            Assert.Single(controller.Nodes, node => node.Kind == ScenarioNodeKind.Action).Presentation?.ActionKind);

        var minimal = Assert.Single(ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateMinimalApiRequest(new MinimalApiRouteFact
        {
            Id = new("behavior-fact:v1:typed-minimal-route"),
            Evidence = [ScenarioTestFactory.SourceEvidence("typed-minimal")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new("entry-point:v1:typed-minimal"),
            HandlerRoot = new("method:v1:Program.TypedHandler"),
            HandlerKind = MinimalApiHandlerKind.NamedMethod,
            HttpMethod = HttpMethodKind.Post,
            CanonicalRoute = "/typed",
            OperationKey = "POST /typed",
        })).Graphs);
        Assert.Equal(
            ScenarioActionKind.MinimalApiHandler,
            Assert.Single(minimal.Nodes, node => node.Kind == ScenarioNodeKind.Action).Presentation?.ActionKind);
    }

    [Fact]
    public void HttpActionPresentationCarriesControllerTypeAndExactMethodFromProgramIndex()
    {
        var graph = Assert.Single(ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest()).Graphs);
        var action = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);

        Assert.Equal(ScenarioActionKind.ControllerAction, action.Presentation?.ActionKind);
        Assert.Equal("GetMeaning.Controllers.GadgetsController", action.Presentation?.ControllerTypeName);
        Assert.Equal("GetById", action.Presentation?.ActionMethodName);
        Assert.Equal(
            graph.Nodes.Single(node => node.Kind == ScenarioNodeKind.EntryPoint).Evidence.Select(evidence => evidence.Id.Value),
            action.Evidence.Select(evidence => evidence.Id.Value));
    }

    [Fact]
    public void AmbiguousDiTargetFailsClosedWithDiagnosticAndNoServiceNode()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest(ambiguousDiTargets: true));
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ServiceCall);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Call);
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC001");
        Assert.Contains("multiple-di-targets", diagnostic.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleMatchingCallSitesFailClosedWithSC001()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest(multipleCallSites: true));
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ServiceCall);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Call);
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC001");
        Assert.Contains("multiple-matching-call-sites", diagnostic.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteResolutionFailsClosedWithSC001()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest(incompleteResolution: true));
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ServiceCall);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Call);
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC001");
        Assert.Contains("incomplete-resolution", diagnostic.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateSuccessFactoriesFailClosedWithSC007AndNoResultOutcomeClaims()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest(duplicateSuccessFactories: true));
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Result);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Outcome);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind is ScenarioEdgeKind.ResultSuccess
            or ScenarioEdgeKind.ResultFailure
            or ScenarioEdgeKind.OutcomeSuccess
            or ScenarioEdgeKind.OutcomeFailure);
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC007");
        Assert.Contains("success=2", diagnostic.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedFactoryFailsClosedWithSC007AndNoResultOutcomeClaims()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest(unrelatedFactory: true));
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Result);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.Outcome);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind is ScenarioEdgeKind.ResultSuccess
            or ScenarioEdgeKind.ResultFailure
            or ScenarioEdgeKind.OutcomeSuccess
            or ScenarioEdgeKind.OutcomeFailure);
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC007");
        Assert.Contains("success=0", diagnostic.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusSwitchFlowJoinsArmsToDistinctOutcomesAndOrdersMutations()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest(statusSwitchFlow: true));
        var graph = Assert.Single(set.Graphs);

        var outcomes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 404", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 409", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Equal(3, outcomes.Select(node => node.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.ResultStatus);
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeFailure && edge.Detail.Contains("NotFound", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeFailure && edge.Detail.Contains("Conflict", StringComparison.Ordinal));
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind is ScenarioEdgeKind.ResultSuccess or ScenarioEdgeKind.ResultFailure);

        var mutations = graph.Nodes
            .Where(node => node.Kind == ScenarioNodeKind.EntityMutation)
            .Select(node => node.Detail)
            .ToArray();
        Assert.Equal(2, mutations.Length);
        Assert.Contains(mutations, detail => detail == "removes Gadget records");
        Assert.Contains(mutations, detail => detail == "saves changes to GadgetDbContext");
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Mutation);
        Assert.Contains(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Save);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code is "SC003" or "SC006" or "SC007");
    }

    [Fact]
    public void GraphProjectionIsDeterministicCanonicalAndPathFree()
    {
        var first = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest());
        var second = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest());
        var graph = Assert.Single(first.Graphs);

        Assert.Equal(
            CollectProjection(first),
            CollectProjection(second));
        Assert.Contains("\n", graph.DebugProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", graph.DebugProjection, StringComparison.Ordinal);
        Assert.DoesNotContain(FindRepositoryRoot(), first.DebugProjection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// F4: a CreatedAtAction arm whose action name matches only a Get entry point in a competing
    /// controller must never link to that unrelated route. The link resolves only when the GET entry
    /// point belongs to the same controller and method identity; otherwise it fails closed with SC010.
    /// </summary>
    [Fact]
    public void CreatedLinkCompetingControllerActionNameCannotMislinkForeignRoute()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCreatedLinkCompetitionRequest());
        var post = Assert.Single(set.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);

        var created = Assert.Single(
            post.Nodes,
            node => node.Kind == ScenarioNodeKind.Outcome
                && node.Detail.Contains("HTTP 201", StringComparison.Ordinal));
        Assert.DoesNotContain("links to GET api/Other", created.Detail, StringComparison.Ordinal);
        Assert.Contains(post.Diagnostics, diagnostic => diagnostic.Code == "SC010");
    }

    /// <summary>
    /// F5: one authoritative source-order sequence must drive wording and Mermaid even when an Add
    /// mutation is interleaved before a CountAsync query and the save. Grouping semantic kinds before
    /// sequence ordinals would reorder Add after the query.
    /// </summary>
    [Fact]
    public void SourceOrderInterleavedAddCountSaveIsPreservedInWordingAndMermaid()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateInterleavedSourceOrderRequest());
        var graph = Assert.Single(set.Graphs);
        var plan = DocumentationPlanner.Plan(graph);

        string[] phrases = plan.Wording.Phrases.Select(phrase => phrase.Text).ToArray();
        int addIndex = Array.FindIndex(phrases, text => text.Contains("adds Gadget", StringComparison.Ordinal));
        int queryIndex = Array.FindIndex(phrases, text => text.Contains("counts Gadgets", StringComparison.Ordinal));
        int saveIndex = Array.FindIndex(phrases, text => text.Contains("calls SaveChanges", StringComparison.Ordinal));
        Assert.True(addIndex >= 0 && queryIndex >= 0 && saveIndex >= 0, "Wording lacks the interleaved Add/CountAsync/save phrases.");
        Assert.True(addIndex < queryIndex, "Wording must render Add before the interleaved CountAsync query.");
        Assert.True(queryIndex < saveIndex, "Wording must render the CountAsync query before the save.");

        string[] labels = plan.Diagram.Messages.Select(message => message.Label).ToArray();
        int addMessage = Array.FindIndex(labels, label => label.Contains("Add Gadget", StringComparison.Ordinal));
        int queryMessage = Array.FindIndex(labels, label => label.Contains("Count Gadgets", StringComparison.Ordinal));
        int saveMessage = Array.FindIndex(labels, label => label.Contains("calls SaveChanges", StringComparison.Ordinal));
        Assert.True(addMessage >= 0 && queryMessage >= 0 && saveMessage >= 0, "Mermaid lacks the interleaved Add/query/save messages.");
        Assert.True(addMessage < queryMessage, "Mermaid must render Add before the interleaved CountAsync query.");
        Assert.True(queryMessage < saveMessage, "Mermaid must render the CountAsync query before the save.");
    }

    /// <summary>
    /// F8: two status arms sharing one helper kind (StatusCode) join their exact outcome operations
    /// instead of failing closed on helper-kind ambiguity. The helper kind is a consistency check, not
    /// the join key.
    /// </summary>
    [Fact]
    public void StatusSwitchRepeatedHelperKindJoinsOutcomesByExactOperationIdentity()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateRepeatedHelperStatusRequest());
        var graph = Assert.Single(set.Graphs);

        var outcomes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 500", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 503", StringComparison.Ordinal));
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC004");
    }

    /// <summary>
    /// SF3: exact StatusCode polarity comes from the compiler-proven status code, never the helper
    /// kind. StatusCode(200) joins the success path, StatusCode(500) joins the failure path, and an
    /// unsupported 3xx polarity fails closed with SC004 and no outcome node.
    /// </summary>
    [Fact]
    public void StatusCodePolarityExact200IsSuccessAnd4xx5xxFailureAndUnsupportedFailsClosed()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateStatusCodePolarityRequest());
        var graph = Assert.Single(set.Graphs);

        var outcomes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        var successEdges = graph.Edges
            .Where(edge => edge.Kind == ScenarioEdgeKind.OutcomeSuccess)
            .ToArray();
        var failureEdges = graph.Edges
            .Where(edge => edge.Kind == ScenarioEdgeKind.OutcomeFailure)
            .ToArray();

        var http200 = Assert.Single(outcomes, node => node.Detail.Contains("HTTP 200", StringComparison.Ordinal));
        var http500 = Assert.Single(outcomes, node => node.Detail.Contains("HTTP 500", StringComparison.Ordinal));

        Assert.Contains(successEdges, edge => edge.Target == http200.Id);
        Assert.DoesNotContain(failureEdges, edge => edge.Target == http200.Id);
        Assert.Contains(failureEdges, edge => edge.Target == http500.Id);
        Assert.DoesNotContain(successEdges, edge => edge.Target == http500.Id);

        // The unsupported 3xx polarity must fail closed: no outcome node and an explicit diagnostic.
        Assert.DoesNotContain(outcomes, node => node.Detail.Contains("HTTP 399", StringComparison.Ordinal));
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC004");
    }

    /// <summary>
    /// Uncovered direct-terminal shape: a failure status switch followed by a direct CreatedAtAction
    /// return (the external ReserveTickets shape). The builder must retain the exact HTTP 201 outcome
    /// with its compiler-bound GET link even though the created invocation is not inside any switch
    /// arm, must keep the four failure outcomes intact, and must never invent a synthetic
    /// <c>success</c> status arm; the success outcome is joined exactly once.
    /// </summary>
    [Fact]
    public void DirectTerminalCreatedAtActionAfterFailureSwitchRetainsHttp201AndCreatedLink()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateDirectTerminalCreatedAtActionRequest());
        var post = Assert.Single(set.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);

        var outcomes = post.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        var created = Assert.Single(
            outcomes,
            node => node.Detail.Contains("HTTP 201", StringComparison.Ordinal));
        Assert.Contains("links to GET api/Widgets/{id}", created.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(outcomes, node => node.Detail.Contains("success", StringComparison.OrdinalIgnoreCase));

        // The direct terminal adds exactly one success outcome to the four failure arms; no
        // synthesized status arm may add a sixth outcome and no path may duplicate the 201.
        Assert.Equal(
            5,
            outcomes.Select(node => node.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 404", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 409", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 400", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 500", StringComparison.Ordinal));
        Assert.Contains(post.Edges, edge => edge.Kind == ScenarioEdgeKind.ResultStatus);
        Assert.DoesNotContain(post.Diagnostics, diagnostic => diagnostic.Code == "SC010");
    }

    /// <summary>
    /// The direct-terminal join must never duplicate outcomes that already flow through the accepted
    /// structural-result path (ordinary Get) or a CreatedAtAction switch arm (generic FourFlows
    /// Reserve): each status maps to exactly one outcome node in both flows.
    /// </summary>
    [Fact]
    public void DirectTerminalRepairNeverDuplicatesStructuralResultOrSwitchCreatedLinkOutcomes()
    {
        var getSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest());
        var get = Assert.Single(getSet.Graphs);
        var getOutcomes = get.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Single(getOutcomes, node => node.Detail.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Single(getOutcomes, node => node.Detail.Contains("HTTP 404", StringComparison.Ordinal));

        var switchSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCreatedAtActionSwitchRequest());
        var post = Assert.Single(switchSet.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var switchOutcomes = post.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Single(switchOutcomes, node => node.Detail.Contains("HTTP 201", StringComparison.Ordinal));
        Assert.Single(switchOutcomes, node => node.Detail.Contains("HTTP 404", StringComparison.Ordinal));
        Assert.DoesNotContain(post.Diagnostics, diagnostic => diagnostic.Code == "SC010");
    }

    /// <summary>
    /// accepted contract claim 7/9 + accepted contract requirement 7: one complete same-condition alternative group accounts
    /// for the entire DI binding/call-resolution candidate set, so SC001 is suppressed and the
    /// composition carries one configuration decision plus two independently resolved service arms.
    /// Each arm resolves to the exact implementation method of its own registration with no
    /// cross-arm leakage, and each arm materializes exactly one service node with an
    /// action-&gt;service call edge. This fixture carries no EF query/state/mutation facts, so no
    /// query/mutation node exists and the arm member sets are disjoint by construction. A checked-in
    /// true observation never selects an arm when no profile-known value exists.
    /// </summary>
    [Fact]
    public void ConditionalCompositionResolvesEachArmToExactServiceMethodWithDisjointArmNodes()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest());
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");

        var composition = Assert.IsType<ScenarioServiceComposition>(graph.Composition);
        Assert.Equal("GetMeaning.Services.IGadgetService", composition.ServiceType);
        Assert.Equal(ScenarioTestFactory.ConditionalConditionOperation, composition.Decision.ConditionOperation);
        Assert.Equal(ScenarioTestFactory.ConditionalReadOperation, composition.Decision.ReadOperation);
        Assert.Equal(ScenarioTestFactory.ConditionalStorageKey, composition.Decision.Key);

        Assert.True(composition.TrueArm.IsTrue);
        Assert.Equal(ScenarioTestFactory.ServiceRegistrationId, composition.TrueArm.RegistrationId);
        Assert.Equal("GetMeaning.Services.GadgetService", composition.TrueArm.ImplementationType);
        Assert.Equal(ScenarioTestFactory.ServiceMethod, composition.TrueArm.ResolvedMethod);

        Assert.False(composition.FalseArm.IsTrue);
        Assert.Equal(ScenarioTestFactory.OtherServiceRegistrationId, composition.FalseArm.RegistrationId);
        Assert.Equal("GetMeaning.Services.MemoryGadgetService", composition.FalseArm.ImplementationType);
        Assert.Equal(ScenarioTestFactory.OtherServiceMethod, composition.FalseArm.ResolvedMethod);

        // The resolved method is the exact candidate MethodId from the compiler call resolution,
        // never a reconstructed display string and never the interface method.
        Assert.Contains(
            composition.TrueArm.ResolvedMethod,
            new[] { ScenarioTestFactory.ServiceMethod, ScenarioTestFactory.OtherServiceMethod });
        Assert.Contains(
            composition.FalseArm.ResolvedMethod,
            new[] { ScenarioTestFactory.ServiceMethod, ScenarioTestFactory.OtherServiceMethod });
        Assert.NotEqual(ScenarioTestFactory.InterfaceMethod, composition.TrueArm.ResolvedMethod);
        Assert.NotEqual(ScenarioTestFactory.InterfaceMethod, composition.FalseArm.ResolvedMethod);

        // No cross-arm leakage: the SQL/memory method never appears in the JSON/file arm.
        Assert.NotEqual(composition.TrueArm.ResolvedMethod, composition.FalseArm.ResolvedMethod);

        // Both arms retain non-empty evidence with explicit non-Unknown certainty.
        Assert.NotEmpty(composition.TrueArm.Evidence);
        Assert.NotEqual(CertaintyLevel.Unknown, composition.TrueArm.Certainty);
        Assert.NotEmpty(composition.FalseArm.Evidence);
        Assert.NotEqual(CertaintyLevel.Unknown, composition.FalseArm.Certainty);

        // The decision retains the group and configuration evidence with the weakest certainty: the
        // Conservative checked-in observation governs, so the decision is never promoted to Exact.
        Assert.NotEmpty(composition.Decision.Evidence);
        Assert.NotEqual(CertaintyLevel.Unknown, composition.Decision.Certainty);
        Assert.True(composition.Decision.Certainty >= composition.Decision.Evidence.Max(item => item.Certainty));
        Assert.Equal(CertaintyLevel.Conservative, composition.Decision.Certainty);

        // A checked-in true observation never selects an arm without a profile-known value.
        Assert.Null(composition.ProfileSelection);

        // accepted contract: the complete composition materializes one service node per arm with an
        // action->service call edge; the arms carry disjoint member identities (each arm's own
        // service node) and this fixture has no EF query/state/mutation facts.
        var serviceNodes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.ServiceCall).ToArray();
        Assert.Equal(2, serviceNodes.Length);
        Assert.Contains(serviceNodes, node => node.Method == ScenarioTestFactory.ServiceMethod);
        Assert.Contains(serviceNodes, node => node.Method == ScenarioTestFactory.OtherServiceMethod);
        Assert.Equal(2, graph.Edges.Count(edge => edge.Kind == ScenarioEdgeKind.Call));
        Assert.DoesNotContain(
            graph.Nodes,
            node => node.Kind is ScenarioNodeKind.EntityQuery
                or ScenarioNodeKind.EntityMutation
                or ScenarioNodeKind.Outcome);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Query);

        Assert.NotEmpty(composition.TrueArm.MemberNodes);
        Assert.NotEmpty(composition.FalseArm.MemberNodes);
        Assert.DoesNotContain(
            composition.FalseArm.MemberNodes,
            member => composition.TrueArm.MemberNodes.Contains(member));
        Assert.Contains(
            serviceNodes.Single(node => node.Method == ScenarioTestFactory.ServiceMethod).Id,
            composition.TrueArm.MemberNodes);
        Assert.Contains(
            serviceNodes.Single(node => node.Method == ScenarioTestFactory.OtherServiceMethod).Id,
            composition.FalseArm.MemberNodes);
    }

    /// <summary>
    /// accepted contract claim 8: an extra unguarded registration, a missing alternative group, or an incomplete
    /// call resolution never suppresses SC001: the exact existing reason is retained and the
    /// composition stays null with no service node.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, "multiple-di-targets")]
    [InlineData(false, true, false, "multiple-di-targets")]
    [InlineData(false, false, true, "incomplete-resolution")]
    public void IncompleteExtraOrMissingConditionalCandidatesRetainExactSC001(
        bool extraUnguardedRegistration,
        bool missingGroup,
        bool incompleteResolution,
        string expectedReason)
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest(
            extraUnguardedRegistration: extraUnguardedRegistration,
            missingGroup: missingGroup,
            incompleteResolution: incompleteResolution));
        var graph = Assert.Single(set.Graphs);

        Assert.Null(graph.Composition);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ServiceCall);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Call);
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC001");
        Assert.Contains(expectedReason, diagnostic.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// accepted contract claim 12: composition identity, per-arm resolution, and the full graph projection are
    /// deterministic under reversed construction order (registrations, bindings, arms, targets, and
    /// edges) and remain free of absolute checkout paths.
    /// </summary>
    [Fact]
    public void ConditionalCompositionIdentityIsDeterministicUnderReverseConstructionAndPathFree()
    {
        var first = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest());
        var second = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest(reverseConstruction: true));
        var graph = Assert.Single(first.Graphs);
        var reversed = Assert.Single(second.Graphs);

        var composition = Assert.IsType<ScenarioServiceComposition>(graph.Composition);
        var reversedComposition = Assert.IsType<ScenarioServiceComposition>(reversed.Composition);
        Assert.Equal(composition.Id, reversedComposition.Id);
        Assert.Equal(composition.TrueArm.ResolvedMethod, reversedComposition.TrueArm.ResolvedMethod);
        Assert.Equal(composition.FalseArm.ResolvedMethod, reversedComposition.FalseArm.ResolvedMethod);
        Assert.Equal(CollectProjection(first), CollectProjection(second));
        Assert.DoesNotContain(FindRepositoryRoot(), graph.DebugProjection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// accepted contract claim 12: the composition identity derives from profile + conditional top-level method +
    /// condition/read operations + service type + registration identities, never from the entry point
    /// or route. Changing only the entry point/route keeps the same composition ID; changing the
    /// top-level method/condition anchor changes it.
    /// </summary>
    [Fact]
    public void ConditionalCompositionIdentityDependsOnConditionAnchorNotEntryPoint()
    {
        var baseline = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest());
        var changedRoute = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest(differentEntryPoint: true));
        var changedAnchor = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest(differentConditionAnchor: true));

        var baselineComposition = Assert.IsType<ScenarioServiceComposition>(Assert.Single(baseline.Graphs).Composition);
        var changedRouteComposition = Assert.IsType<ScenarioServiceComposition>(Assert.Single(changedRoute.Graphs).Composition);
        var changedAnchorComposition = Assert.IsType<ScenarioServiceComposition>(Assert.Single(changedAnchor.Graphs).Composition);

        // The identity never depends on the entry point or route.
        Assert.Equal(baselineComposition.Id, changedRouteComposition.Id);
        // The identity follows the conditional top-level method/condition anchor.
        Assert.NotEqual(baselineComposition.Id, changedAnchorComposition.Id);

        // The decision anchors actually changed with the condition anchor, proving the identity
        // follows those anchors rather than some unrelated input.
        Assert.Equal(
            baselineComposition.Decision.ConditionOperation,
            changedRouteComposition.Decision.ConditionOperation);
        Assert.NotEqual(
            baselineComposition.Decision.ConditionOperation,
            changedAnchorComposition.Decision.ConditionOperation);
    }

    /// <summary>
    /// accepted contract claim 11: a matching accepted contract profile-known boolean marks one arm selected and the other
    /// excluded only within that analysis profile. Both arms and their resolved methods remain
    /// retained, the selection keeps its analysis-profile provenance, and certainty is never promoted:
    /// the Conservative profile-known fact yields a Conservative selection while the arm facts keep
    /// their own evidence-derived certainty.
    /// </summary>
    [Fact]
    public void ProfileKnownSelectionRetainsBothArmsWithProvenanceAndWeakestCertainty()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest(profileKnownSelection: true));
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
        var composition = Assert.IsType<ScenarioServiceComposition>(graph.Composition);

        // Both arms are retained even though the profile marks the true arm selected.
        Assert.NotNull(composition.TrueArm);
        Assert.NotNull(composition.FalseArm);
        Assert.Equal(ScenarioTestFactory.ServiceMethod, composition.TrueArm.ResolvedMethod);
        Assert.Equal(ScenarioTestFactory.OtherServiceMethod, composition.FalseArm.ResolvedMethod);
        Assert.Equal(CertaintyLevel.Exact, composition.TrueArm.Certainty);
        Assert.Equal(CertaintyLevel.Exact, composition.FalseArm.Certainty);
        Assert.NotEmpty(composition.TrueArm.Evidence);
        Assert.NotEmpty(composition.FalseArm.Evidence);

        // The decision retains the group and configuration evidence with the weakest certainty even
        // when a profile-known selection exists; the Conservative observation governs.
        Assert.NotEmpty(composition.Decision.Evidence);
        Assert.NotEqual(CertaintyLevel.Unknown, composition.Decision.Certainty);
        Assert.True(composition.Decision.Certainty >= composition.Decision.Evidence.Max(item => item.Certainty));
        Assert.Equal(CertaintyLevel.Conservative, composition.Decision.Certainty);

        var selection = Assert.IsType<ScenarioCompositionProfileSelection>(composition.ProfileSelection);
        Assert.True(selection.SelectsTrueArm);
        // The selection retains the profile-known provenance and its exact Conservative evidence and
        // never promotes certainty above the weakest contributor.
        Assert.Equal("analysis-profile", selection.AnalysisProfileSource);
        Assert.NotEmpty(selection.Evidence);
        Assert.All(selection.Evidence, item => Assert.Equal(CertaintyLevel.Conservative, item.Certainty));
        Assert.True(selection.Certainty >= selection.Evidence.Max(item => item.Certainty));
        Assert.Equal(CertaintyLevel.Conservative, selection.Certainty);

        // The profile-known selection still materializes both arm service nodes in the flat graph.
        Assert.Equal(2, graph.Nodes.Count(node => node.Kind == ScenarioNodeKind.ServiceCall));
    }

    /// <summary>
    /// accepted contract review regression (folded into claim 11): companion fact sets from a foreign compilation
    /// profile or a foreign Program Index fingerprint must fail closed at the builder boundary. A
    /// foreign conditional set never contributes a composition (SC001 is retained), and a foreign
    /// configuration set never contributes profile-known selection or decision evidence even when the
    /// group itself is local and complete.
    /// </summary>
    [Fact]
    public void ForeignProfileOrFingerprintCompanionSetsNeverContributeCompositionOrSelection()
    {
        var foreignProfileSet = ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateConditionalDiRequest(foreignConditionalProfile: true));
        var foreignProfileGraph = Assert.Single(foreignProfileSet.Graphs);
        Assert.Null(foreignProfileGraph.Composition);
        Assert.Contains(foreignProfileGraph.Diagnostics, diagnostic => diagnostic.Code == "SC001");

        var foreignFingerprintSet = ScenarioGraphBuilder.Build(
            ScenarioTestFactory.CreateConditionalDiRequest(profileKnownSelection: true, foreignConfigurationFingerprint: true));
        var foreignFingerprintGraph = Assert.Single(foreignFingerprintSet.Graphs);
        var composition = Assert.IsType<ScenarioServiceComposition>(foreignFingerprintGraph.Composition);
        Assert.Null(composition.ProfileSelection);
        // The decision evidence must not carry the foreign configuration read/checked-in artifacts;
        // only the local group evidence may contribute.
        Assert.All(composition.Decision.Evidence, item => Assert.Equal("conditional-group", item.Artifact));
    }

    /// <summary>
    /// accepted contract review regression (folded into claim 11): every composition contract must reject certainty
    /// stronger than the weakest mixed evidence contributor. With the enum ordered Exact, Conservative,
    /// Heuristic, Unknown, a Conservative contributor is weaker, so Exact certainty over mixed
    /// Exact+Conservative evidence is impossible; Conservative certainty over the same evidence is the
    /// accepted weakest-certainty positive.
    /// </summary>
    [Fact]
    public void CompositionContractsRejectCertaintyStrongerThanWeakestMixedEvidence()
    {
        var mixedEvidence = ImmutableArray.Create(
            ScenarioTestFactory.SourceEvidence("exact-evidence"),
            ScenarioTestFactory.ConservativeEvidence("conservative-evidence"));

        Assert.Throws<ArgumentException>(() => new ScenarioConfigurationDecision(
            ScenarioTestFactory.ConditionalConditionOperation,
            ScenarioTestFactory.ConditionalReadOperation,
            ScenarioTestFactory.ConditionalStorageKey,
            mixedEvidence,
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new ScenarioServiceAlternativeArm(
            true,
            ScenarioTestFactory.ServiceRegistrationId,
            "GetMeaning.Services.GadgetService",
            ScenarioTestFactory.ServiceMethod,
            mixedEvidence,
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new ScenarioCompositionProfileSelection(
            true,
            "analysis-profile",
            mixedEvidence,
            CertaintyLevel.Exact));

        var decision = new ScenarioConfigurationDecision(
            ScenarioTestFactory.ConditionalConditionOperation,
            ScenarioTestFactory.ConditionalReadOperation,
            ScenarioTestFactory.ConditionalStorageKey,
            mixedEvidence,
            CertaintyLevel.Conservative);
        Assert.Equal(CertaintyLevel.Conservative, decision.Certainty);
    }

    /// <summary>
    /// accepted contract review regression (folded into claim 12): a service composition must enforce positional arm
    /// polarity — the true arm must carry true polarity and the false arm false polarity. Reversed
    /// positional arms are rejected so downstream rendering or profile selection can never label the
    /// wrong implementation.
    /// </summary>
    [Fact]
    public void ScenarioServiceCompositionRejectsReversedPositionalArms()
    {
        var evidence = ImmutableArray.Create(ScenarioTestFactory.SourceEvidence("arm-evidence"));
        var decision = new ScenarioConfigurationDecision(
            ScenarioTestFactory.ConditionalConditionOperation,
            ScenarioTestFactory.ConditionalReadOperation,
            ScenarioTestFactory.ConditionalStorageKey,
            evidence,
            CertaintyLevel.Exact);
        var trueArm = new ScenarioServiceAlternativeArm(
            true,
            ScenarioTestFactory.ServiceRegistrationId,
            "GetMeaning.Services.GadgetService",
            ScenarioTestFactory.ServiceMethod,
            evidence,
            CertaintyLevel.Exact);
        var falseArm = new ScenarioServiceAlternativeArm(
            false,
            ScenarioTestFactory.OtherServiceRegistrationId,
            "GetMeaning.Services.MemoryGadgetService",
            ScenarioTestFactory.OtherServiceMethod,
            evidence,
            CertaintyLevel.Exact);

        var id = new ScenarioCompositionId("scenario-composition:v1:storage");
        var accepted = new ScenarioServiceComposition(
            id, "GetMeaning.Services.IGadgetService", decision, trueArm, falseArm, null);
        Assert.NotNull(accepted);

        Assert.Throws<ArgumentException>(() => new ScenarioServiceComposition(
            id, "GetMeaning.Services.IGadgetService", decision, falseArm, trueArm, null));
    }

    /// <summary>
    /// accepted contract claim 7: one exact conditional callback boundary produces exactly one typed callback
    /// region carrying ZeroOrOne/Conditional semantics with the exact condition anchor. The region
    /// contains the generated member nodes by exact operation identity, every generated node whose
    /// operation equals a member operation sits inside the region (never presented as unconditional
    /// top-level behavior), and a legacy request without callback facts keeps CallbackRegions
    /// initialized but empty.
    /// </summary>
    [Fact]
    public void ExactConditionalCallbackBoundaryYieldsOneTypedRegionAndLegacyStaysEmpty()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest());
        var graph = Assert.Single(set.Graphs);

        var region = Assert.Single(graph.CallbackRegions);
        Assert.Equal(ScenarioTestFactory.PrimaryCallbackBoundaryId, region.BoundaryId);
        Assert.Equal(CallbackCardinality.ZeroOrOne, region.Cardinality);
        Assert.Equal(CallbackTriggerKind.Conditional, region.Trigger);
        Assert.Equal(ScenarioTestFactory.CallbackConditionOperation, region.TriggerCondition);
        Assert.Equal(CallbackCompletionKind.RejoinsCaller, region.Completion);
        Assert.NotEmpty(region.MemberNodes);
        Assert.NotEmpty(region.Evidence);
        Assert.NotEqual(CertaintyLevel.Unknown, region.Certainty);

        // Every generated node matching a boundary member operation is inside the region, so a
        // callback member is never presented as an unconditional top-level behavior.
        var memberOperations = new[]
        {
            ScenarioTestFactory.ServiceQueryOperation.Value,
            ScenarioTestFactory.SuccessOperation.Value,
        };
        var matching = graph.Nodes
            .Where(node => node.Operation is { } operation
                && memberOperations.Contains(operation.Value, StringComparer.Ordinal))
            .ToArray();
        Assert.NotEmpty(matching);
        foreach (var node in matching)
        {
            Assert.Contains(region.MemberNodes, member => member == node.Id);
        }

        // Legacy request: no callback facts means initialized-but-empty regions, never a default.
        var legacy = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateGetRequest());
        var legacyGraph = Assert.Single(legacy.Graphs);
        Assert.False(legacyGraph.CallbackRegions.IsDefault);
        Assert.Empty(legacyGraph.CallbackRegions);
    }

    /// <summary>
    /// accepted contract claim 8: callback-local completion never terminates the outer scenario. A
    /// RejoinsCaller callback must not add or alter an outer topology terminal as Terminates, an
    /// Unknown completion stays Unknown, and a repeated-or-unknown boundary is never ExactlyOnce.
    /// </summary>
    [Fact]
    public void CallbackCompletionNeverTerminatesOuterTopologyAndRepeatedNeverExactlyOnce()
    {
        var rejoinsSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest());
        var rejoinsGraph = Assert.Single(rejoinsSet.Graphs);
        Assert.Empty(rejoinsGraph.Topology.Terminals);
        Assert.All(rejoinsGraph.Topology.Terminals, terminal => Assert.NotEqual(ScenarioTerminalKind.Terminates, terminal.Kind));
        Assert.Equal(CallbackCompletionKind.RejoinsCaller, Assert.Single(rejoinsGraph.CallbackRegions).Completion);

        var unknownSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest(unknownCompletion: true));
        var unknownGraph = Assert.Single(unknownSet.Graphs);
        Assert.Empty(unknownGraph.Topology.Terminals);
        Assert.All(unknownGraph.Topology.Terminals, terminal => Assert.NotEqual(ScenarioTerminalKind.Terminates, terminal.Kind));
        var unknownRegion = Assert.Single(unknownGraph.CallbackRegions);
        Assert.Equal(CallbackCompletionKind.Unknown, unknownRegion.Completion);
        Assert.NotEqual(CallbackCompletionKind.RejoinsCaller, unknownRegion.Completion);

        var repeatedSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest(repeatedOrUnknown: true));
        var repeatedGraph = Assert.Single(repeatedSet.Graphs);
        Assert.Empty(repeatedGraph.Topology.Terminals);
        Assert.All(repeatedGraph.Topology.Terminals, terminal => Assert.NotEqual(ScenarioTerminalKind.Terminates, terminal.Kind));
        var repeatedRegion = Assert.Single(repeatedGraph.CallbackRegions);
        Assert.Equal(CallbackCardinality.RepeatedOrUnknown, repeatedRegion.Cardinality);
        Assert.NotEqual(CallbackCardinality.ExactlyOnce, repeatedRegion.Cardinality);
        Assert.Equal(CallbackTriggerKind.Unknown, repeatedRegion.Trigger);
        Assert.Null(repeatedRegion.TriggerCondition);
    }

    /// <summary>
    /// accepted contract claim 9: callback fact sets from a foreign compilation profile or a foreign Program
    /// Index fingerprint contribute no Scenario Graph region. Mixed Exact+Conservative evidence
    /// never promotes certainty: both the callback fact and the typed callback region reject an
    /// Exact certainty constructor, and the region additionally rejects empty members, empty
    /// evidence, and invalid trigger/cardinality coupling.
    /// </summary>
    [Fact]
    public void ForeignCallbackSetsContributeNoRegionAndMixedEvidenceExactConstructionThrows()
    {
        var foreignProfileSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest(foreignProfile: true));
        var foreignProfileGraph = Assert.Single(foreignProfileSet.Graphs);
        Assert.Empty(foreignProfileGraph.CallbackRegions);

        var foreignFingerprintSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest(foreignFingerprint: true));
        var foreignFingerprintGraph = Assert.Single(foreignFingerprintSet.Graphs);
        Assert.Empty(foreignFingerprintGraph.CallbackRegions);

        var mixedEvidence = ImmutableArray.Create(
            ScenarioTestFactory.SourceEvidence("callback-exact"),
            ScenarioTestFactory.ConservativeEvidence("callback-conservative"));

        // The callback fact itself already rejects Exact certainty over mixed evidence; the typed
        // region must enforce the same weakest-certainty contract.
        Assert.Throws<ArgumentException>(() => ScenarioTestFactory.CreateCallbackBoundaryFact(
            new CallbackBoundaryId("callback-boundary:v1:mixed"),
            ScenarioTestFactory.CallbackOuterInvocationOperation,
            CallbackCardinality.ExactlyOnce,
            CallbackTriggerKind.Unconditional,
            null,
            CallbackCompletionKind.RejoinsCaller,
            ["operation:v1:factory.Success"],
            mixedEvidence,
            CertaintyLevel.Exact));

        var memberNode = new ScenarioNodeId("scenario-node:v1:member");
        Assert.Throws<ArgumentException>(() => new ScenarioCallbackRegion(
            new ScenarioCallbackRegionId("scenario-callback-region:v1:mixed"),
            ScenarioTestFactory.PrimaryCallbackBoundaryId,
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Conditional,
            ScenarioTestFactory.CallbackConditionOperation,
            CallbackCompletionKind.RejoinsCaller,
            [memberNode],
            mixedEvidence,
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new ScenarioCallbackRegion(
            new ScenarioCallbackRegionId("scenario-callback-region:v1:empty-members"),
            ScenarioTestFactory.PrimaryCallbackBoundaryId,
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Conditional,
            ScenarioTestFactory.CallbackConditionOperation,
            CallbackCompletionKind.RejoinsCaller,
            [],
            [ScenarioTestFactory.SourceEvidence("callback-region")],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new ScenarioCallbackRegion(
            new ScenarioCallbackRegionId("scenario-callback-region:v1:no-evidence"),
            ScenarioTestFactory.PrimaryCallbackBoundaryId,
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Conditional,
            ScenarioTestFactory.CallbackConditionOperation,
            CallbackCompletionKind.RejoinsCaller,
            [memberNode],
            [],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new ScenarioCallbackRegion(
            new ScenarioCallbackRegionId("scenario-callback-region:v1:coupling"),
            ScenarioTestFactory.PrimaryCallbackBoundaryId,
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Unconditional,
            null,
            CallbackCompletionKind.RejoinsCaller,
            [memberNode],
            [ScenarioTestFactory.SourceEvidence("callback-region")],
            CertaintyLevel.Exact));
    }

    /// <summary>
    /// accepted contract claim 10: callback region identity, member order, and graph debug projection are
    /// deterministic across reversed boundary/member construction order and repeated construction,
    /// including a two-boundary set whose boundary array is supplied in reverse. The accepted contract
    /// composition request remains source-compatible after the request gains the final optional
    /// callback fact set parameter: it still builds its composition and adds no regions.
    /// </summary>
    [Fact]
    public void CallbackRegionIdentityIsDeterministicUnderReversedInputAndRepeatedConstruction()
    {
        var normalSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest());
        var reversedSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest(
            reverseBoundaryConstruction: true,
            reverseMemberOrder: true));
        var repeatedSet = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest());
        var normalGraph = Assert.Single(normalSet.Graphs);
        var reversedGraph = Assert.Single(reversedSet.Graphs);
        var repeatedGraph = Assert.Single(repeatedSet.Graphs);

        var normalRegion = Assert.Single(normalGraph.CallbackRegions);
        var reversedRegion = Assert.Single(reversedGraph.CallbackRegions);
        var repeatedRegion = Assert.Single(repeatedGraph.CallbackRegions);

        Assert.Equal(normalRegion.Id, reversedRegion.Id);
        Assert.Equal(normalRegion.Id, repeatedRegion.Id);
        Assert.Equal(normalRegion.MemberNodes.AsEnumerable(), reversedRegion.MemberNodes.AsEnumerable());
        Assert.Equal(normalRegion.MemberNodes.AsEnumerable(), repeatedRegion.MemberNodes.AsEnumerable());
        Assert.Equal(normalGraph.DebugProjection, reversedGraph.DebugProjection);
        Assert.Equal(CollectProjection(normalSet), CollectProjection(reversedSet));
        Assert.Equal(CollectProjection(normalSet), CollectProjection(repeatedSet));
        Assert.DoesNotContain(FindRepositoryRoot(), normalGraph.DebugProjection, StringComparison.OrdinalIgnoreCase);

        // Boundary-array input order is canonical too: a two-boundary set supplied in reverse yields
        // the same region identities and member order as the forward set.
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var primary = ScenarioTestFactory.CreateCallbackBoundaryFact(
            ScenarioTestFactory.PrimaryCallbackBoundaryId,
            ScenarioTestFactory.CallbackOuterInvocationOperation,
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Conditional,
            ScenarioTestFactory.CallbackConditionOperation,
            CallbackCompletionKind.RejoinsCaller,
            [ScenarioTestFactory.ServiceQueryOperation.Value, ScenarioTestFactory.SuccessOperation.Value],
            [ScenarioTestFactory.SourceEvidence("callback-boundary")],
            CertaintyLevel.Exact);
        var secondary = ScenarioTestFactory.CreateCallbackBoundaryFact(
            ScenarioTestFactory.SecondaryCallbackBoundaryId,
            ScenarioTestFactory.CallbackSecondOuterInvocationOperation,
            CallbackCardinality.RepeatedOrUnknown,
            CallbackTriggerKind.Unknown,
            null,
            CallbackCompletionKind.Unknown,
            [ScenarioTestFactory.NotFoundOperation.Value],
            [ScenarioTestFactory.SourceEvidence("callback-boundary-on-error")],
            CertaintyLevel.Exact);
        var forwardSet = new CallbackBoundaryFactSet(
            1,
            "test",
            ScenarioTestFactory.Profile,
            baseRequest.ProgramIndex.IndexFingerprint,
            [primary, secondary],
            [],
            "callback-boundary-fact-set");
        var backwardSet = new CallbackBoundaryFactSet(
            1,
            "test",
            ScenarioTestFactory.Profile,
            baseRequest.ProgramIndex.IndexFingerprint,
            [secondary, primary],
            [],
            "callback-boundary-fact-set");
        var forwardGraphs = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest(callbackBoundaryFacts: forwardSet));
        var backwardGraphs = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateCallbackBoundaryRequest(callbackBoundaryFacts: backwardSet));
        var forwardGraph = Assert.Single(forwardGraphs.Graphs);
        var backwardGraph = Assert.Single(backwardGraphs.Graphs);
        Assert.Equal(
            forwardGraph.CallbackRegions.Select(region => region.Id),
            backwardGraph.CallbackRegions.Select(region => region.Id));
        Assert.Equal(
            forwardGraph.CallbackRegions.Select(region => string.Join(",", region.MemberNodes.Select(node => node.Value))),
            backwardGraph.CallbackRegions.Select(region => string.Join(",", region.MemberNodes.Select(node => node.Value))));

        // The new final optional request parameter defaults to null: the accepted contract composition request
        // still builds its typed composition and contributes no callback region.
        var pa5Set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateConditionalDiRequest());
        var pa5Graph = Assert.Single(pa5Set.Graphs);
        Assert.IsType<ScenarioServiceComposition>(pa5Graph.Composition);
        Assert.Empty(pa5Graph.CallbackRegions);
    }

    /// <summary>
    /// accepted contract claim 5/6/8: a complete conditional composition with a matching exact FusionCache
    /// GetOrSetAsync fact joins the SQL arm's EF query into one typed cache-miss callback region
    /// while both service arms stay disjoint. The true (SQL) arm carries its service and query nodes,
    /// the false (JSON) arm carries only its service node, no node is shared, the query edge is
    /// scoped to the true-arm service node, and the region carries the framework
    /// ZeroOrOne/Conditional/CacheMiss semantics with the exact query member and no operation trigger
    /// condition. A checked-in observation never selects an arm.
    /// </summary>
    [Fact]
    public void FusionCacheCompositionJoinsSqlQueryIntoOneCacheMissRegionWithDisjointArms()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateFusionCacheCompositionRequest());
        var graph = Assert.Single(set.Graphs);

        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code is "SC001" or "SC014");
        var composition = Assert.IsType<ScenarioServiceComposition>(graph.Composition);
        Assert.Null(composition.ProfileSelection);

        // Disjoint arms: the true (SQL) arm carries its service + the EF query; the false (JSON)
        // arm carries only its service.
        Assert.NotEmpty(composition.TrueArm.MemberNodes);
        Assert.NotEmpty(composition.FalseArm.MemberNodes);
        Assert.Empty(composition.TrueArm.MemberNodes.Intersect(composition.FalseArm.MemberNodes));

        var serviceNodes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.ServiceCall).ToArray();
        Assert.Equal(2, serviceNodes.Length);
        var sqlService = Assert.Single(serviceNodes, node => node.Method == ScenarioTestFactory.ServiceMethod);
        var jsonService = Assert.Single(serviceNodes, node => node.Method == ScenarioTestFactory.OtherServiceMethod);

        var queryNodes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.EntityQuery).ToArray();
        var queryNode = Assert.Single(queryNodes);
        Assert.Equal(ScenarioTestFactory.ServiceMethod, queryNode.Method);

        // The query edge joins the true-arm service node, never the false arm, and the member
        // identities keep the query inside the SQL arm only.
        var queryEdge = Assert.Single(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Query);
        Assert.Equal(sqlService.Id, queryEdge.Source);
        Assert.Contains(sqlService.Id, composition.TrueArm.MemberNodes);
        Assert.DoesNotContain(sqlService.Id, composition.FalseArm.MemberNodes);
        Assert.Contains(jsonService.Id, composition.FalseArm.MemberNodes);
        Assert.DoesNotContain(jsonService.Id, composition.TrueArm.MemberNodes);
        Assert.Contains(queryNode.Id, composition.TrueArm.MemberNodes);
        Assert.DoesNotContain(queryNode.Id, composition.FalseArm.MemberNodes);

        // Exactly one CacheMiss region whose member is the exact EF query node; the query is never
        // presented as unconditional SQL work.
        var region = Assert.Single(graph.CallbackRegions);
        Assert.Equal(ScenarioTestFactory.FusionCacheBoundaryId, region.BoundaryId);
        Assert.Equal(CallbackCardinality.ZeroOrOne, region.Cardinality);
        Assert.Equal(CallbackTriggerKind.Conditional, region.Trigger);
        Assert.Null(region.TriggerCondition);
        Assert.Equal(CallbackCompletionKind.Unknown, region.Completion);
        Assert.Equal(FrameworkCallbackConditionKind.CacheMiss, region.FrameworkCondition);
        Assert.Equal(queryNode.Id, Assert.Single(region.MemberNodes));

        // The debug projection exposes the arm member identities and the framework condition.
        Assert.Contains("framework=CacheMiss", graph.DebugProjection, StringComparison.Ordinal);
        Assert.Contains("trueMembers=", graph.DebugProjection, StringComparison.Ordinal);
        Assert.Contains("falseMembers=", graph.DebugProjection, StringComparison.Ordinal);
    }

    /// <summary>
    /// accepted contract claim 9 fallback: an unknown anonymous metadata boundary with no exact-one FusionCache
    /// fact (missing, foreign operation, duplicate facts, a fact anchored to a foreign profile, a
    /// foreign Program Index fingerprint, a different callback boundary, or an SEQFC001 diagnostic
    /// anchored to a foreign outer operation) yields no cache-miss region, keeps both arm service
    /// nodes and the true-arm query membership, and never invents a cache diagnostic or a checked-in
    /// selection. A foreign-diagnostic-operation diagnostic carries canonical detail for another
    /// operation, so the exact-detail matcher must keep the query and emit no SC014.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("foreign")]
    [InlineData("multiple")]
    [InlineData("foreign-profile-fact")]
    [InlineData("foreign-fingerprint-fact")]
    [InlineData("boundary-mismatch-fact")]
    [InlineData("foreign-diagnostic-operation")]
    public void FusionCacheCompositionWithoutExactSingleFactKeepsArmNodesAndNoRegion(string factMode)
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateFusionCacheCompositionRequest(factMode));
        var graph = Assert.Single(set.Graphs);

        Assert.Empty(graph.CallbackRegions);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC014");

        var composition = Assert.IsType<ScenarioServiceComposition>(graph.Composition);
        Assert.Null(composition.ProfileSelection);

        // Both arm service nodes remain and the EF query still belongs only to the SQL arm.
        Assert.Equal(2, graph.Nodes.Count(node => node.Kind == ScenarioNodeKind.ServiceCall));
        Assert.NotEmpty(composition.TrueArm.MemberNodes);
        Assert.NotEmpty(composition.FalseArm.MemberNodes);
        Assert.Empty(composition.TrueArm.MemberNodes.Intersect(composition.FalseArm.MemberNodes));

        var queryNode = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        var sqlService = Assert.Single(
            graph.Nodes,
            node => node.Kind == ScenarioNodeKind.ServiceCall && node.Method == ScenarioTestFactory.ServiceMethod);
        Assert.Contains(sqlService.Id, composition.TrueArm.MemberNodes);
        Assert.Contains(queryNode.Id, composition.TrueArm.MemberNodes);
        Assert.DoesNotContain(queryNode.Id, composition.FalseArm.MemberNodes);
    }

    /// <summary>
    /// accepted contract claim 9 unsupported-shape fallback: the framework model reports the exact SEQFC001
    /// unsupported-shape diagnostic for a recognizably FusionCache operation without an exact fact.
    /// The scenario builder degrades with SC014, withholds the boundary member (the EF query node)
    /// and its edge from the flat graph, and prunes that identity from the SQL arm membership so
    /// unsupported cache work is never presented as unconditional SQL work. The composition itself,
    /// both service-call arm nodes, and their disjoint arm membership are retained; no cache-miss
    /// region exists and a checked-in observation never selects an arm.
    /// </summary>
    [Fact]
    public void FusionCacheUnsupportedShapeWithholdsQueryAndKeepsBothServiceArms()
    {
        var set = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateFusionCacheCompositionRequest("unsupported"));
        var graph = Assert.Single(set.Graphs);

        // The exact unsupported-shape code surfaces as SC014; the graph never invents cache work.
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC014");
        Assert.Contains("FusionCache", diagnostic.Summary, StringComparison.Ordinal);
        Assert.Empty(graph.CallbackRegions);

        // The composition and both service-call nodes are retained.
        var composition = Assert.IsType<ScenarioServiceComposition>(graph.Composition);
        Assert.Null(composition.ProfileSelection);
        var serviceNodes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.ServiceCall).ToArray();
        Assert.Equal(2, serviceNodes.Length);
        var sqlService = Assert.Single(serviceNodes, node => node.Method == ScenarioTestFactory.ServiceMethod);
        var jsonService = Assert.Single(serviceNodes, node => node.Method == ScenarioTestFactory.OtherServiceMethod);

        // The unsupported boundary member (the EF query) is withheld: no query node, no query edge,
        // and each arm carries only its own service node, disjoint from the other.
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == ScenarioEdgeKind.Query);
        Assert.Equal(sqlService.Id, Assert.Single(composition.TrueArm.MemberNodes));
        Assert.Equal(jsonService.Id, Assert.Single(composition.FalseArm.MemberNodes));
        Assert.Empty(composition.TrueArm.MemberNodes.Intersect(composition.FalseArm.MemberNodes));
    }

    /// <summary>
    /// Persistence-request association is consumer filtering: only an exact assignment target joined
    /// to a same-method entity mutation and a later save is projected as a transition. Generic facts
    /// remain available to other consumers, and input construction order cannot affect the graph.
    /// </summary>
    [Theory]
    [InlineData("matching", true)]
    [InlineData("wrong-entity", false)]
    [InlineData("missing-mutation", false)]
    [InlineData("missing-save", false)]
    [InlineData("incompatible-save", false)]
    [InlineData("equal-ordinal", false)]
    public void PersistenceAssignmentJoinRequiresCompatibleEntityMutationAndSave(
        string partition,
        bool expectedStateNode)
    {
        var forward = BuildPersistenceAssignmentRequest(partition, reverseConstruction: false);
        var reversed = BuildPersistenceAssignmentRequest(partition, reverseConstruction: true);

        var forwardGraph = Assert.Single(ScenarioGraphBuilder.Build(forward).Graphs);
        var reversedGraph = Assert.Single(ScenarioGraphBuilder.Build(reversed).Graphs);

        Assert.Equal(expectedStateNode,
            forwardGraph.Nodes.Any(node => node.Kind == ScenarioNodeKind.StateAssignment));
        Assert.Equal(forwardGraph.DebugProjection, reversedGraph.DebugProjection);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PersistenceAssignmentRequiresSameControlArm(bool reverseConstruction, bool oppositeArm)
    {
        var request = ScenarioTestFactory.CreateWorkItemTopologyRequest(reverseConstruction);
        var originalAssignment = Assert.Single(request.NonGetSemanticFacts.StateAssignments);
        var assignment = new StateAssignmentSemanticFact(
            originalAssignment.Id,
            originalAssignment.Method,
            originalAssignment.Operation,
            originalAssignment.TargetMember,
            originalAssignment.TargetType,
            originalAssignment.ValueKind,
            originalAssignment.Value,
            originalAssignment.Evidence,
            originalAssignment.Certainty,
            sequenceOrdinal: 0);
        var save = Assert.Single(request.NonGetSemanticFacts.EntityFrameworkMutations,
            fact => fact.MutationKind == EntityFrameworkMutationKind.SaveChangesAsync);
        var mutation = new EntityFrameworkMutationFact
        {
            Id = new BehaviorFactId("ef-mut:v1:control-arm"),
            Method = ScenarioTestFactory.WorkItemServiceMethod,
            Operation = oppositeArm
                ? ScenarioTestFactory.WorkItemConflictFactoryOperation
                : ScenarioTestFactory.WorkItemStateAssignmentOperation,
            MutationKind = EntityFrameworkMutationKind.Add,
            SequenceOrdinal = 1,
            DbContextType = "AdvancedAnalysis.DecisionTopology.Data.WorkDbContext",
            EntityType = "AdvancedAnalysis.DecisionTopology.Models.WorkItem",
            Evidence = [ScenarioTestFactory.SourceEvidence("control-arm-mutation")],
            Certainty = CertaintyLevel.Exact,
        };
        var adjusted = request with
        {
            NonGetSemanticFacts = request.NonGetSemanticFacts with
            {
                StateAssignments = [assignment],
                EntityFrameworkMutations = reverseConstruction
                    ? [save, mutation]
                    : [mutation, save],
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(adjusted).Graphs);
        var stateNodes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.StateAssignment).ToArray();
        Assert.Equal(oppositeArm ? 0 : 1, stateNodes.Length);
        Assert.DoesNotContain(graph.Edges, edge => oppositeArm && edge.Kind == ScenarioEdgeKind.StateAssignment);
    }

    private static ScenarioAnalysisRequest BuildPersistenceAssignmentRequest(string partition, bool reverseConstruction)
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var evidence = new EvidenceRef(
            new EvidenceId("evidence:v1:persistence-assignment"),
            EvidenceKind.Source,
            "src/Services/GadgetService.cs",
            range: null,
            symbol: "GetMeaning.Models.Gadget.Status",
            detail: "Status = Cancelled",
            CertaintyLevel.Exact);
        var assignment = new StateAssignmentSemanticFact(
            new SemanticFactId("state-assignment:v1:persistence"),
            ScenarioTestFactory.ServiceMethod,
            new OperationId("op:persistence:assignment"),
            "GetMeaning.Models.Gadget.Status",
            "GetMeaning.Models.GadgetStatus",
            StateAssignmentValueKind.EnumConstant,
            "Cancelled",
            [evidence],
            CertaintyLevel.Exact,
            sequenceOrdinal: 1);
        var mutations = new List<EntityFrameworkMutationFact>();
        switch (partition)
        {
            case "matching":
                mutations.Add(CreatePersistenceMutation("add", EntityFrameworkMutationKind.Add, "GetMeaning.Models.Gadget", "GetMeaning.Data.GadgetDbContext", 2));
                mutations.Add(CreatePersistenceMutation("save", EntityFrameworkMutationKind.SaveChangesAsync, string.Empty, "GetMeaning.Data.GadgetDbContext", 3));
                break;
            case "wrong-entity":
                mutations.Add(CreatePersistenceMutation("add", EntityFrameworkMutationKind.Add, "GetMeaning.Models.Other", "GetMeaning.Data.GadgetDbContext", 2));
                mutations.Add(CreatePersistenceMutation("save", EntityFrameworkMutationKind.SaveChangesAsync, string.Empty, "GetMeaning.Data.GadgetDbContext", 3));
                break;
            case "missing-mutation":
                mutations.Add(CreatePersistenceMutation("save", EntityFrameworkMutationKind.SaveChangesAsync, string.Empty, "GetMeaning.Data.GadgetDbContext", 3));
                break;
            case "missing-save":
                mutations.Add(CreatePersistenceMutation("add", EntityFrameworkMutationKind.Add, "GetMeaning.Models.Gadget", "GetMeaning.Data.GadgetDbContext", 2));
                break;
            case "incompatible-save":
                mutations.Add(CreatePersistenceMutation("add", EntityFrameworkMutationKind.Add, "GetMeaning.Models.Gadget", "GetMeaning.Data.GadgetDbContext", 2));
                mutations.Add(CreatePersistenceMutation("save", EntityFrameworkMutationKind.SaveChangesAsync, string.Empty, "GetMeaning.Data.OtherDbContext", 3));
                break;
            case "equal-ordinal":
                mutations.Add(CreatePersistenceMutation("add", EntityFrameworkMutationKind.Add, "GetMeaning.Models.Gadget", "GetMeaning.Data.GadgetDbContext", 1));
                mutations.Add(CreatePersistenceMutation("save", EntityFrameworkMutationKind.SaveChangesAsync, string.Empty, "GetMeaning.Data.GadgetDbContext", 3));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(partition), partition, null);
        }

        if (reverseConstruction)
        {
            mutations.Reverse();
        }

        return baseRequest with
        {
            NonGetSemanticFacts = baseRequest.NonGetSemanticFacts with
            {
                StateAssignments = [assignment],
                EntityFrameworkMutations = mutations.ToImmutableArray(),
            },
        };
    }

    private static EntityFrameworkMutationFact CreatePersistenceMutation(
        string key,
        EntityFrameworkMutationKind kind,
        string entityType,
        string dbContextType,
        int sequenceOrdinal)
        => new()
        {
            Id = new BehaviorFactId($"ef-mut:v1:persistence:{key}"),
            Method = ScenarioTestFactory.ServiceMethod,
            Operation = new OperationId($"op:persistence:{key}"),
            MutationKind = kind,
            SequenceOrdinal = sequenceOrdinal,
            DbContextType = dbContextType,
            EntityType = entityType,
            Evidence = [new EvidenceRef(
                new EvidenceId($"evidence:v1:persistence:{key}"),
                EvidenceKind.Source,
                "src/Services/GadgetService.cs",
                range: null,
                symbol: key,
                detail: null,
                CertaintyLevel.Exact)],
            Certainty = CertaintyLevel.Exact,
        };

    private static string CollectProjection(ScenarioGraphSet set) => string.Join(
        "\n",
        set.Graphs
            .SelectMany(graph => graph.Nodes.Select(node => node.Id.Value)
                .Concat(graph.Edges.Select(edge => edge.Id.Value))
                .Concat(graph.Diagnostics.Select(diagnostic => diagnostic.Id.Value)))
            .Order(StringComparer.Ordinal));

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
