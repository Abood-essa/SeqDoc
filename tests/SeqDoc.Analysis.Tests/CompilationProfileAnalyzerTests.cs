using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class CompilationProfileAnalyzerTests
{
    [Fact]
    public async Task ValidProjectReturnsCompiledProjectSummary()
    {
        var request = CreateRequest("RelocatableIdentity");
        var result = await new RoslynCompilationProfileAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(ApplicationOutcome.Succeeded, result.Outcome);
        var project = Assert.Single(Assert.IsType<CompilationAnalysisSummary>(result.Value).Projects);
        Assert.Equal("RelocatableIdentity", project.Name);
        Assert.Equal("tests/fixtures/PassA/RelocatableIdentity/RelocatableIdentity.csproj", project.RepositoryRelativePath);
        Assert.DoesNotContain(request.RepositoryRoot, project.AssemblyIdentity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrokenProjectReturnsEveryCompilerErrorAndNoValue()
    {
        var request = CreateRequest("BrokenCompilation");
        var result = await new RoslynCompilationProfileAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(ApplicationOutcome.BuildFailure, result.Outcome);
        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CS0246" && diagnostic.Summary.Contains("MissingResult", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CS0246" && diagnostic.Summary.Contains("UnknownRequest", StringComparison.Ordinal));
        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.NotEmpty(diagnostic.TechnicalCause);
            Assert.NotEmpty(diagnostic.UserImpact);
            Assert.NotEmpty(diagnostic.NextAction);
        });
    }

    [Fact]
    public async Task MissingTargetReturnsTypedInvalidInput()
    {
        var root = FindRepositoryRoot();
        var profile = CompilationProfile.Create("missing.csproj", "Release", "net10.0");
        var request = new CompilationAnalysisRequest(root, Path.Combine(root, "missing.csproj"), profile);

        var result = await new RoslynCompilationProfileAnalyzer().AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(ApplicationOutcome.InvalidInput, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal("SD1002", Assert.Single(result.Diagnostics).Code);
    }

    private static CompilationAnalysisRequest CreateRequest(string fixtureName)
    {
        var root = FindRepositoryRoot();
        var relativePath = $"tests/fixtures/PassA/{fixtureName}/{fixtureName}.csproj";
        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(relativePath, "Release", "net10.0"));
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
