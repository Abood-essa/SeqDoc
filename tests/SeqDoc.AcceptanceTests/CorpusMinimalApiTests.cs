using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.Rendering.Markdown;
using SeqDoc.Testing;
using Xunit;

namespace SeqDoc.AcceptanceTests;

public sealed class CorpusMinimalApiTests
{
    private static string ExternalRoot => Path.Combine(
        ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.Provided).Root, "testRepo");
    private const string ExternalTarget = "TelecomSimulator.Api/TelecomSimulator.Api.csproj";

    [Fact]
    public async Task TelecomSimulatorMinimalApiProducesTypedDeterministicPostDocumentation()
    {
        string target = Path.Combine(ExternalRoot, ExternalTarget.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(target), target);

        var profile = CompilationProfile.Create(ExternalTarget, "Release", "net10.0");
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(ExternalRoot, target, profile), CancellationToken.None);
        Assert.True(extraction.IsSuccess, Diagnostics(extraction.Diagnostics));

        var artifacts = extraction.Value!;
        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(artifacts.ProgramIndex, artifacts.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, Diagnostics(behavior.Diagnostics));

        var framework = await new FrameworkModelHost([new AspNetCoreMinimalApiModel()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, artifacts.ProgramIndex),
                new FrameworkAnalysisContext(profile, artifacts.ProgramIndex, artifacts.CallbackBoundaryFacts),
                artifacts.Operations,
                artifacts.Symbols), CancellationToken.None);

        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            artifacts.ProgramIndex,
            behavior.Value!,
            framework,
            artifacts.SemanticFacts,
            artifacts.DependencyInjectionFacts,
            artifacts.StructuralResultFacts,
            artifacts.NonGetSemanticFacts,
            artifacts.ConditionalDependencyInjectionFacts,
            artifacts.ConfigurationSemanticFacts,
            artifacts.CallbackBoundaryFacts,
            artifacts.PredicateSemanticFacts,
            artifacts.MinimalApiHandlerFacts));

        Assert.Single(graphs.Graphs);
        var graph = Assert.Single(graphs.Graphs, candidate => candidate.HttpMethod == HttpMethodKind.Post);
        Assert.Equal("api/sms", graph.CanonicalRoute.Trim('/'));
        Assert.Equal("POST api/sms", graph.OperationKey);
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
        Assert.NotNull(graph.HandlerTopology);
        var topology = graph.HandlerTopology!;
        Assert.Equal(["roll is at most 30", "roll is at most 50"], topology.Decisions.Select(item => item.PredicateText));
        Assert.Equal([500, 200, 200], topology.Outcomes.Select(item => item.StatusCode));
        Assert.Equal("SmsRequest", Assert.Single(topology.Parameters, item => item.BindingKind == HttpBindingKind.Body).TypeName);

        Assert.All(graph.Nodes, node =>
        {
            Assert.NotEmpty(node.Evidence);
            Assert.NotEqual(CertaintyLevel.Unknown, node.Certainty);
        });
        Assert.All(graph.Edges, edge =>
        {
            Assert.NotEmpty(edge.Evidence);
            Assert.NotEqual(CertaintyLevel.Unknown, edge.Certainty);
        });
        Assert.DoesNotContain(ExternalRoot, graphs.DebugProjection, StringComparison.OrdinalIgnoreCase);

        var plan = DocumentationPlanner.Plan(graph);
        var action = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key.StartsWith("action", StringComparison.Ordinal));
        Assert.Contains("minimal API", action.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("controller", action.Text, StringComparison.OrdinalIgnoreCase);
        Assert.All(plan.Wording.Phrases, phrase =>
        {
            Assert.NotEmpty(phrase.Evidence);
            Assert.NotEqual(CertaintyLevel.Unknown, phrase.Certainty);
            Assert.DoesNotContain(ExternalRoot, phrase.Text, StringComparison.OrdinalIgnoreCase);
        });

        string markdown = MarkdownRenderer.RenderDocument(plan.Wording, plan.Diagram);
        string mermaid = MermaidRenderer.Render(plan.Diagram);
        Assert.Empty(MermaidValidator.Validate(mermaid));
        Assert.Contains("HTTP 200", markdown, StringComparison.Ordinal);
        Assert.Contains("HTTP 500", markdown, StringComparison.Ordinal);
        Assert.Contains("The request body binds to SmsRequest request.", markdown, StringComparison.Ordinal);
        Assert.Contains("The Minimal API handler responds with HTTP 500.", markdown, StringComparison.Ordinal);
        Assert.Contains("The Minimal API handler responds with HTTP 200.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("controller", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.AspNetCore", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Results.Ok", markdown, StringComparison.Ordinal);
        Assert.Contains("roll is at most 30", mermaid, StringComparison.Ordinal);
        Assert.Contains("Wait 11 seconds", mermaid, StringComparison.Ordinal);
        int requestIndex = mermaid.IndexOf("client->>action: POST api/sms", StringComparison.Ordinal);
        int outerAltIndex = mermaid.IndexOf("alt roll is at most 30", StringComparison.Ordinal);
        Assert.True(requestIndex >= 0 && requestIndex < outerAltIndex, mermaid);
        Assert.Equal(1, mermaid.Split("action-->>client: HTTP 500", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, mermaid.Split("action-->>client: HTTP 200", StringSplitOptions.None).Length - 1);
        Assert.True(mermaid.IndexOf("action-->>client: HTTP 500", StringComparison.Ordinal) > outerAltIndex);
        Assert.True(mermaid.IndexOf("Wait 11 seconds", StringComparison.Ordinal)
            < mermaid.IndexOf("action-->>client: HTTP 200", StringComparison.Ordinal), mermaid);
        Assert.DoesNotContain("Condition", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Continue", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Path terminates", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain(ExternalRoot, markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ExternalRoot, mermaid, StringComparison.OrdinalIgnoreCase);

        var built = DocumentationSetBuilder.Build(
            graphs.Profile.Id.Value,
            graphs.ProgramIndexFingerprint,
            [new DocumentSetEntry(DocumentationFileNaming.EntryKey(graph.EntryPoint, graph.OperationKey), plan.Wording, plan.Diagram)]);
        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        string outputRoot = Path.Combine(Path.GetTempPath(), $"seqdoc-cr5-telecom-{Guid.NewGuid():N}");
        try
        {
            var activation = OutputSetActivator.Activate(outputRoot, built.Files);
            Assert.True(activation.Succeeded, activation.FailureMessage);
            Assert.All(built.Files, file => Assert.True(File.Exists(Path.Combine(outputRoot, file.RelativePath))));
            Assert.True(File.Exists(Path.Combine(outputRoot, "index.md")));
            foreach (var file in built.Files)
            {
                var activated = File.ReadAllBytes(Path.Combine(outputRoot, file.RelativePath));
                Assert.Equal(file.Content, activated);
            }

            var activatedMarkdown = File.ReadAllText(Path.Combine(outputRoot, built.Files.Single(file => file.RelativePath.EndsWith(".md", StringComparison.Ordinal) && file.RelativePath != "index.md").RelativePath));
            var activatedMermaid = File.ReadAllText(Path.Combine(outputRoot, built.Files.Single(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)).RelativePath));
            Assert.Contains("The request body binds to SmsRequest request.", activatedMarkdown, StringComparison.Ordinal);
            Assert.True(activatedMermaid.IndexOf("client->>action: POST api/sms", StringComparison.Ordinal)
                < activatedMermaid.IndexOf("alt roll is at most 30", StringComparison.Ordinal));
            Assert.Equal(2, activatedMermaid.Split("action-->>client: HTTP 200", StringSplitOptions.None).Length - 1);
            Assert.True(activatedMermaid.IndexOf("Wait 11 seconds", StringComparison.Ordinal)
                < activatedMermaid.IndexOf("action-->>client: HTTP 200", StringComparison.Ordinal), activatedMermaid);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static string Diagnostics(IEnumerable<SeqDoc.Core.Diagnostics.AnalysisDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}"));
}
