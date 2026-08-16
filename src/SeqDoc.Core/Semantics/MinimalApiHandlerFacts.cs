using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace SeqDoc.Core.Semantics;

public enum MinimalApiHandlerOperationKind { Invocation, Delay, Outcome }

public sealed record MinimalApiHandlerArm
{
    public MinimalApiHandlerArm(int sourceOrdinal, bool isTrue = true, int? decisionOrdinal = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOrdinal);
        if (decisionOrdinal is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decisionOrdinal));
        }
        SourceOrdinal = sourceOrdinal;
        IsTrue = isTrue;
        DecisionOrdinal = decisionOrdinal;
    }

    public int SourceOrdinal { get; init; }
    public bool IsTrue { get; init; }
    public int? DecisionOrdinal { get; init; }
}

public sealed record MinimalApiHandlerParameter(
    string Name,
    string TypeName,
    HttpBindingKind BindingKind,
    string? BindingReason = null,
    ImmutableArray<EvidenceRef> Evidence = default,
    CertaintyLevel Certainty = CertaintyLevel.Exact);

public sealed record MinimalApiHandlerPredicate(
    OperationId Operation,
    PredicateExpression Expression,
    int Constant,
    MinimalApiHandlerArm TrueArm,
    MinimalApiHandlerArm FalseArm,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty,
    bool TrueArmTerminates = false)
{
    public string PredicateText => ExpressionText(Expression);

    private static string ExpressionText(PredicateExpression expression)
        => expression.Kind == PredicateExpressionKind.Comparison
            ? $"{expression.Children[0].DisplayName} {expression.ComparisonOperator switch
            {
                PredicateComparisonOperatorKind.LessThanOrEqual => "is at most",
                PredicateComparisonOperatorKind.LessThan => "is less than",
                PredicateComparisonOperatorKind.GreaterThanOrEqual => "is at least",
                PredicateComparisonOperatorKind.GreaterThan => "is greater than",
                PredicateComparisonOperatorKind.NotEqual => "is not",
                _ => "equals"
            }} {expression.Children[1].ConstantValue}"
            : "unsupported predicate";
}

