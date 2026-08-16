using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Core.Tests.Identity;

public sealed class RepositoryRelativePathTests
{
    [Theory]
    [InlineData("src\\App\\App.csproj", "src/App/App.csproj")]
    [InlineData("./src//App/App.csproj", "src/App/App.csproj")]
    [InlineData("src/../App.csproj", "App.csproj")]
    public void NormalizeProducesCanonicalLogicalPath(string input, string expected)
    {
        Assert.Equal(expected, RepositoryRelativePath.Normalize(input));
    }

    [Theory]
    [InlineData("C:\\repo\\App.csproj")]
    [InlineData("/repo/App.csproj")]
    [InlineData("../App.csproj")]
    [InlineData(".")]
    public void NormalizeRejectsPathsOutsideLogicalRepository(string input)
    {
        Assert.Throws<ArgumentException>(() => RepositoryRelativePath.Normalize(input));
    }

    [Fact]
    public void NormalizePreservesDistinctUnicodeFileNames()
    {
        const string decomposed = "src/Cafe\u0301/App.csproj";

        Assert.Equal(decomposed, RepositoryRelativePath.Normalize(decomposed));
        Assert.NotEqual("src/Caf\u00e9/App.csproj", RepositoryRelativePath.Normalize(decomposed));
    }
}
