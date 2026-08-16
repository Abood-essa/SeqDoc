using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace SeqDoc.Analysis.Roslyn.Workspace;

internal static partial class WorkspaceDiagnosticClassifier
{
    public static WorkspaceDiagnosticKind GetEffectiveKind(
        WorkspaceDiagnostic diagnostic,
        Func<string, string?, bool>? isWarningPromoted = null) =>
        diagnostic.Kind == WorkspaceDiagnosticKind.Failure
        && TryGetMisreportedWarning(diagnostic, out string? projectPath, out string? code)
        && !(isWarningPromoted?.Invoke(projectPath, code) ?? false)
            ? WorkspaceDiagnosticKind.Warning
            : diagnostic.Kind;

    public static bool HasFailure(
        IEnumerable<WorkspaceDiagnostic> diagnostics,
        Func<string, string?, bool>? isWarningPromoted = null) =>
        diagnostics.Any(diagnostic =>
            GetEffectiveKind(diagnostic, isWarningPromoted) == WorkspaceDiagnosticKind.Failure);

    public static bool IsNuGetAuditWarning(WorkspaceDiagnostic diagnostic) =>
        NuGetAuditWarningPattern().IsMatch(diagnostic.Message);

    public static bool IsPackageTfmSupportWarning(WorkspaceDiagnostic diagnostic) =>
        PackageTfmSupportWarningPattern().IsMatch(diagnostic.Message);

    private static bool TryGetMisreportedWarning(
        WorkspaceDiagnostic diagnostic,
        out string projectPath,
        out string? code)
    {
        var auditMatch = NuGetAuditWarningPattern().Match(diagnostic.Message);
        if (auditMatch.Success)
        {
            projectPath = auditMatch.Groups["project"].Value;
            code = auditMatch.Groups["severity"].Value switch
            {
                "low" => "NU1901",
                "moderate" => "NU1902",
                "high" => "NU1903",
                "critical" => "NU1904",
                _ => null,
            };
            return code is not null;
        }

        var supportMatch = PackageTfmSupportWarningPattern().Match(diagnostic.Message);
        projectPath = supportMatch.Groups["project"].Value;
        code = null;
        return supportMatch.Success;
    }

    // Roslyn issue 75182 loses the warning kind while forwarding NuGet audit logs.
    // Keep this workaround narrow so genuine project-load failures remain fatal.
    [GeneratedRegex(
        "^Msbuild failed when processing the file '(?<project>[^\\r\\n]+)' with message: Package '[^\\r\\n]+' [^\\r\\n]+ has a known (?<severity>low|moderate|high|critical) severity vulnerability, https://github\\.com/advisories/GHSA-[0-9A-Za-z-]+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex NuGetAuditWarningPattern();

    [GeneratedRegex(
        "^Msbuild failed when processing the file '(?<project>[^\\r\\n]+)' with message: [^\\r\\n]+ doesn't support [^\\r\\n]+ and has not been tested with it\\. Consider upgrading your TargetFramework to [^\\r\\n]+ or later\\. You may also set <SuppressTfmSupportBuildWarnings>true</SuppressTfmSupportBuildWarnings> in the project file to ignore this warning and attempt to run in this unsupported configuration at your own risk\\.$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex PackageTfmSupportWarningPattern();
}
