using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SeqDoc.Cli;
using SeqDoc.Rendering.Markdown;
using SeqDoc.Testing;
using Xunit;
using Xunit.Sdk;

namespace SeqDoc.AcceptanceTests;

/// <summary>
/// Issue #8 acceptance: the merged #41/#44 outbound service-client producer must reach visible
/// Markdown and Mermaid through the real, config-driven analysis pipeline for the two positive
/// external lanes (CreditTransfer Web, SMS UI Web), the SMS WindowsHost lane must prove
/// project/profile isolation, and every analyzed lane must be deterministic and within its diagram
/// budget.
///
/// Harness path: <b>in-process production CLI</b>. Each lane calls <see cref="CliHost.RunAsync"/> with
/// the exact <c>analyze --project ... --config &lt;yaml&gt; --output ... --json</c> contract the shipped
/// <c>seqdoc</c> binary uses. That path runs the whole production wiring - <c>YamlConfigurationResolver</c>
/// (so <c>selection.roots</c> from the frozen configs drive root admission), <c>MsBuildCompilationProfileResolver</c>
/// (so the real project RID / resolved TFM / MSBuild properties feed the <c>CompilationProfileId</c> and the
/// assembly identity embedded in the <c>method:v1:</c> root hashes), <c>AggregateAnalysisBuilder</c>,
/// <c>ScenarioGraphBuilder</c>, <c>DocumentationPlanner</c>, <c>DocumentationSetBuilder</c> and
/// <c>OutputSetActivator</c>. MSBuild registration happens inside the resolver (not in <c>Program.Main</c>),
/// so calling <see cref="CliHost.RunAsync"/> in-process is the faithful path and needs no subprocess; a
/// hand-rolled <c>CompilationProfile.Create</c> would produce different profile/assembly hashes and the
/// frozen <c>sms-gateway-ui.yaml</c> roots would not resolve. If the corpus, a lane project, or a usable
/// MSBuild/SDK is unavailable, the affected lane is skipped.
///
/// These claims assert structural, wording and determinism properties on the real produced artifacts,
/// not frozen profile/fingerprint equality (the external corpus packages float; the point-in-time
/// anchor lives in the Issue #8 artifact ledger).
/// </summary>
[CollectionDefinition(ServiceClientExternalCorpusSuite.Name, DisableParallelization = true)]
public sealed class ServiceClientExternalCorpusSuite : ICollectionFixture<ServiceClientExternalCorpusFixture>
{
    public const string Name = "ServiceClientExternalCorpus";
}

[Collection(ServiceClientExternalCorpusSuite.Name)]
public sealed class ServiceClientExternalCorpusTests
{
    private readonly ServiceClientExternalCorpusFixture _corpus;

    public ServiceClientExternalCorpusTests(ServiceClientExternalCorpusFixture corpus) => _corpus = corpus;

