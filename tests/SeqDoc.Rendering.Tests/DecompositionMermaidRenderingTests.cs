using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

// I23 coverage for claim 11: the complete production pipeline (DocumentationSetBuilder
// with decomposition enabled + Markdown/Mermaid renderers) must emit structurally valid Mermaid for
// every view, and a REAL mermaid-cli run must accept every emitted .mmd file. Compiles against the
// decomposition surface is exercised directly. The CLI lane follows the I21/I22 acceptance precedent
// (`npx @mermaid-js/mermaid-cli` per .mmd to SVG with exit 0 and non-empty SVG) and is skipped by
// early return when node/npx is unavailable on PATH.
public sealed class DecompositionMermaidRenderingTests
{
    [Fact]
    public void EveryEmittedViewMermaidPassesStructuralValidation()
    {
        var built = BuildDecomposedSet();

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        var mermaidFiles = built.Files
            .Where(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal))
            .ToArray();
        Assert.True(
            mermaidFiles.Length >= 3,
            $"Expected an overview plus at least two children but found {mermaidFiles.Length} mermaid files.");
        Assert.All(
            mermaidFiles,
            file => Assert.Empty(MermaidValidator.Validate(Encoding.UTF8.GetString(file.Content))));
    }

    [Fact]
    public async Task RealMermaidCliRendersEveryEmittedViewToSvg()
    {
        string? npx = FindOnPath("npx");
        if (npx is null || FindOnPath("node") is null)
        {
            // Skipped: real mermaid-cli acceptance requires node and npx on PATH.
            return;
        }

        var built = BuildDecomposedSet();
        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        var mermaidFiles = built.Files
            .Where(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal))
            .ToArray();
        Assert.True(mermaidFiles.Length >= 3);

        string workDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-decomp-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        try
        {
            foreach (var file in mermaidFiles)
            {
                string mmdPath = Path.Combine(workDirectory, file.RelativePath);
                await File.WriteAllTextAsync(mmdPath, Encoding.UTF8.GetString(file.Content));
                string svgPath = Path.ChangeExtension(mmdPath, ".svg");

                var startInfo = new ProcessStartInfo
                {
                    FileName = npx,
                    WorkingDirectory = workDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                startInfo.ArgumentList.Add("-y");
                startInfo.ArgumentList.Add("@mermaid-js/mermaid-cli");
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(mmdPath);
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add(svgPath);

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to launch npx for mermaid-cli.");
                string output = await process.StandardOutput.ReadToEndAsync()
                    + await process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw new InvalidOperationException($"mermaid-cli timed out for {file.RelativePath}.\n{output}");
                }

                Assert.True(
                    process.ExitCode == 0,
                    $"mermaid-cli failed for {file.RelativePath} with exit code {process.ExitCode}:\n{output}");
                Assert.True(File.Exists(svgPath), $"Expected rendered SVG at {svgPath}.");
                Assert.True(new FileInfo(svgPath).Length > 0, $"Rendered SVG was empty for {file.RelativePath}.");
            }
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    // --------------------------------------------------------------------------------------------

    private static DocumentationSetBuildResult BuildDecomposedSet()
        => DocumentationSetBuilder.Build(
            "profile:v1:test",
            "fingerprint",
            [new DocumentSetEntry(
                DecompositionTestPlans.BaseFileName,
                DecompositionTestPlans.CreateWording(),
                DecompositionTestPlans.CreateDecomposablePlan())],
            DecompositionTestPlans.SplittingBudget(DecompositionTestPlans.CreateDecomposablePlan()),
            new DiagramDecompositionOptions(Enabled: true));

    private static string? FindOnPath(string fileName)
    {
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return directories
            .SelectMany(directory => new[]
            {
                // Platform-executable shims must win over extensionless POSIX scripts; on Windows
                // Process.Start cannot launch the bare 'npx' shell script.
                Path.Combine(directory, $"{fileName}.exe"),
                Path.Combine(directory, $"{fileName}.cmd"),
                Path.Combine(directory, fileName),
            })
            .FirstOrDefault(File.Exists);
    }
}
