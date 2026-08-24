using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Semantics;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.EntityFramework;
using Xunit;

namespace SeqDoc.Analysis.Tests.Persistence;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class EfCoreExtractionTests
{
    private const string FourFlowsFixtureRelativePath = "tests/fixtures/BehaviorDocumentation/FourFlows/FourFlows.csproj";
    private const string FourFlowsServiceMetadataName = "BehaviorDocumentation.FourFlows.Services.WidgetService";
    private const string GetMeaningFixtureRelativePath = "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj";
    private const string GetMeaningServiceMetadataName = "BehaviorDocumentation.GetMeaning.Services.GadgetService";

    [Fact]
    public async Task ExtractFourFlowsServiceProducesCompletePersistenceFacts()
    {
        var extraction = await ExtractSuccessfullyAsync(FourFlowsFixtureRelativePath);
        var framework = await ComposeAsync(extraction);

        var serviceType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == FourFlowsServiceMetadataName);
        var methods = extraction.ProgramIndex.Methods.Where(method => method.ContainingType == serviceType.Id).ToArray();

        // 1. GetByIdAsync - Query fact with AsNoTracking and Includes
        var getByIdMethod = Assert.Single(methods, method => method.Name == "GetByIdAsync");
        var getQueries = framework.Facts.OfType<EntityFrameworkQueryFact>()
            .Where(fact => fact.Method == getByIdMethod.Id)
            .ToArray();
        var getQuery = Assert.Single(getQueries);
        Assert.Equal("BehaviorDocumentation.FourFlows.Data.WidgetDbContext", getQuery.DbContextType);
        Assert.Equal("BehaviorDocumentation.FourFlows.Models.Widget", getQuery.EntityType);
        Assert.Equal(
            new[]
            {
                EntityFrameworkQueryOperatorKind.AsNoTracking,
                EntityFrameworkQueryOperatorKind.Include,
                EntityFrameworkQueryOperatorKind.Include,
                EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync,
            },
            getQuery.Chain.Select(item => item.OperatorKind));
        Assert.Equal(ComparisonOperatorKind.Equal, getQuery.PredicateOperator);
        Assert.NotNull(getQuery.PredicateOperation);

        // 2. CancelAsync - Query, State Assignment, RemoveRange mutation, SaveChangesAsync
        var cancelMethod = Assert.Single(methods, method => method.Name == "CancelAsync");
        var cancelQueries = framework.Facts.OfType<EntityFrameworkQueryFact>()
            .Where(fact => fact.Method == cancelMethod.Id)
            .ToArray();
        Assert.Single(cancelQueries);

        var cancelAssignments = extraction.NonGetSemanticFacts.StateAssignments
            .Where(fact => fact.Method == cancelMethod.Id)
            .ToArray();
        var cancelAssignment = Assert.Single(cancelAssignments);
        Assert.Equal("BehaviorDocumentation.FourFlows.Models.Widget.Status", cancelAssignment.TargetMember);
        Assert.Equal(StateAssignmentValueKind.EnumConstant, cancelAssignment.ValueKind);
        Assert.Equal("Cancelled", cancelAssignment.Value);