    // --- Claim 1: the frozen configs resolve their roots and produce the accepted flow-document set.
    // Proves sms-gateway-ui.yaml's frozen method:v1 hashes are correct against the corpus (classic MVC
    // controller actions only become roots through selection.roots), and that credit-transfer.yaml's
    // configured roots still resolve. A failed root resolution surfaces as SD4011 + a non-Succeeded
    // outcome, so the analysis must complete and carry no root-resolution diagnostic.
    [Fact]
    public void ConfiguredRootsResolveAndProduceTheAcceptedDocumentSet()
    {
        // CreditTransfer (8) and SmsUiWeb (4) are the required positive lanes whose document sets
        // Issue #8 pins exactly. SmsWindowsHost and FraudManagement are the isolation / regression
        // lanes running against the floating external corpus (no lock files): a NuGet float that
        // adds one document must not red the suite, but a drop below the frozen floor still fails.
        // FraudManagement is best-effort - when its maintained checkout does not build it is silently
        // excluded (see fixture); the two pinned positive lanes must always be present.
        var analyzed = RequireAllLaneScope();

        var expectations = new Dictionary<CorpusLane, (int Expected, bool Exact)>
        {
            [CorpusLane.CreditTransfer] = (8, true),
            [CorpusLane.SmsWindowsHost] = (15, false),
            [CorpusLane.SmsUiWeb] = (4, true),
            [CorpusLane.FraudManagement] = (36, false),
        };

        Assert.All(analyzed, lane =>
        {
            var run = _corpus.Require(lane).Run1;
            Assert.True(
                string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase),
                $"{lane}: analysis outcome was '{run.Outcome}'; diagnostics: {run.DiagnosticSummary}");
            Assert.DoesNotContain("SD4011", run.DiagnosticCodes);
            Assert.DoesNotContain("SD4008", run.DiagnosticCodes);

            var (expected, exact) = expectations[lane];
            if (exact)
            {
                Assert.Equal(expected, run.FlowDocumentCount);
            }
            else
            {
                Assert.True(
                    run.FlowDocumentCount >= expected,
                    $"{lane}: produced {run.FlowDocumentCount} flow documents, below the frozen floor {expected}.");
            }
        });
    }

    /// <summary>
    /// Shared guard for the three all-lane claims. The whole corpus being absent is a clean skip
    /// (matches <c>CorpusMinimalApiTests</c>); otherwise the two Issue #8-pinned positive lanes must
    /// have been analyzed - a genuinely missing pinned lane is a loud failure - and the per-lane body
    /// then runs only over the lanes that actually produced a result (best-effort FraudManagement is
    /// excluded when its maintained checkout does not build).
    /// </summary>
    private CorpusLane[] RequireAllLaneScope()
    {
        if (_corpus.AnalyzedLanes.Count == 0 && _corpus.CorpusAbsent)
        {
            throw SkipException.ForSkip("the Provided external test-project corpus is not installed.");
        }

        Assert.Contains(CorpusLane.CreditTransfer, _corpus.AnalyzedLanes);
        Assert.Contains(CorpusLane.SmsUiWeb, _corpus.AnalyzedLanes);
        return _corpus.AnalyzedLanes.ToArray();
    }

    // --- Claim 2: the outbound-client producer fact reaches visible output for both positive lanes,
    // joined once (never a second generic MethodCall for the same call site).
    [Fact]
    public void PositiveLanesRenderTheJoinedOutboundClientMessageExactlyOnce()
    {
        foreach (var lane in PositiveLanes())
        {
            var run = _corpus.Require(lane.Lane).Run1;
            string markdown = run.FlowMarkdown;

            // The joined service-client boundary phrase is visible, naming the generated client and the
            // admitted contract.
            Assert.Contains(
                $"through the {lane.Contract} service-client boundary",
                markdown,
                StringComparison.Ordinal);
            Assert.Contains(lane.Client, markdown, StringComparison.Ordinal);

            foreach (string operation in lane.ReachingOperations)
            {
                string callSite = $"{lane.Client}.{operation}";

                // Rendered exactly once: no generic-plus-client duplicate for the same call site.
                int occurrences = Regex.Count(markdown, Regex.Escape(callSite));
                Assert.True(
                    occurrences == 1,
                    $"{lane.Lane}: '{callSite}' appears {occurrences} time(s) in the flow Markdown (expected exactly one, as the joined boundary message).");

                // Every occurrence is the joined boundary form.
                Assert.Matches(
                    Regex.Escape(callSite) + @" through the " + Regex.Escape(lane.Contract) + " service-client boundary",
                    markdown);
            }

            // The generated client is a real participant in the diagram set for the lane.
            Assert.Contains(lane.Client, run.MermaidText, StringComparison.OrdinalIgnoreCase);

            // CreditTransfer's client calls are all guarded, so its Mermaid shows the participant but
            // no arrow (DP002 guarded-arm withhold - accepted residual). SMS UI Web has unguarded
            // client calls that DO render a message arrow carrying the operation name.
            if (lane.Lane == CorpusLane.SmsUiWeb)
            {
                var clientParticipant = Regex.Match(
                    run.MermaidText,
                    @"participant (\S+) as SMSGatewayWcfServiceClient\b");
                Assert.True(
                    clientParticipant.Success,
                    "SmsUiWeb: no Mermaid participant aliased to SMSGatewayWcfServiceClient.");
                string participantId = Regex.Escape(clientParticipant.Groups[1].Value);
                Assert.Matches(
                    @"->>\s*" + participantId + @"\s*:\s*(GetMessageTemplates|GetSMSCNodes|GetMsisdnFilterationList)\b",
                    run.MermaidText);
            }

            // Result mapping is surfaced as caller syntax, not a network/execution claim.
            Assert.Contains("the call result is assigned to", markdown, StringComparison.Ordinal);
        }
    }

    // --- Claim 3: positive-lane wording stays inside compiler evidence and is credential-safe.
    [Fact]
    public void PositiveLaneWordingIsEvidenceBoundedAndCredentialSafe()
    {
        // Over-strong wording is only a defect when it decorates the service-client boundary claim
        // itself; unrelated caller-syntax assignment wording (e.g. a source field named
        // "NumberOfRetries") is evidence-bounded and legitimate.
        string[] overStrongTokens =
        [
            "FaultContract", "retry", "retries", "timeout", "timed out",
            "delivered", "delivery", "was received", "response was received",
            "thrown fault", "fault was thrown", "over the network", "network call",
        ];
        string[] credentialTokens =
        [
            "password", "connectionstring", "connection string", "secret", "apikey", "api key", "pwd=",
        ];

        foreach (var lane in PositiveLanes())
        {
            var run = _corpus.Require(lane.Lane).Run1;
            string haystack = run.AllMarkdown + "\n" + run.MermaidText;

            // Credentials must not surface anywhere in the generated output.
            foreach (string token in credentialTokens)
            {
                Assert.DoesNotContain(token, haystack, StringComparison.OrdinalIgnoreCase);
            }

            var boundaryClauses = Regex.Split(run.AllMarkdown, @"(?<=[.;\n])")
                .Where(clause => clause.Contains("service-client boundary", StringComparison.Ordinal)
                    || clause.Contains(lane.Client + ".", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(boundaryClauses);
            Assert.All(boundaryClauses, clause =>
            {
                foreach (string token in overStrongTokens)
                {
                    Assert.DoesNotContain(token, clause, StringComparison.OrdinalIgnoreCase);
                }
            });

            // The highest-severity network-execution tokens must be absent from the ENTIRE positive-lane
            // flow Markdown, not merely from the boundary clause - a producer regression that emitted
            // "the response was received" in a neighbouring sentence would otherwise pass.
            string[] networkExecutionTokens =
            [
                "over the network", "network call", "response was received",
                "was delivered", "fault was thrown", "was thrown", "timed out",
            ];
            foreach (string token in networkExecutionTokens)
            {
                Assert.DoesNotContain(token, run.FlowMarkdown, StringComparison.OrdinalIgnoreCase);
            }

            // Result mapping stays caller-syntax across the positive lane.
            Assert.Contains("the call result is assigned to", run.FlowMarkdown, StringComparison.Ordinal);
        }
    }

    // --- Claim 4: the SMS WindowsHost lane proves project/profile isolation, not a global-absence claim.
    [Fact]
    public void WindowsHostLaneKeepsTheUiWebServiceClientOutOfItsProfile()
    {
        var run = _corpus.Require(CorpusLane.SmsWindowsHost).Run1;

        // Floor, not exact: WindowsHost is an isolation lane on the floating external corpus (see claim 1).
        Assert.True(
            run.FlowDocumentCount >= 15,
            $"SmsWindowsHost: produced {run.FlowDocumentCount} flow documents, below the frozen floor 15.");

        string text = run.AllMarkdown + "\n" + run.MermaidText;
        foreach (string token in new[]
                 {
                     "ISMSGatewayWcfService", "SMSGatewayWcfServiceClient",
                     "UI.Web", "service-client boundary", "GetMsisdnFilterationList",
                 })
        {
            Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }

        // Isolation, not a global-absence claim.
        Assert.DoesNotContain("no WCF client", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contains no service client", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no outbound service client", text, StringComparison.OrdinalIgnoreCase);
    }

    // --- Claim 5: every analyzed lane is byte-deterministic across two fully independent runs
    // (independent SQLite store + output directory each time) - including the isolation and
    // regression lanes, where the BD-severity change could have perturbed a withheld-loop body.
    [Fact]
    public void EveryLaneProducesByteIdenticalOutputAcrossTwoIndependentRuns()
    {
        var analyzed = RequireAllLaneScope();
        var withSecondRun = analyzed.Where(lane => _corpus.Require(lane).Run2 is not null).ToArray();

        // The two pinned positive lanes must always have a second determinism run.
        Assert.Contains(CorpusLane.CreditTransfer, withSecondRun);
        Assert.Contains(CorpusLane.SmsUiWeb, withSecondRun);

        foreach (var lane in withSecondRun)
        {
            var result = _corpus.Require(lane);
            var first = result.Run1.Files;
            var second = result.Run2!.Files;

            Assert.Equal(
                first.Select(file => file.RelativePath).OrderBy(path => path, StringComparer.Ordinal),
                second.Select(file => file.RelativePath).OrderBy(path => path, StringComparer.Ordinal));

            foreach (var file in first)
            {
                var other = second.Single(candidate => candidate.RelativePath == file.RelativePath);
                Assert.True(
                    file.Content.AsSpan().SequenceEqual(other.Content),
                    $"{lane}: '{file.RelativePath}' differs between two independent runs.");
            }
        }
    }

    // --- Claim 6: every generated Mermaid file is within the configured budget and renders; every
    // relative Markdown link resolves to a generated file.
    [Fact]
    public void EveryLaneStaysWithinTheMermaidBudgetAndHasNoDanglingMarkdownLinks()
    {
        foreach (var lane in RequireAllLaneScope())
        {
            var run = _corpus.Require(lane).Run1;
            var names = run.Files
                .SelectMany(file => new[] { file.RelativePath, Path.GetFileName(file.RelativePath) })
                .ToHashSet(StringComparer.Ordinal);

            foreach (var file in run.Files.Where(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)))
            {
                string mermaid = Encoding.UTF8.GetString(file.Content);
                Assert.True(
                    mermaid.Length <= run.MaxMermaidCharacters,
                    $"{lane}: '{file.RelativePath}' has {mermaid.Length} characters, over the configured budget {run.MaxMermaidCharacters}.");
                Assert.Empty(MermaidValidator.Validate(mermaid));
            }

            foreach (var file in run.Files.Where(file => file.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
            {
                string markdown = Encoding.UTF8.GetString(file.Content);
                foreach (Match match in Regex.Matches(markdown, @"\]\(([^)]+)\)"))
                {
                    string target = match.Groups[1].Value.Trim();
                    if (target.StartsWith("http://", StringComparison.Ordinal)
                        || target.StartsWith("https://", StringComparison.Ordinal)
                        || target.StartsWith("mailto:", StringComparison.Ordinal)
                        || target.StartsWith('#'))
                    {
                        continue;
                    }

                    string relative = target.Split('#', 2)[0];
                    if (relative.StartsWith("./", StringComparison.Ordinal))
                    {
                        relative = relative[2..];
                    }

                    if (relative.Length == 0)
                    {
                        continue;
                    }

                    Assert.True(
                        names.Contains(relative),
                        $"{lane}: '{file.RelativePath}' links to '{target}', which is not a generated file.");
                }
            }
        }
    }

    // --- Claim 7: the BD-severity fix is observable end to end - the SMS UI Web lane completes (not
    // AnalysisFailure) while carrying the non-fatal withhold-class BD2011 natural-loop diagnostic.
    [Fact]
    public void SmsUiWebLaneCompletesEndToEndWithANonFatalWithholdClassBehaviorDiagnostic()
    {
        var run = _corpus.Require(CorpusLane.SmsUiWeb).Run1;

        Assert.True(
            string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase),
            $"SMS UI Web analysis outcome was '{run.Outcome}'; the withhold-class BD diagnostic escalated. Diagnostics: {run.DiagnosticSummary}");
        Assert.Contains("BD2011", run.DiagnosticCodes);

        // The pipeline still produced the full documentation set with the joined outbound-client message.
        Assert.Equal(4, run.FlowDocumentCount);
        Assert.Contains("service-client boundary", run.FlowMarkdown, StringComparison.Ordinal);
    }

    private static IEnumerable<PositiveLane> PositiveLanes()
    {
        yield return new PositiveLane(
            CorpusLane.CreditTransfer,
            "CreditTransferServiceClient",
            "ICreditTransferService",
            ["TransferCredit", "VirginTransferCredit"]);
        yield return new PositiveLane(
            CorpusLane.SmsUiWeb,
            "SMSGatewayWcfServiceClient",
            "ISMSGatewayWcfService",
            ["GetMessageTemplates", "GetSMSCNodes", "GetJob", "GetMsisdnFilterationList"]);
    }

    private sealed record PositiveLane(
        CorpusLane Lane,
        string Client,
        string Contract,
        string[] ReachingOperations);
}

public enum CorpusLane
{
    CreditTransfer,
    SmsWindowsHost,
    SmsUiWeb,
    FraudManagement,
}

/// <summary>One completed production-CLI analysis run for one external lane.</summary>
public sealed record LaneRun(
    string Outcome,
    ImmutableArray<string> DiagnosticCodes,
    string DiagnosticSummary,
    int MaxMermaidCharacters,
    ImmutableArray<RenderedFile> Files)
{
    public int FlowDocumentCount => Files.Count(file =>
        file.RelativePath.EndsWith(".md", StringComparison.Ordinal)
        && !file.RelativePath.Contains('/')
        && file.RelativePath != "index.md");

    public string FlowMarkdown => Combine(Files.Where(file =>
        file.RelativePath.EndsWith(".md", StringComparison.Ordinal) && file.RelativePath != "index.md"));

    public string AllMarkdown => Combine(Files.Where(file => file.RelativePath.EndsWith(".md", StringComparison.Ordinal)));

    public string MermaidText => Combine(Files.Where(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)));

    private static string Combine(IEnumerable<RenderedFile> files) => string.Join(
        "\n",
        files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => Encoding.UTF8.GetString(file.Content)));
}

