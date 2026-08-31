using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Core.ScenarioGraph;

/// <summary>Closed vocabulary of scenario-graph node kinds for the v0 evidence-backed graph.</summary>
public enum ScenarioNodeKind
{
    Unknown,
    EntryPoint,
    Action,
    MethodCall,
    ServiceCall,
    EntityQuery,
    StateAssignment,
    EntityMutation,
    SourceObservation,
    Result,
    Outcome,
    Delay,
    Dispatch,
    Handler,

    /// <summary>
    /// An admitted outbound invocation of a service-client operation (an exact
    /// <c>ClientBase&lt;TContract&gt;</c>-derived source/generated client call), distinct from
    /// <see cref="ServiceCall"/> (DI-resolved same-process dispatch): this node represents a
    /// compiler-proven call through a client boundary, never in-process dispatch.
    /// </summary>
    ClientOperationInvocation,
}

/// <summary>
/// Closed vocabulary of scenario-graph edge kinds. Result/outcome edges carry the success/data versus
/// failure/status polarity proven by structural-result or status-switch facts. State-assignment and
/// observation edges order non-interaction facts; mutation and save edges carry exact EF mutation and
/// persistence requests.
/// </summary>
public enum ScenarioEdgeKind
{
    Unknown,
    Entry,
    Call,
    Dispatch,
    Query,
    StateAssignment,
    Mutation,
    Save,
    Observation,
    ResultSuccess,
    ResultFailure,
    ResultStatus,
    OutcomeSuccess,
    OutcomeFailure,
}

/// <summary>Typed entry action presentation kinds used by wording; null remains valid for legacy callers.</summary>
public enum ScenarioActionKind
{
    Unknown,
    ControllerAction,
    MinimalApiHandler,
    ConfiguredMethod,
    HostedWorker,
    ServiceOperation,
}

public enum HostedWorkerControlKind
{
    AwaitedRepeatingLoop, EnumerationLoop, CatchLoopContinuation,
    CancellationCheck, SemaphoreBoundary, TerminalOutcome,
    ReturnBoundary, ThrowBoundary,
}

public enum ScenarioFlowContainerKind
{
    NaturalLoop, CatchRegion, FilterRegion, FinallyRegion,
    TryAndCatchRegion, TryAndFinallyRegion, TryRegion,
}

public sealed record ScenarioFlowContainer
{
    public ScenarioFlowContainer(FlowRegionId region, MethodId method, ScenarioFlowContainerKind kind,
        FlowNodeId? header, FlowRegionId? parent, ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region.Value, nameof(region));
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value, nameof(method));
        if (!Enum.IsDefined(kind) || kind == ScenarioFlowContainerKind.NaturalLoop && header is null)
        {
            throw new ArgumentException("Invalid flow container kind or header.", nameof(kind));
        }
        Evidence = ScenarioFlowContractValidation.NormalizeEvidence(evidence, nameof(evidence));
        if (certainty == CertaintyLevel.Unknown || certainty < Evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Invalid flow container certainty.", nameof(certainty));
        }
        Region = region; Method = method; Kind = kind; Header = header; Parent = parent; Certainty = certainty;
    }
    public FlowRegionId Region { get; init; }
    public MethodId Method { get; init; }
    public ScenarioFlowContainerKind Kind { get; init; }
    public FlowNodeId? Header { get; init; }
    public FlowRegionId? Parent { get; init; }
    public ImmutableArray<EvidenceRef> Evidence { get; init; }
    public CertaintyLevel Certainty { get; init; }
}

public sealed record ScenarioFlowPlacement
{
    public ScenarioFlowPlacement(ScenarioNodeId scenarioNode, MethodId method, FlowNodeId? anchor,
        ImmutableArray<FlowRegionId> containers, ImmutableArray<ScenarioArmId> guardArms,
        ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioNode.Value, nameof(scenarioNode));
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value, nameof(method));
        containers = containers.IsDefault ? [] : containers;
        guardArms = guardArms.IsDefault ? [] : guardArms;
        if (containers.Any(item => string.IsNullOrWhiteSpace(item.Value)) || containers.Length != containers.Distinct().Count())
        {
            throw new ArgumentException("Invalid placement containers.", nameof(containers));
        }
        if (guardArms.Any(item => string.IsNullOrWhiteSpace(item.Value)) || guardArms.Length != guardArms.Distinct().Count())
        {
            throw new ArgumentException("Invalid placement guard arms.", nameof(guardArms));
        }
        Evidence = ScenarioFlowContractValidation.NormalizeEvidence(evidence, nameof(evidence));
        if (certainty == CertaintyLevel.Unknown || certainty < Evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Invalid placement certainty.", nameof(certainty));
        }
        ScenarioNode = scenarioNode; Method = method; Anchor = anchor; Containers = containers; GuardArms = guardArms.OrderBy(item => item.Value, StringComparer.Ordinal).ToImmutableArray(); Certainty = certainty;
    }
    public ScenarioNodeId ScenarioNode { get; init; }
    public MethodId Method { get; init; }
    public FlowNodeId? Anchor { get; init; }
    public ImmutableArray<FlowRegionId> Containers { get; init; }
    public ImmutableArray<ScenarioArmId> GuardArms { get; init; }
    public ImmutableArray<EvidenceRef> Evidence { get; init; }
    public CertaintyLevel Certainty { get; init; }
}

internal static class ScenarioFlowContractValidation
{
    public static ImmutableArray<EvidenceRef> NormalizeEvidence(ImmutableArray<EvidenceRef> evidence, string name)
    {
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Evidence is required.", name);
        }
        return evidence.DistinctBy(item => item.Id).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
    }
}

/// <summary>Typed discriminator for the source of a scenario root.</summary>
public enum ScenarioRootKind
{
    HttpEntryPoint,
    ConfiguredMethod,
    HostedWorker,

