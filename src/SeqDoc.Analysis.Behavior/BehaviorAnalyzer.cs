using System.Collections.Immutable;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Behavior;

/// <summary>
/// Validates the extracted behavior input, normalizes method flows, resolves calls, and produces the
/// behavior snapshot for one profile.
/// </summary>
public sealed class BehaviorAnalyzer : IBehaviorAnalyzer
{
    private const int SchemaVersion = 1;
    private const string ProducerVersion = "0.1.0-pass-b";

    public Task<ApplicationResult<BehaviorSnapshot>> AnalyzeAsync(
        BehaviorAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        diagnostics.AddRange(request.BehaviorInput.Diagnostics);
        diagnostics.AddRange(ExtractionValidator.Validate(request.BehaviorInput));

        if (HasBlockingDiagnostics(diagnostics))
        {
            return Task.FromResult(ApplicationResult.Failure<BehaviorSnapshot>(
                ApplicationOutcome.AnalysisFailure,
                diagnostics.ToImmutable()));
        }

        var flows = ImmutableArray.CreateBuilder<MethodFlowSnapshot>();
        foreach (var body in request.BehaviorInput.Methods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var built = MethodFlowBuilder.Build(body);
            diagnostics.AddRange(built.Diagnostics);
            flows.Add(built.Snapshot);
        }

        if (HasBlockingDiagnostics(diagnostics))
        {
            return Task.FromResult(ApplicationResult.Failure<BehaviorSnapshot>(
                ApplicationOutcome.AnalysisFailure,
                diagnostics.ToImmutable()));
        }

        var flowsFinal = flows.ToImmutable();
        var callGraph = CallResolver.Build(request, flowsFinal);
        var rtaFoundation = new RtaFoundation(
            request.BehaviorInput.Instantiations,
            HasExplicitRoots: false);
        var snapshot = new BehaviorSnapshot(
            SchemaVersion,
            ProducerVersion,
            request.BehaviorInput.Profile,
            request.BehaviorInput.ProgramIndexFingerprint,
            flowsFinal,
            callGraph,
            rtaFoundation,
            request.BehaviorInput.Instantiations,
            diagnostics.ToImmutable(),
            string.Empty);
        var completed = snapshot with { BehaviorFingerprint = BehaviorFingerprint.Compute(snapshot) };
        return Task.FromResult(ApplicationResult.Success(completed, completed.Diagnostics));
    }

    private static bool HasBlockingDiagnostics(IEnumerable<AnalysisDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic => diagnostic.Code.StartsWith("BD", StringComparison.Ordinal));
}
