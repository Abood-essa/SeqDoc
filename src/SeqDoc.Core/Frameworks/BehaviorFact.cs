using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// Base shape for every evidence-backed behavior fact a framework model emits. The host rejects
/// facts without non-empty evidence instead of presenting unsupported behavior as proven.
/// </summary>
public abstract record BehaviorFact
{
    public required BehaviorFactId Id { get; init; }

    public required ImmutableArray<EvidenceRef> Evidence { get; init; }

    public required CertaintyLevel Certainty { get; init; }
}

/// <summary>
/// Minimal general fact shape for model outputs without a specialized payload. Concrete models
/// should prefer typed fact records derived from <see cref="BehaviorFact"/>.
/// </summary>
public sealed record GeneralBehaviorFact : BehaviorFact
{
    public required string Kind { get; init; }

    public string? Detail { get; init; }
}
