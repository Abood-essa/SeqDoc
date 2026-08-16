using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

/// <summary>
/// One compiler-proven status-switch arm: a switch over a status-typed enum value whose case arm
/// reaches exactly one distinct admitted ASP.NET Core outcome helper. The status member is the exact
/// enum constant (or the reserved <c>default</c> token for the default arm); the helper kind and
/// outcome operation are exact. Ambiguous arms (zero or several distinct helpers) never produce a
/// fact (architecture decision). A CreatedAtAction arm additionally carries the compiler-bound target controller
/// method identity so the scenario join never resolves by action-name text alone. This fact supplies
/// exact controller status mapping without adding or reinterpreting Method Flow switch edges.
/// </summary>
public sealed record StatusSwitchArmFact
{
    public StatusSwitchArmFact(
        SemanticFactId id,
        MethodId method,
        OperationId switchOperation,
        string statusEnumType,
        string statusMemberName,
        HttpOutcomeHelperKind helperKind,
        OperationId outcomeOperation,
        string? createdActionName,
        MethodId? createdTargetMethod,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        NonGetSemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(switchOperation.Value, nameof(switchOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(statusEnumType, nameof(statusEnumType));
        ArgumentException.ThrowIfNullOrWhiteSpace(statusMemberName, nameof(statusMemberName));
        ArgumentException.ThrowIfNullOrWhiteSpace(outcomeOperation.Value, nameof(outcomeOperation));
        if (!Enum.IsDefined(helperKind))
        {
            throw new ArgumentOutOfRangeException(nameof(helperKind), "Undefined HTTP outcome helper kind.");
        }

        if (helperKind == HttpOutcomeHelperKind.CreatedAtAction && string.IsNullOrWhiteSpace(createdActionName))
        {
            throw new ArgumentException("A CreatedAtAction status arm requires the compiler-proven action name.", nameof(createdActionName));
        }

        if (helperKind == HttpOutcomeHelperKind.CreatedAtAction && createdTargetMethod is null)
        {
            throw new ArgumentException("A CreatedAtAction status arm requires the compiler-bound target controller method identity.", nameof(createdTargetMethod));
        }

        Id = id;
        Method = method;
        SwitchOperation = switchOperation;
        StatusEnumType = statusEnumType;
        StatusMemberName = statusMemberName;
        HelperKind = helperKind;
        OutcomeOperation = outcomeOperation;
        CreatedActionName = createdActionName;
        CreatedTargetMethod = createdTargetMethod;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public OperationId SwitchOperation { get; }

    /// <summary>Canonical fully qualified identity of the enum type whose constant the arm tests.</summary>
    public string StatusEnumType { get; }

    /// <summary>The exact enum member name, or the reserved token <c>default</c> for the default arm.</summary>
    public string StatusMemberName { get; }

    public HttpOutcomeHelperKind HelperKind { get; }

    public OperationId OutcomeOperation { get; }

    /// <summary>Compiler-proven action name for a CreatedAtAction arm; null for every other helper.</summary>
    public string? CreatedActionName { get; }

    /// <summary>
    /// Compiler-bound target controller method identity for a CreatedAtAction arm; null for every
    /// other helper. The scenario join resolves the Get entry point by this identity, never by a
    /// global action-name text match.
    /// </summary>
    public MethodId? CreatedTargetMethod { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One compiler-proven direct terminal outcome: an ASP.NET Core outcome helper invocation reached on
/// the method's terminal path OUTSIDE every status-switch arm (for example a success-path
/// CreatedAtAction return after an admitted failure switch). This fact is additive and represented
/// separately from <see cref="StatusSwitchArmFact"/>; it never synthesizes a status member such as
/// <c>success</c> and never claims a status-to-outcome mapping. The exact canonical invocation
/// operation identity, HTTP helper kind, source ordinal, and (for CreatedAtAction) the compiler-bound
/// target controller method identity are retained. The scenario builder joins this companion fact to
/// the exact framework <c>HttpDirectOutcomeFact</c> by operation identity and only when status arms
/// exist, so ordinary direct outcomes on the accepted structural-result path are never duplicated.
/// </summary>
public sealed record DirectTerminalOutcomeFact
{
    public DirectTerminalOutcomeFact(
        SemanticFactId id,
        MethodId method,
        OperationId operation,
        HttpOutcomeHelperKind helperKind,
        string? createdActionName,
        MethodId? createdTargetMethod,
        int sequenceOrdinal,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        NonGetSemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentOutOfRangeException.ThrowIfNegative(sequenceOrdinal);
        if (!Enum.IsDefined(helperKind))
        {
            throw new ArgumentOutOfRangeException(nameof(helperKind), "Undefined HTTP outcome helper kind.");
        }

        if (helperKind == HttpOutcomeHelperKind.CreatedAtAction && string.IsNullOrWhiteSpace(createdActionName))
        {
            throw new ArgumentException("A CreatedAtAction direct terminal requires the compiler-proven action name.", nameof(createdActionName));
        }

        if (helperKind == HttpOutcomeHelperKind.CreatedAtAction && createdTargetMethod is null)
        {
            throw new ArgumentException("A CreatedAtAction direct terminal requires the compiler-bound target controller method identity.", nameof(createdTargetMethod));
        }

        Id = id;
        Method = method;
        Operation = operation;
        HelperKind = helperKind;
        CreatedActionName = createdActionName;
        CreatedTargetMethod = createdTargetMethod;
        SequenceOrdinal = sequenceOrdinal;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    /// <summary>Exact canonical invocation operation identity that produced the terminal outcome.</summary>
    public OperationId Operation { get; }

    public HttpOutcomeHelperKind HelperKind { get; }

    /// <summary>Compiler-proven action name for a CreatedAtAction terminal; null for every other helper.</summary>
    public string? CreatedActionName { get; }

    /// <summary>
    /// Compiler-bound target controller method identity for a CreatedAtAction terminal; null for every
    /// other helper. The scenario join resolves the Get entry point by this identity, never by a
    /// global action-name text match.
    /// </summary>
    public MethodId? CreatedTargetMethod { get; }

    /// <summary>Deterministic source-order position of the terminal invocation within its method.</summary>
    public int SequenceOrdinal { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>Closed vocabulary of compiler-proven state-assignment value shapes.</summary>
public enum StateAssignmentValueKind
{
    Unknown,
    EnumConstant,
    Literal,
    Parameter,
    Local,
    ObjectCreation,
}

/// <summary>
/// One exact property/field assignment projected from compiler operations. The target member and the
/// assigned value are exact; enum constants name the exact member, literals carry the constant value,
/// and parameter/local/object-creation values carry their canonical identity. This is an additive
/// companion fact and never adds Method Flow edges.
/// </summary>
public sealed record StateAssignmentSemanticFact
{
    public StateAssignmentSemanticFact(
        SemanticFactId id,
        MethodId method,
        OperationId operation,
        string targetMember,
        string targetType,
        StateAssignmentValueKind valueKind,
        string value,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        int sequenceOrdinal = 0)
    {
        NonGetSemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMember, nameof(targetMember));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType, nameof(targetType));
        ArgumentOutOfRangeException.ThrowIfNegative(sequenceOrdinal);
        if (!Enum.IsDefined(valueKind) || valueKind == StateAssignmentValueKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(valueKind), "Undefined state-assignment value kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Id = id;
        Method = method;
        Operation = operation;
        TargetMember = targetMember;
        TargetType = targetType;
        ValueKind = valueKind;
        Value = value;
        Evidence = evidence;
        Certainty = certainty;
        SequenceOrdinal = sequenceOrdinal;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public OperationId Operation { get; }

    /// <summary>Canonical identity of the assigned member (containing type plus member name).</summary>
    public string TargetMember { get; }

    public string TargetType { get; }

    public StateAssignmentValueKind ValueKind { get; }

    public string Value { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }

    /// <summary>Deterministic source-order position of the assignment within its method.</summary>
    public int SequenceOrdinal { get; }
}

/// <summary>Closed vocabulary of relational/time companion-fact shapes.</summary>
public enum RelationalTimeFactKind
{
    Unknown,
    RelationalPattern,
    TimeComparison,
}

/// <summary>
/// One conservative relational-pattern or DateTime-comparison companion fact. Relational patterns
/// (for example <c>quantity is &lt;= 0</c>) and DateTime comparisons (for example
/// <c>forDate &lt; DateTime.UtcNow.Date</c>) carry the exact normalized operator and the compiler
/// operands; a constant threshold is retained when the compiler proved one. The fact is conservative
/// because patterns and framework time semantics are not the exact primitive vocabulary.
/// </summary>
public sealed record RelationalTimeSemanticFact
{
    public RelationalTimeSemanticFact(
        SemanticFactId id,
        MethodId method,
        OperationId operation,
        RelationalTimeFactKind kind,
        ComparisonOperatorKind @operator,
        OperationId leftOperation,
        OperationId? rightOperation,
        string? thresholdValue,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        NonGetSemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(leftOperation.Value, nameof(leftOperation));
        if (!Enum.IsDefined(kind) || kind == RelationalTimeFactKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined relational/time fact kind.");
        }

        if (!Enum.IsDefined(@operator))
        {
            throw new ArgumentOutOfRangeException(nameof(@operator), "Undefined comparison operator kind.");
        }

        if (rightOperation is null && thresholdValue is null)
        {
            throw new ArgumentException("A relational/time fact requires a right operand or a constant threshold.", nameof(rightOperation));
        }

        Id = id;
        Method = method;
        Operation = operation;
        Kind = kind;
        Operator = @operator;
        LeftOperation = leftOperation;
        RightOperation = rightOperation;
        ThresholdValue = thresholdValue;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public OperationId Operation { get; }

    public RelationalTimeFactKind Kind { get; }

    public ComparisonOperatorKind Operator { get; }

    public OperationId LeftOperation { get; }

    public OperationId? RightOperation { get; }

    /// <summary>Constant threshold value when the compiler proved one; null otherwise.</summary>
    public string? ThresholdValue { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>Closed vocabulary of evidenced source observations.</summary>
public enum SourceObservationKind
{
    Unknown,
    Todo,
    Note,
}

/// <summary>
/// One evidenced source observation (for example a TODO or NOTE comment) anchored to a method. An
/// observation is explicitly non-interaction: it never produces a scenario interaction, diagram
/// message, or behavioral edge. It retains its source evidence and conservative certainty.
/// </summary>
public sealed record SourceObservationSemanticFact
{
    public SourceObservationSemanticFact(
        SemanticFactId id,
        MethodId method,
        OperationId anchorOperation,
        SourceObservationKind kind,
        string text,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        NonGetSemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(anchorOperation.Value, nameof(anchorOperation));
        if (!Enum.IsDefined(kind) || kind == SourceObservationKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined source observation kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));
        Id = id;
        Method = method;
        AnchorOperation = anchorOperation;
        Kind = kind;
        Text = text;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    /// <summary>The enclosing statement operation that anchors the comment in the method.</summary>
    public OperationId AnchorOperation { get; }

    public SourceObservationKind Kind { get; }

    public string Text { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>Closed vocabulary of EF operation sequence entries.</summary>
public enum EfOperationSequenceKind
{
    Unknown,
    QueryTerminal,
    Mutation,
}

/// <summary>
/// One source-order EF operation of a method. The Roslyn collector records every recognized EF query
/// terminal and mutation call in traversal order so the scenario builder can order multiple query and
/// mutation facts authoritatively without relying on unstable identity hashing.
/// </summary>
public sealed record EfOperationSequenceFact
{
    public EfOperationSequenceFact(
        MethodId method,
        OperationId operation,
        EfOperationSequenceKind kind,
        int ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value, nameof(method));
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        if (!Enum.IsDefined(kind) || kind == EfOperationSequenceKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined EF operation sequence kind.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        Method = method;
        Operation = operation;
        Kind = kind;
        Ordinal = ordinal;
    }

    public MethodId Method { get; }

    public OperationId Operation { get; }

    public EfOperationSequenceKind Kind { get; }

    public int Ordinal { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of non-Get semantic companion facts for one compilation profile:
/// status-switch arms, direct terminal outcomes, state assignments, relational/time comparisons, source
/// observations, and the ordered EF query/mutation sequence. The set records schema and producer
/// versions, the compilation profile, the Program Index fingerprint, canonically ordered facts,
/// diagnostics, and a deterministic debug representation free of absolute paths. Persistence and
/// cache reconstruction are explicitly out of scope for this contract.
/// </summary>
public sealed record NonGetSemanticFactSet(
    int SchemaVersion,
    string ProducerVersion,
    CompilationProfile Profile,
    string ProgramIndexFingerprint,
    ImmutableArray<StatusSwitchArmFact> StatusSwitchArms,
    ImmutableArray<DirectTerminalOutcomeFact> DirectTerminalOutcomes,
    ImmutableArray<StateAssignmentSemanticFact> StateAssignments,
    ImmutableArray<RelationalTimeSemanticFact> RelationalTimeFacts,
    ImmutableArray<SourceObservationSemanticFact> SourceObservations,
    ImmutableArray<EntityFrameworkMutationFact> EntityFrameworkMutations,
    ImmutableArray<EfOperationSequenceFact> EfOperationSequence,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string DebugProjection);

internal static class NonGetSemanticFactContracts
{
    public static void Validate(
        SemanticFactId id,
        MethodId method,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value, nameof(method));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A non-Get semantic fact requires non-empty evidence.", nameof(evidence));
        }

        if (evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Artifact)))
        {
            throw new ArgumentException("Non-Get semantic-fact evidence must reference a non-empty artifact.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A non-Get semantic fact requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Fact certainty must never exceed its strongest evidence.", nameof(certainty));
        }
    }
}
