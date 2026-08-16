using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// Directs one call site toward a framework-proven target. <see cref="SourceOperation"/> names the
/// operation the hint governs; the host consumes operation descriptors before a persisted
/// CallSiteId necessarily exists, so the operation anchor is the smallest stable typed source.
/// The target is exact symbol identity, never a raw method-name string.
/// </summary>
public sealed record CallResolutionHint(
    OperationId SourceOperation,
    MethodId? TargetMethod,
    SymbolId? TargetType,
    string Reason,
    int Ordinal,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>
/// Suppresses framework plumbing that does not materially change an outcome. Suppression never hides
/// a material effect.
/// </summary>
public sealed record SuppressionHint(
    string Scope,
    string Reason,
    int Ordinal,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>
/// Shapes how one boundary method is summarized without hiding material effects.
/// </summary>
public sealed record MethodSummaryRule(
    string Scope,
    string Reason,
    int Ordinal,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);
