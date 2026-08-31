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

    /// <summary>
    /// Withhold-class behavior-diagnostic codes: each one is a local "skip one element and keep going"
    /// signal whose method flow is still fully built, fingerprinted, and safe to consume downstream.
    /// A code outside this set fails closed (blocking), so every unrecognized or future <c>BD</c> code
    /// and every structural invariant keeps stopping the analysis.
    /// </summary>
    /// <remarks>
    /// Severity contract for the <c>BD</c> ranges:
    /// <list type="bullet">
    /// <item><c>BD1001</c>-<c>BD1014</c> (<see cref="ExtractionValidator"/>): extraction-level structural
    /// invariants (duplicate body, non-canonical order, missing fingerprint, bad ordinals/refs/regions).
    /// Always blocking, at both call sites.</item>
    /// <item><c>BD2001</c>, <c>BD2002</c>, <c>BD2003</c>, <c>BD2010</c>, <c>BD2011</c>
    /// (<see cref="MethodFlowBuilder"/>): local withhold - one operation/edge/natural loop is skipped
    /// via <c>continue</c>, the method flow is still produced and fingerprintable. Non-blocking.</item>
    /// <item><c>BD3001</c> (<c>CallResolver</c>): per-invocation dynamic dispatch with no static target;
    /// the method flow persists. Non-blocking.</item>
    /// <item><c>BD2004</c> ("method flow has no exit block") and <c>BD2012</c> ("loop-anchor collection
    /// invalid"): kept blocking. BD2004 means terminal reconciliation has no exit to resolve against;
    /// BD2012 means the compiler loop-anchor collection is internally corrupt (duplicate operations or
    /// missing evidence), which points at suspect upstream extraction rather than one malformed loop.
    /// Conservative default per the checkpoint capsule.</item>
    /// </list>
    /// </remarks>
    private static readonly ImmutableHashSet<string> NonBlockingDiagnosticCodes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "BD2001",
        "BD2002",
        "BD2003",
        "BD2010",
        "BD2011",
        "BD3001");

    private static bool HasBlockingDiagnostics(IEnumerable<AnalysisDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic =>
            diagnostic.Code.StartsWith("BD", StringComparison.Ordinal)
            && !NonBlockingDiagnosticCodes.Contains(diagnostic.Code));
}
