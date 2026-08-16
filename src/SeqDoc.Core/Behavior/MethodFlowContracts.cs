using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Behavior;

/// <summary>Classifies a method-flow edge using a closed SeqDoc vocabulary.</summary>
public enum FlowEdgeKind
{
    Unknown,
    Normal,
    True,
    False,
    SwitchCase,
    SwitchDefault,
    Return,
    Throw,
    Rethrow,
    ExceptionHandler,
    Filter,
    Finally,
    LoopBack,
}

/// <summary>Classifies one structural method exit.</summary>
public enum FlowOutcomeKind
{
    Unknown,
    NormalCompletion,
    ExplicitReturn,
    EscapingThrow,
    NoNormalExit,
}

/// <summary>Classifies a flow region in a normalized method flow.</summary>
public enum FlowRegionKind
{
    Unknown,
    Root,
    Try,
    Catch,
    Filter,
    Finally,
    NaturalLoop,
    IrreducibleLoop,
}

/// <summary>Base record for all method-flow nodes. Derived records carry the typed shape.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "nodeKind")]
[JsonDerivedType(typeof(EntryFlowNode), "Entry")]
[JsonDerivedType(typeof(ExitFlowNode), "Exit")]
[JsonDerivedType(typeof(OperationFlowNode), "Operation")]
[JsonDerivedType(typeof(InvocationFlowNode), "Invocation")]
[JsonDerivedType(typeof(DecisionFlowNode), "Decision")]
[JsonDerivedType(typeof(ReturnFlowNode), "Return")]
[JsonDerivedType(typeof(ThrowFlowNode), "Throw")]
[JsonDerivedType(typeof(AwaitFlowNode), "Await")]
[JsonDerivedType(typeof(LoopNode), "Loop")]
[JsonDerivedType(typeof(UnknownOperationFlowNode), "UnknownOperation")]
public abstract record FlowNode(
    FlowNodeId Id,
    MethodId Method,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty)
{
    public abstract FlowNodeKind Kind { get; }
}

public enum FlowNodeKind
{
    Unknown,
    Entry,
    Exit,
    Operation,
    Invocation,
    Decision,
    Return,
    Throw,
    Await,
    Loop,
    UnknownOperation,
}

public sealed record EntryFlowNode(
    FlowNodeId Id,
    MethodId Method,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    public override FlowNodeKind Kind => FlowNodeKind.Entry;
}

public sealed record ExitFlowNode(
    FlowNodeId Id,
    MethodId Method,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    public override FlowNodeKind Kind => FlowNodeKind.Exit;
}

public sealed record OperationFlowNode(
    FlowNodeId Id,
    MethodId Method,
    OperationId Operation,
    ExtractedOperationKind OperationKind,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    public override FlowNodeKind Kind => FlowNodeKind.Operation;
}

