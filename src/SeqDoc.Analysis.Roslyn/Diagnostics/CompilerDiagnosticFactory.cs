using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using CoreDiagnosticSeverity = SeqDoc.Core.Diagnostics.DiagnosticSeverity;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Diagnostics;

internal static class CompilerDiagnosticFactory
{
    public static AnalysisDiagnostic CreateInput(
        string code,
        string summary,
        string cause,
        string nextAction,
        CompilationProfileId? profile = null)
    {
        return Create(
            code,
            CoreDiagnosticSeverity.Error,
            AnalysisStage.ProfileResolution,
            summary,
            new DiagnosticLocation("analysis target", profile),
            cause,
            "No compilation or Program Index was produced.",
            nextAction,
            profile,
            null,
            0);
    }

    public static AnalysisDiagnostic CreateProfileResolution(
        string code,
        CoreDiagnosticSeverity severity,
        string summary,
        string location,
        string technicalCause,
        string userImpact,
        string nextAction,
        string subjectId,
        string? internalDetail = null)
    {
        return Create(
            code,
            severity,
            AnalysisStage.ProfileResolution,
            summary,
            new DiagnosticLocation(location),
            technicalCause,
            userImpact,
            nextAction,
            null,
            subjectId,
            0,
            internalDetail);
    }

    public static ImmutableArray<AnalysisDiagnostic> CreateWorkspace(
        IEnumerable<WorkspaceDiagnostic> workspaceDiagnostics,
        CompilationProfileId profile,
        Func<string, string?, bool>? isWarningPromoted = null)
    {
        var ordered = workspaceDiagnostics
            .OrderBy(diagnostic => diagnostic.Kind)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();

        return ordered.Select((diagnostic, ordinal) =>
        {
            var effectiveKind = WorkspaceDiagnosticClassifier.GetEffectiveKind(diagnostic, isWarningPromoted);
            return Create(
                "SD1101",
                effectiveKind == WorkspaceDiagnosticKind.Failure
                    ? CoreDiagnosticSeverity.Error
                    : CoreDiagnosticSeverity.Warning,
                AnalysisStage.WorkspaceLoad,
                effectiveKind == WorkspaceDiagnosticKind.Failure
                    ? "MSBuild could not load part of the selected project graph."
                    : "MSBuild reported a workspace warning.",
                new DiagnosticLocation("MSBuild workspace", profile),
                diagnostic.Message,
                effectiveKind == WorkspaceDiagnosticKind.Failure
                    ? "The compiler gate failed and no Program Index was produced."
                    : "Analysis can continue, but the project may not match the intended build.",
                WorkspaceDiagnosticClassifier.IsNuGetAuditWarning(diagnostic)
                    ? "Review the advisory and update or explicitly suppress the affected package according to repository policy."
                    : WorkspaceDiagnosticClassifier.IsPackageTfmSupportWarning(diagnostic)
                        ? "Review the package's target-framework support and upgrade the project or package where practical."
                        : "Restore and build the selected target with the repository's pinned SDK, then retry.",
                profile,
                null,
                ordinal);
        }).ToImmutableArray();
    }

    public static ImmutableArray<AnalysisDiagnostic> CreateCompiler(
        IEnumerable<(Diagnostic Diagnostic, StableProjectId Project)> compilerDiagnostics,
        CompilationProfileId profile)
    {
        var ordered = compilerDiagnostics
            .OrderBy(item => item.Diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Diagnostic.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(item => item.Diagnostic.Location.SourceSpan.Start)
            .ThenBy(item => item.Diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ThenBy(item => item.Project.Value, StringComparer.Ordinal)
            .ToArray();

        return ordered.Select((item, ordinal) =>
        {
            var lineSpan = item.Diagnostic.Location.GetLineSpan();
            var description = lineSpan.IsValid
                ? $"{lineSpan.Path}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1})"
                : "compiler";

            return Create(
                item.Diagnostic.Id,
                CoreDiagnosticSeverity.Error,
                AnalysisStage.CompilationValidation,
                item.Diagnostic.GetMessage(CultureInfo.InvariantCulture),
                new DiagnosticLocation(description, profile, item.Project),
                item.Diagnostic.ToString(),
                "The compiler gate failed and no Program Index was produced.",
                "Fix the compiler error using the selected configuration and target framework, then retry.",
                profile,
                item.Project.Value,
                ordinal);
        }).ToImmutableArray();
    }

    public static AnalysisDiagnostic CreateInfrastructure(
        string summary,
        Exception exception,
        CompilationProfileId? profile)
    {
        return Create(
            "SD1102",
            CoreDiagnosticSeverity.Error,
            AnalysisStage.WorkspaceLoad,
            summary,
            new DiagnosticLocation("MSBuild workspace", profile),
            exception.Message,
            "The compiler gate failed and no Program Index was produced.",
            "Confirm the pinned SDK is installed, restore the target, and retry in a fresh process.",
            profile,
            null,
            0,
            exception.ToString());
    }

    public static AnalysisDiagnostic CreateIndexFailure(
        Exception exception,
        CompilationProfileId profile)
    {
        return Create(
            "SD1301",
            CoreDiagnosticSeverity.Error,
            AnalysisStage.BaselineIndex,
            "The validated compilation could not be converted into a Program Index.",
            new DiagnosticLocation("baseline Program Index", profile),
            exception.Message,
            "No Program Index was produced.",
            "Report the failure with the internal diagnostic detail and analyzed project shape.",
            profile,
            null,
            0,
            exception.ToString());
    }

    private static AnalysisDiagnostic Create(
        string code,
        CoreDiagnosticSeverity severity,
        AnalysisStage stage,
        string summary,
        DiagnosticLocation location,
        string technicalCause,
        string userImpact,
        string nextAction,
        CompilationProfileId? profile,
        string? subjectId,
        int ordinal,
        string? internalDetail = null)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            stage,
            profile,
            subjectId,
            ordinal));

        return new AnalysisDiagnostic(
            id,
            code,
            severity,
            stage,
            summary,
            location,
            technicalCause,
            userImpact,
            nextAction,
            CertaintyLevel.Exact,
            internalDetail: internalDetail);
    }
}
