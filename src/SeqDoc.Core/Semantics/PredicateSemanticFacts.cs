using System.Collections.Immutable;
using System.Globalization;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

/// <summary>
/// Closed vocabulary of normalized predicate-expression nodes projected from exact compiler
/// operations. Leaf nodes (null/boolean/enum/numeric/string/character constants and stable
/// symbol/opaque values) carry no children; <see cref="BooleanTruth"/> and <see cref="Negation"/>
/// carry exactly one child; comparison, logical, and binary-arithmetic composites carry exactly two
/// ordered children. Explicit negation is always represented structurally, never by swapping text.
/// </summary>
public enum PredicateExpressionKind
{
    NullConstant,
    BooleanConstant,
    EnumConstant,
    NumericConstant,
    StringConstant,
    CharacterConstant,
    SymbolValue,
    OpaqueValue,
    BooleanTruth,
    Comparison,
    LogicalAnd,
    LogicalOr,
    Negation,
    BinaryArithmetic,
}

/// <summary>Closed vocabulary of normalized comparison operators over predicate operands.</summary>
public enum PredicateComparisonOperatorKind
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

/// <summary>Closed vocabulary of supported binary arithmetic operators in predicate operands.</summary>
public enum PredicateArithmeticOperatorKind
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
}

/// <summary>
/// One normalized predicate-expression node. The node retains the stable expression kind, its ordered
/// children (empty for leaves), the exact operator kind for comparison/arithmetic nodes, the canonical
/// type name of the expression, a stable display identity for symbol/opaque operands, and a canonical
/// constant value for constant leaves. No node ever carries source spelling, arbitrary
/// <c>ToString()</c> output, or runtime-evaluated values. Construction enforces the exact shape
/// invariants: defined kind, exact arity, operator fields only on their owning composite kinds,
/// non-blank type name, non-blank display identity for symbol/opaque operands, a constant value that
/// matches its leaf kind, and no unrelated fields on any node.
/// </summary>
public sealed record PredicateExpression
{
    public PredicateExpression(
        PredicateExpressionKind kind,
        ImmutableArray<PredicateExpression> children,
        string typeName,
        PredicateComparisonOperatorKind? comparisonOperator = null,
        PredicateArithmeticOperatorKind? arithmeticOperator = null,
        string? displayName = null,
        string? constantValue = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined predicate expression kind.");
        }

        int expectedArity = kind switch
        {
            PredicateExpressionKind.BooleanTruth or PredicateExpressionKind.Negation => 1,
            PredicateExpressionKind.Comparison
                or PredicateExpressionKind.LogicalAnd
                or PredicateExpressionKind.LogicalOr
                or PredicateExpressionKind.BinaryArithmetic => 2,
            _ => 0,
        };
        if (children.IsDefault || children.Length != expectedArity)
        {
            throw new ArgumentException(
                $"A {kind} predicate expression requires exactly {expectedArity} initialized ordered children.",
                nameof(children));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(typeName, nameof(typeName));

        if (kind == PredicateExpressionKind.Comparison)
        {
            if (comparisonOperator is null || !Enum.IsDefined(comparisonOperator.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(comparisonOperator),
                    "A comparison predicate expression requires a defined comparison operator.");
            }
        }
        else if (comparisonOperator is not null)
        {
            throw new ArgumentException(
                $"A {kind} predicate expression must not carry a comparison operator.",
                nameof(comparisonOperator));
        }

        if (kind == PredicateExpressionKind.BinaryArithmetic)
        {
            if (arithmeticOperator is null || !Enum.IsDefined(arithmeticOperator.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arithmeticOperator),
                    "A binary arithmetic predicate expression requires a defined arithmetic operator.");
            }
        }
        else if (arithmeticOperator is not null)
        {
            throw new ArgumentException(
                $"A {kind} predicate expression must not carry an arithmetic operator.",
                nameof(arithmeticOperator));
        }

