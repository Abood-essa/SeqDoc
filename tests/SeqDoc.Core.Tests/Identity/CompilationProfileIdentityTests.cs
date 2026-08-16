using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Core.Tests.Identity;

public sealed class CompilationProfileIdentityTests
{
    private const string ExpectedCanonicalJson =
        "{\"schemaVersion\":1,\"repositoryRelativeTargetPath\":\"src/App/App.csproj\",\"configuration\":\"Release\",\"targetFramework\":\"net10.0\",\"runtimeIdentifier\":null,\"msBuildProperties\":{\"A\":\"1\",\"Z\":\"2\"},\"analysisProperties\":{\"Environment\":\"Production\"}}";

    private const string ExpectedProfileId =
        "profile:v1:ccdcd2a07ad2b9252116914fbb1c23c791a2a6f5a91ed03b3c7b1a99d42e6b4a";

    [Fact]
    public void CreateMatchesGoldenCanonicalDescriptorAndId()
    {
        var profile = CreateGoldenProfile();

        Assert.Equal(ExpectedCanonicalJson, profile.CanonicalJson);
        Assert.Equal(ExpectedProfileId, profile.Id.Value);
    }

    [Fact]
    public void CreateIsIndependentOfPropertyOrderingAndMsBuildKeyCasing()
    {
        var first = CompilationProfile.Create(
            "src/App/App.csproj",
            "Release",
            "net10.0",
            msBuildProperties: [KeyValuePair.Create("z", "2"), KeyValuePair.Create("a", "1")],
            analysisProperties: [KeyValuePair.Create("Environment", "Production")]);
        var second = CompilationProfile.Create(
            "src\\App\\App.csproj",
            "Release",
            "net10.0",
            msBuildProperties: [KeyValuePair.Create("A", "1"), KeyValuePair.Create("Z", "2")],
            analysisProperties: [KeyValuePair.Create("Environment", "Production")]);

        Assert.Equal(first.CanonicalJson, second.CanonicalJson);
        Assert.Equal(first.Id, second.Id);
    }

    [Theory]
    [InlineData("Debug", "net10.0", null)]
    [InlineData("Release", "net9.0", null)]
    [InlineData("Release", "net10.0", "win-x64")]
    public void BehaviorAffectingProfileValuesChangeIdentity(
        string configuration,
        string targetFramework,
        string? runtimeIdentifier)
    {
        var changed = CompilationProfile.Create(
            "src/App/App.csproj",
            configuration,
            targetFramework,
            runtimeIdentifier,
            [KeyValuePair.Create("A", "1"), KeyValuePair.Create("Z", "2")],
            [KeyValuePair.Create("Environment", "Production")]);

        Assert.NotEqual(ExpectedProfileId, changed.Id.Value);
    }

    [Fact]
    public void DuplicateMsBuildPropertyNamesFailRegardlessOfCasing()
    {
        Assert.Throws<ArgumentException>(() => CompilationProfile.Create(
            "src/App/App.csproj",
            "Release",
            "net10.0",
            msBuildProperties: [KeyValuePair.Create("Feature", "on"), KeyValuePair.Create("FEATURE", "off")]));
    }

    [Fact]
    public void ProjectIdentityMatchesGoldenVectorAndExcludesCheckoutPath()
    {
        var projectId = StableIdentity.CreateProjectId(CreateGoldenProfile().Id, "src\\App\\App.csproj");

        Assert.Equal(
            "project:v1:9893037d924d109c75619a7bfbaaf9f5051c0c976f28426a0656e9881bf1842e",
            projectId.Value);
        Assert.DoesNotContain("C:", projectId.Value, StringComparison.Ordinal);
    }

    private static CompilationProfile CreateGoldenProfile()
    {
        return CompilationProfile.Create(
            "src/App/App.csproj",
            "Release",
            "net10.0",
            msBuildProperties: [KeyValuePair.Create("Z", "2"), KeyValuePair.Create("A", "1")],
            analysisProperties: [KeyValuePair.Create("Environment", "Production")]);
    }
}
