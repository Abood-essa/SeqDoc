using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn.Diagnostics;
using SeqDoc.Analysis.Roslyn.Toolchains;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Roslyn;

public sealed class RoslynCompilationProfileAnalyzer : ICompilationProfileAnalyzer
{
    public async Task<ApplicationResult<CompilationAnalysisSummary>> AnalyzeAsync(
        CompilationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationDiagnostic = Validate(request);
        if (validationDiagnostic is not null)
        {
            return ApplicationResult.Failure<CompilationAnalysisSummary>(
                ApplicationOutcome.InvalidInput,
                [validationDiagnostic]);
        }

        try
        {
            await MsBuildRegistration.EnsureRegisteredAsync(request.RepositoryRoot, cancellationToken)
                .ConfigureAwait(false);
            var (loaded, diagnostics) = await CompilationWorkspaceLoader.LoadAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (loaded is null)
            {
                return ApplicationResult.Failure<CompilationAnalysisSummary>(
                    ApplicationOutcome.BuildFailure,
                    diagnostics);
            }

            using (loaded)
            {
                var projects = loaded.Projects
                    .Select(project => new CompiledProjectSummary(
                        project.StableId,
                        project.Project.Name,
                        project.RepositoryRelativePath,
                        project.Compilation.Assembly.Identity.ToString()))
                    .OrderBy(project => project.Id.Value, StringComparer.Ordinal)
                    .ToImmutableArray();
                return ApplicationResult.Success(
                    new CompilationAnalysisSummary(request.Profile, projects),
                    diagnostics);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApplicationResult.Failure<CompilationAnalysisSummary>(
                ApplicationOutcome.Cancelled,
                []);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException
                                          or ArgumentException)
        {
            var diagnostic = CompilerDiagnosticFactory.CreateInfrastructure(
                "The selected compilation profile could not be loaded.",
                exception,
                request.Profile.Id);
            return ApplicationResult.Failure<CompilationAnalysisSummary>(
                ApplicationOutcome.BuildFailure,
                [diagnostic]);
        }
    }

    internal static SeqDoc.Core.Diagnostics.AnalysisDiagnostic? Validate(CompilationAnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RepositoryRoot)
            || !Path.IsPathFullyQualified(request.RepositoryRoot)
            || !Directory.Exists(request.RepositoryRoot))
        {
            return CompilerDiagnosticFactory.CreateInput(
                "SD1001",
                "The repository root is missing or invalid.",
                "Compilation analysis requires an existing absolute repository directory.",
                "Provide the absolute path to the repository root.",
                request.Profile?.Id);
        }

        if (string.IsNullOrWhiteSpace(request.TargetPath)
            || !Path.IsPathFullyQualified(request.TargetPath)
            || !File.Exists(request.TargetPath))
        {
            return CompilerDiagnosticFactory.CreateInput(
                "SD1002",
                "The analysis target is missing or invalid.",
                "Compilation analysis requires an existing absolute project or solution path.",
                "Provide an existing .csproj, .sln, or .slnx path.",
                request.Profile?.Id);
        }

        if (request.Profile is null)
        {
            return CompilerDiagnosticFactory.CreateInput(
                "SD1003",
                "The compilation profile is required.",
                "No explicit single-target compilation profile was supplied.",
                "Supply a profile containing the configuration and target framework.");
        }

        var extension = Path.GetExtension(request.TargetPath);
        if (!extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return CompilerDiagnosticFactory.CreateInput(
                "SD1004",
                "The analysis target type is unsupported.",
                $"Target '{request.TargetPath}' is not a .csproj, .sln, or .slnx file.",
                "Select one C# project or solution.",
                request.Profile.Id);
        }

        var expectedTargetPath = Path.GetFullPath(
            Path.Combine(request.RepositoryRoot, request.Profile.RepositoryRelativeTargetPath));
        if (!string.Equals(expectedTargetPath, Path.GetFullPath(request.TargetPath), StringComparison.OrdinalIgnoreCase))
        {
            return CompilerDiagnosticFactory.CreateInput(
                "SD1005",
                "The compilation profile does not identify the selected target.",
                "The profile's repository-relative target path resolves to a different file.",
                "Create the profile from the selected target's repository-relative path.",
                request.Profile.Id);
        }

        return null;
    }
}
