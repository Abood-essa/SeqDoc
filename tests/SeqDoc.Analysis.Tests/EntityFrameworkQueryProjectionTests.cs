using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.EntityFramework;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class EntityFrameworkQueryProjectionTests
{
    private const string FixtureName = "GetMeaning";
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj";
    private const string ServiceMetadataName = "BehaviorDocumentation.GetMeaning.Services.GadgetService";
    private const string GetByIdMethodName = "GetByIdAsync";

    [Fact]
    public async Task ExactGetQueryAdmitsSingleOrDefaultAsyncAndRejectsUnsupportedShapes()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var framework = await ComposeAsync(extraction);

        var efOperations = extraction.Operations
            .Where(operation => operation.TargetIdentity is { } target
                && target.ContainingMetadataType.EndsWith("EntityFrameworkQueryableExtensions", StringComparison.Ordinal))
            .ToArray();
        Assert.True(
            efOperations.Length > 0,
            $"No EF operations projected. Applied models: {string.Join(",", framework.AppliedModels.Select(model => model.ModelId))}; diagnostics: {string.Join(";", framework.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Summary}"))}");
        Assert.Contains(framework.AppliedModels, model => model.ModelId == EntityFrameworkQueryModel.ModelIdValue);
        Assert.True(
            efOperations.Any(operation => operation.QueryChain is not null),
            $"Query chains projected: {efOperations.Count(operation => operation.QueryChain is not null)} of {efOperations.Length}");

        var serviceMethod = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == GetByIdMethodName && method.ContainingType ==
                Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == ServiceMetadataName).Id);
        var facts = framework.Facts.OfType<EntityFrameworkQueryFact>().ToArray();

        var query = Assert.Single(facts, fact => fact.Method == serviceMethod.Id);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Data.GadgetDbContext", query.DbContextType);
        Assert.Equal("Microsoft.EntityFrameworkCore.DbSet<BehaviorDocumentation.GetMeaning.Models.Gadget>", query.DbSetMemberType);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Models.Gadget", query.EntityType);
        Assert.Equal(ComparisonOperatorKind.Equal, query.PredicateOperator);
        Assert.NotNull(query.PredicateOperation);
        Assert.Equal(
            new[]
            {
                EntityFrameworkQueryOperatorKind.AsNoTracking,
                EntityFrameworkQueryOperatorKind.Include,
                EntityFrameworkQueryOperatorKind.Include,
                EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync,
            },
            query.Chain.Select(item => item.OperatorKind));
        Assert.Equal(
            "BehaviorDocumentation.GetMeaning.Models.Gadget.Parts",
            query.Chain.Single(item => item.OperatorKind == EntityFrameworkQueryOperatorKind.Include
                && item.NavigationMember?.EndsWith(".Parts", StringComparison.Ordinal) == true).NavigationMember);
        Assert.Equal(
            "BehaviorDocumentation.GetMeaning.Models.Gadget.Category",
            query.Chain.Single(item => item.OperatorKind == EntityFrameworkQueryOperatorKind.Include
                && item.NavigationMember?.EndsWith(".Category", StringComparison.Ordinal) == true).NavigationMember);

        // Unsupported terminals, unsupported chain operators, non-equality predicates, and lookalike
        // helpers never produce an EF query fact; the only other admitted fact is the Token query.
        var unsupportedMethods = extraction.ProgramIndex.Methods
            .Where(method => method.Name is "FindFirstAsync" or "FindByLabelAsync" or "FindLookalikeAsync")
            .Select(method => method.Id)
            .ToHashSet();
        Assert.DoesNotContain(facts, fact => unsupportedMethods.Contains(fact.Method));
        Assert.Equal(2, facts.Length);
    }

    [Fact]
    public async Task ProjectedChainIsOrderedRepeatableAndLinksEqualityPredicate()
    {
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();
        var firstFramework = await ComposeAsync(first);
        var secondFramework = await ComposeAsync(second);

        var serviceMethod = Assert.Single(first.ProgramIndex.Methods,
            method => method.Name == GetByIdMethodName && method.ContainingType ==
                Assert.Single(first.ProgramIndex.Types, type => type.MetadataName == ServiceMetadataName).Id);
        var firstFact = Assert.Single(firstFramework.Facts.OfType<EntityFrameworkQueryFact>(), fact => fact.Method == serviceMethod.Id);
        var secondFact = Assert.Single(secondFramework.Facts.OfType<EntityFrameworkQueryFact>(), fact => fact.Method == serviceMethod.Id);
        Assert.Equal(firstFact.Id, secondFact.Id);
        Assert.Equal(
            firstFact.Chain.Select(item => $"{item.OperatorKind}:{item.Operation.Value}"),
            secondFact.Chain.Select(item => $"{item.OperatorKind}:{item.Operation.Value}"));

        // The predicate anchor joins to the accepted contract comparison semantic fact for the same method and
        // operation, proving the equality predicate is compiler-proven rather than guessed.
        var comparison = Assert.Single(first.SemanticFacts.Comparisons,
            fact => fact.Method == serviceMethod.Id && fact.Operation == firstFact.PredicateOperation);
        Assert.Equal(ComparisonOperatorKind.Equal, comparison.Operator);

        Assert.Equal(
            string.Join("\n", firstFramework.Facts.OfType<EntityFrameworkQueryFact>().Select(fact => fact.Id.Value).Order(StringComparer.Ordinal)),
            string.Join("\n", secondFramework.Facts.OfType<EntityFrameworkQueryFact>().Select(fact => fact.Id.Value).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task ProjectedFactsRetainSourceEvidenceWithoutCheckoutPaths()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var framework = await ComposeAsync(extraction);
        var facts = framework.Facts.OfType<EntityFrameworkQueryFact>().ToArray();
        Assert.NotEmpty(facts);

        foreach (var fact in facts)
        {
            Assert.NotEmpty(fact.Evidence);
            Assert.All(fact.Evidence, item => Assert.False(string.IsNullOrWhiteSpace(item.Artifact)));
            Assert.DoesNotContain(FindRepositoryRoot(), string.Join("\n", fact.Evidence.Select(item => item.Artifact)), StringComparison.OrdinalIgnoreCase);
            Assert.True(fact.Certainty >= fact.Evidence.Max(item => item.Certainty));
        }
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

    private static async Task<FrameworkAnalysisResult> ComposeAsync(ProfileAnalysisExtraction extraction)
    {
        var host = new FrameworkModelHost([new EntityFrameworkQueryModel()]);
        return await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(extraction.ProgramIndex.Profile, extraction.ProgramIndex),
                new FrameworkAnalysisContext(extraction.ProgramIndex.Profile, extraction.ProgramIndex),
                extraction.Operations,
                extraction.Symbols),
            CancellationToken.None);
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
