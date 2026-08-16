using Microsoft.CodeAnalysis;
using SeqDoc.Analysis.Roslyn.Workspace;
using Xunit;

namespace SeqDoc.Analysis.Tests;

public sealed class WorkspaceDiagnosticClassifierTests
{
    [Fact]
    public void NuGetAuditWarningMisreportedByRoslynIsNonFatal()
    {
        var diagnostic = new WorkspaceDiagnostic(
            WorkspaceDiagnosticKind.Failure,
            "Msbuild failed when processing the file 'C:\\source\\App.csproj' with message: Package 'Example.Package' 1.2.3 has a known high severity vulnerability, https://github.com/advisories/GHSA-1234-abcd-5678");

        Assert.Equal(
            WorkspaceDiagnosticKind.Warning,
            WorkspaceDiagnosticClassifier.GetEffectiveKind(diagnostic));
        Assert.False(WorkspaceDiagnosticClassifier.HasFailure([diagnostic]));
    }

    [Theory]
    [InlineData("Project could not be loaded.")]
    [InlineData("Msbuild failed when processing the file 'App.csproj' with message: Package 'Example.Package' could not be resolved.")]
    [InlineData("Msbuild failed when processing the file 'App.csproj' with message: Package 'Example.Package' 1.2.3 has a known high severity vulnerability, https://example.com/GHSA-1234-abcd-5678")]
    public void OtherWorkspaceFailuresRemainFatal(string message)
    {
        var diagnostic = new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Failure, message);

        Assert.Equal(
            WorkspaceDiagnosticKind.Failure,
            WorkspaceDiagnosticClassifier.GetEffectiveKind(diagnostic));
        Assert.True(WorkspaceDiagnosticClassifier.HasFailure([diagnostic]));
    }

    [Fact]
    public void ExistingWorkspaceWarningRemainsWarning()
    {
        var diagnostic = new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Warning, "Workspace warning.");

        Assert.Equal(
            WorkspaceDiagnosticKind.Warning,
            WorkspaceDiagnosticClassifier.GetEffectiveKind(diagnostic));
    }

    [Fact]
    public void PackageTargetFrameworkSupportWarningMisreportedByRoslynIsNonFatal()
    {
        var diagnostic = new WorkspaceDiagnostic(
            WorkspaceDiagnosticKind.Failure,
            "Msbuild failed when processing the file 'C:\\source\\App.csproj' with message: Example.Package 10.0.2 doesn't support net6.0 and has not been tested with it. Consider upgrading your TargetFramework to net8.0 or later. You may also set <SuppressTfmSupportBuildWarnings>true</SuppressTfmSupportBuildWarnings> in the project file to ignore this warning and attempt to run in this unsupported configuration at your own risk.");

        Assert.Equal(
            WorkspaceDiagnosticKind.Warning,
            WorkspaceDiagnosticClassifier.GetEffectiveKind(diagnostic));
        Assert.False(WorkspaceDiagnosticClassifier.HasFailure([diagnostic]));
    }

    [Fact]
    public void RepositoryPromotedAuditWarningRemainsFatal()
    {
        var diagnostic = new WorkspaceDiagnostic(
            WorkspaceDiagnosticKind.Failure,
            "Msbuild failed when processing the file 'C:\\source\\App.csproj' with message: Package 'Example.Package' 1.2.3 has a known high severity vulnerability, https://github.com/advisories/GHSA-1234-abcd-5678");

        var effectiveKind = WorkspaceDiagnosticClassifier.GetEffectiveKind(
            diagnostic,
            (project, code) => project.EndsWith("App.csproj", StringComparison.Ordinal) && code == "NU1903");

        Assert.Equal(WorkspaceDiagnosticKind.Failure, effectiveKind);
    }
}
