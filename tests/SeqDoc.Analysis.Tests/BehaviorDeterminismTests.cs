using System.Diagnostics;
using System.Text.Json;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class BehaviorDeterminismTests
{
    [Fact]
    public async Task ParallelRunsProduceIdenticalBehaviorFingerprints()
    {
        var request = CreateFixtureRequest("DispatchAndValues");
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => AnalyzeAsync(request)));
        var fingerprints = results.Select(result => result.Value!.BehaviorFingerprint).Distinct().ToArray();

        Assert.Single(fingerprints);
        var first = results[0].Value!;
        var second = results[1].Value!;
        Assert.Equal(
            first.MethodFlows.Select(flow => flow.FlowFingerprint).Order(StringComparer.Ordinal),
            second.MethodFlows.Select(flow => flow.FlowFingerprint).Order(StringComparer.Ordinal));
        Assert.Equal(
            first.CallGraph.Edges.Select(edge => $"{edge.Caller.Value}|{edge.CandidateTarget.Value}").Order(StringComparer.Ordinal),
            second.CallGraph.Edges.Select(edge => $"{edge.Caller.Value}|{edge.CandidateTarget.Value}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PhysicalRelocationDoesNotChangeBehaviorIdentity()
    {
        var source = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "PassB", "DispatchAndValues");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-behavior-relocation-{Guid.NewGuid():N}");
        var firstRoot = Path.Combine(temporaryDirectory, "first");
        var secondRoot = Path.Combine(temporaryDirectory, "second");
        try
        {
            CopyFixture(source, firstRoot);
            CopyFixture(source, secondRoot);
            await RestoreAsync(firstRoot);
            await RestoreAsync(secondRoot);

            var first = await AnalyzeFixtureRootAsync(firstRoot);
            var second = await AnalyzeFixtureRootAsync(secondRoot);

            Assert.Equal(first.BehaviorFingerprint, second.BehaviorFingerprint);
            Assert.Equal(
                first.MethodFlows.Select(flow => flow.Method.Value).Order(StringComparer.Ordinal),
                second.MethodFlows.Select(flow => flow.Method.Value).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(firstRoot, JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secondRoot, JsonSerializer.Serialize(second), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileLocalIdentityRelocationKeepsCanonicalTypesAndFingerprint()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-file-local-{Guid.NewGuid():N}");
        var firstRoot = Path.Combine(temporaryDirectory, "first");
        var secondRoot = Path.Combine(temporaryDirectory, "second");
        try
        {
            WriteFileLocalProject(firstRoot);
            WriteFileLocalProject(secondRoot);
            await RestoreProjectAsync(firstRoot, "FileLocalIdentity.csproj");
            await RestoreProjectAsync(secondRoot, "FileLocalIdentity.csproj");

            var first = await BuildFileLocalAsync(firstRoot);
            var second = await BuildFileLocalAsync(secondRoot);

            Assert.Equal(first.IndexFingerprint, second.IndexFingerprint);
            Assert.Equal(first.InputManifestHash, second.InputManifestHash);
            Assert.Equal(
                first.Types.Select(type => type.Id.Value).Order(StringComparer.Ordinal),
                second.Types.Select(type => type.Id.Value).Order(StringComparer.Ordinal));
            Assert.Equal(
                first.Types.Select(type => type.MetadataName).Order(StringComparer.Ordinal),
                second.Types.Select(type => type.MetadataName).Order(StringComparer.Ordinal));
            Assert.Equal(
                first.Methods.Select(method => method.DisplaySignature).Order(StringComparer.Ordinal),
                second.Methods.Select(method => method.DisplaySignature).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(firstRoot, JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secondRoot, JsonSerializer.Serialize(second), StringComparison.OrdinalIgnoreCase);

            var sameNamedFileLocalTypes = second.Types
                .Where(type => type.MetadataName.Contains("LocalWorker", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(2, sameNamedFileLocalTypes.Length);
            Assert.Equal(2, sameNamedFileLocalTypes.Select(type => type.Id).Distinct().Count());
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static void WriteFileLocalProject(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "FileLocalIdentity.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(root, "FileLocalAlpha.cs"),
            """
            namespace FileLocalIdentity;

            file sealed class LocalWorker
            {
                public string Name => "alpha-worker";
            }

            public sealed class PublicSurface
            {
                public string Describe()
                {
                    var worker = new LocalWorker();
                    return worker.Name;
                }
            }
            """);
        File.WriteAllText(
            Path.Combine(root, "FileLocalBeta.cs"),
            """
            namespace FileLocalIdentity;

            file sealed class LocalWorker
            {
                public string Name => "beta-worker";
            }
            """);
    }

    private static async Task<ProgramIndexSnapshot> BuildFileLocalAsync(string root)
    {
        const string relativePath = "FileLocalIdentity.csproj";
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
        var result = await new RoslynProgramIndexBuilder().BuildAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));
        return Assert.IsType<ProgramIndexSnapshot>(result.Value);
    }

    private static Task RestoreAsync(string root) => RestoreProjectAsync(root, "DispatchAndValues.csproj");

    private static async Task RestoreProjectAsync(string root, string projectFile)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"restore {projectFile} --nologo",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{await output}\n{await error}");
    }

    private static async Task<ApplicationResult<BehaviorSnapshot>> AnalyzeAsync(CompilationAnalysisRequest request)
    {
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        var analyzed = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(
            analyzed.IsSuccess,
            string.Join(Environment.NewLine, analyzed.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return analyzed;
    }

    private static async Task<BehaviorSnapshot> AnalyzeFixtureRootAsync(string root)
    {
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, "DispatchAndValues.csproj"),
            CompilationProfile.Create("DispatchAndValues.csproj", "Release", "net10.0"));
        var result = await AnalyzeAsync(request);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private static CompilationAnalysisRequest CreateFixtureRequest(string name)
    {
        var root = FindRepositoryRoot();
        var relativePath = $"tests/fixtures/PassB/{name}/{name}.csproj";
        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
    }

    private static void CopyFixture(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (Path.GetFileName(file) != "packages.lock.json")
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }
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