        var cancelMutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == cancelMethod.Id)
            .ToArray();
        Assert.Equal(2, cancelMutations.Length);
        Assert.Equal(EntityFrameworkMutationKind.RemoveRange, cancelMutations[0].MutationKind);
        Assert.Equal(EntityFrameworkMutationKind.SaveChangesAsync, cancelMutations[1].MutationKind);

        // 3. ReserveAsync - Query, Add mutation
        var reserveMethod = Assert.Single(methods, method => method.Name == "ReserveAsync");
        var reserveMutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == reserveMethod.Id)
            .ToArray();
        Assert.Contains(reserveMutations, fact => fact.MutationKind == EntityFrameworkMutationKind.Add);

        // 4. UpdateAsync - Query and complete mutation sequence
        var updateMethod = Assert.Single(methods, method => method.Name == "UpdateAsync");
        var updateMutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == updateMethod.Id)
            .OrderBy(fact => fact.SequenceOrdinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                EntityFrameworkMutationKind.RemoveRange,
                EntityFrameworkMutationKind.Clear,
                EntityFrameworkMutationKind.Add,
                EntityFrameworkMutationKind.SaveChangesAsync,
            },
            updateMutations.Select(mutation => mutation.MutationKind));
    }

    [Fact]
    public async Task ExtractGetMeaningServiceAdmitsExactQueriesAndRejectsLookalikes()
    {
        var extraction = await ExtractSuccessfullyAsync(GetMeaningFixtureRelativePath);
        var framework = await ComposeAsync(extraction);

        var serviceType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == GetMeaningServiceMetadataName);
        var methods = extraction.ProgramIndex.Methods.Where(method => method.ContainingType == serviceType.Id).ToArray();

        // 1. GetByIdAsync admits SingleOrDefaultAsync query with AsNoTracking and Includes
        var getByIdMethod = Assert.Single(methods, method => method.Name == "GetByIdAsync");
        var getByIdFact = Assert.Single(framework.Facts.OfType<EntityFrameworkQueryFact>(), fact => fact.Method == getByIdMethod.Id);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Data.GadgetDbContext", getByIdFact.DbContextType);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Models.Gadget", getByIdFact.EntityType);
        Assert.Equal(
            new[]
            {
                EntityFrameworkQueryOperatorKind.AsNoTracking,
                EntityFrameworkQueryOperatorKind.Include,
                EntityFrameworkQueryOperatorKind.Include,
                EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync,
            },
            getByIdFact.Chain.Select(item => item.OperatorKind));

        // 2. FindLookalikeAsync calls LookalikeSingleOrDefaultAsync and must be rejected fail-closed
        var lookalikeMethod = Assert.Single(methods, method => method.Name == "FindLookalikeAsync");
        var lookalikeQueries = framework.Facts.OfType<EntityFrameworkQueryFact>()
            .Where(fact => fact.Method == lookalikeMethod.Id)
            .ToArray();
        Assert.Empty(lookalikeQueries);

        // 3. FindFirstAsync lacks AsNoTracking and has unsupported chain shape so it produces no query fact
        var findFirstMethod = Assert.Single(methods, method => method.Name == "FindFirstAsync");
        var findFirstQueries = framework.Facts.OfType<EntityFrameworkQueryFact>()
            .Where(fact => fact.Method == findFirstMethod.Id)
            .ToArray();
        Assert.Empty(findFirstQueries);
    }

    [Fact]
    public async Task EfOperationSequenceAndMutationsPreserveSourceOrder()
    {
        var extraction = await ExtractSuccessfullyAsync(FourFlowsFixtureRelativePath);
        var serviceType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == FourFlowsServiceMetadataName);
        var cancelMethod = Assert.Single(extraction.ProgramIndex.Methods, method => method.ContainingType == serviceType.Id && method.Name == "CancelAsync");

        var querySequence = extraction.NonGetSemanticFacts.EfOperationSequence
            .Where(item => item.Method == cancelMethod.Id)
            .ToArray();
        var singleQuery = Assert.Single(querySequence);
        Assert.Equal(EfOperationSequenceKind.QueryTerminal, singleQuery.Kind);

        var mutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(item => item.Method == cancelMethod.Id)
            .OrderBy(item => item.SequenceOrdinal)
            .ToArray();
        Assert.Equal(2, mutations.Length);
        Assert.True(singleQuery.Ordinal < mutations[0].SequenceOrdinal);
        Assert.True(mutations[0].SequenceOrdinal < mutations[1].SequenceOrdinal);
    }

    [Fact]
    public async Task NonEfClassesAndLookalikeMethodsDoNotProducePersistenceFacts()
    {
        var extraction = await ExtractSuccessfullyAsync(FourFlowsFixtureRelativePath);
        var framework = await ComposeAsync(extraction);

        // Verify that methods on types not referencing DbContext/DbSet do not produce EF query or mutation facts
        var nonEfTypes = extraction.ProgramIndex.Types
            .Where(type => !type.MetadataName.Contains("DbContext", StringComparison.Ordinal)
                && !type.MetadataName.Contains("WidgetService", StringComparison.Ordinal))
            .Select(type => type.Id)
            .ToHashSet();

        var nonEfMethods = extraction.ProgramIndex.Methods
            .Where(method => nonEfTypes.Contains(method.ContainingType))
            .Select(method => method.Id)
            .ToHashSet();

        var efQueries = framework.Facts.OfType<EntityFrameworkQueryFact>()
            .Where(fact => nonEfMethods.Contains(fact.Method))
            .ToArray();
        Assert.Empty(efQueries);

        var efMutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => nonEfMethods.Contains(fact.Method))
            .ToArray();
        Assert.Empty(efMutations);
    }

    [Fact]
    public async Task OrdinaryDtoAndViewModelAssignmentsDoNotProducePersistedStateMutations()
    {
        var extraction = await ExtractSuccessfullyAsync(FourFlowsFixtureRelativePath);

        // In FourFlows, UpdateAsync assigns command.Label and command.Price to updated Widget,
        // and ProbeController contains local assignments.
        // Generic assignments exist as StateAssignmentSemanticFact, but they never invent EF entity mutation facts.
        var probeType = extraction.ProgramIndex.Types.FirstOrDefault(type => type.MetadataName.Contains("ProbeController", StringComparison.Ordinal));
        if (probeType is not null)
        {
            var probeMethods = extraction.ProgramIndex.Methods.Where(m => m.ContainingType == probeType.Id).Select(m => m.Id).ToHashSet();
            var probeMutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations.Where(m => probeMethods.Contains(m.Method)).ToArray();
            Assert.Empty(probeMutations);
        }

        // Verify that only exact DbSet Add, RemoveRange, Clear, and DbContext SaveChanges(Async) are mutation facts
        foreach (var mutation in extraction.NonGetSemanticFacts.EntityFrameworkMutations)
        {
            Assert.Contains(
                mutation.MutationKind,
                new[]
                {
                    EntityFrameworkMutationKind.Add,
                    EntityFrameworkMutationKind.RemoveRange,
                    EntityFrameworkMutationKind.Clear,
                    EntityFrameworkMutationKind.SaveChangesAsync,
                    EntityFrameworkMutationKind.SaveChanges,
                });
            Assert.True(!string.IsNullOrWhiteSpace(mutation.DbContextType) || !string.IsNullOrWhiteSpace(mutation.EntityType));
        }
    }

    [Fact]
    public async Task PersistenceFactsRetainEvidenceAndCertaintyWithoutLocalPaths()
    {
        var extraction = await ExtractSuccessfullyAsync(FourFlowsFixtureRelativePath);
        var framework = await ComposeAsync(extraction);
        var repoRoot = FindRepositoryRoot();

        var queries = framework.Facts.OfType<EntityFrameworkQueryFact>().ToArray();
        Assert.NotEmpty(queries);
        foreach (var query in queries)
        {
            Assert.NotEmpty(query.Evidence);
            Assert.All(query.Evidence, e => Assert.False(string.IsNullOrWhiteSpace(e.Artifact)));
            Assert.DoesNotContain(repoRoot, string.Join(";", query.Evidence.Select(e => e.Artifact)), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(CertaintyLevel.Exact, query.Certainty);
        }

        var mutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations;
        Assert.NotEmpty(mutations);
        foreach (var mutation in mutations)
        {
            Assert.NotEmpty(mutation.Evidence);
            Assert.All(mutation.Evidence, e => Assert.False(string.IsNullOrWhiteSpace(e.Artifact)));
            Assert.DoesNotContain(repoRoot, string.Join(";", mutation.Evidence.Select(e => e.Artifact)), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(CertaintyLevel.Exact, mutation.Certainty);
        }
    }

    [Fact]
    public async Task PersistenceExtractionIsDeterministicAndConfinedToProfile()
    {
        var first = await ExtractSuccessfullyAsync(FourFlowsFixtureRelativePath);
        var second = await ExtractSuccessfullyAsync(FourFlowsFixtureRelativePath);

        Assert.Equal(first.ProgramIndex.IndexFingerprint, second.ProgramIndex.IndexFingerprint);
        Assert.Equal(first.ProgramIndex.Profile.Id, second.ProgramIndex.Profile.Id);

        var firstMutations = first.NonGetSemanticFacts.EntityFrameworkMutations;
        var secondMutations = second.NonGetSemanticFacts.EntityFrameworkMutations;
        Assert.Equal(firstMutations.Length, secondMutations.Length);
        for (int i = 0; i < firstMutations.Length; i++)
        {
            Assert.Equal(firstMutations[i].Id, secondMutations[i].Id);
            Assert.Equal(firstMutations[i].MutationKind, secondMutations[i].MutationKind);
            Assert.Equal(firstMutations[i].SequenceOrdinal, secondMutations[i].SequenceOrdinal);
            Assert.Equal(firstMutations[i].DbContextType, secondMutations[i].DbContextType);
            Assert.Equal(firstMutations[i].EntityType, secondMutations[i].EntityType);
        }

        var firstAssignments = first.NonGetSemanticFacts.StateAssignments;
        var secondAssignments = second.NonGetSemanticFacts.StateAssignments;
        Assert.Equal(firstAssignments.Length, secondAssignments.Length);
        for (int i = 0; i < firstAssignments.Length; i++)
        {
            Assert.Equal(firstAssignments[i].Id, secondAssignments[i].Id);
            Assert.Equal(firstAssignments[i].TargetMember, secondAssignments[i].TargetMember);
            Assert.Equal(firstAssignments[i].Value, secondAssignments[i].Value);
            Assert.Equal(firstAssignments[i].SequenceOrdinal, secondAssignments[i].SequenceOrdinal);
        }
    }

    [Fact]
    public async Task ExtractGetMeaningAdmitsSupportedSynchronousSaveChanges()
    {
        var extraction = await ExtractSuccessfullyAsync(GetMeaningFixtureRelativePath);

        var serviceType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == GetMeaningServiceMetadataName);
        var methods = extraction.ProgramIndex.Methods.Where(method => method.ContainingType == serviceType.Id).ToArray();

        // CreateWithSyncSave produces exact SaveChanges synchronous mutation fact
        var syncSaveMethod = Assert.Single(methods, method => method.Name == "CreateWithSyncSave");
        var syncMutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == syncSaveMethod.Id)
            .OrderBy(fact => fact.SequenceOrdinal)
            .ToArray();
        Assert.Equal(2, syncMutations.Length);
        Assert.Equal(EntityFrameworkMutationKind.Add, syncMutations[0].MutationKind);
        Assert.Equal(EntityFrameworkMutationKind.SaveChanges, syncMutations[1].MutationKind);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Data.GadgetDbContext", syncMutations[1].DbContextType);
        Assert.Equal(CertaintyLevel.Exact, syncMutations[1].Certainty);
    }

    [Fact]
    public async Task RawSqlMethodsDoNotProduceTrackedMutationsOrSaveEdges()
    {
        var extraction = await ExtractSuccessfullyAsync(GetMeaningFixtureRelativePath);
        var serviceType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == GetMeaningServiceMetadataName);
        var rawSqlMethod = Assert.Single(extraction.ProgramIndex.Methods, method => method.ContainingType == serviceType.Id && method.Name == "RawSqlProbeAsync");

        var rawMutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == rawSqlMethod.Id)
            .ToArray();
        Assert.Empty(rawMutations);

        var observations = extraction.NonGetSemanticFacts.SourceObservations
            .Where(fact => fact.Method == rawSqlMethod.Id)
            .ToArray();
        Assert.Empty(observations);
    }

    [Fact]
    public async Task ExtractGetMeaningMultiEntityServiceProducesDistinctEntityMutations()
    {
        var extraction = await ExtractSuccessfullyAsync(GetMeaningFixtureRelativePath);
        var serviceType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == GetMeaningServiceMetadataName);
        var methods = extraction.ProgramIndex.Methods.Where(method => method.ContainingType == serviceType.Id).ToArray();

        var multiEntityMethod = Assert.Single(methods, method => method.Name == "CreateMultiEntityAsync");
        var multiMutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == multiEntityMethod.Id)
            .OrderBy(fact => fact.SequenceOrdinal)
            .ToArray();

        Assert.Equal(3, multiMutations.Length);
        Assert.Equal(EntityFrameworkMutationKind.Add, multiMutations[0].MutationKind);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Models.Gadget", multiMutations[0].EntityType);

        Assert.Equal(EntityFrameworkMutationKind.Add, multiMutations[1].MutationKind);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Models.Category", multiMutations[1].EntityType);

        Assert.Equal(EntityFrameworkMutationKind.SaveChangesAsync, multiMutations[2].MutationKind);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Data.GadgetDbContext", multiMutations[2].DbContextType);
    }

    [Fact]
    public async Task LookalikeFakeDbContextAndFakeRepositoryAreRejectedFailClosed()
    {
        var extraction = await ExtractSuccessfullyAsync(GetMeaningFixtureRelativePath);
        var framework = await ComposeAsync(extraction);

        var lookalikeTypes = extraction.ProgramIndex.Types
            .Where(type => type.MetadataName.Contains("FakeDbContext", StringComparison.Ordinal)
                || type.MetadataName.Contains("FakeRepository", StringComparison.Ordinal)
                || type.MetadataName.Contains("LookalikeCaller", StringComparison.Ordinal)
                || type.MetadataName.Contains("QueryableLookalikes", StringComparison.Ordinal))
            .Select(type => type.Id)
            .ToHashSet();

        var lookalikeMethods = extraction.ProgramIndex.Methods
            .Where(method => lookalikeTypes.Contains(method.ContainingType))
            .Select(method => method.Id)
            .ToHashSet();

        var queries = framework.Facts.OfType<EntityFrameworkQueryFact>()
            .Where(fact => lookalikeMethods.Contains(fact.Method))
            .ToArray();
        Assert.Empty(queries);

        var mutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => lookalikeMethods.Contains(fact.Method))
            .ToArray();
        Assert.Empty(mutations);
    }

    private static async Task<ProfileAnalysisExtraction> ExtractSuccessfullyAsync(string fixturePath)
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, fixturePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(fixturePath, "Release", "net10.0"));
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
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
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
