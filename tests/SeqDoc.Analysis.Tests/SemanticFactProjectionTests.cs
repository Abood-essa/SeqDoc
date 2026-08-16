using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class SemanticFactProjectionTests
{
    private const string FixtureName = "SemanticFacts";
    private static readonly char[] ProjectionLineSeparators = { '\r', '\n' };

    [Fact]
    public async Task RepeatedExtractionProducesIdenticalFactIdsAndDebugProjection()
    {
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();

        var firstIds = CollectFactIds(first.SemanticFacts);
        var secondIds = CollectFactIds(second.SemanticFacts);

        Assert.NotEmpty(firstIds);
        Assert.Equal(firstIds, secondIds);

        var firstProjection = first.SemanticFacts.DebugProjection;
        var secondProjection = second.SemanticFacts.DebugProjection;
        Assert.Equal(firstProjection, secondProjection);

        // Canonical projection uses only \n separators; a platform \r must never appear.
        Assert.DoesNotContain("\r", firstProjection, StringComparison.Ordinal);
        Assert.Contains("\n", firstProjection, StringComparison.Ordinal);

        var firstProjectionIds = ParseProjectionFactIds(firstProjection);
        var secondProjectionIds = ParseProjectionFactIds(secondProjection);
        Assert.NotEmpty(firstProjectionIds);
        Assert.Equal(string.Join("\n", firstProjectionIds), string.Join("\n", secondProjectionIds));
        Assert.Equal(
            string.Join("\n", firstProjectionIds.Order(StringComparer.Ordinal)),
            string.Join("\n", firstProjectionIds));
    }

    [Fact]
    public async Task DebugProjectionContainsNoAbsoluteRepositoryRoot()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var projection = extraction.SemanticFacts.DebugProjection;

        Assert.NotEmpty(projection);
        Assert.DoesNotContain(FindRepositoryRoot(), projection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EqualityAndRelationalComparisonsUseTheClosedOperatorVocabulary()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.SemanticFacts;
        var equalMethod = FindMethod(extraction, "IsEqual");
        var belowMethod = FindMethod(extraction, "IsBelow");

        var equalFact = Assert.Single(facts.Comparisons, fact => fact.Method == equalMethod);
        Assert.Equal(ComparisonOperatorKind.Equal, equalFact.Operator);
        AssertComparisonOperands(extraction, equalMethod, equalFact);
        var belowFact = Assert.Single(facts.Comparisons, fact => fact.Method == belowMethod);
        Assert.Equal(ComparisonOperatorKind.LessThan, belowFact.Operator);
        AssertComparisonOperands(extraction, belowMethod, belowFact);
    }

    [Fact]
    public async Task ArithmeticBinaryOperationEmitsNoComparisonFact()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var sumMethod = FindMethod(extraction, "Sum");

        Assert.DoesNotContain(extraction.SemanticFacts.Comparisons, fact => fact.Method == sumMethod);
    }

    [Fact]
    public async Task NamedAndReorderedArgumentsBindToCompilerParameterOrdinals()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var callingMethod = FindMethod(extraction, "CallWithReorderedArguments");
        var targetMethod = FindMethod(extraction, "Describe");

        var bindings = extraction.SemanticFacts.ArgumentBindings
            .Where(fact => fact.Method == callingMethod && fact.TargetMethod == targetMethod)
            .ToArray();
        Assert.Equal(2, bindings.Length);

        var idBinding = Assert.Single(bindings, fact => fact.ParameterOrdinal == 0);
        var nameBinding = Assert.Single(bindings, fact => fact.ParameterOrdinal == 1);

        Assert.Equal("7", ConstantForArgument(extraction, callingMethod, idBinding.ArgumentOperation));
        Assert.Equal("alpha", ConstantForArgument(extraction, callingMethod, nameBinding.ArgumentOperation));
    }

    [Fact]
    public async Task ProjectedFactsRetainNonEmptyEvidenceAndNeverPromoteCertainty()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.SemanticFacts;

        Assert.All(facts.Comparisons, fact => AssertEvidence(fact.Evidence, fact.Certainty));
        Assert.All(facts.ArgumentBindings, fact => AssertEvidence(fact.Evidence, fact.Certainty));
        Assert.All(facts.ReturnProvenances, fact => AssertEvidence(fact.Evidence, fact.Certainty));
    }

    [Fact]
    public async Task ExplicitValueReturnHasProvenanceAndNoValueReturnInventsNone()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.SemanticFacts;
        var computeMethod = FindMethod(extraction, "ComputeValue");
        var notifyMethod = FindMethod(extraction, "Notify");

        var valueProvenance = Assert.Single(facts.ReturnProvenances, fact => fact.Method == computeMethod);
        Assert.NotEqual(default, valueProvenance.ValueOperation);

        Assert.DoesNotContain(facts.ReturnProvenances, fact => fact.Method == notifyMethod);
    }

    [Fact]
    public async Task BehaviorInputFingerprintRemainsStableAcrossSemanticProjectionAccess()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var fingerprintBefore = extraction.BehaviorInput.InputFingerprint;

        // Accessing the semantic seam must not mutate the accepted behavior input.
        _ = extraction.SemanticFacts.DebugProjection;

        var fingerprintAfter = extraction.BehaviorInput.InputFingerprint;

        Assert.Equal(fingerprintBefore, fingerprintAfter);
        Assert.Equal(64, fingerprintBefore.Length);
        Assert.Matches("^[0-9a-f]{64}$", fingerprintBefore);
    }

    [Fact]
    public void RepresentativeComparisonConstructorRejectsEmptyEvidenceUnknownCertaintyAndCertaintyPromotion()
    {
        var id = new SemanticFactId("semantic-fact-test-id");
        var method = new MethodId("method-test");
        var operation = new OperationId("operation-test");
        var leftOperation = new OperationId("left-test");
        var rightOperation = new OperationId("right-test");

        // (a) Empty evidence must be rejected.
        Assert.Throws<ArgumentException>(() => new ComparisonSemanticFact(
            id,
            method,
            ComparisonOperatorKind.Equal,
            operation,
            leftOperation,
            rightOperation,
            [],
            CertaintyLevel.Exact));

        // (b) Unknown certainty must be rejected even with valid source evidence.
        Assert.Throws<ArgumentException>(() => new ComparisonSemanticFact(
            id,
            method,
            ComparisonOperatorKind.Equal,
            operation,
            leftOperation,
            rightOperation,
            [CreateSourceEvidence(CertaintyLevel.Exact)],
            CertaintyLevel.Unknown));

        // (c) Exact fact certainty must not be derived from Conservative evidence.
        Assert.Throws<ArgumentException>(() => new ComparisonSemanticFact(
            id,
            method,
            ComparisonOperatorKind.Equal,
            operation,
            leftOperation,
            rightOperation,
            [CreateSourceEvidence(CertaintyLevel.Conservative)],
            CertaintyLevel.Exact));
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
        var relativePath = $"tests/fixtures/BehaviorDocumentation/{FixtureName}/{FixtureName}.csproj";
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
        return await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
    }

    private static MethodId FindMethod(ProfileAnalysisExtraction extraction, string name)
        => Assert.Single(extraction.ProgramIndex.Methods, method => method.Name == name).Id;

    private static string? ConstantForArgument(ProfileAnalysisExtraction extraction, MethodId method, OperationId operation)
        => Assert.Single(
            Assert.Single(extraction.BehaviorInput.Methods, body => body.Method == method).Operations,
            candidate => candidate.Id == operation).ConstantValue;

    private static void AssertComparisonOperands(ProfileAnalysisExtraction extraction, MethodId method, ComparisonSemanticFact fact)
    {
        Assert.NotEqual(default, fact.LeftOperation);
        Assert.NotEqual(default, fact.RightOperation);

        var anchored = Assert.Single(
            Assert.Single(extraction.BehaviorInput.Methods, body => body.Method == method).Operations,
            operation => operation.Id == fact.Operation);
        Assert.Equal(ExtractedOperationKind.Binary, anchored.Kind);
        Assert.Equal(2, anchored.Operands.Length);
        Assert.Equal(fact.LeftOperation, anchored.Operands[0]);
        Assert.Equal(fact.RightOperation, anchored.Operands[1]);
    }

    private static string CollectFactIds(SemanticFactSet facts) => string.Join(
        "\n",
        facts.Comparisons
            .Select(fact => fact.Id.Value)
            .Concat(facts.ArgumentBindings.Select(fact => fact.Id.Value))
            .Concat(facts.ReturnProvenances.Select(fact => fact.Id.Value))
            .Order(StringComparer.Ordinal));

    private static ImmutableArray<string> ParseProjectionFactIds(string projection)
    {
        return projection
            .Split(ProjectionLineSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("comparison ", StringComparison.Ordinal)
                || line.StartsWith("argument-binding ", StringComparison.Ordinal)
                || line.StartsWith("return-provenance ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
            .ToImmutableArray();
    }

    private static EvidenceRef CreateSourceEvidence(CertaintyLevel certainty) => new(
        new EvidenceId("evidence-test-id"),
        EvidenceKind.Source,
        "test-artifact",
        new SourceRange(
            new DocumentId("document-test-id"),
            new SourcePosition(1, 1),
            new SourcePosition(1, 5)),
        "test-symbol",
        null,
        certainty);

    private static void AssertEvidence(ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.False(string.IsNullOrWhiteSpace(item.Artifact)));
        Assert.True(certainty != CertaintyLevel.Unknown, "A projected fact must carry explicit certainty.");
        Assert.True(certainty >= evidence.Max(item => item.Certainty), "Fact certainty must never exceed its strongest evidence.");
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
