using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Core.Tests.Semantics;

public sealed class PredicateSemanticFactTests
{
    private const string Fingerprint = "predicate-facts:v1:abc123";

    private static CompilationProfile Profile
        => CompilationProfile.Create("src/App/App.csproj", "Release", "net10.0");

    private static EvidenceRef CreateEvidence(CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(
            new EvidenceId("evidence:v1:predicate"),
            EvidenceKind.Source,
            "src/Controllers/OrdersController.cs",
            range: null,
            symbol: "OrdersController.Create",
            detail: null,
            certainty);

    private static PredicateExpression Symbol(string displayName, string typeName = "System.Boolean")
        => new(PredicateExpressionKind.SymbolValue, [], typeName, displayName: displayName);

    private static PredicateExpression NumericConstant(string value)
        => new(PredicateExpressionKind.NumericConstant, [], "System.Int32", constantValue: value);

    private static PredicateExpression Negation(PredicateExpression operand)
        => new(PredicateExpressionKind.Negation, [operand], "System.Boolean");

    private static PredicateExpression LogicalAnd(PredicateExpression left, PredicateExpression right)
        => new(PredicateExpressionKind.LogicalAnd, [left, right], "System.Boolean");

    private static PredicateExpression Comparison(
        PredicateExpression left,
        PredicateExpression right,
        PredicateComparisonOperatorKind @operator)
        => new(
            PredicateExpressionKind.Comparison,
            [left, right],
            "System.Boolean",
            comparisonOperator: @operator);

    private static PredicateSemanticFact CreateFact(
        PredicateExpression root,
        string id = "predicate-fact:v1:test",
        CompilationProfileId? profileId = null,
        string fingerprint = Fingerprint)
        => new(
            new SemanticFactId(id),
            new MethodId("method:v1:test"),
            new OperationId("source-operation:v1:test"),
            root,
            profileId ?? Profile.Id,
            fingerprint,
            [CreateEvidence()],
            CertaintyLevel.Exact);

    [Fact]
    public void PredicateExpressionEnforcesShapeAndRejectsUnrelatedFields()
    {
        // (a) Every admitted leaf family constructs with its exact shape.
        Assert.NotNull(new PredicateExpression(PredicateExpressionKind.NullConstant, [], "System.String"));
        Assert.NotNull(new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "true"));
        Assert.NotNull(new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "false"));
        Assert.NotNull(new PredicateExpression(PredicateExpressionKind.EnumConstant, [], "OrderStatus", constantValue: "Active"));
        Assert.NotNull(NumericConstant("42"));
        Assert.NotNull(new PredicateExpression(PredicateExpressionKind.StringConstant, [], "System.String", constantValue: "pending"));
        Assert.NotNull(new PredicateExpression(PredicateExpressionKind.CharacterConstant, [], "System.Char", constantValue: "a"));
        Assert.NotNull(Symbol("Order.IsPaid"));
        Assert.NotNull(new PredicateExpression(PredicateExpressionKind.OpaqueValue, [], "System.DateTime", displayName: "DateTime.UtcNow"));

