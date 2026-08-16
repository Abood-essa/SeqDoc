using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

/// <summary>
/// Closed vocabulary of static result-factory names admitted by the translation-alpha structural
/// result projection. Admission requires the compiler-proven self-returning factory shape on a type
/// that exposes an instance boolean IsSuccess member; lookalike result shapes never project a fact.
/// </summary>
public enum StructuralResultFactoryKind
{
    Unknown,
    Success,
    NotFound,
    Conflict,
    ValidationError,
}

/// <summary>
/// One compiler-proven static result-factory invocation. The factory kind, IsSuccess polarity, and
/// optional argument operation are projected from exact compiler symbols and member shapes, never
/// from raw names alone. The fact distinguishes the success/data path from the failure/status path
/// at the boundary that constructs the result.
/// </summary>
public sealed record StructuralResultFactoryFact
{
    public StructuralResultFactoryFact(
        SemanticFactId id,
        MethodId method,
        OperationId operation,
        string resultType,
        StructuralResultFactoryKind factoryKind,
        bool isSuccess,
        OperationId? argumentOperation,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        StructuralResultFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(resultType, nameof(resultType));
        if (!Enum.IsDefined(factoryKind) || factoryKind == StructuralResultFactoryKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(factoryKind), "Undefined structural result factory kind.");
        }

        Id = id;
        Method = method;
        Operation = operation;
        ResultType = resultType;
        FactoryKind = factoryKind;
        IsSuccess = isSuccess;
        ArgumentOperation = argumentOperation;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public OperationId Operation { get; }

    public string ResultType { get; }

    public StructuralResultFactoryKind FactoryKind { get; }

    public bool IsSuccess { get; }

    public OperationId? ArgumentOperation { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One compiler-proven outcome helper invocation reached on one path of a result decision. The
/// helper kind and outcome operation anchor are exact; downstream joins match the helper kind to the
/// accepted ASP.NET Core direct-outcome facts to recover the exact status code.
/// </summary>
public sealed record StructuralOutcomePath(
    HttpOutcomeHelperKind HelperKind,
    OperationId OutcomeOperation);

/// <summary>
/// One compiler-proven decision on a result's exact IsSuccess member. The decision, property-read,
/// and result-operand anchors are exact operations; the polarity records whether the branch condition
/// negates IsSuccess. The success and failure paths record the exact outcome helpers reached on each
/// path, so downstream joins never guess which branch produces which HTTP outcome.
/// <see cref="ResultLocalName"/> names the local/parameter operand the decision tests so scenario
/// joins can locate the value node in the accepted local value graph.
/// </summary>
public sealed record StructuralResultDecisionFact
{
    public StructuralResultDecisionFact(
        SemanticFactId id,
        MethodId method,
        OperationId decisionOperation,
        OperationId propertyOperation,
        OperationId resultOperation,
        string? resultLocalName,
        bool isSuccessNegated,
        ImmutableArray<StructuralOutcomePath> successPath,
        ImmutableArray<StructuralOutcomePath> failurePath,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        StructuralResultFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionOperation.Value, nameof(decisionOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyOperation.Value, nameof(propertyOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(resultOperation.Value, nameof(resultOperation));
        if (successPath.IsDefault || failurePath.IsDefault)
        {
            throw new ArgumentException("Result decision paths must be initialized immutable arrays.", nameof(id));
        }

        Id = id;
        Method = method;
        DecisionOperation = decisionOperation;
        PropertyOperation = propertyOperation;
        ResultOperation = resultOperation;
        ResultLocalName = resultLocalName;
        IsSuccessNegated = isSuccessNegated;
        SuccessPath = successPath;
        FailurePath = failurePath;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public OperationId DecisionOperation { get; }

    public OperationId PropertyOperation { get; }

    public OperationId ResultOperation { get; }

    public string? ResultLocalName { get; }

    public bool IsSuccessNegated { get; }

    public ImmutableArray<StructuralOutcomePath> SuccessPath { get; }

    public ImmutableArray<StructuralOutcomePath> FailurePath { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of structural result companion facts for one compilation profile.
/// The set records schema and producer versions, the compilation profile, the Program Index
/// fingerprint, canonically ordered factory and decision facts, diagnostics, and a deterministic
/// debug representation free of absolute paths. Persistence and cache reconstruction are explicitly
/// out of scope for this contract.
/// </summary>
public sealed record StructuralResultFactSet(
    int SchemaVersion,
    string ProducerVersion,
    CompilationProfile Profile,
    string ProgramIndexFingerprint,
    ImmutableArray<StructuralResultFactoryFact> Factories,
    ImmutableArray<StructuralResultDecisionFact> Decisions,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string DebugProjection);

internal static class StructuralResultFactContracts
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
            throw new ArgumentException("A structural result fact requires non-empty evidence.", nameof(evidence));
        }

        if (evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Artifact)))
        {
            throw new ArgumentException("Structural result evidence must reference a non-empty artifact.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A structural result fact requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Fact certainty must never exceed its strongest evidence.", nameof(certainty));
        }
    }
}
