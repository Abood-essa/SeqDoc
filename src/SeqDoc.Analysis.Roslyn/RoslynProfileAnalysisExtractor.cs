using System.Text.Json;
using SeqDoc.Analysis.Roslyn.Behavior;
using SeqDoc.Analysis.Roslyn.Diagnostics;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Analysis.Roslyn.Toolchains;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Analysis.Roslyn;

/// <summary>Produces the Program Index and extracted behavior input from one compilation per profile.</summary>
public sealed class RoslynProfileAnalysisExtractor : IProfileAnalysisExtractor
{
    public async Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractAsync(
        CompilationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationDiagnostic = RoslynCompilationProfileAnalyzer.Validate(request);
        if (validationDiagnostic is not null)
        {
            return ApplicationResult.Failure<ProfileAnalysisExtraction>(
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
                return ApplicationResult.Failure<ProfileAnalysisExtraction>(
                    ApplicationOutcome.BuildFailure,
                    diagnostics);
            }

            using (loaded)
            {
                try
                {
                    var index = await RoslynProgramIndexExtractor.ExtractAsync(
                        loaded,
                        request.Profile,
                        request.RepositoryRoot,
                        cancellationToken).ConfigureAwait(false);
                    var behavior = await RoslynBehaviorExtractor.ExtractAsync(
                        loaded,
                        request.Profile,
                        index.IndexFingerprint,
                        request.RepositoryRoot,
                        request.RepositoryOwnedConfigurationFiles,
                        cancellationToken).ConfigureAwait(false);
                    return ApplicationResult.Success(
                        new ProfileAnalysisExtraction(index, behavior),
                        diagnostics);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return ApplicationResult.Failure<ProfileAnalysisExtraction>(ApplicationOutcome.Cancelled, []);
                }
                catch (Exception exception) when (exception is IOException
                                                  or UnauthorizedAccessException
                                                  or InvalidOperationException
                                                  or ArgumentException
                                                  or JsonException)
                {
                    return ApplicationResult.Failure<ProfileAnalysisExtraction>(
                        ApplicationOutcome.AnalysisFailure,
                        [CompilerDiagnosticFactory.CreateIndexFailure(exception, request.Profile.Id)]);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApplicationResult.Failure<ProfileAnalysisExtraction>(ApplicationOutcome.Cancelled, []);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException
                                          or ArgumentException)
        {
            return ApplicationResult.Failure<ProfileAnalysisExtraction>(
                ApplicationOutcome.BuildFailure,
                [CompilerDiagnosticFactory.CreateInfrastructure(
                    "The selected compilation profile could not be loaded.",
                    exception,
                    request.Profile.Id)]);
        }
    }
}