        // (b) Undefined kinds and wrong arities are rejected for leaves and composites.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateExpression((PredicateExpressionKind)999, [], "System.Boolean"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.BooleanTruth, [], "System.Boolean"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.Negation,
            [NumericConstant("0"), NumericConstant("0")],
            "System.Boolean"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.LogicalAnd,
            [Symbol("Order.IsPaid")],
            "System.Boolean"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.StringConstant,
            [NumericConstant("1")],
            "System.String"));

        // (c) Operator fields are exact-kind only: comparison/arithmetic require their own defined
        // operator and must reject the other operator family; unrelated kinds reject both.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateExpression(
            PredicateExpressionKind.Comparison,
            [Symbol("Order.Quantity"), NumericConstant("0")],
            "System.Boolean"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateExpression(
            PredicateExpressionKind.Comparison,
            [Symbol("Order.Quantity"), NumericConstant("0")],
            "System.Boolean",
            comparisonOperator: (PredicateComparisonOperatorKind)999));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.Comparison,
            [Symbol("Order.Quantity"), NumericConstant("0")],
            "System.Boolean",
            comparisonOperator: PredicateComparisonOperatorKind.GreaterThan,
            arithmeticOperator: PredicateArithmeticOperatorKind.Add));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PredicateExpression(
            PredicateExpressionKind.BinaryArithmetic,
            [Symbol("Order.Quantity"), NumericConstant("1")],
            "System.Int32"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.BinaryArithmetic,
            [Symbol("Order.Quantity"), NumericConstant("1")],
            "System.Int32",
            arithmeticOperator: PredicateArithmeticOperatorKind.Add,
            comparisonOperator: PredicateComparisonOperatorKind.Equal));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.LogicalAnd,
            [Symbol("Order.IsPaid"), Symbol("Order.IsShipped")],
            "System.Boolean",
            comparisonOperator: PredicateComparisonOperatorKind.Equal));

        // (d) Type names must be non-blank and symbol/opaque operands require a display identity.
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.NullConstant, [], " "));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Boolean", displayName: " "));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.OpaqueValue, [], "System.DateTime", displayName: " "));

        // (e) Constant values must match their leaf kind.
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "yes"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.NumericConstant, [], "System.Int32", constantValue: "abc"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.CharacterConstant, [], "System.Char", constantValue: "ab"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.EnumConstant, [], "OrderStatus", constantValue: " "));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(PredicateExpressionKind.StringConstant, [], "System.String"));

        // (f) Unrelated fields are rejected: constants on symbol/opaque and composites, display names
        // on constants, and values on a null constant.
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.SymbolValue,
            [],
            "System.Boolean",
            displayName: "Order.IsPaid",
            constantValue: "true"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.NullConstant,
            [],
            "System.String",
            constantValue: "x"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.BooleanConstant,
            [],
            "System.Boolean",
            constantValue: "true",
            displayName: "Order.IsPaid"));
        Assert.Throws<ArgumentException>(() => new PredicateExpression(
            PredicateExpressionKind.LogicalAnd,
            [Symbol("Order.IsPaid"), Symbol("Order.IsShipped")],
            "System.Boolean",
            constantValue: "true"));

        // (g) A valid comparison composite retains its exact operator.
        var comparison = Comparison(Symbol("Order.Quantity", "System.Int32"), NumericConstant("0"), PredicateComparisonOperatorKind.GreaterThan);
        Assert.Equal(PredicateExpressionKind.Comparison, comparison.Kind);
        Assert.Equal(PredicateComparisonOperatorKind.GreaterThan, comparison.ComparisonOperator);
        Assert.Null(comparison.ArithmeticOperator);
        Assert.Null(comparison.DisplayName);
        Assert.Null(comparison.ConstantValue);
    }

    [Fact]
    public void PredicateSemanticFactRejectsMissingAnchorsAndWeakEvidence()
    {
        var root = Comparison(Symbol("Order.IsPaid"), new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "true"), PredicateComparisonOperatorKind.Equal);
        var fact = CreateFact(root);
        Assert.Equal(root, fact.Root);
        Assert.Equal(new OperationId("source-operation:v1:test"), fact.SourceConditionOperation);
        Assert.Equal(Profile.Id, fact.ProfileId);
        Assert.Equal(Fingerprint, fact.ProgramIndexFingerprint);
        Assert.Single(fact.Evidence);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);

        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId(" "),
            new MethodId("method:v1:test"),
            new OperationId("operation:v1:test"),
            root,
            Profile.Id,
            Fingerprint,
            [CreateEvidence()],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId(" "),
            new OperationId("operation:v1:test"),
            root,
            Profile.Id,
            Fingerprint,
            [CreateEvidence()],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId("method:v1:test"),
            new OperationId("operation:v1:test"),
            root,
            Profile.Id,
            Fingerprint,
            [],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId("method:v1:test"),
            new OperationId("operation:v1:test"),
            root,
            Profile.Id,
            Fingerprint,
            default,
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId("method:v1:test"),
            new OperationId("operation:v1:test"),
            root,
            Profile.Id,
            Fingerprint,
            [CreateEvidence()],
            CertaintyLevel.Unknown));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId("method:v1:test"),
            new OperationId("operation:v1:test"),
            root,
            Profile.Id,
            Fingerprint,
            [CreateEvidence(CertaintyLevel.Heuristic)],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId("method:v1:test"),
            new OperationId(" "),
            root,
            Profile.Id,
            Fingerprint,
            [CreateEvidence()],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentNullException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId("method:v1:test"),
            new OperationId("operation:v1:test"),
            null!,
            Profile.Id,
            Fingerprint,
            [CreateEvidence()],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId("method:v1:test"),
            new OperationId("operation:v1:test"),
            root,
            new CompilationProfileId(" "),
            Fingerprint,
            [CreateEvidence()],
            CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFact(
            new SemanticFactId("predicate-fact:v1:test"),
            new MethodId("method:v1:test"),
            new OperationId("operation:v1:test"),
            root,
            Profile.Id,
            " ",
            [CreateEvidence()],
            CertaintyLevel.Exact));
    }

    [Fact]
    public void PredicateTreeRetainsOrderedChildrenAndValueEquality()
    {
        var left = LogicalAnd(
            Negation(Symbol("Order.IsPaid")),
            new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "true"));
        var right = NumericConstant("0");
        var root = Comparison(left, right, PredicateComparisonOperatorKind.NotEqual);

        Assert.Equal(2, root.Children.Length);
        Assert.Equal(PredicateExpressionKind.LogicalAnd, root.Children[0].Kind);
        Assert.Equal(PredicateExpressionKind.NumericConstant, root.Children[1].Kind);
        Assert.Equal(PredicateExpressionKind.Negation, root.Children[0].Children[0].Kind);
        Assert.Equal("Order.IsPaid", root.Children[0].Children[0].Children[0].DisplayName);
        Assert.Equal("true", root.Children[0].Children[1].ConstantValue);

        var reconstructed = Comparison(
            LogicalAnd(
                Negation(Symbol("Order.IsPaid")),
                new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "true")),
            NumericConstant("0"),
            PredicateComparisonOperatorKind.NotEqual);
        var swapped = Comparison(right, left, PredicateComparisonOperatorKind.NotEqual);

        // ImmutableArray equality is reference-based, so compare the tree structurally: a rebuilt
        // tree must be identical node-by-node while the swapped operand order must differ at index 0.
        AssertExpressionEqual(reconstructed, root);
        Assert.Equal(PredicateExpressionKind.NumericConstant, swapped.Children[0].Kind);
        Assert.Equal(PredicateExpressionKind.LogicalAnd, swapped.Children[1].Kind);
        Assert.NotEqual(PredicateExpressionKind.LogicalAnd, swapped.Children[0].Kind);

        var firstFact = CreateFact(root);
        var secondFact = CreateFact(Comparison(Symbol("Order.IsPaid"), new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "false"), PredicateComparisonOperatorKind.Equal), "predicate-fact:v1:other");
        var set = new PredicateSemanticFactSet(
            SchemaVersion: 1,
            "predicate-collector:v1",
            Profile,
            Fingerprint,
            [secondFact, firstFact],
            [],
            "predicate-facts:v1");

        Assert.Equal(root, set.Predicates[1].Root);
        Assert.Equal(secondFact, set.Predicates[0]);
        Assert.Equal(firstFact, set.Predicates[1]);
        Assert.Equal(2, set.Predicates.Length);
    }

    private static void AssertExpressionEqual(PredicateExpression expected, PredicateExpression actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.TypeName, actual.TypeName);
        Assert.Equal(expected.ComparisonOperator, actual.ComparisonOperator);
        Assert.Equal(expected.ArithmeticOperator, actual.ArithmeticOperator);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.ConstantValue, actual.ConstantValue);
        Assert.Equal(expected.Children.Length, actual.Children.Length);
        for (int i = 0; i < expected.Children.Length; i++)
        {
            AssertExpressionEqual(expected.Children[i], actual.Children[i]);
        }
    }

    [Fact]
    public void PredicateSemanticFactSetRejectsMalformedStateAndRetainsDeclaredProperties()
    {
        var root = Comparison(Symbol("Order.IsPaid"), new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "true"), PredicateComparisonOperatorKind.Equal);
        var fact = CreateFact(root);

        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            SchemaVersion: 0,
            "predicate-collector:v1",
            Profile,
            Fingerprint,
            [fact],
            [],
            "predicate-facts:v1"));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            SchemaVersion: 1,
            " ",
            Profile,
            Fingerprint,
            [fact],
            [],
            "predicate-facts:v1"));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            SchemaVersion: 1,
            "predicate-collector:v1",
            null!,
            Fingerprint,
            [fact],
            [],
            "predicate-facts:v1"));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            SchemaVersion: 1,
            "predicate-collector:v1",
            Profile,
            " ",
            [fact],
            [],
            "predicate-facts:v1"));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            SchemaVersion: 1,
            "predicate-collector:v1",
            Profile,
            Fingerprint,
            default,
            default,
            "predicate-facts:v1"));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            SchemaVersion: 1,
            "predicate-collector:v1",
            Profile,
            Fingerprint,
            [fact],
            [],
            " "));

        var accepted = new PredicateSemanticFactSet(
            SchemaVersion: 1,
            "predicate-collector:v1",
            Profile,
            Fingerprint,
            [fact],
            [],
            "predicate-facts:v1");

        Assert.Equal(1, accepted.SchemaVersion);
        Assert.Equal("predicate-collector:v1", accepted.ProducerVersion);
        Assert.Equal(Profile.Id, accepted.Profile.Id);
        Assert.Equal(Fingerprint, accepted.ProgramIndexFingerprint);
        Assert.Equal(fact, Assert.Single(accepted.Predicates));
        Assert.Empty(accepted.Diagnostics);
        Assert.Equal("predicate-facts:v1", accepted.DebugProjection);
    }

    [Fact]
    public void MappingIsNonEmptyOrderedDistinctAndSetCarriesMappings()
    {
        var root = Comparison(Symbol("Order.IsPaid"), new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "true"), PredicateComparisonOperatorKind.Equal);
        var predicate = CreateFact(root);
        var evidence = ImmutableArray.Create(CreateEvidence());
        var mapping = new PredicateDecisionMappingFact(
            new SemanticFactId("predicate-mapping:v1:test"),
            predicate.Id,
            predicate.Method,
            [new OperationId("flow-condition:1"), new OperationId("flow-condition:2")],
            Profile.Id,
            Fingerprint,
            evidence,
            CertaintyLevel.Exact);

        var set = new PredicateSemanticFactSet(1, "predicate-collector:v1", Profile, Fingerprint, [predicate], [mapping], [], "predicate-facts:v1");
        Assert.Equal(predicate.Id, Assert.Single(set.Mappings).PredicateId);
        Assert.Equal(["flow-condition:1", "flow-condition:2"], set.Mappings[0].LoweredConditionOperations.Select(operation => operation.Value));
        Assert.Throws<ArgumentException>(() => new PredicateDecisionMappingFact(
            mapping.Id, predicate.Id, predicate.Method, [], Profile.Id, Fingerprint, evidence, CertaintyLevel.Exact));
        Assert.Throws<ArgumentException>(() => new PredicateDecisionMappingFact(
            mapping.Id, predicate.Id, predicate.Method, [new OperationId("flow-condition:1"), new OperationId("flow-condition:1")], Profile.Id, Fingerprint, evidence, CertaintyLevel.Exact));
    }

    [Fact]
    public void PredicateSetRejectsUnscopedDuplicateOrUnorderedPredicatesAndDuplicateMappings()
    {
        var root = Comparison(Symbol("Order.IsPaid"), new PredicateExpression(PredicateExpressionKind.BooleanConstant, [], "System.Boolean", constantValue: "true"), PredicateComparisonOperatorKind.Equal);
        var first = CreateFact(root, "predicate-fact:v1:a");
        var second = CreateFact(root, "predicate-fact:v1:b");

        PredicateSemanticFactSet Create(params PredicateSemanticFact[] predicates)
            => new(1, "predicate-collector:v1", Profile, Fingerprint, predicates.ToImmutableArray(), [], "predicate-facts:v1");

        Assert.Throws<ArgumentException>(() => Create(second, first));
        Assert.Throws<ArgumentException>(() => Create(first, first));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            1, "predicate-collector:v1", Profile, Fingerprint,
            [CreateFact(root, "predicate-fact:v1:a", new CompilationProfileId("other-profile"))], [], "predicate-facts:v1"));
        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            1, "predicate-collector:v1", Profile, "other-fingerprint",
            [first], [], "predicate-facts:v1"));

        var mapping = new PredicateDecisionMappingFact(
            new SemanticFactId("predicate-mapping:v1:a"), first.Id, first.Method,
            [new OperationId("flow-condition:1")], Profile.Id, Fingerprint,
            [CreateEvidence()], CertaintyLevel.Exact);
        var duplicatePredicateMapping = new PredicateDecisionMappingFact(
            new SemanticFactId("predicate-mapping:v1:b"), first.Id, first.Method,
            [new OperationId("flow-condition:2")], Profile.Id, Fingerprint,
            [CreateEvidence()], CertaintyLevel.Exact);

        Assert.Throws<ArgumentException>(() => new PredicateSemanticFactSet(
            1, "predicate-collector:v1", Profile, Fingerprint, [first, second],
            [mapping, duplicatePredicateMapping], [], "predicate-facts:v1"));

        // A source predicate may be retained without a mapping when compiler ownership is ambiguous.
        var mappingless = Create(first);
        Assert.Single(mappingless.Predicates);
        Assert.Empty(mappingless.Mappings);
    }
}
