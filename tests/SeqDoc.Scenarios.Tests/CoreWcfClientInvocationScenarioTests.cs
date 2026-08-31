using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// Proves the Scenario Graph join added on top of issue #5/#7's client-boundary facts: a compiler-proven
/// <see cref="ServiceClientInvocationFact"/> reachable as a root-level direct call joins an independently
/// admitted <see cref="ServiceClientBoundaryFact"/> (exact client/contract identity, classified
/// <see cref="ServiceClientKind.SourceClient"/> or <see cref="ServiceClientKind.GeneratedClient"/>) into
/// one <see cref="ScenarioNodeKind.ClientOperationInvocation"/> node, replacing the generic
/// <see cref="ScenarioNodeKind.MethodCall"/> node the same call site would otherwise produce (mirroring
/// <see cref="ScenarioGraphBuilderTests.RootDirectCallsAreTypedRootOnlyNodesAndRetainSC001"/>'s base
/// fixture); an invocation with no matching admitted boundary produces neither node kind, only a
/// conservative diagnostic (never a broad HTTP/client fallback); and a matching
/// <see cref="ServiceFaultContractFact"/> joins by exact operation symbol.
/// </summary>
public sealed class CoreWcfClientInvocationScenarioTests
{
    private const string ContractType = "CoreWcfServices.ICalculatorService";
    private const string ClientType = "CoreWcfServices.CalculatorSourceClient";
    private static readonly SymbolId ContractTypeSymbol = new($"symbol:v1:{ContractType}");
    private static readonly SymbolId ClientTypeSymbol = new($"symbol:v1:{ClientType}");
    private static readonly SymbolId OperationSymbol = new($"symbol:v1:{ContractType}.SendAsync");

    private static ServiceClientInvocationFact CreateInvocationFact(
        CertaintyLevel certainty = CertaintyLevel.Exact,
        string idSuffix = "root-direct",
        string operationName = "SendAsync",
        ClientInvocationResultClaimKind resultClaim = ClientInvocationResultClaimKind.ResultAssigned,
        string? resultBindingName = "result")
        => new()
        {
            Id = new BehaviorFactId($"behavior-fact:v1:service-client-invocation:{idSuffix}"),
            Evidence = [ScenarioTestFactory.SourceEvidence("service-client-invocation")],
            Certainty = certainty,
            CallerMethod = ScenarioTestFactory.ActionMethod,
            InvocationOperation = ScenarioTestFactory.RootDirectCallOperation,
            ServiceContractType = ContractType,
            ServiceContractTypeSymbol = ContractTypeSymbol,
            ClientType = ClientType,
            ClientTypeSymbol = ClientTypeSymbol,
            OperationName = operationName,
            OperationSymbol = OperationSymbol,
            OperationKey = $"{ContractType}.{operationName}",
            ResultClaim = resultClaim,
            IsAwaited = true,
            ResultBindingName = resultBindingName,
            DeclaredResultType = "System.Double",
        };

    private static ServiceClientBoundaryFact CreateBoundaryFact(
        ServiceClientKind clientKind = ServiceClientKind.SourceClient,
        CertaintyLevel certainty = CertaintyLevel.Exact,
        string idSuffix = "root-direct")
        => new()
        {
            Id = new BehaviorFactId($"behavior-fact:v1:service-client-boundary:{idSuffix}"),
            Evidence = [certainty == CertaintyLevel.Exact
                ? ScenarioTestFactory.SourceEvidence("service-client-boundary")
                : ScenarioTestFactory.ConservativeEvidence("service-client-boundary")],
            Certainty = certainty,
            ServiceContractType = ContractType,
            ServiceContractTypeSymbol = ContractTypeSymbol,
            ClientType = ClientType,
            ClientTypeSymbol = ClientTypeSymbol,
            ClientKind = clientKind,
        };

