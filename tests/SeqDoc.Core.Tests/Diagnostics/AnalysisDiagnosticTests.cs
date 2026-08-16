using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Core.Tests.Diagnostics;

public sealed class AnalysisDiagnosticTests
{
    [Fact]
    public void DiagnosticRetainsRequiredUserFacingFields()
    {
        var diagnostic = new AnalysisDiagnostic(
            new DiagnosticId("diagnostic:v1:test"),
            "SEQBUILD001",
            DiagnosticSeverity.Error,
            AnalysisStage.CompilationValidation,
            "SeqDoc stopped because the selected project does not compile.",
            new DiagnosticLocation("src/Orders.cs:12", new CompilationProfileId("profile:v1:test")),
            "CS1002: ; expected",
            "The active Program Index was not replaced.",
            "Fix the compiler error and run SeqDoc again.",
            CertaintyLevel.Exact);

        Assert.Equal("src/Orders.cs:12", diagnostic.Location.Description);
        Assert.NotEmpty(diagnostic.Summary);
        Assert.NotEmpty(diagnostic.TechnicalCause);
        Assert.NotEmpty(diagnostic.UserImpact);
        Assert.NotEmpty(diagnostic.NextAction);
        Assert.Empty(diagnostic.Evidence);
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("cause")]
    [InlineData("impact")]
    [InlineData("action")]
    public void DiagnosticRejectsMissingRequiredUserFacingField(string missingField)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["summary"] = "Summary",
            ["cause"] = "Cause",
            ["impact"] = "Impact",
            ["action"] = "Action",
        };
        values[missingField] = " ";

        Assert.Throws<ArgumentException>(() => new AnalysisDiagnostic(
            new DiagnosticId("diagnostic:v1:test"),
            "SEQTEST001",
            DiagnosticSeverity.Error,
            AnalysisStage.CompilationValidation,
            values["summary"],
            new DiagnosticLocation("test"),
            values["cause"],
            values["impact"],
            values["action"],
            CertaintyLevel.Exact));
    }
}
