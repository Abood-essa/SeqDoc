using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Wording;

/// <summary>Closed vocabulary of user-facing wording phrase kinds.</summary>
public enum WordingPhraseKind
{
    /// <summary>A direct evidence-backed statement derived from scenario-graph facts.</summary>
    Statement,

    /// <summary>
    /// An explicit technical-fallback statement shown when an unsupported or degraded fact prevents
    /// a confident claim. The fallback never invents domain meaning and always carries conservative
    /// certainty.
    /// </summary>
    TechnicalFallback,
}

/// <summary>
/// One user-facing wording phrase. Every phrase carries non-empty evidence and explicit certainty
/// that never exceeds its strongest evidence; a phrase can never be promoted beyond what its
/// evidence supports. Technical-fallback phrases make incomplete or unsupported analysis visible
/// instead of hiding it behind confident wording.
/// </summary>
public sealed record WordingPhrase
{
    public WordingPhrase(
        WordingPhraseId id,
        string key,
        WordingPhraseKind kind,
        string text,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined wording phrase kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A wording phrase requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A wording phrase requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException(
                "Wording phrase certainty must never exceed its strongest evidence.",
                nameof(certainty));
        }

        Id = id;
        Key = key;
        Kind = kind;
        Text = text;
        Evidence = evidence;
        Certainty = certainty;
    }

    public WordingPhraseId Id { get; }

    public string Key { get; }

    public WordingPhraseKind Kind { get; }

    public string Text { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One deterministic wording document for one HTTP entry point. Phrases preserve the planner's
/// semantic order (entry, action, service call, entity query, then failure before success, with
/// technical fallbacks last) and every phrase retains evidence and certainty. The document is
/// memory-only and is never persisted; the debug projection is canonical, newline-only, and free of
/// absolute paths.
/// </summary>
public sealed record WordingDocument(
    EntryPointId EntryPoint,
    CompilationProfileId Profile,
    string OperationKey,
    string Title,
    ImmutableArray<WordingPhrase> Phrases,
    string DebugProjection);
