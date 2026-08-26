using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// Builds Roslyn-neutral scenario-analysis requests from hand-authored facts so the graph-join
/// algorithm is tested as a small pure layer without a compiler session. All identities are stable
/// test anchors; evidence is source-shaped and deterministic.
/// </summary>
internal static class ScenarioTestFactory
{
    internal static class PredicateTestIds
    {
        internal static readonly OperationId OwnerCondition = new("operation:v1:decision:predicate-owner");
        internal static readonly OperationId SubordinateCondition = new("operation:v1:decision:predicate-subordinate");
    }
    private const string ResultType = "GetMeaning.Services.GadgetResult<GetMeaning.Models.Gadget>";
    private const string ServiceTypeName = "GetMeaning.Services.IGadgetService";
    private const string ImplementationTypeName = "GetMeaning.Services.GadgetService";

    internal static readonly CompilationProfile Profile = CompilationProfile.Create(
        "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj",
        "Release",
        "net10.0");

    // A distinct compilation profile used by the regression foreign-set partitions: companion fact sets
    // from another profile must never contribute evidence or selection to the current graph.
    internal static readonly CompilationProfile ForeignProfile = CompilationProfile.Create(
        "tests/fixtures/BehaviorDocumentation/GetMeaning/Other.csproj",
        "Release",
        "net10.0");
    internal static string ProgramIndexFingerprint => CreateGetRequest().ProgramIndex.IndexFingerprint;

    internal static readonly MethodId ActionMethod = new("method:v1:GetMeaning.Controllers.GadgetsController.GetById");
    internal static readonly MethodId ConstructorMethod = new("method:v1:GetMeaning.Controllers.GadgetsController..ctor");
    internal static readonly MethodId InterfaceMethod = new("method:v1:GetMeaning.Services.IGadgetService.GetByIdAsync");
    internal static readonly MethodId ServiceMethod = new("method:v1:GetMeaning.Services.GadgetService.GetByIdAsync");
    internal static readonly MethodId OtherServiceMethod = new("method:v1:GetMeaning.Services.MemoryGadgetService.GetByIdAsync");
    internal static readonly OperationId ServiceCallOperation = new("operation:v1:call.GetByIdAsync");
    internal static readonly OperationId PredicateOperation = new("operation:v1:predicate.IdEquals");
    internal static readonly OperationId SuccessOperation = new("operation:v1:factory.Success");
    internal static readonly OperationId DuplicateSuccessOperation = new("operation:v1:factory.Success.duplicate");
    internal static readonly OperationId NotFoundOperation = new("operation:v1:factory.NotFound");
    internal static readonly CallSiteId ServiceCallSiteId = new("call-site:v1:GetById");
    internal static readonly CallSiteId SecondCallSiteId = new("call-site:v1:GetById.second");
    internal static readonly EntryPointId GetEntryPoint = new("entry-point:v1:GET-api-Gadgets");
    internal static readonly SemanticFactId ServiceRegistrationId = new("semantic-fact:v1:di-registration:GadgetService");
    internal static readonly SemanticFactId OtherServiceRegistrationId = new("semantic-fact:v1:di-registration:MemoryGadgetService");

    internal static readonly OperationId RootDirectCallOperation = new("operation:v1:root.transfer");
    internal static readonly MethodId RootDirectCallTarget = new("method:v1:Payments.TransferGateway.SendAsync");
    internal static readonly MethodId NestedDirectCallTarget = new("method:v1:Payments.TransferGateway.ValidateAsync");

    /// <summary>
    /// Presentation-contract helper: gives every decision exact Owner predicate wording so planner
    /// fragment tests exercise the renderable path. Fixture requests without predicate semantic
    /// facts produce decisions without wording, and the planner now withholds such decisions
    /// instead of rendering generic labels.
    /// </summary>
    internal static ScenarioGraph WithExactOwnerWording(ScenarioGraph graph)
    {
        var decisions = graph.Topology.Decisions
            .Select((decision, index) => new ScenarioDecision(
                decision.Id,
                decision.Method,
                decision.ControllingFlowNode,
                decision.Condition,
                decision.Evidence,
                decision.Certainty,
                new ScenarioPredicateWording(
                    new SemanticFactId($"semantic-fact:v1:predicate:wording:{index}"),
                    new PredicateExpression(
                        PredicateExpressionKind.Comparison,
                        [
                            new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Object", displayName: "reservation"),
                            new PredicateExpression(PredicateExpressionKind.NullConstant, [], "System.Object"),
                        ],
                        "System.Boolean",
                        PredicateComparisonOperatorKind.Equal),
                    ScenarioPredicateWordingRole.Owner,
                    [SourceEvidence("predicate")],
                    CertaintyLevel.Exact)))
            .ToImmutableArray();
        var topology = new ScenarioTopology(
            decisions,
            graph.Topology.Arms,
            graph.Topology.Memberships,
            graph.Topology.Terminals);
        return new ScenarioGraph(
            graph.EntryPoint,
            graph.Profile,
            graph.RootMethod,
            graph.HttpMethod,
            graph.CanonicalRoute,
            graph.OperationKey,
            graph.Nodes,
            graph.Edges,
            graph.Diagnostics,
            graph.DebugProjection,
            topology,
            graph.Composition,
            graph.CallbackRegions,
            graph.HandlerTopology,
            graph.DispatchHandlerExpansion,
            graph.RootKind,
            graph.DirectCallExpansion);
    }

    // accepted contract conditional DI composition anchors. The top-level method identity is the synthesized
    // Program entry that owns the if/else; the condition/read operations and the toggle key join the
    // alternative group to the accepted contract configuration facts.
    internal const string ConditionalStorageKey = "Storage:UseMemoryStorage";
    internal static readonly MethodId ConditionalProgramMethod = new("method:v1:AdvancedAnalysis.ConditionalDependencyInjection.Program.<Main>$");
    internal static readonly OperationId ConditionalReadOperation = new("operation:v1:conditional:read.UseMemoryStorage");
    internal static readonly OperationId ConditionalConditionOperation = new("operation:v1:conditional:condition.UseMemoryStorage");

    // accepted contract identity-anchor variant: a different top-level method and condition/read operations prove
    // the composition identity follows the conditional anchor rather than the entry point or route.
    internal static readonly MethodId ConditionalProgramMethodAlternate = new("method:v1:AdvancedAnalysis.ConditionalDependencyInjection.Program.<Main>$:alternate");
    internal static readonly OperationId ConditionalReadOperationAlternate = new("operation:v1:conditional:read.UseAlternateStorage");
    internal static readonly OperationId ConditionalConditionOperationAlternate = new("operation:v1:conditional:condition.UseAlternateStorage");

