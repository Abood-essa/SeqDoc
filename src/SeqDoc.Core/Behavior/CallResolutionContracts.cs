using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Behavior;

/// <summary>Classifies how a call target was resolved.</summary>
public enum CallResolutionKind
{
    Unknown,
    DirectExact,
    Cha,
    UnresolvedCandidateSet,
}

/// <summary>Classifies the syntactic shape of one call site.</summary>
public enum CallKind
{
    Unknown,
    Static,
    Instance,
    Constructor,
    DelegateOrEvent,
    Dynamic,
    Reflection,
    Extension,
}

/// <summary>Describes one call site with its declared target and possible runtime candidates.</summary>
public sealed record CallSite(
    CallSiteId Id,
    MethodId ContainingMethod,
    OperationId InvocationOperation,
    CallKind Kind,
    MethodId? DeclaredTarget,
    CallTargetResolution Resolution,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>
/// Describes how a call resolves. Candidate arrays are ordered by full MethodId, never likelihood.
/// </summary>
public sealed record CallTargetResolution(
    CallResolutionKind Kind,
    ImmutableArray<MethodId> Candidates,
    string? Scope,
    bool IsComplete,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Describes one canonical call-graph edge from caller through a call site to a candidate target.</summary>
public sealed record CallGraphEdge(
    MethodId Caller,
    CallSiteId CallSite,
    MethodId CandidateTarget);

/// <summary>
/// Contains one canonical call-edge fact set and deterministic forward and reverse projections.
/// </summary>
public sealed record CallGraph(
    ImmutableArray<CallGraphEdge> Edges,
    ImmutableArray<CallSite> CallSites);

/// <summary>Carries profile-wide observed instantiation facts for RTA foundations.</summary>
public sealed record RtaFoundation(
    ImmutableArray<TypeInstantiationFact> Instantiations,
    bool HasExplicitRoots);
