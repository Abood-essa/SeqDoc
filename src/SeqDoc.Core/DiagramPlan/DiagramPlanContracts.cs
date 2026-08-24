using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.DiagramPlan;

/// <summary>Closed vocabulary of renderer-neutral diagram participant roles.</summary>
public enum DiagramParticipantKind
{
    Client,
    Controller,
    Service,
    Data,
    Unknown,
}

/// <summary>Closed vocabulary of renderer-neutral diagram message kinds.</summary>
public enum DiagramMessageKind
{
    Request,
    Response,
    Unknown,
}

/// <summary>Closed vocabulary of renderer-neutral diagram branch (polarity) kinds.</summary>
public enum DiagramBranchKind
{
    Success,
    Failure,
    Unknown,
}

/// <summary>
/// One renderer-neutral diagram participant. Every participant carries non-empty evidence and
/// explicit certainty that never exceeds its strongest evidence.
/// </summary>
public sealed record DiagramParticipant
{
    public DiagramParticipant(
        DiagramPlanElementId id,
        string key,
        string label,
        DiagramParticipantKind kind,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined diagram participant kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(label, nameof(label));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A diagram participant requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A diagram participant requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException(
                "Diagram participant certainty must never exceed its strongest evidence.",
                nameof(certainty));
        }