public sealed record MinimalApiHandlerOperation(
    OperationId Id,
    MinimalApiHandlerOperationKind Kind,
    string? TargetIdentity,
    int? DelayMilliseconds,
    int? StatusCode,
    string? FactoryIdentity,
    MinimalApiHandlerArm Arm,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

public sealed record MinimalApiHandlerOutcome(
    OperationId Id,
    string FactoryIdentity,
    int? StatusCode,
    MinimalApiHandlerArm Arm,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

public sealed record MinimalApiHandlerFact : BehaviorFact
{
    [SetsRequiredMembers]
    public MinimalApiHandlerFact(CallbackBoundaryId boundaryId, MethodId handlerRoot, OperationId bodyAnchor,
        ImmutableArray<MinimalApiHandlerParameter> parameters, ImmutableArray<MinimalApiHandlerOperation> operations,
        ImmutableArray<MinimalApiHandlerPredicate> predicates, ImmutableArray<MinimalApiHandlerOutcome> outcomes,
        ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        if (string.IsNullOrWhiteSpace(boundaryId.Value) || boundaryId.Value.EndsWith(':') || string.IsNullOrWhiteSpace(handlerRoot.Value)
            || string.IsNullOrWhiteSpace(bodyAnchor.Value) || evidence.IsDefaultOrEmpty || certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("Minimal API handler facts require canonical identity, evidence, and certainty.");
        }
        if (parameters.IsDefault || operations.IsDefault || predicates.IsDefault || outcomes.IsDefault
            || parameters.Any(item => string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.TypeName)
                || !Enum.IsDefined(item.BindingKind) || item.Evidence.IsDefaultOrEmpty || item.Certainty == CertaintyLevel.Unknown
                || item.Certainty < item.Evidence.Max(evidence => evidence.Certainty))
            || parameters.GroupBy(item => item.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new ArgumentException("Minimal API handler parameters require names and types.");
        }
        if (operations.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id.Value)
            || !Enum.IsDefined(item.Kind) || item.Evidence.IsDefaultOrEmpty || item.Certainty == CertaintyLevel.Unknown
            || item.Certainty < item.Evidence.Max(evidence => evidence.Certainty)
            || item.Kind == MinimalApiHandlerOperationKind.Invocation && (string.IsNullOrWhiteSpace(item.TargetIdentity) || item.StatusCode is not null || item.DelayMilliseconds is not null || item.FactoryIdentity is not null)
            || item.Kind == MinimalApiHandlerOperationKind.Delay && (string.IsNullOrWhiteSpace(item.TargetIdentity) || item.DelayMilliseconds is null or <= 0 || item.StatusCode is not null || item.FactoryIdentity is not null)
            || item.Kind == MinimalApiHandlerOperationKind.Outcome && (string.IsNullOrWhiteSpace(item.FactoryIdentity) || item.StatusCode is not (>= 100 and <= 599) || item.DelayMilliseconds is not null)))
        {
            throw new ArgumentException("Minimal API handler operations must be complete and evidenced.");
        }
        if (operations.Select(item => item.Id.Value).Distinct(StringComparer.Ordinal).Count() != operations.Length
            || outcomes.Select(item => item.Id.Value).Distinct(StringComparer.Ordinal).Count() != outcomes.Length
            || outcomes.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id.Value)
            || string.IsNullOrWhiteSpace(item.FactoryIdentity) || item.StatusCode is null
             || item.StatusCode is not (>= 100 and <= 599)
             || item.Evidence.IsDefaultOrEmpty || item.Certainty == CertaintyLevel.Unknown
             || item.Certainty < item.Evidence.Max(evidence => evidence.Certainty)
             || operations.Count(operation => operation.Kind == MinimalApiHandlerOperationKind.Outcome
                 && operation.Id == item.Id && operation.StatusCode == item.StatusCode
                 && operation.FactoryIdentity == item.FactoryIdentity
                 && operation.Arm.DecisionOrdinal == item.Arm.DecisionOrdinal
                 && operation.Arm.IsTrue == item.Arm.IsTrue) != 1))
        {
            throw new ArgumentException("Minimal API handler outcomes must be complete and evidenced.");
        }
        if (predicates.Any(predicate => predicate.Evidence.IsDefaultOrEmpty || predicate.Certainty == CertaintyLevel.Unknown
            || predicate.Certainty < predicate.Evidence.Max(evidence => evidence.Certainty)
            || !IsValidComparison(predicate.Expression)
            || predicate.Expression.Children.Length != 2
            || predicate.TrueArm.IsTrue == predicate.FalseArm.IsTrue
            || predicate.TrueArm.DecisionOrdinal != predicate.FalseArm.DecisionOrdinal)
            || predicates.Select(predicate => predicate.Operation.Value).Distinct(StringComparer.Ordinal).Count() != predicates.Length)
        {
            throw new ArgumentException("Minimal API predicates must be unique, explicit, evidenced comparisons with opposite arms.");
        }
        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Fact certainty cannot be stronger than its evidence.");
        }
        BoundaryId = boundaryId; HandlerRoot = handlerRoot; BodyAnchor = bodyAnchor;
        Parameters = parameters; Operations = operations; Predicates = predicates; Outcomes = outcomes;
        Id = new BehaviorFactId($"behavior-fact:v1:minimal-handler:{boundaryId.Value}");
        Evidence = evidence; Certainty = certainty;
    }

    public CallbackBoundaryId BoundaryId { get; }
    public MethodId HandlerRoot { get; }
    public OperationId BodyAnchor { get; }
    public ImmutableArray<MinimalApiHandlerParameter> Parameters { get; }
    public ImmutableArray<MinimalApiHandlerOperation> Operations { get; }
    public ImmutableArray<MinimalApiHandlerPredicate> Predicates { get; }
    public ImmutableArray<MinimalApiHandlerOutcome> Outcomes { get; }

    private static bool IsValidComparison(PredicateExpression expression)
        => expression.Kind == PredicateExpressionKind.Comparison
            && expression.ComparisonOperator is { } comparisonOperator
            && Enum.IsDefined(comparisonOperator)
            && expression.Children.Length == 2
            && expression.Children.All(child => child is not null && IsValidExpression(child));

    private static bool IsValidExpression(PredicateExpression expression)
        => Enum.IsDefined(expression.Kind)
            && expression.Children.All(child => child is not null && IsValidExpression(child));
}

public sealed class MinimalApiHandlerFactSet
{
    public MinimalApiHandlerFactSet(CompilationProfile profile, string fingerprint,
        IEnumerable<MinimalApiHandlerFact> facts, IEnumerable<AnalysisDiagnostic> diagnostics, string debugProjection)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugProjection);

        var factArray = facts.ToArray();
        if (factArray.Any(fact => fact is null))
        {
            throw new ArgumentException("Minimal API handler facts must not contain null items.", nameof(facts));
        }
        if (factArray.Select(fact => fact.BoundaryId.Value).Distinct(StringComparer.Ordinal).Count() != factArray.Length)
        {
            throw new ArgumentException("Minimal API handler facts must not duplicate boundary identities.", nameof(facts));
        }
        if (factArray.Select(fact => fact.Id.Value).Distinct(StringComparer.Ordinal).Count() != factArray.Length)
        {
            throw new ArgumentException("Minimal API handler facts must not duplicate fact identities.", nameof(facts));
        }

        Profile = profile;
        ProgramIndexFingerprint = fingerprint;
        Facts = factArray.OrderBy(fact => fact.BoundaryId.Value, StringComparer.Ordinal).ToImmutableArray();
        Diagnostics = diagnostics
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        DebugProjection = debugProjection;
    }

    public CompilationProfile Profile { get; }
    public string ProgramIndexFingerprint { get; }
    public ImmutableArray<MinimalApiHandlerFact> Facts { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }
    public string DebugProjection { get; }
}