        switch (kind)
        {
            case PredicateExpressionKind.NullConstant:
                if (constantValue is not null || displayName is not null)
                {
                    throw new ArgumentException(
                        "A null constant carries neither a constant value nor a display name.",
                        nameof(constantValue));
                }

                break;
            case PredicateExpressionKind.BooleanConstant:
                if (constantValue is not ("true" or "false"))
                {
                    throw new ArgumentException(
                        "A boolean constant requires the canonical 'true' or 'false' value.",
                        nameof(constantValue));
                }

                RejectDisplayName(displayName, kind);
                break;
            case PredicateExpressionKind.NumericConstant:
                if (constantValue is null
                    || !double.TryParse(
                        constantValue,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out double parsedNumeric)
                    || !double.IsFinite(parsedNumeric))
                {
                    throw new ArgumentException(
                        "A numeric constant requires a finite invariant numeric value.",
                        nameof(constantValue));
                }

                RejectDisplayName(displayName, kind);
                break;
            case PredicateExpressionKind.StringConstant:
                if (constantValue is null)
                {
                    throw new ArgumentException("A string constant requires a constant value.", nameof(constantValue));
                }

                RejectDisplayName(displayName, kind);
                break;
            case PredicateExpressionKind.CharacterConstant:
                if (constantValue is null || constantValue.Length != 1)
                {
                    throw new ArgumentException(
                        "A character constant requires exactly one character.",
                        nameof(constantValue));
                }

                RejectDisplayName(displayName, kind);
                break;
            case PredicateExpressionKind.EnumConstant:
                if (string.IsNullOrWhiteSpace(constantValue))
                {
                    throw new ArgumentException(
                        "An enum constant requires the exact member name as its constant value.",
                        nameof(constantValue));
                }

                RejectDisplayName(displayName, kind);
                break;
            case PredicateExpressionKind.SymbolValue:
            case PredicateExpressionKind.OpaqueValue:
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    throw new ArgumentException(
                        $"A {kind} predicate operand requires a non-blank stable display identity.",
                        nameof(displayName));
                }

                if (constantValue is not null)
                {
                    throw new ArgumentException(
                        $"A {kind} predicate operand must not carry a constant value.",
                        nameof(constantValue));
                }

                break;
            default:
                if (displayName is not null || constantValue is not null)
                {
                    throw new ArgumentException(
                        $"A {kind} predicate expression must not carry a display name or constant value.",
                        nameof(constantValue));
                }

                break;
        }

        Kind = kind;
        Children = children;
        TypeName = typeName;
        ComparisonOperator = comparisonOperator;
        ArithmeticOperator = arithmeticOperator;
        DisplayName = displayName;
        ConstantValue = constantValue;
    }

    public PredicateExpressionKind Kind { get; }

    /// <summary>Gets the ordered children; empty for leaves and non-null for every admitted node.</summary>
    public ImmutableArray<PredicateExpression> Children { get; }

    /// <summary>Gets the exact comparison operator for a <see cref="PredicateExpressionKind.Comparison"/> node.</summary>
    public PredicateComparisonOperatorKind? ComparisonOperator { get; }

    /// <summary>Gets the exact arithmetic operator for a <see cref="PredicateExpressionKind.BinaryArithmetic"/> node.</summary>
    public PredicateArithmeticOperatorKind? ArithmeticOperator { get; }

    /// <summary>Gets the canonical type name of the expression (for example <c>System.Boolean</c>).</summary>
    public string TypeName { get; }

    /// <summary>Gets the stable display identity for symbol/opaque operands; null on every other node.</summary>
    public string? DisplayName { get; }

    /// <summary>Gets the canonical constant value on constant leaves; null on every other node.</summary>
    public string? ConstantValue { get; }

    private static void RejectDisplayName(string? displayName, PredicateExpressionKind kind)
    {
        if (displayName is not null)
        {
            throw new ArgumentException(
                $"A {kind} predicate expression must not carry a display name.",
                nameof(displayName));
        }
    }
}