public sealed record InvocationFlowNode : FlowNode
{
    public InvocationFlowNode(
        FlowNodeId Id,
        MethodId Method,
        OperationId Operation,
        MethodId? Target,
        bool IsDispatchable,
        bool IsDelegateOrEventInvoke,
        bool IsStatic,
        bool IsConstructor,
        bool IsDynamic,
        ImmutableArray<EvidenceRef> Evidence,
        CertaintyLevel Certainty,
         string? TargetContainingTypeName = null,
         string? TargetMethodName = null,
         bool IsInsideNestedFunction = false,
         bool IsSourceBacked = true,
         bool IsLoadedProjectTarget = false,
         int BlockOrdinal = 0,
         int EvaluationOrdinal = 0,
         string? TargetAssemblyName = null,
         bool IsPlatformTarget = false) : base(Id, Method, Evidence, Certainty)
    {
        var hasTypedPresentation = TargetContainingTypeName is not null
            || TargetMethodName is not null
            || TargetAssemblyName is not null;
        if (hasTypedPresentation
            && (TargetContainingTypeName is null
                || TargetMethodName is null
                || TargetAssemblyName is null))
        {
            throw new ArgumentException("Typed invocation target assembly, containing type, and method names must be supplied together.");
        }

        if (TargetContainingTypeName is not null
            && (string.IsNullOrWhiteSpace(TargetContainingTypeName)
                || TargetContainingTypeName.Any(char.IsWhiteSpace)
                || string.IsNullOrWhiteSpace(TargetMethodName)
                || TargetMethodName!.Any(char.IsWhiteSpace)
                || string.IsNullOrWhiteSpace(TargetAssemblyName)
                || TargetAssemblyName!.Any(char.IsWhiteSpace)))
        {
            throw new ArgumentException("Typed invocation target names must be non-empty and contain no whitespace.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(BlockOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(EvaluationOrdinal);

        this.Operation = Operation;
        this.Target = Target;
        this.IsDispatchable = IsDispatchable;
        this.IsDelegateOrEventInvoke = IsDelegateOrEventInvoke;
        this.IsStatic = IsStatic;
        this.IsConstructor = IsConstructor;
        this.IsDynamic = IsDynamic;
        this.TargetContainingTypeName = TargetContainingTypeName;
        this.TargetMethodName = TargetMethodName;
        this.IsInsideNestedFunction = IsInsideNestedFunction;
        this.IsSourceBacked = IsSourceBacked;
        this.IsLoadedProjectTarget = IsLoadedProjectTarget;
        this.BlockOrdinal = BlockOrdinal;
        this.EvaluationOrdinal = EvaluationOrdinal;
        this.TargetAssemblyName = TargetAssemblyName;
        this.IsPlatformTarget = IsPlatformTarget;
    }

    public OperationId Operation { get; init; }
    public MethodId? Target { get; init; }
    public bool IsDispatchable { get; init; }
    public bool IsDelegateOrEventInvoke { get; init; }
    public bool IsStatic { get; init; }
    public bool IsConstructor { get; init; }
    public bool IsDynamic { get; init; }
    public string? TargetContainingTypeName { get; init; }
    public string? TargetMethodName { get; init; }
    public bool IsInsideNestedFunction { get; init; }
    public bool IsSourceBacked { get; init; }
    public bool IsLoadedProjectTarget { get; init; }
    public int BlockOrdinal { get; init; }
    public int EvaluationOrdinal { get; init; }
    public string? TargetAssemblyName { get; init; }
    public bool IsPlatformTarget { get; init; }

    public override FlowNodeKind Kind => FlowNodeKind.Invocation;
}

public sealed record DecisionFlowNode(
    FlowNodeId Id,
    MethodId Method,
    OperationId Condition,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    public override FlowNodeKind Kind => FlowNodeKind.Decision;
}

public sealed record ReturnFlowNode(
    FlowNodeId Id,
    MethodId Method,
    OperationId? Value,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    public override FlowNodeKind Kind => FlowNodeKind.Return;
}

public sealed record ThrowFlowNode(
    FlowNodeId Id,
    MethodId Method,
    OperationId? Exception,
    bool IsRethrow,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    public override FlowNodeKind Kind => FlowNodeKind.Throw;
}

public sealed record AwaitFlowNode(
    FlowNodeId Id,
    MethodId Method,
    OperationId Operand,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    public override FlowNodeKind Kind => FlowNodeKind.Await;
}

public sealed record UnknownOperationFlowNode(
    FlowNodeId Id,
    MethodId Method,
    OperationId Operation,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    public override FlowNodeKind Kind => FlowNodeKind.UnknownOperation;
}

public sealed record LoopNode(
    FlowNodeId Id,
    MethodId Method,
    FlowRegionId Region,
    FlowNodeId? Header,
    ImmutableArray<FlowNodeId> Body,
    ImmutableArray<FlowNodeId> Exits,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty) : FlowNode(Id, Method, Evidence, Certainty)
{
    private ImmutableArray<int> _bodyBlockOrdinals;

    /// <summary>Canonical compiler block ordinals for loop members, excluding the header.</summary>
    public ImmutableArray<int> BodyBlockOrdinals
    {
        get => _bodyBlockOrdinals;
        init => _bodyBlockOrdinals = CanonicalizeBodyBlockOrdinals(value);
    }

    [JsonConstructor]
    public LoopNode(
        FlowNodeId id,
        MethodId method,
        FlowRegionId region,
        FlowNodeId? header,
        ImmutableArray<FlowNodeId> body,
        ImmutableArray<FlowNodeId> exits,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        ImmutableArray<int> bodyBlockOrdinals)
        : this(id, method, region, header, body, exits, evidence, certainty)
    {
        BodyBlockOrdinals = bodyBlockOrdinals;
    }

    private static ImmutableArray<int> CanonicalizeBodyBlockOrdinals(ImmutableArray<int> ordinals)
    {
        if (ordinals.Any(ordinal => ordinal < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(ordinals), "Loop body block ordinals must be nonnegative.");
        }

        return ordinals.Distinct().Order().ToImmutableArray();
    }

    public override FlowNodeKind Kind => FlowNodeKind.Loop;
}

/// <summary>Connects two method-flow nodes with a classified control or data transfer.</summary>
public sealed record FlowEdge(
    FlowEdgeId Id,
    MethodId Method,
    FlowNodeId Source,
    FlowNodeId Target,
    FlowEdgeKind Kind,
    OperationId? Guard,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Describes one normalized control or exception region.</summary>
public sealed record FlowRegion(
    FlowRegionId Id,
    MethodId Method,
    FlowRegionKind Kind,
    FlowRegionId? Parent,
    int Ordinal,
    ImmutableArray<FlowNodeId> Nodes,
    string? ExceptionType,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Describes one structural method exit with its supporting terminal evidence.</summary>
public sealed record FlowOutcome(
    FlowOutcomeKind Kind,
    int? BlockOrdinal,
    OperationId? TerminalOperation,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Describes one normalized method flow with nodes, edges, regions, loops, and outcomes.</summary>
public sealed record MethodFlowSnapshot(
    MethodId Method,
    string BodyFingerprint,
    ImmutableArray<FlowNode> Nodes,
    ImmutableArray<FlowEdge> Edges,
    ImmutableArray<FlowRegion> Regions,
    ImmutableArray<FlowOutcome> Outcomes,
    LocalValueGraph ValueGraph,
    ImmutableArray<ControlDependence> ControlDependences,
    MethodSummary? Summary,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string FlowFingerprint);
