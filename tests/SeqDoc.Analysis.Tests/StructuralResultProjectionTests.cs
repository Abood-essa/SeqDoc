using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class StructuralResultProjectionTests
{
    private const string FixtureName = "GetMeaning";
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj";
    private const string ServiceMetadataName = "BehaviorDocumentation.GetMeaning.Services.GadgetService";
    private const string ControllerMetadataName = "BehaviorDocumentation.GetMeaning.Controllers.GadgetsController";
    private const string ResultType = "BehaviorDocumentation.GetMeaning.Services.GadgetResult<BehaviorDocumentation.GetMeaning.Models.Gadget>";

    [Fact]
    public async Task ProjectionDistinguishesSuccessDataAndFailureStatusFactories()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var serviceMethod = FindMethod(extraction, ServiceMetadataName, "GetByIdAsync");
        var factories = extraction.StructuralResultFacts.Factories
            .Where(fact => fact.Method == serviceMethod)
            .ToArray();

        var success = Assert.Single(factories, fact => fact.FactoryKind == StructuralResultFactoryKind.Success);
        Assert.True(success.IsSuccess);
        Assert.Equal(ResultType, success.ResultType);
        Assert.NotNull(success.ArgumentOperation);

        var notFound = Assert.Single(factories, fact => fact.FactoryKind == StructuralResultFactoryKind.NotFound);
        Assert.False(notFound.IsSuccess);
        Assert.Equal(ResultType, notFound.ResultType);

        // Lookalike result shapes never project meaning: a type with IsSuccess but no self-returning
        // factory, a factory that does not return its containing type, and an opposite-polarity
        // fully-shaped type whose Success factory constructs IsSuccess as false all fail closed.
        Assert.DoesNotContain(extraction.StructuralResultFacts.Factories,
            fact => fact.ResultType.Contains("LookalikeOutcome", StringComparison.Ordinal)
                || fact.ResultType.Contains("PlainFactory", StringComparison.Ordinal)
                || fact.ResultType.Contains("OppositePolarityResult", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProjectionRecordsIsSuccessDecisionWithExactPathOutcomes()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var actionMethod = FindMethod(extraction, ControllerMetadataName, "GetById");
        var decision = Assert.Single(extraction.StructuralResultFacts.Decisions,
            fact => fact.Method == actionMethod);

        // Roslyn normalizes `if (!result.IsSuccess)` into a plain IsSuccess branch value whose
        // true/false successors swap; the projection therefore records the polarity implicitly and
        // asserts the exact outcome helpers reached on each path.
        Assert.True(
            decision.SuccessPath.Length == 1 && decision.FailurePath.Length == 1,
            $"Decision paths: success=[{string.Join(",", decision.SuccessPath.Select(path => path.HelperKind.ToString()))}] failure=[{string.Join(",", decision.FailurePath.Select(path => path.HelperKind.ToString()))}] negated={decision.IsSuccessNegated}");
        Assert.Equal(HttpOutcomeHelperKind.Ok, Assert.Single(decision.SuccessPath).HelperKind);
        Assert.Equal(HttpOutcomeHelperKind.NotFound, Assert.Single(decision.FailurePath).HelperKind);
        Assert.NotEqual(default, decision.PropertyOperation);
        Assert.NotEqual(default, decision.ResultOperation);
    }

    [Fact]
    public async Task RepeatedProjectionIsDeterministicAndCanonical()
    {
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();

        var firstIds = CollectFactIds(first.StructuralResultFacts);
        var secondIds = CollectFactIds(second.StructuralResultFacts);
        Assert.NotEmpty(firstIds);
        Assert.Equal(firstIds, secondIds);
        Assert.Equal(first.StructuralResultFacts.DebugProjection, second.StructuralResultFacts.DebugProjection);
        Assert.DoesNotContain("\r", first.StructuralResultFacts.DebugProjection, StringComparison.Ordinal);
        Assert.Contains("\n", first.StructuralResultFacts.DebugProjection, StringComparison.Ordinal);
        Assert.DoesNotContain(FindRepositoryRoot(), first.StructuralResultFacts.DebugProjection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepresentativeFactoryConstructorRejectsEmptyEvidenceAndUnknownCertainty()
    {
        var id = new SemanticFactId("structural-fact-test-id");
        var method = new MethodId("method-test");
        var operation = new OperationId("operation-test");

        Assert.Throws<ArgumentException>(() => new StructuralResultFactoryFact(
            id,
            method,
            operation,
            "Test.Result<Test.Data>",
            StructuralResultFactoryKind.Success,
            true,
            null,
            [],
            CertaintyLevel.Exact));

        Assert.Throws<ArgumentException>(() => new StructuralResultFactoryFact(
            id,
            method,
            operation,
            "Test.Result<Test.Data>",
            StructuralResultFactoryKind.Success,
            true,
            null,
            [CreateSourceEvidence(CertaintyLevel.Conservative)],
            CertaintyLevel.Exact));
    }

    [Fact]
    public async Task FullyShapedOppositePolarityLookalikeEmitsNoStructuralFacts()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var decoyController = Assert.Single(extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.GetMeaning.Controllers.DecoyResultController");
        var decoyAction = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == "GetById" && method.ContainingType == decoyController.Id).Id;

        // A result-shaped type whose factories return the opposite polarity from their names and
        // whose decision branches on a non-IsSuccess Boolean member reaches exact outcome helpers;
        // only the exact IsSuccess property and compiler-proven returned state are admissible, so
        // this fully-shaped lookalike must never project factory or decision meaning.
        Assert.DoesNotContain(extraction.StructuralResultFacts.Factories,
            fact => fact.ResultType.Contains("OppositePolarityResult", StringComparison.Ordinal));
        Assert.DoesNotContain(extraction.StructuralResultFacts.Decisions,
            fact => fact.Method == decoyAction);
    }

    private static MethodId FindMethod(ProfileAnalysisExtraction extraction, string typeMetadataName, string methodName)
    {
        var type = Assert.Single(extraction.ProgramIndex.Types, candidate => candidate.MetadataName == typeMetadataName);
        return Assert.Single(extraction.ProgramIndex.Methods,
            candidate => candidate.Name == methodName && candidate.ContainingType == type.Id).Id;
    }

    private static string CollectFactIds(StructuralResultFactSet facts) => string.Join(
        "\n",
        facts.Factories
            .Select(fact => fact.Id.Value)
            .Concat(facts.Decisions.Select(fact => fact.Id.Value))
            .Order(StringComparer.Ordinal));

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