    internal static ScenarioAnalysisRequest CreateGetRequest(
        bool ambiguousDiTargets = false,
        bool multipleCallSites = false,
        bool incompleteResolution = false,
        bool duplicateSuccessFactories = false,
        bool unrelatedFactory = false,
        bool statusSwitchFlow = false,
        bool decisionGuarded = false,
        string? predicateJoinMismatch = null)
    {
        var controllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.GadgetsController");
        var interfaceType = new SymbolId("symbol:v1:GetMeaning.Services.IGadgetService");
        var serviceType = new SymbolId("symbol:v1:GetMeaning.Services.GadgetService");
        var otherServiceType = new SymbolId("symbol:v1:GetMeaning.Services.MemoryGadgetService");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "GetMeaning.Controllers.GadgetsController"),
            CreateType(interfaceType, "GetMeaning.Services.IGadgetService"),
            CreateType(serviceType, "GetMeaning.Services.GadgetService"),
            CreateType(otherServiceType, "GetMeaning.Services.MemoryGadgetService"));
        var methods = ImmutableArray.Create(
            CreateMethod(ActionMethod, controllerType, "GetById"),
            CreateMethod(ConstructorMethod, controllerType, ".ctor"),
            CreateMethod(InterfaceMethod, interfaceType, "GetByIdAsync"),
            CreateMethod(ServiceMethod, serviceType, "GetByIdAsync"),
            CreateMethod(OtherServiceMethod, otherServiceType, "GetByIdAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.Cha,
            ambiguousDiTargets
                ? ImmutableArray.Create(ServiceMethod, OtherServiceMethod)
                : ImmutableArray.Create(ServiceMethod),
            "source",
            IsComplete: !incompleteResolution,
            [],
            [SourceEvidence("call-resolution")],
            CertaintyLevel.Exact);
        var callSitesBuilder = ImmutableArray.CreateBuilder<CallSite>();
        callSitesBuilder.Add(new CallSite(
            ServiceCallSiteId,
                         predicateJoinMismatch == "foreign-method" ? new MethodId("method:v1:foreign.PredicateOwner") : ActionMethod,
            ServiceCallOperation,
            CallKind.Instance,
            InterfaceMethod,
            resolution,
            [SourceEvidence("call-site")],
            CertaintyLevel.Exact));
        if (multipleCallSites)
        {
            callSitesBuilder.Add(new CallSite(
                SecondCallSiteId,
                ActionMethod,
                new OperationId("operation:v1:call.GetByIdAsync.second"),
                CallKind.Instance,
                InterfaceMethod,
                resolution,
                [SourceEvidence("call-site-second")],
                CertaintyLevel.Exact));
        }

        var callSites = callSitesBuilder.ToImmutable();
        var callGraphEdges = ImmutableArray.CreateBuilder<CallGraphEdge>();
        callGraphEdges.Add(new CallGraphEdge(ActionMethod, ServiceCallSiteId, ServiceMethod));
        if (ambiguousDiTargets)
        {
            callGraphEdges.Add(new CallGraphEdge(ActionMethod, ServiceCallSiteId, OtherServiceMethod));
        }

        if (multipleCallSites)
        {
            callGraphEdges.Add(new CallGraphEdge(ActionMethod, SecondCallSiteId, ServiceMethod));
        }

        var actionLocalNode = new ValueNode(
            new ValueNodeId("value-node:v1:local.result"),
            ActionMethod,
            ValueNodeKind.OperationResult,
            ResultType,
            "result",
            null,
            null,
            null,
            [SourceEvidence("value-local")],
            CertaintyLevel.Exact);
        // The decision-guarded variant gives the action flow a real decision and a guarded call
        // invocation so the topology builder has arms to place material nodes under; the flat default
        // keeps an empty flow so decision-free graphs retain their legacy no-diagnostic behavior.
        var actionNodes = new List<FlowNode>();
        var actionEdges = new List<FlowEdge>();
        var actionDependences = new List<ControlDependence>();
        if (decisionGuarded)
        {
            var entry = new EntryFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(ActionMethod, "Entry", 0, 0, "entry")),
                ActionMethod,
                [SourceEvidence("action-entry")],
                CertaintyLevel.Exact);
            var exit = new ExitFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(ActionMethod, "Exit", int.MaxValue, int.MaxValue, "exit")),
                ActionMethod,
                [SourceEvidence("action-exit")],
                CertaintyLevel.Exact);
            var decision = new DecisionFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(ActionMethod, "Decision", 1, 0, "decision")),
                ActionMethod,
                PredicateTestIds.OwnerCondition,
                [SourceEvidence("action-decision")],
                CertaintyLevel.Exact);
            var subordinateDecision = new DecisionFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(ActionMethod, "Decision", 1, 1, "subordinate-decision")),
                ActionMethod,
                PredicateTestIds.SubordinateCondition,
                [SourceEvidence("action-subordinate-decision")],
                CertaintyLevel.Exact);
            var callInvocation = new InvocationFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(ActionMethod, "Invocation", 2, 0, "operation")),
                ActionMethod,
                ServiceCallOperation,
                InterfaceMethod,
                IsDispatchable: false,
                IsDelegateOrEventInvoke: false,
                IsStatic: false,
                IsConstructor: false,
                IsDynamic: false,
                [SourceEvidence("action-call")],
                CertaintyLevel.Exact);
            actionNodes.AddRange(new FlowNode[] { entry, exit, decision, subordinateDecision, callInvocation });
            actionEdges.Add(Edge(ActionMethod, 0, entry, decision, FlowEdgeKind.Normal, null));
            actionEdges.Add(Edge(ActionMethod, 1, decision, subordinateDecision, FlowEdgeKind.True, PredicateTestIds.OwnerCondition));
            actionEdges.Add(Edge(ActionMethod, 2, decision, exit, FlowEdgeKind.False, PredicateTestIds.OwnerCondition));
            actionEdges.Add(Edge(ActionMethod, 3, subordinateDecision, callInvocation, FlowEdgeKind.True, PredicateTestIds.SubordinateCondition));
            actionEdges.Add(Edge(ActionMethod, 4, subordinateDecision, exit, FlowEdgeKind.False, PredicateTestIds.SubordinateCondition));
            actionEdges.Add(Edge(ActionMethod, 5, callInvocation, exit, FlowEdgeKind.Normal, null));
            actionDependences.Add(new ControlDependence(
                decision.Id,
                subordinateDecision.Id,
                true,
                [SourceEvidence("action-dependence")],
                CertaintyLevel.Exact));
            actionDependences.Add(new ControlDependence(
                subordinateDecision.Id,
                callInvocation.Id,
                true,
                [SourceEvidence("action-subordinate-dependence")],
                CertaintyLevel.Exact));
        }

        var actionFlow = new MethodFlowSnapshot(
            ActionMethod,
            "body-fingerprint",
            actionNodes.ToImmutableArray(),
            actionEdges.ToImmutableArray(),
            [],
            [],
            new LocalValueGraph([actionLocalNode], []),
            actionDependences.ToImmutableArray(),
            null,
            [],
            "flow-fingerprint");
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [actionFlow],
            new CallGraph(callGraphEdges.ToImmutable(), callSites),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint");

        var entryPoint = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:GET-api-Gadgets"),
            Evidence = [SourceEvidence("entry-point")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = GetEntryPoint,
            RootMethod = ActionMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "api/Gadgets/{id}",
            OperationKey = "GET api/Gadgets/{id}",
        };
        var outcomeOk = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:Ok"),
            Evidence = [SourceEvidence("outcome-ok")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.Ok"),
            HelperKind = HttpOutcomeHelperKind.Ok,
            StatusCode = 200,
        };
        var outcomeNotFound = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:NotFound"),
            Evidence = [SourceEvidence("outcome-not-found")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.NotFound"),
            HelperKind = HttpOutcomeHelperKind.NotFound,
            StatusCode = 404,
        };
        var outcomeConflict = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:Conflict"),
            Evidence = [SourceEvidence("outcome-conflict")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.Conflict"),
            HelperKind = HttpOutcomeHelperKind.Conflict,
            StatusCode = 409,
        };
        var query = new EntityFrameworkQueryFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:ef-query:GetById"),
            Evidence = [SourceEvidence("ef-query")],
            Certainty = CertaintyLevel.Exact,
            Method = ServiceMethod,
            Operation = new OperationId("operation:v1:SingleOrDefaultAsync"),
            DbContextType = "GetMeaning.Data.GadgetDbContext",
            DbSetMemberType = "Microsoft.EntityFrameworkCore.DbSet<GetMeaning.Models.Gadget>",
            EntityType = "GetMeaning.Models.Gadget",
            Chain =
            [
                new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.AsNoTracking,
                    new OperationId("operation:v1:AsNoTracking"),
                    null),
                new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.Include,
                    new OperationId("operation:v1:Include-Parts"),
                    "GetMeaning.Models.Gadget.Parts"),
                new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.Include,
                    new OperationId("operation:v1:Include-Category"),
                    "GetMeaning.Models.Gadget.Category"),
                new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync,
                    new OperationId("operation:v1:SingleOrDefaultAsync"),
                    null),
            ],
            PredicateOperation = PredicateOperation,
            PredicateOperator = ComparisonOperatorKind.Equal,
        };

        var frameworkFacts = new List<BehaviorFact> { entryPoint, outcomeOk, outcomeNotFound, query };
        if (statusSwitchFlow)
        {
            frameworkFacts.Add(outcomeConflict);
        }

        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            frameworkFacts.ToImmutableArray(),
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var registrations = ambiguousDiTargets
            ? ImmutableArray.Create(
                CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName),
                CreateRegistration(OtherServiceRegistrationId, ServiceMethod, ServiceTypeName, "GetMeaning.Services.MemoryGadgetService"))
            : ImmutableArray.Create(
                CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName));
        var bindings = ambiguousDiTargets
            ? ImmutableArray.Create(
                CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0),
                CreateBinding(ConstructorMethod, OtherServiceRegistrationId, ServiceTypeName, "GetMeaning.Services.MemoryGadgetService", 1))
            : ImmutableArray.Create(
                CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0));
        var dependencyInjection = new DependencyInjectionFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            registrations,
            bindings,
            [],
            "di-test");

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [
                new ComparisonSemanticFact(
                    new SemanticFactId("semantic-fact:v1:comparison:IdEquals"),
                    ServiceMethod,
                    ComparisonOperatorKind.Equal,
                    PredicateOperation,
                    new OperationId("operation:v1:Id"),
                    new OperationId("operation:v1:id"),
                    [SourceEvidence("comparison")],
                    CertaintyLevel.Exact),
            ],
            [],
            BuildReturnProvenances(unrelatedFactory, duplicateSuccessFactories),
            [],
            "semantic-test");

        var factoriesBuilder = ImmutableArray.CreateBuilder<StructuralResultFactoryFact>();
        // The Success factory exists in the method; when unrelatedFactory is set its result is not
        // proven to flow to the return, so it is never associated with the service result.
        factoriesBuilder.Add(CreateFactoryFact(
            "semantic-fact:v1:factory:Success",
            ServiceMethod,
            SuccessOperation,
            StructuralResultFactoryKind.Success,
            true,
            new OperationId("operation:v1:data")));

        if (duplicateSuccessFactories)
        {
            factoriesBuilder.Add(CreateFactoryFact(
                "semantic-fact:v1:factory:Success.duplicate",
                ServiceMethod,
                DuplicateSuccessOperation,
                StructuralResultFactoryKind.Success,
                true,
                new OperationId("operation:v1:data.duplicate")));
        }

        factoriesBuilder.Add(CreateFactoryFact(
            "semantic-fact:v1:factory:NotFound",
            ServiceMethod,
            NotFoundOperation,
            StructuralResultFactoryKind.NotFound,
            false,
            null));
        var structural = new StructuralResultFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            factoriesBuilder.ToImmutable(),
            statusSwitchFlow
                ? []
                : [
                    new StructuralResultDecisionFact(
                        new SemanticFactId("semantic-fact:v1:decision:IsSuccess"),
                        ActionMethod,
                        new OperationId("operation:v1:decision.not"),
                        new OperationId("operation:v1:property.IsSuccess"),
                        new OperationId("operation:v1:local.result"),
                        "result",
                        true,
                        [new StructuralOutcomePath(HttpOutcomeHelperKind.Ok, new OperationId("operation:v1:outcome.Ok"))],
                        [new StructuralOutcomePath(HttpOutcomeHelperKind.NotFound, new OperationId("operation:v1:outcome.NotFound"))],
                        [SourceEvidence("decision")],
                        CertaintyLevel.Exact),
                ],
            [],
            "structural-test");

        var nonGet = statusSwitchFlow
            ? new NonGetSemanticFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    new StatusSwitchArmFact(
                        new SemanticFactId("semantic-fact:v1:status:NotFound"),
                        ActionMethod,
                        new OperationId("operation:v1:switch.Status"),
                        "GetMeaning.Services.GadgetResultStatus",
                        "NotFound",
                        HttpOutcomeHelperKind.NotFound,
                        new OperationId("operation:v1:outcome.NotFound"),
                        null,
                        null,
                        [SourceEvidence("status-arm-not-found")],
                        CertaintyLevel.Exact),
                    new StatusSwitchArmFact(
                        new SemanticFactId("semantic-fact:v1:status:Conflict"),
                        ActionMethod,
                        new OperationId("operation:v1:switch.Status"),
                        "GetMeaning.Services.GadgetResultStatus",
                        "Conflict",
                        HttpOutcomeHelperKind.Conflict,
                        new OperationId("operation:v1:outcome.Conflict"),
                        null,
                        null,
                        [SourceEvidence("status-arm-conflict")],
                        CertaintyLevel.Exact),
                    new StatusSwitchArmFact(
                        new SemanticFactId("semantic-fact:v1:status:Default"),
                        ActionMethod,
                        new OperationId("operation:v1:switch.Status"),
                        "GetMeaning.Services.GadgetResultStatus",
                        "default",
                        HttpOutcomeHelperKind.Ok,
                        new OperationId("operation:v1:outcome.Ok"),
                        null,
                        null,
                        [SourceEvidence("status-arm-default")],
                        CertaintyLevel.Exact),
                ],
                [],
                [],
                [],
                [],
                [
                    new EntityFrameworkMutationFact
                    {
                        Id = new BehaviorFactId("behavior-fact:v1:ef-mutation:RemoveRange"),
                        Method = ServiceMethod,
                        Operation = new OperationId("operation:v1:RemoveRange"),
                        MutationKind = EntityFrameworkMutationKind.RemoveRange,
                        SequenceOrdinal = 1,
                        DbContextType = "GetMeaning.Data.GadgetDbContext",
                        EntityType = "GetMeaning.Models.Gadget",
                        Evidence = [SourceEvidence("ef-mutation-remove")],
                        Certainty = CertaintyLevel.Exact,
                    },
                    new EntityFrameworkMutationFact
                    {
                        Id = new BehaviorFactId("behavior-fact:v1:ef-mutation:Save"),
                        Method = ServiceMethod,
                        Operation = new OperationId("operation:v1:SaveChangesAsync"),
                        MutationKind = EntityFrameworkMutationKind.SaveChangesAsync,
                        SequenceOrdinal = 2,
                        DbContextType = "GetMeaning.Data.GadgetDbContext",
                        EntityType = string.Empty,
                        Evidence = [SourceEvidence("ef-mutation-save")],
                        Certainty = CertaintyLevel.Exact,
                    },
                ],
                [
                    new EfOperationSequenceFact(ServiceMethod, new OperationId("operation:v1:SingleOrDefaultAsync"), EfOperationSequenceKind.QueryTerminal, 0),
                    new EfOperationSequenceFact(ServiceMethod, new OperationId("operation:v1:RemoveRange"), EfOperationSequenceKind.Mutation, 1),
                    new EfOperationSequenceFact(ServiceMethod, new OperationId("operation:v1:SaveChangesAsync"), EfOperationSequenceKind.Mutation, 2),
                ],
                [],
                "non-get-status-switch")
            : new NonGetSemanticFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                "non-get-test");

        PredicateSemanticFactSet? predicateFacts = decisionGuarded
            ? new PredicateSemanticFactSet(
                1,
                "test",
                predicateJoinMismatch == "foreign-profile" ? ForeignProfile : Profile,
                predicateJoinMismatch == "foreign-fingerprint" ? "foreign-fingerprint" : index.IndexFingerprint,
                [
                    new PredicateSemanticFact(
                        new SemanticFactId("semantic-fact:v1:predicate:owner"),
                         predicateJoinMismatch == "foreign-method" ? new MethodId("method:v1:foreign.PredicateOwner") : ActionMethod,
                        new OperationId("operation:v1:predicate:source"),
                        new PredicateExpression(
                            PredicateExpressionKind.LogicalAnd,
                            [
                                new PredicateExpression(
                                    PredicateExpressionKind.Comparison,
                                    [
                                        new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Object", displayName: "reservation"),
                                        new PredicateExpression(PredicateExpressionKind.NullConstant, [], "System.Object"),
                                    ],
                                    "System.Boolean",
                                    PredicateComparisonOperatorKind.Equal),
                                new PredicateExpression(
                                    PredicateExpressionKind.Comparison,
                                    [
                                        new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "GetMeaning.Status", displayName: "status"),
                                        new PredicateExpression(PredicateExpressionKind.EnumConstant, [], "GetMeaning.Status", constantValue: "Cancelled"),
                                    ],
                                    "System.Boolean",
                                    PredicateComparisonOperatorKind.Equal),
                            ],
                            "System.Boolean"),
                         predicateJoinMismatch == "foreign-profile" ? ForeignProfile.Id : Profile.Id,
                         predicateJoinMismatch == "foreign-fingerprint" ? "foreign-fingerprint" : index.IndexFingerprint,
                        [SourceEvidence("predicate")],
                        CertaintyLevel.Exact),
                ],
                [
            new PredicateDecisionMappingFact(
                        new SemanticFactId("semantic-fact:v1:predicate-mapping:owner"),
                        new SemanticFactId("semantic-fact:v1:predicate:owner"),
                predicateJoinMismatch == "foreign-method" ? new MethodId("method:v1:foreign.PredicateOwner") : ActionMethod,
                predicateJoinMismatch == "unmapped-lowered"
                    ? [PredicateOperation]
                    : [PredicateTestIds.OwnerCondition, PredicateTestIds.SubordinateCondition],
                predicateJoinMismatch == "foreign-profile" ? ForeignProfile.Id : Profile.Id,
                predicateJoinMismatch == "foreign-fingerprint" ? "foreign-fingerprint" : index.IndexFingerprint,
                        [SourceEvidence("predicate-mapping")],
                        CertaintyLevel.Exact),
                ],
                [],
                "predicate-test")
            : null;

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            dependencyInjection,
            structural,
            nonGet,
            PredicateSemanticFacts: predicateFacts);
    }

    internal static ScenarioAnalysisRequest CreateConfiguredRootRequest(
        bool includeFrameworkRoot = false,
        bool reverseConstruction = false)
    {
        var request = CreateGetRequest();
        var roots = includeFrameworkRoot
            ? new[] { ServiceMethod, ActionMethod }
            : new[] { ServiceMethod };
        if (reverseConstruction)
        {
            Array.Reverse(roots);
        }

        return request with { ConfiguredRoots = roots.ToImmutableArray() };
    }

    internal static ScenarioAnalysisRequest CreateRootDirectCallRequest(
        bool decisionGuarded = false,
        string? exclusion = null,
        bool duplicateAnchor = false,
        bool reverseConstruction = false)
    {
        var baseRequest = CreateGetRequest();
        var evidence = SourceEvidence("root-direct-call");
        var validateOperation = new OperationId("operation:v1:root.validate");
        var validateTarget = new MethodId("method:v1:Payments.TransferValidator.ValidateAsync");
        var decisionId = new FlowNodeId("flow-node:v1:root-direct:decision");
        var entryId = new FlowNodeId("flow-node:v1:root-direct:entry");
        var exitId = new FlowNodeId("flow-node:v1:root-direct:exit");

        var calls = new List<InvocationFlowNode>
        {
            new(new FlowNodeId("flow-node:v1:root-direct:send"), ActionMethod, RootDirectCallOperation,
                exclusion is "unresolved" ? null : RootDirectCallTarget,
                false, exclusion is "delegate", false, exclusion is "constructor", exclusion is "dynamic",
                [evidence], CertaintyLevel.Exact, "Payments.TransferGateway", "SendAsync",
                exclusion is "nested", IsSourceBacked: exclusion is not "unresolved",
                IsLoadedProjectTarget: true, BlockOrdinal: decisionGuarded ? 1 : 0,
                EvaluationOrdinal: 1, TargetAssemblyName: "Payments", IsPlatformTarget: exclusion is "platform"),
        };
        if (decisionGuarded)
        {
            calls.Add(new InvocationFlowNode(new FlowNodeId("flow-node:v1:root-direct:validate"), ActionMethod, validateOperation,
                validateTarget, false, false, false, false, false, [evidence], CertaintyLevel.Exact,
                "Payments.TransferValidator", "ValidateAsync", IsSourceBacked: true,
                IsLoadedProjectTarget: true, BlockOrdinal: 0, EvaluationOrdinal: 0,
                TargetAssemblyName: "Payments"));
        }
        if (duplicateAnchor)
        {
            calls.Add(calls[0] with { Id = new FlowNodeId("flow-node:v1:root-direct:send-duplicate") });
        }
        if (reverseConstruction)
        {
            calls.Reverse();
        }

        var flowNodes = new List<FlowNode>
        {
            new EntryFlowNode(entryId, ActionMethod, [evidence], CertaintyLevel.Exact),
            new ExitFlowNode(exitId, ActionMethod, [evidence], CertaintyLevel.Exact),
        };
        if (decisionGuarded)
        {
            flowNodes.Add(new DecisionFlowNode(decisionId, ActionMethod, PredicateOperation, [evidence], CertaintyLevel.Exact));
        }
        flowNodes.AddRange(calls);
        var flowEdges = new List<FlowEdge>();
        if (decisionGuarded)
        {
            flowEdges.Add(new FlowEdge(new("flow-edge:v1:root-direct:entry"), ActionMethod, entryId, decisionId, FlowEdgeKind.Normal, null, [evidence], CertaintyLevel.Exact));
            flowEdges.Add(new FlowEdge(new("flow-edge:v1:root-direct:true"), ActionMethod, decisionId, calls[0].Id, FlowEdgeKind.True, PredicateOperation, [evidence], CertaintyLevel.Exact));
            flowEdges.Add(new FlowEdge(new("flow-edge:v1:root-direct:false"), ActionMethod, decisionId, calls[1].Id, FlowEdgeKind.False, PredicateOperation, [evidence], CertaintyLevel.Exact));
        }
        else
        {
            for (var i = 0; i < calls.Count; i++)
            {
                flowEdges.Add(new FlowEdge(new($"flow-edge:v1:root-direct:{i}"), ActionMethod,
                    i == 0 ? entryId : calls[i - 1].Id, calls[i].Id, FlowEdgeKind.Normal, null, [evidence], CertaintyLevel.Exact));
            }
        }

        var canonicalCalls = calls
            .GroupBy(call => call.Operation)
            .OrderBy(group => group.Key.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var resolutionEvidence = exclusion is "conservative-resolution"
            ? ImmutableArray.Create(ConservativeEvidence("root-direct-call-resolution"))
            : ImmutableArray.Create(evidence);
        var callSites = canonicalCalls.Select((call, ordinal) => new CallSite(
            new CallSiteId($"call-site:v1:root-direct:{ordinal}"), ActionMethod, call.Operation, CallKind.Instance,
            call.Target, new CallTargetResolution(
                exclusion is "ambiguous" ? CallResolutionKind.Cha : CallResolutionKind.DirectExact,
                exclusion is "ambiguous"
                    ? ImmutableArray.Create(RootDirectCallTarget, NestedDirectCallTarget)
                    : call.Target is null
                        ? ImmutableArray<MethodId>.Empty
                        : ImmutableArray.Create(call.Target.Value),
                "source", exclusion is not "unresolved", [], resolutionEvidence,
                exclusion is "conservative-resolution" ? CertaintyLevel.Conservative : CertaintyLevel.Exact),
            [evidence], CertaintyLevel.Exact)).ToImmutableArray();
        var targetFlow = new MethodFlowSnapshot(NestedDirectCallTarget, "nested-target", [], [], [], [], new LocalValueGraph([], []), [], null, [], "nested-target");
        var behavior = baseRequest.Behavior with
        {
            MethodFlows = [new MethodFlowSnapshot(ActionMethod, "root-direct", flowNodes.ToImmutableArray(), flowEdges.ToImmutableArray(), [], [], new LocalValueGraph([], []),
                decisionGuarded ? [new ControlDependence(decisionId, calls[0].Id, true, [evidence], CertaintyLevel.Exact)] : [], null, [], "root-direct"), targetFlow],
            CallGraph = new CallGraph(callSites.Select(site => new CallGraphEdge(ActionMethod, site.Id,
                site.Resolution.Candidates.Length == 0 ? RootDirectCallTarget : site.Resolution.Candidates[0])).ToImmutableArray(), callSites),
        };
        return baseRequest with
        {
            Behavior = behavior,
            DependencyInjectionFacts = new DependencyInjectionFactSet(1, "test", Profile, baseRequest.ProgramIndex.IndexFingerprint, [], [], [], "empty-di"),
        };
    }

    internal static ScenarioAnalysisRequest CreateRootDirectCallTryRequest()
    {
        var request = CreateRootDirectCallRequest(decisionGuarded: true);
        var flow = request.Behavior.MethodFlows.Single(candidate => candidate.Method == ActionMethod);
        var exit = flow.Nodes.OfType<ExitFlowNode>().Single();
        var calls = flow.Nodes.OfType<InvocationFlowNode>().OrderBy(node => node.BlockOrdinal).ToArray();
        var extraEdges = calls.Select((call, ordinal) => new FlowEdge(
            StableIdentity.CreateFlowEdgeId(new FlowEdgeIdentityDescriptor(
                ActionMethod, call.Id.Value, exit.Id.Value, FlowEdgeKind.Normal.ToString(), 10 + ordinal)),
            ActionMethod,
            call.Id,
            exit.Id,
            FlowEdgeKind.Normal,
            null,
            [SourceEvidence("root-direct-try-edge")],
            CertaintyLevel.Exact));
        var decision = flow.Nodes.OfType<DecisionFlowNode>().Single();
        var falseCall = calls.Single(call => call.Operation != RootDirectCallOperation);
        var root = new FlowRegion(
            StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(ActionMethod, "Root", 0)),
            ActionMethod,
            FlowRegionKind.Root,
            null,
            0,
            flow.Nodes.Select(node => node.Id).ToImmutableArray(),
            null,
            [],
            CertaintyLevel.Exact);
        var tryRegion = new FlowRegion(
            StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(ActionMethod, "Try", 1)),
            ActionMethod,
            FlowRegionKind.Try,
            root.Id,
            1,
            flow.Nodes.OfType<DecisionFlowNode>().Select(node => node.Id)
                .Concat(calls.Select(node => node.Id)).ToImmutableArray(),
            null,
            [SourceEvidence("root-direct-try")],
            CertaintyLevel.Exact);
        // Keep the CT-3 call-site facts as the canonical anchors. Only the normal arm-to-exit
        // edges and region memberships are added here; no second invocation or call-site fact is
        // fabricated by the Try mutation.
        return request with
        {
            Behavior = request.Behavior with
            {
                MethodFlows = request.Behavior.MethodFlows
                    .Select(candidate => candidate.Method == ActionMethod
                        ? candidate with
                        {
                            Edges = candidate.Edges.AddRange(extraEdges),
                            ControlDependences = candidate.ControlDependences.Add(new ControlDependence(
                                decision.Id,
                                falseCall.Id,
                                false,
                                [SourceEvidence("root-direct-try-false-membership")],
                                CertaintyLevel.Exact)),
                            Regions = [root, tryRegion],
                        }
                        : candidate)
                    .ToImmutableArray(),
            },
        };
    }

    internal static ScenarioAnalysisRequest CreateMinimalApiRequest(MinimalApiRouteFact route)
    {
        var request = CreateGetRequest();
        var fact = route with
        {
            Id = new BehaviorFactId("behavior-fact:v1:minimal-api-route"),
            Evidence = [SourceEvidence("minimal-api-route")],
            Certainty = CertaintyLevel.Exact,
        };
        return request with
        {
            FrameworkFacts = new FrameworkAnalysisResult(
                true,
                [fact],
                [], [], [], [],
                [new FrameworkModelDescriptor("seqdoc.aspnetcore.minimal-api", "1.0.0", "test", 101)],
                request.Profile.Id,
                request.ProgramIndex.IndexFingerprint),
        };
    }

    internal const string ServiceContractTypeName = "CoreWcfServices.ICalculatorService";
    internal const string ServiceImplementationTypeName = "CoreWcfServices.CalculatorService";
    internal const string ServiceOperationName = "Add";
    internal const string ServiceOperationKeyValue = $"{ServiceContractTypeName}.{ServiceOperationName}";
    internal static readonly SymbolId ServiceContractTypeSymbol = new($"symbol:v1:{ServiceContractTypeName}");
    internal static readonly SymbolId ServiceImplementationTypeSymbol = new($"symbol:v1:{ServiceImplementationTypeName}");
    internal static readonly SymbolId ServiceOperationSymbol = new($"symbol:v1:{ServiceContractTypeName}.{ServiceOperationName}");

    // A dedicated service-implementation method identity, distinct from ActionMethod (a controller
    // action): reusing ActionMethod would anchor the service-operation root to a controller's Method
    // Flow instead of a real service implementation method, masking bugs in root identity, evidence
    // propagation, and topology that depend on the actual admitted service method.
    internal static readonly MethodId ServiceOperationMethod =
        new($"method:v1:{ServiceImplementationTypeName}.{ServiceOperationName}");

    internal static EntryPointId ServiceOperationEntryPoint => StableIdentity.CreateServiceOperationEntryPointId(
        new ServiceOperationEntryPointIdentityDescriptor(Profile.Id, ServiceOperationMethod, ServiceOperationKeyValue));

    private static ServiceOperationCapabilityFact CreateServiceCapabilityFact(CertaintyLevel certainty = CertaintyLevel.Exact)
        => new()
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-operation-capability:Add"),
            Evidence = [certainty == CertaintyLevel.Exact ? SourceEvidence("service-operation-capability") : ConservativeEvidence("service-operation-capability")],
            Certainty = certainty,
            RootMethod = ServiceOperationMethod,
            ServiceContractType = ServiceContractTypeName,
            ServiceContractTypeSymbol = ServiceContractTypeSymbol,
            ImplementationType = ServiceImplementationTypeName,
            ImplementationTypeSymbol = ServiceImplementationTypeSymbol,
            OperationName = ServiceOperationName,
            OperationSymbol = ServiceOperationSymbol,
            OperationKey = ServiceOperationKeyValue,
        };

    private static ServiceEndpointRegistrationFact CreateServiceRegistrationFact(CertaintyLevel certainty = CertaintyLevel.Exact)
        => new()
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-endpoint-registration:Add"),
            Evidence = [certainty == CertaintyLevel.Exact ? SourceEvidence("service-endpoint-registration") : ConservativeEvidence("service-endpoint-registration")],
            Certainty = certainty,
            ImplementationType = ServiceImplementationTypeName,
            ImplementationTypeSymbol = ServiceImplementationTypeSymbol,
            ServiceContractType = ServiceContractTypeName,
            ServiceContractTypeSymbol = ServiceContractTypeSymbol,
            BindingType = "CoreWCF.BasicHttpBinding",
            Address = "/CalculatorService/basicHttp",
        };

    /// <summary>
    /// A minimal Program Index + Method Flow scoped to the CoreWCF service implementation method
    /// (<see cref="ServiceOperationMethod"/>), independent of the GetMeaning controller/action fixture
    /// <see cref="CreateGetRequest"/> builds. This is what anchors the service-operation scenario tests
    /// to a real service-shaped root method identity and flow rather than a borrowed controller action.
    /// </summary>
    private static ScenarioAnalysisRequest CreateServiceBaseRequest()
    {
        var contractType = new SymbolId($"symbol:v1:{ServiceContractTypeName}");
        var implementationType = new SymbolId($"symbol:v1:{ServiceImplementationTypeName}");
        var index = CreateIndex(
            ImmutableArray.Create(
                CreateType(contractType, ServiceContractTypeName),
                CreateType(implementationType, ServiceImplementationTypeName)),
            ImmutableArray.Create(CreateMethod(ServiceOperationMethod, implementationType, ServiceOperationName)));

        var entry = new EntryFlowNode(
            StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(ServiceOperationMethod, "Entry", 0, 0, "entry")),
            ServiceOperationMethod,
            [SourceEvidence("service-operation-entry")],
            CertaintyLevel.Exact);
        var exit = new ExitFlowNode(
            StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(ServiceOperationMethod, "Exit", int.MaxValue, int.MaxValue, "exit")),
            ServiceOperationMethod,
            [SourceEvidence("service-operation-exit")],
            CertaintyLevel.Exact);
        var flow = new MethodFlowSnapshot(
            ServiceOperationMethod,
            "service-operation-body-fingerprint",
            [entry, exit],
            [Edge(ServiceOperationMethod, 0, entry, exit, FlowEdgeKind.Normal, null)],
            [],
            [],
            new LocalValueGraph([], []),
            [],
            null,
            [],
            "service-operation-flow-fingerprint");
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [flow],
            new CallGraph([], []),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "service-operation-behavior-fingerprint");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            new FrameworkAnalysisResult(true, [], [], [], [], [], [], Profile.Id, index.IndexFingerprint),
            new SemanticFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"));
    }

    /// <summary>
    /// A CoreWCF service operation root, admitted by joining an independently proven capability fact
    /// with a matching registration fact, anchored to <see cref="ServiceOperationMethod"/>'s own Program
    /// Index entry and Method Flow: issue #7's model projects dispatch through the same default
    /// Method-Flow-driven topology path a controller action already uses. No HTTP method or canonical
    /// route ever backs this root.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateServiceOperationRequest(
        CertaintyLevel capabilityCertainty = CertaintyLevel.Exact,
        CertaintyLevel registrationCertainty = CertaintyLevel.Exact)
    {
        var request = CreateServiceBaseRequest();
        return request with
        {
            FrameworkFacts = new FrameworkAnalysisResult(
                true,
                [CreateServiceCapabilityFact(capabilityCertainty), CreateServiceRegistrationFact(registrationCertainty)],
                [], [], [], [],
                [new FrameworkModelDescriptor("seqdoc.corewcf.services", "2.0.0", "test", 110)],
                request.Profile.Id,
                request.ProgramIndex.IndexFingerprint),
        };
    }

    private static ServiceEndpointRegistrationFact CreateSecondServiceRegistrationFact(CertaintyLevel certainty = CertaintyLevel.Exact)
        => new()
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-endpoint-registration:Add:second"),
            Evidence = [SourceEvidence("service-endpoint-registration-second")],
            Certainty = certainty,
            ImplementationType = ServiceImplementationTypeName,
            ImplementationTypeSymbol = ServiceImplementationTypeSymbol,
            ServiceContractType = ServiceContractTypeName,
            ServiceContractTypeSymbol = ServiceContractTypeSymbol,
            BindingType = "CoreWCF.WSHttpBinding",
            Address = "/CalculatorService/wsHttp",
        };

    /// <summary>
    /// Two exact, independently proven endpoint registrations for the same (implementation, contract)
    /// pair: proves exactly one root is admitted (never one per endpoint), both registrations' evidence
    /// is unioned into it, and reversing the registration array's order never changes the admitted root,
    /// its evidence, or its certainty.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateServiceOperationRequestWithTwoRegistrations(bool reversed = false)
    {
        var request = CreateServiceBaseRequest();
        var capability = CreateServiceCapabilityFact();
        var first = CreateServiceRegistrationFact();
        var second = CreateSecondServiceRegistrationFact();
        return request with
        {
            FrameworkFacts = new FrameworkAnalysisResult(
                true,
                reversed ? [capability, second, first] : [capability, first, second],
                [], [], [], [],
                [new FrameworkModelDescriptor("seqdoc.corewcf.services", "2.0.0", "test", 110)],
                request.Profile.Id,
                request.ProgramIndex.IndexFingerprint),
        };
    }

    /// <summary>
    /// A compiler-proven service contract capability with no matching endpoint registration: proves the
    /// unregistered-capability boundary (no executable root, no execution wording, a conservative
    /// diagnostic instead).
    /// </summary>
    internal static ScenarioAnalysisRequest CreateUnregisteredServiceCapabilityRequest(CertaintyLevel capabilityCertainty = CertaintyLevel.Exact)
    {
        var request = CreateServiceBaseRequest();
        return request with
        {
            FrameworkFacts = new FrameworkAnalysisResult(
                true,
                [CreateServiceCapabilityFact(capabilityCertainty)],
                [], [], [], [],
                [new FrameworkModelDescriptor("seqdoc.corewcf.services", "2.0.0", "test", 110)],
                request.Profile.Id,
                request.ProgramIndex.IndexFingerprint),
        };
    }

    internal static ScenarioAnalysisRequest CreateMinimalApiHandlerRequest()
    {
        var handlerRoot = new MethodId("method:v1:Program.Telecom");
        var baseRequest = CreateMinimalApiRequest(new MinimalApiRouteFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:minimal-handler-route"),
            Evidence = [SourceEvidence("minimal-handler-route")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:POST-api-sms"),
            HandlerRoot = handlerRoot,
            HandlerKind = MinimalApiHandlerKind.AnonymousFunction,
            HttpMethod = HttpMethodKind.Post,
            CanonicalRoute = "api/sms",
            OperationKey = "POST api/sms",
        });

        var evidence = ImmutableArray.Create(SourceEvidence("minimal-handler"));
        var parameters = ImmutableArray.Create(
            new MinimalApiHandlerParameter("request", "SmsRequest", HttpBindingKind.Body, Evidence: evidence),
            new MinimalApiHandlerParameter("cancellationToken", "System.Threading.CancellationToken", HttpBindingKind.CancellationToken, Evidence: evidence));
        var predicates = ImmutableArray.Create(30, 50).Select((constant, index) =>
            new MinimalApiHandlerPredicate(
                new OperationId($"operation:v1:telecom:predicate:{index}"),
                new PredicateExpression(
                    PredicateExpressionKind.Comparison,
                    [
                        new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Int32", displayName: "roll"),
                        new PredicateExpression(PredicateExpressionKind.NumericConstant, [], "System.Int32", constantValue: constant.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ],
                    "System.Boolean",
                    PredicateComparisonOperatorKind.LessThanOrEqual),
                constant,
                 new MinimalApiHandlerArm(index == 0 ? 0 : 2, true, index),
                 new MinimalApiHandlerArm(index == 0 ? 1 : 3, false, index),
                evidence,
                CertaintyLevel.Exact,
                TrueArmTerminates: true)).ToImmutableArray();
        var outcomes = ImmutableArray.Create(
            new MinimalApiHandlerOutcome(new OperationId("operation:v1:telecom:problem"), "Microsoft.AspNetCore.Http.Results.Problem", 500, new(0), evidence, CertaintyLevel.Exact),
            new MinimalApiHandlerOutcome(new OperationId("operation:v1:telecom:delayed-ok"), "Microsoft.AspNetCore.Http.Results.Ok", 200, new(2), evidence, CertaintyLevel.Exact),
            new MinimalApiHandlerOutcome(new OperationId("operation:v1:telecom:immediate-ok"), "Microsoft.AspNetCore.Http.Results.Ok", 200, new(3), evidence, CertaintyLevel.Exact));
        var operations = ImmutableArray.Create<MinimalApiHandlerOperation>(
            new(outcomes[0].Id, MinimalApiHandlerOperationKind.Outcome, outcomes[0].FactoryIdentity, null, 500, outcomes[0].FactoryIdentity, outcomes[0].Arm, evidence, CertaintyLevel.Exact),
            new(new OperationId("operation:v1:telecom:delay"), MinimalApiHandlerOperationKind.Delay, "System.Threading.Tasks.Task.Delay", 11000, null, null, new(1, true, 1), evidence, CertaintyLevel.Exact),
            new(outcomes[1].Id, MinimalApiHandlerOperationKind.Outcome, outcomes[1].FactoryIdentity, null, 200, outcomes[1].FactoryIdentity, outcomes[1].Arm, evidence, CertaintyLevel.Exact),
            new(outcomes[2].Id, MinimalApiHandlerOperationKind.Outcome, outcomes[2].FactoryIdentity, null, 200, outcomes[2].FactoryIdentity, outcomes[2].Arm, evidence, CertaintyLevel.Exact));
        var fact = new MinimalApiHandlerFact(
            new CallbackBoundaryId("callback-boundary:v1:telecom"),
            handlerRoot,
            new OperationId("operation:v1:telecom:body"),
            parameters,
            operations,
            predicates,
            outcomes,
            evidence,
            CertaintyLevel.Exact);
        var facts = new MinimalApiHandlerFactSet(Profile, baseRequest.ProgramIndex.IndexFingerprint, [fact], [], "minimal-handler");
        return baseRequest with { HandlerFacts = facts };
    }

    internal static ScenarioAnalysisRequest CreateMinimalApiDispatchRequest(
        DispatchFact dispatch, bool foreignProfile = false, bool foreignFingerprint = false)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var request = CreateMinimalApiRequest(new MinimalApiRouteFact
        {
            Id = new("behavior-fact:v1:minimal-dispatch-route"),
            Evidence = [SourceEvidence("minimal-dispatch-route")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:POST-orders"),
            HandlerRoot = new MethodId("method:v1:Program.CreateOrder"),
            HandlerKind = MinimalApiHandlerKind.NamedMethod,
            HttpMethod = HttpMethodKind.Post,
            CanonicalRoute = "/orders",
            OperationKey = "POST /orders",
        });
        request = request with
        {
            FrameworkFacts = request.FrameworkFacts with
            {
                Facts = request.FrameworkFacts.Facts.Add(dispatch),
            },
        };

        var dispatchTypes = request.ProgramIndex.Types
            .Add(CreateType(new SymbolId("symbol:v1:Dispatch.Handler"), "Dispatch.Handler"))
            .Add(CreateType(new SymbolId("symbol:v1:Aggregate"), "Aggregate"))
            .Add(CreateType(new SymbolId("symbol:v1:Dto"), "Dto"))
            .Add(CreateType(new SymbolId("symbol:v1:Alpha.Widget"), "Alpha.Widget"))
            .Add(CreateType(new SymbolId("symbol:v1:Beta.Widget"), "Beta.Widget"));
        var dispatchMethods = request.ProgramIndex.Methods
            .Add(CreateDispatchMethod(DispatchHandlerFlowFixture.Handler, new SymbolId("symbol:v1:Dispatch.Handler"), "Handle",
                dispatch.Id.Value.Contains("return-mismatch", StringComparison.Ordinal) ? "System.Threading.Tasks.Task<OtherDto>"
                : dispatch.Id.Value.Contains("task-return", StringComparison.Ordinal) ? "System.Threading.Tasks.Task<Dto>" : "Dto"))
            .Add(CreateDispatchMethod(new("Aggregate.Create"), new SymbolId("symbol:v1:Aggregate"), "Create", "Aggregate"))
            .Add(CreateDispatchMethod(new("Aggregate.Add"), new SymbolId("symbol:v1:Aggregate"), "Add", "void"))
            .Add(CreateDispatchMethod(new("Dto.FromDomain"), new SymbolId("symbol:v1:Dto"), "FromDomain", "Dto"))
            .Add(CreateDispatchMethod(new("Aggregate.Total"), new SymbolId("symbol:v1:Aggregate"), "Total", "int"))
            .Add(CreateDispatchMethod(new("Alpha.Widget.Send"), new SymbolId("symbol:v1:Alpha.Widget"), "Send", "void"))
            .Add(CreateDispatchMethod(new("Beta.Widget.Send"), new SymbolId("symbol:v1:Beta.Widget"), "Send", "void"));
        var dispatchIndex = request.ProgramIndex with { Types = dispatchTypes, Methods = dispatchMethods };
        var dispatchBehavior = request.Behavior with
        {
            MethodFlows = BuildDispatchFlows(dispatch),
            CallGraph = BuildDispatchCallGraph(dispatch),
        };
        request = request with { ProgramIndex = dispatchIndex, Behavior = dispatchBehavior };

        if (foreignProfile)
        {
            request = request with { Profile = ForeignProfile };
        }

        if (foreignFingerprint)
        {
            const string fingerprint = "foreign-program-index-fingerprint";
            request = request with
            {
                ProgramIndex = request.ProgramIndex with { IndexFingerprint = fingerprint },
                Behavior = request.Behavior with { ProgramIndexFingerprint = fingerprint },
            };
        }

        return request;
    }

    private static ProgramMethod CreateDispatchMethod(MethodId id, SymbolId type, string name, string returnType)
        => new(id, new SymbolId($"symbol:v1:{id.Value}"), type, name, $"{name}()", [], returnType, $"dispatch:{id.Value}", "dispatch-body", [SourceEvidence($"dispatch-method:{name}")]);

    private static ImmutableArray<MethodFlowSnapshot> BuildDispatchFlows(DispatchFact dispatch)
    {
        var handler = DispatchHandlerFlowFixture.Handler;
        var create = new MethodId("Aggregate.Create");
        var add = new MethodId("Aggregate.Add");
        var dto = new MethodId("Dto.FromDomain");
        var total = new MethodId("Aggregate.Total");
        var ev = SourceEvidence("dispatch-flow");
        FlowNodeId Id(string name) => new($"flow-node:v1:dispatch:{name}");
        var header = new DecisionFlowNode(Id("header"), handler, new("operation:v1:loop.header"), [ev], CertaintyLevel.Exact);
        var createNode = new InvocationFlowNode(Id("create"), handler, new("Aggregate.Create"), create, false, false, false, false, false, [ev], CertaintyLevel.Exact, "Aggregate", "Create", IsSourceBacked: true, IsLoadedProjectTarget: true, BlockOrdinal: 0, EvaluationOrdinal: 0, TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false);
        // The second identical operation anchor deliberately has no typed target presentation. It
        // remains the duplicate-anchor regression partition rather than becoming a second claim.
        var duplicateCreateNode = new InvocationFlowNode(Id("create-duplicate"), handler, new("Aggregate.Create"), new("Aggregate.Create.Duplicate"), false, false, false, false, false, [ev], CertaintyLevel.Exact);
        var addNode = new InvocationFlowNode(Id("add"), handler, new("Aggregate.Add"), add, false, false, false, false, false, [ev], CertaintyLevel.Exact, "Aggregate", "Add", IsSourceBacked: true, IsLoadedProjectTarget: true, BlockOrdinal: 1, EvaluationOrdinal: 0, TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false);
        var dtoNode = new InvocationFlowNode(Id("dto"), handler, new("Dto.FromDomain"), dto, false, false, false, false, false, [ev], CertaintyLevel.Exact, "Dto", "FromDomain", IsSourceBacked: true, IsLoadedProjectTarget: true, BlockOrdinal: 2, EvaluationOrdinal: 0, TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false);
        var alpha = new MethodId("Alpha.Widget.Send");
        var beta = new MethodId("Beta.Widget.Send");
        var alphaNode = new InvocationFlowNode(Id("alpha"), handler, new("Alpha.Widget.Send"), alpha, false, false, false, false, false, [ev], CertaintyLevel.Exact, "Alpha.Widget", "Send", IsSourceBacked: true, IsLoadedProjectTarget: true, BlockOrdinal: 3, EvaluationOrdinal: 0, TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false);
        var betaNode = new InvocationFlowNode(Id("beta"), handler, new("Beta.Widget.Send"), beta, false, false, false, false, false, [ev], CertaintyLevel.Exact, "Beta.Widget", "Send", IsSourceBacked: true, IsLoadedProjectTarget: true, BlockOrdinal: 4, EvaluationOrdinal: 0, TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false);
        var returnNode = new ReturnFlowNode(Id("return"), handler, new("return"), [ev], CertaintyLevel.Exact);
        var exit = new ExitFlowNode(Id("exit"), handler, [ev], CertaintyLevel.Exact);
        var loop = new LoopNode(Id("loop"), handler, new("flow-region:v1:dispatch:items"), header.Id, [addNode.Id], [exit.Id], [ev], CertaintyLevel.Exact, [1]);
        var nestedTotal = new InvocationFlowNode(new("flow-node:v1:total"), dto, new("Aggregate.Total"), total, false, false, false, false, false, [ev], CertaintyLevel.Exact, "Aggregate", "Total", IsSourceBacked: true, IsLoadedProjectTarget: true, BlockOrdinal: 0, EvaluationOrdinal: 0, TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false);
        if (dispatch.Id.Value.Contains("incomplete-loop", StringComparison.Ordinal))
        {
            loop = loop with { Exits = [] };
        }
        if (dispatch.Id.Value.Contains("foreign-loop-back", StringComparison.Ordinal))
        {
            var outside = new InvocationFlowNode(Id("outside"), handler, new("Aggregate.Outside"), new("Aggregate.Outside"), false, false, false, false, false, [ev], CertaintyLevel.Exact, "Aggregate", "Outside", IsSourceBacked: true, IsLoadedProjectTarget: true, BlockOrdinal: 5, EvaluationOrdinal: 0, TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false);
            var nodes = ImmutableArray.Create<FlowNode>(createNode, duplicateCreateNode, header, addNode, dtoNode, outside, returnNode, loop, exit);
            return [
                new MethodFlowSnapshot(handler, "dispatch-handler", nodes, [new FlowEdge(new("flow-edge:v1:foreign"), handler, outside.Id, header.Id, FlowEdgeKind.LoopBack, null, [ev], CertaintyLevel.Exact)], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-handler"),
                new MethodFlowSnapshot(add, "dispatch-add", [], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-add"),
                new MethodFlowSnapshot(dto, "dispatch-dto", [nestedTotal], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-dto"),
                new MethodFlowSnapshot(total, "dispatch-total", [], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-total"),
            ];
        }
        if (dispatch.Id.Value.Contains("unresolved-lookalike", StringComparison.Ordinal))
        {
            var look = new InvocationFlowNode(
                Id("lookalike"), handler, new("Lookalike.Add"), new("Lookalike.Add"), false, false, false, false,
                false, [ev], CertaintyLevel.Exact, "Lookalike", "Add", IsInsideNestedFunction: true,
                TargetAssemblyName: "Fixture.Application", IsPlatformTarget: false, BlockOrdinal: 3);
            return
            [
                new MethodFlowSnapshot(handler, "dispatch-handler", [createNode, duplicateCreateNode, header, addNode, look, dtoNode, returnNode, loop, exit], [new FlowEdge(new("flow-edge:v1:back"), handler, addNode.Id, header.Id, FlowEdgeKind.LoopBack, null, [ev], CertaintyLevel.Exact)], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-handler"),
                new MethodFlowSnapshot(add, "dispatch-add", [], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-add"),
                new MethodFlowSnapshot(dto, "dispatch-dto", [nestedTotal], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-dto"),
                new MethodFlowSnapshot(total, "dispatch-total", [], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-total"),
            ];
        }
        return
        [
            new MethodFlowSnapshot(handler, "dispatch-handler", ImmutableArray.Create<FlowNode>(createNode, duplicateCreateNode, header, addNode, dtoNode)
                .AddRange(dispatch.Id.Value.Contains("canonical-participants", StringComparison.Ordinal)
                    ? ImmutableArray.Create<FlowNode>(alphaNode, betaNode) : [])
                .AddRange(ImmutableArray.Create<FlowNode>(returnNode, loop, exit)), [new FlowEdge(new("flow-edge:v1:back"), handler, addNode.Id, header.Id, FlowEdgeKind.LoopBack, null, [ev], CertaintyLevel.Exact)], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-handler"),
            new MethodFlowSnapshot(add, "dispatch-add", [], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-add"),
            new MethodFlowSnapshot(dto, "dispatch-dto", [nestedTotal], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-dto"),
            new MethodFlowSnapshot(total, "dispatch-total", [], [], [], [], new LocalValueGraph([], []), [], null, [], "dispatch-total"),
        ];
    }

    private static CallGraph BuildDispatchCallGraph(DispatchFact dispatch)
    {
        var handler = DispatchHandlerFlowFixture.Handler;
        var dto = new MethodId("Dto.FromDomain");
        var sites = new List<CallSite>();
        var handlerInvocations = new[]
        {
            (Id: new FlowNodeId("flow-node:v1:dispatch:add"), Operation: new OperationId("Aggregate.Add")),
            (Id: new FlowNodeId("flow-node:v1:dispatch:create"), Operation: new OperationId("Aggregate.Create")),
            (Id: new FlowNodeId("flow-node:v1:dispatch:create-duplicate"), Operation: new OperationId("Aggregate.Create")),
            (Id: new FlowNodeId("flow-node:v1:dispatch:dto"), Operation: new OperationId("Dto.FromDomain")),
        }
        .Concat(dispatch.Id.Value.Contains("canonical-participants", StringComparison.Ordinal)
            ? new[]
            {
                (Id: new FlowNodeId("flow-node:v1:dispatch:alpha"), Operation: new OperationId("Alpha.Widget.Send")),
                (Id: new FlowNodeId("flow-node:v1:dispatch:beta"), Operation: new OperationId("Beta.Widget.Send")),
            }
            : dispatch.Id.Value.Contains("unresolved-lookalike", StringComparison.Ordinal)
                ? new[]
                {
                    (Id: new FlowNodeId("flow-node:v1:dispatch:lookalike"), Operation: new OperationId("Lookalike.Add")),
                }
                : dispatch.Id.Value.Contains("foreign-loop-back", StringComparison.Ordinal)
                    ? new[]
                    {
                        (Id: new FlowNodeId("flow-node:v1:dispatch:outside"), Operation: new OperationId("Aggregate.Outside")),
                    }
            : [])
        .OrderBy(invocation => invocation.Id.Value, StringComparer.Ordinal)
        .ToArray();
        int Ordinal(FlowNodeId flowNodeId)
            => Array.FindIndex(handlerInvocations, invocation => invocation.Id == flowNodeId);
        void Add(MethodId caller, FlowNodeId flowNodeId, OperationId operation, MethodId target)
        {
            var ev = SourceEvidence($"dispatch-call:{operation.Value}");
            var ordinal = caller == handler ? Ordinal(flowNodeId) : 0;
            var callSiteId = StableIdentity.CreateCallSiteId(
                new CallSiteIdentityDescriptor(caller, operation, ordinal));
            sites.Add(new CallSite(callSiteId, caller, operation, CallKind.Instance, target,
                new CallTargetResolution(CallResolutionKind.DirectExact, [target], "source", true, [], [ev], CertaintyLevel.Exact), [ev], CertaintyLevel.Exact));
        }
        Add(handler, new FlowNodeId("flow-node:v1:dispatch:create"), new("Aggregate.Create"), new("Aggregate.Create"));
        Add(handler, new FlowNodeId("flow-node:v1:dispatch:create-duplicate"), new("Aggregate.Create"), new("Aggregate.Create.Duplicate"));
        Add(handler, new FlowNodeId("flow-node:v1:dispatch:add"), new("Aggregate.Add"), new("Aggregate.Add"));
        Add(handler, new FlowNodeId("flow-node:v1:dispatch:dto"), new("Dto.FromDomain"), dto);
        if (dispatch.Id.Value.Contains("canonical-participants", StringComparison.Ordinal))
        {
            Add(handler, new FlowNodeId("flow-node:v1:dispatch:alpha"), new("Alpha.Widget.Send"), new("Alpha.Widget.Send"));
            Add(handler, new FlowNodeId("flow-node:v1:dispatch:beta"), new("Beta.Widget.Send"), new("Beta.Widget.Send"));
        }
        Add(dto, new FlowNodeId("flow-node:v1:total"), new OperationId("Aggregate.Total"), new("Aggregate.Total"));
        if (dispatch.Id.Value.Contains("unresolved-lookalike", StringComparison.Ordinal))
        {
            var ev = SourceEvidence("dispatch-call:Lookalike.Add");
            sites.Add(new CallSite(
                new CallSiteId("call-site:v1:Lookalike.Add"),
                handler,
                new OperationId("Lookalike.Add"),
                CallKind.Instance,
                new MethodId("Lookalike.Add"),
                new CallTargetResolution(CallResolutionKind.DirectExact, [new MethodId("Lookalike.Add")], "source", true, [], [ev], CertaintyLevel.Exact),
                [ev],
                CertaintyLevel.Conservative));
        }
        var edges = sites
            .Where(site => site.Resolution.Candidates.Length != 0)
            .Select(site => new CallGraphEdge(site.ContainingMethod, site.Id, site.Resolution.Candidates[0]))
            .ToImmutableArray();
        return new CallGraph(edges, sites.ToImmutableArray());
    }

    /// <summary>
    /// accepted contract conditional DI composition fixture. Two exact registrations for IGadgetService sit behind
    /// one top-level condition operation (GadgetService true arm / MemoryGadgetService false arm)
    /// whose compiler call resolution includes BOTH implementations. The conditional fact set carries
    /// one complete alternative group so the Scenario Graph may suppress SC001 only for that exact
    /// pair. The <paramref name="extraUnguardedRegistration"/>, <paramref name="missingGroup"/>, and
    /// <paramref name="incompleteResolution"/> variants prove the fail-closed partitions;
    /// <paramref name="profileKnownSelection"/> adds a Conservative accepted contract profile-known true for the
    /// toggle key; <paramref name="reverseConstruction"/> reverses registration, binding, arm,
    /// target, and edge construction order to prove identity determinism;
    /// <paramref name="differentEntryPoint"/> changes only the entry point/route;
    /// <paramref name="differentConditionAnchor"/> changes the top-level method and condition/read
    /// operations so the composition identity determinism test can prove which anchors form it;
    /// <paramref name="foreignConditionalProfile"/> attaches the conditional fact set to a foreign
    /// compilation profile; and <paramref name="foreignConfigurationFingerprint"/> attaches the
    /// configuration fact set to a foreign Program Index fingerprint.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateConditionalDiRequest(
        bool profileKnownSelection = false,
        bool extraUnguardedRegistration = false,
        bool missingGroup = false,
        bool incompleteResolution = false,
        bool reverseConstruction = false,
        bool differentEntryPoint = false,
        bool differentConditionAnchor = false,
        bool foreignConditionalProfile = false,
        bool foreignConfigurationFingerprint = false)
    {
        var extraRegistrationId = new SemanticFactId("semantic-fact:v1:di-registration:ExtraGadgetService");
        var extraServiceType = new SymbolId("symbol:v1:GetMeaning.Services.ExtraGadgetService");
        var extraServiceMethod = new MethodId("method:v1:GetMeaning.Services.ExtraGadgetService.GetByIdAsync");

        // The condition-anchor variant changes the top-level method and the condition/read operations
        // so the composition identity determinism test can prove those anchors are part of the
        // identity; the entry-point variant changes only the entry point and route.
        var conditionalMethod = differentConditionAnchor ? ConditionalProgramMethodAlternate : ConditionalProgramMethod;
        var conditionalCondition = differentConditionAnchor ? ConditionalConditionOperationAlternate : ConditionalConditionOperation;
        var conditionalRead = differentConditionAnchor ? ConditionalReadOperationAlternate : ConditionalReadOperation;

        var controllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.GadgetsController");
        var interfaceType = new SymbolId("symbol:v1:GetMeaning.Services.IGadgetService");
        var serviceType = new SymbolId("symbol:v1:GetMeaning.Services.GadgetService");
        var otherServiceType = new SymbolId("symbol:v1:GetMeaning.Services.MemoryGadgetService");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "GetMeaning.Controllers.GadgetsController"),
            CreateType(interfaceType, "GetMeaning.Services.IGadgetService"),
            CreateType(serviceType, "GetMeaning.Services.GadgetService"),
            CreateType(otherServiceType, "GetMeaning.Services.MemoryGadgetService"));
        var methods = ImmutableArray.Create(
            CreateMethod(ActionMethod, controllerType, "GetById"),
            CreateMethod(ConstructorMethod, controllerType, ".ctor"),
            CreateMethod(InterfaceMethod, interfaceType, "GetByIdAsync"),
            CreateMethod(ServiceMethod, serviceType, "GetByIdAsync"),
            CreateMethod(OtherServiceMethod, otherServiceType, "GetByIdAsync"));
        if (extraUnguardedRegistration)
        {
            types = types.Add(CreateType(extraServiceType, "GetMeaning.Services.ExtraGadgetService"));
            methods = methods.Add(CreateMethod(extraServiceMethod, extraServiceType, "GetByIdAsync"));
        }

        var index = CreateIndex(types, methods);

        var candidateTargets = ImmutableArray.CreateBuilder<MethodId>();
        candidateTargets.Add(ServiceMethod);
        candidateTargets.Add(OtherServiceMethod);
        if (extraUnguardedRegistration)
        {
            candidateTargets.Add(extraServiceMethod);
        }

        if (reverseConstruction)
        {
            candidateTargets.Reverse();
        }

        var resolution = new CallTargetResolution(
            CallResolutionKind.Cha,
            candidateTargets.ToImmutable(),
            "source",
            IsComplete: !incompleteResolution,
            [],
            [SourceEvidence("call-resolution")],
            CertaintyLevel.Exact);
        var callSites = ImmutableArray.Create(new CallSite(
            ServiceCallSiteId,
            ActionMethod,
            ServiceCallOperation,
            CallKind.Instance,
            InterfaceMethod,
            resolution,
            [SourceEvidence("call-site")],
            CertaintyLevel.Exact));
        var callGraphEdges = ImmutableArray.CreateBuilder<CallGraphEdge>();
        callGraphEdges.Add(new CallGraphEdge(ActionMethod, ServiceCallSiteId, ServiceMethod));
        callGraphEdges.Add(new CallGraphEdge(ActionMethod, ServiceCallSiteId, OtherServiceMethod));
        if (extraUnguardedRegistration)
        {
            callGraphEdges.Add(new CallGraphEdge(ActionMethod, ServiceCallSiteId, extraServiceMethod));
        }

        if (reverseConstruction)
        {
            callGraphEdges.Reverse();
        }

        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [CreateActionFlow(ActionMethod)],
            new CallGraph(callGraphEdges.ToImmutable(), callSites),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint");

        var entryPoint = new HttpEntryPointFact
        {
            Id = new BehaviorFactId(differentEntryPoint
                ? "behavior-fact:v1:entry-point:GET-api-Gadgets-v2"
                : "behavior-fact:v1:entry-point:GET-api-Gadgets"),
            Evidence = [SourceEvidence("entry-point")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = differentEntryPoint ? new EntryPointId("entry-point:v1:GET-api-Gadgets-v2") : GetEntryPoint,
            RootMethod = ActionMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = differentEntryPoint ? "api/Gadgets/{id}/v2" : "api/Gadgets/{id}",
            OperationKey = differentEntryPoint ? "GET api/Gadgets/{id}/v2" : "GET api/Gadgets/{id}",
        };
        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            [entryPoint],
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            "semantic-test");

        var registrations = ImmutableArray.CreateBuilder<DependencyInjectionRegistrationFact>();
        var bindings = ImmutableArray.CreateBuilder<DependencyInjectionBindingFact>();
        registrations.Add(CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName));
        registrations.Add(CreateRegistration(OtherServiceRegistrationId, ServiceMethod, ServiceTypeName, "GetMeaning.Services.MemoryGadgetService"));
        bindings.Add(CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0));
        bindings.Add(CreateBinding(ConstructorMethod, OtherServiceRegistrationId, ServiceTypeName, "GetMeaning.Services.MemoryGadgetService", 1));
        if (extraUnguardedRegistration)
        {
            registrations.Add(CreateRegistration(extraRegistrationId, ServiceMethod, ServiceTypeName, "GetMeaning.Services.ExtraGadgetService"));
            bindings.Add(CreateBinding(ConstructorMethod, extraRegistrationId, ServiceTypeName, "GetMeaning.Services.ExtraGadgetService", 2));
        }

        if (reverseConstruction)
        {
            registrations.Reverse();
            bindings.Reverse();
        }

        var dependencyInjection = new DependencyInjectionFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            registrations.ToImmutable(),
            bindings.ToImmutable(),
            [],
            "di-test");

        var trueArm = new ConditionalDependencyInjectionRegistrationArmFact(
            new SemanticFactId("semantic-fact:v1:conditional-arm:storage-true"),
            conditionalMethod,
            new OperationId("operation:v1:conditional:registration.GadgetService"),
            conditionalCondition,
            conditionalRead,
            ConditionalStorageKey,
            ServiceRegistrationId,
            ServiceTypeName,
            ImplementationTypeName,
            DependencyInjectionLifetime.Scoped,
            IsTrueArm: true,
            [SourceEvidence("conditional-arm-true")],
            CertaintyLevel.Exact);
        var falseArm = new ConditionalDependencyInjectionRegistrationArmFact(
            new SemanticFactId("semantic-fact:v1:conditional-arm:storage-false"),
            conditionalMethod,
            new OperationId("operation:v1:conditional:registration.MemoryGadgetService"),
            conditionalCondition,
            conditionalRead,
            ConditionalStorageKey,
            OtherServiceRegistrationId,
            ServiceTypeName,
            "GetMeaning.Services.MemoryGadgetService",
            DependencyInjectionLifetime.Scoped,
            IsTrueArm: false,
            [SourceEvidence("conditional-arm-false")],
            CertaintyLevel.Exact);
        var group = new ConditionalDependencyInjectionGroupFact(
            new SemanticFactId("semantic-fact:v1:conditional-group:storage"),
            conditionalMethod,
            conditionalCondition,
            conditionalRead,
            ConditionalStorageKey,
            ServiceTypeName,
            ServiceRegistrationId,
            OtherServiceRegistrationId,
            ImplementationTypeName,
            "GetMeaning.Services.MemoryGadgetService",
            DependencyInjectionLifetime.Scoped,
            [SourceEvidence("conditional-group")],
            CertaintyLevel.Exact);
        var armFacts = ImmutableArray.Create(trueArm, falseArm);
        var groups = missingGroup
            ? ImmutableArray<ConditionalDependencyInjectionGroupFact>.Empty
            : ImmutableArray.Create(group);
        var conditionalFacts = new ConditionalDependencyInjectionFactSet(
            1,
            "test",
            foreignConditionalProfile ? ForeignProfile : Profile,
            index.IndexFingerprint,
            reverseConstruction ? armFacts.AsEnumerable().Reverse().ToImmutableArray() : armFacts,
            groups,
            [],
            "conditional-di-test");

        // The profile-known partition: a matching accepted contract analysis-profile boolean marks the true arm
        // selected only within that profile. The scenario builder must retain both arms and
        // provenance and must never promote certainty (the profile-known fact is Conservative).
        var profileKnown = ImmutableArray.CreateBuilder<ProfileKnownConfigurationValueFact>();
        if (profileKnownSelection)
        {
            profileKnown.Add(new ProfileKnownConfigurationValueFact(
                new SemanticFactId("semantic-fact:v1:profile-known:UseMemoryStorage"),
                ConditionalStorageKey,
                true,
                "analysis-profile",
                [ConservativeEvidence("profile-known")],
                CertaintyLevel.Conservative));
        }

        // The configuration facts for the toggle key feed the composition decision evidence: the
        // exact read and direct condition (Exact) plus the Conservative checked-in observation. The
        // decision must aggregate group/config evidence and degrade to the weakest contributor.
        var configReads = ImmutableArray.Create(new ConfigurationReadSemanticFact(
            new SemanticFactId("semantic-fact:v1:config-read:UseMemoryStorage"),
            conditionalMethod,
            conditionalRead,
            ConditionalStorageKey,
            defaultValue: null,
            [SourceEvidence("config-read")],
            CertaintyLevel.Exact));
        var configConditions = ImmutableArray.Create(new ConfigurationConditionSemanticFact(
            new SemanticFactId("semantic-fact:v1:config-condition:UseMemoryStorage"),
            conditionalMethod,
            conditionalRead,
            conditionalCondition,
            trueWhenReadTrue: true,
            [SourceEvidence("config-condition")],
            CertaintyLevel.Exact));

        // A checked-in true observation is always present so the builder can prove that a checked-in
        // value never selects an arm; only the profile-known partition marks a selection.
        var checkedIn = ImmutableArray.Create(new CheckedInConfigurationValueFact(
            new SemanticFactId("semantic-fact:v1:checked-in:UseMemoryStorage"),
            ConditionalStorageKey,
            true,
            "tests/fixtures/AdvancedAnalysis/ConditionalDependencyInjection/appsettings.json",
            [ConservativeEvidence("checked-in")],
            CertaintyLevel.Conservative,
            mayBeOverridden: true));
        var configurationFacts = new ConfigurationSemanticFactSet(
            1,
            "test",
            Profile,
            foreignConfigurationFingerprint ? "foreign-fingerprint" : index.IndexFingerprint,
            configReads,
            configConditions,
            [],
            checkedIn,
            profileKnown.ToImmutable(),
            [],
            "configuration-test");

        var structural = new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-test");
        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            "non-get-test");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            dependencyInjection,
            structural,
            nonGet,
            conditionalFacts,
            configurationFacts);
    }

    /// <summary>
    /// accepted contract synthetic complete composition fixture: the accepted contract conditional DI pair (GadgetService true /
    /// MemoryGadgetService false) gains one EF query fact on the true (SQL) arm and one
    /// Unknown-provenance anonymous FusionCache boundary whose member is that query operation. The
    /// <paramref name="factMode"/> controls the framework companion: "matching" adds exactly one
    /// matching <see cref="FusionCacheGetOrSetFact"/>; "none" adds no fact; "foreign" adds a fact
    /// whose outer operation does not match the boundary; "multiple" adds two matching facts;
    /// "foreign-profile-fact" anchors the fact to a foreign compilation profile;
    /// "foreign-fingerprint-fact" anchors the fact to a foreign Program Index fingerprint;
    /// "boundary-mismatch-fact" anchors the fact to a different callback boundary; "unsupported"
    /// adds no fact but carries the exact SEQFC001 unsupported-shape framework diagnostic anchored
    /// to the boundary's outer operation so the scenario builder must degrade with SC014 and
    /// withhold the boundary member nodes; and "foreign-diagnostic-operation" adds the same
    /// diagnostic anchored to a foreign outer operation so the exact-detail matcher must keep the
    /// query and emit no SC014. The scenario builder must create the cache-miss region only for
    /// "matching", must keep both arm nodes for every mode without ever inventing a selection, and
    /// must emit SC014 with member withholding only when the SEQFC001 diagnostic is present AND
    /// bound to the exact boundary outer operation.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateFusionCacheCompositionRequest(string factMode = "matching")
    {
        if (factMode is not ("matching" or "none" or "foreign" or "multiple"
            or "foreign-profile-fact" or "foreign-fingerprint-fact" or "boundary-mismatch-fact"
            or "unsupported" or "foreign-diagnostic-operation"))
        {
            throw new ArgumentOutOfRangeException(nameof(factMode), factMode, "Unknown FusionCache fact mode.");
        }

        var baseRequest = CreateConditionalDiRequest();
        var frameworkFacts = baseRequest.FrameworkFacts.Facts.ToList();

        var query = new EntityFrameworkQueryFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:ef-query:fusion-cache"),
            Evidence = [SourceEvidence("ef-query-fusion-cache")],
            Certainty = CertaintyLevel.Exact,
            Method = ServiceMethod,
            Operation = ServiceQueryOperation,
            DbContextType = "GetMeaning.Data.GadgetDbContext",
            DbSetMemberType = "Microsoft.EntityFrameworkCore.DbSet<GetMeaning.Models.Gadget>",
            EntityType = "GetMeaning.Models.Gadget",
            Chain =
            [
                new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync,
                    ServiceQueryOperation,
                    null),
            ],
            PredicateOperation = null,
            PredicateOperator = ComparisonOperatorKind.Equal,
        };
        frameworkFacts.Add(query);

        if (factMode is "matching" or "foreign" or "multiple"
            or "foreign-profile-fact" or "foreign-fingerprint-fact" or "boundary-mismatch-fact")
        {
            frameworkFacts.Add(CreateFusionCacheFact(
                "behavior-fact:v1:fusion:get-or-set",
                "fusion-get-or-set",
                baseRequest.ProgramIndex.IndexFingerprint,
                foreignOperation: factMode == "foreign",
                foreignProfile: factMode == "foreign-profile-fact",
                foreignFingerprint: factMode == "foreign-fingerprint-fact",
                boundaryMismatch: factMode == "boundary-mismatch-fact"));
            if (factMode == "multiple")
            {
                frameworkFacts.Add(CreateFusionCacheFact(
                    "behavior-fact:v1:fusion:get-or-set.second",
                    "fusion-get-or-set-second",
                    baseRequest.ProgramIndex.IndexFingerprint,
                    foreignOperation: false));
            }
        }

        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            frameworkFacts.ToImmutableArray(),
            [],
            [],
            [],
            factMode == "unsupported"
                ? [UnsupportedFusionCacheDiagnostic()]
                : factMode == "foreign-diagnostic-operation"
                    ? [UnsupportedFusionCacheDiagnostic(new OperationId("operation:v1:foreign-fusion-cache"))]
                    : [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            baseRequest.Profile.Id,
            baseRequest.ProgramIndex.IndexFingerprint);

        var boundary = new CallbackBoundaryFact(
            FusionCacheBoundaryId,
            ServiceMethod,
            FusionCacheOuterOperation,
            2,
            CallbackTargetKind.AnonymousFunction,
            null,
            FusionCacheFactoryBodyOperation,
            null,
            null,
            CallbackCardinality.Unknown,
            CallbackTriggerKind.Unknown,
            null,
            CallbackCompletionKind.Unknown,
            CallbackContractProvenance.Unknown,
            [ServiceQueryOperation.Value],
            [SourceEvidence("fusion-boundary")],
            CertaintyLevel.Exact);
        var callbackFacts = new CallbackBoundaryFactSet(
            1,
            "test",
            Profile,
            baseRequest.ProgramIndex.IndexFingerprint,
            [boundary],
            [],
            "callback-boundary-fusion");

        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            baseRequest.ProgramIndex.IndexFingerprint,
            [],
            [],
            [],
            [],
            [],
            [],
            [new EfOperationSequenceFact(ServiceMethod, ServiceQueryOperation, EfOperationSequenceKind.QueryTerminal, 0)],
            [],
            "non-get-fusion-cache");

        return baseRequest with
        {
            FrameworkFacts = framework,
            CallbackBoundaryFacts = callbackFacts,
            NonGetSemanticFacts = nonGet,
        };
    }

    /// <summary>
    /// One exact FusionCache 2.6.0 GetOrSetAsync fact for the accepted SQL arm method and outer
    /// operation. Defaults anchor the fact to the request's own profile, Program Index fingerprint,
    /// and <see cref="FusionCacheBoundaryId"/>. The foreign variants change exactly one anchor at a
    /// time (foreign operation, foreign profile, foreign fingerprint, or a different boundary
    /// identity) so the scenario join can prove a non-matching fact never selects a cache-miss
    /// region.
    /// </summary>
    private static FusionCacheGetOrSetFact CreateFusionCacheFact(
        string id,
        string artifact,
        string programIndexFingerprint,
        bool foreignOperation = false,
        bool foreignProfile = false,
        bool foreignFingerprint = false,
        bool boundaryMismatch = false)
        => new(
            foreignProfile ? ForeignProfile.Id : Profile.Id,
            foreignFingerprint ? "foreign-fingerprint" : programIndexFingerprint,
            boundaryMismatch
                ? new CallbackBoundaryId("callback-boundary:v1:sql-service:other-factory")
                : FusionCacheBoundaryId,
            ServiceMethod,
            foreignOperation
                ? new OperationId("operation:v1:fusion:outer.other")
                : FusionCacheOuterOperation,
            2,
            "2.6.0",
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Conditional,
            FrameworkCallbackConditionKind.CacheMiss,
            new BehaviorFactId(id),
            [SourceEvidence(artifact)],
            CertaintyLevel.Exact);

    /// <summary>
    /// One deterministic SEQFC001 unsupported-shape FusionCache diagnostic mirroring the framework
    /// model's Warning. The identity derives from the stable profile and the canonical subject
    /// (the operation+reason detail) through <see cref="StableIdentity.CreateDiagnosticId"/>, the
    /// code is the exact Core constant, the stage is FrameworkModel, the location is profile-scoped,
    /// and certainty is Exact. The canonical <see cref="AnalysisDiagnostic.InternalDetail"/> is built
    /// by <see cref="FusionCacheDiagnosticCodes.UnsupportedShapeDetail"/> so the Scenario Graph
    /// builder can bind the code to the exact diagnosed outer operation; the default anchor is the
    /// composition's FusionCache outer operation (exact unsupported mode) and a foreign anchor
    /// produces a diagnostic that must never degrade this boundary. Evidence is intentionally not
    /// embedded: <see cref="AnalysisDiagnostic"/> evidence is optional by contract and the Scenario
    /// Graph builder joins the diagnostic only by its exact code and canonical detail.
    /// </summary>
    private static AnalysisDiagnostic UnsupportedFusionCacheDiagnostic(OperationId? anchor = null)
    {
        var detail = FusionCacheDiagnosticCodes.UnsupportedShapeDetail(
            anchor ?? FusionCacheOuterOperation,
            "unsupported-synthetic");
        return new AnalysisDiagnostic(
            StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                FusionCacheDiagnosticCodes.UnsupportedShape,
                AnalysisStage.FrameworkModel,
                Profile.Id,
                detail,
                Ordinal: 0)),
            FusionCacheDiagnosticCodes.UnsupportedShape,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "The FusionCache GetOrSetAsync call has an unsupported shape; no cache-miss contract was admitted.",
            new DiagnosticLocation("fusion cache get-or-set", Profile.Id),
            "The operation is recognizably the FusionCache GetOrSetAsync family but the exact supported contract cannot be proven.",
            "No FusionCache cache-miss fact was emitted; the call is never presented as supported cache work.",
            "Use the exact supported FusionCache 2.6.0 GetOrSetAsync overload with a key, an anonymous value factory, and an options callback.",
            CertaintyLevel.Exact,
            internalDetail: detail);
    }

    // accepted contract callback-boundary anchors. The caller is the accepted single service method
    // (GadgetService.GetByIdAsync); the generated member nodes whose exact OperationId values the
    // boundary must reference are the EF query node (ServiceQueryOperation) and the structural
    // result nodes (SuccessOperation/NotFoundOperation). The region join must use exact operation
    // identity, never display text.
    internal static readonly OperationId ServiceQueryOperation = new("operation:v1:SingleOrDefaultAsync");
    internal static readonly OperationId CallbackConditionOperation = new("operation:v1:callback:condition.IsEnabled");
    internal static readonly OperationId CallbackOuterInvocationOperation = new("operation:v1:callback:outer.ProcessAsync");
    internal static readonly OperationId CallbackSecondOuterInvocationOperation = new("operation:v1:callback:outer.OnError");
    internal static readonly OperationId CallbackTargetBodyOperation = new("operation:v1:callback:target.body");
    internal static readonly OperationId CallbackContractInvokeOperation = new("operation:v1:callback:contract.invoke");
    internal static readonly CallbackBoundaryId PrimaryCallbackBoundaryId = new("callback-boundary:v1:gadget-service:onReady");
    internal static readonly CallbackBoundaryId SecondaryCallbackBoundaryId = new("callback-boundary:v1:gadget-service:onError");

    // accepted contract FusionCache composition anchors. The true arm is the accepted SQL implementation method
    // (GadgetService) whose GetOrSetAsync value factory contains the EF query; the false arm is the
    // memory/JSON implementation with no query. The boundary is the Unknown-provenance anonymous
    // metadata target the FusionCache model admits; the exact GetOrSetAsync fact supplies the
    // ZeroOrOne/Conditional/CacheMiss semantics.
    internal static readonly OperationId FusionCacheOuterOperation = new("operation:v1:fusion:outer.GetCustomerByIdAsync");
    internal static readonly OperationId FusionCacheFactoryBodyOperation = new("operation:v1:fusion:factory.body");
    internal static readonly CallbackBoundaryId FusionCacheBoundaryId = new("callback-boundary:v1:sql-service:cache-miss-factory");

    /// <summary>
    /// accepted contract callback-boundary variant builder. Starts from the accepted GetMeaning single-service
    /// request (<see cref="CreateGetRequest"/>) and appends one optional memory-first callback
    /// boundary fact set. When <paramref name="callbackBoundaryFacts"/> is supplied it is used
    /// verbatim; otherwise one exact boundary fact is constructed for the request's own Profile and
    /// Program Index fingerprint with the caller service method <see cref="ServiceMethod"/> and the
    /// exact generated member-node operations (query and success result). The
    /// <paramref name="repeatedOrUnknown"/>, <paramref name="unknownCompletion"/>,
    /// <paramref name="foreignProfile"/>, <paramref name="foreignFingerprint"/>,
    /// <paramref name="reverseBoundaryConstruction"/>, and <paramref name="reverseMemberOrder"/>
    /// variants drive the constructed fact set; the final optional fact set makes the foreign
    /// profile/fingerprint and reversed-order partitions explicit without extra product vocabulary.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateCallbackBoundaryRequest(
        bool repeatedOrUnknown = false,
        bool unknownCompletion = false,
        bool foreignProfile = false,
        bool foreignFingerprint = false,
        bool reverseBoundaryConstruction = false,
        bool reverseMemberOrder = false,
        CallbackBoundaryFactSet? callbackBoundaryFacts = null)
    {
        var baseRequest = CreateGetRequest();
        if (callbackBoundaryFacts is not null)
        {
            return baseRequest with { CallbackBoundaryFacts = callbackBoundaryFacts };
        }

        var cardinality = repeatedOrUnknown ? CallbackCardinality.RepeatedOrUnknown : CallbackCardinality.ZeroOrOne;
        var trigger = repeatedOrUnknown ? CallbackTriggerKind.Unknown : CallbackTriggerKind.Conditional;
        OperationId? triggerCondition = repeatedOrUnknown ? null : CallbackConditionOperation;
        var completion = unknownCompletion ? CallbackCompletionKind.Unknown : CallbackCompletionKind.RejoinsCaller;
        var factSet = CreateCallbackBoundaryFactSet(
            baseRequest.ProgramIndex.IndexFingerprint,
            cardinality,
            trigger,
            triggerCondition,
            completion,
            foreignProfile,
            foreignFingerprint,
            reverseBoundaryConstruction,
            reverseMemberOrder);
        return baseRequest with { CallbackBoundaryFacts = factSet };
    }

    /// <summary>
    /// Builds the accepted contract callback boundary fact set for the accepted GetMeaning single-service flow:
    /// one exact anonymous-function callback argument to a source delegate parameter of
    /// <see cref="ServiceMethod"/>, SourceBody contract provenance, and the generated member
    /// operations <c>operation:v1:SingleOrDefaultAsync</c> and <c>operation:v1:factory.Success</c>.
    /// The foreign-profile and foreign-fingerprint variants detach the set from the request binding;
    /// <paramref name="reverseMemberOrder"/> reverses the canonical member array and
    /// <paramref name="reverseBoundaryConstruction"/> reverses the boundary array so region identity
    /// and member order determinism can be observed at the Scenario Graph layer.
    /// </summary>
    internal static CallbackBoundaryFactSet CreateCallbackBoundaryFactSet(
        string programIndexFingerprint,
        CallbackCardinality cardinality,
        CallbackTriggerKind trigger,
        OperationId? triggerCondition,
        CallbackCompletionKind completion,
        bool foreignProfile,
        bool foreignFingerprint,
        bool reverseBoundaryConstruction,
        bool reverseMemberOrder)
    {
        var members = ImmutableArray.Create(ServiceQueryOperation.Value, SuccessOperation.Value);
        if (reverseMemberOrder)
        {
            members = members.AsEnumerable().Reverse().ToImmutableArray();
        }

        var boundary = CreateCallbackBoundaryFact(
            PrimaryCallbackBoundaryId,
            CallbackOuterInvocationOperation,
            cardinality,
            trigger,
            triggerCondition,
            completion,
            members,
            [SourceEvidence("callback-boundary")],
            CertaintyLevel.Exact);
        var boundaries = reverseBoundaryConstruction
            ? ImmutableArray.Create(boundary)
            : ImmutableArray.Create(boundary);
        return new CallbackBoundaryFactSet(
            1,
            "test",
            foreignProfile ? ForeignProfile : Profile,
            foreignFingerprint ? "foreign-fingerprint" : programIndexFingerprint,
            boundaries,
            [],
            "callback-boundary-fact-set");
    }

    /// <summary>
    /// One exact source callback boundary anchored to <see cref="ServiceMethod"/> as caller and
    /// contract method, an anonymous-function target body, parameter ordinal zero, and the supplied
    /// cardinality/trigger/completion/member anchors. Evidence and certainty follow the accepted
    /// weakest-certainty contract so mixed Exact+Conservative evidence with Exact certainty is
    /// rejected by construction.
    /// </summary>
    internal static CallbackBoundaryFact CreateCallbackBoundaryFact(
        CallbackBoundaryId id,
        OperationId outerInvocationOperation,
        CallbackCardinality cardinality,
        CallbackTriggerKind trigger,
        OperationId? triggerCondition,
        CallbackCompletionKind completion,
        ImmutableArray<string> memberOperations,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
        => new(
            id,
            ServiceMethod,
            outerInvocationOperation,
            0,
            CallbackTargetKind.AnonymousFunction,
            null,
            CallbackTargetBodyOperation,
            ServiceMethod,
            CallbackContractInvokeOperation,
            cardinality,
            trigger,
            triggerCondition,
            completion,
            CallbackContractProvenance.SourceBody,
            memberOperations,
            evidence,
            certainty);

    /// <summary>
    /// One source-ordered sequence where an Add mutation precedes an interleaved CountAsync query and
    /// the save follows it. Wording and Mermaid must honor the single authoritative source order
    /// (Add, CountAsync, save) rather than grouping semantic kinds before sequence ordinals.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateInterleavedSourceOrderRequest()
    {
        var controllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.GadgetsController");
        var interfaceType = new SymbolId("symbol:v1:GetMeaning.Services.IGadgetService");
        var serviceType = new SymbolId("symbol:v1:GetMeaning.Services.GadgetService");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "GetMeaning.Controllers.GadgetsController"),
            CreateType(interfaceType, "GetMeaning.Services.IGadgetService"),
            CreateType(serviceType, "GetMeaning.Services.GadgetService"));
        var methods = ImmutableArray.Create(
            CreateMethod(ActionMethod, controllerType, "GetById"),
            CreateMethod(ConstructorMethod, controllerType, ".ctor"),
            CreateMethod(InterfaceMethod, interfaceType, "GetByIdAsync"),
            CreateMethod(ServiceMethod, serviceType, "GetByIdAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.Cha,
            ImmutableArray.Create(ServiceMethod),
            "source",
            IsComplete: true,
            [],
            [SourceEvidence("call-resolution")],
            CertaintyLevel.Exact);
        var callSites = ImmutableArray.Create(new CallSite(
            ServiceCallSiteId,
            ActionMethod,
            ServiceCallOperation,
            CallKind.Instance,
            InterfaceMethod,
            resolution,
            [SourceEvidence("call-site")],
            CertaintyLevel.Exact));
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [CreateActionFlow(ActionMethod)],
            new CallGraph([new CallGraphEdge(ActionMethod, ServiceCallSiteId, ServiceMethod)], callSites),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint");

        var entryPoint = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:GET-api-Gadgets"),
            Evidence = [SourceEvidence("entry-point")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = GetEntryPoint,
            RootMethod = ActionMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "api/Gadgets/{id}",
            OperationKey = "GET api/Gadgets/{id}",
        };
        var outcomeOk = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:Ok"),
            Evidence = [SourceEvidence("outcome-ok")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.Ok"),
            HelperKind = HttpOutcomeHelperKind.Ok,
            StatusCode = 200,
        };
        var query = new EntityFrameworkQueryFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:ef-query:CountAsync"),
            Evidence = [SourceEvidence("ef-query-count")],
            Certainty = CertaintyLevel.Exact,
            Method = ServiceMethod,
            Operation = new OperationId("operation:v1:CountAsync"),
            DbContextType = "GetMeaning.Data.GadgetDbContext",
            DbSetMemberType = "Microsoft.EntityFrameworkCore.DbSet<GetMeaning.Models.Gadget>",
            EntityType = "GetMeaning.Models.Gadget",
            Chain =
            [
                new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.CountAsync,
                    new OperationId("operation:v1:CountAsync"),
                    null),
            ],
            PredicateOperation = null,
            PredicateOperator = ComparisonOperatorKind.Equal,
        };
        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            [entryPoint, outcomeOk, query],
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            "semantic-test");

        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            [],
            [
                new EntityFrameworkMutationFact
                {
                    Id = new BehaviorFactId("behavior-fact:v1:ef-mutation:Add"),
                    Method = ServiceMethod,
                    Operation = new OperationId("operation:v1:Add"),
                    MutationKind = EntityFrameworkMutationKind.Add,
                    SequenceOrdinal = 0,
                    DbContextType = "GetMeaning.Data.GadgetDbContext",
                    EntityType = "GetMeaning.Models.Gadget",
                    Evidence = [SourceEvidence("ef-mutation-add")],
                    Certainty = CertaintyLevel.Exact,
                },
                new EntityFrameworkMutationFact
                {
                    Id = new BehaviorFactId("behavior-fact:v1:ef-mutation:Save"),
                    Method = ServiceMethod,
                    Operation = new OperationId("operation:v1:SaveChangesAsync"),
                    MutationKind = EntityFrameworkMutationKind.SaveChangesAsync,
                    SequenceOrdinal = 2,
                    DbContextType = "GetMeaning.Data.GadgetDbContext",
                    EntityType = string.Empty,
                    Evidence = [SourceEvidence("ef-mutation-save")],
                    Certainty = CertaintyLevel.Exact,
                },
            ],
            [
                new EfOperationSequenceFact(ServiceMethod, new OperationId("operation:v1:Add"), EfOperationSequenceKind.Mutation, 0),
                new EfOperationSequenceFact(ServiceMethod, new OperationId("operation:v1:CountAsync"), EfOperationSequenceKind.QueryTerminal, 1),
                new EfOperationSequenceFact(ServiceMethod, new OperationId("operation:v1:SaveChangesAsync"), EfOperationSequenceKind.Mutation, 2),
            ],
            [],
            "non-get-interleaved-source-order");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            new DependencyInjectionFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName),
                ],
                [
                    CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0),
                ],
                [],
                "di-test"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-test"),
            nonGet);
    }

    /// <summary>
    /// Two arms sharing one helper kind (StatusCode) with distinct compiler-proven outcome operations
    /// and distinct statuses. The builder must join each arm to its exact outcome operation; a helper
    /// kind is only a consistency check, so no arm fails closed with SC004.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateRepeatedHelperStatusRequest()
    {
        var controllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.GadgetsController");
        var interfaceType = new SymbolId("symbol:v1:GetMeaning.Services.IGadgetService");
        var serviceType = new SymbolId("symbol:v1:GetMeaning.Services.GadgetService");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "GetMeaning.Controllers.GadgetsController"),
            CreateType(interfaceType, "GetMeaning.Services.IGadgetService"),
            CreateType(serviceType, "GetMeaning.Services.GadgetService"));
        var methods = ImmutableArray.Create(
            CreateMethod(ActionMethod, controllerType, "GetById"),
            CreateMethod(ConstructorMethod, controllerType, ".ctor"),
            CreateMethod(InterfaceMethod, interfaceType, "GetByIdAsync"),
            CreateMethod(ServiceMethod, serviceType, "GetByIdAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.Cha,
            ImmutableArray.Create(ServiceMethod),
            "source",
            IsComplete: true,
            [],
            [SourceEvidence("call-resolution")],
            CertaintyLevel.Exact);
        var callSites = ImmutableArray.Create(new CallSite(
            ServiceCallSiteId,
            ActionMethod,
            ServiceCallOperation,
            CallKind.Instance,
            InterfaceMethod,
            resolution,
            [SourceEvidence("call-site")],
            CertaintyLevel.Exact));
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [CreateActionFlow(ActionMethod)],
            new CallGraph([new CallGraphEdge(ActionMethod, ServiceCallSiteId, ServiceMethod)], callSites),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint");

        var entryPoint = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:GET-api-Gadgets"),
            Evidence = [SourceEvidence("entry-point")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = GetEntryPoint,
            RootMethod = ActionMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "api/Gadgets/{id}",
            OperationKey = "GET api/Gadgets/{id}",
        };
        var outcome500 = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:StatusCode500"),
            Evidence = [SourceEvidence("outcome-500")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.Status500"),
            HelperKind = HttpOutcomeHelperKind.StatusCode,
            StatusCode = 500,
        };
        var outcome503 = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:StatusCode503"),
            Evidence = [SourceEvidence("outcome-503")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.Status503"),
            HelperKind = HttpOutcomeHelperKind.StatusCode,
            StatusCode = 503,
        };
        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            [entryPoint, outcome500, outcome503],
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            "semantic-test");

        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:Status500"),
                    ActionMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "default",
                    HttpOutcomeHelperKind.StatusCode,
                    new OperationId("operation:v1:outcome.Status500"),
                    null,
                    null,
                    [SourceEvidence("status-arm-500")],
                    CertaintyLevel.Exact),
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:Status503"),
                    ActionMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "Conflict",
                    HttpOutcomeHelperKind.StatusCode,
                    new OperationId("operation:v1:outcome.Status503"),
                    null,
                    null,
                        [SourceEvidence("status-arm-503")],
                        CertaintyLevel.Exact),
            ],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            "non-get-repeated-helper-status");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            new DependencyInjectionFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName),
                ],
                [
                    CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0),
                ],
                [],
                "di-test"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-test"),
            nonGet);
    }

    /// <summary>
    /// Exact StatusCode polarity partitions: a compiler-proven StatusCode(200) outcome is success,
    /// StatusCode(500) is failure, and an unsupported 3xx polarity must fail closed with SC004 and no
    /// outcome node. The helper kind is never the polarity source; the exact status code is.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateStatusCodePolarityRequest()
    {
        var controllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.GadgetsController");
        var interfaceType = new SymbolId("symbol:v1:GetMeaning.Services.IGadgetService");
        var serviceType = new SymbolId("symbol:v1:GetMeaning.Services.GadgetService");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "GetMeaning.Controllers.GadgetsController"),
            CreateType(interfaceType, "GetMeaning.Services.IGadgetService"),
            CreateType(serviceType, "GetMeaning.Services.GadgetService"));
        var methods = ImmutableArray.Create(
            CreateMethod(ActionMethod, controllerType, "GetById"),
            CreateMethod(ConstructorMethod, controllerType, ".ctor"),
            CreateMethod(InterfaceMethod, interfaceType, "GetByIdAsync"),
            CreateMethod(ServiceMethod, serviceType, "GetByIdAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.Cha,
            ImmutableArray.Create(ServiceMethod),
            "source",
            IsComplete: true,
            [],
            [SourceEvidence("call-resolution")],
            CertaintyLevel.Exact);
        var callSites = ImmutableArray.Create(new CallSite(
            ServiceCallSiteId,
            ActionMethod,
            ServiceCallOperation,
            CallKind.Instance,
            InterfaceMethod,
            resolution,
            [SourceEvidence("call-site")],
            CertaintyLevel.Exact));
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [CreateActionFlow(ActionMethod)],
            new CallGraph([new CallGraphEdge(ActionMethod, ServiceCallSiteId, ServiceMethod)], callSites),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint");

        var entryPoint = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:GET-api-Gadgets"),
            Evidence = [SourceEvidence("entry-point")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = GetEntryPoint,
            RootMethod = ActionMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "api/Gadgets/{id}",
            OperationKey = "GET api/Gadgets/{id}",
        };
        var outcome200 = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:StatusCode200"),
            Evidence = [SourceEvidence("outcome-200")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.Status200"),
            HelperKind = HttpOutcomeHelperKind.StatusCode,
            StatusCode = 200,
        };
        var outcome500 = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:StatusCode500"),
            Evidence = [SourceEvidence("outcome-500")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.Status500"),
            HelperKind = HttpOutcomeHelperKind.StatusCode,
            StatusCode = 500,
        };
        var outcome399 = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:StatusCode399"),
            Evidence = [SourceEvidence("outcome-399")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = ActionMethod,
            Operation = new OperationId("operation:v1:outcome.Status399"),
            HelperKind = HttpOutcomeHelperKind.StatusCode,
            StatusCode = 399,
        };
        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            [entryPoint, outcome200, outcome500, outcome399],
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            "semantic-test");

        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:Success"),
                    ActionMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "default",
                    HttpOutcomeHelperKind.StatusCode,
                    new OperationId("operation:v1:outcome.Status200"),
                    null,
                    null,
                    [SourceEvidence("status-arm-200")],
                    CertaintyLevel.Exact),
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:Failure"),
                    ActionMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "Conflict",
                    HttpOutcomeHelperKind.StatusCode,
                    new OperationId("operation:v1:outcome.Status500"),
                    null,
                    null,
                    [SourceEvidence("status-arm-500")],
                    CertaintyLevel.Exact),
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:UnsupportedPolarity"),
                    ActionMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "ValidationError",
                    HttpOutcomeHelperKind.StatusCode,
                    new OperationId("operation:v1:outcome.Status399"),
                    null,
                    null,
                    [SourceEvidence("status-arm-399")],
                    CertaintyLevel.Exact),
            ],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            "non-get-status-code-polarity");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            new DependencyInjectionFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName),
                ],
                [
                    CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0),
                ],
                [],
                "di-test"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-test"),
            nonGet);
    }

    /// <summary>
    /// A CreatedAtAction arm whose action name matches only a Get entry point in a competing
    /// controller. The created link must never resolve to the unrelated controller route; it fails
    /// closed with SC010 because the arm's controller has no matching Get entry point.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateCreatedLinkCompetitionRequest()
    {
        var controllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.GadgetsController");
        var otherControllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.OtherGadgetsController");
        var interfaceType = new SymbolId("symbol:v1:GetMeaning.Services.IGadgetService");
        var serviceType = new SymbolId("symbol:v1:GetMeaning.Services.GadgetService");
        var otherServiceType = new SymbolId("symbol:v1:GetMeaning.Services.MemoryGadgetService");
        var reserveMethod = new MethodId("method:v1:GetMeaning.Controllers.GadgetsController.Reserve");
        var otherGetById = new MethodId("method:v1:GetMeaning.Controllers.OtherGadgetsController.GetById");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "GetMeaning.Controllers.GadgetsController"),
            CreateType(otherControllerType, "GetMeaning.Controllers.OtherGadgetsController"),
            CreateType(interfaceType, "GetMeaning.Services.IGadgetService"),
            CreateType(serviceType, "GetMeaning.Services.GadgetService"),
            CreateType(otherServiceType, "GetMeaning.Services.MemoryGadgetService"));
        var methods = ImmutableArray.Create(
            CreateMethod(reserveMethod, controllerType, "Reserve"),
            CreateMethod(otherGetById, otherControllerType, "GetById"),
            CreateMethod(ConstructorMethod, controllerType, ".ctor"),
            CreateMethod(InterfaceMethod, interfaceType, "GetByIdAsync"),
            CreateMethod(ServiceMethod, serviceType, "GetByIdAsync"),
            CreateMethod(OtherServiceMethod, otherServiceType, "GetByIdAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.Cha,
            ImmutableArray.Create(ServiceMethod),
            "source",
            IsComplete: true,
            [],
            [SourceEvidence("call-resolution")],
            CertaintyLevel.Exact);
        var reserveCallSite = new CallSite(
            new CallSiteId("call-site:v1:Reserve"),
            reserveMethod,
            new OperationId("operation:v1:call.ReserveAsync"),
            CallKind.Instance,
            InterfaceMethod,
            resolution,
            [SourceEvidence("call-site-reserve")],
            CertaintyLevel.Exact);
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [CreateActionFlow(reserveMethod)],
            new CallGraph([new CallGraphEdge(reserveMethod, reserveCallSite.Id, ServiceMethod)], [reserveCallSite]),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint");

        var postEntry = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:POST-api-Widgets"),
            Evidence = [SourceEvidence("entry-point-post")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:POST-api-Widgets"),
            RootMethod = reserveMethod,
            HttpMethod = HttpMethodKind.Post,
            CanonicalRoute = "api/Widgets",
            OperationKey = "POST api/Widgets",
        };
        var otherGetEntry = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:GET-api-Other-id"),
            Evidence = [SourceEvidence("entry-point-other-get")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:GET-api-Other-id"),
            RootMethod = otherGetById,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "api/Other/{id}",
            OperationKey = "GET api/Other/{id}",
        };
        var outcomeCreated = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:Created"),
            Evidence = [SourceEvidence("outcome-created")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.Created"),
            HelperKind = HttpOutcomeHelperKind.CreatedAtAction,
            StatusCode = 201,
        };
        var outcomeNotFound = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:NotFound"),
            Evidence = [SourceEvidence("outcome-not-found")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.NotFound"),
            HelperKind = HttpOutcomeHelperKind.NotFound,
            StatusCode = 404,
        };
        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            [postEntry, otherGetEntry, outcomeCreated, outcomeNotFound],
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            "semantic-test");

        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:Created"),
                    reserveMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "default",
                    HttpOutcomeHelperKind.CreatedAtAction,
                    new OperationId("operation:v1:outcome.Created"),
                    "GetById",
                    new MethodId("method:v1:GetMeaning.Controllers.GadgetsController.GetById"),
                    [SourceEvidence("status-arm-created")],
                    CertaintyLevel.Exact),
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:NotFound"),
                    reserveMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "NotFound",
                    HttpOutcomeHelperKind.NotFound,
                    new OperationId("operation:v1:outcome.NotFound"),
                    null,
                    null,
                        [SourceEvidence("status-arm-not-found")],
                        CertaintyLevel.Exact),
            ],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            "non-get-created-link-competition");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            new DependencyInjectionFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName),
                ],
                [
                    CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0),
                ],
                [],
                "di-test"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-test"),
            nonGet);
    }

    /// <summary>
    /// The uncovered direct-terminal shape: the action switches a failure status to the exact failure
    /// outcomes (404/409/400/500) and then, on the success path, returns a direct CreatedAtAction
    /// whose HTTP 201 call sits OUTSIDE every switch arm. The builder must retain the 201 outcome with
    /// the compiler-bound GET link even though no status arm references the created invocation, and it
    /// must never invent a synthetic <c>success</c> status arm for that direct call.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateDirectTerminalCreatedAtActionRequest()
    {
        var controllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.GadgetsController");
        var interfaceType = new SymbolId("symbol:v1:GetMeaning.Services.IGadgetService");
        var serviceType = new SymbolId("symbol:v1:GetMeaning.Services.GadgetService");
        var reserveMethod = new MethodId("method:v1:GetMeaning.Controllers.GadgetsController.Reserve");
        var getByIdMethod = new MethodId("method:v1:GetMeaning.Controllers.GadgetsController.GetById");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "GetMeaning.Controllers.GadgetsController"),
            CreateType(interfaceType, "GetMeaning.Services.IGadgetService"),
            CreateType(serviceType, "GetMeaning.Services.GadgetService"));
        var methods = ImmutableArray.Create(
            CreateMethod(reserveMethod, controllerType, "Reserve"),
            CreateMethod(getByIdMethod, controllerType, "GetById"),
            CreateMethod(ConstructorMethod, controllerType, ".ctor"),
            CreateMethod(InterfaceMethod, interfaceType, "GetByIdAsync"),
            CreateMethod(ServiceMethod, serviceType, "GetByIdAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.Cha,
            ImmutableArray.Create(ServiceMethod),
            "source",
            IsComplete: true,
            [],
            [SourceEvidence("call-resolution")],
            CertaintyLevel.Exact);
        var reserveCallSite = new CallSite(
            new CallSiteId("call-site:v1:Reserve"),
            reserveMethod,
            new OperationId("operation:v1:call.ReserveAsync"),
            CallKind.Instance,
            InterfaceMethod,
            resolution,
            [SourceEvidence("call-site-reserve")],
            CertaintyLevel.Exact);
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [CreateActionFlow(reserveMethod)],
            new CallGraph([new CallGraphEdge(reserveMethod, reserveCallSite.Id, ServiceMethod)], [reserveCallSite]),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint");

        var postEntry = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:POST-api-Widgets-reservations"),
            Evidence = [SourceEvidence("entry-point-post")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:POST-api-Widgets-reservations"),
            RootMethod = reserveMethod,
            HttpMethod = HttpMethodKind.Post,
            CanonicalRoute = "api/Widgets/{id}/reservations",
            OperationKey = "POST api/Widgets/{id}/reservations",
        };
        var getEntry = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:GET-api-Widgets-id"),
            Evidence = [SourceEvidence("entry-point-get")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:GET-api-Widgets-id"),
            RootMethod = getByIdMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "api/Widgets/{id}",
            OperationKey = "GET api/Widgets/{id}",
        };
        var outcomeNotFound = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:NotFound"),
            Evidence = [SourceEvidence("outcome-not-found")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.NotFound"),
            HelperKind = HttpOutcomeHelperKind.NotFound,
            StatusCode = 404,
        };
        var outcomeConflict = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:Conflict"),
            Evidence = [SourceEvidence("outcome-conflict")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.Conflict"),
            HelperKind = HttpOutcomeHelperKind.Conflict,
            StatusCode = 409,
        };
        var outcomeBadRequest = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:BadRequest"),
            Evidence = [SourceEvidence("outcome-bad-request")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.BadRequest"),
            HelperKind = HttpOutcomeHelperKind.BadRequest,
            StatusCode = 400,
        };
        var outcomeStatusCode500 = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:StatusCode500"),
            Evidence = [SourceEvidence("outcome-status-500")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.Status500"),
            HelperKind = HttpOutcomeHelperKind.StatusCode,
            StatusCode = 500,
        };
        // The direct success-path CreatedAtAction: no status arm references this invocation; the
        // builder must retain the exact HTTP 201 plus the compiler-bound GET target on its own.
        var outcomeCreated = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:Created"),
            Evidence = [SourceEvidence("outcome-created")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.Created"),
            HelperKind = HttpOutcomeHelperKind.CreatedAtAction,
            StatusCode = 201,
        };
        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            [postEntry, getEntry, outcomeNotFound, outcomeConflict, outcomeBadRequest, outcomeStatusCode500, outcomeCreated],
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            "semantic-test");

        // The compiler-proven direct success-path CreatedAtAction companion: the exact invocation
        // operation identity plus the compiler-bound GET target; the builder joins it only when the
        // method already has admitted status arms and the operation is not carried by any arm.
        var createdTerminal = new DirectTerminalOutcomeFact(
            new SemanticFactId("semantic-fact:v1:direct-terminal:Created"),
            reserveMethod,
            new OperationId("operation:v1:outcome.Created"),
            HttpOutcomeHelperKind.CreatedAtAction,
            "GetById",
            getByIdMethod,
            5,
            [SourceEvidence("direct-terminal-created")],
            CertaintyLevel.Exact);

        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:NotFound"),
                    reserveMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "NotFound",
                    HttpOutcomeHelperKind.NotFound,
                    new OperationId("operation:v1:outcome.NotFound"),
                    null,
                    null,
                    [SourceEvidence("status-arm-not-found")],
                    CertaintyLevel.Exact),
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:Conflict"),
                    reserveMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "Conflict",
                    HttpOutcomeHelperKind.Conflict,
                    new OperationId("operation:v1:outcome.Conflict"),
                    null,
                    null,
                    [SourceEvidence("status-arm-conflict")],
                    CertaintyLevel.Exact),
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:ValidationError"),
                    reserveMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "ValidationError",
                    HttpOutcomeHelperKind.BadRequest,
                    new OperationId("operation:v1:outcome.BadRequest"),
                    null,
                    null,
                    [SourceEvidence("status-arm-validation-error")],
                    CertaintyLevel.Exact),
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:Default"),
                    reserveMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "default",
                    HttpOutcomeHelperKind.StatusCode,
                    new OperationId("operation:v1:outcome.Status500"),
                    null,
                    null,
                    [SourceEvidence("status-arm-default")],
                    CertaintyLevel.Exact),
            ],
            [createdTerminal],
            [],
            [],
            [],
            [],
            [],
            [],
            "non-get-direct-terminal-created");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            new DependencyInjectionFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName),
                ],
                [
                    CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0),
                ],
                [],
                "di-test"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-test"),
            nonGet);
    }

    /// <summary>
    /// The accepted CreatedAtAction-inside-a-switch shape (the generic FourFlows Reserve): the default
    /// arm returns CreatedAtAction targeting a Get action of the SAME controller, so the created link
    /// resolves exactly once. Guards that the direct-terminal join never duplicates the switch-arm
    /// outcome.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateCreatedAtActionSwitchRequest()
    {
        var controllerType = new SymbolId("symbol:v1:GetMeaning.Controllers.GadgetsController");
        var interfaceType = new SymbolId("symbol:v1:GetMeaning.Services.IGadgetService");
        var serviceType = new SymbolId("symbol:v1:GetMeaning.Services.GadgetService");
        var reserveMethod = new MethodId("method:v1:GetMeaning.Controllers.GadgetsController.Reserve");
        var getByIdMethod = new MethodId("method:v1:GetMeaning.Controllers.GadgetsController.GetById");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "GetMeaning.Controllers.GadgetsController"),
            CreateType(interfaceType, "GetMeaning.Services.IGadgetService"),
            CreateType(serviceType, "GetMeaning.Services.GadgetService"));
        var methods = ImmutableArray.Create(
            CreateMethod(reserveMethod, controllerType, "Reserve"),
            CreateMethod(getByIdMethod, controllerType, "GetById"),
            CreateMethod(ConstructorMethod, controllerType, ".ctor"),
            CreateMethod(InterfaceMethod, interfaceType, "GetByIdAsync"),
            CreateMethod(ServiceMethod, serviceType, "GetByIdAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.Cha,
            ImmutableArray.Create(ServiceMethod),
            "source",
            IsComplete: true,
            [],
            [SourceEvidence("call-resolution")],
            CertaintyLevel.Exact);
        var reserveCallSite = new CallSite(
            new CallSiteId("call-site:v1:Reserve"),
            reserveMethod,
            new OperationId("operation:v1:call.ReserveAsync"),
            CallKind.Instance,
            InterfaceMethod,
            resolution,
            [SourceEvidence("call-site-reserve")],
            CertaintyLevel.Exact);
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [CreateActionFlow(reserveMethod)],
            new CallGraph([new CallGraphEdge(reserveMethod, reserveCallSite.Id, ServiceMethod)], [reserveCallSite]),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint");

        var postEntry = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:POST-api-Widgets-reservations"),
            Evidence = [SourceEvidence("entry-point-post")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:POST-api-Widgets-reservations"),
            RootMethod = reserveMethod,
            HttpMethod = HttpMethodKind.Post,
            CanonicalRoute = "api/Widgets/{id}/reservations",
            OperationKey = "POST api/Widgets/{id}/reservations",
        };
        var getEntry = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:GET-api-Widgets-id"),
            Evidence = [SourceEvidence("entry-point-get")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:GET-api-Widgets-id"),
            RootMethod = getByIdMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "api/Widgets/{id}",
            OperationKey = "GET api/Widgets/{id}",
        };
        var outcomeCreated = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:Created"),
            Evidence = [SourceEvidence("outcome-created")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.Created"),
            HelperKind = HttpOutcomeHelperKind.CreatedAtAction,
            StatusCode = 201,
        };
        var outcomeNotFound = new HttpDirectOutcomeFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:outcome:NotFound"),
            Evidence = [SourceEvidence("outcome-not-found")],
            Certainty = CertaintyLevel.Exact,
            RootMethod = reserveMethod,
            Operation = new OperationId("operation:v1:outcome.NotFound"),
            HelperKind = HttpOutcomeHelperKind.NotFound,
            StatusCode = 404,
        };
        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            [postEntry, getEntry, outcomeCreated, outcomeNotFound],
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            "semantic-test");

        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:Created"),
                    reserveMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "default",
                    HttpOutcomeHelperKind.CreatedAtAction,
                    new OperationId("operation:v1:outcome.Created"),
                    "GetById",
                    getByIdMethod,
                    [SourceEvidence("status-arm-created")],
                    CertaintyLevel.Exact),
                new StatusSwitchArmFact(
                    new SemanticFactId("semantic-fact:v1:status:NotFound"),
                    reserveMethod,
                    new OperationId("operation:v1:switch.Status"),
                    "GetMeaning.Services.GadgetResultStatus",
                    "NotFound",
                    HttpOutcomeHelperKind.NotFound,
                    new OperationId("operation:v1:outcome.NotFound"),
                    null,
                    null,
                        [SourceEvidence("status-arm-not-found")],
                        CertaintyLevel.Exact),
            ],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            "non-get-created-at-action-switch");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            new DependencyInjectionFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    CreateRegistration(ServiceRegistrationId, ServiceMethod, ServiceTypeName, ImplementationTypeName),
                ],
                [
                    CreateBinding(ConstructorMethod, ServiceRegistrationId, ServiceTypeName, ImplementationTypeName, 0),
                ],
                [],
                "di-test"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-test"),
            nonGet);
    }

    // ---- accepted contract frozen DecisionTopology fixture vocabulary (Scenario topology claims 6-12) ----
    // The service flow below is the Roslyn-neutral Method Flow the architecture decision repair produces for the
    // frozen WorkItemService.ProcessAsync: an absent decision whose true arm holds the Not Found
    // factory and its represented return terminal, a locked decision with the same shape, and a
    // continuing success path (state assignment, save, success factory, terminal) guarded by both
    // decisions on the false arms.

    internal static readonly MethodId WorkItemActionMethod = new("method:v1:AdvancedAnalysis.DecisionTopology.Controllers.WorkItemsController.Process");
    internal static readonly MethodId WorkItemConstructorMethod = new("method:v1:AdvancedAnalysis.DecisionTopology.Controllers.WorkItemsController..ctor");
    internal static readonly MethodId WorkItemServiceMethod = new("method:v1:AdvancedAnalysis.DecisionTopology.Services.WorkItemService.ProcessAsync");
    internal static readonly OperationId WorkItemCallOperation = new("operation:v1:workitem:call.ProcessAsync");
    internal static readonly OperationId WorkItemQueryOperation = new("operation:v1:workitem:SingleOrDefaultAsync");
    internal static readonly OperationId WorkItemQueryPredicateOperation = new("operation:v1:workitem:predicate.IdEquals");
    internal static readonly OperationId WorkItemAbsentCondition = new("operation:v1:workitem:item-is-null");
    internal static readonly OperationId WorkItemLockedCondition = new("operation:v1:workitem:item-is-locked");
    internal static readonly OperationId WorkItemNotFoundFactoryOperation = new("operation:v1:workitem:factory.NotFound");
    internal static readonly OperationId WorkItemConflictFactoryOperation = new("operation:v1:workitem:factory.Conflict");
    internal static readonly OperationId WorkItemSuccessFactoryOperation = new("operation:v1:workitem:factory.Success");
    internal static readonly OperationId WorkItemStateAssignmentOperation = new("operation:v1:workitem:assign.Status");
    internal static readonly OperationId WorkItemSaveOperation = new("operation:v1:workitem:SaveChangesAsync");
    internal static readonly OperationId WorkItemMissingAnchorOperation = new("operation:v1:workitem:query.missing-anchor");
    internal static readonly OperationId WorkItemLoopCondition = new("operation:v1:workitem:has-more-tickets");
    internal static readonly OperationId WorkItemAddOperation = new("operation:v1:workitem:Add");
    internal static readonly EntryPointId WorkItemEntryPoint = new("entry-point:v1:GET-api-WorkItems-id");
    internal static readonly EntryPointId WorkItemRelocatedEntryPoint = new("entry-point:v1:GET-api-WorkItems-v2-id");

    // Evidence artifacts shared by the review-finding adversarial fixtures. The degraded terminal
    // evidence fixture proves terminal/rejoin facts aggregate decision, traversed-edge, and boundary
    // evidence and degrade to the weakest supported certainty. The loop fixtures prove the exact
    // own-header terminal carries both the LoopNode fact artifact and the LoopBack edge artifact and
    // degrades to the weakest contributor.
    internal const string WorkItemDecisionEvidence = "workitem-decision";
    internal const string WorkItemBoundaryEdgeEvidence = "workitem-boundary-edge";
    internal const string WorkItemBoundaryTerminalEvidence = "workitem-boundary-return";
    internal const string WorkItemLoopEvidence = "workitem-loop";
    internal const string WorkItemLoopBackEvidence = "workitem-loop-back";

    internal static ScenarioAnalysisRequest CreateWorkItemTopologyRequest(bool reverseConstruction = false, EntryPointId? entryPointId = null)
        => CreateWorkItemTopologyRequestCore(reverseConstruction: reverseConstruction, entryPointId: entryPointId);

    /// <summary>
    /// The work-item request with one material scenario node that has NO exact eligible Method Flow
    /// anchor. The topology builder must keep the node visible and emit SC011, withholding any arm
    /// membership for it.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateMissingAnchorTopologyRequest()
        => CreateWorkItemTopologyRequestCore(missingAnchor: true);

    /// <summary>
    /// The work-item request with the continuing save directly controlled by BOTH semantic arms of
    /// BOTH decisions. The topology builder must emit SC012 for every same-decision conflict and
    /// withhold the conflicting memberships so no save membership survives; it never selects one arm.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateDualPolarityConflictRequest()
        => CreateWorkItemTopologyRequestCore(dualPolarityConflict: true);

    /// <summary>
    /// The work-item request with loop-back and exception-region topology around the locked decision.
    /// The topology builder must emit SC013 and never claim an exact terminal/rejoin classification
    /// for that decision while keeping known nodes visible.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateUnsupportedTopologyRequest()
        => CreateWorkItemTopologyRequestCore(unsupportedTopology: true);

    internal static ScenarioAnalysisRequest CreatePlainTryTopologyRequest()
        => CreateWorkItemTopologyRequestCore(plainTry: true);

    internal static ScenarioAnalysisRequest CreateExceptionRegionTopologyRequest(string regionKind)
        => CreateWorkItemTopologyRequestCore(exceptionRegionKind: regionKind);

    internal static ScenarioAnalysisRequest CreateFinallyTargetTopologyRequest()
        => CreateWorkItemTopologyRequestCore(finallyTarget: true);

    /// <summary>
    /// The work-item request with an exact natural loop (accepted CT-4 design item 1):
    /// one DecisionFlowNode is the exact <see cref="LoopNode.Header"/> of an existing LoopNode, the
    /// lowered header and body sit inside a Try region, and the body's LoopBack edge targets that same
    /// header. The body Add mutation carries a direct true-arm dependence. The topology builder must
    /// classify both normal arms (body iteration boundary and loop exit rejoin) without SC013 and
    /// retain the body interaction membership; it must never treat the enclosing Try region as an
    /// exception decision or the same-header LoopBack as an unsupported edge.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateExactOwnHeaderLoopRequest()
        => CreateOwnHeaderLoopRequestCore("valid");

    /// <summary>
    /// Malformed or unsupported variants of the exact-own-header loop request (regressions F2/F3).
    /// "foreign-body-source" keeps the same edges but records a LoopNode whose Body does NOT contain
    /// the actual LoopBack source; "mismatched-exit" records a LoopNode whose Exits do NOT contain the
    /// actual normal exit; "catch"/"filter"/"finally" keep the exact own-header LoopBack but place the
    /// header and body in a genuine Catch/Filter/Finally region instead of the compiler-lowered Try.
    /// Every variant must fail closed with SC013 rather than classify the arms as represented
    /// iteration, because the loop snapshot is incomplete/foreign or the header is a real exception
    /// decision.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateMalformedOwnHeaderLoopRequest(string variant)
        => CreateOwnHeaderLoopRequestCore(variant);

    private static ScenarioAnalysisRequest CreateOwnHeaderLoopRequestCore(string variant)
    {
        var controllerType = new SymbolId("symbol:v1:AdvancedAnalysis.DecisionTopology.Controllers.WorkItemsController");
        var serviceType = new SymbolId("symbol:v1:AdvancedAnalysis.DecisionTopology.Services.WorkItemService");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "AdvancedAnalysis.DecisionTopology.Controllers.WorkItemsController"),
            CreateType(serviceType, "AdvancedAnalysis.DecisionTopology.Services.WorkItemService"));
        var methods = ImmutableArray.Create(
            CreateMethod(WorkItemActionMethod, controllerType, "Process"),
            CreateMethod(WorkItemConstructorMethod, controllerType, ".ctor"),
            CreateMethod(WorkItemServiceMethod, serviceType, "ProcessAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.DirectExact,
            ImmutableArray.Create(WorkItemServiceMethod),
            "source",
            IsComplete: true,
            [],
            [SourceEvidence("workitem-call-resolution")],
            CertaintyLevel.Exact);
        var callSites = ImmutableArray.Create(new CallSite(
            new CallSiteId("call-site:v1:WorkItems.Process"),
            WorkItemActionMethod,
            WorkItemCallOperation,
            CallKind.Instance,
            WorkItemServiceMethod,
            resolution,
            [SourceEvidence("workitem-call-site")],
            CertaintyLevel.Exact));
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [CreateActionFlow(WorkItemActionMethod), CreateExactOwnHeaderLoopServiceFlow(variant)],
            new CallGraph([new CallGraphEdge(WorkItemActionMethod, callSites[0].Id, WorkItemServiceMethod)], callSites),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint-workitem");

        var entryPoint = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:entry-point:GET-api-WorkItems-id"),
            Evidence = [SourceEvidence("workitem-entry-point")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = WorkItemEntryPoint,
            RootMethod = WorkItemActionMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "api/WorkItems/{id}",
            OperationKey = "GET api/WorkItems/{id}",
        };
        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            [entryPoint],
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var addMutation = new EntityFrameworkMutationFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:workitem:add-ticket"),
            Method = WorkItemServiceMethod,
            Operation = WorkItemAddOperation,
            MutationKind = EntityFrameworkMutationKind.Add,
            SequenceOrdinal = 1,
            DbContextType = "AdvancedAnalysis.DecisionTopology.Data.WorkDbContext",
            EntityType = "AdvancedAnalysis.DecisionTopology.Models.Ticket",
            Evidence = [SourceEvidence("workitem-add")],
            Certainty = CertaintyLevel.Exact,
        };
        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            [],
            [addMutation],
            [new EfOperationSequenceFact(WorkItemServiceMethod, WorkItemAddOperation, EfOperationSequenceKind.Mutation, 1)],
            [],
            "non-get-workitem-loop");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            new SemanticFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], [], "semantic-workitem"),
            new DependencyInjectionFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    CreateRegistration(
                        new SemanticFactId("semantic-fact:v1:workitem:registration"),
                        WorkItemServiceMethod,
                        "AdvancedAnalysis.DecisionTopology.Services.WorkItemService",
                        "AdvancedAnalysis.DecisionTopology.Services.WorkItemService"),
                ],
                [
                    CreateBinding(
                        WorkItemConstructorMethod,
                        new SemanticFactId("semantic-fact:v1:workitem:registration"),
                        "AdvancedAnalysis.DecisionTopology.Services.WorkItemService",
                        "AdvancedAnalysis.DecisionTopology.Services.WorkItemService",
                        0),
                ],
                [],
                "di-workitem"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-workitem"),
            nonGet);
    }

    /// <summary>
    /// The work-item request with the continuing save directly controlled by BOTH semantic arms of the
    /// absent decision while the locked decision's false membership stays valid. SC012 must withhold
    /// only the conflicting absent decision and retain the valid locked membership.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateScopedConflictTopologyRequest()
        => CreateWorkItemTopologyRequestCore(scopedConflict: true);

    /// <summary>
    /// The work-item request with the continuing save controlled by BOTH arms of BOTH decisions. Every
    /// same-decision conflict must be reported as its own SC012 in deterministic order.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateMultipleConflictTopologyRequest()
        => CreateWorkItemTopologyRequestCore(multipleConflict: true);

    /// <summary>
    /// The work-item request with a mixed terminal/rejoin (or operation-derived duplicate-return)
    /// boundary on the absent decision's true arm. The arm's reachable subgraph contains a represented
    /// return sink and a rejoin boundary; flipping the boundary edge ordinals proves the exact
    /// classification must not depend on traversal/edge input order. Complete-arm traversal must fail
    /// closed with SC013 and classify the arm Unknown.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateMixedBoundaryTopologyRequest(bool reverseBoundaryEdges = false, bool duplicateReturn = false)
        => CreateWorkItemTopologyRequestCore(customServiceFlow: CreateBoundaryTopologyServiceFlow(reverseBoundaryEdges, duplicateReturn));

    /// <summary>
    /// The work-item request with one operation identity carried by TWO eligible anchors (invocation
    /// plus await, or two invocations) whose control memberships disagree (or agree when
    /// <paramref name="agreeing"/>). Disagreeing anchors must withhold membership and emit SC011;
    /// agreeing anchors must retain placement.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateDuplicateAnchorTopologyRequest(bool duplicateInvocation = false, bool agreeing = false)
        => CreateWorkItemTopologyRequestCore(customServiceFlow: CreateDuplicateAnchorServiceFlow(duplicateInvocation, agreeing));

    /// <summary>
    /// The work-item request whose controller action flow guards the exact service-call operation
    /// anchor. The material service-call scenario node must carry the exact call operation and be
    /// scoped to the guarding decision's arm.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateServiceCallScopedRequest()
        => CreateWorkItemTopologyRequestCore(customActionFlow: CreateServiceCallScopedActionFlow());

    /// <summary>
    /// The work-item request with a supported terminating true arm whose traversed edge and boundary
    /// return carry Conservative evidence while the decision is Exact. Terminal/rejoin facts must
    /// aggregate decision, traversed-edge, and boundary evidence and degrade to Conservative.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateDegradedTerminalEvidenceRequest()
        => CreateWorkItemTopologyRequestCore(customServiceFlow: CreateDegradedTerminalEvidenceFlow());

    /// <summary>
    /// The work-item request with three decisions whose arms and memberships exercise the frozen
    /// canonical order: controlling flow-node identity, semantic polarity (false before true), then
    /// controlled scenario-node identity — never hashed decision/arm identity order.
    /// </summary>
    internal static ScenarioAnalysisRequest CreateCanonicalOrderTopologyRequest()
        => CreateWorkItemTopologyRequestCore(customServiceFlow: CreateCanonicalOrderServiceFlow());

    private static ScenarioAnalysisRequest CreateWorkItemTopologyRequestCore(
        bool reverseConstruction = false,
        bool missingAnchor = false,
        bool dualPolarityConflict = false,
        bool unsupportedTopology = false,
        bool scopedConflict = false,
        bool multipleConflict = false,
        bool plainTry = false,
        string? exceptionRegionKind = null,
        bool finallyTarget = false,
        EntryPointId? entryPointId = null,
        MethodFlowSnapshot? customActionFlow = null,
        MethodFlowSnapshot? customServiceFlow = null)
    {
        var controllerType = new SymbolId("symbol:v1:AdvancedAnalysis.DecisionTopology.Controllers.WorkItemsController");
        var serviceType = new SymbolId("symbol:v1:AdvancedAnalysis.DecisionTopology.Services.WorkItemService");
        var types = ImmutableArray.Create(
            CreateType(controllerType, "AdvancedAnalysis.DecisionTopology.Controllers.WorkItemsController"),
            CreateType(serviceType, "AdvancedAnalysis.DecisionTopology.Services.WorkItemService"));
        var methods = ImmutableArray.Create(
            CreateMethod(WorkItemActionMethod, controllerType, "Process"),
            CreateMethod(WorkItemConstructorMethod, controllerType, ".ctor"),
            CreateMethod(WorkItemServiceMethod, serviceType, "ProcessAsync"));
        var index = CreateIndex(types, methods);

        var resolution = new CallTargetResolution(
            CallResolutionKind.DirectExact,
            ImmutableArray.Create(WorkItemServiceMethod),
            "source",
            IsComplete: true,
            [],
            [SourceEvidence("workitem-call-resolution")],
            CertaintyLevel.Exact);
        var callSites = ImmutableArray.Create(new CallSite(
            new CallSiteId("call-site:v1:WorkItems.Process"),
            WorkItemActionMethod,
            WorkItemCallOperation,
            CallKind.Instance,
            WorkItemServiceMethod,
            resolution,
            [SourceEvidence("workitem-call-site")],
            CertaintyLevel.Exact));
        var serviceFlow = customServiceFlow ?? CreateWorkItemServiceFlow(
            reverseConstruction,
            dualPolarityConflict,
            unsupportedTopology,
            scopedConflict,
            multipleConflict,
            plainTry ? "Try" : exceptionRegionKind,
            finallyTarget);
        var actionFlow = customActionFlow ?? CreateActionFlow(WorkItemActionMethod);
        var behavior = new BehaviorSnapshot(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [actionFlow, serviceFlow],
            new CallGraph([new CallGraphEdge(WorkItemActionMethod, callSites[0].Id, WorkItemServiceMethod)], callSites),
            new RtaFoundation([], HasExplicitRoots: true),
            [],
            [],
            "behavior-fingerprint-workitem");

        var effectiveEntryPoint = entryPointId ?? WorkItemEntryPoint;
        var entryPoint = new HttpEntryPointFact
        {
            Id = new BehaviorFactId(entryPointId is null
                ? "behavior-fact:v1:entry-point:GET-api-WorkItems-id"
                : $"behavior-fact:v1:entry-point:{effectiveEntryPoint.Value}"),
            Evidence = [SourceEvidence("workitem-entry-point")],
            Certainty = CertaintyLevel.Exact,
            EntryPointId = effectiveEntryPoint,
            RootMethod = WorkItemActionMethod,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = entryPointId is null ? "api/WorkItems/{id}" : "api/WorkItems-v2/{id}",
            OperationKey = entryPointId is null ? "GET api/WorkItems/{id}" : "GET api/WorkItems-v2/{id}",
        };
        var query = new EntityFrameworkQueryFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:workitem:query"),
            Evidence = [SourceEvidence("workitem-ef-query")],
            Certainty = CertaintyLevel.Exact,
            Method = WorkItemServiceMethod,
            Operation = missingAnchor ? WorkItemMissingAnchorOperation : WorkItemQueryOperation,
            DbContextType = "AdvancedAnalysis.DecisionTopology.Data.WorkDbContext",
            DbSetMemberType = "Microsoft.EntityFrameworkCore.DbSet<AdvancedAnalysis.DecisionTopology.Models.WorkItem>",
            EntityType = "AdvancedAnalysis.DecisionTopology.Models.WorkItem",
            Chain =
            [
                new EntityFrameworkQueryChainItem(
                    EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync,
                    WorkItemQueryOperation,
                    null),
            ],
            PredicateOperation = WorkItemQueryPredicateOperation,
            PredicateOperator = ComparisonOperatorKind.Equal,
        };
        var frameworkFacts = new List<BehaviorFact> { entryPoint, query };
        if (reverseConstruction)
        {
            frameworkFacts.Reverse();
        }

        var framework = new FrameworkAnalysisResult(
            Recognized: true,
            frameworkFacts.ToImmutableArray(),
            [],
            [],
            [],
            [],
            [new FrameworkModelDescriptor("seqdoc.entityframework.queries", "1.0.0", "test", 1)],
            Profile.Id,
            index.IndexFingerprint);

        var semanticFacts = new SemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [],
            [],
            "semantic-workitem");

        var stateAssignment = new StateAssignmentSemanticFact(
            new SemanticFactId("semantic-fact:v1:workitem:state:Status"),
            WorkItemServiceMethod,
            WorkItemStateAssignmentOperation,
            "AdvancedAnalysis.DecisionTopology.Models.WorkItem.Status",
            "AdvancedAnalysis.DecisionTopology.Models.WorkItemStatus",
            StateAssignmentValueKind.EnumConstant,
            "Processed",
            [SourceEvidence("workitem-state-assignment")],
            CertaintyLevel.Exact,
            1);
        var saveMutation = new EntityFrameworkMutationFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:workitem:save"),
            Method = WorkItemServiceMethod,
            Operation = WorkItemSaveOperation,
            MutationKind = EntityFrameworkMutationKind.SaveChangesAsync,
            SequenceOrdinal = 2,
            DbContextType = "AdvancedAnalysis.DecisionTopology.Data.WorkDbContext",
            EntityType = "AdvancedAnalysis.DecisionTopology.Models.WorkItem",
            Evidence = [SourceEvidence("workitem-save")],
            Certainty = CertaintyLevel.Exact,
        };
        var mutationFacts = new List<EntityFrameworkMutationFact> { saveMutation };
        var sequenceFacts = new List<EfOperationSequenceFact>
        {
            new(WorkItemServiceMethod, WorkItemQueryOperation, EfOperationSequenceKind.QueryTerminal, 0),
            new(WorkItemServiceMethod, WorkItemSaveOperation, EfOperationSequenceKind.Mutation, 2),
        };
        if (reverseConstruction)
        {
            mutationFacts.Reverse();
            sequenceFacts.Reverse();
        }

        var nonGet = new NonGetSemanticFactSet(
            1,
            "test",
            Profile,
            index.IndexFingerprint,
            [],
            [],
            [stateAssignment],
            [],
            [],
            mutationFacts.ToImmutableArray(),
            sequenceFacts.ToImmutableArray(),
            [],
            "non-get-workitem-topology");

        return new ScenarioAnalysisRequest(
            Profile,
            index,
            behavior,
            framework,
            semanticFacts,
            new DependencyInjectionFactSet(
                1,
                "test",
                Profile,
                index.IndexFingerprint,
                [
                    CreateRegistration(
                        new SemanticFactId("semantic-fact:v1:workitem:registration"),
                        WorkItemServiceMethod,
                        "AdvancedAnalysis.DecisionTopology.Services.WorkItemService",
                        "AdvancedAnalysis.DecisionTopology.Services.WorkItemService"),
                ],
                [
                    CreateBinding(
                        WorkItemConstructorMethod,
                        new SemanticFactId("semantic-fact:v1:workitem:registration"),
                        "AdvancedAnalysis.DecisionTopology.Services.WorkItemService",
                        "AdvancedAnalysis.DecisionTopology.Services.WorkItemService",
                        0),
                ],
                [],
                "di-workitem"),
            new StructuralResultFactSet(1, "test", Profile, index.IndexFingerprint, [], [], [], "structural-workitem"),
            nonGet);
    }

    /// <summary>
    /// The Method Flow the architecture decision repair must produce for the frozen WorkItemService.ProcessAsync:
    /// every eligible node of each controlled block (factory invocation AND represented return
    /// terminal) carries a direct dependence, the continuing path is guarded by both decisions on the
    /// false arms, and synthetic structural nodes are never controlled. Reversing the construction
    /// order of nodes, edges, dependences, and regions must not change the topology identity or its
    /// canonical order.
    /// </summary>
    private static MethodFlowSnapshot CreateWorkItemServiceFlow(
        bool reverse,
        bool dualPolarityConflict,
        bool unsupportedTopology,
        bool scopedConflict = false,
        bool multipleConflict = false,
        string? nestedRegionKind = null,
        bool finallyTarget = false)
    {
        var entry = new EntryFlowNode(
            WorkItemFlowNode("Entry", 0, "entry"),
            WorkItemServiceMethod,
            [],
            CertaintyLevel.Exact);
        var exit = new ExitFlowNode(
            WorkItemFlowNode("Exit", 99, "exit"),
            WorkItemServiceMethod,
            [],
            CertaintyLevel.Exact);
        var query = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 0, "operation"),
            WorkItemServiceMethod,
            WorkItemQueryOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        var queryAwait = new AwaitFlowNode(
            WorkItemFlowNode("Await", 0, "operation"),
            WorkItemServiceMethod,
            WorkItemQueryOperation,
            [],
            CertaintyLevel.Exact);
        var absent = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 1, "decision"),
            WorkItemServiceMethod,
            WorkItemAbsentCondition,
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var notFoundFactory = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 2, "operation"),
            WorkItemServiceMethod,
            WorkItemNotFoundFactoryOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: true,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        var notFoundTerminal = new ReturnFlowNode(
            WorkItemFlowNode("Return", 2, "terminal"),
            WorkItemServiceMethod,
            WorkItemNotFoundFactoryOperation,
            [],
            CertaintyLevel.Exact);
        var locked = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 3, "decision"),
            WorkItemServiceMethod,
            WorkItemLockedCondition,
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var conflictFactory = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 4, "operation"),
            WorkItemServiceMethod,
            WorkItemConflictFactoryOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: true,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        var conflictTerminal = new ReturnFlowNode(
            WorkItemFlowNode("Return", 4, "terminal"),
            WorkItemServiceMethod,
            WorkItemConflictFactoryOperation,
            [],
            CertaintyLevel.Exact);
        var state = new OperationFlowNode(
            WorkItemFlowNode("Operation", 6, "operation"),
            WorkItemServiceMethod,
            WorkItemStateAssignmentOperation,
            ExtractedOperationKind.Assignment,
            [],
            CertaintyLevel.Exact);
        var save = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 6, "operation"),
            WorkItemServiceMethod,
            WorkItemSaveOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        var saveAwait = new AwaitFlowNode(
            WorkItemFlowNode("Await", 6, "operation"),
            WorkItemServiceMethod,
            WorkItemSaveOperation,
            [],
            CertaintyLevel.Exact);
        var successFactory = new InvocationFlowNode(
            // Distinct block ordinal from the save invocation (block 6): the Method Flow builder
            // derives flow-node identities from (kind, block, evaluation ordinal, role), so two
            // operations must never share one identity in a valid hand-authored input.
            WorkItemFlowNode("Invocation", 7, "operation"),
            WorkItemServiceMethod,
            WorkItemSuccessFactoryOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: true,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        var successTerminal = new ReturnFlowNode(
            WorkItemFlowNode("Return", 6, "terminal"),
            WorkItemServiceMethod,
            WorkItemSuccessFactoryOperation,
            [],
            CertaintyLevel.Exact);

        var nodes = new List<FlowNode>
        {
            entry,
            exit,
            query,
            queryAwait,
            absent,
            notFoundFactory,
            notFoundTerminal,
            locked,
            conflictFactory,
            conflictTerminal,
            state,
            save,
            saveAwait,
            successFactory,
            successTerminal,
        };

        var edges = new List<FlowEdge>
        {
            WorkItemEdge(0, entry, query, FlowEdgeKind.Normal, null),
            WorkItemEdge(1, query, queryAwait, FlowEdgeKind.Normal, null),
            WorkItemEdge(2, queryAwait, absent, FlowEdgeKind.Normal, null),
            WorkItemEdge(3, absent, notFoundFactory, FlowEdgeKind.True, WorkItemAbsentCondition),
            WorkItemEdge(4, absent, locked, FlowEdgeKind.False, WorkItemAbsentCondition),
            WorkItemEdge(5, locked, conflictFactory, FlowEdgeKind.True, WorkItemLockedCondition),
            WorkItemEdge(6, locked, state, FlowEdgeKind.False, WorkItemLockedCondition),
            WorkItemEdge(7, state, save, FlowEdgeKind.Normal, null),
            WorkItemEdge(8, save, saveAwait, FlowEdgeKind.Normal, null),
            WorkItemEdge(9, saveAwait, successFactory, FlowEdgeKind.Normal, null),
            WorkItemEdge(10, notFoundFactory, notFoundTerminal, FlowEdgeKind.Normal, null),
            WorkItemEdge(11, notFoundTerminal, exit, FlowEdgeKind.Return, null),
            WorkItemEdge(12, conflictFactory, conflictTerminal, FlowEdgeKind.Normal, null),
            WorkItemEdge(13, conflictTerminal, exit, FlowEdgeKind.Return, null),
            WorkItemEdge(14, successFactory, successTerminal, FlowEdgeKind.Normal, null),
            WorkItemEdge(15, successTerminal, exit, FlowEdgeKind.Return, null),
        };
        if (unsupportedTopology)
        {
            // The locked true arm crosses a loop back to the absent decision; the arm's continuation
            // is not a plain if/else rejoin and must fail closed with SC013.
            edges.Add(WorkItemEdge(16, locked, absent, FlowEdgeKind.LoopBack, WorkItemLockedCondition));
        }

        var dependences = new List<ControlDependence>
        {
            WorkItemDependence(absent, notFoundFactory, onTrue: true),
            WorkItemDependence(absent, notFoundTerminal, onTrue: true),
            WorkItemDependence(absent, state, onTrue: false),
            WorkItemDependence(absent, save, onTrue: false),
            // The awaited continuation carries the same direct dependences as its invocation so the
            // two eligible anchors agree (mirrors the compiler output under architecture decision).
            WorkItemDependence(absent, saveAwait, onTrue: false),
            WorkItemDependence(absent, successFactory, onTrue: false),
            WorkItemDependence(absent, successTerminal, onTrue: false),
            WorkItemDependence(locked, conflictFactory, onTrue: true),
            WorkItemDependence(locked, conflictTerminal, onTrue: true),
            WorkItemDependence(locked, state, onTrue: false),
            WorkItemDependence(locked, save, onTrue: false),
            WorkItemDependence(locked, saveAwait, onTrue: false),
            WorkItemDependence(locked, successFactory, onTrue: false),
            WorkItemDependence(locked, successTerminal, onTrue: false),
        };
        if (scopedConflict)
        {
            // The continuing save (and its awaited continuation) is ALSO claimed by the absent
            // decision's true arm: a same-decision dual-polarity conflict that must fail closed with
            // SC012 while the locked false membership stays valid.
            dependences.Add(WorkItemDependence(absent, save, onTrue: true));
            dependences.Add(WorkItemDependence(absent, saveAwait, onTrue: true));
        }

        if (dualPolarityConflict || multipleConflict)
        {
            // The save and its awaited continuation are claimed by BOTH arms of BOTH decisions, so no
            // valid save membership survives: every same-decision conflict is withheld and each
            // conflict is reported as its own SC012, never hidden by the first one found.
            dependences.Add(WorkItemDependence(absent, save, onTrue: true));
            dependences.Add(WorkItemDependence(absent, saveAwait, onTrue: true));
            dependences.Add(WorkItemDependence(locked, save, onTrue: true));
            dependences.Add(WorkItemDependence(locked, saveAwait, onTrue: true));
        }

        var regions = new List<FlowRegion>
        {
            new(
                StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(WorkItemServiceMethod, "Root", 0)),
                WorkItemServiceMethod,
                FlowRegionKind.Root,
                null,
                0,
                nodes.Select(node => node.Id).ToImmutableArray(),
                null,
                [],
                CertaintyLevel.Exact),
        };
        if (unsupportedTopology || nestedRegionKind is not null || finallyTarget)
        {
            var regionKind = nestedRegionKind ?? (finallyTarget ? "Finally" : "Catch");
            regions.Add(new FlowRegion(
                StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(WorkItemServiceMethod, regionKind, 1)),
                WorkItemServiceMethod,
                Enum.Parse<FlowRegionKind>(regionKind),
                regions[0].Id,
                1,
                nestedRegionKind == "Try"
                    ? nodes.Select(node => node.Id).ToImmutableArray()
                    : finallyTarget
                        ? [conflictFactory.Id]
                    : [locked.Id, conflictFactory.Id],
                regionKind is "Catch" or "Filter" ? "System.Exception" : null,
                [SourceEvidence("workitem-try")],
                CertaintyLevel.Exact));
        }

        var outcomes = new List<FlowOutcome>
        {
            new(FlowOutcomeKind.ExplicitReturn, 2, WorkItemNotFoundFactoryOperation, [], CertaintyLevel.Exact),
            new(FlowOutcomeKind.ExplicitReturn, 4, WorkItemConflictFactoryOperation, [], CertaintyLevel.Exact),
            new(FlowOutcomeKind.ExplicitReturn, 6, WorkItemSuccessFactoryOperation, [], CertaintyLevel.Exact),
        };

        return new MethodFlowSnapshot(
            WorkItemServiceMethod,
            "body-fingerprint-workitem",
            (reverse ? nodes.AsEnumerable().Reverse() : nodes).ToImmutableArray(),
            (reverse ? edges.AsEnumerable().Reverse() : edges).ToImmutableArray(),
            (reverse ? regions.AsEnumerable().Reverse() : regions).ToImmutableArray(),
            outcomes.ToImmutableArray(),
            new LocalValueGraph([], []),
            (reverse ? dependences.AsEnumerable().Reverse() : dependences).ToImmutableArray(),
            null,
            [],
            "flow-fingerprint-workitem");
    }

    /// <summary>
    /// The Method Flow shape of an exact natural loop whose header is a DecisionFlowNode: the
    /// LoopNode records that header, the body Add invocation, and the exit; the lowered header and
    /// body are placed inside a Try region (the compiler's enumerator-disposal shape, not exception
    /// recovery); and the body's LoopBack edge targets that same header. The loop body Add is the
    /// only controlled material node (direct true-arm dependence). This is the fixture that architecture decision
    /// decision 11 admits: both normal arms classify without SC013 even though the header is inside a
    /// Try region, while genuine catch/filter/finally regions and foreign loop backs stay unsupported.
    /// The review-finding variants deliberately break the loop snapshot or the region placement:
    /// "foreign-body-source" records a Body that does not contain the actual LoopBack source,
    /// "mismatched-exit" records Exits that do not contain the actual normal exit, and
    /// "catch"/"filter"/"finally" place the exact header and body in a genuine exception region. The
    /// LoopBack edge carries Conservative evidence so the positive terminal must aggregate the loop
    /// fact and loop-back edge artifacts and degrade to the weakest contributor.
    /// </summary>
    private static MethodFlowSnapshot CreateExactOwnHeaderLoopServiceFlow(string variant = "valid")
    {
        if (variant is not ("valid" or "foreign-body-source" or "mismatched-exit" or "catch" or "filter" or "finally"))
        {
            throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown loop fixture variant.");
        }

        var entry = new EntryFlowNode(
            WorkItemFlowNode("Entry", 0, "entry"),
            WorkItemServiceMethod,
            [],
            CertaintyLevel.Exact);
        var exit = new ExitFlowNode(
            WorkItemFlowNode("Exit", 99, "exit"),
            WorkItemServiceMethod,
            [],
            CertaintyLevel.Exact);
        var query = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 1, "operation"),
            WorkItemServiceMethod,
            WorkItemQueryOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        var loopHeader = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 2, "decision"),
            WorkItemServiceMethod,
            WorkItemLoopCondition,
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var add = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 3, "operation"),
            WorkItemServiceMethod,
            WorkItemAddOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);

        // The malformed variants keep the exact same edges and header identity but record a LoopNode
        // whose Body does not contain the actual LoopBack source ("foreign-body-source") or whose
        // Exits do not contain the actual normal exit ("mismatched-exit"). A complete loop snapshot
        // must agree with both before the carve-out classifies represented iteration.
        var loopBody = variant == "foreign-body-source"
            ? ImmutableArray.Create(query.Id)
            : ImmutableArray.Create(add.Id);
        var loopExits = variant == "mismatched-exit"
            ? ImmutableArray.Create(query.Id)
            : ImmutableArray.Create(exit.Id);
        FlowRegionKind regionKind = variant switch
        {
            "catch" => FlowRegionKind.Catch,
            "filter" => FlowRegionKind.Filter,
            "finally" => FlowRegionKind.Finally,
            _ => FlowRegionKind.Try,
        };
        var innerRegionId = StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(
            WorkItemServiceMethod,
            regionKind.ToString(),
            1));
        var loop = new LoopNode(
            WorkItemFlowNode("Loop", 2, "loop"),
            WorkItemServiceMethod,
            innerRegionId,
            loopHeader.Id,
            loopBody,
            loopExits,
            [SourceEvidence(WorkItemLoopEvidence)],
            CertaintyLevel.Exact);

        var nodes = new List<FlowNode> { entry, exit, query, loopHeader, add, loop };
        // The LoopBack edge is deliberately built with Conservative evidence so the body-arm terminal
        // must aggregate the loop fact (Exact) and the loop-back edge (Conservative) artifacts and
        // degrade to the weakest contributor instead of inheriting the decision's Exact certainty.
        var loopBackEdge = new FlowEdge(
            StableIdentity.CreateFlowEdgeId(new FlowEdgeIdentityDescriptor(
                WorkItemServiceMethod,
                add.Id.Value,
                loopHeader.Id.Value,
                FlowEdgeKind.LoopBack.ToString(),
                3)),
            WorkItemServiceMethod,
            add.Id,
            loopHeader.Id,
            FlowEdgeKind.LoopBack,
            WorkItemLoopCondition,
            [ConservativeEvidence(WorkItemLoopBackEvidence)],
            CertaintyLevel.Conservative);
        var edges = new List<FlowEdge>
        {
            WorkItemEdge(0, entry, query, FlowEdgeKind.Normal, null),
            WorkItemEdge(1, query, loopHeader, FlowEdgeKind.Normal, null),
            WorkItemEdge(2, loopHeader, add, FlowEdgeKind.True, WorkItemLoopCondition),
            loopBackEdge,
            WorkItemEdge(4, loopHeader, exit, FlowEdgeKind.False, WorkItemLoopCondition),
        };
        var dependences = new List<ControlDependence>
        {
            WorkItemDependence(loopHeader, add, onTrue: true),
        };
        var rootRegion = new FlowRegion(
            StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(WorkItemServiceMethod, "Root", 0)),
            WorkItemServiceMethod,
            FlowRegionKind.Root,
            null,
            0,
            nodes.Select(node => node.Id).ToImmutableArray(),
            null,
            [],
            CertaintyLevel.Exact);
        var innerRegion = new FlowRegion(
            innerRegionId,
            WorkItemServiceMethod,
            regionKind,
            rootRegion.Id,
            1,
            [loopHeader.Id, add.Id],
            regionKind is FlowRegionKind.Catch or FlowRegionKind.Filter ? "System.Exception" : null,
            [SourceEvidence("workitem-try")],
            CertaintyLevel.Exact);

        return new MethodFlowSnapshot(
            WorkItemServiceMethod,
            "body-fingerprint-exact-own-header-loop",
            nodes.ToImmutableArray(),
            edges.ToImmutableArray(),
            ImmutableArray.Create(rootRegion, innerRegion),
            [],
            new LocalValueGraph([], []),
            dependences.ToImmutableArray(),
            null,
            [],
            "flow-fingerprint-exact-own-header-loop");
    }

    private static FlowNodeId WorkItemFlowNode(string kind, int block, string role)
        => FlowNode(WorkItemServiceMethod, kind, block, role);

    private static FlowNodeId FlowNode(MethodId method, string kind, int block, string role)
        => StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(method, kind, block, 0, role));

    private static FlowEdge WorkItemEdge(int ordinal, FlowNode source, FlowNode target, FlowEdgeKind kind, OperationId? guard)
        => Edge(WorkItemServiceMethod, ordinal, source, target, kind, guard);

    private static FlowEdge Edge(MethodId method, int ordinal, FlowNode source, FlowNode target, FlowEdgeKind kind, OperationId? guard)
        => new(
            StableIdentity.CreateFlowEdgeId(new FlowEdgeIdentityDescriptor(
                method,
                source.Id.Value,
                target.Id.Value,
                kind.ToString(),
                ordinal)),
            method,
            source.Id,
            target.Id,
            kind,
            guard,
            [],
            CertaintyLevel.Exact);

    /// <summary>
    /// One decision whose true arm reaches a represented return sink AND a rejoin boundary (or an
    /// operation-derived duplicate return with a continuation) from the same invocation. The current
    /// first-boundary classifier claims an exact kind; the accepted contract fix must traverse the complete
    /// reachable arm subgraph and fail closed with SC013. Reversing the boundary edge ordinals proves
    /// the classification is independent of edge input order.
    /// </summary>
    private static MethodFlowSnapshot CreateBoundaryTopologyServiceFlow(bool reverseBoundaryEdges, bool duplicateReturn)
    {
        var entry = new EntryFlowNode(WorkItemFlowNode("Entry", 0, "entry"), WorkItemServiceMethod, [], CertaintyLevel.Exact);
        var exit = new ExitFlowNode(WorkItemFlowNode("Exit", 99, "exit"), WorkItemServiceMethod, [], CertaintyLevel.Exact);
        var absent = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 1, "decision"),
            WorkItemServiceMethod,
            WorkItemAbsentCondition,
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var notFoundFactory = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 2, "operation"),
            WorkItemServiceMethod,
            WorkItemNotFoundFactoryOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: true,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        var notFoundTerminal = new ReturnFlowNode(
            WorkItemFlowNode("Return", 2, "terminal"),
            WorkItemServiceMethod,
            WorkItemNotFoundFactoryOperation,
            [],
            CertaintyLevel.Exact);
        var duplicateTerminal = new ReturnFlowNode(
            WorkItemFlowNode("Return", 3, "terminal"),
            WorkItemServiceMethod,
            WorkItemNotFoundFactoryOperation,
            [],
            CertaintyLevel.Exact);

        var nodes = new List<FlowNode> { entry, exit, absent, notFoundFactory, notFoundTerminal };
        if (duplicateReturn)
        {
            nodes.Add(duplicateTerminal);
        }

        // The Method Flow shape of a represented terminal: the factory sequences to the represented
        // terminal with an internal Normal edge, then the terminal is the block tail whose outgoing
        // Return edge reaches the successor/exit. The duplicate-return variant adds an
        // operation-derived duplicate return node that continues with a Normal edge, so it can never
        // be accepted as a terminal boundary alone. Swapping the boundary edge ordinals proves the
        // classification is independent of edge input order.
        var edges = new List<FlowEdge>
        {
            WorkItemEdge(0, entry, absent, FlowEdgeKind.Normal, null),
            WorkItemEdge(1, absent, notFoundFactory, FlowEdgeKind.True, WorkItemAbsentCondition),
            WorkItemEdge(2, absent, exit, FlowEdgeKind.False, WorkItemAbsentCondition),
            WorkItemEdge(reverseBoundaryEdges ? 13 : 11, notFoundFactory, notFoundTerminal, FlowEdgeKind.Normal, null),
            WorkItemEdge(12, notFoundTerminal, exit, FlowEdgeKind.Return, null),
        };
        if (duplicateReturn)
        {
            edges.Add(WorkItemEdge(reverseBoundaryEdges ? 11 : 13, notFoundFactory, duplicateTerminal, FlowEdgeKind.Normal, null));
            edges.Add(WorkItemEdge(14, duplicateTerminal, exit, FlowEdgeKind.Normal, null));
        }
        else
        {
            edges.Add(WorkItemEdge(reverseBoundaryEdges ? 11 : 13, notFoundFactory, exit, FlowEdgeKind.Normal, null));
        }

        var dependences = new List<ControlDependence>
        {
            WorkItemDependence(absent, notFoundFactory, onTrue: true),
        };

        return new MethodFlowSnapshot(
            WorkItemServiceMethod,
            "body-fingerprint-boundary",
            nodes.ToImmutableArray(),
            edges.ToImmutableArray(),
            [],
            [],
            new LocalValueGraph([], []),
            dependences.ToImmutableArray(),
            null,
            [],
            "flow-fingerprint-boundary");
    }

    /// <summary>
    /// One operation identity (the save) carried by TWO eligible anchors: an invocation plus its await,
    /// or two duplicate invocations. The non-await invocation is preferred as the anchor, so the second
    /// anchor's disagreeing membership is silently ignored today. The accepted contract fix must retain every
    /// eligible anchor and withhold membership with SC011 unless their memberships agree.
    /// </summary>
    private static MethodFlowSnapshot CreateDuplicateAnchorServiceFlow(bool duplicateInvocation, bool agreeing)
    {
        var entry = new EntryFlowNode(WorkItemFlowNode("Entry", 0, "entry"), WorkItemServiceMethod, [], CertaintyLevel.Exact);
        var exit = new ExitFlowNode(WorkItemFlowNode("Exit", 99, "exit"), WorkItemServiceMethod, [], CertaintyLevel.Exact);
        var absent = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 1, "decision"),
            WorkItemServiceMethod,
            WorkItemAbsentCondition,
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var invocation = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 2, "operation"),
            WorkItemServiceMethod,
            WorkItemSaveOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        // The await (or duplicate invocation) deliberately sorts BEFORE the invocation so the current
        // non-await preference decides the anchor instead of first-encounter order.
        var second = duplicateInvocation
            ? (FlowNode)new InvocationFlowNode(
                WorkItemFlowNode("Invocation", 3, "operation"),
                WorkItemServiceMethod,
                WorkItemSaveOperation,
                null,
                IsDispatchable: false,
                IsDelegateOrEventInvoke: false,
                IsStatic: false,
                IsConstructor: false,
                IsDynamic: false,
                [],
                CertaintyLevel.Exact)
            : new AwaitFlowNode(
                WorkItemFlowNode("Await", 1, "operation"),
                WorkItemServiceMethod,
                WorkItemSaveOperation,
                [],
                CertaintyLevel.Exact);

        var nodes = new List<FlowNode> { entry, exit, absent, invocation, second };
        var edges = new List<FlowEdge>
        {
            WorkItemEdge(0, entry, absent, FlowEdgeKind.Normal, null),
            WorkItemEdge(1, absent, invocation, FlowEdgeKind.True, WorkItemAbsentCondition),
            WorkItemEdge(2, invocation, second, FlowEdgeKind.Normal, null),
            WorkItemEdge(3, second, exit, FlowEdgeKind.Normal, null),
            WorkItemEdge(4, absent, exit, FlowEdgeKind.False, WorkItemAbsentCondition),
        };
        var dependences = new List<ControlDependence>
        {
            WorkItemDependence(absent, invocation, onTrue: true),
            WorkItemDependence(absent, second, onTrue: agreeing),
        };

        return new MethodFlowSnapshot(
            WorkItemServiceMethod,
            "body-fingerprint-duplicate-anchor",
            nodes.ToImmutableArray(),
            edges.ToImmutableArray(),
            [],
            [],
            new LocalValueGraph([], []),
            dependences.ToImmutableArray(),
            null,
            [],
            "flow-fingerprint-duplicate-anchor");
    }

    /// <summary>
    /// The controller action flow with the exact service-call operation guarded by a decision. The
    /// material service-call scenario node must be scoped when this anchor is available; today the node
    /// is created with a null operation and stays silently unscoped.
    /// </summary>
    private static MethodFlowSnapshot CreateServiceCallScopedActionFlow()
    {
        var entry = new EntryFlowNode(FlowNode(WorkItemActionMethod, "Entry", 0, "entry"), WorkItemActionMethod, [], CertaintyLevel.Exact);
        var exit = new ExitFlowNode(FlowNode(WorkItemActionMethod, "Exit", 99, "exit"), WorkItemActionMethod, [], CertaintyLevel.Exact);
        var callDecision = new DecisionFlowNode(
            FlowNode(WorkItemActionMethod, "Decision", 1, "decision"),
            WorkItemActionMethod,
            WorkItemAbsentCondition,
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var callInvocation = new InvocationFlowNode(
            FlowNode(WorkItemActionMethod, "Invocation", 2, "operation"),
            WorkItemActionMethod,
            WorkItemCallOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);

        var nodes = new List<FlowNode> { entry, exit, callDecision, callInvocation };
        var edges = new List<FlowEdge>
        {
            Edge(WorkItemActionMethod, 0, entry, callDecision, FlowEdgeKind.Normal, null),
            Edge(WorkItemActionMethod, 1, callDecision, callInvocation, FlowEdgeKind.True, WorkItemAbsentCondition),
            Edge(WorkItemActionMethod, 2, callDecision, exit, FlowEdgeKind.False, WorkItemAbsentCondition),
            Edge(WorkItemActionMethod, 3, callInvocation, exit, FlowEdgeKind.Normal, null),
        };
        var dependences = new List<ControlDependence>
        {
            new(callDecision.Id, callInvocation.Id, true, [SourceEvidence("workitem-dependence")], CertaintyLevel.Exact),
        };

        return new MethodFlowSnapshot(
            WorkItemActionMethod,
            "body-fingerprint-call-guard",
            nodes.ToImmutableArray(),
            edges.ToImmutableArray(),
            [],
            [],
            new LocalValueGraph([], []),
            dependences.ToImmutableArray(),
            null,
            [],
            "flow-fingerprint-call-guard");
    }

    /// <summary>
    /// A supported terminating true arm whose traversed edge and boundary return carry Conservative
    /// evidence while the decision is Exact. Terminal/rejoin facts must aggregate the decision,
    /// traversed-edge, and boundary evidence and degrade to the weakest supported certainty
    /// (Conservative), never publish the exact decision certainty alone.
    /// </summary>
    private static MethodFlowSnapshot CreateDegradedTerminalEvidenceFlow()
    {
        var entry = new EntryFlowNode(WorkItemFlowNode("Entry", 0, "entry"), WorkItemServiceMethod, [], CertaintyLevel.Exact);
        var exit = new ExitFlowNode(WorkItemFlowNode("Exit", 99, "exit"), WorkItemServiceMethod, [], CertaintyLevel.Exact);
        var absent = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 1, "decision"),
            WorkItemServiceMethod,
            WorkItemAbsentCondition,
            [SourceEvidence(WorkItemDecisionEvidence)],
            CertaintyLevel.Exact);
        var notFoundFactory = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 2, "operation"),
            WorkItemServiceMethod,
            WorkItemNotFoundFactoryOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: true,
            IsConstructor: false,
            IsDynamic: false,
            [SourceEvidence(WorkItemDecisionEvidence)],
            CertaintyLevel.Exact);
        var notFoundTerminal = new ReturnFlowNode(
            WorkItemFlowNode("Return", 2, "terminal"),
            WorkItemServiceMethod,
            WorkItemNotFoundFactoryOperation,
            [ConservativeEvidence(WorkItemBoundaryTerminalEvidence)],
            CertaintyLevel.Conservative);
        var internalEdge = new FlowEdge(
            StableIdentity.CreateFlowEdgeId(new FlowEdgeIdentityDescriptor(
                WorkItemServiceMethod,
                notFoundFactory.Id.Value,
                notFoundTerminal.Id.Value,
                FlowEdgeKind.Normal.ToString(),
                3)),
            WorkItemServiceMethod,
            notFoundFactory.Id,
            notFoundTerminal.Id,
            FlowEdgeKind.Normal,
            null,
            [ConservativeEvidence(WorkItemBoundaryEdgeEvidence)],
            CertaintyLevel.Conservative);
        var terminalEdge = new FlowEdge(
            StableIdentity.CreateFlowEdgeId(new FlowEdgeIdentityDescriptor(
                WorkItemServiceMethod,
                notFoundTerminal.Id.Value,
                exit.Id.Value,
                FlowEdgeKind.Return.ToString(),
                4)),
            WorkItemServiceMethod,
            notFoundTerminal.Id,
            exit.Id,
            FlowEdgeKind.Return,
            null,
            [],
            CertaintyLevel.Exact);

        var nodes = new List<FlowNode> { entry, exit, absent, notFoundFactory, notFoundTerminal };
        var edges = new List<FlowEdge>
        {
            WorkItemEdge(0, entry, absent, FlowEdgeKind.Normal, null),
            WorkItemEdge(1, absent, notFoundFactory, FlowEdgeKind.True, WorkItemAbsentCondition),
            WorkItemEdge(2, absent, exit, FlowEdgeKind.False, WorkItemAbsentCondition),
            internalEdge,
            terminalEdge,
        };
        var dependences = new List<ControlDependence>
        {
            WorkItemDependence(absent, notFoundFactory, onTrue: true),
        };

        return new MethodFlowSnapshot(
            WorkItemServiceMethod,
            "body-fingerprint-degraded-evidence",
            nodes.ToImmutableArray(),
            edges.ToImmutableArray(),
            [],
            [],
            new LocalValueGraph([], []),
            dependences.ToImmutableArray(),
            null,
            [],
            "flow-fingerprint-degraded-evidence");
    }

    /// <summary>
    /// Three decisions (blocks 1, 3, 5) with memberships across both polarities and three distinct
    /// controlled scenario nodes. The frozen canonical order is controlling flow-node identity, then
    /// semantic polarity (false before true), then controlled scenario-node identity; hashed
    /// decision/arm identities must never determine the order.
    /// </summary>
    private static MethodFlowSnapshot CreateCanonicalOrderServiceFlow()
    {
        var entry = new EntryFlowNode(WorkItemFlowNode("Entry", 0, "entry"), WorkItemServiceMethod, [], CertaintyLevel.Exact);
        var exit = new ExitFlowNode(WorkItemFlowNode("Exit", 99, "exit"), WorkItemServiceMethod, [], CertaintyLevel.Exact);
        var d1 = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 1, "decision"),
            WorkItemServiceMethod,
            WorkItemAbsentCondition,
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var d2 = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 3, "decision"),
            WorkItemServiceMethod,
            WorkItemLockedCondition,
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var d3 = new DecisionFlowNode(
            WorkItemFlowNode("Decision", 5, "decision"),
            WorkItemServiceMethod,
            new OperationId("operation:v1:workitem:item-finalized"),
            [SourceEvidence("workitem-decision")],
            CertaintyLevel.Exact);
        var query = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 2, "operation"),
            WorkItemServiceMethod,
            WorkItemQueryOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);
        var state = new OperationFlowNode(
            WorkItemFlowNode("Operation", 4, "operation"),
            WorkItemServiceMethod,
            WorkItemStateAssignmentOperation,
            ExtractedOperationKind.Assignment,
            [],
            CertaintyLevel.Exact);
        var save = new InvocationFlowNode(
            WorkItemFlowNode("Invocation", 6, "operation"),
            WorkItemServiceMethod,
            WorkItemSaveOperation,
            null,
            IsDispatchable: false,
            IsDelegateOrEventInvoke: false,
            IsStatic: false,
            IsConstructor: false,
            IsDynamic: false,
            [],
            CertaintyLevel.Exact);

        var nodes = new List<FlowNode> { entry, exit, d1, d2, d3, query, state, save };
        var edges = new List<FlowEdge>
        {
            WorkItemEdge(0, entry, d1, FlowEdgeKind.Normal, null),
            WorkItemEdge(1, d1, query, FlowEdgeKind.True, WorkItemAbsentCondition),
            WorkItemEdge(2, d1, d2, FlowEdgeKind.False, WorkItemAbsentCondition),
            WorkItemEdge(3, d2, state, FlowEdgeKind.True, WorkItemLockedCondition),
            WorkItemEdge(4, d2, d3, FlowEdgeKind.False, WorkItemLockedCondition),
            WorkItemEdge(5, d3, save, FlowEdgeKind.True, new OperationId("operation:v1:workitem:item-finalized")),
            WorkItemEdge(6, d3, exit, FlowEdgeKind.False, new OperationId("operation:v1:workitem:item-finalized")),
            WorkItemEdge(7, query, exit, FlowEdgeKind.Normal, null),
            WorkItemEdge(8, state, exit, FlowEdgeKind.Normal, null),
            WorkItemEdge(9, save, exit, FlowEdgeKind.Normal, null),
        };
        var dependences = new List<ControlDependence>
        {
            WorkItemDependence(d1, query, onTrue: true),
            WorkItemDependence(d1, state, onTrue: false),
            WorkItemDependence(d2, state, onTrue: true),
            WorkItemDependence(d2, save, onTrue: false),
            WorkItemDependence(d3, query, onTrue: true),
            WorkItemDependence(d3, save, onTrue: false),
        };

        return new MethodFlowSnapshot(
            WorkItemServiceMethod,
            "body-fingerprint-canonical-order",
            nodes.ToImmutableArray(),
            edges.ToImmutableArray(),
            [],
            [],
            new LocalValueGraph([], []),
            dependences.ToImmutableArray(),
            null,
            [],
            "flow-fingerprint-canonical-order");
    }

    private static ControlDependence WorkItemDependence(DecisionFlowNode decision, FlowNode controlled, bool onTrue)
        => new(decision.Id, controlled.Id, onTrue, [SourceEvidence("workitem-dependence")], CertaintyLevel.Exact);

    private static MethodFlowSnapshot CreateActionFlow(MethodId method)
        => new(
            method,
            "body-fingerprint",
            [],
            [],
            [],
            [],
            new LocalValueGraph([], []),
            [],
            null,
            [],
            "flow-fingerprint");

    private static ImmutableArray<ReturnProvenanceSemanticFact> BuildReturnProvenances(
        bool unrelatedFactory,
        bool duplicateSuccessFactories)
    {
        var builder = ImmutableArray.CreateBuilder<ReturnProvenanceSemanticFact>();
        if (!unrelatedFactory)
        {
            builder.Add(new ReturnProvenanceSemanticFact(
                new SemanticFactId("semantic-fact:v1:return:Success"),
                ServiceMethod,
                SuccessOperation,
                [SourceEvidence("return-success")],
                CertaintyLevel.Exact));
        }

        if (duplicateSuccessFactories)
        {
            builder.Add(new ReturnProvenanceSemanticFact(
                new SemanticFactId("semantic-fact:v1:return:Success.duplicate"),
                ServiceMethod,
                DuplicateSuccessOperation,
                [SourceEvidence("return-success-duplicate")],
                CertaintyLevel.Exact));
        }

        builder.Add(new ReturnProvenanceSemanticFact(
            new SemanticFactId("semantic-fact:v1:return:NotFound"),
            ServiceMethod,
            NotFoundOperation,
            [SourceEvidence("return-not-found")],
            CertaintyLevel.Exact));
        return builder.ToImmutable();
    }

    private static StructuralResultFactoryFact CreateFactoryFact(
        string id,
        MethodId method,
        OperationId operation,
        StructuralResultFactoryKind kind,
        bool isSuccess,
        OperationId? argumentOperation)
        => new(
            new SemanticFactId(id),
            method,
            operation,
            ResultType,
            kind,
            isSuccess,
            argumentOperation,
            [SourceEvidence($"factory:{id}")],
            CertaintyLevel.Exact);

    private static ProgramIndexSnapshot CreateIndex(
        ImmutableArray<ProgramType> types,
        ImmutableArray<ProgramMethod> methods)
        => new(
            SchemaVersion: 1,
            ProducerVersion: "test",
            Profile,
            [],
            [],
            [],
            types,
            [],
            methods,
            [],
            [],
            [],
            [],
            [],
            "input-hash",
            "index-fingerprint");

    private static ProgramType CreateType(SymbolId id, string metadataName)
        => new(
            id,
            new ProjectId("project:v1:test"),
            new SymbolId("symbol:v1:global"),
            metadataName,
            ProgramTypeKind.Class,
            null,
            [],
            "type-signature",
            [SourceEvidence($"type:{metadataName}")]);

    private static ProgramMethod CreateMethod(MethodId id, SymbolId containingType, string name)
        => new(
            id,
            new SymbolId($"symbol:v1:{name}"),
            containingType,
            name,
            $"DisplaySignature:{name}",
            [],
            "System.Threading.Tasks.Task<object>",
            "method-signature",
            null,
            [SourceEvidence($"method:{name}")]);

    private static DependencyInjectionRegistrationFact CreateRegistration(
        SemanticFactId id,
        MethodId sourceMethod,
        string serviceType,
        string implementationType)
        => new(
            id,
            sourceMethod,
            new OperationId($"operation:v1:registration:{implementationType}"),
            serviceType,
            implementationType,
            DependencyInjectionLifetime.Scoped,
            [SourceEvidence($"registration:{implementationType}")],
            CertaintyLevel.Exact);

    private static DependencyInjectionBindingFact CreateBinding(
        MethodId constructorMethod,
        SemanticFactId registrationId,
        string serviceType,
        string implementationType,
        int ordinal)
        => new(
            new SemanticFactId($"semantic-fact:v1:binding:{ordinal}"),
            constructorMethod,
            ordinal,
            "service",
            serviceType,
            registrationId,
            serviceType,
            implementationType,
            DependencyInjectionLifetime.Scoped,
            [SourceEvidence($"binding:{ordinal}")],
            CertaintyLevel.Exact);

    internal static EvidenceRef SourceEvidence(string artifact)
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

    internal static EvidenceRef ConservativeEvidence(string artifact)
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
            CertaintyLevel.Conservative);
}
