using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Behavior;

/// <summary>Classifies one local value-graph node using a closed SeqDoc vocabulary.</summary>
public enum ValueNodeKind
{
    Unknown,
    Parameter,
    LocalDefinition,
    Constant,
    OperationResult,
    InvocationResult,
    MemberRead,
    MemberWrite,
    Merge,
    Capture,
}

/// <summary>Classifies one local value-graph edge.</summary>
public enum ValueEdgeKind
{
    Unknown,
    Assignment,
    Operand,
    Argument,
    Return,
    Conversion,
    MemberRead,
    MemberWrite,
    ConditionalSelection,
    CaptureCollapse,
}

public sealed record ValueNode(
    ValueNodeId Id,
    MethodId Method,
    ValueNodeKind Kind,
    string TypeDescriptor,
    string? Name,
    OperationId? DefiningOperation,
    int? ParameterOrdinal,
    string? ConstantValue,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

public sealed record ValueEdge(
    ValueEdgeId Id,
    MethodId Method,
    ValueNodeId Source,
    ValueNodeId Target,
    ValueEdgeKind Kind,
    OperationId? Guard,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Contains one method's definition-based local value graph.</summary>
public sealed record LocalValueGraph(
    ImmutableArray<ValueNode> Nodes,
    ImmutableArray<ValueEdge> Edges);

/// <summary>Records one direct control-dependence relationship.</summary>
public sealed record ControlDependence(
    FlowNodeId ControllingDecision,
    FlowNodeId ControlledNode,
    bool ControlledOnTrue,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Describes how one parameter influences the method body and its returns.</summary>
public sealed record ParameterFlow(
    int ParameterOrdinal,
    string ParameterName,
    bool FlowsToReturn,
    bool InfluencesStateWrite,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Summarizes one method's structural behavior without framework interpretation.</summary>
public sealed record MethodSummary(
    MethodId Method,
    string BodyFingerprint,
    ImmutableArray<ParameterFlow> ParameterFlows,
    ImmutableArray<SymbolId> StateReads,
    ImmutableArray<SymbolId> StateWrites,
    ImmutableArray<FlowOutcome> Outcomes,
    bool IsComplete,
    CertaintyLevel Certainty,
    ImmutableArray<EvidenceRef> Evidence);
