using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

public enum DispatchBoundaryKind { Unknown, RequestResponse, Notification, Stream }
public enum DispatchCardinality { Unknown, ExactlyOne, OneOrMore, ZeroOrMore }
public enum DispatchResolution { Unknown, ExactSingle, Ambiguous, OpenGeneric, GeneratedBodyUnavailable, Unresolved }
public enum PipelineMetadataKind { Unknown, Known }

public sealed record DispatchPipelineStage
{
    public DispatchPipelineStage(int ordinal, string name, ImmutableArray<EvidenceRef> evidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateEvidence(evidence);
        Ordinal = ordinal; Name = name; Evidence = evidence;
    }
    public int Ordinal { get; }
    public string Name { get; }
    public ImmutableArray<EvidenceRef> Evidence { get; }
    private static void ValidateEvidence(ImmutableArray<EvidenceRef> evidence)
    {
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A pipeline stage requires evidence.", nameof(evidence));
        }
    }
}

public sealed record DispatchPipelineMetadata
{
    public DispatchPipelineMetadata(PipelineMetadataKind kind, ImmutableArray<DispatchPipelineStage> stages)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (kind == PipelineMetadataKind.Unknown && !stages.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Unknown pipeline metadata cannot carry stages.", nameof(stages));
        }
        if (stages.IsDefault || stages.Any(stage => stage is null)
            || stages.Select(stage => stage.Ordinal).Distinct().Count() != stages.Length)
        {
            throw new ArgumentException("Pipeline stages must be initialized and have distinct ordinals.", nameof(stages));
        }
        Kind = kind; Stages = stages.OrderBy(stage => stage.Ordinal).ToImmutableArray();
    }
    public static DispatchPipelineMetadata Unknown { get; } = new(PipelineMetadataKind.Unknown, []);
    public PipelineMetadataKind Kind { get; }
    public ImmutableArray<DispatchPipelineStage> Stages { get; }
}

public sealed record DispatchCandidate
{
    public DispatchCandidate(MethodId method, string displayName, bool bodyAvailable,
        ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ValidateEvidence(evidence, certainty);
        Method = method; DisplayName = displayName; BodyAvailable = bodyAvailable; Evidence = evidence; Certainty = certainty;
    }
    public MethodId Method { get; }
    public string DisplayName { get; }
    public bool BodyAvailable { get; }
    public ImmutableArray<EvidenceRef> Evidence { get; }
    public CertaintyLevel Certainty { get; }
    private static void ValidateEvidence(ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        if (evidence.IsDefaultOrEmpty || certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A dispatch candidate requires evidence and certainty.");
        }
        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Dispatch candidate certainty cannot exceed its evidence.", nameof(certainty));
        }
    }
}

