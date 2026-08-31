using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.EntityFramework;
using SeqDoc.Rendering.Markdown;
using SeqDoc.Testing;
using Xunit;

namespace SeqDoc.AcceptanceTests;

public sealed class EntityFramework6EdmxProductionTests
{
    private const string Fixture = "tests/fixtures/PassC/EntityFramework6Edmx/EntityFramework6Edmx.csproj";
    private const string ExternalProject = "CreditTransfer-om/CreditTransferEngine/CreditTransferEngine.csproj";
    private const string ExpectedExternalHead = "02b82a5115ef6e2d138c70670f28b959fb646f6e";

    [Fact]
    public async Task ExactEf6OperationsAndEdmxMetadataReachGeneratedDocumentation()
    {
        if (Environment.GetEnvironmentVariable("SEQDOC_EF6_EXTERNAL_CHILD") == "1")
        {
            await AssertCreditTransferProducerAsync();
            return;
        }

        string root = FindRepositoryRoot();
        var profile = CompilationProfile.Create(Fixture, "Release", "net9.0");
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, Path.Combine(root, Fixture.Replace('/', Path.DirectorySeparatorChar)), profile),
            CancellationToken.None);
        Assert.True(extraction.IsSuccess, string.Join("; ", extraction.Diagnostics.Select(d => d.TechnicalCause)));

        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, string.Join("; ", behavior.Diagnostics.Select(d => d.TechnicalCause)));

        var framework = await new FrameworkModelHost([new EntityFrameworkQueryModel(), new EntityFramework6Model()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols), CancellationToken.None);
        var rootMethod = Assert.Single(extraction.Value.ProgramIndex.Methods,
            method => method.Name == "Execute" && method.ContainingType ==
                Assert.Single(extraction.Value.ProgramIndex.Types, type => type.MetadataName == "InitialRedTest.Operations").Id);

        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            extraction.Value.ProgramIndex,
            behavior.Value!,
            framework,
            extraction.Value.SemanticFacts,
            extraction.Value.DependencyInjectionFacts,
            extraction.Value.StructuralResultFacts,
            extraction.Value.NonGetSemanticFacts,
            ConfiguredRoots: [rootMethod.Id]));
        var graph = Assert.Single(graphs.Graphs, candidate => candidate.RootMethod == rootMethod.Id);
        var plan = DocumentationPlanner.Plan(graph);
        string markdown = MarkdownRenderer.RenderDocument(plan.Wording, plan.Diagram);
        string mermaid = MermaidRenderer.Render(plan.Diagram);

        var methodNames = extraction.Value.ProgramIndex.Methods
            .ToDictionary(method => method.Id, method => method.Name);
        var queryFacts = framework.Facts.OfType<EntityFrameworkQueryFact>().ToArray();
        Assert.Equal(4, queryFacts.Length);
        Assert.All(queryFacts, fact =>
        {
            Assert.NotEmpty(fact.Chain);
            Assert.NotEmpty(fact.Evidence);
            Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        });
        Assert.Collection(
            queryFacts.Where(fact => methodNames[fact.Method] == "Execute"),
            first => Assert.Equal([EntityFrameworkQueryOperatorKind.FirstOrDefault], first.Chain.Select(item => item.OperatorKind)),
            count => Assert.Equal([EntityFrameworkQueryOperatorKind.Count], count.Chain.Select(item => item.OperatorKind)));
        Assert.Equal(
            [EntityFrameworkQueryOperatorKind.Where, EntityFrameworkQueryOperatorKind.Count],
            Assert.Single(queryFacts, fact => methodNames[fact.Method] == "LocalWhereCount").Chain.Select(item => item.OperatorKind));
        Assert.Equal(
            [EntityFrameworkQueryOperatorKind.Where, EntityFrameworkQueryOperatorKind.Where, EntityFrameworkQueryOperatorKind.Count],
            Assert.Single(queryFacts, fact => methodNames[fact.Method] == "MultipleWhereCount").Chain.Select(item => item.OperatorKind));
        Assert.Contains(framework.Facts.OfType<EntityFrameworkMutationFact>(), fact =>
            fact.MutationKind == EntityFrameworkMutationKind.Add && fact.DbContextType == "InitialRedTest.RecordsContext");
        Assert.DoesNotContain(framework.Facts.OfType<EntityFrameworkQueryFact>(), fact =>
            fact.Method.Value.Contains("Lookalikes", StringComparison.Ordinal));
        Assert.Contains(framework.Facts.OfType<EntityFrameworkEdmxMetadataFact>(), fact =>
            fact.HasFunctionImport && fact.HasStoreFunction && !string.IsNullOrWhiteSpace(fact.ContentFingerprint));
        Assert.Contains("finds at most one", markdown, StringComparison.Ordinal);
        Assert.Contains("count", markdown, StringComparison.Ordinal);
        Assert.Contains("Find at most one Record", plan.Diagram.Messages.Select(message => message.Label));
        Assert.Contains("Count Records", plan.Diagram.Messages.Select(message => message.Label));
        Assert.Contains("Find at most one Record", mermaid, StringComparison.Ordinal);
        Assert.Contains("Count Records", mermaid, StringComparison.Ordinal);
        Assert.Contains("Add", mermaid, StringComparison.Ordinal);
        Assert.Contains("SaveChanges", mermaid, StringComparison.Ordinal);
        Assert.Contains("EDMX metadata boundary: tests/fixtures/PassC/EntityFramework6Edmx/Model.edmx; FunctionImport declaration present: True; store-function declaration present: True; unsupported declaration-only metadata boundary; database mapping and runtime behavior are not inferred.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("DeclaredStoreFunction", markdown + mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("execution", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invocation", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rows", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commit", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transaction succeeded", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transaction committed", markdown, StringComparison.OrdinalIgnoreCase);
        await RunCreditTransferProducerInSelectedProcessAsync(root);
    }

    private static async Task RunCreditTransferProducerInSelectedProcessAsync(string repositoryRoot)
    {
        string corpus = ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.Provided).Root;
        string testProject = Path.Combine(repositoryRoot, "tests", "SeqDoc.AcceptanceTests", "SeqDoc.AcceptanceTests.csproj");
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"test \"{testProject}\" -c Release --no-build --filter \"FullyQualifiedName~EntityFramework6EdmxProductionTests.ExactEf6OperationsAndEdmxMetadataReachGeneratedDocumentation\"",
            WorkingDirectory = corpus,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["SEQDOC_EF6_EXTERNAL_CHILD"] = "1";
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the external EF6 verification process.");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
    }

    private static async Task AssertCreditTransferProducerAsync()
    {
        string corpus = ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.Provided).Root;
        string project = Path.Combine(corpus, ExternalProject.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(project), project);
        Assert.Equal(ExpectedExternalHead, GitHead(Path.GetDirectoryName(project)!));

        string relativeProject = ExternalProject;
        var profile = CompilationProfile.Create(relativeProject, "Release", "net9.0");
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            // Keep both producer phases in this process's repository-selected MSBuild registration.
            // The source project remains the pinned external checkout; only SDK selection is anchored
            // to the already-verified SeqDoc repository phase above.
            new CompilationAnalysisRequest(corpus, project, profile), CancellationToken.None);
        Assert.True(extraction.IsSuccess, Diagnostics(extraction.Diagnostics));
        var artifacts = extraction.Value!;
        Assert.Equal(profile.Id, artifacts.ProgramIndex.Profile.Id);
        Assert.NotEmpty(artifacts.ProgramIndex.IndexFingerprint);

        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(artifacts.ProgramIndex, artifacts.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, Diagnostics(behavior.Diagnostics));
        var framework = await new FrameworkModelHost([new EntityFrameworkQueryModel(), new EntityFramework6Model()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, artifacts.ProgramIndex),
                new FrameworkAnalysisContext(profile, artifacts.ProgramIndex),
                artifacts.Operations, artifacts.Symbols), CancellationToken.None);

        var transactionType = Assert.Single(artifacts.ProgramIndex.Types, type => type.MetadataName == "CreditTransferEngine.BusinessLogic.TransactionManager");
        var loggerType = Assert.Single(artifacts.ProgramIndex.Types, type => type.MetadataName == "CreditTransferEngine.BusinessLogic.Logger");
        var roots = new[]
        {
            Assert.Single(artifacts.ProgramIndex.Methods, method => method.ContainingType == transactionType.Id && method.Name == "GetDailyTransferCount"),
            Assert.Single(artifacts.ProgramIndex.Methods, method => method.ContainingType == loggerType.Id && method.Name == "LogActionToDB"),
        };
        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile, artifacts.ProgramIndex, behavior.Value!, framework, artifacts.SemanticFacts,
            artifacts.DependencyInjectionFacts, artifacts.StructuralResultFacts, artifacts.NonGetSemanticFacts,
            ConfiguredRoots: roots.Select(method => method.Id).ToImmutableArray()));
        Assert.Equal(profile.Id, graphs.Profile.Id);
        Assert.Equal(artifacts.ProgramIndex.IndexFingerprint, graphs.ProgramIndexFingerprint);
        Assert.Equal(roots.Length, graphs.Graphs.Length);
        Assert.All(graphs.Graphs.SelectMany(graph => graph.Nodes), item => Assert.NotEmpty(item.Evidence));
        Assert.All(graphs.Graphs.SelectMany(graph => graph.Edges), item => Assert.NotEmpty(item.Evidence));
        Assert.All(framework.Facts, fact => Assert.NotEmpty(fact.Evidence));

        var countGraph = Assert.Single(graphs.Graphs, graph => graph.RootMethod == roots[0].Id);
        var logGraph = Assert.Single(graphs.Graphs, graph => graph.RootMethod == roots[1].Id);
        var countPlan = DocumentationPlanner.Plan(countGraph);
        var logPlan = DocumentationPlanner.Plan(logGraph);
        string countMarkdown = MarkdownRenderer.RenderDocument(countPlan.Wording, countPlan.Diagram);
        string countMermaid = MermaidRenderer.Render(countPlan.Diagram);
        string logMarkdown = MarkdownRenderer.RenderDocument(logPlan.Wording, logPlan.Diagram);
        string logMermaid = MermaidRenderer.Render(logPlan.Diagram);
        var countFacts = framework.Facts.OfType<EntityFrameworkQueryFact>()
            .Where(fact => fact.Method == roots[0].Id
                && fact.Chain.Any(item => item.OperatorKind is EntityFrameworkQueryOperatorKind.Count or EntityFrameworkQueryOperatorKind.CountAsync))
            .ToArray();
        Assert.Equal(2, countFacts.Length);
        Assert.All(countFacts, fact =>
        {
            Assert.StartsWith("System.Data.Entity.DbSet<", fact.DbSetMemberType, StringComparison.Ordinal);
            Assert.NotEmpty(fact.Evidence);
        });
        var countOperations = artifacts.Operations
            .Where(operation => operation.Method == roots[0].Id
                && operation.TargetIdentity is { MethodMetadataName: "Count" })
            .ToArray();
        Assert.Equal(2, countOperations.Length);
        Assert.All(countOperations, operation =>
        {
            var identity = operation.TargetIdentity!;
            Assert.Equal("System.Linq.Queryable", identity.AssemblyIdentity);
            Assert.Equal("9.0.0.0", identity.AssemblyVersion);
            Assert.Equal("System.Linq.Queryable", identity.ContainingMetadataType);
            Assert.Equal("System.Int32", identity.ReturnType);
            Assert.NotNull(operation.QueryChain);
            Assert.StartsWith("System.Data.Entity.DbSet<", operation.QueryChain!.ReceiverType, StringComparison.Ordinal);
            Assert.All(operation.QueryChain.Steps, step =>
            {
                Assert.Equal("Where", step.TargetIdentity.MethodMetadataName);
                Assert.DoesNotContain("TSource", string.Join("|", step.TargetIdentity.Parameters.Select(parameter => parameter.FullyQualifiedType)), StringComparison.Ordinal);
            });
        });
        Assert.Contains("counts Transactions", countMarkdown, StringComparison.Ordinal);
        Assert.Contains(countGraph.Diagnostics, diagnostic => diagnostic.Code == "SC011");
        Assert.Contains(countGraph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        Assert.Contains(countPlan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP002");
        Assert.DoesNotContain("Count Transactions", countMermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Count Transactions", countPlan.Diagram.Messages.Select(message => message.Label));
        var logMutations = framework.Facts.OfType<EntityFrameworkMutationFact>()
            .Where(fact => fact.Method == roots[1].Id).OrderBy(fact => fact.SequenceOrdinal).ToArray();
        Assert.Equal([EntityFrameworkMutationKind.Add, EntityFrameworkMutationKind.SaveChanges], logMutations.Select(fact => fact.MutationKind));
        Assert.Contains("counts Transactions", countMarkdown, StringComparison.Ordinal);
        Assert.Contains("Add Log", logMarkdown, StringComparison.Ordinal);
        Assert.Contains("calls SaveChanges", logMarkdown, StringComparison.Ordinal);
        Assert.Contains("Add Log", logMermaid, StringComparison.Ordinal);
        Assert.Contains("SaveChanges", logMermaid, StringComparison.Ordinal);

        string rendered = countMarkdown + logMarkdown + logMermaid;
        foreach (var forbidden in new[]
                 {
                     "database execution", "database success", " rows affected", "transaction committed",
                     "transaction succeeded", "stored procedure execution",
                 })
        {
            Assert.DoesNotContain(forbidden, rendered, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Diagnostics(IEnumerable<SeqDoc.Core.Diagnostics.AnalysisDiagnostic> diagnostics)
        => string.Join("; ", diagnostics.Select(diagnostic => diagnostic.TechnicalCause));

    private static string GitHead(string workingDirectory)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start git.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        return output;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
