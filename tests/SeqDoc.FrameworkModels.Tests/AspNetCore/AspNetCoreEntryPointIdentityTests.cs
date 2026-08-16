using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

/// <summary>
/// Golden and sensitivity coverage for the canonical HTTP entry-point identity. The golden vector is a
/// compatibility contract: identical semantic inputs must produce identical bytes on every platform,
/// so changing the canonical descriptor or hash prefix is a breaking change. The HTTP method is typed
/// and serialized through a canonical uppercase token, so differently cased inputs cannot create
/// distinct identities for one method.
/// </summary>
public sealed class AspNetCoreEntryPointIdentityTests
{
    private static HttpEntryPointIdentityDescriptor CreateDescriptor()
        => new(
            Profile: new CompilationProfileId("profile:v1:test"),
            RootMethod: new MethodId("method:v1:test"),
            HttpMethod: HttpMethodKind.Get,
            CanonicalRoute: "api/Orders/{id:guid}");

    [Fact]
    public void GoldenVectorIsDeterministicAndVersioned()
    {
        var first = StableIdentity.CreateEntryPointId(CreateDescriptor());

        Assert.Equal(first, StableIdentity.CreateEntryPointId(CreateDescriptor()));
        Assert.StartsWith("entry-point:v1:", first.Value, StringComparison.Ordinal);
        Assert.Equal(64, first.Value.Length - "entry-point:v1:".Length);
        // Compatibility vector: identical semantic inputs must produce identical bytes on every
        // platform, so this value is a locked contract and must not change.
        Assert.Equal(
            "entry-point:v1:0fa805ba4b7c89bad37345c45c9adb75882d2cac02487ae81ed784943e92eaff",
            first.Value);
    }

    [Fact]
    public void IdentityChangesWhenAnySemanticInputChanges()
    {
        var baseline = StableIdentity.CreateEntryPointId(CreateDescriptor());

        Assert.NotEqual(
            baseline,
            StableIdentity.CreateEntryPointId(CreateDescriptor() with { Profile = new CompilationProfileId("profile:v1:other") }));
        Assert.NotEqual(
            baseline,
            StableIdentity.CreateEntryPointId(CreateDescriptor() with { RootMethod = new MethodId("method:v1:other") }));
        Assert.NotEqual(
            baseline,
            StableIdentity.CreateEntryPointId(CreateDescriptor() with { HttpMethod = HttpMethodKind.Post }));
        Assert.NotEqual(
            baseline,
            StableIdentity.CreateEntryPointId(CreateDescriptor() with { CanonicalRoute = "api/Orders/other" }));
    }

    [Fact]
    public void SameRootWithDifferentRoutesProducesDistinctIdentities()
    {
        var get = StableIdentity.CreateEntryPointId(CreateDescriptor() with { CanonicalRoute = "api/Orders/{id:guid}" });
        var alternate = StableIdentity.CreateEntryPointId(CreateDescriptor() with { CanonicalRoute = "api/Orders/alternate" });

        Assert.NotEqual(get, alternate);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("rootMethod")]
    [InlineData("canonicalRoute")]
    public void IdentityRejectsBlankSemanticInputs(string field)
    {
        var descriptor = field switch
        {
            "profile" => CreateDescriptor() with { Profile = new CompilationProfileId(" ") },
            "rootMethod" => CreateDescriptor() with { RootMethod = new MethodId(" ") },
            _ => CreateDescriptor() with { CanonicalRoute = " " },
        };

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateEntryPointId(descriptor));
    }

    [Fact]
    public void IdentityRejectsUndefinedHttpMethodValue()
    {
        var descriptor = CreateDescriptor() with { HttpMethod = (HttpMethodKind)99 };

        Assert.Throws<ArgumentOutOfRangeException>(() => StableIdentity.CreateEntryPointId(descriptor));
    }
}
