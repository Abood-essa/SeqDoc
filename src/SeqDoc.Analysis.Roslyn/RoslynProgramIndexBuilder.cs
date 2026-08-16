using SeqDoc.Application.Analysis;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Analysis.Roslyn;

public sealed class RoslynProgramIndexBuilder : IProgramIndexBuilder
{
    public async Task<ApplicationResult<ProgramIndexSnapshot>> BuildAsync(
        CompilationAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!extraction.IsSuccess)
        {
            return ApplicationResult.Failure<ProgramIndexSnapshot>(extraction.Outcome, extraction.Diagnostics);
        }

        return ApplicationResult.Success(extraction.Value!.ProgramIndex, extraction.Diagnostics);
    }
}
