using SeqDoc.Application.Analysis;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Analysis.Tests;

public sealed class ApplicationResultTests
{
    [Fact]
    public void SuccessRequiresValueAndNormalizesDiagnostics()
    {
        var result = ApplicationResult.Success("index");

        Assert.True(result.IsSuccess);
        Assert.Equal("index", result.Value);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FailureHasNoValueAndRequiresDiagnostic()
    {
        var result = ApplicationResult.Failure<string>(
            ApplicationOutcome.BuildFailure,
            [CreateDiagnostic()]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void FailureRejectsSucceededOutcome()
    {
        Assert.Throws<ArgumentException>(() => ApplicationResult.Failure<string>(
            ApplicationOutcome.Succeeded,
            [CreateDiagnostic()]));
    }

    private static AnalysisDiagnostic CreateDiagnostic()
    {
        return new AnalysisDiagnostic(
            new DiagnosticId("diagnostic:v1:test"),
            "SEQTEST001",
            DiagnosticSeverity.Error,
            AnalysisStage.CompilationValidation,
            "Compilation failed.",
            new DiagnosticLocation("test"),
            "Compiler error.",
            "No index was activated.",
            "Fix the error and retry.",
            CertaintyLevel.Exact);
    }
}
