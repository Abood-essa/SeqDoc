using System.Text;
using System.Text.RegularExpressions;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Wording;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.FrameworkModels.EntityFramework;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.AcceptanceTests;

/// <summary>
/// First-Get presentation acceptance: the unrelated GetMeaning fixture and the external
/// TicketReservation Get flow produce readable, deterministic, evidence-backed wording and
/// structurally valid Mermaid, including 200/404 outcomes and explicit conservative uncertainty for
/// the unsupported Guid predicate. The scenario semantics are reused from the accepted contract pipeline; this
/// suite asserts presentation-level claims only.
/// </summary>
public sealed class BehaviorDocumentationGetPresentationTests
{
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj";
    private const string ExternalTicketReservationRoot = "samples/Provided/TicketReservation-Solution";
    private const string ExternalTicketReservationTarget = "TicketReservation.Api/TicketReservation.Api.csproj";

    private static readonly string[] BannedTerms =
    [
        "synergize", "leverage", "holistic", "robustify", "performant", "paradigm", "align", "utilize",
    ];

    private static readonly string[] FillerPhrases =
    [
        "Certainly", "Here is", "As an automated assistant", "I hope this helps", "We are pleased to", "best-in-class",
        "seamless", "enterprise-grade",
    ];

    private static readonly Regex MarkdownLinkRegex = new(
        @"!?\[[^\]]*\]\(\s*(?<target><[^>]+>|[^\s)]+)(?:\s+['""].*?['""])?\s*\)",
        RegexOptions.CultureInvariant);

