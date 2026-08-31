using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SeqDoc.Cli.Tests;

public sealed class CliProcessTests
{
    [Fact]
    public async Task InvalidJsonCommandUsesTypedDocumentAndExitCodeTwo()
    {
        var result = await RunAsync("unknown", "--json");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("SD4000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task CatalogWithoutActiveCacheIsActionableAndDoesNotCreateDatabase()
    {
        string root = FindRepositoryRoot();
        string target = Fixture(root, "GeneratedAndPartialSource");
        using var cache = new TemporaryCache();

        var result = await RunAsync("catalog", target, "--repository-root", root, "--cache", cache.Path);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Output);
        Assert.Contains("SD4001", result.Error, StringComparison.Ordinal);
        Assert.Contains("Next action: Run 'seqdoc analyze'", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(cache.Path));
    }

    [Fact]
    public async Task AnalyzeCatalogInspectAndBuildFailureHonorProcessContracts()
    {
        string root = FindRepositoryRoot();
        string validTarget = Fixture(root, "GeneratedAndPartialSource");
        string brokenTarget = Fixture(root, "BrokenCompilation");
        using var cache = new TemporaryCache();

        var analyze = await RunAsync(
            "analyze", validTarget, "--repository-root", root, "--cache", cache.Path, "--json");
        Assert.Equal(0, analyze.ExitCode);
        Assert.Equal(string.Empty, analyze.Error);
        using var analyzeDocument = JsonDocument.Parse(analyze.Output);
        Assert.Equal("Succeeded", analyzeDocument.RootElement.GetProperty("outcome").GetString());
        Assert.True(File.Exists(cache.Path));

        var catalog = await RunAsync(
            "catalog", validTarget, "--repository-root", root, "--cache", cache.Path, "--kind", "project", "--json");
        Assert.Equal(0, catalog.ExitCode);
        using var catalogDocument = JsonDocument.Parse(catalog.Output);
        JsonElement items = catalogDocument.RootElement.GetProperty("data").GetProperty("items");
        Assert.NotEqual(0, items.GetArrayLength());
        string id = items[0].GetProperty("id").GetString()!;

        var prefix = await RunAsync(
            "catalog", validTarget, "--repository-root", root, "--cache", cache.Path,
            "--kind", "project", "--id", id[..^8], "--json");
        Assert.Equal(0, prefix.ExitCode);
        using var prefixDocument = JsonDocument.Parse(prefix.Output);
        Assert.Equal(1, prefixDocument.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());

        var missingPrefix = await RunAsync(
            "catalog", validTarget, "--repository-root", root, "--cache", cache.Path,
            "--kind", "project", "--id", "project:v1:missing", "--json");
        Assert.Equal(2, missingPrefix.ExitCode);
        Assert.Contains("SD4002", missingPrefix.Output, StringComparison.Ordinal);

        var ambiguousPrefix = await RunAsync(
            "catalog", validTarget, "--repository-root", root, "--cache", cache.Path,
            "--kind", "type", "--id", "symbol:v1:", "--json");
        Assert.Equal(2, ambiguousPrefix.ExitCode);
        Assert.Contains("SD4003", ambiguousPrefix.Output, StringComparison.Ordinal);

        var inspectBefore = await RunAsync(
            "inspect", "solution", validTarget, "--repository-root", root, "--cache", cache.Path, "--json");
        Assert.Equal(0, inspectBefore.ExitCode);
        using var beforeDocument = JsonDocument.Parse(inspectBefore.Output);
        var profileElement = beforeDocument.RootElement.GetProperty("data").GetProperty("inspection")
            .GetProperty("profiles")[0];
        string fingerprint = profileElement.GetProperty("indexFingerprint").GetString()!;
        JsonElement behaviorElement = profileElement.GetProperty("behavior");
        Assert.True(behaviorElement.GetProperty("available").GetBoolean());
        Assert.NotEqual(0, behaviorElement.GetProperty("methodFlows").GetInt32());
        Assert.Equal(64, behaviorElement.GetProperty("behaviorFingerprint").GetString()!.Length);

        string configurationPath = cache.WriteConfiguration("""
            schemaVersion: 1
            profiles:
              production:
                msbuildProperties:
                  EnvironmentName: Production
            """);
        var configuredAnalyze = await RunAsync(
            "analyze", validTarget, "--repository-root", root, "--cache", cache.Path,
            "--config", configurationPath, "--profile", "production", "--json");
        Assert.Equal(0, configuredAnalyze.ExitCode);
        var configuredCatalog = await RunAsync(
            "catalog", validTarget, "--repository-root", root, "--cache", cache.Path,
            "--config", configurationPath, "--profile", "production", "--kind", "project", "--json");
        Assert.Equal(0, configuredCatalog.ExitCode);

        var broken = await RunAsync(
            "analyze", brokenTarget, "--repository-root", root, "--cache", cache.Path);
        Assert.Equal(3, broken.ExitCode);
        Assert.Equal(string.Empty, broken.Output);
        Assert.Contains("Next action:", broken.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0246: CS0246", broken.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(System.IO.Path.Combine(cache.DirectoryPath, "build-diagnostics.json")));

        var inspectAfter = await RunAsync(
            "inspect", "solution", validTarget, "--repository-root", root, "--cache", cache.Path, "--json");
        using var afterDocument = JsonDocument.Parse(inspectAfter.Output);
        Assert.Equal(fingerprint, afterDocument.RootElement.GetProperty("data").GetProperty("inspection")
            .GetProperty("profiles")[0].GetProperty("indexFingerprint").GetString());
    }

    [Fact]
    public async Task AnalyzePersistsCollapsedConstructedInterfaceEdges()
    {
        string root = FindRepositoryRoot();
        string target = Fixture(root, "DuplicateConstructedInterfaces");
        using var cache = new TemporaryCache();

        var result = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("Succeeded", document.RootElement.GetProperty("outcome").GetString());
        Assert.True(File.Exists(cache.Path));

        var inspect = await RunAsync(
            "inspect", "solution", target, "--repository-root", root, "--cache", cache.Path, "--json");
        Assert.Equal(0, inspect.ExitCode);
        using var inspection = JsonDocument.Parse(inspect.Output);
        Assert.Single(inspection.RootElement.GetProperty("data").GetProperty("inspection").GetProperty("profiles").EnumerateArray());
    }

    [Fact]
    public async Task BuildFailureBoundsPrimaryDiagnosticsAndRetainsCompleteArtifact()
    {
        string root = FindRepositoryRoot();
        string target = Fixture(root, "ManyDiagnostics");
        using var cache = new TemporaryCache();

        var result = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--json");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement diagnostics = document.RootElement.GetProperty("diagnostics");
        JsonElement diagnosticOutput = document.RootElement.GetProperty("diagnosticOutput");
        Assert.Equal(20, diagnostics.GetArrayLength());
        Assert.Equal(25, diagnosticOutput.GetProperty("totalCount").GetInt32());
        Assert.Equal(20, diagnosticOutput.GetProperty("displayedCount").GetInt32());
        Assert.Equal(5, diagnosticOutput.GetProperty("omittedCount").GetInt32());
        Assert.Equal(64, diagnosticOutput.GetProperty("artifactSha256").GetString()!.Length);

        string artifactPath = System.IO.Path.Combine(cache.DirectoryPath, "build-diagnostics.json");
        using var artifact = JsonDocument.Parse(await File.ReadAllTextAsync(artifactPath));
        Assert.Equal(25, artifact.RootElement.GetProperty("diagnosticCount").GetInt32());
        Assert.Equal(25, artifact.RootElement.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task AnalyzeWithOutputGeneratesDeterministicDocsWithoutChangingNoOutputBehavior()
    {
        string root = FindRepositoryRoot();
        string target = GetMeaningFixture(root);
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var withoutOutput = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--json");
        Assert.Equal(0, withoutOutput.ExitCode);
        Assert.Equal(string.Empty, withoutOutput.Error);
        Assert.False(Directory.Exists(output));

        var withOutput = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--output", output, "--json");
        Assert.Equal(0, withOutput.ExitCode);
        Assert.Equal(string.Empty, withOutput.Error);
        Assert.True(File.Exists(System.IO.Path.Combine(output, "index.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(output, "seqdoc.manifest.json")));
        Assert.Contains(
            Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly),
            file => !string.Equals(System.IO.Path.GetFileName(file), "index.md", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(withOutput.Output);
        JsonElement documentation = document.RootElement.GetProperty("data").GetProperty("documentation");
        Assert.NotEqual(0, documentation.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task AllFrameworksWithOutputIsRejected()
    {
        // Regression: multi-profile output is not admitted in accepted contract. The combination must be rejected
        // up front rather than flattening graphs under one profile.
        string root = FindRepositoryRoot();
        string target = GetMeaningFixture(root);
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var result = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--all-frameworks", "--output", output, "--json");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
        Assert.Contains(
            document.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "SD4000");
    }

    [Fact]
    public async Task ConfiguredExactMethodRootIsAcceptedWithoutNameMatching()
    {
        string root = FindRepositoryRoot();
        string target = FourFlowsFixture(root);
        using var cache = new TemporaryCache();
        string methodId = await FirstMethodIdAsync(target, cache);
        string config = cache.WriteConfiguration($"""
            schemaVersion: 1
            selection:
              roots:
                - {methodId}
            """);
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var result = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--config", config, "--output", output, "--json");

        Assert.Equal(0, result.ExitCode);
        const string displaySignature = "BehaviorDocumentation.FourFlows.Services.MutationProbeService.UnsupportedAndUnrelatedProbe()";
        var configuredDocuments = Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly)
            .Where(file =>
            {
                string content = File.ReadAllText(file);
                return content.Contains(displaySignature, StringComparison.Ordinal)
                    && content.Contains($"The selected method {displaySignature} executes.", StringComparison.Ordinal);
            })
            .ToArray();
        var configuredDocument = Assert.Single(configuredDocuments);
        var documentation = File.ReadAllText(configuredDocument);
        Assert.DoesNotContain("controller action", documentation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API client", documentation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP GET", documentation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfiguredRootsWithAllFrameworksAreRejectedExplicitly()
    {
        string root = FindRepositoryRoot();
        string target = FourFlowsFixture(root);
        using var cache = new TemporaryCache();
        string methodId = await FirstMethodIdAsync(target, cache);
        string config = cache.WriteConfiguration($"schemaVersion: 1\nselection:\n  roots: [{methodId}]\n");

        var result = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--config", config, "--all-frameworks", "--json");

        Assert.Equal(2, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
        Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
             diagnostic => diagnostic.GetProperty("code").GetString() == "SD4012");
    }

    [Fact]
    public async Task ConfiguredEmptyRootsWithAllFrameworksAreRejectedButAbsentRootsRemainAllowed()
    {
        string root = FindRepositoryRoot();
        string target = GetMeaningFixture(root);
        using var cache = new TemporaryCache();
        string config = cache.WriteConfiguration("schemaVersion: 1\nselection:\n  roots: []\n");

        var rejected = await RunAsync("catalog", target, "--repository-root", root, "--cache", cache.Path,
            "--config", config, "--all-frameworks", "--json");
        Assert.Equal(2, rejected.ExitCode);
        using var rejectedDocument = JsonDocument.Parse(rejected.Output);
        Assert.Contains(rejectedDocument.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "SD4012");

        var seeded = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path, "--json");
        Assert.Equal(0, seeded.ExitCode);
        var allowed = await RunAsync("catalog", target, "--repository-root", root, "--cache", cache.Path,
            "--all-frameworks", "--json");
        Assert.Equal(0, allowed.ExitCode);
    }

    [Fact]
    public async Task ConfiguredRootSelectionIsExactAndAtomicWhenOneConfiguredIdIsInvalid()
    {
        string root = FindRepositoryRoot();
        string target = FourFlowsFixture(root);
        using var cache = new TemporaryCache();
        string methodId = await FirstMethodIdAsync(target, cache);
        string config = cache.WriteConfiguration($"""
            schemaVersion: 1
            selection:
              roots:
                - {methodId}
                - {methodId[..^1]}
            """);
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var result = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--config", config, "--output", output, "--json");

        Assert.Equal(2, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task InvalidConfiguredRootPreservesPreviouslyGeneratedOutput()
    {
        string root = FindRepositoryRoot();
        string target = FourFlowsFixture(root);
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");
        var first = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--output", output, "--json");
        Assert.Equal(0, first.ExitCode);
        var before = Directory.GetFiles(output, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !file.EndsWith(".stale", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToDictionary(file => file, File.ReadAllBytes, StringComparer.Ordinal);

        string config = cache.WriteConfiguration("schemaVersion: 1\nselection:\n  roots: [method:v1:stale-or-foreign]\n");
        var invalid = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--config", config, "--output", output, "--json");

        Assert.Equal(2, invalid.ExitCode);
        using var document = JsonDocument.Parse(invalid.Output);
        Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
        foreach (var pair in before)
        {
            Assert.True(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), $"Changed {Path.GetFileName(pair.Key)}");
        }
    }

    [Fact]
    public async Task ConfiguredRootFromPortableProfileIsRejectedForWindowsProfileAndPreservesOutput()
    {
        string root = FindRepositoryRoot();
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");
        var seeded = await RunAsync("analyze", FourFlowsFixture(root), "--repository-root", root,
            "--cache", cache.Path, "--output", output, "--json");
        Assert.Equal(0, seeded.ExitCode);
        var before = Directory.GetFiles(output, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !file.EndsWith(".stale", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToDictionary(file => file, File.ReadAllBytes, StringComparer.Ordinal);

        string target = Fixture(root, "MultiTargetProfiles");
        string portableMethodId = await PortableOnlyMethodIdAsync(target, cache);
        string config = cache.WriteConfiguration($"schemaVersion: 1\nselection:\n  roots: [\"{portableMethodId}\"]\n");
        var invalid = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--framework", "net10.0-windows", "--config", config, "--output", output, "--json");

        Assert.Equal(2, invalid.ExitCode);
        using var document = JsonDocument.Parse(invalid.Output);
        Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
        Assert.Contains(document.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "SD4011");
        foreach (var pair in before)
        {
            Assert.True(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), $"Changed {Path.GetFileName(pair.Key)}");
        }
    }

    [Fact]
    public async Task FailedGenerationMarksStaleAndPreservesPriorDocs()
    {
        string root = FindRepositoryRoot();
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var first = await RunAsync(
            "analyze", GetMeaningFixture(root), "--repository-root", root, "--cache", cache.Path, "--output", output, "--json");
        Assert.Equal(0, first.ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(output, "index.md")));

        // The second target activates analysis but admits no Get flows; generation fails and the
        // previous documentation is preserved and explicitly marked stale rather than deleted.
        var second = await RunAsync(
            "analyze", Fixture(root, "GeneratedAndPartialSource"), "--repository-root", root,
            "--cache", cache.Path, "--output", output, "--json");
        // Regression: analysis activated successfully, so generation failure is a distinct outcome
        // with a distinct exit code, never reported as analysis failure (exit code 4). The previous
        // documentation stays preserved and marked stale and the active-analysis diagnostic remains.
        Assert.NotEqual(4, second.ExitCode);
        using var document = JsonDocument.Parse(second.Output);
        Assert.Equal("DocumentationGenerationFailure", document.RootElement.GetProperty("outcome").GetString());
        Assert.Contains(
            document.RootElement.GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "SD4008");
        Assert.True(File.Exists(System.IO.Path.Combine(output, "index.md")));
        Assert.True(File.Exists(System.IO.Path.Combine(output, "seqdoc.stale")));
    }

    [Fact]
    public async Task AnalyzeWithOutputGeneratesAllFourFlowsAndDeterministicManifest()
    {
        // Claim 14 surface: the CLI no longer filters to Get; every admitted flow is generated in
        // deterministic operation-key/entry-id order and the ownership manifest is byte-stable.
        string root = FindRepositoryRoot();
        string target = FourFlowsFixture(root);
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var first = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--output", output, "--json");
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(string.Empty, first.Error);
        string manifestPath = System.IO.Path.Combine(output, "seqdoc.manifest.json");
        Assert.True(File.Exists(manifestPath));
        string[] flowFiles = Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(System.IO.Path.GetFileName(file), "index.md", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(4, flowFiles.Length);
        Assert.Contains(
            flowFiles,
            file => System.IO.Path.GetFileName(file).StartsWith("delete-api-widgets-id-", StringComparison.Ordinal));
        Assert.Contains(
            flowFiles,
            file => System.IO.Path.GetFileName(file).StartsWith("post-api-widgets-id-reservations-", StringComparison.Ordinal));

        byte[] manifestAfterFirst = await File.ReadAllBytesAsync(manifestPath);
        var second = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--output", output, "--json");
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(manifestAfterFirst, await File.ReadAllBytesAsync(manifestPath));
    }

    [Fact]
    public async Task AnalyzeRegistersEf6ModelAndGeneratesEdmxDocumentationThroughTheRealCli()
    {
        string root = FindRepositoryRoot();
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        string target = "tests/fixtures/PassC/EntityFramework6Edmx/EntityFramework6Edmx.csproj";
        var result = await RunAsync("analyze", target,
            "--repository-root", root, "--cache", cache.Path, "--output", output, "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("Succeeded", document.RootElement.GetProperty("outcome").GetString());
        Assert.True(File.Exists(System.IO.Path.Combine(output, "index.md")));
        Assert.Contains(Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly), file =>
            File.ReadAllText(file).Contains("EDMX metadata boundary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeEntryRequiresOutputAndSelectsExactlyOneFlow()
    {
        string root = FindRepositoryRoot();
        string target = FourFlowsFixture(root);
        using var cache = new TemporaryCache();

        var missingOutput = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--entry", "GET api/Widgets/{id}", "--json");
        Assert.Equal(2, missingOutput.ExitCode);
        using (var document = JsonDocument.Parse(missingOutput.Output))
        {
            Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("SD4000", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }

        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");
        var byKey = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--output", output, "--entry", "POST api/Widgets/{id}/reservations", "--json");
        Assert.Equal(0, byKey.ExitCode);
        Assert.Equal(
            1,
            Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly).Count(file => !string.Equals(System.IO.Path.GetFileName(file), "index.md", StringComparison.Ordinal)));
        Assert.Contains(
            Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly),
            file => System.IO.Path.GetFileName(file).StartsWith("post-api-widgets-id-reservations-", StringComparison.Ordinal));

        string output2 = System.IO.Path.Combine(cache.DirectoryPath, "docs2");
        // The stable entry key is the readable identity the output set exposes as the flow file name
        // (operation-key slug plus entry-id suffix); a focused entry can name one flow exactly.
        string postFile = Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly)
            .Single(file => System.IO.Path.GetFileName(file).StartsWith("post-api-widgets-id-reservations-", StringComparison.Ordinal));
        string postEntryKey = System.IO.Path.GetFileNameWithoutExtension(postFile);
        var byId = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--output", output2, "--entry", postEntryKey, "--json");
        Assert.True(
            byId.ExitCode == 0,
            $"byId failed: exit {byId.ExitCode}\nOUT:\n{byId.Output}\nERR:\n{byId.Error}");
        Assert.Contains(
            Directory.GetFiles(output2, "*.md", SearchOption.TopDirectoryOnly),
            file => System.IO.Path.GetFileName(file).StartsWith("post-api-widgets-id-reservations-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeEntryUnknownAndAmbiguousRejectAsInvalidInput()
    {
        string root = FindRepositoryRoot();
        string target = FourFlowsFixture(root);
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var unknown = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--output", output, "--entry", "GET api/Widgets", "--json");
        Assert.Equal(2, unknown.ExitCode);
        using (var document = JsonDocument.Parse(unknown.Output))
        {
            Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("SD4009", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }

        Assert.False(Directory.Exists(output));

        var ambiguous = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--output", output, "--entry", "entry-point:v1:", "--json");
        Assert.Equal(2, ambiguous.ExitCode);
        using (var document = JsonDocument.Parse(ambiguous.Output))
        {
            Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("SD4010", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        }

        Assert.False(Directory.Exists(output));
    }

    /// <summary>
    /// F7: focused entry selection is exactly-one. A strict prefix of the readable entry key (the
    /// full operation-key slug plus entry-id suffix) is not an exact operation key or full entry key,
    /// so it must be rejected deterministically without touching output.
    /// </summary>
    [Fact]
    public async Task AnalyzeEntrySelectionRejectsUniqueEntryKeyPrefixAsInvalidInput()
    {
        string root = FindRepositoryRoot();
        string target = FourFlowsFixture(root);
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var seeded = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--output", output, "--json");
        Assert.Equal(0, seeded.ExitCode);
        string postFile = Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly)
            .Single(file => System.IO.Path.GetFileName(file).StartsWith("post-api-widgets-id-reservations-", StringComparison.Ordinal));
        string fullEntryKey = System.IO.Path.GetFileNameWithoutExtension(postFile);
        string uniquePrefix = fullEntryKey[..^2];

        string output2 = System.IO.Path.Combine(cache.DirectoryPath, "docs2");
        var result = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--output", output2, "--entry", uniquePrefix, "--json");

        Assert.Equal(2, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("InvalidInput", document.RootElement.GetProperty("outcome").GetString());
        Assert.False(Directory.Exists(output2));
    }

    /// <summary>
    /// F9: when a forced generation failure follows a valid activation, the previous documentation
    /// survives byte-for-byte, the stale marker is written, and the command returns the
    /// documentation-generation failure outcome (never an analysis failure outcome).
    /// </summary>
    [Fact]
    public async Task PreviousValidDocsSurviveForcedGenerationFailureByteForByte()
    {
        string root = FindRepositoryRoot();
        using var cache = new TemporaryCache();
        string output = System.IO.Path.Combine(cache.DirectoryPath, "docs");

        var first = await RunAsync(
            "analyze", FourFlowsFixture(root), "--repository-root", root, "--cache", cache.Path, "--output", output, "--json");
        Assert.Equal(0, first.ExitCode);
        string[] owned = Directory.GetFiles(output, "*", SearchOption.TopDirectoryOnly)
            .Where(file => file.EndsWith(".md", StringComparison.Ordinal)
                || file.EndsWith(".mmd", StringComparison.Ordinal)
                || string.Equals(System.IO.Path.GetFileName(file), "seqdoc.manifest.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(owned);
        var before = owned.ToDictionary(file => file, File.ReadAllBytes, StringComparer.Ordinal);

        var second = await RunAsync(
            "analyze", Fixture(root, "GeneratedAndPartialSource"), "--repository-root", root,
            "--cache", cache.Path, "--output", output, "--json");
        Assert.NotEqual(4, second.ExitCode);
        using var document = JsonDocument.Parse(second.Output);
        Assert.Equal("DocumentationGenerationFailure", document.RootElement.GetProperty("outcome").GetString());
        Assert.True(File.Exists(System.IO.Path.Combine(output, "seqdoc.stale")));

        foreach (string file in owned)
        {
            Assert.True(
                before[file].SequenceEqual(File.ReadAllBytes(file)),
                $"Prior documentation byte '{System.IO.Path.GetFileName(file)}' changed after forced generation failure.");
        }

        // The owned set (Markdown, Mermaid, and the ownership manifest) survives byte-for-byte; the
        // same file classes must still be present after the failed generation.
        string[] actual = Directory.GetFiles(output, "*", SearchOption.TopDirectoryOnly)
            .Where(file => file.EndsWith(".md", StringComparison.Ordinal)
                || file.EndsWith(".mmd", StringComparison.Ordinal)
                || string.Equals(System.IO.Path.GetFileName(file), "seqdoc.manifest.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(owned, actual);
    }

    private static string FourFlowsFixture(string root) =>
        System.IO.Path.Combine(root, "tests", "fixtures", "BehaviorDocumentation", "FourFlows", "FourFlows.csproj");

    private static string GetMeaningFixture(string root) =>
        System.IO.Path.Combine(root, "tests", "fixtures", "BehaviorDocumentation", "GetMeaning", "GetMeaning.csproj");

    private static string Fixture(string root, string name) =>
        System.IO.Path.Combine(root, "tests", "fixtures", "PassA", name, $"{name}.csproj");

    private static async Task<ProcessResult> RunAsync(params string[] arguments)
    {
        string root = FindRepositoryRoot();
        string assembly = System.IO.Path.Combine(root, "src", "SeqDoc.Cli", "bin", "Release", "net10.0", "SeqDoc.Cli.dll");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add(assembly);
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static async Task<string> FirstMethodIdAsync(string target, TemporaryCache cache)
    {
        string root = FindRepositoryRoot();
        var analyze = await RunAsync("analyze", target, "--repository-root", root, "--cache", cache.Path, "--json");
        Assert.Equal(0, analyze.ExitCode);
        var catalog = await RunAsync("catalog", target, "--repository-root", root, "--cache", cache.Path,
            "--kind", "method", "--json");
        Assert.Equal(0, catalog.ExitCode);
        using var document = JsonDocument.Parse(catalog.Output);
        var items = document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        return items.Single(item => item.GetProperty("detail").GetString() ==
            "BehaviorDocumentation.FourFlows.Services.MutationProbeService.UnsupportedAndUnrelatedProbe()")
            .GetProperty("id").GetString()!;
    }

    private static async Task<string> PortableOnlyMethodIdAsync(string target, TemporaryCache cache)
    {
        var analyze = await RunAsync("analyze", target, "--repository-root", FindRepositoryRoot(), "--cache", cache.Path,
            "--framework", "net10.0", "--json");
        Assert.Equal(0, analyze.ExitCode);
        var catalog = await RunAsync("catalog", target, "--repository-root", FindRepositoryRoot(), "--cache", cache.Path,
            "--framework", "net10.0", "--kind", "method", "--query", "PortableOnly", "--json");
        Assert.Equal(0, catalog.ExitCode);
        using var document = JsonDocument.Parse(catalog.Output);
        var item = Assert.Single(document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        string id = item.GetProperty("id").GetString()!;
        Assert.StartsWith("method:v1:", id, StringComparison.Ordinal);
        Assert.Contains("PortableOnly", item.GetProperty("detail").GetString(), StringComparison.Ordinal);
        return id;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryCache : IDisposable
    {
        public TemporaryCache()
        {
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"seqdoc-cli-{Guid.NewGuid():N}");
            Path = System.IO.Path.Combine(DirectoryPath, "cache.db");
        }

        public string DirectoryPath { get; }

        public string Path { get; }

        public string WriteConfiguration(string contents)
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = System.IO.Path.Combine(DirectoryPath, "seqdoc.yml");
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
