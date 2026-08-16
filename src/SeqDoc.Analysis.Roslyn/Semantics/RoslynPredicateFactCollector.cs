using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Analysis.Roslyn.Semantics;

/// <summary>
/// Accumulates CR-1 typed normalized predicate fact drafts during one Roslyn compilation/extraction
/// session and builds the Roslyn-neutral, memory-only <see cref="PredicateSemanticFactSet"/>. The
/// collector is called from the accepted CFG traversal exactly where a material decision block exposes
/// both a conditional successor and its branch value, so every fact anchors the exact compiler
/// condition operation of the accepted behavior input and never adds or reinterprets Method Flow
/// edges or fingerprints. The projection is deliberately narrow: implicit conversions unwrap; null
/// checks, relational/binary-or/negated patterns, built-in comparisons/logical/arithmetic binaries,
/// explicit structural negation, literals/constants including enum fields, stable symbol operands,
/// and the stable <c>DateTime.UtcNow</c> opaque property are admitted. User-defined/lifted/dynamic
/// operators, side-effecting assignments/increments, invocations, and interpolations fail closed with
/// a stable PRED001 diagnostic and no fact. Fact identity and detail come from a canonical tree
/// serializer (never source spelling or paths), facts are deterministically ordered by identity, and
/// duplicate drafts that conflict on evidence produce a stable PRED002 diagnostic.
/// </summary>
internal sealed class RoslynPredicateFactCollector
{
    private const string ProducerVersion = "0.1.0-alpha";
    private const string FactKind = "predicate-expression";
    private const string UnsupportedDiagnosticCode = "PRED001";
    private const string ConflictDiagnosticCode = "PRED002";

    private readonly List<PredicateDraft> _drafts = [];

    /// <summary>Receives one canonical source condition and its complete, compiler-bound mapping.</summary>
    public void Add(
        MethodId method,
        OperationId sourceConditionOperation,
        IOperation root,
        ImmutableArray<EvidenceRef> evidence,
        ImmutableArray<OperationId> loweredConditionOperations,
        bool mappingAmbiguous = false) =>
        _drafts.Add(new PredicateDraft(
            method,
            sourceConditionOperation,
            root,
            evidence,
            loweredConditionOperations,
            mappingAmbiguous));