    // Added last to preserve the existing numeric values of every prior member; no persistence layer
    // currently serializes ScenarioGraphSet/ScenarioRootKind (confirmed by a repository-wide search), so
    // this is a purely additive, backward-compatible extension.
    ServiceOperation,
}

/// <summary>
/// One evidence-backed scenario-graph node. The key is the canonical stable identity used to build
/// the node identity; method and operation anchors are optional per kind. Every node carries
/// non-empty evidence and explicit certainty that never exceeds its strongest evidence. The
/// presentation sequence ordinal is an additive source-order hint (default zero) used only to order
/// same-rank nodes deterministically; the renderer never infers order.
/// </summary>
public sealed record ScenarioNode
{
    public ScenarioNode(
        ScenarioNodeId id,
        ScenarioNodeKind kind,
        string key,
        MethodId? method,
        OperationId? operation,
        string detail,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        int sequenceOrdinal = 0,
        ScenarioNodePresentation? presentation = null)
    {
        if (!Enum.IsDefined(kind) || kind == ScenarioNodeKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined scenario node kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        ArgumentOutOfRangeException.ThrowIfNegative(sequenceOrdinal);
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario node requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario node requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException("Scenario node certainty must never exceed its strongest evidence.", nameof(certainty));
        }

        Id = id;
        Kind = kind;
        Key = key;
        Method = method;
        Operation = operation;
        Detail = detail;
        Evidence = evidence;
        Certainty = certainty;
        SequenceOrdinal = sequenceOrdinal;
        Presentation = presentation;
    }

    public ScenarioNodeId Id { get; }

    public ScenarioNodeKind Kind { get; }

    public string Key { get; }

    public MethodId? Method { get; }

    public OperationId? Operation { get; }

    public string Detail { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }

    public int SequenceOrdinal { get; }

    /// <summary>Typed presentation inputs used by the wording/DiagramPlan layer for concise display names; null when a node kind proves no presentation fact.</summary>
    public ScenarioNodePresentation? Presentation { get; }
}

/// <summary>
/// Typed canonical presentation inputs for one scenario node. The wording/DiagramPlan layer decides
/// concise display names and interaction labels from these typed facts; no layer parses display or
/// debug strings, and renderers never infer semantics. Canonical identities, keys, detail strings,
/// and fully qualified type names remain the source of truth for joins, evidence, and determinism.
/// Fields are kind-specific: exactly the authoritative facts a node's kind proves, so a node carries
/// only the fields that correspond to its scenario kind.
/// </summary>
public sealed record ScenarioNodePresentation(
    string? ControllerTypeName = null,
    string? ActionMethodName = null,
    string? ContractTypeName = null,
    string? ImplementationTypeName = null,
    string? CalledMemberName = null,
    string? ArgumentLabel = null,
    string? DbContextTypeName = null,
    string? EntityTypeName = null,
    EntityFrameworkQueryOperatorKind? QueryOperatorKind = null,
    EntityFrameworkMutationKind? MutationKind = null,
    StructuralResultFactoryKind? ResultFactoryKind = null,
    HttpOutcomeHelperKind? OutcomeHelperKind = null,
    int? OutcomeStatusCode = null,
    string? OutcomeCreatedRoute = null,
    ScenarioActionKind? ActionKind = null,
    HttpBindingKind? HandlerBindingKind = null,
    string? HandlerParameterName = null,
    string? HandlerParameterTypeName = null,
    int? SourceOrdinal = null,
    string? RequestTypeName = null,
    string? ResponseTypeName = null,
    string? HandlerTypeName = null,
    bool? HandlerBodyAvailable = null,
    string? TargetContainingTypeName = null,
    string? TargetMemberName = null,
    string? ConfiguredContainingTypeName = null,
    string? ConfiguredMethodName = null,
    string? ConfiguredDisplaySignature = null,
    string? HostedWorkerTypeName = null,
    string? HostedWorkerStartMethodName = null,
    string? HostedWorkerExecuteMethodName = null,
    string? HostedWorkerStopMethodName = null,
    HostedWorkerLifecycleStep? HostedWorkerLifecycleStep = null,
    string? HostedWorkerCancellationParameterName = null,
    bool HostedWorkerSchedulerRegistration = false,
    HostedWorkerControlKind? HostedWorkerControlKind = null,
    FlowRegionId? HostedWorkerFlowRegion = null,
    FlowNodeId? HostedWorkerHeader = null,
    int? HostedWorkerBlockOrdinal = null,
    string? ClientTypeName = null,
    ServiceClientKind? ClientKind = null,
    ClientInvocationResultClaimKind? ResultClaimKind = null,
    bool ResultIsAwaited = false,
    string? ResultBindingName = null,
    string? DeclaredResultTypeName = null,
    string? DeclaredFaultTypeNames = null);

/// <summary>
/// One evidence-backed scenario-graph edge connecting two nodes. Every edge carries non-empty
/// evidence and explicit certainty that never exceeds its strongest evidence. The presentation
/// sequence ordinal is an additive source-order hint (default zero) used only to order same-rank
/// edges deterministically; the renderer never infers order.
/// </summary>
public sealed record ScenarioEdge
{
    public ScenarioEdge(
        ScenarioEdgeId id,
        ScenarioNodeId source,
        ScenarioNodeId target,
        ScenarioEdgeKind kind,
        string detail,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        int sequenceOrdinal = 0)
    {
        if (!Enum.IsDefined(kind) || kind == ScenarioEdgeKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined scenario edge kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Value, nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Value, nameof(target));
        ArgumentOutOfRangeException.ThrowIfNegative(sequenceOrdinal);
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario edge requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario edge requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException("Scenario edge certainty must never exceed its strongest evidence.", nameof(certainty));
        }

