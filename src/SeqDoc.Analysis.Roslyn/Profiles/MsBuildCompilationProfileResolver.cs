using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn.Diagnostics;
using SeqDoc.Analysis.Roslyn.Toolchains;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Roslyn.Profiles;

public sealed class MsBuildCompilationProfileResolver : ICompilationProfileResolver
{
    public async Task<ApplicationResult<ResolvedCompilationProfiles>> ResolveAsync(
        CompilationProfileResolutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputFailure = Validate(request);
        if (inputFailure is not null)
        {
            return ApplicationResult.Failure<ResolvedCompilationProfiles>(
                ApplicationOutcome.InvalidInput,
                [inputFailure]);
        }

        var relativeTarget = RepositoryRelativePath.Normalize(Path.GetRelativePath(
            request.RepositoryRoot,
            request.TargetPath));
        try
        {
            var toolchainVersion = await MsBuildRegistration.EnsureRegisteredAsync(request.RepositoryRoot, cancellationToken)
                .ConfigureAwait(false);
            var available = EvaluatedTargetFrameworkDiscovery.Discover(request, cancellationToken);
            if (available.Length == 0)
            {
                return ApplicationResult.Failure<ResolvedCompilationProfiles>(
                    ApplicationOutcome.UnsupportedRequiredFeature,
                    [CreateDiagnostic(
                        "SD1010",
                        "The selected project has no evaluated target framework.",
                        relativeTarget,
                        "MSBuild produced neither TargetFramework nor TargetFrameworks.",
                        "No compilation profile or Program Index was produced.",
                        "Select an SDK-style C# project with an evaluated target framework.")]);
            }

            string[] selected;
            if (request.AllTargetFrameworks)
            {
                selected = available;
            }
            else if (!string.IsNullOrWhiteSpace(request.TargetFramework))
            {
                var match = available.FirstOrDefault(framework => string.Equals(
                    framework,
                    request.TargetFramework,
                    StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    return ApplicationResult.Failure<ResolvedCompilationProfiles>(
                        ApplicationOutcome.InvalidInput,
                        [CreateDiagnostic(
                            "SD1009",
                            "The requested target framework is unavailable.",
                            relativeTarget,
                            $"Requested '{request.TargetFramework}'; evaluated frameworks: {string.Join(", ", available)}.",
                            "No compilation profile or Program Index was produced.",
                            "Select one of the evaluated target frameworks.")]);
                }

                selected = [match];
            }
            else if (available.Length == 1)
            {
                selected = available;
            }
            else
            {
                return ApplicationResult.Failure<ResolvedCompilationProfiles>(
                    ApplicationOutcome.InvalidInput,
                    [CreateDiagnostic(
                        "SD1008",
                        "The selected project targets multiple frameworks.",
                        relativeTarget,
                        $"Evaluated frameworks: {string.Join(", ", available)}.",
                        "No compilation profile or Program Index was produced.",
                        "Select one framework explicitly or request all frameworks as separate profiles.")]);
            }

            var analysisProperties = (request.AnalysisProperties
                    ?? ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal))
                .SetItem("seqdoc.toolchainVersion", toolchainVersion);
            var profiles = selected.Select(framework => CompilationProfile.Create(
                    relativeTarget,
                    request.Configuration,
                    framework,
                    request.RuntimeIdentifier,
                    request.MsBuildProperties,
                    analysisProperties))
                .OrderBy(profile => profile.TargetFramework, StringComparer.Ordinal)
                .ToImmutableArray();
            return ApplicationResult.Success(new ResolvedCompilationProfiles(
                available.ToImmutableArray(),
                profiles,
                toolchainVersion));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApplicationResult.Failure<ResolvedCompilationProfiles>(ApplicationOutcome.Cancelled, []);
        }
        catch (NotSupportedException exception)
        {
            return ApplicationResult.Failure<ResolvedCompilationProfiles>(
                ApplicationOutcome.UnsupportedRequiredFeature,
                [CreateDiagnostic(
                    "SD1012",
                    "The solution framework graph cannot be represented by one profile set.",
                    relativeTarget,
                    exception.Message,
                    "No compilation profile or Program Index was produced.",
                    "Select one root C# project for framework discovery.")]);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException
                                          or ArgumentException
                                          || exception.GetType().FullName == "Microsoft.Build.Exceptions.InvalidProjectFileException")
        {
            return ApplicationResult.Failure<ResolvedCompilationProfiles>(
                ApplicationOutcome.BuildFailure,
                [CreateDiagnostic(
                    "SD1103",
                    "MSBuild could not evaluate target frameworks.",
                    relativeTarget,
                    "MSBuild project evaluation failed for the selected repository-relative target.",
                    "No compilation profile or Program Index was produced.",
                    "Verify the selected SDK, project imports, workloads, restore state, and MSBuild properties.",
                    exception.GetType().FullName)]);
        }
    }

