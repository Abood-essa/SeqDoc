using System.Collections.Immutable;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Evidence;

/// <summary>Identifies the artifact category that supports a SeqDoc fact.</summary>
public enum EvidenceKind
{
    Source,
    GeneratedSource,
    FormalContract,
    XmlDocumentation,
    AssemblyMetadata,
    PortablePdb,
    SourceLinkSource,
    DecompiledIl,
    Configuration,
    FrameworkModel,
    RuntimeTrace,
}

/// <summary>States how directly the available evidence supports a fact.</summary>
public enum CertaintyLevel
{
    Exact,
    Conservative,
    Heuristic,
    Unknown,
}

/// <summary>Represents a zero-based line and column within a document.</summary>
public readonly record struct SourcePosition
{
    public SourcePosition(int line, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}

/// <summary>Represents a half-open source range associated with a stable document identity.</summary>
public sealed record SourceRange
{
    public SourceRange(DocumentId document, SourcePosition start, SourcePosition end)
    {
        if (string.IsNullOrWhiteSpace(document.Value))
        {
            throw new ArgumentException("A source range requires a document ID.", nameof(document));
        }

        if (end.Line < start.Line || (end.Line == start.Line && end.Column < start.Column))
        {
            throw new ArgumentException("The source range end must not precede its start.", nameof(end));
        }

        Document = document;
        Start = start;
        End = end;
    }

    public DocumentId Document { get; }

    public SourcePosition Start { get; }

    public SourcePosition End { get; }
}

/// <summary>Points to the source, contract, metadata, or model evidence supporting a fact.</summary>
public sealed record EvidenceRef
{
    public EvidenceRef(
        EvidenceId id,
        EvidenceKind kind,
        string artifact,
        SourceRange? range,
        string? symbol,
        string? detail,
        CertaintyLevel certainty,
        ImmutableArray<EvidenceRef> underlyingEvidence = default,
        string? producerId = null,
        string? producerVersion = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Evidence requires a stable ID.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(artifact);

        Id = id;
        Kind = kind;
        Artifact = artifact;
        Range = range;
        Symbol = symbol;
        Detail = detail;
        Certainty = certainty;
        UnderlyingEvidence = underlyingEvidence.IsDefault ? [] : underlyingEvidence;
        ProducerId = producerId;
        ProducerVersion = producerVersion;

        if (kind == EvidenceKind.FrameworkModel)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(producerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(producerVersion);

            if (UnderlyingEvidence.IsEmpty
                || UnderlyingEvidence.Any(item => item is null
                    || item.Kind is not (EvidenceKind.Source or EvidenceKind.GeneratedSource)
                    || item.Range is null
                    || string.IsNullOrWhiteSpace(item.Symbol)))
            {
                throw new ArgumentException(
                    "Framework-model evidence must retain direct source-operation evidence with a range and symbol.",
                    nameof(underlyingEvidence));
            }
        }
    }

    public EvidenceId Id { get; }

    public EvidenceKind Kind { get; }

    public string Artifact { get; }

    public SourceRange? Range { get; }

    public string? Symbol { get; }

    public string? Detail { get; }

    public CertaintyLevel Certainty { get; }

    /// <summary>
    /// Gets source evidence beneath a derived fact. Framework-model evidence must populate this collection.
    /// </summary>
    public ImmutableArray<EvidenceRef> UnderlyingEvidence { get; }

    /// <summary>Gets the producing framework-model ID when the evidence is model-derived.</summary>
    public string? ProducerId { get; }

    /// <summary>Gets the producing framework-model version when the evidence is model-derived.</summary>
    public string? ProducerVersion { get; }
}
