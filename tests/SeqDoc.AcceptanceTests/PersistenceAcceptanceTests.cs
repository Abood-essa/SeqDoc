using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.FrameworkModels.EntityFramework;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.AcceptanceTests;

public sealed class PersistenceAcceptanceTests
{
    [Fact]
    public async Task GetMeaningPersistenceFactsReachDiagramAndMarkdownDeterministically()
    {
        const string fixture = "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj";
        var root = FindRepositoryRoot();
        var target = Path.Combine(root, fixture.Replace('/', Path.DirectorySeparatorChar));
        var profile = CompilationProfile.Create(fixture, "Release", "net10.0");
        var first = await BuildAsync(root, target, profile);
        var second = await BuildAsync(root, target, profile);
        var firstGraph = Assert.Single(first.Graphs, graph => graph.OperationKey == "GET api/Gadgets/{id}");
        var secondGraph = Assert.Single(second.Graphs, graph => graph.OperationKey == firstGraph.OperationKey);
        var firstPlan = DocumentationPlanner.Plan(firstGraph);
        var secondPlan = DocumentationPlanner.Plan(secondGraph);

        var query = Assert.Single(firstGraph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.Contains("SingleOrDefaultAsync", query.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(query.Evidence);
        Assert.Equal(CertaintyLevel.Exact, query.Certainty);
        var markdown = MarkdownRenderer.RenderDocument(firstPlan.Wording, firstPlan.Diagram);
        Assert.Equal(markdown, MarkdownRenderer.RenderDocument(secondPlan.Wording, secondPlan.Diagram));
        Assert.Contains("## Sequence diagram", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("committed", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rows", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(root, markdown, StringComparison.OrdinalIgnoreCase);

        var firstQueryConfigured = await BuildAsync(root, target, profile, "FindFirstSupportedAsync");
        var secondQueryConfigured = await BuildAsync(root, target, profile, "FindFirstSupportedAsync");
        var queryGraph = Assert.Single(firstQueryConfigured.Graphs, graph => graph.RootKind == ScenarioRootKind.ConfiguredMethod);
        var repeatedQueryGraph = Assert.Single(secondQueryConfigured.Graphs, graph => graph.RootKind == ScenarioRootKind.ConfiguredMethod);
        var queryNode = Assert.Single(queryGraph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.Equal(EntityFrameworkQueryOperatorKind.FirstOrDefaultAsync, queryNode.Presentation?.QueryOperatorKind);
        Assert.NotEmpty(queryNode.Evidence);
        Assert.Equal(CertaintyLevel.Exact, queryNode.Certainty);
        var queryPlan = DocumentationPlanner.Plan(queryGraph);
        Assert.Single(queryPlan.Diagram.Messages, message => message.Source == "service");
        Assert.Contains(queryPlan.Wording.Phrases, phrase => phrase.Text.Contains("FirstOrDefault", StringComparison.Ordinal));
        var queryMarkdown = MarkdownRenderer.RenderDocument(queryPlan.Wording, queryPlan.Diagram);
        var repeatedQueryPlan = DocumentationPlanner.Plan(repeatedQueryGraph);
        Assert.Equal(queryMarkdown, MarkdownRenderer.RenderDocument(repeatedQueryPlan.Wording, repeatedQueryPlan.Diagram));
        Assert.Contains("FirstOrDefault", queryMarkdown, StringComparison.Ordinal);

        var firstRaw = await BuildAsync(root, target, profile, "RawSqlProbeAsync");
        var secondRaw = await BuildAsync(root, target, profile, "RawSqlProbeAsync");
        var rawGraph = Assert.Single(firstRaw.Graphs, graph => graph.RootKind == ScenarioRootKind.ConfiguredMethod);
        var repeatedRawGraph = Assert.Single(secondRaw.Graphs, graph => graph.RootKind == ScenarioRootKind.ConfiguredMethod);
        var rawNodes = rawGraph.Nodes.Where(node => node.Kind == ScenarioNodeKind.SourceObservation).ToArray();
        Assert.Equal(2, rawNodes.Length);
        Assert.All(rawNodes, node =>
        {
            Assert.NotEmpty(node.Evidence);
            Assert.Equal(CertaintyLevel.Conservative, node.Certainty);
        });
        Assert.Equal(2, DocumentationPlanner.Plan(rawGraph).Wording.Phrases.Count(phrase => phrase.Text.Contains("source boundary", StringComparison.Ordinal)));
        var rawMarkdown = MarkdownRenderer.RenderDocument(DocumentationPlanner.Plan(rawGraph).Wording, DocumentationPlanner.Plan(rawGraph).Diagram);
        Assert.Equal(rawMarkdown, MarkdownRenderer.RenderDocument(DocumentationPlanner.Plan(repeatedRawGraph).Wording, DocumentationPlanner.Plan(repeatedRawGraph).Diagram));
        Assert.DoesNotContain("SELECT * FROM", rawMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE Gadgets SET", rawMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE Id = {0}", rawMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("database contents", rawMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("affected rows", rawMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rawGraph.Nodes, node => node.Kind is ScenarioNodeKind.EntityMutation or ScenarioNodeKind.StateAssignment);

        var firstConfigured = await BuildAsync(root, target, profile, "CreateWithAllSupportedMutationsAsync");
        var secondConfigured = await BuildAsync(root, target, profile, "CreateWithAllSupportedMutationsAsync");
        var configured = Assert.Single(firstConfigured.Graphs, graph => graph.RootKind == ScenarioRootKind.ConfiguredMethod);
        var repeatedConfigured = Assert.Single(secondConfigured.Graphs, graph => graph.RootKind == ScenarioRootKind.ConfiguredMethod);
        var persistence = configured.Nodes
            .Where(node => node.Kind == ScenarioNodeKind.EntityMutation)
            .OrderBy(node => node.SequenceOrdinal)
            .ToArray();
        Assert.Equal(5, persistence.Length);
        Assert.Equal(
            new[]
            {
                EntityFrameworkMutationKind.Add,
                EntityFrameworkMutationKind.Add,
                EntityFrameworkMutationKind.RemoveRange,
                EntityFrameworkMutationKind.Clear,
                EntityFrameworkMutationKind.SaveChangesAsync,
            },
            persistence.Select(node => node.Presentation!.MutationKind!.Value).ToArray());
        Assert.Equal("BehaviorDocumentation.GetMeaning.Models.Gadget", persistence[0].Presentation?.EntityTypeName);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Models.Category", persistence[1].Presentation?.EntityTypeName);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Models.Gadget", persistence[2].Presentation?.EntityTypeName);
        Assert.Equal("BehaviorDocumentation.GetMeaning.Models.Category", persistence[3].Presentation?.EntityTypeName);
        Assert.Contains("GadgetDbContext", persistence[4].Detail, StringComparison.Ordinal);
        Assert.All(persistence, node =>
        {
            Assert.Single(configured.Edges, edge => edge.Target == node.Id);
            Assert.NotEmpty(node.Evidence);
            Assert.Equal(CertaintyLevel.Exact, node.Certainty);
        });
        var configuredPlan = DocumentationPlanner.Plan(configured);
        Assert.Equal(5, configuredPlan.Diagram.Messages.Count(message => message.Source == "service"));
        Assert.Contains(configuredPlan.Diagram.Messages, message => message.Label == "calls SaveChanges");
        Assert.Contains(configuredPlan.Wording.Phrases, phrase =>
            phrase.Text.Contains("calls SaveChanges", StringComparison.Ordinal)
            || phrase.Text.Contains("requests saving changes", StringComparison.Ordinal));
        var configuredMarkdown = MarkdownRenderer.RenderDocument(configuredPlan.Wording, configuredPlan.Diagram);
        var repeatedMarkdown = MarkdownRenderer.RenderDocument(
            DocumentationPlanner.Plan(repeatedConfigured).Wording,
            DocumentationPlanner.Plan(repeatedConfigured).Diagram);
        Assert.Equal(configuredMarkdown, repeatedMarkdown);
        Assert.Contains("## Sequence diagram", configuredMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("succeeded", configuredMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("committed", configuredMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rows", configuredMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database contents", configuredMarkdown, StringComparison.OrdinalIgnoreCase);

        var guardedSet = await BuildAsync(root, target, profile, "CreateIfTarget");
        var guarded = Assert.Single(guardedSet.Graphs, graph => graph.RootKind == ScenarioRootKind.ConfiguredMethod);
        var guardedMutations = guarded.Nodes.Where(node => node.Kind == ScenarioNodeKind.EntityMutation).ToArray();
        Assert.Equal(2, guardedMutations.Length);
        var decision = Assert.Single(guarded.Topology.Decisions, item => item.PredicateWording is not null);
        Assert.NotNull(decision.PredicateWording);
        var terminals = guarded.Topology.Terminals
            .Where(terminal => guarded.Topology.Arms.Single(arm => arm.Id == terminal.Arm).Decision == decision.Id)
            .ToArray();
        Assert.Equal(2, terminals.Length);
        Assert.All(terminals, terminal => Assert.Equal(ScenarioTerminalKind.Terminates, terminal.Kind));
        var mutationArm = Assert.Single(
            guarded.Topology.Memberships
                .Where(item => guardedMutations.Any(node => node.Id == item.ScenarioNode))
                .Select(item => item.Arm)
                .Distinct());
        Assert.Contains(terminals, terminal => terminal.Arm == mutationArm);
        var guardedPlan = DocumentationPlanner.Plan(guarded);
        Assert.DoesNotContain(guardedPlan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP002");
        Assert.Empty(guardedPlan.Diagram.Branches);
        Assert.Contains(guardedPlan.Diagram.Messages, message =>
            message.Label.Contains("NotFound", StringComparison.Ordinal));

        var alt = Assert.Single(EnumerateFragments(guardedPlan.Diagram.Sequence), fragment => fragment.Kind == DiagramFragmentKind.Alt);
        var messageById = guardedPlan.Diagram.Messages.ToDictionary(message => message.Id);
        var armWithMutations = Assert.Single(alt.Arms, arm =>
            arm.MessageRefs.Any(messageId => messageById[messageId].Label == "Add Gadget")
            && arm.MessageRefs.Any(messageId => messageById[messageId].Label == "calls SaveChanges"));
        Assert.Contains(armWithMutations.MessageRefs, messageId => messageById[messageId].Source == "service" && messageById[messageId].Label == "Add Gadget");
        Assert.Contains(armWithMutations.MessageRefs, messageId => messageById[messageId].Source == "service" && messageById[messageId].Label == "calls SaveChanges");
        Assert.Contains(alt.Arms, arm => arm.MessageRefs.Any(messageId => messageById[messageId].Label.Contains("NotFound", StringComparison.Ordinal)));
    }

    private static async Task<ScenarioGraphSet> BuildAsync(
        string root,
        string target,
        CompilationProfile profile,
        string? configuredMethodName = null)
    {
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(new CompilationAnalysisRequest(root, target, profile), CancellationToken.None);
        Assert.True(extraction.IsSuccess, string.Join("; ", extraction.Diagnostics.Select(d => d.TechnicalCause)));
        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, string.Join("; ", behavior.Diagnostics.Select(d => d.TechnicalCause)));
        var framework = await new FrameworkModelHost([new AspNetCoreControllerModel(), new EntityFrameworkQueryModel()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols), CancellationToken.None);
        var configuredRoots = configuredMethodName is null
            ? ImmutableArray<MethodId>.Empty
            : [Assert.Single(extraction.Value.ProgramIndex.Methods, method =>
                method.Name == configuredMethodName
                && method.ContainingType == Assert.Single(extraction.Value.ProgramIndex.Types,
                    type => type.MetadataName == "BehaviorDocumentation.GetMeaning.Services.GadgetService").Id).Id];
        return ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(profile, extraction.Value.ProgramIndex, behavior.Value!, framework,
            extraction.Value.SemanticFacts, extraction.Value.DependencyInjectionFacts, extraction.Value.StructuralResultFacts,
            extraction.Value.NonGetSemanticFacts,
            PredicateSemanticFacts: extraction.Value.PredicateSemanticFacts,
            ConfiguredRoots: configuredRoots));
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

    private static IEnumerable<DiagramFragment> EnumerateFragments(DiagramSequence sequence)
    {
        foreach (var element in sequence.Elements)
        {
            if (element.NestedFragment is not null)
            {
                foreach (var fragment in EnumerateFragments(element.NestedFragment))
                {
                    yield return fragment;
                }
            }
        }
    }

    private static IEnumerable<DiagramFragment> EnumerateFragments(DiagramFragment fragment)
    {
        yield return fragment;
        foreach (var arm in fragment.Arms)
        {
            foreach (var nested in arm.Fragments)
            {
                foreach (var descendant in EnumerateFragments(nested))
                {
                    yield return descendant;
                }
            }
        }

        foreach (var nested in fragment.Fragments)
        {
            foreach (var descendant in EnumerateFragments(nested))
            {
                yield return descendant;
            }
        }
    }
}