public sealed record DispatchFact : BehaviorFact
{
    [SetsRequiredMembers]
    public DispatchFact(BehaviorFactId id, CompilationProfileId profile, string programIndexFingerprint,
        MethodId callerMethod, OperationId operationId,
        DispatchBoundaryKind boundary, DispatchCardinality cardinality, DispatchResolution resolution,
        string requestType, string? responseType, ImmutableArray<DispatchCandidate> candidates,
        DispatchPipelineMetadata pipeline, ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value); ArgumentException.ThrowIfNullOrWhiteSpace(profile.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint); ArgumentException.ThrowIfNullOrWhiteSpace(callerMethod.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId.Value); ArgumentException.ThrowIfNullOrWhiteSpace(requestType);
        if (!Enum.IsDefined(boundary) || boundary == DispatchBoundaryKind.Unknown || !Enum.IsDefined(cardinality) || cardinality == DispatchCardinality.Unknown || !Enum.IsDefined(resolution) || resolution == DispatchResolution.Unknown)
        {
            throw new ArgumentException("Dispatch enums must be defined.");
        }
        if (boundary is DispatchBoundaryKind.RequestResponse or DispatchBoundaryKind.Stream && string.IsNullOrWhiteSpace(responseType)
            || boundary == DispatchBoundaryKind.Notification && responseType is not null)
        {
            throw new ArgumentException("Dispatch response type does not match its boundary.", nameof(responseType));
        }
        if (evidence.IsDefaultOrEmpty || certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A dispatch fact requires evidence and certainty.");
        }
        var contributors = evidence
            .Concat(candidates.SelectMany(candidate => candidate.Evidence))
            .Concat(pipeline?.Stages.SelectMany(stage => stage.Evidence) ?? [])
            .Select(item => item.Certainty)
            .ToArray();
        if (certainty < contributors.Max())
        {
            throw new ArgumentException("Dispatch fact certainty cannot exceed candidate or pipeline evidence.", nameof(certainty));
        }
        if (candidates.IsDefault || candidates.Select(candidate => candidate.Method.Value).Distinct(StringComparer.Ordinal).Count() != candidates.Length)
        {
            throw new ArgumentException("Dispatch candidates must be initialized and distinct.", nameof(candidates));
        }
        if (boundary == DispatchBoundaryKind.Notification && cardinality == DispatchCardinality.ExactlyOne)
        {
            throw new ArgumentException("Notifications cannot claim exactly-one cardinality.", nameof(cardinality));
        }
        if (boundary == DispatchBoundaryKind.RequestResponse && cardinality != DispatchCardinality.ExactlyOne)
        {
            throw new ArgumentException("Request/response dispatch must have exactly-one cardinality.", nameof(cardinality));
        }
        if (resolution == DispatchResolution.Ambiguous && candidates.Length < 2)
        {
            throw new ArgumentException("Ambiguous dispatch requires at least two candidates.", nameof(candidates));
        }
        if (resolution == DispatchResolution.ExactSingle && (candidates.Length != 1
            || !candidates[0].BodyAvailable
            || candidates[0].Certainty != CertaintyLevel.Exact))
        {
            throw new ArgumentException("Exact single dispatch requires one source body candidate.", nameof(resolution));
        }
        if (resolution == DispatchResolution.GeneratedBodyUnavailable && (candidates.Length != 1 || candidates[0].BodyAvailable
            || candidates[0].Certainty == CertaintyLevel.Unknown || certainty != CertaintyLevel.Conservative && candidates[0].Certainty > certainty))
        {
            throw new ArgumentException("Generated-body-unavailable dispatch requires one candidate.", nameof(resolution));
        }
        if (resolution is DispatchResolution.Ambiguous or DispatchResolution.OpenGeneric or DispatchResolution.Unresolved && Selected(candidates, boundary, cardinality, resolution) is not null)
        {
            throw new ArgumentException("Unsupported dispatch resolution cannot select a handler.", nameof(resolution));
        }
        Id = id; Evidence = evidence; Certainty = certainty;
        Profile = profile; ProgramIndexFingerprint = programIndexFingerprint;
        CallerMethod = callerMethod; OperationId = operationId; Boundary = boundary; Cardinality = cardinality; Resolution = resolution;
        RequestType = requestType; ResponseType = responseType; Candidates = candidates.OrderBy(candidate => candidate.Method.Value, StringComparer.Ordinal).ThenBy(candidate => candidate.DisplayName, StringComparer.Ordinal).ToImmutableArray();
        Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }
    public MethodId CallerMethod { get; }
    public CompilationProfileId Profile { get; }
    public string ProgramIndexFingerprint { get; }
    public OperationId OperationId { get; }
    public DispatchBoundaryKind Boundary { get; }
    public DispatchCardinality Cardinality { get; }
    public DispatchResolution Resolution { get; }
    public string RequestType { get; }
    public string? ResponseType { get; }
    public ImmutableArray<DispatchCandidate> Candidates { get; }
    public DispatchPipelineMetadata Pipeline { get; }
    public DispatchCandidate? SelectedHandler => Selected(Candidates, Boundary, Cardinality, Resolution);
    private static DispatchCandidate? Selected(ImmutableArray<DispatchCandidate> candidates, DispatchBoundaryKind boundary, DispatchCardinality cardinality, DispatchResolution resolution)
        => boundary != DispatchBoundaryKind.Notification
            && (resolution is DispatchResolution.ExactSingle or DispatchResolution.GeneratedBodyUnavailable)
            && candidates.Length == 1 ? candidates[0] : null;
}
