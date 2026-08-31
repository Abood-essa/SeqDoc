using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.EntityFramework;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class EntityFramework6ProjectionTests
{
    private const string Fixture = "tests/fixtures/PassC/EntityFramework6Edmx/EntityFramework6Edmx.csproj";
    private static readonly string[] ExpectedMutationKinds = ["Add", "SaveChanges"];

    [Fact]
    public async Task Ef6FactsAndEdmxIdentityAreOnePerSourceAndRepeatable()
    {
        var first = await Extract();
        var second = await Extract();
        var firstFramework = await Compose(first);
        var secondFramework = await Compose(second);

        var firstFacts = firstFramework.Facts.Where(f => f is EntityFrameworkQueryFact or EntityFrameworkMutationFact or EntityFrameworkEdmxMetadataFact).ToArray();
        var secondFacts = secondFramework.Facts.Where(f => f is EntityFrameworkQueryFact or EntityFrameworkMutationFact or EntityFrameworkEdmxMetadataFact).ToArray();

        Assert.True(firstFacts.Length >= 6);
        Assert.Equal(firstFacts.Select(f => f.Id.Value), secondFacts.Select(f => f.Id.Value));
        var execute = Assert.Single(first.ProgramIndex.Methods, method => method.Name == "Execute");
        Assert.Equal(2, firstFramework.Facts.OfType<EntityFrameworkQueryFact>().Count(fact => fact.Method == execute.Id));
        var reassigned = Assert.Single(first.ProgramIndex.Methods, method => method.Name == "ReassignedTransactions");
        Assert.DoesNotContain(firstFramework.Facts.OfType<EntityFrameworkQueryFact>(), fact => fact.Method == reassigned.Id);
        var metadata = Assert.Single(firstFacts.OfType<EntityFrameworkEdmxMetadataFact>());
        Assert.Equal("tests/fixtures/PassC/EntityFramework6Edmx/Model.edmx", metadata.RepositoryRelativePath);
        Assert.True(metadata.HasFunctionImport);
        Assert.NotEmpty(metadata.ContentFingerprint);
        Assert.All(firstFacts, fact => Assert.NotEmpty(fact.Evidence));
        Assert.Equal(ExpectedMutationKinds, firstFacts.OfType<EntityFrameworkMutationFact>()
            .Where(f => f.Method == execute.Id)
            .OrderBy(f => f.SequenceOrdinal).Select(f => f.MutationKind.ToString()).ToArray());
    }

    [Fact]
    public async Task LocalWhereCountRequiresOneUnconditionalExactEf6Definition()
    {
        var extraction = await Extract();
        var framework = await Compose(extraction);
        var methods = extraction.ProgramIndex.Methods
            .Where(method => method.Name is "LocalWhereCount" or "ConditionalLocalWhereCount" or "LoopLocalWhereCount" or "ForeignLocalWhereCount" or "UnsupportedLocalWhereCount")
            .ToDictionary(method => method.Name, StringComparer.Ordinal);
        var queries = framework.Facts.OfType<EntityFrameworkQueryFact>().ToArray();

        var positive = Assert.Single(queries, fact => fact.Method == methods["LocalWhereCount"].Id);
        Assert.Equal(EntityFrameworkQueryOperatorKind.Count, positive.Chain[^1].OperatorKind);
        Assert.Equal(EntityFrameworkQueryOperatorKind.Where, positive.Chain[0].OperatorKind);
        var underlying = positive.Evidence.SelectMany(evidence => evidence.UnderlyingEvidence).ToArray();
        Assert.True(underlying.Length >= 2, "The terminal and local initializer/Where must both remain evidence-backed.");
        Assert.Equal(underlying.Length, underlying.Select(evidence => evidence.Id).Distinct().Count());

        foreach (var rejected in new[] { "ConditionalLocalWhereCount", "LoopLocalWhereCount", "ForeignLocalWhereCount", "UnsupportedLocalWhereCount" })
        {
            Assert.DoesNotContain(queries, fact => fact.Method == methods[rejected].Id);
        }
    }

    [Fact]
    public async Task DbSetOperationsRequireACompilerProvenDerivedDbContextOwner()
    {
        var extraction = await Extract();
        var framework = await Compose(extraction);
        var methods = extraction.ProgramIndex.Methods
            .Where(method => method.Name is "NonContextDbSetDirect" or "NonContextDbSetRecovered"
                or "NonContextDbSetLocal" or "NonContextDbSetAdd")
            .Select(method => method.Id)
            .ToHashSet();

        Assert.DoesNotContain(framework.Facts.OfType<EntityFrameworkQueryFact>(), fact => methods.Contains(fact.Method));
        Assert.DoesNotContain(framework.Facts.OfType<EntityFrameworkMutationFact>(), fact => methods.Contains(fact.Method));
    }

    [Fact]
    public async Task CapturedLambdaAndLocalFunctionDefinitionsAreWithheld()
    {
        var extraction = await Extract();
        var framework = await Compose(extraction);
        var methods = extraction.ProgramIndex.Methods
            .Where(method => method.Name is "CapturedLambdaLocalWhereCount" or "CapturedLocalFunctionWhereCount")
            .Select(method => method.Id)
            .ToHashSet();

        Assert.DoesNotContain(framework.Facts.OfType<EntityFrameworkQueryFact>(), fact => methods.Contains(fact.Method));
    }

    [Fact]
    public async Task QueryableIdentitiesCarryTheMeasuredAssemblyTokenAndWhereItemsStayOrdered()
    {
        var extraction = await Extract();
        var execute = Assert.Single(extraction.ProgramIndex.Methods, method => method.Name == "Execute");
        var multiple = Assert.Single(extraction.ProgramIndex.Methods, method => method.Name == "MultipleWhereCount");
        var operations = extraction.Operations.Where(operation => operation.Method == execute.Id || operation.Method == multiple.Id).ToArray();
        var queryable = operations.Where(operation => operation.TargetIdentity?.ContainingMetadataType == "System.Linq.Queryable").ToArray();
        Assert.NotEmpty(queryable);
        var token = typeof(FrameworkMethodIdentity).GetProperty("AssemblyPublicKeyToken");
        Assert.NotNull(token);
        Assert.Contains(queryable, operation => (string?)token!.GetValue(operation.TargetIdentity) == "b03f5f7f11d50a3a");
        Assert.Contains(operations, operation => operation.TargetIdentity?.AssemblyIdentity == "EntityFramework"
            && (string?)token!.GetValue(operation.TargetIdentity) == "b77a5c561934e089");

        var framework = await Compose(extraction);
        var fact = Assert.Single(framework.Facts.OfType<EntityFrameworkQueryFact>(), candidate => candidate.Method == multiple.Id);
        Assert.Equal(
            new[] { EntityFrameworkQueryOperatorKind.Where, EntityFrameworkQueryOperatorKind.Where, EntityFrameworkQueryOperatorKind.Count },
            fact.Chain.Select(item => item.OperatorKind));
        Assert.Equal(3, fact.Chain.Select(item => item.Operation).Distinct().Count());
        Assert.NotEmpty(fact.Evidence);
    }

    [Fact]
    public async Task EdmxExtractionObservesCancellationInsteadOfUsingAPathOnlyFallback()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(
                FindRoot(),
                Path.Combine(FindRoot(), Fixture.Replace('/', Path.DirectorySeparatorChar)),
                CompilationProfile.Create(Fixture, "Release", "net9.0")),
            cancellation.Token);
        Assert.Equal(ApplicationOutcome.Cancelled, result.Outcome);
    }

    private static async Task<ProfileAnalysisExtraction> Extract()
    {
        var root = FindRoot();
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, Path.Combine(root, Fixture.Replace('/', Path.DirectorySeparatorChar)), CompilationProfile.Create(Fixture, "Release", "net9.0")), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(";", result.Diagnostics.Select(d => d.TechnicalCause)));
        return result.Value!;
    }

    private static async Task<FrameworkAnalysisResult> Compose(ProfileAnalysisExtraction extraction) => await new FrameworkModelHost([new EntityFramework6Model()]).AnalyzeAsync(
        new FrameworkAnalysisRequest(new FrameworkDetectionContext(extraction.ProgramIndex.Profile, extraction.ProgramIndex), new FrameworkAnalysisContext(extraction.ProgramIndex.Profile, extraction.ProgramIndex), extraction.Operations, extraction.Symbols), CancellationToken.None);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
