using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// One model's outcome for one analyzed input. Unrecognized results carry no artifacts; models never
/// manufacture exact behavior when a pattern is unsupported.
/// </summary>
public sealed record ModelResult
{
    public ModelResult(
        bool recognized,
        ImmutableArray<BehaviorFact> facts = default,
        ImmutableArray<CallResolutionHint> resolutionHints = default,
        ImmutableArray<SuppressionHint> suppressionHints = default,
        ImmutableArray<MethodSummaryRule> summaryRules = default,
        ImmutableArray<AnalysisDiagnostic> diagnostics = default)
    {
        // An unrecognized model may still report typed diagnostics for an unsupported pattern, but
        // it must never claim behavior through facts, hints, or rules.
        if (!recognized
            && (!facts.IsDefaultOrEmpty
                || !resolutionHints.IsDefaultOrEmpty
                || !suppressionHints.IsDefaultOrEmpty
                || !summaryRules.IsDefaultOrEmpty))
        {
            throw new ArgumentException(
                "An unrecognized model result cannot carry facts, resolution hints, suppression hints, or summary rules.",
                nameof(recognized));
        }

        Recognized = recognized;
        Facts = facts.IsDefault ? [] : facts;
        ResolutionHints = resolutionHints.IsDefault ? [] : resolutionHints;
        SuppressionHints = suppressionHints.IsDefault ? [] : suppressionHints;
        SummaryRules = summaryRules.IsDefault ? [] : summaryRules;
        Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
    }

    public bool Recognized { get; }

    public ImmutableArray<BehaviorFact> Facts { get; }

    public ImmutableArray<CallResolutionHint> ResolutionHints { get; }

    public ImmutableArray<SuppressionHint> SuppressionHints { get; }

    public ImmutableArray<MethodSummaryRule> SummaryRules { get; }

    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    /// <summary>Represents a model that did not recognize the input without emitting artifacts.</summary>
    public static ModelResult Unrecognized { get; } = new(false);
}

/// <summary>Describes one host analysis pass over operations and symbols.</summary>
public sealed record FrameworkAnalysisRequest(
    FrameworkDetectionContext DetectionContext,
    FrameworkAnalysisContext AnalysisContext,
    ImmutableArray<OperationDescriptor> Operations,
    ImmutableArray<SymbolDescriptor> Symbols);

/// <summary>
/// Contains the canonical, evidence-validated aggregation of every applicable model for one request.
/// Facts and diagnostics are ordered by stable identity, never by registration order.
/// </summary>
public sealed record FrameworkAnalysisResult(
    bool Recognized,
    ImmutableArray<BehaviorFact> Facts,
    ImmutableArray<CallResolutionHint> ResolutionHints,
    ImmutableArray<SuppressionHint> SuppressionHints,
    ImmutableArray<MethodSummaryRule> SummaryRules,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<FrameworkModelDescriptor> AppliedModels,
    CompilationProfileId? ProfileId = null,
    string? ProgramIndexFingerprint = null);