/// <summary>
/// One canonical typed normalized predicate fact for one material decision condition. The fact anchors
/// the exact compiler condition operation, retains the whole predicate tree without source spelling,
/// and carries the compilation profile and Program Index fingerprint so later joins never resolve by
/// method text alone. Evidence is non-empty and certainty is explicit and never exceeds the strongest
/// evidence contributor.
/// </summary>
public sealed record PredicateSemanticFact
{
    public PredicateSemanticFact(
        SemanticFactId id,
        MethodId method,
        OperationId sourceConditionOperation,
        PredicateExpression root,
        CompilationProfileId profileId,
        string programIndexFingerprint,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        SemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceConditionOperation.Value, nameof(sourceConditionOperation));
        ArgumentNullException.ThrowIfNull(root, nameof(root));

        if (string.IsNullOrWhiteSpace(profileId.Value))
        {
            throw new ArgumentException(
                "A predicate semantic fact requires a non-blank compilation profile ID.",
                nameof(profileId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));
        Id = id;
        Method = method;
        SourceConditionOperation = sourceConditionOperation;
        Root = root;
        ProfileId = profileId;
        ProgramIndexFingerprint = programIndexFingerprint;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    /// <summary>Exact compiler operation identifying the complete source condition.</summary>
    public OperationId SourceConditionOperation { get; }

    public PredicateExpression Root { get; }

    public CompilationProfileId ProfileId { get; }

    /// <summary>Program Index fingerprint that scopes the fact; later joins must honor it exactly.</summary>
    public string ProgramIndexFingerprint { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>Exact mapping from one source predicate to its lowered Method Flow condition operations.</summary>
public sealed record PredicateDecisionMappingFact
{
    public PredicateDecisionMappingFact(
        SemanticFactId id,
        SemanticFactId predicateId,
        MethodId method,
        ImmutableArray<OperationId> loweredConditionOperations,
        CompilationProfileId profileId,
        string programIndexFingerprint,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value, nameof(method));
        SemanticFactContracts.Validate(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(predicateId.Value, nameof(predicateId));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId.Value, nameof(profileId));
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));
        if (loweredConditionOperations.IsDefaultOrEmpty
            || loweredConditionOperations.Any(operation => string.IsNullOrWhiteSpace(operation.Value))
            || loweredConditionOperations.Distinct().Count() != loweredConditionOperations.Length
            || !loweredConditionOperations.SequenceEqual(loweredConditionOperations.OrderBy(operation => operation.Value, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Mapping operations must be non-empty, distinct, and ordinally ordered.", nameof(loweredConditionOperations));
        }

        Id = id;
        PredicateId = predicateId;
        Method = method;
        LoweredConditionOperations = loweredConditionOperations;
        ProfileId = profileId;
        ProgramIndexFingerprint = programIndexFingerprint;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }
    public SemanticFactId PredicateId { get; }
    public MethodId Method { get; }
    public ImmutableArray<OperationId> LoweredConditionOperations { get; }
    public CompilationProfileId ProfileId { get; }
    public string ProgramIndexFingerprint { get; }
    public ImmutableArray<EvidenceRef> Evidence { get; }
    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of canonical typed normalized predicate facts for one compilation
/// profile. The set records schema and producer versions, the compilation profile, the Program Index
/// fingerprint, facts in deterministic order, diagnostics, and a deterministic debug representation.
/// Construction enforces the impossible-state invariants: schema version exactly 1, a non-blank
/// producer and fingerprint, a non-null profile, initialized (never default) fact and diagnostic
/// collections, and non-blank debug text. Persistence and cache reconstruction are explicitly out of
/// scope for this contract.
/// </summary>
public sealed class PredicateSemanticFactSet
{
    public PredicateSemanticFactSet(
        int SchemaVersion,
        string ProducerVersion,
        CompilationProfile Profile,
        string ProgramIndexFingerprint,
        ImmutableArray<PredicateSemanticFact> Predicates,
        ImmutableArray<AnalysisDiagnostic> Diagnostics,
        string DebugProjection)
        : this(SchemaVersion, ProducerVersion, Profile, ProgramIndexFingerprint, Predicates, [], Diagnostics, DebugProjection)
    {
    }

    public PredicateSemanticFactSet(
        int SchemaVersion,
        string ProducerVersion,
        CompilationProfile Profile,
        string ProgramIndexFingerprint,
        ImmutableArray<PredicateSemanticFact> Predicates,
        ImmutableArray<PredicateDecisionMappingFact> Mappings,
        ImmutableArray<AnalysisDiagnostic> Diagnostics,
        string DebugProjection)
    {
        if (SchemaVersion != 1)
        {
            throw new ArgumentException("The predicate fact set schema version must be exactly 1.", nameof(SchemaVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProducerVersion, nameof(ProducerVersion));
        if (Profile is null)
        {
            throw new ArgumentException("The predicate fact set requires a non-null compilation profile.", nameof(Profile));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProgramIndexFingerprint, nameof(ProgramIndexFingerprint));
        ArgumentException.ThrowIfNullOrWhiteSpace(DebugProjection, nameof(DebugProjection));
        if (Predicates.IsDefault || Mappings.IsDefault || Diagnostics.IsDefault)
        {
            throw new ArgumentException(
                "The predicate fact set facts and diagnostics must be initialized.",
                nameof(Predicates));
        }

        if (!IsOrdinalUnique(Predicates, predicate => predicate.Id.Value)
            || Predicates.Any(predicate => predicate.ProfileId != Profile.Id
                || predicate.ProgramIndexFingerprint != ProgramIndexFingerprint))
        {
            throw new ArgumentException("Predicates must be unique, ordinally ordered, and scoped to the set.", nameof(Predicates));
        }

        if (!IsOrdinalUnique(Mappings, mapping => mapping.Id.Value))
        {
            throw new ArgumentException("Mappings must be unique and ordinally ordered by ID.", nameof(Mappings));
        }

        var predicateIds = Predicates.Select(predicate => predicate.Id).ToHashSet();
        var mappedPredicates = new HashSet<SemanticFactId>();
        var mappedOperations = new HashSet<OperationId>();
        foreach (var mapping in Mappings)
        {
            if (!predicateIds.Contains(mapping.PredicateId)
                || !mappedPredicates.Add(mapping.PredicateId)
                || !Predicates.First(predicate => predicate.Id == mapping.PredicateId).Method.Equals(mapping.Method)
                || mapping.ProfileId != Profile.Id
                || mapping.ProgramIndexFingerprint != ProgramIndexFingerprint
                || mapping.LoweredConditionOperations.Any(operation => !mappedOperations.Add(operation)))
            {
                throw new ArgumentException("Mappings must reference matching predicates and uniquely own lowered operations.", nameof(Mappings));
            }
        }

        this.SchemaVersion = SchemaVersion;
        this.ProducerVersion = ProducerVersion;
        this.Profile = Profile;
        this.ProgramIndexFingerprint = ProgramIndexFingerprint;
        this.Predicates = Predicates;
        this.Mappings = Mappings;
        this.Diagnostics = Diagnostics;
        this.DebugProjection = DebugProjection;
    }

    public int SchemaVersion { get; }

    public string ProducerVersion { get; }

    public CompilationProfile Profile { get; }

    public string ProgramIndexFingerprint { get; }

    /// <summary>Gets the predicate facts in deterministic producer order.</summary>
    public ImmutableArray<PredicateSemanticFact> Predicates { get; }

    public ImmutableArray<PredicateDecisionMappingFact> Mappings { get; }

    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    public string DebugProjection { get; }

    private static bool IsOrdinalUnique<T>(ImmutableArray<T> items, Func<T, string> key)
        => items.Select(key).SequenceEqual(items.Select(key).Order(StringComparer.Ordinal))
            && items.Select(key).Distinct(StringComparer.Ordinal).Count() == items.Length;
}
