using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// CR-1 projection proof through the PredicateFacts fixture. Admitted methods normalize their material
/// decision condition into the typed predicate tree; unsupported shapes fail closed with a PRED001
/// diagnostic and no fact; repeated extraction is deterministic, anchored, and never displaces the
/// accepted legacy comparison projection.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class PredicateSemanticFactProjectionTests
{
    private const string FixtureName = "PredicateFacts";
    private const string FixtureRelativePath = "tests/fixtures/CorpusRoadmap/PredicateFacts/PredicateFacts.csproj";

    [Fact]
    public async Task AdmittedMethodsProjectPredicateFactsWithCanonicalRootShapes()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var predicateSet = extraction.PredicateSemanticFacts;
        var factsByMethod = extraction.PredicateSemanticFacts.Predicates
            .GroupBy(fact => fact.Method)
            .ToDictionary(group => names[group.Key], group => group.ToArray());

        // Every admitted representative family yields at least one fact: null check, boolean truth,
        // comparison, enum constant comparison, relational pattern comparison, arithmetic operand.
        foreach (var methodName in new[]
                 {
                     "IsNull", "IsTrue", "IsEqual", "IsCancelled", "IsLarge", "IsSumAbove",
                 })
        {
            Assert.True(
                factsByMethod.TryGetValue(methodName, out var facts) && facts.Length > 0,
                $"The admitted method {methodName} must project at least one predicate fact.");
        }

        // The admitted roots span the canonical vocabulary without inventing source spelling: the
        // comparison and boolean-truth forms appear as roots while binary arithmetic and enum
        // constants appear as ordered descendants of a comparison.
        var roots = factsByMethod
            .Where(pair => pair.Key is "IsNull" or "IsTrue" or "IsEqual" or "IsCancelled" or "IsLarge" or "IsSumAbove")
            .SelectMany(pair => pair.Value)
            .Select(fact => fact.Root)
            .ToArray();
        Assert.Contains(roots, root => root.Kind == PredicateExpressionKind.Comparison);
        Assert.Contains(roots, root => root.Kind == PredicateExpressionKind.BooleanTruth);
        Assert.Contains(roots, root => ContainsKind(root, PredicateExpressionKind.BinaryArithmetic));
        Assert.Contains(roots, root => ContainsKind(root, PredicateExpressionKind.EnumConstant));
    }

    [Fact]
    public async Task StructuralNegationIsExplicitAndGroupedDecisionsRemainExactlyEvidenced()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var predicateSet = extraction.PredicateSemanticFacts;

        // !ready must be represented as a structural Negation node, never as swapped comparison text.
        var notReadyFact = Assert.Single(
            predicateSet.Predicates.Where(fact => names[fact.Method] == "IsNotReady"));
        Assert.Equal(PredicateExpressionKind.Negation, notReadyFact.Root.Kind);

        // The short-circuit grouped decision lowers to at least one material decision and every one
        // of its facts stays compiler-proven: exact certainty carried by exact source evidence.
        var groupedFacts = predicateSet.Predicates
            .Where(fact => names[fact.Method] == "IsGroupedDecision")
            .ToArray();
        Assert.NotEmpty(groupedFacts);
        Assert.All(groupedFacts, fact => AssertExactEvidence(fact.Evidence, fact.Certainty));
    }

    [Fact]
    public async Task NullChecksRetainTheTestedOperandAndTypedNullConstant()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);

        foreach (var methodName in new[] { "IsNull", "IsEqualNull" })
        {
            var fact = Assert.Single(extraction.PredicateSemanticFacts.Predicates.Where(fact => names[fact.Method] == methodName));
            Assert.Equal(PredicateExpressionKind.Comparison, fact.Root.Kind);
            Assert.Equal(PredicateComparisonOperatorKind.Equal, fact.Root.ComparisonOperator);
            Assert.Equal(PredicateExpressionKind.SymbolValue, fact.Root.Children[0].Kind);
            Assert.Equal(PredicateExpressionKind.NullConstant, fact.Root.Children[1].Kind);
            Assert.Equal(fact.Root.Children[0].TypeName, fact.Root.Children[1].TypeName);
        }
    }

    [Fact]
    public async Task MemberReceiversRemainDistinctAndInvocationReceiversFailClosed()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var fact = Assert.Single(extraction.PredicateSemanticFacts.Predicates.Where(fact => names[fact.Method] == "HasMoreItems"));

        Assert.Equal(PredicateExpressionKind.Comparison, fact.Root.Kind);
        Assert.Equal("left.Count", fact.Root.Children[0].DisplayName);
        Assert.Equal("right.Count", fact.Root.Children[1].DisplayName);
        Assert.DoesNotContain(extraction.PredicateSemanticFacts.Predicates, fact => names[fact.Method] == "HasReturnedOrderItems");
        Assert.Contains(extraction.PredicateSemanticFacts.Diagnostics, diagnostic => diagnostic.Code == "PRED001");
    }

    [Fact]
    public async Task ShortCircuitLogicalGroupingProjectsAsOneConditionAnchoredTree()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var predicateSet = extraction.PredicateSemanticFacts;
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var fact = Assert.Single(extraction.PredicateSemanticFacts.Predicates.Where(fact => names[fact.Method] == "IsGroupedDecision"));

        Assert.Equal(PredicateExpressionKind.LogicalAnd, fact.Root.Kind);
        Assert.Equal(PredicateExpressionKind.LogicalOr, fact.Root.Children[0].Kind);
        Assert.Equal(PredicateExpressionKind.SymbolValue, fact.Root.Children[0].Children[0].Kind);
        Assert.Equal(PredicateExpressionKind.SymbolValue, fact.Root.Children[0].Children[1].Kind);
        Assert.Equal(PredicateExpressionKind.SymbolValue, fact.Root.Children[1].Kind);
        var mapping = Assert.Single(predicateSet.Mappings, item => item.PredicateId == fact.Id);
        Assert.True(mapping.LoweredConditionOperations.Length >= 2);
        Assert.Equal(mapping.LoweredConditionOperations.Length, mapping.LoweredConditionOperations.Distinct().Count());
    }

    [Fact]
    public async Task NestedAndGroupedNegationRetainsEveryStructuralNode()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);

        var doubleNegated = Assert.Single(extraction.PredicateSemanticFacts.Predicates.Where(fact => names[fact.Method] == "IsDoubleNegated"));
        Assert.Equal(PredicateExpressionKind.Negation, doubleNegated.Root.Kind);
        Assert.Equal(PredicateExpressionKind.Negation, doubleNegated.Root.Children[0].Kind);

        var neither = Assert.Single(extraction.PredicateSemanticFacts.Predicates.Where(fact => names[fact.Method] == "IsNeitherReadyNorEnabled"));
        Assert.Equal(PredicateExpressionKind.Negation, neither.Root.Kind);
        Assert.Equal(PredicateExpressionKind.LogicalOr, neither.Root.Children[0].Kind);

        var parenthesized = Assert.Single(extraction.PredicateSemanticFacts.Predicates.Where(fact => names[fact.Method] == "IsParenthesizedNotReady"));
        Assert.Equal(PredicateExpressionKind.Negation, parenthesized.Root.Kind);
        Assert.Equal(PredicateExpressionKind.SymbolValue, parenthesized.Root.Children[0].Kind);
    }

    [Fact]
    public async Task UnsupportedPredicateShapesProduceNoFactsAndAStableDiagnostic()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var predicateSet = extraction.PredicateSemanticFacts;

        foreach (var methodName in new[]
                 {
                       "IsSamePrice", "AreEqualNullable", "IsDynamicEqual",
                       "IsAfterIncrement", "IsBlank", "HasInterpolatedPrefix",
                       "HasReturnedOrderItems",
                       "IsReadyProperty", "HasNestedPropertyCount",
                  })
        {
            Assert.DoesNotContain(
                predicateSet.Predicates,
                fact => names[fact.Method] == methodName);
        }

        Assert.Contains(predicateSet.Diagnostics, diagnostic => diagnostic.Code == "PRED001");

        var expired = Assert.Single(predicateSet.Predicates.Where(fact => names[fact.Method] == "IsExpired"));
        Assert.Contains(
            expired.Root.Children,
            child => child.Kind == PredicateExpressionKind.OpaqueValue
                && child.DisplayName == "DateTime.UtcNow");

    }

    [Fact]
    public async Task BuiltInStringAndCharacterEqualityProjectButUserOperatorsDoNot()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);

        foreach (var methodName in new[] { "IsReadyString", "IsNotEmptyString", "IsReadyCharacter" })
        {
            var fact = Assert.Single(extraction.PredicateSemanticFacts.Predicates.Where(item => names[item.Method] == methodName));
            Assert.Equal(PredicateExpressionKind.Comparison, fact.Root.Kind);
            Assert.Contains(fact.Root.Children, child => child.Kind is PredicateExpressionKind.StringConstant or PredicateExpressionKind.CharacterConstant);
        }

        Assert.DoesNotContain(extraction.PredicateSemanticFacts.Predicates, item => names[item.Method] == "IsSamePrice");
    }

    [Fact]
    public async Task NestedStatementLoopAndConditionalPredicatesRetainUniqueExactMappings()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var names = extraction.ProgramIndex.Methods.ToDictionary(method => method.Id, method => method.Name);
        var decisionIds = extraction.Artifacts.BehaviorInput.Methods
            .SelectMany(method => method.Blocks)
            .Where(block => block.BranchCondition is not null)
            .Select(block => block.BranchCondition!.Value)
            .ToHashSet();
        var targetNames = new[] { "IsNestedStatement", "IsWhileCondition", "IsForCondition", "IsConditionalExpression" };
        var facts = extraction.PredicateSemanticFacts.Predicates
            .Where(fact => targetNames.Contains(names[fact.Method]))
            .ToArray();

        Assert.NotEmpty(facts);
        Assert.Equal(facts.Length, facts.Select(fact => fact.Id).Distinct().Count());
        Assert.All(facts, fact =>
        {
            var mappings = extraction.PredicateSemanticFacts.Mappings.Where(mapping => mapping.PredicateId == fact.Id).ToArray();
            Assert.True(mappings.Length <= 1);
            if (decisionIds.Count > 0)
            {
                Assert.NotEmpty(mappings);
                Assert.All(mappings.SelectMany(mapping => mapping.LoweredConditionOperations), operation => Assert.Contains(operation, decisionIds));
            }
        });
    }

    [Fact]
    public async Task RepeatedExtractionIsDeterministicAnchoredAndCompatibleWithLegacyComparisons()
    {
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();

        var firstIds = string.Join(
            "\n",
            first.PredicateSemanticFacts.Predicates.Select(fact => fact.Id.Value).Order(StringComparer.Ordinal));
        var secondIds = string.Join(
            "\n",
            second.PredicateSemanticFacts.Predicates.Select(fact => fact.Id.Value).Order(StringComparer.Ordinal));
        Assert.NotEmpty(firstIds);
        Assert.Equal(firstIds, secondIds);
        Assert.Equal(
            first.PredicateSemanticFacts.DebugProjection,
            second.PredicateSemanticFacts.DebugProjection);
        Assert.Equal(
            first.PredicateSemanticFacts.Mappings.Select(mapping => mapping.Id.Value),
            second.PredicateSemanticFacts.Mappings.Select(mapping => mapping.Id.Value));

        // Every fact anchors the same compilation profile and Program Index fingerprint as its set,
        // and the set's debug representation carries the fingerprint instead of any checkout path.
        var predicateSet = first.PredicateSemanticFacts;
        Assert.NotEmpty(predicateSet.Predicates);
        Assert.False(string.IsNullOrWhiteSpace(predicateSet.ProgramIndexFingerprint));
        Assert.All(predicateSet.Predicates, fact =>
        {
            Assert.Equal(predicateSet.Profile.Id, fact.ProfileId);
            Assert.Equal(predicateSet.ProgramIndexFingerprint, fact.ProgramIndexFingerprint);
        });
        Assert.All(predicateSet.Mappings, mapping =>
        {
            Assert.Equal(predicateSet.Profile.Id, mapping.ProfileId);
            Assert.Equal(predicateSet.ProgramIndexFingerprint, mapping.ProgramIndexFingerprint);
            Assert.NotEmpty(mapping.LoweredConditionOperations);
            Assert.Equal(mapping.LoweredConditionOperations.Length, mapping.LoweredConditionOperations.Distinct().Count());
            Assert.Contains(predicateSet.Predicates, fact => fact.Id == mapping.PredicateId);
        });
        Assert.Contains(
            $"programIndexFingerprint={predicateSet.ProgramIndexFingerprint}",
            predicateSet.DebugProjection,
            StringComparison.Ordinal);
        Assert.All(predicateSet.Mappings, mapping =>
        {
            Assert.Contains($"mapping {mapping.Id.Value}", predicateSet.DebugProjection, StringComparison.Ordinal);
            Assert.Contains(string.Join("|", mapping.LoweredConditionOperations.Select(operation => operation.Value)), predicateSet.DebugProjection, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(FindRepositoryRoot(), predicateSet.DebugProjection, StringComparison.OrdinalIgnoreCase);

        // The additive companion never displaces the accepted legacy comparison projection.
        var equalMethod = Assert.Single(first.ProgramIndex.Methods, method => method.Name == "IsEqual").Id;
        Assert.Contains(first.SemanticFacts.Comparisons, fact => fact.Method == equalMethod);

        var decisionConditionIds = first.Artifacts.BehaviorInput.Methods
            .SelectMany(method => method.Blocks)
            .Where(block => block.BranchCondition is not null)
            .Select(block => block.BranchCondition!.Value)
            .ToHashSet();
        Assert.NotEmpty(decisionConditionIds);
        Assert.All(predicateSet.Mappings, mapping =>
            Assert.All(mapping.LoweredConditionOperations, operation => Assert.Contains(operation, decisionConditionIds)));
        Assert.Equal(predicateSet.Predicates.Length, predicateSet.Mappings.Length);
        Assert.All(predicateSet.Predicates, predicate => Assert.Single(predicateSet.Mappings, mapping => mapping.PredicateId == predicate.Id));
        Assert.All(predicateSet.Mappings, mapping => Assert.Single(predicateSet.Predicates, predicate => predicate.Id == mapping.PredicateId));
    }

    private static bool ContainsKind(PredicateExpression node, PredicateExpressionKind kind)
        => node.Kind == kind || node.Children.Any(child => ContainsKind(child, kind));

    private static void AssertExactEvidence(ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        Assert.NotEmpty(evidence);
        Assert.Equal(CertaintyLevel.Exact, certainty);
        Assert.All(evidence, item => Assert.Equal(CertaintyLevel.Exact, item.Certainty));
    }

    private static async Task<ProfileAnalysisExtraction> ExtractSuccessfullyAsync()
    {
        var result = await ExtractFixtureAsync();
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractFixtureAsync()
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0"));
        return await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
