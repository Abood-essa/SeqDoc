using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Profiles;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class MultiTargetProgramIndexTests
{
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
        Assert.Equal("net10.0", windowsIndex.Projects.Single(project => project.Name == "WindowsDependency").TargetFramework);
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

    private static CompilationAnalysisRequest CreateAnalysisRequest(
        CompilationProfileResolutionRequest request,
        CompilationProfile profile) =>
        new(request.RepositoryRoot, request.TargetPath, profile);

    private static void AssertSucceeded(ApplicationResult<ProgramIndexSnapshot> result) =>
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));
}
