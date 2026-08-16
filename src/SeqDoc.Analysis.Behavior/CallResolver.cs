using System.Collections.Immutable;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Analysis.Behavior;

/// <summary>
/// Resolves call targets to exact direct targets or conservative CHA candidate sets and builds the
/// canonical call graph.
/// </summary>
public static class CallResolver
{
    public static CallGraph Build(
        BehaviorAnalysisRequest request,
        ImmutableArray<MethodFlowSnapshot> flows)
    {
        ArgumentNullException.ThrowIfNull(request);

        var callSites = ImmutableArray.CreateBuilder<CallSite>();
        var edges = ImmutableArray.CreateBuilder<CallGraphEdge>();
        var indexedMethods = request.ProgramIndex.Methods.Select(method => method.Id).ToHashSet();
        var concreteMethods = request.ProgramIndex.Methods
            .Where(method => method.BodyFingerprint is not null)
            .Select(method => method.Id)
            .ToHashSet();
        var interfaceTypes = request.ProgramIndex.Types
            .Where(type => type.Kind == ProgramTypeKind.Interface)
            .Select(type => type.Id)
            .ToHashSet();
        var implementations = BuildInterfaceImplementations(request);
        var overrides = BuildMethodOverrides(request);
        var methodContainingTypes = request.ProgramIndex.Methods.ToDictionary(
            method => method.Id,
            method => method.ContainingType);

        foreach (var flow in flows.OrderBy(flow => flow.Method.Value, StringComparer.Ordinal))
        {
            var invocationOrdinal = 0;
            foreach (var invocation in flow.Nodes
                         .OfType<InvocationFlowNode>()
                         .OrderBy(node => node.Id.Value, StringComparer.Ordinal))
            {
                var id = StableIdentity.CreateCallSiteId(new CallSiteIdentityDescriptor(
                    flow.Method,
                    invocation.Operation,
                    invocationOrdinal));
                invocationOrdinal++;
                var callSite = ResolveCallSite(
                    flow.Method,
                    invocation,
                    id,
                    indexedMethods,
                    concreteMethods,
                    interfaceTypes,
                    methodContainingTypes,
                    implementations,
                    overrides,
                    invocationOrdinal);
                callSites.Add(callSite);
                foreach (var candidate in callSite.Resolution.Candidates)
                {
                    edges.Add(new CallGraphEdge(flow.Method, id, candidate));
                }
            }
        }

        return new CallGraph(
            edges
                .OrderBy(edge => edge.Caller.Value, StringComparer.Ordinal)
                .ThenBy(edge => edge.CallSite.Value, StringComparer.Ordinal)
                .ThenBy(edge => edge.CandidateTarget.Value, StringComparer.Ordinal)
                .ToImmutableArray(),
            callSites
                .OrderBy(site => site.ContainingMethod.Value, StringComparer.Ordinal)
                .ThenBy(site => site.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray());
    }

    private static CallSite ResolveCallSite(
        MethodId containingMethod,
        InvocationFlowNode invocation,
        CallSiteId id,
        HashSet<MethodId> indexedMethods,
        HashSet<MethodId> concreteMethods,
        HashSet<SymbolId> interfaceTypes,
        Dictionary<MethodId, SymbolId> methodContainingTypes,
        Dictionary<MethodId, HashSet<MethodId>> implementations,
        Dictionary<MethodId, HashSet<MethodId>> overrides,
        int invocationOrdinal)
    {
        var declaredTarget = invocation.Target;
        var callKind = invocation.IsDelegateOrEventInvoke
            ? CallKind.DelegateOrEvent
            : invocation.IsDynamic
                ? CallKind.Dynamic
                : invocation.IsConstructor
                    ? CallKind.Constructor
                    : invocation.IsStatic
                        ? CallKind.Static
                        : CallKind.Instance;
        var evidence = invocation.Evidence;

        if (invocation.IsDelegateOrEventInvoke)
        {
            return new CallSite(
                id,
                containingMethod,
                invocation.Operation,
                callKind,
                declaredTarget,
                new CallTargetResolution(
                    CallResolutionKind.Unknown,
                    [],
                    "unbounded delegate or event dispatch",
                    IsComplete: false,
                    [],
                    evidence,
                    CertaintyLevel.Unknown),
                evidence,
                CertaintyLevel.Unknown);
        }

        if (invocation.IsDynamic)
        {
            var diagnostic = CreateCallDiagnostic(containingMethod, invocation.Operation, invocationOrdinal);
            return new CallSite(
                id,
                containingMethod,
                invocation.Operation,
                callKind,
                declaredTarget,
                new CallTargetResolution(
                    CallResolutionKind.Unknown,
                    [],
                    "dynamic dispatch",
                    IsComplete: false,
                    [diagnostic],
                    evidence,
                    CertaintyLevel.Unknown),
                evidence,
                CertaintyLevel.Unknown);
        }

        if (declaredTarget is not { } target)
        {
            return new CallSite(
                id,
                containingMethod,
                invocation.Operation,
                callKind,
                null,
                new CallTargetResolution(
                    CallResolutionKind.Unknown,
                    [],
                    "no compiler-declared target",
                    IsComplete: false,
                    [],
                    evidence,
                    CertaintyLevel.Unknown),
                evidence,
                CertaintyLevel.Unknown);
        }

        if (!invocation.IsDispatchable)
        {
            return new CallSite(
                id,
                containingMethod,
                invocation.Operation,
                callKind,
                target,
                new CallTargetResolution(
                    CallResolutionKind.DirectExact,
                    ImmutableArray.Create(target),
                    "compiler-proven non-virtual target",
                    IsComplete: true,
                    [],
                    evidence,
                    CertaintyLevel.Exact),
                evidence,
                CertaintyLevel.Exact);
        }

        var candidates = FindChaCandidates(
            target,
            indexedMethods,
            concreteMethods,
            interfaceTypes,
            methodContainingTypes,
            implementations,
            overrides);
        if (candidates.IsEmpty)
        {
            return new CallSite(
                id,
                containingMethod,
                invocation.Operation,
                callKind,
                target,
                new CallTargetResolution(
                    CallResolutionKind.UnresolvedCandidateSet,
                    [],
                    "no legal implementation found in loaded scope",
                    IsComplete: false,
                    [],
                    evidence,
                    CertaintyLevel.Conservative),
                evidence,
                CertaintyLevel.Conservative);
        }

        return new CallSite(
            id,
            containingMethod,
            invocation.Operation,
            callKind,
            target,
            new CallTargetResolution(
                CallResolutionKind.Cha,
                candidates,
                "loaded source scope",
                IsComplete: true,
                [],
                evidence,
                CertaintyLevel.Conservative),
            evidence,
            CertaintyLevel.Conservative);
    }

    private static AnalysisDiagnostic CreateCallDiagnostic(
        MethodId containingMethod,
        OperationId invocationOperation,
        int invocationOrdinal)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "BD3001",
            AnalysisStage.BaselineIndex,
            null,
            $"{containingMethod.Value}@{invocationOperation.Value}",
            invocationOrdinal));
        return new AnalysisDiagnostic(
            id,
            "BD3001",
            DiagnosticSeverity.Warning,
            AnalysisStage.BaselineIndex,
            "Dynamic dispatch has no statically decidable call target.",
            new DiagnosticLocation("call resolution", symbol: new SymbolId(containingMethod.Value)),
            "The dynamic invocation is not bound to a specific method by the compiler.",
            "The call target is unknown until runtime.",
            "Treat the call as having an unbounded target set.",
            CertaintyLevel.Exact);
    }

    private static ImmutableArray<MethodId> FindChaCandidates(
        MethodId target,
        HashSet<MethodId> indexedMethods,
        HashSet<MethodId> concreteMethods,
        HashSet<SymbolId> interfaceTypes,
        Dictionary<MethodId, SymbolId> methodContainingTypes,
        Dictionary<MethodId, HashSet<MethodId>> implementations,
        Dictionary<MethodId, HashSet<MethodId>> overrides)
    {
        var isInterfaceMember = methodContainingTypes.TryGetValue(target, out var containingType)
            && interfaceTypes.Contains(containingType);
        if (isInterfaceMember)
        {
            if (!implementations.TryGetValue(target, out var impls))
            {
                return [];
            }

            return impls
                .Where(indexedMethods.Contains)
                .Where(concreteMethods.Contains)
                .Distinct()
                .OrderBy(methodId => methodId.Value, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        var candidates = new HashSet<MethodId>();
        if (concreteMethods.Contains(target))
        {
            candidates.Add(target);
        }

        if (overrides.TryGetValue(target, out var derived))
        {
            candidates.UnionWith(derived);
        }

        return candidates
            .Where(indexedMethods.Contains)
            .Where(concreteMethods.Contains)
            .OrderBy(methodId => methodId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static Dictionary<MethodId, HashSet<MethodId>> BuildInterfaceImplementations(BehaviorAnalysisRequest request)
    {
        var implementations = new Dictionary<MethodId, HashSet<MethodId>>();
        foreach (var fact in request.BehaviorInput.InterfaceImplementations)
        {
            if (!implementations.TryGetValue(fact.InterfaceMember, out var set))
            {
                set = new HashSet<MethodId>();
                implementations[fact.InterfaceMember] = set;
            }

            set.Add(fact.Implementation);
        }

        return implementations;
    }

    private static Dictionary<MethodId, HashSet<MethodId>> BuildMethodOverrides(BehaviorAnalysisRequest request)
    {
        var overrides = new Dictionary<MethodId, HashSet<MethodId>>();
        foreach (var fact in request.BehaviorInput.MethodOverrides)
        {
            if (!overrides.TryGetValue(fact.BaseMethod, out var set))
            {
                set = new HashSet<MethodId>();
                overrides[fact.BaseMethod] = set;
            }

            set.Add(fact.Override);
        }

        return overrides;
    }
}