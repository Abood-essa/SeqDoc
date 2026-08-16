using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn.Profiles;
using SeqDoc.Application.Analysis;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class CompilationProfileResolverTests
{
    private static readonly string[] ExpectedFrameworks = ["net10.0", "net10.0-windows"];

    [Fact]
    public async Task ImportedFrameworksRequireExplicitSelectionAndResolveDeterministically()
    {
        var request = CreateRequest();
        var resolver = new MsBuildCompilationProfileResolver();

        var ambiguous = await resolver.ResolveAsync(request, CancellationToken.None);
        Assert.Equal(ApplicationOutcome.InvalidInput, ambiguous.Outcome);
        Assert.Equal("SD1008", Assert.Single(ambiguous.Diagnostics).Code);

        var all = await resolver.ResolveAsync(request with { AllTargetFrameworks = true }, CancellationToken.None);
        Assert.Equal(ApplicationOutcome.Succeeded, all.Outcome);
        var value = Assert.IsType<ResolvedCompilationProfiles>(all.Value);
        Assert.Equal(ExpectedFrameworks, value.AvailableTargetFrameworks.ToArray());
        Assert.Equal(ExpectedFrameworks, value.Profiles.Select(profile => profile.TargetFramework).ToArray());
        Assert.Equal(2, value.Profiles.Select(profile => profile.Id).Distinct().Count());

        var repeated = await resolver.ResolveAsync(request with { AllTargetFrameworks = true }, CancellationToken.None);
        Assert.Equal(value.Profiles.Select(profile => profile.Id), repeated.Value!.Profiles.Select(profile => profile.Id));
    }

    [Fact]
    public async Task ExplicitSelectionIsCaseInsensitiveAndUnavailableSelectionFails()
    {
        var resolver = new MsBuildCompilationProfileResolver();
        var selected = await resolver.ResolveAsync(
            CreateRequest() with { TargetFramework = "NET10.0-WINDOWS" },
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.Succeeded, selected.Outcome);
        Assert.Equal("net10.0-windows", Assert.Single(selected.Value!.Profiles).TargetFramework);

        var unavailable = await resolver.ResolveAsync(
            CreateRequest() with { TargetFramework = "net9.0" },
            CancellationToken.None);
        Assert.Equal(ApplicationOutcome.InvalidInput, unavailable.Outcome);
        Assert.Equal("SD1009", Assert.Single(unavailable.Diagnostics).Code);
    }

    [Fact]
    public async Task HomogeneousSolutionDiscoversTheSameEvaluatedFrameworks()
    {
        var projectRequest = CreateRequest();
        var solutionRequest = projectRequest with
        {
            TargetPath = Path.Combine(
                Path.GetDirectoryName(projectRequest.TargetPath)!,
                "MultiTargetProfiles.slnx"),
            AllTargetFrameworks = true,
        };

        var result = await new MsBuildCompilationProfileResolver().ResolveAsync(
            solutionRequest,
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.Succeeded, result.Outcome);
        Assert.Equal(ExpectedFrameworks, result.Value!.Profiles.Select(profile => profile.TargetFramework).ToArray());
    }

    [Fact]
    public async Task ConflictingSelectionAndReservedPropertiesFailBeforeEvaluation()
    {
        var resolver = new MsBuildCompilationProfileResolver();
        var conflicting = await resolver.ResolveAsync(
            CreateRequest() with { TargetFramework = "net10.0", AllTargetFrameworks = true },
            CancellationToken.None);
        Assert.Equal("SD1006", Assert.Single(conflicting.Diagnostics).Code);

        var properties = ImmutableSortedDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase)
            .Add("TargetFramework", "net10.0");
        var reserved = await resolver.ResolveAsync(
            CreateRequest() with { MsBuildProperties = properties },
            CancellationToken.None);
        Assert.Equal("SD1007", Assert.Single(reserved.Diagnostics).Code);
    }

    [Fact]
    public async Task SingleTargetProjectResolvesImplicitlyAndCancellationIsTyped()
    {
        var root = FindRepositoryRoot();
        const string relative = "tests/fixtures/PassA/RelocatableIdentity/RelocatableIdentity.csproj";
        var request = new CompilationProfileResolutionRequest(
            root,
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)),
            "Release");
        var resolver = new MsBuildCompilationProfileResolver();

        var result = await resolver.ResolveAsync(request, CancellationToken.None);
        Assert.Equal("net10.0", Assert.Single(result.Value!.Profiles).TargetFramework);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await resolver.ResolveAsync(request, cancellation.Token);
        Assert.Equal(ApplicationOutcome.Cancelled, cancelled.Outcome);
    }

    internal static CompilationProfileResolutionRequest CreateRequest(
        ImmutableSortedDictionary<string, string>? properties = null)
    {
        var root = FindRepositoryRoot();
        const string relative = "tests/fixtures/PassA/MultiTargetProfiles/MultiTargetProfiles.csproj";
        return new CompilationProfileResolutionRequest(
            root,
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)),
            "Release",
            MsBuildProperties: properties);
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