public sealed record RenderedFile(string RelativePath, byte[] Content);

public sealed record LaneResult(string Name, LaneRun Run1, LaneRun? Run2);

/// <summary>
/// Runs each Issue #8 external lane once through the production config-driven CLI pipeline (the two
/// positive lanes twice, for the determinism claim), so the expensive MSBuild workspace load happens
/// at most twice per lane.
/// </summary>
public sealed class ServiceClientExternalCorpusFixture : IAsyncLifetime
{
    private readonly Dictionary<CorpusLane, LaneResult> _results = [];
    private readonly Dictionary<CorpusLane, string> _skips = [];
    private readonly List<string> _tempDirectories = [];

    /// <summary>True only when the Provided corpus itself could not be resolved (every lane skipped
    /// for the same reason). A best-effort or infrastructure failure of a single lane does not set this.</summary>
    public bool CorpusAbsent { get; private set; }

    /// <summary>The lanes that produced a <see cref="LaneResult"/>, in stable enum order. The all-lane
    /// claims iterate this set; a lane that was absent, hit an infrastructure/build failure, or threw
    /// is not included (best-effort FraudManagement is silently excluded that way).</summary>
    public IReadOnlyCollection<CorpusLane> AnalyzedLanes =>
        _results.Keys.OrderBy(lane => (int)lane).ToArray();

