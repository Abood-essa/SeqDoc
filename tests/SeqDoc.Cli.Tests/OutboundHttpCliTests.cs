using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SeqDoc.Cli.Tests;

/// <summary>
/// First observable consumer proof for issue 54: a configured scenario root whose Method Flow
/// contains one supported direct <c>HttpClient.GetAsync(string)</c> / <c>PostAsync(string, HttpContent)</c>
/// call must produce the exact frozen behavior phrase and the exact frozen Mermaid message in the
/// generated Markdown for both supported profiles (<c>net9.0</c> and <c>net10.0</c>).
/// These two tests are RED until the seven production files are implemented.
/// </summary>
public sealed class OutboundHttpCliTests
{
    private static readonly string[] SupportedFrameworks = ["net9.0", "net10.0"];

    [Fact]
    public async Task GetAsyncStringProducesGeneratedBoundary()
    {
        foreach (string framework in SupportedFrameworks)
        {
            string generated = await GenerateConfiguredRootDocumentAsync(
                framework, "BehaviorDocumentation.OutboundHttp.SupportedRequests.Get()");

            Assert.Contains(
                "The method calls HttpClient.GetAsync at an outbound HTTP GET request boundary.",
                generated,
                StringComparison.Ordinal);
            Assert.Contains("HTTP GET request", generated, StringComparison.Ordinal);
            Assert.Contains("HTTP boundary", generated, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PostAsyncStringContentProducesGeneratedBoundary()
    {
        foreach (string framework in SupportedFrameworks)
        {
            string generated = await GenerateConfiguredRootDocumentAsync(
                framework, "BehaviorDocumentation.OutboundHttp.SupportedRequests.Post()");

            Assert.Contains(
                "The method calls HttpClient.PostAsync at an outbound HTTP POST request boundary.",
                generated,
                StringComparison.Ordinal);
            Assert.Contains("HTTP POST request", generated, StringComparison.Ordinal);
            Assert.Contains("HTTP boundary", generated, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LocalOutputIsByteIdenticalAcrossIndependentRuns()
    {
        string[] roots =
        [
            "BehaviorDocumentation.OutboundHttp.SupportedRequests.Get()",
            "BehaviorDocumentation.OutboundHttp.SupportedRequests.Post()",
        ];

        foreach (string framework in SupportedFrameworks)
        {
            var run1 = await GenerateOutputTreeAsync(framework, roots);
            var run2 = await GenerateOutputTreeAsync(framework, roots);

            Assert.Equal(run1.RelativePaths, run2.RelativePaths);
            foreach (string relative in run1.RelativePaths)
            {
                Assert.Equal(
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(run1.Files[relative])),
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(run2.Files[relative])));
            }

            Assert.Equal(run1.DiagnosticSequence, run2.DiagnosticSequence);

            // The generated Markdown + Mermaid must never carry the fixture's credential-shaped
            // constants, URI/host/body values, or any response/status/retry claim for the HTTP node.
            string[] forbidden =
            [
                "AKIA" + "IOSFODNN7EXAMPLE", "sk_" + "live_", "access_token", "{\"ping\":true}",
                "example.test", "Bearer ", "Authorization",
            ];
            foreach (string relative in run1.RelativePaths)
            {
                if (!relative.EndsWith(".md", StringComparison.Ordinal)
                    && !relative.EndsWith(".mmd", StringComparison.Ordinal))
                {
                    continue;
                }

                string text = System.Text.Encoding.UTF8.GetString(run1.Files[relative]);
                foreach (string needle in forbidden)
                {
                    Assert.DoesNotContain(needle, text, StringComparison.Ordinal);
                }

                // Response/status/retry claim wording is forbidden on any line that mentions the HTTP node.
                foreach (string line in text.Split('\n').Where(l => l.Contains("HTTP", StringComparison.Ordinal)))
                {
                    foreach (string claim in new[] { "response", "status", "retry", "success", "completed" })
                    {
                        Assert.DoesNotContain(claim, line, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
    }

    private static async Task<OutputTree> GenerateOutputTreeAsync(string framework, string[] methodDetails)
    {
        string root = FindRepositoryRoot();
        string target = OutboundHttpFixture(root);
        using var cache = new TemporaryCache();

        var analyze = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--framework", framework, "--json");
        Assert.Equal(0, analyze.ExitCode);

        var catalog = await RunAsync(
            "catalog", target, "--repository-root", root, "--cache", cache.Path,
            "--framework", framework, "--kind", "method", "--json");
        Assert.Equal(0, catalog.ExitCode);
        using var document = JsonDocument.Parse(catalog.Output);
        var items = document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        string[] methodIds = methodDetails
            .Select(detail => items.Single(item => item.GetProperty("detail").GetString() == detail)
                .GetProperty("id").GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        string config = cache.WriteConfiguration(
            "schemaVersion: 1\nselection:\n  roots:\n" + string.Join("\n", methodIds.Select(id => $"    - {id}")));
        string output = System.IO.Path.Combine(cache.DirectoryPath, $"docs-{framework}");

        var generate = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--framework", framework, "--config", config, "--output", output, "--json");
        Assert.Equal(0, generate.ExitCode);

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
        {
            string relative = System.IO.Path.GetRelativePath(output, file).Replace('\\', '/');
            if (relative.EndsWith(".md", StringComparison.Ordinal)
                || relative.EndsWith(".mmd", StringComparison.Ordinal)
                || relative == "seqdoc.manifest.json")
            {
                files[relative] = File.ReadAllBytes(file);
            }
        }

        using var generateDocument = JsonDocument.Parse(generate.Output);
        string diagnosticSequence = generateDocument.RootElement.TryGetProperty("diagnostics", out var diagnostics)
            ? string.Join("|", diagnostics.EnumerateArray().Select(d => d.GetRawText()))
            : string.Empty;

        return new OutputTree(
            files.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
            files,
            diagnosticSequence);
    }

    private sealed record OutputTree(
        string[] RelativePaths,
        IReadOnlyDictionary<string, byte[]> Files,
        string DiagnosticSequence);

    private static async Task<string> GenerateConfiguredRootDocumentAsync(string framework, string methodDetail)
    {
        string root = FindRepositoryRoot();
        string target = OutboundHttpFixture(root);
        using var cache = new TemporaryCache();

        var analyze = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path, "--framework", framework, "--json");
        Assert.True(analyze.ExitCode == 0, $"analyze seed failed: exit {analyze.ExitCode}\n{analyze.Output}\n{analyze.Error}");

        var catalog = await RunAsync(
            "catalog", target, "--repository-root", root, "--cache", cache.Path,
            "--framework", framework, "--kind", "method", "--json");
        Assert.Equal(0, catalog.ExitCode);
        using var document = JsonDocument.Parse(catalog.Output);
        string methodId = document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("detail").GetString() == methodDetail)
            .GetProperty("id").GetString()!;

        string config = cache.WriteConfiguration($"""
            schemaVersion: 1
            selection:
              roots:
                - {methodId}
            """);
        string output = System.IO.Path.Combine(cache.DirectoryPath, $"docs-{framework}");

        var generate = await RunAsync(
            "analyze", target, "--repository-root", root, "--cache", cache.Path,
            "--framework", framework, "--config", config, "--output", output, "--json");
        Assert.True(generate.ExitCode == 0, $"analyze generate failed: exit {generate.ExitCode}\n{generate.Output}\n{generate.Error}");

        string documentPath = Directory.GetFiles(output, "*.md", SearchOption.TopDirectoryOnly)
            .Where(file => !string.Equals(System.IO.Path.GetFileName(file), "index.md", StringComparison.Ordinal))
            .Single(file => File.ReadAllText(file).Contains(methodDetail, StringComparison.Ordinal));
        return File.ReadAllText(documentPath);
    }

    private static string OutboundHttpFixture(string root) =>
        System.IO.Path.Combine(root, "tests", "fixtures", "BehaviorDocumentation", "OutboundHttp", "OutboundHttp.csproj");

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
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"seqdoc-http-{Guid.NewGuid():N}");
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