    private static AnalysisDiagnostic? Validate(CompilationProfileResolutionRequest request)
    {
        var subject = string.IsNullOrWhiteSpace(request.TargetPath)
            ? "analysis-target"
            : Path.GetFileName(request.TargetPath);
        if (string.IsNullOrWhiteSpace(request.RepositoryRoot)
            || !Path.IsPathFullyQualified(request.RepositoryRoot)
            || !Directory.Exists(request.RepositoryRoot))
        {
            return CreateDiagnostic(
                "SD1021",
                "The repository root is missing or invalid.",
                subject,
                "Profile resolution requires an existing absolute repository directory.",
                "No compilation profile was produced.",
                "Provide the absolute repository root.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetPath)
            || !Path.IsPathFullyQualified(request.TargetPath)
            || !File.Exists(request.TargetPath))
        {
            return CreateDiagnostic(
                "SD1022",
                "The analysis target is missing or invalid.",
                subject,
                "Profile resolution requires an existing absolute project path.",
                "No compilation profile was produced.",
                "Provide an existing C# project path.");
        }

        var relative = Path.GetRelativePath(request.RepositoryRoot, request.TargetPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return CreateDiagnostic(
                "SD1024",
                "The target is outside the repository root.",
                subject,
                "Stable profile identity requires a repository-relative target path.",
                "No compilation profile was produced.",
                "Select a project contained by the repository root.");
        }

        var extension = Path.GetExtension(request.TargetPath);
        if (!extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return CreateDiagnostic(
                "SD1011",
                "The target type does not support framework discovery.",
                RepositoryRelativePath.Normalize(relative),
                "Framework discovery supports .csproj, .sln, and .slnx targets.",
                "No compilation profile was produced.",
                "Select a C# project or solution.");
        }

        if (string.IsNullOrWhiteSpace(request.Configuration))
        {
            return CreateDiagnostic(
                "SD1023",
                "The build configuration is required.",
                RepositoryRelativePath.Normalize(relative),
                "Profile resolution requires an explicit configuration.",
                "No compilation profile was produced.",
                "Provide a configuration such as Release or Debug.");
        }

        if (request.AllTargetFrameworks && !string.IsNullOrWhiteSpace(request.TargetFramework))
        {
            return CreateDiagnostic(
                "SD1006",
                "Framework selection options conflict.",
                RepositoryRelativePath.Normalize(relative),
                "An explicit framework and all-framework selection were both requested.",
                "No compilation profile was produced.",
                "Choose one explicit framework or request all frameworks.");
        }

        var reserved = MsBuildGlobalProperties.FindReservedProperty(
            request.MsBuildProperties ?? ImmutableSortedDictionary<string, string>.Empty);
        return reserved is null
            ? null
            : CreateDiagnostic(
                "SD1007",
                "A controlled profile property was supplied twice.",
                RepositoryRelativePath.Normalize(relative),
                $"MSBuild property '{reserved}' must use its dedicated request field.",
                "No compilation profile was produced.",
                "Remove the reserved property from custom MSBuild properties.");
    }

    private static AnalysisDiagnostic CreateDiagnostic(
        string code,
        string summary,
        string subject,
        string cause,
        string impact,
        string nextAction,
        string? internalDetail = null) =>
        CompilerDiagnosticFactory.CreateProfileResolution(
            code,
            DiagnosticSeverity.Error,
            summary,
            subject,
            cause,
            impact,
            nextAction,
            subject,
            internalDetail);
}
