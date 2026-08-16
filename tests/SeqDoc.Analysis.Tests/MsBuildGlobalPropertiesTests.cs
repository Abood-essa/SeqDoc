using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Analysis.Tests;

public sealed class MsBuildGlobalPropertiesTests
{
    [Fact]
    public void WorkspaceProfileDoesNotEraseReferencedProjectFrameworkSets()
    {
        var profile = CompilationProfile.Create("App.csproj", "Release", "net10.0");

        var properties = MsBuildGlobalProperties.CreateForWorkspace(profile);

        Assert.Equal("net10.0", properties["TargetFramework"]);
        Assert.False(properties.ContainsKey("TargetFrameworks"));
    }
}
