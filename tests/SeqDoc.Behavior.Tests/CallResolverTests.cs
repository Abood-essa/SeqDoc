using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Behavior.Tests;

public sealed class CallResolverTests
{
    private static readonly CompilationProfile Profile = CompilationProfile.Create("Dispatch.csproj", "Release", "net10.0");
    private static readonly MethodId Caller = new("method:v1:caller");

    [Fact]
    public void NonDispatchableCallResolvesDirectExact()
    {
        var target = new MethodId("method:v1:target");
        var request = CreateRequest(target: target, dispatchable: false);
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: false));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallResolutionKind.DirectExact, site.Resolution.Kind);
        Assert.Equal(target, Assert.Single(site.Resolution.Candidates));
        Assert.Equal(CertaintyLevel.Exact, site.Resolution.Certainty);
        Assert.Single(graph.Edges);
    }

    [Fact]
    public void DispatchableCallProducesOrderedChaCandidates()
    {
        var declaringType = new SymbolId("symbol:v1:contract");
        var implementationType = new SymbolId("symbol:v1:impl");
        var target = new MethodId("method:v1:interface.run");
        var implementation = new MethodId("method:v1:impl.run");
        var request = CreateIndexRequest(
            declaringType: declaringType,
            implementationType: implementationType,
            target: target,
            implementation: implementation);
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: true));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallResolutionKind.Cha, site.Resolution.Kind);
        Assert.Equal(implementation, Assert.Single(site.Resolution.Candidates));
        Assert.Equal(CertaintyLevel.Conservative, site.Resolution.Certainty);
        var edge = Assert.Single(graph.Edges);
        Assert.Equal(Caller, edge.Caller);
        Assert.Equal(implementation, edge.CandidateTarget);
    }

    [Fact]
    public void DispatchableCallWithNoImplementationsIsUnresolvedCandidateSet()
    {
        var declaringType = new SymbolId("symbol:v1:contract");
        var target = new MethodId("method:v1:interface.run");
        var index = new ProgramIndexSnapshot(
            1,
            "test",
            Profile,
            [],
            [],
            [],
            ImmutableArray.Create(new ProgramType(declaringType, new ProjectId("project:v1:test"), new SymbolId("symbol:v1:ns"), "IContract", ProgramTypeKind.Interface, null, [], "sig", [])),
            [],
            ImmutableArray.Create(CreateMethod(target, declaringType, "Run")),
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "fingerprint");
        var request = new BehaviorAnalysisRequest(index, CreateInput());
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: true));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallResolutionKind.UnresolvedCandidateSet, site.Resolution.Kind);
        Assert.Empty(site.Resolution.Candidates);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void CandidateOrderingIsByFullMethodIdNeverFirstCandidateWins()
    {
        var declaringType = new SymbolId("symbol:v1:contract");
        var first = new SymbolId("symbol:v1:first");
        var second = new SymbolId("symbol:v1:second");
        var target = new MethodId("method:v1:interface.run");
        var firstMethod = new MethodId("method:v1:zzz-first");
        var secondMethod = new MethodId("method:v1:aaa-second");
        var request = CreateIndexRequest(
            declaringType: declaringType,
            implementationType: first,
            target: target,
            implementation: firstMethod);
        request = request with { ProgramIndex = request.ProgramIndex with { Types = request.ProgramIndex.Types.Add(CreateType(second, declaringType)) } };
        request = request with { ProgramIndex = request.ProgramIndex with { Methods = request.ProgramIndex.Methods.Add(CreateMethod(secondMethod, second, "Run")) } };
        request = request with
        {
            BehaviorInput = request.BehaviorInput with
            {
                InterfaceImplementations = request.BehaviorInput.InterfaceImplementations
                    .Add(new InterfaceImplementationFact(secondMethod, target, [], CertaintyLevel.Exact)),
            },
        };
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: true));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(2, site.Resolution.Candidates.Length);
        Assert.Equal(
            site.Resolution.Candidates.Select(candidate => candidate.Value).Order(StringComparer.Ordinal),
            site.Resolution.Candidates.Select(candidate => candidate.Value));
        Assert.Equal(secondMethod, site.Resolution.Candidates[0]);
    }

    [Fact]
    public void NonVirtualInstanceCallIsClassifiedInstanceAndDirectExact()
    {
        var target = new MethodId("method:v1:sealed.method");
        var request = CreateRequest(target: target, dispatchable: false);
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: false));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallKind.Instance, site.Kind);
        Assert.Equal(CallResolutionKind.DirectExact, site.Resolution.Kind);
    }

    [Fact]
    public void StaticCallIsClassifiedStatic()
    {
        var target = new MethodId("method:v1:static.method");
        var request = CreateRequest(target: target, dispatchable: false);
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: false, isStatic: true));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallKind.Static, site.Kind);
    }

    [Fact]
    public void DelegateInvokeIsUnknownResolution()
    {
        var target = new MethodId("method:v1:delegate.invoke");
        var request = CreateRequest(target: target, dispatchable: true);
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: true, isDelegate: true));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallKind.DelegateOrEvent, site.Kind);
        Assert.Equal(CallResolutionKind.Unknown, site.Resolution.Kind);
        Assert.Equal(CertaintyLevel.Unknown, site.Resolution.Certainty);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void DynamicInvokeIsUnknownResolutionWithDiagnostic()
    {
        var request = CreateRequest(target: null, dispatchable: false);
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", null, dispatchable: false, isDynamic: true));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallKind.Dynamic, site.Kind);
        Assert.Equal(CallResolutionKind.Unknown, site.Resolution.Kind);
        Assert.Contains(site.Resolution.Diagnostics, diagnostic => diagnostic.Code == "BD3001");
    }

    [Fact]
    public void DefaultInterfaceMethodIsAChaCandidate()
    {
        var declaringType = new SymbolId("symbol:v1:interface");
        var implementationType = new SymbolId("symbol:v1:impl");
        var target = new MethodId("method:v1:interface.default");
        var implementation = new MethodId("method:v1:impl.abstract");
        var index = new ProgramIndexSnapshot(
            1,
            "test",
            Profile,
            [],
            [],
            [],
            ImmutableArray.Create(
                CreateType(implementationType, declaringType),
                new ProgramType(declaringType, new ProjectId("project:v1:test"), new SymbolId("symbol:v1:ns"), "IThing", ProgramTypeKind.Interface, null, [], "sig", [])),
            [],
            ImmutableArray.Create(
                CreateMethod(target, declaringType, "ProcessDefault"),
                CreateMethod(implementation, implementationType, "ProcessAbstract")),
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "fingerprint");
        var input = new ExtractedBehaviorInput(
            Profile,
            "fingerprint",
            [],
            new ExtractedTypeHierarchy([], true),
            [],
            ImmutableArray.Create(new InterfaceImplementationFact(
                target,
                target,
                [],
                CertaintyLevel.Exact)),
            [],
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(index, input);
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: true));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallResolutionKind.Cha, site.Resolution.Kind);
        Assert.Contains(target, site.Resolution.Candidates);
    }

    [Fact]
    public void ClassVirtualCallIncludesDeclaredAndOverrides()
    {
        var baseType = new SymbolId("symbol:v1:base");
        var derivedType = new SymbolId("symbol:v1:derived");
        var target = new MethodId("method:v1:base.compute");
        var derived = new MethodId("method:v1:derived.compute");
        var index = new ProgramIndexSnapshot(
            1,
            "test",
            Profile,
            [],
            [],
            [],
            ImmutableArray.Create(
                new ProgramType(baseType, new ProjectId("project:v1:test"), new SymbolId("symbol:v1:ns"), "Base", ProgramTypeKind.Class, null, [], "sig", []),
                new ProgramType(derivedType, new ProjectId("project:v1:test"), new SymbolId("symbol:v1:ns"), "Derived", ProgramTypeKind.Class, baseType, [], "sig", [])),
            [],
            ImmutableArray.Create(CreateMethod(target, baseType, "Compute"), CreateMethod(derived, derivedType, "Compute")),
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "fingerprint");
        var input = new ExtractedBehaviorInput(
            Profile,
            "fingerprint",
            [],
            new ExtractedTypeHierarchy([], true),
            [],
            [],
            ImmutableArray.Create(new MethodOverrideFact(derived, target, [], CertaintyLevel.Exact)),
            [],
            string.Empty);
        var request = new BehaviorAnalysisRequest(index, input);
        var flow = CreateFlow(CreateInvocation("behavior-operation:v1:invoke", target, dispatchable: true));

        var graph = CallResolver.Build(request, ImmutableArray.Create(flow));

        var site = Assert.Single(graph.CallSites);
        Assert.Equal(CallResolutionKind.Cha, site.Resolution.Kind);
        Assert.Equal(2, site.Resolution.Candidates.Length);
        Assert.Contains(target, site.Resolution.Candidates);
        Assert.Contains(derived, site.Resolution.Candidates);
    }

    private static BehaviorAnalysisRequest CreateIndexRequest(
        SymbolId declaringType,
        SymbolId implementationType,
        MethodId target,
        MethodId implementation)
    {
        var index = new ProgramIndexSnapshot(
            1,
            "test",
            Profile,
            [],
            [],
            [],
            ImmutableArray.Create(
                CreateType(implementationType, declaringType),
                new ProgramType(declaringType, new ProjectId("project:v1:test"), new SymbolId("symbol:v1:ns"), "IContract", ProgramTypeKind.Interface, null, [], "sig", [])),
            [],
            ImmutableArray.Create(CreateMethod(target, declaringType, "Run"), CreateMethod(implementation, implementationType, "Run")),
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "fingerprint");
        var input = new ExtractedBehaviorInput(
            Profile,
            "fingerprint",
            [],
            new ExtractedTypeHierarchy([], true),
            [],
            ImmutableArray.Create(new InterfaceImplementationFact(implementation, target, [], CertaintyLevel.Exact)),
            [],
            [],
            string.Empty);
        return new BehaviorAnalysisRequest(index, input);
    }

    private static BehaviorAnalysisRequest CreateRequest(MethodId? target, bool dispatchable)
    {
        var index = new ProgramIndexSnapshot(
            1,
            "test",
            Profile,
            [],
            [],
            [],
            [],
            [],
            target is null ? [] : ImmutableArray.Create(CreateMethod(target.Value, new SymbolId("symbol:v1:contract"), "Run")),
            [],
            [],
            [],
            [],
            [],
            "manifest",
            "fingerprint");
        return new BehaviorAnalysisRequest(index, CreateInput());
    }

    private static ProgramType CreateType(SymbolId id, SymbolId? baseType) =>
        new(id, new ProjectId("project:v1:test"), new SymbolId("symbol:v1:ns"), "Type", ProgramTypeKind.Class, baseType, [], "sig", []);

    private static ProgramMethod CreateMethod(MethodId id, SymbolId containingType, string name) =>
        new(
            id,
            new SymbolId($"symbol:v1:{name}"),
            containingType,
            name,
            name,
            [],
            "System.Void",
            "sig",
            "body",
            []);

    private static ExtractedBehaviorInput CreateInput() =>
        new(Profile, "fingerprint", [], new ExtractedTypeHierarchy([], true), [], [], [], [], string.Empty);

    private static MethodFlowSnapshot CreateFlow(params InvocationFlowNode[] invocations)
    {
        var nodes = ImmutableArray.CreateBuilder<FlowNode>();
        nodes.Add(new EntryFlowNode(
            StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(Caller, "Entry", 0, 0, "entry")),
            Caller,
            [],
            CertaintyLevel.Exact));
        nodes.AddRange(invocations);
        nodes.Add(new ExitFlowNode(
            StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(Caller, "Exit", int.MaxValue, int.MaxValue, "exit")),
            Caller,
            [],
            CertaintyLevel.Exact));
        return new MethodFlowSnapshot(
            Caller,
            "body",
            nodes.ToImmutable(),
            [],
            [],
            [],
            new LocalValueGraph([], []),
            [],
            null,
            [],
            "flow");
    }

    private static InvocationFlowNode CreateInvocation(
        string operationId,
        MethodId? target,
        bool dispatchable,
        bool isStatic = false,
        bool isConstructor = false,
        bool isDynamic = false,
        bool isDelegate = false) =>
        new(
            StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(Caller, "Invocation", 0, 1, "invocation")),
            Caller,
            new OperationId(operationId),
            target,
            dispatchable,
            isDelegate,
            isStatic,
            isConstructor,
            isDynamic,
            [],
            CertaintyLevel.Exact);
}