        Id = id;
        Source = source;
        Target = target;
        Kind = kind;
        Detail = detail;
        Evidence = evidence;
        Certainty = certainty;
        SequenceOrdinal = sequenceOrdinal;
    }

    public ScenarioEdgeId Id { get; }

    public ScenarioNodeId Source { get; }

    public ScenarioNodeId Target { get; }

    public ScenarioEdgeKind Kind { get; }

    public string Detail { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }

    public int SequenceOrdinal { get; }
}

/// <summary>
/// One explicit ambiguity or incomplete-join finding for one scenario graph. Scenario building never
/// selects one candidate silently; every refused or incomplete join is recorded here with a stable
/// identity.
/// </summary>
public sealed record ScenarioGraphDiagnostic(
    DiagnosticId Id,
    string Code,
    string Summary,
    string Detail)
{
    public ImmutableArray<EvidenceRef> Evidence { get; init; } = [];
    public CertaintyLevel Certainty { get; init; } = CertaintyLevel.Conservative;
}

/// <summary>
/// Closed vocabulary of terminal/rejoin classification for one scenario decision arm. An arm whose
/// exact continuation cannot be proven fails closed as <see cref="Unknown"/> with an explicit SC013
/// diagnostic; no renderer may display an unknown arm as terminating or rejoining.
/// </summary>
public enum ScenarioTerminalKind
{
    Unknown,
    Terminates,
    Rejoins,
}

public enum ScenarioPredicateWordingRole
{
    Owner,
    Subordinate,
}

public sealed record ScenarioPredicateWording
{
    public ScenarioPredicateWording(
        SemanticFactId predicateId,
        PredicateExpression root,
        ScenarioPredicateWordingRole role,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateId.Value, nameof(predicateId));
        ArgumentNullException.ThrowIfNull(root);
        if (!Enum.IsDefined(role)) { throw new ArgumentOutOfRangeException(nameof(role)); }
        if (evidence.IsDefaultOrEmpty) { throw new ArgumentException("Predicate wording requires evidence.", nameof(evidence)); }
        if (certainty == CertaintyLevel.Unknown || certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Predicate wording certainty must not exceed its evidence.", nameof(certainty));
        }
        PredicateId = predicateId; Root = root; Role = role; Evidence = evidence; Certainty = certainty;
    }
    public SemanticFactId PredicateId { get; }
    public PredicateExpression Root { get; }
    public ScenarioPredicateWordingRole Role { get; }
    public ImmutableArray<EvidenceRef> Evidence { get; }
    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One evidence-backed scenario decision derived from a controlling <c>DecisionFlowNode</c>. The
/// identity follows architecture decision: compilation profile, root/containing method, and controlling flow-node
/// identity for root/service topology, with the optional exact direct-call occurrence scope as an additional
/// identity input for callee-local decisions — never entry-point identity, labels, source text, traversal order,
/// or display order. A null scope preserves legacy root/service identity. The record enforces the same evidence/certainty invariants as <see cref="ScenarioNode"/>:
/// non-empty evidence, explicit certainty, and certainty never stronger than the strongest evidence.
/// </summary>
public sealed record ScenarioDecision
{
    public ScenarioDecision(
        ScenarioDecisionId id,
        MethodId method,
        FlowNodeId controllingFlowNode,
        OperationId condition,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        ScenarioPredicateWording? predicateWording = null,
        string? occurrenceScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value, nameof(method));
        ArgumentException.ThrowIfNullOrWhiteSpace(controllingFlowNode.Value, nameof(controllingFlowNode));
        ArgumentException.ThrowIfNullOrWhiteSpace(condition.Value, nameof(condition));
        if (occurrenceScope is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(occurrenceScope, nameof(occurrenceScope));
        }
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario decision requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario decision requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException("Scenario decision certainty must never exceed its strongest evidence.", nameof(certainty));
        }

        Id = id;
        Method = method;
        ControllingFlowNode = controllingFlowNode;
        Condition = condition;
        Evidence = evidence;
        Certainty = certainty;
        PredicateWording = predicateWording;
        OccurrenceScope = occurrenceScope;
    }

    public ScenarioDecisionId Id { get; }

    public MethodId Method { get; }

    public FlowNodeId ControllingFlowNode { get; }

    public OperationId Condition { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }

    public ScenarioPredicateWording? PredicateWording { get; }

    /// <summary>Exact direct-call expansion-step identity for a callee-local decision; null for root/service topology.</summary>
    public string? OccurrenceScope { get; }
}

/// <summary>
/// One semantic true/false arm of a scenario decision. Both arms are always represented so nested
/// membership under different decisions and same-decision polarity conflicts stay explicit. The
/// record enforces non-empty evidence, explicit certainty, and certainty never stronger than the
/// strongest evidence.
/// </summary>
public sealed record ScenarioArm
{
    public ScenarioArm(
        ScenarioArmId id,
        ScenarioDecisionId decision,
        bool IsTrue,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(decision.Value, nameof(decision));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario arm requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario arm requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException("Scenario arm certainty must never exceed its strongest evidence.", nameof(certainty));
        }