    private static ServiceFaultContractFact CreateFaultFact()
        => new()
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-fault-contract:root-direct"),
            Evidence = [ScenarioTestFactory.SourceEvidence("service-fault-contract")],
            Certainty = CertaintyLevel.Exact,
            ServiceContractType = ContractType,
            OperationName = "SendAsync",
            OperationSymbol = OperationSymbol,
            FaultType = "CoreWcfServices.NegativeSquareRootFault",
            FaultTypeIdentity = new FrameworkTypeIdentity("CoreWcfServices", "1.0.0", "CoreWcfServices.NegativeSquareRootFault"),
        };

    private static ScenarioAnalysisRequest CreateRequest(params BehaviorFact[] additionalFacts)
    {
        var baseRequest = ScenarioTestFactory.CreateRootDirectCallRequest();
        return baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(additionalFacts),
                ProfileId = baseRequest.Profile.Id,
                ProgramIndexFingerprint = baseRequest.ProgramIndex.IndexFingerprint,
            },
        };
    }

    private static ScenarioAnalysisRequest CreateConfiguredRequest(params BehaviorFact[] additionalFacts)
    {
        var baseRequest = ScenarioTestFactory.CreateConfiguredRootDirectCallRequest();
        return baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(additionalFacts),
                ProfileId = baseRequest.Profile.Id,
                ProgramIndexFingerprint = baseRequest.ProgramIndex.IndexFingerprint,
            },
        };
    }

    private static ScenarioAnalysisRequest WithRootPredicateFacts(ScenarioAnalysisRequest request)
    {
        var source = ScenarioTestFactory.CreateGetRequest(decisionGuarded: true).PredicateSemanticFacts!;
        var mapping = source.Mappings.Single();
        var behavior = request.Behavior with
        {
            MethodFlows = request.Behavior.MethodFlows
                .Select(flow => flow.Method == ScenarioTestFactory.ActionMethod
                    && flow.Nodes.OfType<DecisionFlowNode>().Any()
                    ? flow with
                    {
                        Edges = flow.Edges.AddRange(
                            flow.Nodes.OfType<InvocationFlowNode>()
                                .Where(invocation => !flow.Edges.Any(edge => edge.Source == invocation.Id))
                                .Select((invocation, ordinal) => new FlowEdge(
                                    new($"flow-edge:v1:root-direct:terminal:{ordinal}"),
                                    flow.Method,
                                    invocation.Id,
                                    flow.Nodes.OfType<ExitFlowNode>().Single().Id,
                                    FlowEdgeKind.Normal,
                                    null,
                                    invocation.Evidence,
                                    invocation.Certainty)))
                    }
                    : flow)
                .ToImmutableArray(),
        };
        return request with
        {
            Behavior = behavior,
            PredicateSemanticFacts = new PredicateSemanticFactSet(
                source.SchemaVersion,
                source.ProducerVersion,
                request.Profile,
                request.ProgramIndex.IndexFingerprint,
                source.Predicates,
                [new PredicateDecisionMappingFact(
                    mapping.Id,
                    mapping.PredicateId,
                    ScenarioTestFactory.ActionMethod,
                    request.Behavior.MethodFlows
                        .SelectMany(flow => flow.Nodes)
                        .OfType<DecisionFlowNode>()
                        .Where(decision => decision.Method == ScenarioTestFactory.ActionMethod)
                        .Select(decision => decision.Condition)
                        .Distinct()
                        .ToImmutableArray(),
                    request.Profile.Id,
                    request.ProgramIndex.IndexFingerprint,
                    mapping.Evidence,
                    mapping.Certainty)],
                source.Diagnostics,
                source.DebugProjection),
        };
    }

    [Fact]
    public void ConfiguredMethodRootMatchingClientBoundaryReplacesTheGenericMethodCallNodeWithATypedClientOperationInvocationNode()
    {
        // Mirrors MatchingClientBoundaryReplacesTheGenericMethodCallNodeWithATypedClientOperationInvocationNode
        // for a ConfiguredMethod root: the join is wired into the configured branch, not just the HTTP
        // controller-action (SC001) branch.
        var request = CreateConfiguredRequest(CreateInvocationFact(), CreateBoundaryFact());

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.Equal(ScenarioRootKind.ConfiguredMethod, graph.RootKind);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        var node = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(ScenarioTestFactory.RootDirectCallOperation, node.Operation);
        Assert.Equal(ClientType, node.Presentation?.ClientTypeName);
        Assert.Equal("SendAsync", node.Presentation?.CalledMemberName);
        Assert.Equal(ServiceClientKind.SourceClient, node.Presentation?.ClientKind);
        Assert.Equal(CertaintyLevel.Exact, node.Certainty);

        // The new node is parented to the configured action node exactly like the HTTP path.
        var edge = Assert.Single(graph.Edges, edge => edge.Target == node.Id);
        var actionNode = Assert.Single(graph.Nodes, item => item.Kind == ScenarioNodeKind.Action);
        Assert.Equal(actionNode.Id, edge.Source);
        Assert.Equal(ScenarioEdgeKind.Call, edge.Kind);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-UNSUPPORTED-INVOCATION");
    }

    [Fact]
    public void ConfiguredMethodRootInvocationWithoutAMatchingClientBoundaryProducesNoNodeAndAConservativeDiagnostic()
    {
        var request = CreateConfiguredRequest(CreateInvocationFact());

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        var diagnostic = Assert.Single(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-UNSUPPORTED-INVOCATION");
        Assert.Contains(ClientType, diagnostic.Detail);
        Assert.Contains("SendAsync", diagnostic.Detail);
    }

    [Fact]
    public void ConfiguredMethodRootForeignFrameworkFactProfileFailsOpenWithoutSuppressingTheGenericRootLocalCall()
    {
        // FrameworkFactsBound(request) is false because FrameworkFacts.ProfileId is a foreign
        // compilation profile: the typed client join is suppressed AND the generic-node exclusion is
        // never armed, so the valid root-local non-client direct call still renders and no
        // SC-CLIENT-* diagnostic is fabricated from the unbound fact set.
        var baseRequest = ScenarioTestFactory.CreateConfiguredRootDirectCallRequest(
            decisionGuarded: true,
            foreignFactProfile: ScenarioTestFactory.ForeignProfile.Id);
        var request = baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(CreateInvocationFact(), CreateBoundaryFact()),
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.Equal(ScenarioRootKind.ConfiguredMethod, graph.RootKind);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall
            && node.Operation == new OperationId("operation:v1:root.validate"));
        Assert.DoesNotContain(graph.Diagnostics,
            diagnostic => diagnostic.Code.StartsWith("SC-CLIENT-", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfiguredMethodRootForeignFrameworkFactFingerprintFailsOpenWithoutSuppressingTheGenericRootLocalCall()
    {
        // Same fail-open proof as the foreign-profile case, driven by a foreign Program Index
        // fingerprint on the framework fact set.
        var baseRequest = ScenarioTestFactory.CreateConfiguredRootDirectCallRequest(
            decisionGuarded: true,
            foreignFactFingerprint: "foreign-fingerprint");
        var request = baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(CreateInvocationFact(), CreateBoundaryFact()),
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.Equal(ScenarioRootKind.ConfiguredMethod, graph.RootKind);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall
            && node.Operation == new OperationId("operation:v1:root.validate"));
        Assert.DoesNotContain(graph.Diagnostics,
            diagnostic => diagnostic.Code.StartsWith("SC-CLIENT-", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfiguredMethodRootClientInvocationAdmitsDeterministicallyRegardlessOfFrameworkFactInputOrder()
    {
        ScenarioAnalysisRequest BuildRequest(bool reversed)
        {
            var baseRequest = ScenarioTestFactory.CreateConfiguredRootDirectCallRequest(reverseConstruction: reversed);
            BehaviorFact[] extra = reversed
                ? [CreateBoundaryFact(), CreateInvocationFact()]
                : [CreateInvocationFact(), CreateBoundaryFact()];
            return baseRequest with
            {
                FrameworkFacts = baseRequest.FrameworkFacts with
                {
                    Facts = baseRequest.FrameworkFacts.Facts.AddRange(extra),
                    ProfileId = baseRequest.Profile.Id,
                    ProgramIndexFingerprint = baseRequest.ProgramIndex.IndexFingerprint,
                },
            };
        }

        var forward = Assert.Single(ScenarioGraphBuilder.Build(BuildRequest(reversed: false)).Graphs);
        var reversedGraph = Assert.Single(ScenarioGraphBuilder.Build(BuildRequest(reversed: true)).Graphs);

        var forwardNode = Assert.Single(forward.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        var reversedNode = Assert.Single(reversedGraph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(ProjectNode(forwardNode), ProjectNode(reversedNode));

        var forwardPlan = DocumentationPlanner.Plan(forward);
        var reversedPlan = DocumentationPlanner.Plan(reversedGraph);

        // Reversed-input determinism must compare the full message projection in output order,
        // not just labels: id/key, source, target, label, kind, ordered evidence (id + certainty),
        // and the message certainty.
        Assert.Equal(
            forwardPlan.Diagram.Messages.Select(ProjectMessage).ToArray(),
            reversedPlan.Diagram.Messages.Select(ProjectMessage).ToArray());
    }

    private static string ProjectNode(ScenarioNode node)
        => string.Join('|',
            node.Id.Value,
            node.Detail,
            node.Certainty.ToString(),
            string.Join(',', node.Evidence.Select(item => $"{item.Id.Value}:{item.Certainty}")));

    private static string ProjectMessage(SeqDoc.Core.DiagramPlan.DiagramMessage message)
        => string.Join('|',
            message.Id.Value,
            message.Key,
            message.Source,
            message.Target,
            message.Label,
            message.Kind.ToString(),
            message.Certainty.ToString(),
            string.Join(',', message.Evidence.Select(item => $"{item.Id.Value}:{item.Certainty}")));

    [Fact]
    public void ConfiguredMethodRootDoesNotJoinACalleeAnchoredClientInvocationAndDoesNotOverExcludeRootLocalGenericCalls()
    {
        // The residual boundary of the configured-branch wiring: the join is root-local only
        // (AddServiceClientInvocations filters CallerMethod == RootMethod and requires the call site to
        // be a root-local DirectExact call). A ServiceClientInvocationFact anchored to a CALLEE of the
        // configured root is therefore neither joined as a ClientOperationInvocation nor turned into a
        // conservative diagnostic; the callee's client call at best stays a generic MethodCall under the
        // depth/traversal rules. The generic-MethodCall exclusion must also stay scoped to the proven
        // root-local client call site: a root-local non-client direct call still renders, and a genuine
        // root-local client invocation still joins exactly once with no duplicate.
        var calleeMethod = ScenarioTestFactory.RootDirectCallTarget;
        var calleeClientOperation = new OperationId("operation:v1:callee.notify");
        var validateOperation = new OperationId("operation:v1:root.validate");

        var calleeInvocation = new ServiceClientInvocationFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-client-invocation:callee"),
            Evidence = [ScenarioTestFactory.SourceEvidence("service-client-invocation-callee")],
            Certainty = CertaintyLevel.Exact,
            CallerMethod = calleeMethod,
            InvocationOperation = calleeClientOperation,
            ServiceContractType = ContractType,
            ServiceContractTypeSymbol = ContractTypeSymbol,
            ClientType = ClientType,
            ClientTypeSymbol = ClientTypeSymbol,
            OperationName = "NotifyAsync",
            OperationSymbol = new SymbolId($"symbol:v1:{ContractType}.NotifyAsync"),
            OperationKey = $"{ContractType}.NotifyAsync",
            ResultClaim = ClientInvocationResultClaimKind.Discarded,
            IsAwaited = false,
            ResultBindingName = null,
            DeclaredResultType = "System.Void",
        };
        var calleeBoundary = CreateBoundaryFact(idSuffix: "callee");

        var baseRequest = ScenarioTestFactory.CreateConfiguredRootDirectCallRequest(decisionGuarded: true);
        var request = baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(
                    CreateInvocationFact(), CreateBoundaryFact(), calleeInvocation, calleeBoundary),
                ProfileId = baseRequest.Profile.Id,
                ProgramIndexFingerprint = baseRequest.ProgramIndex.IndexFingerprint,
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.Equal(ScenarioRootKind.ConfiguredMethod, graph.RootKind);

        // The callee-anchored invocation never joins and never fabricates a diagnostic.
        Assert.DoesNotContain(graph.Nodes, node =>
            node.Kind == ScenarioNodeKind.ClientOperationInvocation && node.Operation == calleeClientOperation);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic =>
            diagnostic.Code == "SC-CLIENT-UNSUPPORTED-INVOCATION" && diagnostic.Detail.Contains("NotifyAsync"));

        // The genuine root-local client invocation still joins exactly once.
        var clientNode = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(ScenarioTestFactory.RootDirectCallOperation, clientNode.Operation);

        // The root-local non-client direct call is not over-excluded by the client-operation filter.
        Assert.Contains(graph.Nodes, node =>
            node.Kind == ScenarioNodeKind.MethodCall && node.Operation == validateOperation);
    }

    [Fact]
    public void ConfiguredMethodRootGuardedClientInvocationHasExactTopologyArmMembershipAndNoGenericDuplicate()
    {
        var baseRequest = WithRootPredicateFacts(
            ScenarioTestFactory.CreateConfiguredRootDirectCallRequest(decisionGuarded: true));
        var request = baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(CreateInvocationFact(), CreateBoundaryFact()),
                ProfileId = baseRequest.Profile.Id,
                ProgramIndexFingerprint = baseRequest.ProgramIndex.IndexFingerprint,
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.Equal(ScenarioRootKind.ConfiguredMethod, graph.RootKind);

        // Exactly one typed client node, parented to the action node by a Call edge.
        var clientNode = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(ScenarioTestFactory.RootDirectCallOperation, clientNode.Operation);
        var actionNode = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
        var edge = Assert.Single(graph.Edges, item => item.Target == clientNode.Id);
        Assert.Equal(actionNode.Id, edge.Source);
        Assert.Equal(ScenarioEdgeKind.Call, edge.Kind);

        var decision = Assert.Single(graph.Topology.Decisions);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
        var falseArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && !arm.IsTrue);
        Assert.Single(graph.Topology.Memberships,
            membership => membership.Arm == trueArm.Id && membership.ScenarioNode == clientNode.Id);
        Assert.DoesNotContain(graph.Topology.Memberships,
            membership => membership.Arm == falseArm.Id && membership.ScenarioNode == clientNode.Id);

        // The generic-MethodCall exclusion still applied under the guard: no generic node (and hence no
        // generic-node membership) for the same call-site operation.
        Assert.DoesNotContain(graph.Nodes, node =>
            node.Kind == ScenarioNodeKind.MethodCall && node.Operation == ScenarioTestFactory.RootDirectCallOperation);

        var plan = DocumentationPlanner.Plan(ScenarioTestFactory.WithExactOwnerWording(graph));
        Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "client-operation-invocation");
        Assert.Single(plan.Diagram.Messages, message => message.Label == "SendAsync");
        var fragment = Assert.Single(plan.Diagram.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Opt, fragment.Kind);
        var messageEdge = Assert.Single(graph.Edges, item => item.Target == clientNode.Id);
        var messageId = new DiagramPlanElementId("diagram-element:v1:message:" + messageEdge.Id.Value);
        Assert.Contains(messageId, fragment.MessageRefs);
        Assert.DoesNotContain(messageId, plan.Diagram.Sequence.MessageRefs);

    }

    [Fact]
    public void HttpRootGuardedClientInvocationUsesTheSameExactArmMembershipAndTypedReplacement()
    {
        var baseRequest = WithRootPredicateFacts(
            ScenarioTestFactory.CreateRootDirectCallRequest(decisionGuarded: true));
        var request = baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(CreateInvocationFact(), CreateBoundaryFact()),
                ProfileId = baseRequest.Profile.Id,
                ProgramIndexFingerprint = baseRequest.ProgramIndex.IndexFingerprint,
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.Equal(ScenarioRootKind.HttpEntryPoint, graph.RootKind);
        var clientNode = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall
            && node.Operation == ScenarioTestFactory.RootDirectCallOperation);
        var decision = Assert.Single(graph.Topology.Decisions);
        var trueArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && arm.IsTrue);
        var falseArm = Assert.Single(graph.Topology.Arms, arm => arm.Decision == decision.Id && !arm.IsTrue);
        Assert.Single(graph.Topology.Memberships,
            membership => membership.Arm == trueArm.Id && membership.ScenarioNode == clientNode.Id);
        Assert.DoesNotContain(graph.Topology.Memberships,
            membership => membership.Arm == falseArm.Id && membership.ScenarioNode == clientNode.Id);

        var plan = DocumentationPlanner.Plan(ScenarioTestFactory.WithExactOwnerWording(graph));
        Assert.Single(plan.Diagram.Messages, message => message.Label == "SendAsync");
        var fragment = Assert.Single(plan.Diagram.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Opt, fragment.Kind);
        var edge = Assert.Single(graph.Edges, item => item.Target == clientNode.Id);
        var messageId = new DiagramPlanElementId("diagram-element:v1:message:" + edge.Id.Value);
        Assert.Contains(messageId, fragment.MessageRefs);
        Assert.DoesNotContain(messageId, plan.Diagram.Sequence.MessageRefs);

    }

    [Fact]
    public void MatchingClientBoundaryReplacesTheGenericMethodCallNodeWithATypedClientOperationInvocationNode()
    {
        var request = CreateRequest(CreateInvocationFact(), CreateBoundaryFact());

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        var node = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(ScenarioTestFactory.RootDirectCallOperation, node.Operation);
        Assert.Equal(ScenarioTestFactory.ActionMethod, node.Method);
        Assert.Equal(ContractType, node.Presentation?.ContractTypeName);
        Assert.Equal(ClientType, node.Presentation?.ClientTypeName);
        Assert.Equal("SendAsync", node.Presentation?.CalledMemberName);
        Assert.Equal(ServiceClientKind.SourceClient, node.Presentation?.ClientKind);
        Assert.Equal(ClientInvocationResultClaimKind.ResultAssigned, node.Presentation?.ResultClaimKind);
        Assert.True(node.Presentation?.ResultIsAwaited);
        Assert.Equal("result", node.Presentation?.ResultBindingName);
        Assert.Equal("System.Double", node.Presentation?.DeclaredResultTypeName);
        Assert.Null(node.Presentation?.DeclaredFaultTypeNames);
        Assert.Equal(CertaintyLevel.Exact, node.Certainty);

        // The join edge connects the action to the new node, exactly like an ordinary direct call.
        var edge = Assert.Single(graph.Edges, edge => edge.Target == node.Id);
        Assert.Equal(ScenarioEdgeKind.Call, edge.Kind);

        // SC001 is an unrelated pre-existing diagnostic from the same base fixture (no DI-resolved
        // service implementation); the client-invocation join must not suppress or duplicate it.
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-UNSUPPORTED-INVOCATION");
    }

    [Fact]
    public void InvocationWithoutAMatchingClientBoundaryProducesNoNodeAndAConservativeDiagnostic()
    {
        // A proven invocation with no admitted source/generated client boundary (metadata-only or
        // unclassified client) must never fall back to a generic MethodCall node either: the metadata-
        // only-client contract row forbids treating it as an ordinary call once an invocation fact
        // exists for it.
        var request = CreateRequest(CreateInvocationFact());

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        var diagnostic = Assert.Single(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-UNSUPPORTED-INVOCATION");
        Assert.Contains(ClientType, diagnostic.Detail);
        Assert.Contains("SendAsync", diagnostic.Detail);
    }

    [Fact]
    public void UnclassifiedClientBoundaryNeverJoinsAsAnAdmittedInvocation()
    {
        var request = CreateRequest(CreateInvocationFact(), CreateBoundaryFact(ServiceClientKind.Unknown));

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-UNSUPPORTED-INVOCATION");
    }

    [Fact]
    public void MatchingFaultContractJoinsByExactOperationSymbolOntoTheInvocationNode()
    {
        var request = CreateRequest(CreateInvocationFact(), CreateBoundaryFact(), CreateFaultFact());

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        var node = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal("CoreWcfServices.NegativeSquareRootFault", node.Presentation?.DeclaredFaultTypeNames);
        Assert.Contains(node.Evidence, item => item.Artifact == "service-fault-contract");
    }

    [Fact]
    public void ConservativeClientBoundaryDegradesTheJoinedNodeCertaintyButRetainsBothContributorsEvidence()
    {
        var request = CreateRequest(CreateInvocationFact(), CreateBoundaryFact(certainty: CertaintyLevel.Conservative));

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        var node = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(CertaintyLevel.Conservative, node.Certainty);
        Assert.Contains(node.Evidence, item => item.Artifact == "service-client-invocation" && item.Certainty == CertaintyLevel.Exact);
        Assert.Contains(node.Evidence, item => item.Artifact == "service-client-boundary" && item.Certainty == CertaintyLevel.Conservative);
    }

    // --- B2: conflicting client-boundary anchors must never be silently admitted. ---

    [Fact]
    public void MultipleAgreeingClientBoundariesStillAdmitOneCoherentNode()
    {
        // Two independently admitted SourceClient boundaries for the same exact client/contract pair
        // (for example one contributed per operation, as CoreWcfServiceModel does) must still admit the
        // node normally, using the one coherent client kind they agree on.
        var request = CreateRequest(
            CreateInvocationFact(),
            CreateBoundaryFact(idSuffix: "root-direct-1"),
            CreateBoundaryFact(idSuffix: "root-direct-2"));

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        var node = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(ServiceClientKind.SourceClient, node.Presentation?.ClientKind);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-CONFLICTING-BOUNDARY");
    }

    [Fact]
    public void ConflictingClientKindBoundariesForTheSameClientContractPairWithholdTheNodeAndDiagnoseInstead()
    {
        // One boundary classifies SourceClient and another classifies GeneratedClient for the exact
        // same client/contract pair: no single coherent client kind can be admitted, so the node must
        // be withheld in favor of a conservative diagnostic instead of arbitrarily picking one.
        var request = CreateRequest(
            CreateInvocationFact(),
            CreateBoundaryFact(ServiceClientKind.SourceClient, idSuffix: "root-direct-1"),
            CreateBoundaryFact(ServiceClientKind.GeneratedClient, idSuffix: "root-direct-2"));

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        var diagnostic = Assert.Single(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-CONFLICTING-BOUNDARY");
        Assert.Contains(ClientType, diagnostic.Detail);
        Assert.Contains("SourceClient", diagnostic.Detail);
        Assert.Contains("GeneratedClient", diagnostic.Detail);
    }

    // --- B3: two facts admitted for the same call site (InvocationOperation) must never emit two nodes. ---

    [Fact]
    public void DuplicateAgreeingInvocationFactsForTheSameCallSiteStillAdmitExactlyOneNode()
    {
        // Two ServiceClientInvocationFacts that agree on every field but carry distinct fact IDs (a
        // duplicate emission for the same real compiler call site) must still produce exactly one node,
        // never one per fact.
        var request = CreateRequest(
            CreateInvocationFact(idSuffix: "duplicate-1"),
            CreateInvocationFact(idSuffix: "duplicate-2"),
            CreateBoundaryFact());

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        var node = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.Equal(ScenarioTestFactory.RootDirectCallOperation, node.Operation);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-CONFLICTING-INVOCATION");
    }

    [Fact]
    public void ConflictingInvocationFactsForTheSameCallSiteWithholdTheNodeAndDiagnoseInstead()
    {
        // Two ServiceClientInvocationFacts anchored to the same InvocationOperation (the real identity
        // of one compiler call site) but disagreeing on the operation name/result-claim shape must never
        // silently pick one: the call site is withheld in favor of a conservative diagnostic, the same
        // fail-closed posture as B2's conflicting client-kind boundary.
        var request = CreateRequest(
            CreateInvocationFact(idSuffix: "conflict-1", operationName: "SendAsync"),
            CreateInvocationFact(idSuffix: "conflict-2", operationName: "SubtractAsync"),
            CreateBoundaryFact());

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        var diagnostic = Assert.Single(graph.Diagnostics, diagnostic => diagnostic.Code == "SC-CLIENT-CONFLICTING-INVOCATION");
        Assert.Contains(ScenarioTestFactory.RootDirectCallOperation.Value, diagnostic.Detail);
    }

    // --- B5: repeated-call chronology and reversed-input determinism at the observable Scenario layer. ---

    [Fact]
    public void TwoDistinctClientInvocationCallSitesAdmitDeterministicallyRegardlessOfFrameworkFactInputOrder()
    {
        var validateOperation = new OperationId("operation:v1:root.validate");
        var validateClientType = "CoreWcfServices.ValidatorSourceClient";
        var validateClientTypeSymbol = new SymbolId($"symbol:v1:{validateClientType}");
        var validateContractType = "CoreWcfServices.IValidatorService";
        var validateContractTypeSymbol = new SymbolId($"symbol:v1:{validateContractType}");
        var validateOperationSymbol = new SymbolId($"symbol:v1:{validateContractType}.ValidateAsync");

        var firstInvocation = CreateInvocationFact();
        var firstBoundary = CreateBoundaryFact();
        var secondInvocation = new ServiceClientInvocationFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-client-invocation:validate"),
            Evidence = [ScenarioTestFactory.SourceEvidence("service-client-invocation-validate")],
            Certainty = CertaintyLevel.Exact,
            CallerMethod = ScenarioTestFactory.ActionMethod,
            InvocationOperation = validateOperation,
            ServiceContractType = validateContractType,
            ServiceContractTypeSymbol = validateContractTypeSymbol,
            ClientType = validateClientType,
            ClientTypeSymbol = validateClientTypeSymbol,
            OperationName = "ValidateAsync",
            OperationSymbol = validateOperationSymbol,
            OperationKey = $"{validateContractType}.ValidateAsync",
            ResultClaim = ClientInvocationResultClaimKind.Discarded,
            IsAwaited = false,
            ResultBindingName = null,
            DeclaredResultType = "System.Void",
        };
        var secondBoundary = new ServiceClientBoundaryFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-client-boundary:validate"),
            Evidence = [ScenarioTestFactory.SourceEvidence("service-client-boundary-validate")],
            Certainty = CertaintyLevel.Exact,
            ServiceContractType = validateContractType,
            ServiceContractTypeSymbol = validateContractTypeSymbol,
            ClientType = validateClientType,
            ClientTypeSymbol = validateClientTypeSymbol,
            ClientKind = ServiceClientKind.SourceClient,
        };

        var baseRequest = WithRootPredicateFacts(
            ScenarioTestFactory.CreateRootDirectCallRequest(decisionGuarded: true));
        ScenarioAnalysisRequest BuildRequest(BehaviorFact[] facts) => baseRequest with
        {
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.AddRange(facts),
                ProfileId = baseRequest.Profile.Id,
                ProgramIndexFingerprint = baseRequest.ProgramIndex.IndexFingerprint,
            },
        };

        var forward = ScenarioGraphBuilder.Build(BuildRequest(
            [firstInvocation, firstBoundary, secondInvocation, secondBoundary]));
        var reversed = ScenarioGraphBuilder.Build(BuildRequest(
            [secondBoundary, secondInvocation, firstBoundary, firstInvocation]));

        var forwardGraph = Assert.Single(forward.Graphs);
        var reversedGraph = Assert.Single(reversed.Graphs);

        var forwardNodes = forwardGraph.Nodes.Where(node => node.Kind == ScenarioNodeKind.ClientOperationInvocation).ToArray();
        var reversedNodes = reversedGraph.Nodes.Where(node => node.Kind == ScenarioNodeKind.ClientOperationInvocation).ToArray();
        Assert.Equal(2, forwardNodes.Length);

        // Node identity, evidence, and certainty never depend on framework-fact input order.
        Assert.Equal(
            forwardNodes.Select(node => node.Id.Value).OrderBy(id => id, StringComparer.Ordinal),
            reversedNodes.Select(node => node.Id.Value).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(
            forwardNodes.Select(node => node.Detail).OrderBy(detail => detail, StringComparer.Ordinal),
            reversedNodes.Select(node => node.Detail).OrderBy(detail => detail, StringComparer.Ordinal));
        Assert.Equal(
            forwardNodes.Select(node => node.Certainty).OrderBy(certainty => certainty),
            reversedNodes.Select(node => node.Certainty).OrderBy(certainty => certainty));
        Assert.Equal(
            forwardNodes.Select(node => node.Evidence.Select(item => item.Id.Value).OrderBy(id => id, StringComparer.Ordinal)),
            reversedNodes.Select(node => node.Evidence.Select(item => item.Id.Value).OrderBy(id => id, StringComparer.Ordinal)));

        // The observable chronology claim lives at the planner/message layer (SequenceOrdinal-driven),
        // not the Scenario Graph's own hash-ordered node array: the validate call site (BlockOrdinal 0)
        // always precedes the transfer call site (BlockOrdinal 1) in the rendered message order,
        // regardless of which order the underlying framework facts were supplied in.
        var forwardPlan = DocumentationPlanner.Plan(forwardGraph);
        var reversedPlan = DocumentationPlanner.Plan(reversedGraph);
        var forwardLabels = forwardPlan.Diagram.Messages.Select(message => message.Label).ToArray();
        var reversedLabels = reversedPlan.Diagram.Messages.Select(message => message.Label).ToArray();
        Assert.Equal(forwardLabels, reversedLabels);
        Assert.Contains("ValidateAsync", forwardLabels);
        Assert.Contains("SendAsync", forwardLabels);
        Assert.True(
            Array.IndexOf(forwardLabels, "ValidateAsync") < Array.IndexOf(forwardLabels, "SendAsync"),
            $"Expected the source-ordered validate call (BlockOrdinal 0) before the transfer call (BlockOrdinal 1); actual order: {string.Join(", ", forwardLabels)}.");
    }
}
