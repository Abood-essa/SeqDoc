using System.Collections.Immutable;
using System.Text;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Profiles;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class MultiTargetProgramIndexTests
{
    [Theory]
    [InlineData(".NETCoreApp,Version=v8.0", "Windows", "7.0", "net8.0-windows7.0")]
    [InlineData(".NETFramework,Version=v4.8", null, null, "net48")]
    [InlineData(".NETFramework,Version=v4.7.2", null, null, "net472")]
    public void CompilerTargetFrameworkAttributesMapToCanonicalFramework(
        string moniker,
        string? platform,
        string? platformVersion,
        string expected) =>
        Assert.Equal(expected, CompilationWorkspaceLoader.CanonicalTargetFramework(
            moniker, platform, platformVersion, projectFramework: "net8.0-windows"));

    [Fact]
    public async Task ConditionalSourcesReferencesAndFingerprintsRemainProfileIsolated()
    {
        var request = CompilationProfileResolverTests.CreateRequest() with { AllTargetFrameworks = true };
        var resolved = await new MsBuildCompilationProfileResolver().ResolveAsync(request, CancellationToken.None);
        var profiles = resolved.Value!.Profiles;
        var builder = new RoslynProgramIndexBuilder();

        var portableProfile = Assert.Single(profiles, profile => profile.TargetFramework == "net10.0");
        var windowsProfile = Assert.Single(profiles, profile => profile.TargetFramework == "net10.0-windows");
        var portable = await builder.BuildAsync(CreateAnalysisRequest(request, portableProfile), CancellationToken.None);
        var windows = await builder.BuildAsync(CreateAnalysisRequest(request, windowsProfile), CancellationToken.None);
        AssertSucceeded(portable);
        AssertSucceeded(windows);
        var portableIndex = portable.Value!;
        var windowsIndex = windows.Value!;

        Assert.Contains(portableIndex.Types, type => type.MetadataName == "MultiTargetProfiles.PortableOnly");
        Assert.Contains(portableIndex.Types, type => type.MetadataName == "MultiTargetProfiles.PortableSymbolOnly");
        Assert.DoesNotContain(portableIndex.Types, type => type.MetadataName == "MultiTargetProfiles.WindowsOnly");
        Assert.Contains(windowsIndex.Types, type => type.MetadataName == "MultiTargetProfiles.WindowsOnly");
        Assert.Contains(windowsIndex.Types, type => type.MetadataName == "MultiTargetProfiles.WindowsSymbolOnly");
        Assert.DoesNotContain(windowsIndex.Types, type => type.MetadataName == "MultiTargetProfiles.PortableOnly");
        Assert.Contains(portableIndex.Projects, project => project.Name == "PortableDependency");
        Assert.DoesNotContain(portableIndex.Projects, project => project.Name == "WindowsDependency");
        Assert.Contains(windowsIndex.Projects, project => project.Name == "WindowsDependency");
        Assert.DoesNotContain(windowsIndex.Projects, project => project.Name == "PortableDependency");
        Assert.Contains(windowsIndex.Types, type => type.MetadataName == "WindowsDependency.DependencyEvaluatedAsPortable");
        Assert.DoesNotContain(windowsIndex.Types, type => type.MetadataName == "WindowsDependency.DependencyEvaluatedAsWindows");
        Assert.Equal("net10.0-windows7.0", windowsIndex.Projects.Single(project => project.Name == "WindowsDependency").TargetFramework);
        Assert.Contains(portableIndex.References, reference => reference.Kind == ProgramReferenceKind.Package && reference.Identity == "YamlDotNet");
        Assert.DoesNotContain(portableIndex.References, reference => reference.Kind == ProgramReferenceKind.Package && reference.Identity == "System.CommandLine");
        Assert.Contains(windowsIndex.References, reference => reference.Kind == ProgramReferenceKind.Package && reference.Identity == "System.CommandLine");
        Assert.DoesNotContain(windowsIndex.References, reference => reference.Kind == ProgramReferenceKind.Package && reference.Identity == "YamlDotNet");
        Assert.NotEqual(portableIndex.Profile.Id, windowsIndex.Profile.Id);
        Assert.NotEqual(portableIndex.InputManifestHash, windowsIndex.InputManifestHash);
        Assert.NotEqual(portableIndex.IndexFingerprint, windowsIndex.IndexFingerprint);
        Assert.All(portableIndex.Projects, project => Assert.Equal(portableIndex.Profile.Id, project.Profile));
        Assert.All(windowsIndex.Projects, project => Assert.Equal(windowsIndex.Profile.Id, project.Profile));

        var repeatedPortable = await builder.BuildAsync(CreateAnalysisRequest(request, portableProfile), CancellationToken.None);
        var repeatedWindows = await builder.BuildAsync(CreateAnalysisRequest(request, windowsProfile), CancellationToken.None);
        Assert.Equal(portableIndex.IndexFingerprint, repeatedPortable.Value!.IndexFingerprint);
        Assert.Equal(windowsIndex.IndexFingerprint, repeatedWindows.Value!.IndexFingerprint);

        var portableGenerated = portableIndex.Documents
            .Where(document => document.Origin == DocumentOrigin.GeneratedSource)
            .Select(document => document.ContentFingerprint)
            .Order(StringComparer.Ordinal);
        var windowsGenerated = windowsIndex.Documents
            .Where(document => document.Origin == DocumentOrigin.GeneratedSource)
            .Select(document => document.ContentFingerprint)
            .Order(StringComparer.Ordinal);
        Assert.False(portableGenerated.SequenceEqual(windowsGenerated));
    }

    [Fact]
    public async Task CompilerFailureInOneProfileDoesNotContaminateAnother()
    {
        var properties = ImmutableSortedDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase)
            .Add("BreakSelectedProfile", "true");
        var request = CompilationProfileResolverTests.CreateRequest(properties) with { AllTargetFrameworks = true };
        var resolved = await new MsBuildCompilationProfileResolver().ResolveAsync(request, CancellationToken.None);
        var builder = new RoslynProgramIndexBuilder();
        var portableProfile = Assert.Single(resolved.Value!.Profiles, profile => profile.TargetFramework == "net10.0");
        var windowsProfile = Assert.Single(resolved.Value.Profiles, profile => profile.TargetFramework == "net10.0-windows");

        var portable = await builder.BuildAsync(CreateAnalysisRequest(request, portableProfile), CancellationToken.None);
        var windows = await builder.BuildAsync(CreateAnalysisRequest(request, windowsProfile), CancellationToken.None);

        Assert.Equal(ApplicationOutcome.BuildFailure, portable.Outcome);
        Assert.Null(portable.Value);
        Assert.Contains(portable.Diagnostics, diagnostic => diagnostic.Code == "CS0246");
        AssertSucceeded(windows);
        Assert.NotNull(windows.Value);
    }

    [Fact]
    public async Task ReferencedProjectWithoutLocalAssetsKeepsItsEvaluatedFramework()
    {
        var request = CompilationProfileResolverTests.CreateRequest();
        var resolved = await new MsBuildCompilationProfileResolver().ResolveAsync(
            request with { TargetFramework = "net10.0-windows" }, CancellationToken.None);
        var profile = Assert.Single(resolved.Value!.Profiles);
        var dependencyDirectory = Path.Combine(
            Path.GetDirectoryName(request.TargetPath)!,
            "References",
            "WindowsDependency");
        var lockPath = Path.Combine(dependencyDirectory, "packages.lock.json");
        var savedLock = File.ReadAllBytes(lockPath);
        var localAssets = Directory.Exists(Path.Combine(dependencyDirectory, "obj"))
            ? Directory.GetFiles(Path.Combine(dependencyDirectory, "obj"), "project.assets.json", SearchOption.AllDirectories)
            : [];
        var savedAssets = localAssets.ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

        try
        {
            File.WriteAllText(lockPath, SyntheticWindowsDependencyLock, Encoding.UTF8);
            foreach (var path in localAssets)
            {
                File.Delete(path);
            }

            var result = await new RoslynProgramIndexBuilder().BuildAsync(
                CreateAnalysisRequest(request, profile), CancellationToken.None);

            AssertSucceeded(result);
            Assert.Equal(
                "net10.0-windows7.0",
                result.Value!.Projects.Single(project => project.Name == "WindowsDependency").TargetFramework);
            Assert.Contains(result.Value.References, reference =>
                reference.Identity == "WindowsDependency.WindowsLock");
            Assert.DoesNotContain(result.Value.References, reference =>
                reference.Identity == "WindowsDependency.PortableLock");
        }
        finally
        {
            File.WriteAllBytes(lockPath, savedLock);
            foreach (var (path, contents) in savedAssets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, contents);
            }
        }
    }

    [Fact]
    public async Task ProfileBuildOrderDoesNotChangeReferencedFrameworkOrIndexIdentity()
    {
        var request = CompilationProfileResolverTests.CreateRequest() with { AllTargetFrameworks = true };
        var resolved = await new MsBuildCompilationProfileResolver().ResolveAsync(request, CancellationToken.None);
        var profiles = resolved.Value!.Profiles.ToDictionary(profile => profile.TargetFramework, StringComparer.Ordinal);

        async Task<ProgramIndexSnapshot> Build(string targetFramework) =>
            (await new RoslynProgramIndexBuilder().BuildAsync(
                CreateAnalysisRequest(request, profiles[targetFramework]), CancellationToken.None)).Value!;

        var windowsFirst = await Build("net10.0-windows");
        var portableAfterWindows = await Build("net10.0");
        var portableFirst = await Build("net10.0");
        var windowsAfterPortable = await Build("net10.0-windows");

        Assert.Equal("net10.0-windows7.0", windowsFirst.Projects.Single(project => project.Name == "WindowsDependency").TargetFramework);
        Assert.Equal("net10.0-windows7.0", windowsAfterPortable.Projects.Single(project => project.Name == "WindowsDependency").TargetFramework);
        Assert.Equal(windowsFirst.IndexFingerprint, windowsAfterPortable.IndexFingerprint);
        Assert.Equal(portableFirst.IndexFingerprint, portableAfterWindows.IndexFingerprint);
    }

    private static CompilationAnalysisRequest CreateAnalysisRequest(
        CompilationProfileResolutionRequest request,
        CompilationProfile profile) =>
        new(request.RepositoryRoot, request.TargetPath, profile);

    private const string SyntheticWindowsDependencyLock = """
        {
          "version": 2,
          "dependencies": {
            "net10.0": {
              "WindowsDependency.PortableLock": {
                "type": "Direct",
                "resolved": "1.0.0"
              }
            },
            "net10.0-windows7.0": {
              "WindowsDependency.WindowsLock": {
                "type": "Direct",
                "resolved": "2.0.0"
              }
            }
          }
        }
        """;

    private static void AssertSucceeded(ApplicationResult<ProgramIndexSnapshot> result) =>
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));
}