        Id = id;
        Decision = decision;
        this.IsTrue = IsTrue;
        Evidence = evidence;
        Certainty = certainty;
    }

    public ScenarioArmId Id { get; }

    public ScenarioDecisionId Decision { get; }

    public bool IsTrue { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One membership of a controlled scenario node in one decision arm. The identity follows architecture decision:
/// compilation profile, root/containing method, parent arm identity, and controlled scenario node
/// identity only — never entry-point identity. The record enforces non-empty evidence, explicit
/// certainty, and certainty never stronger than the strongest evidence.
/// </summary>
public sealed record ScenarioMembership
{
    public ScenarioMembership(
        ScenarioMembershipId id,
        ScenarioArmId arm,
        ScenarioNodeId scenarioNode,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(arm.Value, nameof(arm));
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioNode.Value, nameof(scenarioNode));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario membership requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario membership requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException("Scenario membership certainty must never exceed its strongest evidence.", nameof(certainty));
        }

        Id = id;
        Arm = arm;
        ScenarioNode = scenarioNode;
        Evidence = evidence;
        Certainty = certainty;
    }

    public ScenarioMembershipId Id { get; }

    public ScenarioArmId Arm { get; }

    public ScenarioNodeId ScenarioNode { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One terminal/rejoin classification for a scenario decision arm. Unsupported or incomplete
/// topology (loop-back, switch shape, exception region, mixed or missing boundary) is recorded as
/// <see cref="ScenarioTerminalKind.Unknown"/> and never claimed exact. The record enforces non-empty
/// evidence, explicit certainty, and certainty never stronger than the strongest evidence.
/// </summary>
public sealed record ScenarioArmTerminal
{
    public ScenarioArmTerminal(
        ScenarioArmId arm,
        ScenarioTerminalKind kind,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arm.Value, nameof(arm));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario arm terminal requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario arm terminal requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException("Scenario arm terminal certainty must never exceed its strongest evidence.", nameof(certainty));
        }

        Arm = arm;
        Kind = kind;
        Evidence = evidence;
        Certainty = certainty;
    }

    public ScenarioArmId Arm { get; }

    public ScenarioTerminalKind Kind { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One deterministic configuration decision of a service composition: the exact condition operation,
/// the accepted contract read operation, and the canonical non-sensitive configuration key shared by the
/// mutually exclusive service arms. The decision retains the canonical union of the conditional
/// group evidence and the matching accepted contract read/condition/checked-in observation evidence so certainty
/// degrades to the weakest contributor; a checked-in value never selects an arm and never promotes
/// the decision. Certainty is validated against the weakest evidence contributor
/// (<see cref="CertaintyLevel"/> higher values are weaker): Exact certainty over mixed
/// Exact+Conservative evidence is rejected.
/// </summary>
public sealed record ScenarioConfigurationDecision
{
    public ScenarioConfigurationDecision(
        OperationId conditionOperation,
        OperationId readOperation,
        string key,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conditionOperation.Value, nameof(conditionOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(readOperation.Value, nameof(readOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario configuration decision requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario configuration decision requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Scenario configuration decision certainty must never exceed its weakest evidence contributor.", nameof(certainty));
        }

        ConditionOperation = conditionOperation;
        ReadOperation = readOperation;
        Key = key;
        Evidence = evidence;
        Certainty = certainty;
    }

    public OperationId ConditionOperation { get; }

    public OperationId ReadOperation { get; }

    public string Key { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One independently resolved service alternative of a scenario composition. The arm retains its
/// exact registration identity, implementation type, and the exactly one resolved implementation
/// method produced by the compiler call resolution for that implementation type. Evidence is non-empty
/// and certainty is explicit and never stronger than the weakest evidence contributor
/// (<see cref="CertaintyLevel"/> higher values are weaker): Exact certainty over mixed
/// Exact+Conservative evidence is rejected. An arm may also carry the canonical scenario node
/// identities of the work joined under it (for example cache-miss factory work under the selected
/// configuration arm); membership is normalized to ordinal node order and empty when absent.
/// </summary>
public sealed record ScenarioServiceAlternativeArm
{
    public ScenarioServiceAlternativeArm(
        bool IsTrue,
        SemanticFactId RegistrationId,
        string ImplementationType,
        MethodId ResolvedMethod,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        ImmutableArray<ScenarioNodeId> memberNodes = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RegistrationId.Value, nameof(RegistrationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(ImplementationType, nameof(ImplementationType));
        ArgumentException.ThrowIfNullOrWhiteSpace(ResolvedMethod.Value, nameof(ResolvedMethod));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario service arm requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario service arm requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Scenario service arm certainty must never exceed its weakest evidence contributor.", nameof(certainty));
        }

        if (!memberNodes.IsDefaultOrEmpty)
        {
            if (memberNodes.Any(node => string.IsNullOrWhiteSpace(node.Value)))
            {
                throw new ArgumentException(
                    "Scenario service arm member nodes must be canonical non-blank node identities.",
                    nameof(memberNodes));
            }

            if (memberNodes.Select(node => node.Value).Distinct(StringComparer.Ordinal).Count() != memberNodes.Length)
            {
                throw new ArgumentException(
                    "Scenario service arm member nodes must be distinct.",
                    nameof(memberNodes));
            }
        }

        this.IsTrue = IsTrue;
        this.RegistrationId = RegistrationId;
        this.ImplementationType = ImplementationType;
        this.ResolvedMethod = ResolvedMethod;
        Evidence = evidence;
        Certainty = certainty;
        MemberNodes = memberNodes.IsDefaultOrEmpty
            ? []
            : memberNodes
                .OrderBy(node => node.Value, StringComparer.Ordinal)
                .ToImmutableArray();
    }

    public bool IsTrue { get; }

    public SemanticFactId RegistrationId { get; }

    public string ImplementationType { get; }

    public MethodId ResolvedMethod { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }

    /// <summary>
    /// Canonical scenario node identities of the work joined under this arm, ordered by node
    /// identity; empty when the arm carries no membership.
    /// </summary>
    public ImmutableArray<ScenarioNodeId> MemberNodes { get; }
}

/// <summary>
/// Optional analysis-profile selection metadata for one scenario composition. A matching accepted contract
/// profile-known boolean marks one arm selected and the other excluded only within that analysis
/// profile; both arms and their provenance always remain retained, certainty is never promoted, and a
/// checked-in JSON value never selects an arm. The selection evidence is exactly the matching
/// profile-known fact evidence and certainty degrades to its weakest contributor
/// (<see cref="CertaintyLevel"/> higher values are weaker).
/// </summary>
public sealed record ScenarioCompositionProfileSelection
{
    public ScenarioCompositionProfileSelection(
        bool selectsTrueArm,
        string analysisProfileSource,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisProfileSource, nameof(analysisProfileSource));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A scenario composition profile selection requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A scenario composition profile selection requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Scenario composition profile selection certainty must never exceed its weakest evidence contributor.", nameof(certainty));
        }

        SelectsTrueArm = selectsTrueArm;
        AnalysisProfileSource = analysisProfileSource;
        Evidence = evidence;
        Certainty = certainty;
    }

    public bool SelectsTrueArm { get; }

    public string AnalysisProfileSource { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One typed service composition of a scenario graph. The composition is Roslyn-neutral and separate
/// from Method Flow <see cref="ScenarioTopology"/>: one configuration decision plus two
/// service-alternative arms retaining registration, implementation, the exact resolved service method,
/// evidence, certainty, and optional analysis-profile selection metadata. A composition exists only
/// when one exact alternative group accounts for the complete DI binding/registration set and call
/// resolution is complete; every other multiple-binding shape retains SC001 and a null composition.
/// Positional arm polarity is enforced: the true arm must carry true polarity and the false arm false
/// polarity, so downstream rendering or profile selection can never label the wrong implementation.
/// </summary>
public sealed record ScenarioServiceComposition
{
    public ScenarioServiceComposition(
        ScenarioCompositionId id,
        string serviceType,
        ScenarioConfigurationDecision decision,
        ScenarioServiceAlternativeArm trueArm,
        ScenarioServiceAlternativeArm falseArm,
        ScenarioCompositionProfileSelection? profileSelection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType, nameof(serviceType));
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(trueArm);
        ArgumentNullException.ThrowIfNull(falseArm);
        if (trueArm.IsTrue != true || falseArm.IsTrue != false)
        {
            throw new ArgumentException("A service composition requires the true arm to carry true polarity and the false arm false polarity.", nameof(trueArm));
        }

        Id = id;
        ServiceType = serviceType;
        Decision = decision;
        TrueArm = trueArm;
        FalseArm = falseArm;
        ProfileSelection = profileSelection;
    }

    public ScenarioCompositionId Id { get; }

    public string ServiceType { get; }

    public ScenarioConfigurationDecision Decision { get; }

    public ScenarioServiceAlternativeArm TrueArm { get; }

    public ScenarioServiceAlternativeArm FalseArm { get; }

    public ScenarioCompositionProfileSelection? ProfileSelection { get; }
}

/// <summary>
/// Roslyn-neutral decision topology for one scenario graph: decisions, semantic arms, arm
/// memberships, and arm terminal/rejoin classifications. All arrays are in canonical, path-free
/// semantic order: decisions by controlling flow-node identity, arms by controlling flow-node
/// identity then semantic polarity, memberships by controlling flow-node identity then polarity then
/// controlled node identity, and terminals in the same semantic arm order. Diagram Plan may reorder
/// terminal arms for display without changing these semantic identities.
/// </summary>
public sealed record ScenarioTopology
{
    public ScenarioTopology(
        ImmutableArray<ScenarioDecision> decisions,
        ImmutableArray<ScenarioArm> arms,
        ImmutableArray<ScenarioMembership> memberships,
        ImmutableArray<ScenarioArmTerminal> terminals,
        ImmutableArray<ScenarioFlowContainer> flowContainers = default,
        ImmutableArray<ScenarioFlowPlacement> flowPlacements = default)
    {
        Decisions = decisions.IsDefault ? [] : decisions;
        Arms = arms.IsDefault ? [] : arms;
        Memberships = memberships.IsDefault ? [] : memberships;
        Terminals = terminals.IsDefault ? [] : terminals;
        FlowContainers = flowContainers.IsDefault ? [] : flowContainers;
        FlowPlacements = flowPlacements.IsDefault ? [] : flowPlacements;
    }
    public ImmutableArray<ScenarioDecision> Decisions { get; init; }
    public ImmutableArray<ScenarioArm> Arms { get; init; }
    public ImmutableArray<ScenarioMembership> Memberships { get; init; }
    public ImmutableArray<ScenarioArmTerminal> Terminals { get; init; }
    public ImmutableArray<ScenarioFlowContainer> FlowContainers { get; init; }
    public ImmutableArray<ScenarioFlowPlacement> FlowPlacements { get; init; }

    public void Deconstruct(out ImmutableArray<ScenarioDecision> decisions,
        out ImmutableArray<ScenarioArm> arms, out ImmutableArray<ScenarioMembership> memberships,
        out ImmutableArray<ScenarioArmTerminal> terminals,
        out ImmutableArray<ScenarioFlowContainer> flowContainers,
        out ImmutableArray<ScenarioFlowPlacement> flowPlacements)
        => (decisions, arms, memberships, terminals, flowContainers, flowPlacements) =
            (Decisions, Arms, Memberships, Terminals, FlowContainers, FlowPlacements);

    public void Deconstruct(out ImmutableArray<ScenarioDecision> decisions,
        out ImmutableArray<ScenarioArm> arms, out ImmutableArray<ScenarioMembership> memberships,
        out ImmutableArray<ScenarioArmTerminal> terminals)
        => (decisions, arms, memberships, terminals) = (Decisions, Arms, Memberships, Terminals);
    /// <summary>The canonical empty topology used by source-compatible legacy construction.</summary>
    public static ScenarioTopology Empty { get; } = new([], [], [], [], [], []);
}

public sealed record ScenarioHandlerParameter(string Name, string TypeName, HttpBindingKind BindingKind,
    ImmutableArray<EvidenceRef> Evidence, CertaintyLevel Certainty);

public sealed record ScenarioHandlerDecision(int Ordinal, int? ParentDecisionOrdinal, bool? ParentIsTrue,
    string PredicateText, ImmutableArray<EvidenceRef> Evidence, CertaintyLevel Certainty);

public sealed record ScenarioHandlerOutcome(int SourceOrdinal, int DecisionOrdinal, bool IsTrue, int StatusCode,
    string FactoryIdentity, ImmutableArray<EvidenceRef> Evidence, CertaintyLevel Certainty);

public sealed record ScenarioHandlerDelay(int SourceOrdinal, int DecisionOrdinal, bool IsTrue, int Milliseconds,
    ImmutableArray<EvidenceRef> Evidence, CertaintyLevel Certainty);

public sealed record ScenarioHandlerTopology(
    ImmutableArray<ScenarioHandlerParameter> Parameters,
    ImmutableArray<ScenarioHandlerDecision> Decisions,
    ImmutableArray<ScenarioHandlerOutcome> Outcomes,
    ImmutableArray<ScenarioHandlerDelay> Delays)
{
    public static ScenarioHandlerTopology Empty { get; } = new([], [], [], []);
}

/// <summary>One bounded, compiler-evidenced direct invocation in a selected dispatch handler.</summary>
public sealed record ScenarioDispatchHandlerStep(
    string Id,
    int SourceOrdinal,
    int ParentDepth,
    MethodId CallerMethod,
    MethodId TargetMethod,
    OperationId Operation,
    string Label,
    string? LoopMembershipKey,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty,
    string? ParentStepId = null,
    string? TargetParticipantIdentity = null);

/// <summary>One complete natural loop joined from Method Flow facts.</summary>
public sealed record ScenarioDispatchHandlerLoop(
    string Key,
    FlowRegionId Region,
    FlowNodeId Header,
    ImmutableArray<FlowNodeId> Body,
    ImmutableArray<FlowNodeId> Exits,
    FlowEdgeId BackEdge,
    string Label,
    ImmutableArray<ScenarioDispatchHandlerStep> MemberSteps,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>An exact explicit return from the selected handler.</summary>
public sealed record ScenarioDispatchHandlerReturn(
    OperationId Operation,
    string TypeName,
    MethodId Method,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

public sealed record ScenarioDispatchParticipant(string Key, string Label, string? Identity = null);

/// <summary>Immutable result of the deliberately bounded dispatch-handler expansion.</summary>
public sealed record ScenarioDispatchHandlerExpansion(
    DispatchCandidate Handler,
    string HandlerPresentation,
    ImmutableArray<ScenarioDispatchHandlerStep> SourceSteps,
    ImmutableArray<ScenarioDispatchHandlerStep> DirectCalls,
    ImmutableArray<ScenarioDispatchHandlerLoop> Loops,
    ScenarioDispatchHandlerReturn? Return,
    bool IsComplete,
    ImmutableArray<ScenarioGraphDiagnostic> Diagnostics,
    ImmutableArray<ScenarioDispatchParticipant> Participants,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty,
    string DebugProjection);

public sealed record ScenarioDirectCallExpansionStep
{
    public ScenarioDirectCallExpansionStep(
        string id, string? parentStepId, int depth, MethodId callerMethod, MethodId targetMethod,
        OperationId operation, ScenarioNodeId scenarioNodeId, int sourceOrdinal,
        ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty, bool isComplete,
        bool isCycleBoundary = false, ImmutableArray<ScenarioArmId> rootArmIds = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(callerMethod.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMethod.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioNodeId.Value);
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOrdinal);
        if (parentStepId is null ? depth != 1 : depth <= 1)
        {
            throw new ArgumentException("A direct-call parent must precede its child by exactly one depth.", nameof(parentStepId));
        }
        if (evidence.IsDefaultOrEmpty) { throw new ArgumentException("A direct-call step requires evidence.", nameof(evidence)); }
        if (certainty == CertaintyLevel.Unknown || certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException("A direct-call step certainty must not exceed its evidence.", nameof(certainty));
        }
        if (!rootArmIds.IsDefaultOrEmpty)
        {
            if (rootArmIds.Any(item => string.IsNullOrWhiteSpace(item.Value))
                || rootArmIds.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() != rootArmIds.Length)
            {
                throw new ArgumentException("Root arm identities must be distinct and canonical.", nameof(rootArmIds));
            }
            rootArmIds = rootArmIds.OrderBy(item => item.Value, StringComparer.Ordinal).ToImmutableArray();
        }
        Id = id; ParentStepId = parentStepId; Depth = depth; CallerMethod = callerMethod; TargetMethod = targetMethod;
        Operation = operation; ScenarioNodeId = scenarioNodeId; SourceOrdinal = sourceOrdinal; Evidence = evidence;
        Certainty = certainty; IsComplete = isComplete; IsCycleBoundary = isCycleBoundary; RootArmIds = rootArmIds.IsDefault ? [] : rootArmIds;
    }
    public string Id { get; }
    public string? ParentStepId { get; }
    public int Depth { get; }
    public MethodId CallerMethod { get; }
    public MethodId TargetMethod { get; }
    public OperationId Operation { get; }
    public ScenarioNodeId ScenarioNodeId { get; }
    public int SourceOrdinal { get; }
    public ImmutableArray<EvidenceRef> Evidence { get; }
    public CertaintyLevel Certainty { get; }
    public bool IsComplete { get; init; }
    public bool IsCycleBoundary { get; }
    public ImmutableArray<ScenarioArmId> RootArmIds { get; init; }
}

public sealed record ScenarioDirectCallExpansion
{
    public ScenarioDirectCallExpansion(ImmutableArray<ScenarioDirectCallExpansionStep> steps, bool isComplete, ImmutableArray<ScenarioGraphDiagnostic> diagnostics)
    {
        if (steps.IsDefault) { throw new ArgumentException("Expansion steps must be initialized.", nameof(steps)); }
        if (diagnostics.IsDefault) { throw new ArgumentException("Expansion diagnostics must be initialized.", nameof(diagnostics)); }
        if (steps.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != steps.Length
            || steps.Select(item => item.ScenarioNodeId.Value).Distinct(StringComparer.Ordinal).Count() != steps.Length)
        {
            throw new ArgumentException("Expansion step and node identities must be distinct.", nameof(steps));
        }
        var byId = steps.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var indexById = steps.Select((step, index) => (step.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (step.IsCycleBoundary && step.IsComplete)
            { throw new ArgumentException("A cycle-boundary step must be incomplete.", nameof(steps)); }
            if (step.ParentStepId is { } parent)
            {
                if (!byId.TryGetValue(parent, out var parentStep) || parentStep.Depth != step.Depth - 1)
                { throw new ArgumentException("Expansion parents must precede children by one depth.", nameof(steps)); }
                if (indexById[parent] >= indexById[step.Id])
                { throw new ArgumentException("Expansion parents must precede children by array index.", nameof(steps)); }
            }
        }
        if (isComplete && (steps.Any(item => !item.IsComplete) || diagnostics.Length != 0))
        { throw new ArgumentException("A complete expansion cannot contain incomplete steps or diagnostics.", nameof(isComplete)); }
        Steps = steps; IsComplete = isComplete; Diagnostics = diagnostics;
    }
    public ImmutableArray<ScenarioDirectCallExpansionStep> Steps { get; init; }
    public bool IsComplete { get; }
    public ImmutableArray<ScenarioGraphDiagnostic> Diagnostics { get; }
    public static ScenarioDirectCallExpansion Empty { get; } = new([], true, []);
}

/// <summary>
/// One typed callback region of a scenario graph. The region translates one exact
/// <see cref="CallbackBoundaryFact"/> into renderer-neutral membership: the generated member nodes
/// inherit the callback trigger/cardinality and are never presented as unconditional top-level
/// behavior. Construction enforces the same impossible-state invariants as the callback boundary
/// fact: defined enums, cardinality/trigger/condition coupling, initialized distinct canonical
/// member node IDs, non-empty artifact evidence, and explicit certainty that never exceeds the
/// strongest evidence contributor (<see cref="CertaintyLevel"/> higher values are weaker). When an
/// optional <see cref="FrameworkCallbackConditionKind"/> is supplied it must be the defined
/// <see cref="FrameworkCallbackConditionKind.CacheMiss"/> and requires cardinality ZeroOrOne, trigger
/// Conditional, and a null operation trigger condition; otherwise the callback boundary
/// cardinality/trigger/condition coupling rules apply unchanged.
/// </summary>
public sealed record ScenarioCallbackRegion
{
    public ScenarioCallbackRegion(
        ScenarioCallbackRegionId id,
        CallbackBoundaryId boundaryId,
        CallbackCardinality cardinality,
        CallbackTriggerKind trigger,
        OperationId? triggerCondition,
        CallbackCompletionKind completion,
        ImmutableArray<ScenarioNodeId> memberNodes,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        FrameworkCallbackConditionKind? frameworkCondition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(boundaryId.Value, nameof(boundaryId));
        if (!Enum.IsDefined(completion))
        {
            throw new ArgumentOutOfRangeException(nameof(completion), "Undefined callback completion kind.");
        }

        if (frameworkCondition is null)
        {
            CallbackBoundaryFactContracts.ValidateCardinalityTrigger(cardinality, trigger, triggerCondition);
        }
        else
        {
            if (!Enum.IsDefined(frameworkCondition.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(frameworkCondition), "Undefined framework callback condition kind.");
            }

            if (frameworkCondition.Value != FrameworkCallbackConditionKind.CacheMiss)
            {
                throw new ArgumentException(
                    "A scenario callback region supports only the CacheMiss framework condition.",
                    nameof(frameworkCondition));
            }

            if (cardinality != CallbackCardinality.ZeroOrOne)
            {
                throw new ArgumentException(
                    "A framework-conditional scenario callback region requires cardinality ZeroOrOne.",
                    nameof(cardinality));
            }

            if (trigger != CallbackTriggerKind.Conditional)
            {
                throw new ArgumentException(
                    "A framework-conditional scenario callback region requires trigger Conditional.",
                    nameof(trigger));
            }

            if (triggerCondition is not null)
            {
                throw new ArgumentException(
                    "A framework-conditional scenario callback region requires no operation trigger condition.",
                    nameof(triggerCondition));
            }
        }

        CallbackBoundaryFactContracts.ValidateEvidence(evidence, certainty);
        if (memberNodes.IsDefault || memberNodes.IsEmpty)
        {
            throw new ArgumentException(
                "A scenario callback region requires non-empty member nodes.",
                nameof(memberNodes));
        }

        if (memberNodes.Any(node => string.IsNullOrWhiteSpace(node.Value)))
        {
            throw new ArgumentException(
                "Scenario callback region member nodes must be canonical non-blank node identities.",
                nameof(memberNodes));
        }

        if (memberNodes.Select(node => node.Value).Distinct(StringComparer.Ordinal).Count() != memberNodes.Length)
        {
            throw new ArgumentException(
                "Scenario callback region member nodes must be distinct.",
                nameof(memberNodes));
        }

        Id = id;
        BoundaryId = boundaryId;
        Cardinality = cardinality;
        Trigger = trigger;
        TriggerCondition = triggerCondition;
        Completion = completion;
        MemberNodes = memberNodes
            .OrderBy(node => node.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        Evidence = evidence;
        Certainty = certainty;
        FrameworkCondition = frameworkCondition;
    }

    public ScenarioCallbackRegionId Id { get; }

    public CallbackBoundaryId BoundaryId { get; }

    public CallbackCardinality Cardinality { get; }

    public CallbackTriggerKind Trigger { get; }

    public OperationId? TriggerCondition { get; }

    public CallbackCompletionKind Completion { get; }

    /// <summary>Canonical member node identities ordered by node identity.</summary>
    public ImmutableArray<ScenarioNodeId> MemberNodes { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }

    /// <summary>
    /// The framework-recognized condition that makes this region conditional, null when the region is
    /// not framework-conditional. A non-null value is always the defined
    /// <see cref="FrameworkCallbackConditionKind.CacheMiss"/> and implies cardinality ZeroOrOne,
    /// trigger Conditional, and a null operation trigger condition.
    /// </summary>
    public FrameworkCallbackConditionKind? FrameworkCondition { get; }
}

/// <summary>
/// One deterministic, evidence-backed v0 scenario graph for one HTTP entry point. The graph identity
/// is the entry-point identity; nodes and edges carry evidence and certainty, and ambiguous or
/// incomplete joins are explicit diagnostics rather than invented selections. The debug projection
/// is canonical, newline-only, and free of absolute paths. Legacy construction without an explicit
/// topology yields a non-null empty <see cref="ScenarioTopology"/> so downstream consumers never
/// observe a null default. The optional <see cref="Composition"/> is a typed service composition
/// populated only when one exact conditional DI group accounts for the complete binding/registration
/// set with complete call resolution; all other shapes keep a null composition and the SC001
/// diagnostic. <see cref="CallbackRegions"/> carries typed callback regions derived from a bound
/// callback boundary fact set; a legacy request without callback facts keeps it initialized but
/// empty, never default.
/// </summary>
public sealed record ScenarioGraph
{
    public ScenarioGraph(
        EntryPointId entryPoint,
        CompilationProfileId profile,
        MethodId rootMethod,
        HttpMethodKind httpMethod,
        string canonicalRoute,
        string operationKey,
        ImmutableArray<ScenarioNode> nodes,
        ImmutableArray<ScenarioEdge> edges,
        ImmutableArray<ScenarioGraphDiagnostic> diagnostics,
        string debugProjection,
        ScenarioTopology topology,
        ScenarioServiceComposition? composition = null,
        ImmutableArray<ScenarioCallbackRegion> callbackRegions = default,
        ScenarioHandlerTopology? handlerTopology = null,
        ScenarioDispatchHandlerExpansion? dispatchHandlerExpansion = null,
        ScenarioRootKind rootKind = ScenarioRootKind.HttpEntryPoint,
        ScenarioDirectCallExpansion? directCallExpansion = null)
    {
        if (!Enum.IsDefined(rootKind))
        {
            throw new ArgumentOutOfRangeException(nameof(rootKind), "Undefined scenario root kind.");
        }

        EntryPoint = entryPoint;
        Profile = profile;
        RootMethod = rootMethod;
        HttpMethod = httpMethod;
        CanonicalRoute = canonicalRoute;
        OperationKey = operationKey;
        Nodes = nodes;
        Edges = edges;
        Diagnostics = diagnostics;
        DebugProjection = debugProjection;
        Topology = topology;
        Composition = composition;
        CallbackRegions = callbackRegions.IsDefault ? [] : callbackRegions;
        HandlerTopology = handlerTopology;
        DispatchHandlerExpansion = dispatchHandlerExpansion;
        RootKind = rootKind;
        DirectCallExpansion = directCallExpansion ?? ScenarioDirectCallExpansion.Empty;
    }

    /// <summary>Source-compatible construction that supplies a non-null empty topology and a null composition.</summary>
    public ScenarioGraph(
        EntryPointId entryPoint,
        CompilationProfileId profile,
        MethodId rootMethod,
        HttpMethodKind httpMethod,
        string canonicalRoute,
        string operationKey,
        ImmutableArray<ScenarioNode> nodes,
        ImmutableArray<ScenarioEdge> edges,
        ImmutableArray<ScenarioGraphDiagnostic> diagnostics,
        string debugProjection)
        : this(entryPoint, profile, rootMethod, httpMethod, canonicalRoute, operationKey,
            nodes, edges, diagnostics, debugProjection, ScenarioTopology.Empty)
    {
    }

    public EntryPointId EntryPoint { get; }

    public CompilationProfileId Profile { get; }

    public MethodId RootMethod { get; }

    public HttpMethodKind HttpMethod { get; }

    public string CanonicalRoute { get; }

    public string OperationKey { get; }

    public ImmutableArray<ScenarioNode> Nodes { get; }

    public ImmutableArray<ScenarioEdge> Edges { get; }

    public ImmutableArray<ScenarioGraphDiagnostic> Diagnostics { get; }

    public string DebugProjection { get; }

    public ScenarioTopology Topology { get; }

    /// <summary>
    /// Typed service composition for the exact proven alternative group; null when SC001 or any other
    /// fail-closed shape applies. The composition never adds service/data nodes to the flat graph.
    /// </summary>
    public ScenarioServiceComposition? Composition { get; }

    /// <summary>
    /// Typed callback regions derived from the bound callback boundary fact set. Member nodes retain
    /// their flat graph nodes; the typed membership is the non-unconditional authority that the
    /// member operations execute under the callback trigger/cardinality. Initialized but empty when
    /// the request carries no bound callback facts.
    /// </summary>
    public ImmutableArray<ScenarioCallbackRegion> CallbackRegions { get; }

    public ScenarioHandlerTopology? HandlerTopology { get; }

    public ScenarioDispatchHandlerExpansion? DispatchHandlerExpansion { get; }

    public ScenarioRootKind RootKind { get; }

    public ScenarioDirectCallExpansion DirectCallExpansion { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of scenario graphs for one compilation profile. The set records
/// schema and producer versions, the compilation profile, the Program Index fingerprint,
/// canonically ordered graphs, diagnostics, and a deterministic debug representation free of
/// absolute paths. Persistence and cache reconstruction are explicitly out of scope for this
/// contract.
/// </summary>
public sealed record ScenarioGraphSet(
    int SchemaVersion,
    string ProducerVersion,
    CompilationProfile Profile,
    string ProgramIndexFingerprint,
    ImmutableArray<ScenarioGraph> Graphs,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string DebugProjection);
