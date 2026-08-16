using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

/// <summary>
/// Closed vocabulary of exact compiler-bound callback argument target forms. Only compiler-bound
/// anonymous functions, source local functions, and source method groups are admitted; arbitrary
/// delegate variables, events, metadata-only targets, and unresolved overloads remain
/// <see cref="Unknown"/> and never select a candidate.
/// </summary>
public enum CallbackTargetKind
{
    Unknown,
    AnonymousFunction,
    LocalFunction,
    MethodGroup,
}

/// <summary>
/// Closed vocabulary of how many times the exact source callback contract invokes the callback.
/// Only a bounded source contract may prove exactly-once or zero-or-one cardinality; repeated,
/// looped, or uncertain invocation stays <see cref="RepeatedOrUnknown"/> or
/// <see cref="Unknown"/> and is never described as once.
/// </summary>
public enum CallbackCardinality
{
    Unknown,
    ExactlyOnce,
    ZeroOrOne,
    RepeatedOrUnknown,
}

/// <summary>
/// Closed vocabulary of the trigger that guards the exact source contract invocation. A direct
/// invocation outside nested control is <see cref="Unconditional"/>; one invocation in one direct
/// supported <c>if</c> arm is <see cref="Conditional"/> with the exact condition anchor. Everything
/// else stays <see cref="Unknown"/>.
/// </summary>
public enum CallbackTriggerKind
{
    Unknown,
    Unconditional,
    Conditional,
}

/// <summary>
/// Closed vocabulary of callback-local completion at the boundary. A callback-local <c>return</c>
/// rejoins the outer caller; a throw, exception region, unsupported terminal, or uncertain contract
/// stays <see cref="Unknown"/> and must never terminate the outer scenario by inference.
/// </summary>
public enum CallbackCompletionKind
{
    Unknown,
    RejoinsCaller,
}

/// <summary>
/// Closed vocabulary of the evidence that grounds the exact callback contract. accepted contract admits only
/// <see cref="SourceBody"/> contracts; <see cref="FrameworkModel"/> remains reserved for a later
/// pass that adapts an exact versioned framework contract into this generic fact contract.
/// </summary>
public enum CallbackContractProvenance
{
    Unknown,
    SourceBody,
    FrameworkModel,
}

/// <summary>
/// One exact source callback boundary: the caller method, the exact outer invocation operation, the
/// callback parameter ordinal, the exact compiler-bound target anchor, the exact source contract
/// method/invoke anchors, the bounded cardinality, trigger, callback-local completion, contract
/// provenance, canonical member operations, evidence, and certainty. Construction enforces the
/// impossible-state invariants so ambiguous, repeated, arbitrary, and unsupported callbacks can never
/// be presented as a definite once/conditional source target.
/// </summary>
public sealed record CallbackBoundaryFact
{
    public CallbackBoundaryFact(
        CallbackBoundaryId id,
        MethodId callerMethod,
        OperationId outerInvocationOperation,
        int parameterOrdinal,
        CallbackTargetKind targetKind,
        MethodId? targetMethod,
        OperationId? targetBodyOperation,
        MethodId? contractMethod,
        OperationId? contractInvokeOperation,
        CallbackCardinality cardinality,
        CallbackTriggerKind trigger,
        OperationId? triggerCondition,
        CallbackCompletionKind completion,
        CallbackContractProvenance contractProvenance,
        ImmutableArray<string> memberOperations,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(callerMethod.Value, nameof(callerMethod));
        ArgumentException.ThrowIfNullOrWhiteSpace(outerInvocationOperation.Value, nameof(outerInvocationOperation));
        ArgumentOutOfRangeException.ThrowIfNegative(parameterOrdinal);
        if (!Enum.IsDefined(completion))
        {
            throw new ArgumentOutOfRangeException(nameof(completion), "Undefined callback completion kind.");
        }

        CallbackBoundaryFactContracts.ValidateTarget(targetKind, targetMethod, targetBodyOperation);
        CallbackBoundaryFactContracts.ValidateCardinalityTrigger(cardinality, trigger, triggerCondition);
        CallbackBoundaryFactContracts.ValidateContract(
            contractProvenance,
            contractMethod,
            contractInvokeOperation);
        CallbackBoundaryFactContracts.ValidateEvidence(evidence, certainty);

        Id = id;
        CallerMethod = callerMethod;
        OuterInvocationOperation = outerInvocationOperation;
        ParameterOrdinal = parameterOrdinal;
        TargetKind = targetKind;
        TargetMethod = targetMethod;
        TargetBodyOperation = targetBodyOperation;
        ContractMethod = contractMethod;
        ContractInvokeOperation = contractInvokeOperation;
        Cardinality = cardinality;
        Trigger = trigger;
        TriggerCondition = triggerCondition;
        Completion = completion;
        ContractProvenance = contractProvenance;
        MemberOperations = CallbackBoundaryFactContracts.CanonicalizeMembers(memberOperations);
        Evidence = evidence;
        Certainty = certainty;
    }