        Id = id;
        Key = key;
        Label = label;
        Kind = kind;
        Evidence = evidence;
        Certainty = certainty;
    }

    public DiagramPlanElementId Id { get; }

    public string Key { get; }

    public string Label { get; }

    public DiagramParticipantKind Kind { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One renderer-neutral diagram message connecting two participants. The source and target reference
/// participant keys, and every message carries non-empty evidence and explicit certainty that never
/// exceeds its strongest evidence.
/// </summary>
public sealed record DiagramMessage
{
    public DiagramMessage(
        DiagramPlanElementId id,
        string key,
        string source,
        string target,
        string label,
        DiagramMessageKind kind,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined diagram message kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(source, nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(target, nameof(target));
        ArgumentException.ThrowIfNullOrWhiteSpace(label, nameof(label));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A diagram message requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A diagram message requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException(
                "Diagram message certainty must never exceed its strongest evidence.",
                nameof(certainty));
        }

        Id = id;
        Key = key;
        Source = source;
        Target = target;
        Label = label;
        Kind = kind;
        Evidence = evidence;
        Certainty = certainty;
    }

    public DiagramPlanElementId Id { get; }

    public string Key { get; }

    public string Source { get; }

    public string Target { get; }

    public string Label { get; }

    public DiagramMessageKind Kind { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One renderer-neutral branch (polarity path) grouping the message keys proven to belong to one
/// outcome path. Every branch carries non-empty evidence and explicit certainty that never exceeds
/// its strongest evidence.
/// </summary>
public sealed record DiagramBranch
{
    public DiagramBranch(
        DiagramPlanElementId id,
        string key,
        string label,
        DiagramBranchKind kind,
        ImmutableArray<string> messageKeys,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined diagram branch kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(label, nameof(label));
        if (messageKeys.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A diagram branch requires at least one message key.", nameof(messageKeys));
        }

        if (messageKeys.Any(item => string.IsNullOrWhiteSpace(item)))
        {
            throw new ArgumentException("A diagram branch message key cannot be empty.", nameof(messageKeys));
        }

        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A diagram branch requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A diagram branch requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException(
                "Diagram branch certainty must never exceed its strongest evidence.",
                nameof(certainty));
        }

        Id = id;
        Key = key;
        Label = label;
        Kind = kind;
        MessageKeys = messageKeys;
        Evidence = evidence;
        Certainty = certainty;
    }

    public DiagramPlanElementId Id { get; }

    public string Key { get; }

    public string Label { get; }

    public DiagramBranchKind Kind { get; }

    public ImmutableArray<string> MessageKeys { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// Closed vocabulary of renderer-neutral diagram fragments. The planner owns fragment kind, order,
/// nesting, and arm polarity; renderers serialize the fragment tree mechanically and never infer a
/// fragment kind from labels, source text, or Mermaid keywords. <c>Par</c> is intentionally absent:
/// independent launch-before-join semantics are not proven, so no fragment can express concurrency.
/// </summary>
public enum DiagramFragmentKind
{
    Alt,
    Opt,
    Break,
    Loop,
}

/// <summary>
/// One explicit arm of an <see cref="DiagramFragmentKind.Alt"/> fragment. <c>else</c> is a planner
/// decision recorded on this record (<see cref="IsElse"/>), never a renderer ordering guess. The
/// key is the stable semantic polarity key (decision condition plus semantic true/false polarity),
/// never a label or visual position; the visual arm order is recorded explicitly and may place a
/// terminating arm first while the semantic identity stays unchanged. Every arm carries non-empty
/// evidence and explicit certainty that never exceeds its strongest evidence.
/// </summary>
public sealed record DiagramAltArm
{
    public DiagramAltArm(
        DiagramPlanElementId id,
        string key,
        string label,
        bool isElse,
        ImmutableArray<DiagramPlanElementId> messageRefs,
        ImmutableArray<DiagramFragment> fragments,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(label, nameof(label));
        if (messageRefs.IsDefault)
        {
            messageRefs = [];
        }

        if (fragments.IsDefault)
        {
            fragments = [];
        }

        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A diagram alt arm requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A diagram alt arm requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException(
                "Diagram alt arm certainty must never exceed its strongest evidence.",
                nameof(certainty));
        }

        Id = id;
        Key = key;
        Label = label;
        IsElse = isElse;
        MessageRefs = messageRefs;
        Fragments = fragments;
        Evidence = evidence;
        Certainty = certainty;
    }

    public DiagramPlanElementId Id { get; }

    public string Key { get; }

    public string Label { get; }

    public bool IsElse { get; }

    public ImmutableArray<DiagramPlanElementId> MessageRefs { get; }

    public ImmutableArray<DiagramFragment> Fragments { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One renderer-neutral diagram fragment in the explicit ordered sequence tree. The planner derives
/// the kind (<see cref="DiagramFragmentKind"/>) only from Scenario Graph topology: both-material
/// decisions become <see cref="DiagramFragmentKind.Alt"/>, one-sided material decisions become
/// <see cref="DiagramFragmentKind.Opt"/>, and a terminating arm becomes a
/// <see cref="DiagramFragmentKind.Break"/>. A <see cref="DiagramFragmentKind.Loop"/> is admitted
/// only from an already exact preplanned plan; the planner never infers loops from raw
/// <c>LoopNode</c> facts. The key is a stable semantic key (decision condition), never a label or
/// traversal order, and every fragment carries non-empty evidence and explicit certainty that never
/// exceeds its strongest evidence.
/// </summary>
public sealed record DiagramFragment
{
    public DiagramFragment(
        DiagramPlanElementId id,
        string key,
        string label,
        DiagramFragmentKind kind,
        ImmutableArray<DiagramAltArm> arms,
        ImmutableArray<DiagramPlanElementId> messageRefs,
        ImmutableArray<DiagramFragment> fragments,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Undefined diagram fragment kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(label, nameof(label));
        if (arms.IsDefault)
        {
            arms = [];
        }

        if (messageRefs.IsDefault)
        {
            messageRefs = [];
        }

        if (fragments.IsDefault)
        {
            fragments = [];
        }

        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A diagram fragment requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A diagram fragment requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Min(item => item.Certainty))
        {
            throw new ArgumentException(
                "Diagram fragment certainty must never exceed its strongest evidence.",
                nameof(certainty));
        }

        ValidateFragmentShape(kind, arms, messageRefs, fragments);

        Id = id;
        Key = key;
        Label = label;
        Kind = kind;
        Arms = arms;
        MessageRefs = messageRefs;
        Fragments = fragments;
        Evidence = evidence;
        Certainty = certainty;
    }

    public DiagramPlanElementId Id { get; }

    public string Key { get; }

    public string Label { get; }

    public DiagramFragmentKind Kind { get; }

    /// <summary>Semantic arms; populated only for <see cref="DiagramFragmentKind.Alt"/> fragments.</summary>
    public ImmutableArray<DiagramAltArm> Arms { get; }

    public ImmutableArray<DiagramPlanElementId> MessageRefs { get; }

    public ImmutableArray<DiagramFragment> Fragments { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }

    /// <summary>
    /// Closed per-kind fragment shapes (F2/F4). Alt owns at least two explicit arms with exactly one
    /// leading non-else arm followed by explicit else arms, and no direct message refs or nested
    /// fragments. Opt never materializes arms and requires at least one message ref or nested
    /// fragment. Break is an empty marker. Loop admits only message refs, never arms or nested
    /// fragments. Populated fields a kind does not admit are rejected instead of silently ignored.
    /// </summary>
    private static void ValidateFragmentShape(
        DiagramFragmentKind kind,
        ImmutableArray<DiagramAltArm> arms,
        ImmutableArray<DiagramPlanElementId> messageRefs,
        ImmutableArray<DiagramFragment> fragments)
    {
        switch (kind)
        {
            case DiagramFragmentKind.Alt:
                if (arms.IsDefaultOrEmpty || arms.Length < 2)
                {
                    throw new ArgumentException(
                        "An Alt fragment requires at least two explicit arms.",
                        nameof(arms));
                }

                if (arms[0].IsElse)
                {
                    throw new ArgumentException(
                        "The first Alt arm must be the leading non-else arm.",
                        nameof(arms));
                }

                if (arms.Skip(1).Any(arm => !arm.IsElse))
                {
                    throw new ArgumentException(
                        "Every Alt arm after the first must be an explicit else arm.",
                        nameof(arms));
                }

                if (!messageRefs.IsDefaultOrEmpty || !fragments.IsDefaultOrEmpty)
                {
                    throw new ArgumentException(
                        "An Alt fragment owns its arms; direct message refs or nested fragments are not admitted.",
                        nameof(kind));
                }

                break;
            case DiagramFragmentKind.Opt:
                if (!arms.IsDefaultOrEmpty)
                {
                    throw new ArgumentException(
                        "An Opt fragment never materializes arms.",
                        nameof(arms));
                }

                if (messageRefs.IsDefaultOrEmpty && fragments.IsDefaultOrEmpty)
                {
                    throw new ArgumentException(
                        "An Opt fragment requires at least one message ref or nested fragment.",
                        nameof(kind));
                }

                break;
            case DiagramFragmentKind.Break:
                if (!arms.IsDefaultOrEmpty || !messageRefs.IsDefaultOrEmpty || !fragments.IsDefaultOrEmpty)
                {
                    throw new ArgumentException(
                        "A Break fragment is an empty marker without arms, message refs, or nested fragments.",
                        nameof(kind));
                }

                break;
            case DiagramFragmentKind.Loop:
                if (!arms.IsDefaultOrEmpty)
                {
                    throw new ArgumentException(
                        "A Loop fragment admits message refs and nested fragments, never arms.",
                        nameof(kind));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "Undefined diagram fragment kind.");
        }
    }
}

/// <summary>
/// One ordered element of a <see cref="DiagramSequence"/>: either a message reference or a nested
/// fragment in exact planner order. Renderers serialize element order verbatim and never move a
/// continuation message ahead of a fragment. An element is exactly one message reference or one
/// fragment, never both and never neither.
/// </summary>
public sealed record DiagramSequenceElement
{
    private DiagramSequenceElement(DiagramPlanElementId? messageRefId, DiagramFragment? nestedFragment)
    {
        if ((messageRefId is null) == (nestedFragment is null))
        {
            throw new ArgumentException(
                "A sequence element is exactly one message reference or one fragment.",
                nameof(nestedFragment));
        }

        MessageRefId = messageRefId;
        NestedFragment = nestedFragment;
    }

    /// <summary>True when the element is a message reference.</summary>
    public bool IsMessageRef => MessageRefId is not null;

    /// <summary>True when the element is a nested fragment.</summary>
    public bool IsFragment => NestedFragment is not null;

    /// <summary>The message reference when <see cref="IsMessageRef"/>; otherwise null.</summary>
    public DiagramPlanElementId? MessageRefId { get; }

    /// <summary>The nested fragment when <see cref="IsFragment"/>; otherwise null.</summary>
    public DiagramFragment? NestedFragment { get; }

    public static DiagramSequenceElement MessageRef(DiagramPlanElementId id)
        => new(id, null);

    public static DiagramSequenceElement Fragment(DiagramFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        return new(null, fragment);
    }
}

/// <summary>
/// One ordered renderer-neutral sequence: message references and nested fragments in exact planner
/// order. <see cref="Elements"/> is the single ordered vocabulary (message ref or fragment) that
/// renderers, debug projection, depth calculation, and validation consume; the legacy
/// <see cref="MessageRefs"/>/<see cref="Fragments"/> projections and the two-array constructor map
/// to the degenerate messages-then-fragments element order so existing callers stay
/// source-compatible. Renderers serialize the tree depth-first and never infer placement. The empty
/// sequence is the legacy topology-empty shape that keeps flat <see cref="DiagramBranch"/> output
/// byte-stable.
/// </summary>
public sealed record DiagramSequence
{
    /// <summary>Legacy source-compatible construction from parallel message/fragment arrays (messages first, then fragments).</summary>
    public DiagramSequence(
        ImmutableArray<DiagramPlanElementId> messageRefs,
        ImmutableArray<DiagramFragment> fragments)
    {
        var builder = ImmutableArray.CreateBuilder<DiagramSequenceElement>();
        if (!messageRefs.IsDefault)
        {
            foreach (var reference in messageRefs)
            {
                builder.Add(DiagramSequenceElement.MessageRef(reference));
            }
        }

        if (!fragments.IsDefault)
        {
            foreach (var fragment in fragments)
            {
                builder.Add(DiagramSequenceElement.Fragment(fragment));
            }
        }

        Elements = builder.ToImmutable();
    }

    /// <summary>Ordered construction from the single element vocabulary.</summary>
    public DiagramSequence(ImmutableArray<DiagramSequenceElement> elements)
    {
        Elements = elements.IsDefault ? [] : elements;
    }

    /// <summary>The single ordered element vocabulary in exact planner chronology.</summary>
    public ImmutableArray<DiagramSequenceElement> Elements { get; }

    /// <summary>Source-compatible projection of the message references in element order.</summary>
    public ImmutableArray<DiagramPlanElementId> MessageRefs
        => Elements
            .Where(element => element.IsMessageRef)
            .Select(element => element.MessageRefId!.Value)
            .ToImmutableArray();

    /// <summary>Source-compatible projection of the nested fragments in element order.</summary>
    public ImmutableArray<DiagramFragment> Fragments
        => Elements
            .Where(element => element.IsFragment)
            .Select(element => element.NestedFragment!)
            .ToImmutableArray();

    /// <summary>The canonical empty sequence used by source-compatible legacy construction.</summary>
    public static DiagramSequence Empty { get; } = new([], []);
}

/// <summary>
/// One explicit planning diagnostic (for example the depth-limit code <c>DP001</c>). Planning
/// diagnostics are deterministic and grounded in the same profile/entry-point identity family as
/// the rest of the plan.
/// </summary>
public sealed record DiagramPlanDiagnostic(
    DiagnosticId Id,
    string Code,
    string Summary,
    string Detail);

/// <summary>
/// One deterministic, renderer-neutral diagram plan for one HTTP entry point. Participants,
/// messages, branches, and the ordered sequence tree preserve the planner's semantic order (client
/// request, action call, data query, then failure before success, with branches failure-first and
/// fragments in exact nesting order) and every element retains evidence and certainty. The plan is
/// memory-only and is never persisted; the debug projection is canonical, newline-only, and free of
/// absolute paths. Renderers serialize this plan verbatim and never inspect scenario graphs or infer
/// semantics themselves. Legacy construction without an explicit sequence yields a non-null empty
/// <see cref="DiagramSequence"/> and empty diagnostics so downstream flat rendering stays
/// byte-stable.
/// </summary>
public sealed record DiagramPlan
{
    public DiagramPlan(
        EntryPointId entryPoint,
        CompilationProfileId profile,
        string operationKey,
        ImmutableArray<DiagramParticipant> participants,
        ImmutableArray<DiagramMessage> messages,
        ImmutableArray<DiagramBranch> branches,
        string debugProjection,
        DiagramSequence sequence,
        ImmutableArray<DiagramPlanDiagnostic> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint.Value, nameof(entryPoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Value, nameof(profile));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey, nameof(operationKey));

        EntryPoint = entryPoint;
        Profile = profile;
        OperationKey = operationKey;
        Participants = participants.IsDefault ? [] : participants;
        Messages = messages.IsDefault ? [] : messages;
        Branches = branches.IsDefault ? [] : branches;
        DebugProjection = debugProjection;
        Sequence = sequence ?? DiagramSequence.Empty;
        Diagnostics = diagnostics.IsDefault ? [] : diagnostics;

        // A non-empty sequence owns exact message coverage: every reference must resolve to a
        // planned message and every planned message must be referenced exactly once. Missing,
        // duplicate, and omitted references are rejected here so renderers never silently skip or
        // repeat behavior. The legacy topology-empty sequence (flat branches) stays unvalidated and
        // byte-stable.
        if (!Sequence.Elements.IsEmpty)
        {
            ValidateSequenceCoverage(Sequence, Messages);
        }
    }

    /// <summary>Source-compatible construction that supplies a non-null empty sequence and empty diagnostics.</summary>
    public DiagramPlan(
        EntryPointId entryPoint,
        CompilationProfileId profile,
        string operationKey,
        ImmutableArray<DiagramParticipant> participants,
        ImmutableArray<DiagramMessage> messages,
        ImmutableArray<DiagramBranch> branches,
        string debugProjection)
        : this(entryPoint, profile, operationKey, participants, messages, branches, debugProjection,
            DiagramSequence.Empty, [])
    {
    }

    public EntryPointId EntryPoint { get; }

    public CompilationProfileId Profile { get; }

    public string OperationKey { get; }

    public ImmutableArray<DiagramParticipant> Participants { get; }

    public ImmutableArray<DiagramMessage> Messages { get; }

    public ImmutableArray<DiagramBranch> Branches { get; }

    public string DebugProjection { get; }

    public DiagramSequence Sequence { get; }

    public ImmutableArray<DiagramPlanDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Exact reference coverage for a non-empty sequence (F3): every reference resolves to a planned
    /// message, no reference repeats, and every planned message is referenced. The sequence tree is
    /// walked recursively so arm and fragment contents participate in coverage.
    /// </summary>
    private static void ValidateSequenceCoverage(
        DiagramSequence sequence,
        ImmutableArray<DiagramMessage> messages)
    {
        var references = new List<DiagramPlanElementId>();
        CollectSequenceReferences(sequence, references);

        var messageIds = messages.Select(message => message.Id).ToHashSet();
        foreach (var reference in references)
        {
            if (!messageIds.Contains(reference))
            {
                throw new ArgumentException(
                    $"A sequence element references diagram message '{reference.Value}' that is not planned.",
                    nameof(sequence));
            }
        }

        if (references.Count != references.Distinct().Count())
        {
            throw new ArgumentException(
                "A planned message is referenced more than once by the sequence tree.",
                nameof(sequence));
        }

        if (references.Count != messages.Length)
        {
            throw new ArgumentException(
                "Every planned message must be referenced exactly once by a non-empty sequence tree.",
                nameof(sequence));
        }
    }

    private static void CollectSequenceReferences(DiagramSequence sequence, List<DiagramPlanElementId> references)
    {
        foreach (var element in sequence.Elements)
        {
            if (element.IsMessageRef)
            {
                references.Add(element.MessageRefId!.Value);
            }
            else
            {
                CollectFragmentReferences(element.NestedFragment!, references);
            }
        }
    }

    private static void CollectFragmentReferences(DiagramFragment fragment, List<DiagramPlanElementId> references)
    {
        foreach (var reference in fragment.MessageRefs)
        {
            references.Add(reference);
        }

        foreach (var arm in fragment.Arms)
        {
            foreach (var reference in arm.MessageRefs)
            {
                references.Add(reference);
            }

            foreach (var nested in arm.Fragments)
            {
                CollectFragmentReferences(nested, references);
            }
        }

        foreach (var nested in fragment.Fragments)
        {
            CollectFragmentReferences(nested, references);
        }
    }
}
