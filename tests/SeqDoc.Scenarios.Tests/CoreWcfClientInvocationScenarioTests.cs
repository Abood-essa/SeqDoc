using SeqDoc.Analysis.Scenarios;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
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

    private static ServiceClientInvocationFact CreateInvocationFact(CertaintyLevel certainty = CertaintyLevel.Exact)
        => new()
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-client-invocation:root-direct"),
            Evidence = [ScenarioTestFactory.SourceEvidence("service-client-invocation")],
            Certainty = certainty,
            CallerMethod = ScenarioTestFactory.ActionMethod,
            InvocationOperation = ScenarioTestFactory.RootDirectCallOperation,
            ServiceContractType = ContractType,
            ServiceContractTypeSymbol = ContractTypeSymbol,
            ClientType = ClientType,
            ClientTypeSymbol = ClientTypeSymbol,
            OperationName = "SendAsync",
            OperationSymbol = OperationSymbol,
            OperationKey = $"{ContractType}.SendAsync",
            ResultClaim = ClientInvocationResultClaimKind.ResultAssigned,
            IsAwaited = true,
            ResultBindingName = "result",
            DeclaredResultType = "System.Double",
        };

    private static ServiceClientBoundaryFact CreateBoundaryFact(
        ServiceClientKind clientKind = ServiceClientKind.SourceClient,
        CertaintyLevel certainty = CertaintyLevel.Exact)
        => new()
        {
            Id = new BehaviorFactId("behavior-fact:v1:service-client-boundary:root-direct"),
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
}