    [Fact]
    public async Task GetMeaningFixtureProducesReadableDocsWithBothOutcomesAndGuidFallback()
    {
        var root = FindRepositoryRoot();
        var target = Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var profile = CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0");
        var set = await BuildScenarioGraphsAsync(root, target, profile);

        var byId = Assert.Single(set.Graphs, graph => graph.OperationKey == "GET api/Gadgets/{id}");
        var byIdPlan = DocumentationPlanner.Plan(byId);
        AssertSemanticPresentationOrder(byIdPlan);
        Assert.Contains(byIdPlan.Wording.Phrases, phrase => phrase.Text.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Contains(byIdPlan.Wording.Phrases, phrase => phrase.Text.Contains("HTTP 404", StringComparison.Ordinal));
        Assert.DoesNotContain(byIdPlan.Wording.Phrases, phrase => phrase.Kind == WordingPhraseKind.TechnicalFallback);
        AssertPhrasesEvidenceBacked(byIdPlan.Wording);
        string markdown = MarkdownRenderer.RenderDocument(byIdPlan.Wording, byIdPlan.Diagram);
        Assert.Empty(MermaidValidator.Validate(MermaidRenderer.Render(byIdPlan.Diagram)));
        Assert.DoesNotContain("\r", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(root, markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Sequence diagram", markdown, StringComparison.Ordinal);

        var token = Assert.Single(set.Graphs, graph => graph.OperationKey == "GET api/Gadgets/token/{token}");
        var tokenPlan = DocumentationPlanner.Plan(token);
        AssertSemanticPresentationOrder(tokenPlan);
        var fallback = Assert.Single(tokenPlan.Wording.Phrases, phrase => phrase.Kind == WordingPhraseKind.TechnicalFallback);
        Assert.Contains("conservative", fallback.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(tokenPlan.Wording.Phrases, phrase => phrase.Text.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Contains(tokenPlan.Wording.Phrases, phrase => phrase.Text.Contains("HTTP 404", StringComparison.Ordinal));
        AssertPhrasesEvidenceBacked(tokenPlan.Wording);
        string tokenMarkdown = MarkdownRenderer.RenderDocument(tokenPlan.Wording, tokenPlan.Diagram);
        Assert.Empty(MermaidValidator.Validate(MermaidRenderer.Render(tokenPlan.Diagram)));
        Assert.Contains("## Technical fallback", tokenMarkdown, StringComparison.Ordinal);

        // Repeated planning of the same graph is byte-identical at the render surface.
        Assert.Equal(
            markdown,
            MarkdownRenderer.RenderDocument(DocumentationPlanner.Plan(byId).Wording, DocumentationPlanner.Plan(byId).Diagram));
    }

    [Fact]
    public async Task TicketReservationGetFlowProducesReadableEvidenceBackedDocs()
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
        var plan = DocumentationPlanner.Plan(get);
        AssertSemanticPresentationOrder(plan);
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Text.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Text.Contains("HTTP 404", StringComparison.Ordinal));
        AssertPhrasesEvidenceBacked(plan.Wording);
        string markdown = MarkdownRenderer.RenderDocument(plan.Wording, plan.Diagram);
        Assert.Empty(MermaidValidator.Validate(MermaidRenderer.Render(plan.Diagram)));
        Assert.DoesNotContain("\r", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(ExternalTicketReservationRoot, markdown, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPhrasesEvidenceBacked(WordingDocument wording)
    {
        Assert.NotEmpty(wording.Phrases);
        foreach (var phrase in wording.Phrases)
        {
            Assert.NotEmpty(phrase.Evidence);
            Assert.NotEqual(SeqDoc.Core.Evidence.CertaintyLevel.Unknown, phrase.Certainty);
        }
    }

    /// <summary>
    /// Focused semantic-ordering assertions for a real planned flow: the diagram emits the client
    /// request before the action call and the data query, failure branches precede success branches,
    /// and the wording bullets follow entry then action then service then query with failure
    /// statements before success statements. The planner owns this order; renderers preserve it.
    /// </summary>
    private static void AssertSemanticPresentationOrder(DocumentationPlan plan)
    {
        int requestIndex = IndexOfFirst(
            plan.Diagram.Messages,
            message => message.Source == "client" && message.Kind == DiagramMessageKind.Request);
        Assert.True(requestIndex >= 0, "The diagram must contain a client request message.");
        Assert.True(requestIndex == 0, "The client request must be the first message.");

        int callIndex = IndexOfFirst(
            plan.Diagram.Messages,
            message => message.Source == "action" && message.Target == "service");
        if (callIndex >= 0)
        {
            Assert.True(requestIndex < callIndex, "The client request must precede the action call.");
        }

        int queryIndex = IndexOfFirst(
            plan.Diagram.Messages,
            message => message.Source == "service" && message.Target == "data");
        if (queryIndex >= 0)
        {
            Assert.True(callIndex >= 0 && callIndex < queryIndex, "The action call must precede the data query.");
        }

        int failureBranch = IndexOfFirst(plan.Diagram.Branches, branch => branch.Kind == DiagramBranchKind.Failure);
        int successBranch = IndexOfFirst(plan.Diagram.Branches, branch => branch.Kind == DiagramBranchKind.Success);
        if (failureBranch >= 0 && successBranch >= 0)
        {
            Assert.True(failureBranch < successBranch, "The failure branch must precede the success branch.");
        }

        string[] statementKeys = plan.Wording.Phrases
            .Where(phrase => phrase.Kind == WordingPhraseKind.Statement)
            .Select(phrase => phrase.Key)
            .ToArray();
        int entryIndex = Array.IndexOf(statementKeys, "entry");
        int actionIndex = Array.IndexOf(statementKeys, "action");
        int serviceIndex = Array.IndexOf(statementKeys, "service-call");
        int queryPhraseIndex = Array.IndexOf(statementKeys, "entity-query");
        Assert.True(
            entryIndex >= 0 && actionIndex >= 0 && serviceIndex >= 0,
            "Entry, action, and service phrases must exist.");
        Assert.True(
            entryIndex < actionIndex && actionIndex < serviceIndex,
            "Wording must follow entry, then action, then service call.");
        if (queryPhraseIndex >= 0)
        {
            Assert.True(serviceIndex < queryPhraseIndex, "The service call phrase must precede the entity query phrase.");
        }

        int resultFailureIndex = Array.IndexOf(statementKeys, "result-failure");
        int resultSuccessIndex = Array.IndexOf(statementKeys, "result-success");
        if (resultFailureIndex >= 0 && resultSuccessIndex >= 0)
        {
            Assert.True(resultFailureIndex < resultSuccessIndex, "The failure result phrase must precede the success result phrase.");
        }
    }

    private static int IndexOfFirst<T>(System.Collections.Immutable.ImmutableArray<T> items, Func<T, bool> predicate)
    {
        for (int index = 0; index < items.Length; index++)
        {
            if (predicate(items[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reproducible acceptance-verification lane for the unrelated GetMeaning Get flows. The lane plans
    /// every admitted Get graph through the real planner, renders and validates the complete output
    /// set in memory, activates it into a temporary root by default, and asserts the generated
    /// Markdown satisfies the repository's documentation-lint invariants. Ordinary runs leave the
    /// repository clean. When the environment variable SEQDOC_TA3_EVIDENCE_ROOT is non-empty, the
    /// same deterministic output is activated under that repository-relative root and compared
    /// byte-for-byte against a fresh temporary activation, producing tracked owner evidence without
    /// changing any production behavior.
    /// </summary>
    [Fact]
    public async Task GetMeaningEvidenceLaneRendersAndActivatesReproducibly()
    {
        var root = FindRepositoryRoot();
        var target = Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var profile = CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0");
        var set = await BuildScenarioGraphsAsync(root, target, profile);

        var graphs = set.Graphs
            .Where(graph => graph.HttpMethod == HttpMethodKind.Get)
            .OrderBy(graph => graph.OperationKey, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(graphs);

        var entries = graphs.Select(graph =>
        {
            var plan = DocumentationPlanner.Plan(graph);
            string fileName = DocumentationFileNaming.EntryKey(graph.EntryPoint, graph.OperationKey);
            return new DocumentSetEntry(fileName, plan.Wording, plan.Diagram);
        }).ToList();
        var built = DocumentationSetBuilder.Build(set.Profile.Id.Value, set.ProgramIndexFingerprint, entries);
        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        Assert.NotEmpty(built.Files);
        AssertDocsLintCompliant(built.Files);

        string? evidenceRoot = Environment.GetEnvironmentVariable("SEQDOC_TA3_EVIDENCE_ROOT");
        bool evidenceLane = !string.IsNullOrWhiteSpace(evidenceRoot);
        string outputRoot = evidenceLane
            ? Path.GetFullPath(evidenceRoot!, root)
            : Path.Combine(Path.GetTempPath(), $"seqdoc-ta3-evidence-{Guid.NewGuid():N}");
        try
        {
            var activation = OutputSetActivator.Activate(outputRoot, built.Files);
            Assert.True(activation.Succeeded, activation.FailureMessage);
            foreach (var file in built.Files)
            {
                Assert.True(
                    File.Exists(Path.Combine(outputRoot, file.RelativePath)),
                    $"Activated file '{file.RelativePath}' is missing from '{outputRoot}'.");
            }

            Assert.True(File.Exists(Path.Combine(outputRoot, "seqdoc.manifest.json")));

            if (evidenceLane)
            {
                // The evidence root must contain the same deterministic bytes as a fresh temporary
                // activation, proving the lane reproduces exactly what ordinary runs produce.
                string tempRoot = Path.Combine(Path.GetTempPath(), $"seqdoc-ta3-evidence-{Guid.NewGuid():N}");
                try
                {
                    var tempActivation = OutputSetActivator.Activate(tempRoot, built.Files);
                    Assert.True(tempActivation.Succeeded, tempActivation.FailureMessage);
                    foreach (var file in built.Files.Where(file =>
                                 file.RelativePath.EndsWith(".md", StringComparison.Ordinal)
                                 || file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)))
                    {
                        Assert.Equal(
                            File.ReadAllBytes(Path.Combine(tempRoot, file.RelativePath)),
                            File.ReadAllBytes(Path.Combine(outputRoot, file.RelativePath)));
                    }
                }
                finally
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
            }
        }
        finally
        {
            if (!evidenceLane && Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Asserts the structural documentation-lint invariants the generated Markdown must satisfy:
    /// canonical newlines, exactly one level-one heading, no skipped heading levels, every section
    /// has content, balanced code fences, no banned terms or filler phrases, and index links that
    /// resolve within the generated output set.
    /// </summary>
    private static void AssertDocsLintCompliant(IReadOnlyList<RenderedOutputFile> files)
    {
        RenderedOutputFile[] markdownFiles = files
            .Where(file => file.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(markdownFiles);
        var paths = files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var file in markdownFiles)
        {
            string content = Encoding.UTF8.GetString(file.Content);
            Assert.DoesNotContain("\r", content, StringComparison.Ordinal);
            string[] lines = content.Split('\n');
            Assert.Equal(1, lines.Count(line => IsHeading(line) && HeadingLevel(line) == 1));

            int previousLevel = 0;
            bool sawHeading = false;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (!IsHeading(line))
                {
                    continue;
                }

                int level = HeadingLevel(line);
                if (!sawHeading)
                {
                    Assert.True(level == 1, $"'{file.RelativePath}' must start with a level-one heading.");
                    sawHeading = true;
                }
                else
                {
                    Assert.True(level <= previousLevel + 1, $"'{file.RelativePath}' skips a heading level at line {index + 1}.");
                }

                bool hasContent = false;
                for (int later = index + 1; later < lines.Length && !IsHeading(lines[later]); later++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[later]))
                    {
                        hasContent = true;
                        break;
                    }
                }

                Assert.True(hasContent, $"Heading '{line}' in '{file.RelativePath}' has no section content.");
                previousLevel = level;
            }

            Assert.True(sawHeading, $"'{file.RelativePath}' contains no heading.");
            Assert.Equal(0, lines.Count(line => line.StartsWith("```", StringComparison.Ordinal)) % 2);

            foreach (string term in BannedTerms)
            {
                Assert.DoesNotContain(term, content, StringComparison.OrdinalIgnoreCase);
            }

            foreach (string phrase in FillerPhrases)
            {
                Assert.DoesNotContain(phrase, content, StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(file.RelativePath, "index.md", StringComparison.Ordinal))
            {
                foreach (Match match in MarkdownLinkRegex.Matches(content).Cast<Match>())
                {
                    string target = match.Groups["target"].Value.Trim().Trim('<', '>');
                    if (target.Length == 0 || target.StartsWith('#'))
                    {
                        continue;
                    }

                    Assert.True(
                        paths.Contains(target),
                        $"Index link target '{target}' is not part of the generated output set.");
                }
            }
        }
    }

    private static bool IsHeading(string line) => line.TrimStart().StartsWith('#')
        && HeadingLevel(line) is >= 1 and <= 6;

    private static int HeadingLevel(string line)
    {
        string trimmed = line.TrimStart();
        int level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        return level;
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
