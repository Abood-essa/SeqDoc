using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.ProgramIndex;

public enum ProjectKind
{
    Library,
    Executable,
    Web,
    Worker,
    Test,
    Unknown,
}

public enum DocumentOrigin
{
    Source,
    LinkedSource,
    GeneratedSource,
    ExternalSource,
}

public enum ProgramTypeKind
{
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    RecordClass,
    RecordStruct,
    Unknown,
}

public enum ProgramReferenceKind
{
    Project,
    Package,
    Assembly,
}

public enum ProgramMemberKind
{
    Field,
    Property,
    Event,
}

public enum InventoryMarkerKind
{
    EntryPointCandidate,
    FrameworkConfigurationCandidate,
    ContractCandidate,
    BinaryCandidate,
}

public sealed record ProgramProject(
    ProjectId Id,
    string Name,
    string RepositoryRelativePath,
    CompilationProfileId Profile,
    string TargetFramework,
    ProjectKind Kind,
    string BuildFingerprint,
    ImmutableArray<ProjectId> ProjectReferences,
    ImmutableArray<EvidenceRef> Evidence);

public sealed record ProgramDocument(
    DocumentId Id,
    ProjectId Project,
    string LogicalPath,
    DocumentOrigin Origin,
    string ContentFingerprint,
    string? SemanticFingerprint,
    ImmutableArray<EvidenceRef> Evidence);

public sealed record ProgramType(
    SymbolId Id,
    ProjectId Project,
    SymbolId Namespace,
    string MetadataName,
    ProgramTypeKind Kind,
    SymbolId? BaseType,
    ImmutableArray<SymbolId> Interfaces,
    string SignatureFingerprint,
    ImmutableArray<EvidenceRef> Evidence);

public sealed record ProgramNamespace(
    SymbolId Id,
    ProjectId Project,
    string Name,
    ImmutableArray<EvidenceRef> Evidence);

public sealed record ProgramMember(
    SymbolId Id,
    ProjectId Project,
    SymbolId ContainingType,
    ProgramMemberKind Kind,
    string Name,
    string FullyQualifiedType,
    string SignatureFingerprint,
    ImmutableArray<EvidenceRef> Evidence);

public sealed record ProgramAttributeApplication(
    string Id,
    SymbolId Target,
    string AttributeType,
    string Constructor,
    ImmutableArray<string> Arguments,
    ImmutableArray<EvidenceRef> Evidence);

public sealed record ParameterDescriptor(
    string Name,
    string FullyQualifiedType,
    ParameterRefKind RefKind);

public sealed record ProgramMethod(
    MethodId Id,
    SymbolId Symbol,
    SymbolId ContainingType,
    string Name,
    string DisplaySignature,
    ImmutableArray<ParameterDescriptor> Parameters,
    string ReturnType,
    string SignatureFingerprint,
    string? BodyFingerprint,
    ImmutableArray<EvidenceRef> Evidence);

public sealed record ProgramReference(
    string Id,
    ProjectId Project,
    ProgramReferenceKind Kind,
    string Identity,
    string? Version,
    ImmutableArray<EvidenceRef> Evidence);

public sealed record ProgramInvocation(
    OperationId Id,
    MethodId ContainingMethod,
    MethodId? BoundTarget,
    string DisplayTarget,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

public sealed record ProgramInventoryMarker(
    string Id,
    ProjectId Project,
    InventoryMarkerKind Kind,
    SymbolId? Symbol,
    ImmutableArray<EvidenceRef> Evidence);

/// <summary>Contains one immutable, profile-isolated baseline Program Index.</summary>
public sealed record ProgramIndexSnapshot(
    int SchemaVersion,
    string ProducerVersion,
    CompilationProfile Profile,
    ImmutableArray<ProgramProject> Projects,
    ImmutableArray<ProgramDocument> Documents,
    ImmutableArray<ProgramNamespace> Namespaces,
    ImmutableArray<ProgramType> Types,
    ImmutableArray<ProgramMember> Members,
    ImmutableArray<ProgramMethod> Methods,
    ImmutableArray<ProgramAttributeApplication> Attributes,
    ImmutableArray<ProgramReference> References,
    ImmutableArray<ProgramInvocation> Invocations,
    ImmutableArray<ProgramInventoryMarker> InventoryMarkers,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string InputManifestHash,
    string IndexFingerprint);