    public async Task InitializeAsync()
    {
        string providedRoot;
        try
        {
            providedRoot = ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.Provided).Root;
        }
        catch (Exception exception) when (exception is SkipException or ExternalCorpusResolutionException)
        {
            CorpusAbsent = true;
            foreach (CorpusLane lane in Enum.GetValues<CorpusLane>())
            {
                _skips[lane] = "the Provided external test-project corpus is not installed.";
            }

            return;
        }

        string repositoryRoot = ExternalCorpusResolver.DiscoverRepositoryRoot(AppContext.BaseDirectory);
        string examples = Path.Combine(repositoryRoot, "docs", "examples");

        await LoadAsync(
            CorpusLane.CreditTransfer,
            Path.Combine(providedRoot, "CreditTransfer-om"),
            "CreditTransferWeb/CreditTransfer.csproj",
            Path.Combine(examples, "credit-transfer.yaml"),
            secondRun: true);
        await LoadAsync(
            CorpusLane.SmsWindowsHost,
            Path.Combine(providedRoot, "SMSGateway-om"),
            "Source/LP.SMSGateway.WindowsHost/LP.SMSGateway.WindowsHost.csproj",
            Path.Combine(examples, "sms-gateway.yaml"),
            secondRun: true);
        await LoadAsync(
            CorpusLane.SmsUiWeb,
            Path.Combine(providedRoot, "SMSGateway-om"),
            "Source/LP.SMSGateway.UI.Web/LP.SMSGateway.UI.Web.csproj",
            Path.Combine(examples, "sms-gateway-ui.yaml"),
            secondRun: true);
        await LoadAsync(
            CorpusLane.FraudManagement,
            Path.Combine(providedRoot, "FraudManagement"),
            "FraudManagement.sln",
            Path.Combine(examples, "fraud-management.yaml"),
            secondRun: true);
    }

    public Task DisposeAsync()
    {
        foreach (string directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort - a leaked temp directory is not a test failure.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return Task.CompletedTask;
    }

    public LaneResult Require(CorpusLane lane)
    {
        if (_results.TryGetValue(lane, out var result))
        {
            return result;
        }

        throw SkipException.ForSkip(_skips.TryGetValue(lane, out var reason)
            ? $"{lane}: {reason}"
            : $"{lane}: lane was not analyzed.");
    }

    private async Task LoadAsync(
        CorpusLane lane,
        string laneRoot,
        string relativeTarget,
        string configPath,
        bool secondRun)
    {
        string target = Path.Combine(laneRoot, relativeTarget.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target))
        {
            _skips[lane] = $"lane project '{relativeTarget}' is not present under '{laneRoot}'.";
            return;
        }

        if (!File.Exists(configPath))
        {
            _skips[lane] = $"lane config '{Path.GetFileName(configPath)}' is not present.";
            return;
        }

        try
        {
            var first = await RunAsync(lane, laneRoot, target, configPath);
            if (IsInfrastructureFailure(first))
            {
                _skips[lane] = $"analysis could not run in this environment (outcome '{first.Outcome}'): {first.DiagnosticSummary}";
                return;
            }

            LaneRun? second = null;
            if (secondRun)
            {
                second = await RunAsync(lane, laneRoot, target, configPath);
                if (IsInfrastructureFailure(second))
                {
                    _skips[lane] = $"second determinism run could not complete (outcome '{second.Outcome}').";
                    return;
                }
            }

            _results[lane] = new LaneResult(lane.ToString(), first, second);
        }
        catch (Exception exception) when (exception is not SkipException)
        {
            string detail = exception.ToString();
            _skips[lane] = "lane analysis threw before producing a result: "
                + detail[..Math.Min(detail.Length, 1500)];
        }
    }

    private static bool IsInfrastructureFailure(LaneRun run)
    {
        if (string.Equals(run.Outcome, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // `run.Outcome` is the CLI JSON `outcome` field, which is `ApplicationOutcome.ToString()`
        // (PascalCase: "BuildFailure", "PersistenceFailure", "Cancelled"). Compare case-insensitively.
        // AnalysisFailure, DocumentationGenerationFailure and UnsupportedRequiredFeature are NOT
        // infrastructure - those fail the affected claim loudly.
        foreach (string infrastructureOutcome in new[] { "BuildFailure", "PersistenceFailure", "Cancelled" })
        {
            if (string.Equals(run.Outcome, infrastructureOutcome, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // A command-line / path / cache-preparation failure is an environment problem, not a finding.
        return run.DiagnosticCodes.Any(code => code is "SD4000" or "SD4004" or "SD4006");
    }

    private async Task<LaneRun> RunAsync(
        CorpusLane lane,
        string laneRoot,
        string target,
        string configPath)
    {
        string outputDirectory = NewTempDirectory($"{lane}-out");
        string cacheDirectory = NewTempDirectory($"{lane}-cache");

        string[] args =
        [
            "analyze", target,
            "--repository-root", laneRoot,
            "--config", configPath,
            "--configuration", "Release",
            "--framework", "net9.0",
            "--cache", Path.Combine(cacheDirectory, "cache-v1.db"),
            "--output", outputDirectory,
            "--json",
        ];

        var standardOutput = new StringWriter();
        var standardError = new StringWriter();
        await CliHost.RunAsync(args, standardOutput, standardError, CancellationToken.None);

        string json = standardOutput.ToString();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string outcome = root.GetProperty("outcome").GetString() ?? "unknown";

        var codes = ImmutableArray<string>.Empty;
        var summary = new StringBuilder();
        if (root.TryGetProperty("diagnostics", out var diagnostics) && diagnostics.ValueKind == JsonValueKind.Array)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            foreach (var diagnostic in diagnostics.EnumerateArray())
            {
                string code = diagnostic.GetProperty("code").GetString() ?? string.Empty;
                builder.Add(code);
                summary.Append(code).Append(' ');
            }

            codes = builder.ToImmutable();
        }

        int budget = ReadMermaidBudget(root);
        var files = ReadGeneratedFiles(outputDirectory);

        return new LaneRun(outcome, codes, summary.ToString().Trim(), budget, files);
    }

    private static int ReadMermaidBudget(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("configuration", out var configuration)
            && configuration.TryGetProperty("diagramBudget", out var diagramBudget)
            && diagramBudget.TryGetProperty("maxMermaidCharacters", out var maxMermaid)
            && maxMermaid.TryGetProperty("value", out var value)
            && value.TryGetInt32(out int budget))
        {
            return budget;
        }

        return 45000;
    }

    private static ImmutableArray<RenderedFile> ReadGeneratedFiles(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<RenderedFile>();
        foreach (string path in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(outputDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
            builder.Add(new RenderedFile(relative, File.ReadAllBytes(path)));
        }

        builder.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return builder.ToImmutable();
    }

    private string NewTempDirectory(string label)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"seqdoc-i8-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