    public PredicateSemanticFactSet Build(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));

        var projected = new List<PredicateSemanticFact>();
        var newDiagnostics = new List<AnalysisDiagnostic>();
        foreach (var draft in _drafts)
        {
            var root = Project(draft.Root, isRoot: true);
            if (root is null)
            {
                newDiagnostics.Add(CreateUnsupportedDiagnostic(profile, draft));
                continue;
            }

            var canonical = BuildCanonical(root);
            var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
                profile.Id,
                FactKind,
                draft.Method,
                draft.ConditionOperation,
                canonical));
            var fact = new PredicateSemanticFact(
                id,
                draft.Method,
                draft.ConditionOperation,
                root,
                profile.Id,
                programIndexFingerprint,
                draft.Evidence,
                CertaintyLevel.Exact);
            projected.Add(fact);
            if (!draft.MappingAmbiguous && !draft.LoweredConditionOperations.IsDefaultOrEmpty)
            {
                var lowered = draft.LoweredConditionOperations
                    .Distinct()
                    .OrderBy(operation => operation.Value, StringComparer.Ordinal)
                    .ToImmutableArray();
                if (lowered.Length == draft.LoweredConditionOperations.Length)
                {
                    var mappingId = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
                        profile.Id,
                        "predicate-decision-mapping",
                        draft.Method,
                        draft.ConditionOperation,
                        string.Join("|", lowered.Select(operation => operation.Value))));
                    _mappings.Add(new PredicateDecisionMappingFact(
                        mappingId,
                        id,
                        draft.Method,
                        lowered,
                        profile.Id,
                        programIndexFingerprint,
                        draft.Evidence,
                        CertaintyLevel.Exact));
                }
            }
            else if (draft.MappingAmbiguous || draft.LoweredConditionOperations.IsDefaultOrEmpty)
            {
                newDiagnostics.Add(CreateMappingDiagnostic(profile, draft));
            }
        }

        var facts = ProjectAndDeDuplicate(projected, profile, newDiagnostics);
        var mappings = ProjectAndDeDuplicateMappings(_mappings, profile, newDiagnostics);
        var allDiagnostics = diagnostics.AddRange(newDiagnostics);
        var debugProjection = BuildDebugProjection(profile, programIndexFingerprint, facts, mappings, allDiagnostics.Length);

        return new PredicateSemanticFactSet(
            1,
            ProducerVersion,
            profile,
            programIndexFingerprint,
            facts,
            mappings,
            allDiagnostics,
            debugProjection);
    }

    private readonly List<PredicateDecisionMappingFact> _mappings = [];

    /// <summary>
    /// Orders facts deterministically by identity and collapses duplicate drafts. Identical drafts
    /// (same identity and evidence) produce exactly one fact; a same-identity group whose evidence
    /// differs is a genuine conflict and emits a stable PRED002 diagnostic while keeping the first
    /// deterministic draft. The identity already carries the canonical projected tree, so this check
    /// never silently selects between different predicate shapes.
    /// </summary>
    private static ImmutableArray<PredicateSemanticFact> ProjectAndDeDuplicate(
        List<PredicateSemanticFact> projected,
        CompilationProfile profile,
        List<AnalysisDiagnostic> diagnostics)
    {
        var result = new List<PredicateSemanticFact>();
        foreach (var group in projected
                     .GroupBy(fact => fact.Id.Value, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = group
                .OrderBy(fact => EvidenceKey(fact.Evidence), StringComparer.Ordinal)
                .ThenBy(fact => fact.SourceConditionOperation.Value, StringComparer.Ordinal)
                .ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (!ordered[index].Evidence.SequenceEqual(ordered[0].Evidence))
                {
                    diagnostics.Add(CreateConflictDiagnostic(profile, ordered[0].Method, ordered[0].SourceConditionOperation));
                    break;
                }
            }

            result.Add(ordered[0]);
        }

        return result.ToImmutableArray();
    }

    private static ImmutableArray<PredicateDecisionMappingFact> ProjectAndDeDuplicateMappings(
        List<PredicateDecisionMappingFact> projected,
        CompilationProfile profile,
        List<AnalysisDiagnostic> diagnostics)
    {
        var result = new List<PredicateDecisionMappingFact>();
        foreach (var group in projected
                     .GroupBy(mapping => mapping.Id.Value, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = group
                .OrderBy(mapping => EvidenceKey(mapping.Evidence), StringComparer.Ordinal)
                .ThenBy(mapping => string.Join("|", mapping.LoweredConditionOperations.Select(operation => operation.Value)), StringComparer.Ordinal)
                .ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (!ordered[index].Evidence.SequenceEqual(ordered[0].Evidence))
                {
                    diagnostics.Add(CreateConflictDiagnostic(profile, ordered[0].Method, new OperationId(ordered[0].PredicateId.Value)));
                    break;
                }
            }

            result.Add(ordered[0]);
        }

        return result.ToImmutableArray();
    }

    private static string EvidenceKey(ImmutableArray<EvidenceRef> evidence)
        => string.Join("|", evidence
            .Select(item => item.Id.Value)
            .Order(StringComparer.Ordinal));

    private static PredicateExpression? Project(IOperation operation, bool isRoot)
    {
        operation = Unwrap(operation);
        return operation switch
        {
            IBinaryOperation binary => ProjectBinary(binary),
            IUnaryOperation unary => ProjectUnary(unary),
            IIsPatternOperation isPattern => ProjectPattern(isPattern),
            IParameterReferenceOperation parameter => ProjectSymbol(parameter.Parameter.Name, parameter.Type, isRoot),
            ILocalReferenceOperation local => ProjectSymbol(local.Local.Name, local.Type, isRoot),
            IFieldReferenceOperation field => ProjectField(field, isRoot),
            IPropertyReferenceOperation property => ProjectProperty(property, isRoot),
            ILiteralOperation literal => ProjectLiteral(literal),
            _ => null,
        };
    }

    private static PredicateExpression? ProjectBinary(IBinaryOperation binary)
    {
        if (IsDynamic(binary.LeftOperand.Type) || IsDynamic(binary.RightOperand.Type)
            || binary.IsLifted || (binary.OperatorMethod is not null && !IsAdmittedOperator(binary)))
        {
            return null;
        }

        switch (binary.OperatorKind)
        {
            case BinaryOperatorKind.Equals:
            case BinaryOperatorKind.NotEquals:
            case BinaryOperatorKind.LessThan:
            case BinaryOperatorKind.LessThanOrEqual:
            case BinaryOperatorKind.GreaterThan:
            case BinaryOperatorKind.GreaterThanOrEqual:
                var comparisonOperator = MapComparisonOperator(binary.OperatorKind);
                var comparisonLeft = ProjectOperand(binary.LeftOperand);
                var comparisonRight = ProjectOperand(binary.RightOperand);
                if (comparisonLeft is null || comparisonRight is null || comparisonOperator is null)
                {
                    return null;
                }

                return new PredicateExpression(
                    PredicateExpressionKind.Comparison,
                    [comparisonLeft, comparisonRight],
                    "System.Boolean",
                    comparisonOperator: comparisonOperator);
            case BinaryOperatorKind.And:
            case BinaryOperatorKind.Or:
            case BinaryOperatorKind.ConditionalAnd:
            case BinaryOperatorKind.ConditionalOr:
                // Short-circuit && / || conditions are lowered by the compiler into separate decision
                // blocks, so a logical binary only reaches this projection when the compiler keeps the
                // whole boolean operation as one branch value. Bitwise & / | on non-boolean types is
                // outside the admitted arithmetic vocabulary and fails closed.
                if (binary.Type?.SpecialType != SpecialType.System_Boolean)
                {
                    return null;
                }

                var logicalLeft = Project(binary.LeftOperand, isRoot: false);
                var logicalRight = Project(binary.RightOperand, isRoot: false);
                if (logicalLeft is null || logicalRight is null)
                {
                    return null;
                }

                return new PredicateExpression(
                        binary.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.ConditionalAnd
                        ? PredicateExpressionKind.LogicalAnd
                        : PredicateExpressionKind.LogicalOr,
                    [logicalLeft, logicalRight],
                    "System.Boolean");
            case BinaryOperatorKind.Add:
            case BinaryOperatorKind.Subtract:
            case BinaryOperatorKind.Multiply:
            case BinaryOperatorKind.Divide:
            case BinaryOperatorKind.Remainder:
                var arithmeticOperator = MapArithmeticOperator(binary.OperatorKind);
                var arithmeticLeft = Project(binary.LeftOperand, isRoot: false);
                var arithmeticRight = Project(binary.RightOperand, isRoot: false);
                if (arithmeticLeft is null || arithmeticRight is null
                    || arithmeticOperator is null
                    || !IsBuiltInNumericType(binary.Type)
                    || !TryTypeName(binary.Type, out var arithmeticType))
                {
                    return null;
                }

                return new PredicateExpression(
                    PredicateExpressionKind.BinaryArithmetic,
                    [arithmeticLeft, arithmeticRight],
                    arithmeticType,
                    arithmeticOperator: arithmeticOperator);
            default:
                return null;
        }
    }

    private static PredicateExpression? ProjectOperand(IOperation operation)
    {
        // Roslyn represents a null operand in a comparison as an implicit conversion whose
        // contextual type is the useful type. The literal itself has no type to preserve.
        if (TryGetNullContext(operation, out var nullType))
        {
            return ProjectNullConstant(nullType);
        }

        return Project(operation, isRoot: false);
    }

    private static PredicateExpression? ProjectUnary(IUnaryOperation unary)
    {
        if (unary.OperatorKind != UnaryOperatorKind.Not
            || (unary.OperatorMethod is not null && !IsAdmittedUnaryOperator(unary))
            || unary.IsLifted)
        {
            return null;
        }

        var operand = Project(unary.Operand, isRoot: false);
        if (operand is null)
        {
            return null;
        }

        return new PredicateExpression(PredicateExpressionKind.Negation, [operand], "System.Boolean");
    }

    private static PredicateExpression? ProjectPattern(IIsPatternOperation isPattern)
    {
        if (isPattern.Pattern is null)
        {
            return null;
        }

        return ProjectPatternOperation(isPattern, isPattern.Pattern);
    }

    private static PredicateExpression? ProjectPatternOperation(IIsPatternOperation isPattern, IPatternOperation pattern)
    {
        switch (pattern)
        {
            case IConstantPatternOperation constantPattern
                when IsNullLiteral(constantPattern.Value):
                var testedValue = Project(isPattern.Value, isRoot: false);
                var nullConstant = ProjectNullConstant(isPattern.Value?.Type);
                return testedValue is null || nullConstant is null
                    ? null
                    : new PredicateExpression(
                        PredicateExpressionKind.Comparison,
                        [testedValue, nullConstant],
                        "System.Boolean",
                        comparisonOperator: PredicateComparisonOperatorKind.Equal);
            case IRelationalPatternOperation relational:
                var input = Project(isPattern.Value, isRoot: false);
                var threshold = Project(relational.Value, isRoot: false);
                var relationalOperator = MapComparisonOperator(relational.OperatorKind);
                if (input is null || threshold is null || relationalOperator is null)
                {
                    return null;
                }

                return new PredicateExpression(
                    PredicateExpressionKind.Comparison,
                    [input, threshold],
                    "System.Boolean",
                    comparisonOperator: relationalOperator);
            case IBinaryPatternOperation binaryPattern:
                var left = ProjectPatternOperation(isPattern, binaryPattern.LeftPattern);
                var right = ProjectPatternOperation(isPattern, binaryPattern.RightPattern);
                if (left is null || right is null)
                {
                    return null;
                }

                return new PredicateExpression(
                    binaryPattern.OperatorKind == BinaryOperatorKind.And
                        ? PredicateExpressionKind.LogicalAnd
                        : PredicateExpressionKind.LogicalOr,
                    [left, right],
                    "System.Boolean");
            case INegatedPatternOperation negated:
                var inner = ProjectPatternOperation(isPattern, negated.Pattern);
                if (inner is null)
                {
                    return null;
                }

                return new PredicateExpression(PredicateExpressionKind.Negation, [inner], "System.Boolean");
            default:
                return null;
        }
    }

    private static PredicateExpression? ProjectNullConstant(ITypeSymbol? type)
    {
        if (!TryTypeName(type, out var typeName))
        {
            return null;
        }

        return new PredicateExpression(PredicateExpressionKind.NullConstant, [], typeName);
    }

    /// <summary>
    /// Projects a stable symbol operand. A boolean parameter/local/property/field that is the root of
    /// the material decision is wrapped in <see cref="PredicateExpressionKind.BooleanTruth"/>; the
    /// same symbol as an inner operand stays a plain symbol value so no node ever invents structure.
    /// </summary>
    private static PredicateExpression? ProjectSymbol(string name, ITypeSymbol? type, bool isRoot)
    {
        if (string.IsNullOrWhiteSpace(name) || !TryTypeName(type, out var typeName))
        {
            return null;
        }

        var symbol = new PredicateExpression(PredicateExpressionKind.SymbolValue, [], typeName, displayName: name);
        if (isRoot && type?.SpecialType == SpecialType.System_Boolean)
        {
            return new PredicateExpression(PredicateExpressionKind.BooleanTruth, [symbol], "System.Boolean");
        }

        return symbol;
    }

    private static PredicateExpression? ProjectProperty(IPropertyReferenceOperation property, bool isRoot)
    {
        if (property.Property is { } propertySymbol && IsDateTimeUtcNow(propertySymbol))
        {
            // The stable special property (current UTC time) is a typed opaque operand; its value is
            // never evaluated. The display identity matches the canonical Core contract shape.
            return new PredicateExpression(
                PredicateExpressionKind.OpaqueValue,
                [],
                "System.DateTime",
                displayName: "DateTime.UtcNow");
        }

        // UtcNow is the sole admitted property. Ordinary getters are executable behavior, not stable operands.
        return null;
    }

    private static PredicateExpression? ProjectField(IFieldReferenceOperation field, bool isRoot)
    {
        if (field.Field is null)
        {
            return null;
        }

        if (field.Field.IsConst && field.ConstantValue is { HasValue: true } constant)
        {
            if (field.Field.ContainingType?.TypeKind == TypeKind.Enum)
            {
                if (!TryTypeName(field.Field.ContainingType, out var enumTypeName))
                {
                    return null;
                }

                return new PredicateExpression(
                    PredicateExpressionKind.EnumConstant,
                    [],
                    enumTypeName,
                    constantValue: field.Field.Name);
            }

            return ProjectConstantValue(constant.Value, field.Field.Type);
        }

        return TryGetReceiverPath(field.Instance, out var receiver)
            ? ProjectSymbol($"{receiver}.{field.Field.Name}", field.Type, isRoot)
            : field.Instance is null
                ? ProjectSymbol(MemberDisplayName(field.Field.ContainingType, field.Field.Name), field.Type, isRoot)
                : null;
    }

    private static PredicateExpression? ProjectLiteral(ILiteralOperation literal)
    {
        if (literal.ConstantValue is not { HasValue: true } constant)
        {
            return null;
        }

        return ProjectConstantValue(constant.Value, literal.Type);
    }

    private static PredicateExpression? ProjectConstantValue(object? value, ITypeSymbol? type)
    {
        if (value is null)
        {
            return ProjectNullConstant(type);
        }

        switch (value)
        {
            case bool boolean:
                return new PredicateExpression(
                    PredicateExpressionKind.BooleanConstant,
                    [],
                    "System.Boolean",
                    constantValue: boolean ? "true" : "false");
            case string text:
                return new PredicateExpression(
                    PredicateExpressionKind.StringConstant,
                    [],
                    TypeNameOrDefault(type, "System.String"),
                    constantValue: text);
            case char character:
                return new PredicateExpression(
                    PredicateExpressionKind.CharacterConstant,
                    [],
                    "System.Char",
                    constantValue: character.ToString());
            case double floating when !double.IsFinite(floating):
                return null;
            case float single when !float.IsFinite(single):
                return null;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                if (!TryTypeName(type, out var numericTypeName))
                {
                    return null;
                }

                return new PredicateExpression(
                    PredicateExpressionKind.NumericConstant,
                    [],
                    numericTypeName,
                    constantValue: Convert.ToString(value, CultureInfo.InvariantCulture));
            default:
                return null;
        }
    }

    private static bool IsDateTimeUtcNow(IPropertySymbol property)
        => string.Equals(property.Name, "UtcNow", StringComparison.Ordinal)
            && string.Equals(
                property.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
                "System.DateTime",
                StringComparison.Ordinal);

    private static bool IsAdmittedOperator(IBinaryOperation binary)
    {
        if (binary.OperatorMethod is null)
        {
            return IsBuiltInStringEquality(binary)
                || IsBuiltInValueTypeComparison(binary)
                || ((binary.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or
                    or BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
                    && binary.Type?.SpecialType == SpecialType.System_Boolean)
                || ((binary.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract
                    or BinaryOperatorKind.Multiply or BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder)
                    && IsBuiltInNumericType(binary.Type));
        }

        if (binary.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or
            or BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
        {
            return binary.Type?.SpecialType == SpecialType.System_Boolean
                && binary.OperatorMethod.ContainingType?.SpecialType == SpecialType.System_Boolean;
        }

        if (binary.OperatorKind is BinaryOperatorKind.Add
            or BinaryOperatorKind.Subtract
            or BinaryOperatorKind.Multiply
            or BinaryOperatorKind.Divide
            or BinaryOperatorKind.Remainder)
        {
            return binary.OperatorMethod.ContainingType?.SpecialType is not null
                && binary.OperatorMethod.ContainingType.SpecialType != SpecialType.None;
        }

        if (binary.OperatorKind is not (BinaryOperatorKind.Equals
            or BinaryOperatorKind.NotEquals
            or BinaryOperatorKind.LessThan
            or BinaryOperatorKind.LessThanOrEqual
            or BinaryOperatorKind.GreaterThan
            or BinaryOperatorKind.GreaterThanOrEqual)
            || binary.Type?.SpecialType != SpecialType.System_Boolean
            || binary.OperatorMethod is not IMethodSymbol method)
        {
            return false;
        }

        // Null equality is compiler-proven even when Roslyn exposes the string equality
        // operator symbol. The null operand remains typed by its contextual conversion.
        if (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            && (IsNullOperation(binary.LeftOperand) || IsNullOperation(binary.RightOperand)))
        {
            return true;
        }

        if (binary.OperatorMethod is IMethodSymbol builtInMethod
            && IsBuiltInNumericType(builtInMethod.ContainingType)
            && builtInMethod.Parameters.Length == 2
            && builtInMethod.Parameters.All(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, builtInMethod.ContainingType))
            && builtInMethod.ReturnType.SpecialType == SpecialType.System_Boolean)
        {
            return true;
        }

        if (string.Equals(method.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal)
            && string.Equals(method.Name, binary.OperatorKind == BinaryOperatorKind.Equals ? "op_Equality" : "op_Inequality", StringComparison.Ordinal)
            && method.Parameters.Length == 2
            && method.Parameters.All(parameter => string.Equals(parameter.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal))
            && string.Equals(method.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.Boolean", StringComparison.Ordinal))
        {
            return string.Equals(binary.LeftOperand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal)
                && string.Equals(binary.RightOperand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal);
        }

        if (binary.OperatorMethod is IMethodSymbol stringMethod
            && binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            && string.Equals(stringMethod.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal)
            && string.Equals(stringMethod.Name, binary.OperatorKind == BinaryOperatorKind.Equals ? "op_Equality" : "op_Inequality", StringComparison.Ordinal)
            && stringMethod.Parameters.Length == 2
            && stringMethod.Parameters.All(parameter => string.Equals(parameter.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal))
            && string.Equals(stringMethod.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.Boolean", StringComparison.Ordinal))
        {
            return string.Equals(binary.LeftOperand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal)
                && string.Equals(binary.RightOperand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal);
        }

        if (!string.Equals(method.Name, binary.OperatorKind switch
        {
            BinaryOperatorKind.Equals => "op_Equality",
            BinaryOperatorKind.NotEquals => "op_Inequality",
            BinaryOperatorKind.LessThan => "op_LessThan",
            BinaryOperatorKind.LessThanOrEqual => "op_LessThanOrEqual",
            BinaryOperatorKind.GreaterThan => "op_GreaterThan",
            BinaryOperatorKind.GreaterThanOrEqual => "op_GreaterThanOrEqual",
            _ => string.Empty,
        }, StringComparison.Ordinal)
            || !string.Equals(method.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.DateTime", StringComparison.Ordinal)
            || method.Parameters.Length != 2
            || method.Parameters.Any(parameter => !string.Equals(parameter.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.DateTime", StringComparison.Ordinal))
            || !string.Equals(method.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.Boolean", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(binary.LeftOperand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.DateTime", StringComparison.Ordinal)
            && string.Equals(binary.RightOperand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.DateTime", StringComparison.Ordinal);
    }

    private static bool IsBuiltInStringEquality(IBinaryOperation binary)
        => binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            && binary.Type?.SpecialType == SpecialType.System_Boolean
            && string.Equals(binary.LeftOperand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal)
            && string.Equals(binary.RightOperand.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal)
            && (binary.OperatorMethod is null || IsStringEqualityMethod(binary.OperatorMethod, binary.OperatorKind));

    private static bool IsStringEqualityMethod(IMethodSymbol method, BinaryOperatorKind kind)
        => string.Equals(method.Name, kind == BinaryOperatorKind.Equals ? "op_Equality" : "op_Inequality", StringComparison.Ordinal)
            && string.Equals(method.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal)
            && method.Parameters.Length == 2
            && method.Parameters.All(parameter => string.Equals(parameter.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.String", StringComparison.Ordinal))
            && string.Equals(method.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.Boolean", StringComparison.Ordinal);

    private static bool IsBuiltInValueTypeComparison(IBinaryOperation binary)
        => (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals
            or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual
            or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual)
            && binary.Type?.SpecialType == SpecialType.System_Boolean
            && binary.LeftOperand.Type is { } left
            && SymbolEqualityComparer.Default.Equals(left, binary.RightOperand.Type)
            && (IsBuiltInNumericType(left) || left.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char || left.TypeKind == TypeKind.Enum);

    private static bool IsNullOperation(IOperation operation)
        => IsNullLiteral(operation);

    private static bool IsNullLiteral(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        if (operation is IParenthesizedOperation parenthesized)
        {
            return IsNullLiteral(parenthesized.Operand);
        }

        return operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };
    }

    private static bool TryGetNullContext(IOperation operation, out ITypeSymbol? contextualType)
    {
        if (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            if (IsNullLiteral(conversion.Operand))
            {
                contextualType = conversion.Type;
                return true;
            }

            if (TryGetNullContext(conversion.Operand, out contextualType))
            {
                return true;
            }
        }

        if (operation is IParenthesizedOperation parenthesized
            && TryGetNullContext(parenthesized.Operand, out contextualType))
        {
            return true;
        }

        if (IsNullLiteral(operation))
        {
            contextualType = operation.Type;
            return true;
        }

        contextualType = null;
        return false;
    }

    private static bool IsAdmittedUnaryOperator(IUnaryOperation unary)
        => unary.OperatorMethod is IMethodSymbol method
            && string.Equals(method.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.Boolean", StringComparison.Ordinal)
            && string.Equals(method.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.Boolean", StringComparison.Ordinal)
            && method.Parameters.Length == 1
            && string.Equals(method.Parameters[0].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), "System.Boolean", StringComparison.Ordinal)
            && string.Equals(method.Name, "op_LogicalNot", StringComparison.Ordinal);

    private static bool IsDynamic(ITypeSymbol? type) => type?.TypeKind == TypeKind.Dynamic;

    private static string MemberDisplayName(INamedTypeSymbol? containingType, string memberName)
        => $"{containingType?.Name ?? "<unknown>"}.{memberName}";

    /// <summary>
    /// Builds an identity from compiler-bound receiver operations only. Invocation, indexer, dynamic,
    /// conditional-access, and other computed receivers are rejected rather than reconstructed from
    /// syntax, because their apparent member path could hide evaluation or side effects.
    /// </summary>
    private static bool TryGetReceiverPath(IOperation? receiver, out string path)
    {
        receiver = receiver is null ? null : Unwrap(receiver);
        switch (receiver)
        {
            case IParameterReferenceOperation parameter:
                path = parameter.Parameter.Name;
                return true;
            case ILocalReferenceOperation local:
                path = local.Local.Name;
                return true;
            case IInstanceReferenceOperation instance when instance.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance:
                path = instance.Syntax is BaseExpressionSyntax ? "base" : "this";
                return true;
            case IFieldReferenceOperation field when !field.Field.IsConst && TryGetReceiverPath(field.Instance, out var fieldReceiver):
                path = $"{fieldReceiver}.{field.Field.Name}";
                return true;
            default:
                path = string.Empty;
                return false;
        }
    }

    private static bool IsBuiltInNumericType(ITypeSymbol? type)
        => type?.SpecialType is SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal;

    private static string TypeNameOrDefault(ITypeSymbol? type, string fallback)
        => TryTypeName(type, out var typeName) ? typeName : fallback;

    private static bool TryTypeName(ITypeSymbol? type, out string typeName)
    {
        if (type is null)
        {
            typeName = string.Empty;
            return false;
        }

        typeName = type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        return !string.IsNullOrWhiteSpace(typeName);
    }

    /// <summary>
    /// Unwraps the implicit conversions and parenthesized wrappers the compiler places around branch
    /// values and pattern operands. Explicit conversions are deliberate value-changing operations and
    /// are never unwrapped; they fail closed as unsupported.
    /// </summary>
    private static IOperation Unwrap(IOperation operation)
    {
        IOperation current = operation;
        while (true)
        {
            if (current is IConversionOperation { IsImplicit: true } conversion)
            {
                current = conversion.Operand;
                continue;
            }

            if (current is IParenthesizedOperation parenthesized)
            {
                current = parenthesized.Operand;
                continue;
            }

            return current;
        }
    }

    private static PredicateComparisonOperatorKind? MapComparisonOperator(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.Equals => PredicateComparisonOperatorKind.Equal,
        BinaryOperatorKind.NotEquals => PredicateComparisonOperatorKind.NotEqual,
        BinaryOperatorKind.LessThan => PredicateComparisonOperatorKind.LessThan,
        BinaryOperatorKind.LessThanOrEqual => PredicateComparisonOperatorKind.LessThanOrEqual,
        BinaryOperatorKind.GreaterThan => PredicateComparisonOperatorKind.GreaterThan,
        BinaryOperatorKind.GreaterThanOrEqual => PredicateComparisonOperatorKind.GreaterThanOrEqual,
        _ => null,
    };

    private static PredicateArithmeticOperatorKind? MapArithmeticOperator(BinaryOperatorKind kind) => kind switch
    {
        BinaryOperatorKind.Add => PredicateArithmeticOperatorKind.Add,
        BinaryOperatorKind.Subtract => PredicateArithmeticOperatorKind.Subtract,
        BinaryOperatorKind.Multiply => PredicateArithmeticOperatorKind.Multiply,
        BinaryOperatorKind.Divide => PredicateArithmeticOperatorKind.Divide,
        BinaryOperatorKind.Remainder => PredicateArithmeticOperatorKind.Remainder,
        _ => null,
    };

    /// <summary>
    /// Serializes one projected predicate tree into a canonical, deterministic, source-independent
    /// text. The text carries only stable kinds, type names, operator kinds, display identities, and
    /// constant values; it never carries source spelling, spans, or checkout paths, so the fact
    /// identity and the set debug representation never depend on the working copy layout.
    /// </summary>
    private static string BuildCanonical(PredicateExpression node)
    {
        switch (node.Kind)
        {
            case PredicateExpressionKind.NullConstant:
                return $"null({Token(node.TypeName)})";
            case PredicateExpressionKind.BooleanConstant:
                return $"bool({Token(node.TypeName)},{Token(node.ConstantValue!)})";
            case PredicateExpressionKind.NumericConstant:
                return $"num({Token(node.TypeName)},{Token(node.ConstantValue!)})";
            case PredicateExpressionKind.StringConstant:
                return $"str({Token(node.TypeName)},{Token(node.ConstantValue!)})";
            case PredicateExpressionKind.CharacterConstant:
                return $"chr({Token(node.TypeName)},{Token(node.ConstantValue!)})";
            case PredicateExpressionKind.EnumConstant:
                return $"enum({Token(node.TypeName)},{Token(node.ConstantValue!)})";
            case PredicateExpressionKind.SymbolValue:
                return $"sym({Token(node.TypeName)},{Token(node.DisplayName!)})";
            case PredicateExpressionKind.OpaqueValue:
                return $"opaque({Token(node.TypeName)},{Token(node.DisplayName!)})";
            case PredicateExpressionKind.BooleanTruth:
                return $"truth({Token(node.TypeName)},{BuildCanonical(node.Children[0])})";
            case PredicateExpressionKind.Negation:
                return $"neg({Token(node.TypeName)},{BuildCanonical(node.Children[0])})";
            case PredicateExpressionKind.Comparison:
                return $"cmp({Token(node.TypeName)},{Token(node.ComparisonOperator!.Value.ToString())},{BuildCanonical(node.Children[0])},{BuildCanonical(node.Children[1])})";
            case PredicateExpressionKind.LogicalAnd:
                return $"and({Token(node.TypeName)},{BuildCanonical(node.Children[0])},{BuildCanonical(node.Children[1])})";
            case PredicateExpressionKind.LogicalOr:
                return $"or({Token(node.TypeName)},{BuildCanonical(node.Children[0])},{BuildCanonical(node.Children[1])})";
            case PredicateExpressionKind.BinaryArithmetic:
                return $"arith({Token(node.TypeName)},{Token(node.ArithmeticOperator!.Value.ToString())},{BuildCanonical(node.Children[0])},{BuildCanonical(node.Children[1])})";
            default:
                throw new ArgumentOutOfRangeException(nameof(node), "Undefined predicate expression kind in canonical serializer.");
        }
    }

    private static string Token(string value)
        => $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}";

    private static AnalysisDiagnostic CreateUnsupportedDiagnostic(CompilationProfile profile, PredicateDraft draft)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            UnsupportedDiagnosticCode,
            AnalysisStage.BaselineIndex,
            profile.Id,
            draft.ConditionOperation.Value,
            0));
        return new AnalysisDiagnostic(
            id,
            UnsupportedDiagnosticCode,
            SeqDoc.Core.Diagnostics.DiagnosticSeverity.Warning,
            AnalysisStage.BaselineIndex,
            "A material decision condition uses an unsupported predicate shape and produced no predicate fact.",
            new DiagnosticLocation("predicate facts", profile: profile.Id, symbol: new SymbolId(draft.Method.Value)),
            "The condition operation is not within the admitted CR-1 predicate vocabulary (for example a user-defined, lifted, dynamic, side-effecting, invocation, or interpolated shape).",
            "The decision is documented at the generic branch level without an exact typed predicate tree.",
            "Keep the decision generic or extend the admitted predicate vocabulary.",
            CertaintyLevel.Exact);
    }

    private static AnalysisDiagnostic CreateConflictDiagnostic(CompilationProfile profile, MethodId method, OperationId conditionOperation)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            ConflictDiagnosticCode,
            AnalysisStage.BaselineIndex,
            profile.Id,
            conditionOperation.Value,
            1));
        return new AnalysisDiagnostic(
            id,
            ConflictDiagnosticCode,
            SeqDoc.Core.Diagnostics.DiagnosticSeverity.Warning,
            AnalysisStage.BaselineIndex,
            "Conflicting predicate fact drafts collapsed onto one identity.",
            new DiagnosticLocation("predicate facts", profile: profile.Id, symbol: new SymbolId(method.Value)),
            "Two predicate drafts projected onto the same fact identity with different evidence.",
            "Exactly one deterministic fact is retained for the condition.",
            "Inspect the extraction traversal so each material condition is collected exactly once.",
            CertaintyLevel.Exact);
    }

    private static AnalysisDiagnostic CreateMappingDiagnostic(CompilationProfile profile, PredicateDraft draft)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "PRED003", AnalysisStage.BaselineIndex, profile.Id, draft.ConditionOperation.Value, 0));
        return new AnalysisDiagnostic(id, "PRED003", SeqDoc.Core.Diagnostics.DiagnosticSeverity.Warning,
            AnalysisStage.BaselineIndex,
            "A source predicate could not be mapped exactly to lowered decision conditions.",
            new DiagnosticLocation("predicate facts", profile: profile.Id, symbol: new SymbolId(draft.Method.Value)),
            "Compiler branch ownership was empty or ambiguous.",
            "The predicate fact is retained without an exact decision mapping.",
            "Keep the decision generic until compiler ownership is unique.", CertaintyLevel.Exact);
    }

    private static string BuildDebugProjection(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<PredicateSemanticFact> facts,
        ImmutableArray<PredicateDecisionMappingFact> mappings,
        int diagnosticCount)
    {
        var builder = new StringBuilder();
        builder.Append("predicate-facts:v1").Append('\n');
        builder.Append("producer=").Append(ProducerVersion).Append('\n');
        builder.Append("profile=").Append(profile.Id.Value).Append('\n');
        builder.Append("programIndexFingerprint=").Append(programIndexFingerprint).Append('\n');
        builder.Append("diagnosticCount=").Append(diagnosticCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var fact in facts.OrderBy(fact => fact.Id.Value, StringComparer.Ordinal))
        {
            builder
                .Append("predicate ")
                .Append(fact.Id.Value)
                .Append(" method=")
                .Append(fact.Method.Value)
                .Append(" condition=")
                .Append(fact.SourceConditionOperation.Value)
                .Append(" root=")
                .Append(BuildCanonical(fact.Root))
                .Append('\n');
        }

        foreach (var mapping in mappings.OrderBy(mapping => mapping.Id.Value, StringComparer.Ordinal))
        {
            builder
                .Append("mapping ")
                .Append(mapping.Id.Value)
                .Append(" predicate=")
                .Append(mapping.PredicateId.Value)
                .Append(" method=")
                .Append(mapping.Method.Value)
                .Append(" lowered=")
                .Append(string.Join("|", mapping.LoweredConditionOperations.Select(operation => operation.Value)))
                .Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    private sealed record PredicateDraft(
        MethodId Method,
        OperationId ConditionOperation,
        IOperation Root,
        ImmutableArray<EvidenceRef> Evidence,
        ImmutableArray<OperationId> LoweredConditionOperations,
        bool MappingAmbiguous);
}
