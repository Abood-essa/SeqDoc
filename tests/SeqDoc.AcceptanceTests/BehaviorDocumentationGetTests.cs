using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.FrameworkModels.EntityFramework;
using Xunit;

namespace SeqDoc.AcceptanceTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BehaviorDocumentationGetGroup
{
    public const string Name = "Translation alpha Get";
}

[Collection(BehaviorDocumentationGetGroup.Name)]
public sealed class BehaviorDocumentationGetTests
{
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj";
    private const string ExternalTicketReservationRoot = "samples/Provided/TicketReservation-Solution";
    private const string ExternalTicketReservationTarget = "TicketReservation.Api/TicketReservation.Api.csproj";

    [Fact]
    public async Task GetMeaningFixtureProducesEvidenceBackedGetScenarioWithBothOutcomes()
    {
        var root = FindRepositoryRoot();
        var target = Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var profile = CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0");

        var first = await BuildScenarioGraphsAsync(root, target, profile);
        var second = await BuildScenarioGraphsAsync(root, target, profile);

        // Repeated analysis yields identical scenario projections and unchanged extraction input
        // fingerprints, proving the new companion collectors never mutate accepted extraction.
        Assert.Equal(first.DebugProjection, second.DebugProjection);
        var get = Assert.Single(first.Graphs, graph => graph.OperationKey == "GET api/Gadgets/{id}");
        Assert.Equal("GET api/Gadgets/{id}", get.OperationKey);

        var query = Assert.Single(get.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.Contains("SingleOrDefaultAsync", query.Detail, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking", query.Detail, StringComparison.Ordinal);
        Assert.Equal(CertaintyLevel.Exact, query.Certainty);

        var outcomes = get.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Contains(outcomes, node => node.Detail.Contains("200", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("404", StringComparison.Ordinal));
        Assert.Contains(get.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeSuccess);
        Assert.Contains(get.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeFailure);
        Assert.Contains(get.Edges, edge => edge.Kind == ScenarioEdgeKind.Query);
        Assert.Contains(get.Edges, edge => edge.Kind == ScenarioEdgeKind.Call);

        foreach (var node in get.Nodes)
        {
            Assert.NotEmpty(node.Evidence);
        }

        foreach (var edge in get.Edges)
        {
            Assert.NotEmpty(edge.Evidence);
        }

        Assert.DoesNotContain(root, first.DebugProjection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\r", first.DebugProjection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnlinkedPredicateQueryRetainsExistenceButDegradesToConservative()
    {
        var root = FindRepositoryRoot();
        var target = Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var profile = CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0");
        var set = await BuildScenarioGraphsAsync(root, target, profile);

        // The Token lookup predicate compares Guids, which the accepted primitive-comparison
        // vocabulary does not admit, so SC005 fires and the query node/edge degrade to Conservative
        // while the query existence and the 200/404 outcomes remain.
        var token = Assert.Single(set.Graphs, graph => graph.OperationKey == "GET api/Gadgets/token/{token}");
        Assert.Contains(token.Diagnostics, diagnostic => diagnostic.Code == "SC005");
        var query = Assert.Single(token.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.Equal(CertaintyLevel.Conservative, query.Certainty);
        var queryEdge = Assert.Single(token.Edges, edge => edge.Kind == ScenarioEdgeKind.Query);
        Assert.Equal(CertaintyLevel.Conservative, queryEdge.Certainty);

        var outcomes = token.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Contains(outcomes, node => node.Detail.Contains("200", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("404", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TicketReservationGetFlowProducesEvidenceBackedScenarioGraph()
    {
        var target = Path.Combine(ExternalTicketReservationRoot, ExternalTicketReservationTarget.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target))
        {
            // The external corpus is a separate admission contract; the test soft-skips when the
            // checkout is absent so the SeqDoc repository never depends on that layout at build time.
            return;
        }

        var profile = CompilationProfile.Create(ExternalTicketReservationTarget, "Release", "net10.0");
        var set = await BuildScenarioGraphsAsync(ExternalTicketReservationRoot, target, profile);

        var get = Assert.Single(set.Graphs, graph => graph.OperationKey.StartsWith("GET api/Reservations", StringComparison.Ordinal));
        Assert.Contains(get.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        var outcomes = get.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Contains(outcomes, node => node.Detail.Contains("200", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("404", StringComparison.Ordinal));
        Assert.Contains(get.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeSuccess);
        Assert.Contains(get.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeFailure);
        Assert.DoesNotContain(ExternalTicketReservationRoot, set.DebugProjection, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ScenarioGraphSet> BuildScenarioGraphsAsync(
        string root,
        string target,
        CompilationProfile profile)
    {
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, target, profile),
            CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var analysis = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(
            analysis.IsSuccess,
            string.Join(Environment.NewLine, analysis.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var host = new FrameworkModelHost(
        [
            new AspNetCoreControllerModel(),
            new EntityFrameworkQueryModel(),
        ]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        return ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            extraction.Value.ProgramIndex,
            analysis.Value!,
            framework,
            extraction.Value.SemanticFacts,
            extraction.Value.DependencyInjectionFacts,
            extraction.Value.StructuralResultFacts,
            extraction.Value.NonGetSemanticFacts));
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