    public CallbackBoundaryId Id { get; }

    /// <summary>Exact method that owns the outer invocation operation.</summary>
    public MethodId CallerMethod { get; }

    /// <summary>Exact compiler operation of the invocation that receives the callback argument.</summary>
    public OperationId OuterInvocationOperation { get; }

    /// <summary>
    /// Compiler parameter ordinal of the callback parameter, never the source argument position or
    /// parameter name, so named and reordered arguments bind exactly as the compiler resolves them.
    /// </summary>
    public int ParameterOrdinal { get; }

    public CallbackTargetKind TargetKind { get; }

    /// <summary>
    /// Exact source method of a local-function or method-group target. Anonymous functions and
    /// unknown targets never carry this anchor.
    /// </summary>
    public MethodId? TargetMethod { get; }

    /// <summary>
    /// Exact source body operation of an anonymous-function target. Local-function, method-group,
    /// and unknown targets never carry this anchor.
    /// </summary>
    public OperationId? TargetBodyOperation { get; }

    /// <summary>
    /// Exact source method whose delegate parameter the source contract invokes. Present only with a
    /// bounded source-body or framework-model contract.
    /// </summary>
    public MethodId? ContractMethod { get; }

    /// <summary>Exact compiler operation of the single invocation of the callback inside the contract.</summary>
    public OperationId? ContractInvokeOperation { get; }

    public CallbackCardinality Cardinality { get; }

    public CallbackTriggerKind Trigger { get; }

    /// <summary>
    /// Exact compiler condition operation of the direct supported <c>if</c> arm when the trigger is
    /// <see cref="CallbackTriggerKind.Conditional"/>; always null otherwise.
    /// </summary>
    public OperationId? TriggerCondition { get; }

    public CallbackCompletionKind Completion { get; }

    public CallbackContractProvenance ContractProvenance { get; }

