using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

/// <summary>
/// Closed typed vocabulary of normalized comparison operators. Only compiler-proven primitive
/// comparisons project into this vocabulary; arithmetic and unsupported binary or pattern shapes
/// produce no invented comparison fact.
/// </summary>
public enum ComparisonOperatorKind
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

/// <summary>
/// One normalized comparison projected from exact compiler semantics. The fact is revision-local and
/// anchored to the source binary operation that grounds it, with non-empty evidence and explicit
/// certainty that never exceeds its strongest evidence. The left and right operand operation ids
/// carry the exact compiler operands that the normalized operator relates.
/// </summary>
public sealed record ComparisonSemanticFact
{
    public ComparisonSemanticFact(
        SemanticFactId id,
        MethodId method,
        ComparisonOperatorKind @operator,
        OperationId operation,
        OperationId leftOperation,
        OperationId rightOperation,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        SemanticFactContracts.Validate(id, method, evidence, certainty);
        if (!Enum.IsDefined(@operator))
        {
            throw new ArgumentOutOfRangeException(nameof(@operator), "Undefined comparison operator kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(leftOperation.Value, nameof(leftOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(rightOperation.Value, nameof(rightOperation));
        Id = id;
        Method = method;
        Operator = @operator;
        Operation = operation;
        LeftOperation = leftOperation;
        RightOperation = rightOperation;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public ComparisonOperatorKind Operator { get; }

    public OperationId Operation { get; }

    public OperationId LeftOperation { get; }

    public OperationId RightOperation { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One compiler-proven argument-to-parameter binding. The parameter ordinal is the compiler's
/// <c>IArgumentOperation.Parameter.Ordinal</c>, never the source argument position or parameter name,
/// so named and reordered arguments bind exactly as the compiler resolves them.
/// </summary>
public sealed record ArgumentBindingSemanticFact
{
    public ArgumentBindingSemanticFact(
        SemanticFactId id,
        MethodId method,
        MethodId targetMethod,
        int parameterOrdinal,
        OperationId argumentOperation,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        SemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMethod.Value, nameof(targetMethod));
        ArgumentOutOfRangeException.ThrowIfNegative(parameterOrdinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(argumentOperation.Value, nameof(argumentOperation));
        Id = id;
        Method = method;
        TargetMethod = targetMethod;
        ParameterOrdinal = parameterOrdinal;
        ArgumentOperation = argumentOperation;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public MethodId TargetMethod { get; }

    public int ParameterOrdinal { get; }

    public OperationId ArgumentOperation { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One explicit compiler-proven value return provenance. Only a return operation that carries a
/// compiler-visible value produces a fact; void and no-value returns never invent provenance.
/// </summary>
public sealed record ReturnProvenanceSemanticFact
{
    public ReturnProvenanceSemanticFact(
        SemanticFactId id,
        MethodId method,
        OperationId valueOperation,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        SemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueOperation.Value, nameof(valueOperation));
        Id = id;
        Method = method;
        ValueOperation = valueOperation;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public OperationId ValueOperation { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of semantic companion facts for one compilation profile. The set
/// records schema and producer versions, the compilation profile, the Program Index fingerprint,
/// canonically ordered facts, diagnostics, and a deterministic debug representation free of absolute
/// paths. Persistence and cache reconstruction are explicitly out of scope for this contract.
/// </summary>
public sealed record SemanticFactSet(
    int SchemaVersion,
    string ProducerVersion,
    CompilationProfile Profile,
    string ProgramIndexFingerprint,
    ImmutableArray<ComparisonSemanticFact> Comparisons,
    ImmutableArray<ArgumentBindingSemanticFact> ArgumentBindings,
    ImmutableArray<ReturnProvenanceSemanticFact> ReturnProvenances,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string DebugProjection);

internal static class SemanticFactContracts
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
            throw new ArgumentException("A semantic fact requires non-empty evidence.", nameof(evidence));
        }

        if (evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Artifact)))
        {
            throw new ArgumentException("Semantic-fact evidence must reference a non-empty artifact.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A semantic fact requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Fact certainty must never exceed its strongest evidence.", nameof(certainty));
        }
    }
}