    /// <summary>
    /// Canonical, path-independent member-operation identities (canonical
    /// <see cref="OperationId.Value"/> strings) of the callback-local query/mutation operations
    /// associated with this boundary, ordinal sorted and distinct.
    /// </summary>
    public ImmutableArray<string> MemberOperations { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of callback boundary companion facts for one compilation profile.
/// The set records schema and producer versions, the compilation profile, the Program Index
/// fingerprint, canonically Id-ordered boundaries, diagnostics, and a deterministic debug
/// representation free of absolute paths and raw values. Construction enforces the impossible-state
/// invariants: schema version exactly 1, a non-blank producer and fingerprint, a non-null profile,
/// initialized (never default) boundary/diagnostic collections with boundaries ordered canonically by
/// <see cref="CallbackBoundaryFact.Id"/>, and non-blank debug text. Persistence and cache
/// reconstruction are explicitly out of scope for this contract.
/// </summary>
public sealed class CallbackBoundaryFactSet
{
    public CallbackBoundaryFactSet(
        int SchemaVersion,
        string ProducerVersion,
        CompilationProfile Profile,
        string ProgramIndexFingerprint,
        ImmutableArray<CallbackBoundaryFact> Boundaries,
        ImmutableArray<AnalysisDiagnostic> Diagnostics,
        string DebugProjection)
    {
        if (SchemaVersion != 1)
        {
            throw new ArgumentException("The callback boundary fact set schema version must be exactly 1.", nameof(SchemaVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProducerVersion, nameof(ProducerVersion));
        if (Profile is null)
        {
            throw new ArgumentException("The callback boundary fact set requires a non-null compilation profile.", nameof(Profile));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProgramIndexFingerprint, nameof(ProgramIndexFingerprint));
        ArgumentException.ThrowIfNullOrWhiteSpace(DebugProjection, nameof(DebugProjection));
        if (Boundaries.IsDefault || Diagnostics.IsDefault)
        {
            throw new ArgumentException("The callback boundary fact set collections and diagnostics must be initialized.", nameof(Boundaries));
        }

        if (Boundaries.Any(boundary => boundary is null))
        {
            throw new ArgumentException("The callback boundary fact set must not contain null boundaries.", nameof(Boundaries));
        }

        this.SchemaVersion = SchemaVersion;
        this.ProducerVersion = ProducerVersion;
        this.Profile = Profile;
        this.ProgramIndexFingerprint = ProgramIndexFingerprint;
        this.Boundaries = Boundaries
            .OrderBy(boundary => boundary.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        this.Diagnostics = Diagnostics;
        this.DebugProjection = DebugProjection;
    }

    public int SchemaVersion { get; }

    public string ProducerVersion { get; }

    public CompilationProfile Profile { get; }

    public string ProgramIndexFingerprint { get; }

    /// <summary>Boundaries ordered canonically by <see cref="CallbackBoundaryFact.Id"/>.</summary>
    public ImmutableArray<CallbackBoundaryFact> Boundaries { get; }

    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    public string DebugProjection { get; }
}

internal static class CallbackBoundaryFactContracts
{
    public static void ValidateTarget(
        CallbackTargetKind targetKind,
        MethodId? targetMethod,
        OperationId? targetBodyOperation)
    {
        if (!Enum.IsDefined(targetKind))
        {
            throw new ArgumentOutOfRangeException(nameof(targetKind), "Undefined callback target kind.");
        }

        bool hasMethod = targetMethod is not null;
        bool hasBody = targetBodyOperation is not null;
        switch (targetKind)
        {
            case CallbackTargetKind.Unknown:
                if (hasMethod || hasBody)
                {
                    throw new ArgumentException(
                        "An unknown callback target cannot carry a target method or body operation.",
                        nameof(targetKind));
                }

                break;
            case CallbackTargetKind.AnonymousFunction:
                if (hasMethod || !hasBody)
                {
                    throw new ArgumentException(
                        "An anonymous-function callback target requires exactly a target body operation and no target method.",
                        nameof(targetKind));
                }

                break;
            case CallbackTargetKind.LocalFunction:
            case CallbackTargetKind.MethodGroup:
                if (!hasMethod || hasBody)
                {
                    throw new ArgumentException(
                        "A local-function or method-group callback target requires exactly a target method and no target body operation.",
                        nameof(targetKind));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(targetKind), "Undefined callback target kind.");
        }

        if (hasMethod)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetMethod!.Value.Value, nameof(targetMethod));
        }

        if (hasBody)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetBodyOperation!.Value.Value, nameof(targetBodyOperation));
        }
    }

    public static void ValidateCardinalityTrigger(
        CallbackCardinality cardinality,
        CallbackTriggerKind trigger,
        OperationId? triggerCondition)
    {
        if (!Enum.IsDefined(cardinality))
        {
            throw new ArgumentOutOfRangeException(nameof(cardinality), "Undefined callback cardinality.");
        }

        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger), "Undefined callback trigger kind.");
        }

        bool hasCondition = triggerCondition is not null;
        if (trigger == CallbackTriggerKind.Conditional)
        {
            if (!hasCondition)
            {
                throw new ArgumentException(
                    "A conditional callback trigger requires an exact condition operation.",
                    nameof(triggerCondition));
            }
        }
        else if (hasCondition)
        {
            throw new ArgumentException(
                "Only a conditional callback trigger can carry a condition operation.",
                nameof(triggerCondition));
        }

        switch (cardinality)
        {
            case CallbackCardinality.ExactlyOnce:
                if (trigger != CallbackTriggerKind.Unconditional)
                {
                    throw new ArgumentException(
                        "An exactly-once callback must be unconditional.",
                        nameof(cardinality));
                }

                break;
            case CallbackCardinality.ZeroOrOne:
                if (trigger != CallbackTriggerKind.Conditional)
                {
                    throw new ArgumentException(
                        "A zero-or-one callback must be conditional.",
                        nameof(cardinality));
                }

                break;
            case CallbackCardinality.RepeatedOrUnknown:
            case CallbackCardinality.Unknown:
                if (trigger != CallbackTriggerKind.Unknown)
                {
                    throw new ArgumentException(
                        "A repeated or unknown callback must not be promoted to a definite trigger.",
                        nameof(cardinality));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cardinality), "Undefined callback cardinality.");
        }

        if (hasCondition)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(triggerCondition!.Value.Value, nameof(triggerCondition));
        }
    }

    public static void ValidateContract(
        CallbackContractProvenance contractProvenance,
        MethodId? contractMethod,
        OperationId? contractInvokeOperation)
    {
        if (!Enum.IsDefined(contractProvenance))
        {
            throw new ArgumentOutOfRangeException(nameof(contractProvenance), "Undefined callback contract provenance.");
        }

        bool hasMethod = contractMethod is not null;
        bool hasInvoke = contractInvokeOperation is not null;
        if (hasMethod != hasInvoke)
        {
            throw new ArgumentException(
                "Callback contract method and invoke anchors must be supplied together.",
                nameof(contractMethod));
        }

        if (contractProvenance == CallbackContractProvenance.SourceBody
            || contractProvenance == CallbackContractProvenance.FrameworkModel)
        {
            if (!hasMethod)
            {
                throw new ArgumentException(
                    "A source-body or framework-model callback contract requires a contract method and invoke anchor.",
                    nameof(contractProvenance));
            }
        }

        if (hasMethod)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contractMethod!.Value.Value, nameof(contractMethod));
            ArgumentException.ThrowIfNullOrWhiteSpace(contractInvokeOperation!.Value.Value, nameof(contractInvokeOperation));
        }
    }

    public static void ValidateEvidence(
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A callback boundary fact requires non-empty evidence.", nameof(evidence));
        }

        if (evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Artifact)))
        {
            throw new ArgumentException("Callback boundary evidence must reference a non-empty artifact.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A callback boundary fact requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Fact certainty must never exceed its strongest evidence.", nameof(certainty));
        }
    }

    /// <summary>
    /// Validates that member operations are an initialized, non-blank, distinct collection and
    /// returns them canonically ordered by ordinal. Canonical ordering keeps identities and debug
    /// output deterministic across construction order.
    /// </summary>
    public static ImmutableArray<string> CanonicalizeMembers(ImmutableArray<string> memberOperations)
    {
        if (memberOperations.IsDefault)
        {
            throw new ArgumentException(
                "Callback member operations must be an initialized immutable array.",
                nameof(memberOperations));
        }

        if (memberOperations.Any(member => string.IsNullOrWhiteSpace(member)))
        {
            throw new ArgumentException(
                "Callback member operations must be non-blank canonical operation identities.",
                nameof(memberOperations));
        }

        if (memberOperations.Distinct(StringComparer.Ordinal).Count() != memberOperations.Length)
        {
            throw new ArgumentException(
                "Callback member operations must be distinct.",
                nameof(memberOperations));
        }

        return memberOperations
            .OrderBy(member => member, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
