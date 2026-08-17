using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Core.Identity;

public enum DocumentIdentityKind
{
    Source,
    LinkedSource,
    GeneratedSource,
    ExternalSource,
}

public sealed record DocumentIdentityDescriptor(
    ProjectId Project,
    DocumentIdentityKind Kind,
    string LogicalPath,
    string? GeneratorIdentity = null,
    string? GeneratorHintName = null);

public enum SymbolIdentityKind
{
    Namespace,
    NamedType,
    Field,
    Property,
    Event,
    Method,
}

public enum ParameterRefKind
{
    None,
    Ref,
    Out,
    In,
    RefReadOnly,
}

public sealed record ParameterIdentityDescriptor(ParameterRefKind RefKind, string FullyQualifiedType);

public sealed record SymbolIdentityDescriptor(
    ProjectId Project,
    string AssemblyIdentity,
    string ContainingMetadataName,
    SymbolIdentityKind Kind,
    string MetadataName,
    int GenericArity,
    string? ExplicitInterfaceIdentity,
    ImmutableArray<ParameterIdentityDescriptor> Parameters,
    string? ReturnType,
    bool IncludeReturnTypeInIdentity = false);

/// <summary>
/// Describes a revision-local operation anchor. Source edits may intentionally change this identity.
/// </summary>
public sealed record OperationIdentityDescriptor(
    DocumentId Document,
    MethodId Method,
    string OperationKind,
    int SourceStart,
    int SourceLength,
    int SameKindSiblingOrdinal);

/// <summary>
/// Describes the canonical identity of one HTTP entry point. Scoped by compilation profile, exact
/// root method, typed HTTP method, and canonical route so identical routes on different roots remain
/// distinct and one root exposing several routes produces several entry-point identities. The HTTP
/// method is typed so callers cannot create different identities for differently cased tokens of the
/// same method.
/// </summary>
public sealed record HttpEntryPointIdentityDescriptor(
    CompilationProfileId Profile,
    MethodId RootMethod,
    HttpMethodKind HttpMethod,
    string CanonicalRoute);

public sealed record ConfiguredMethodEntryPointIdentityDescriptor(
    CompilationProfileId Profile,
    MethodId RootMethod);

public sealed record ScenarioDirectCallExpansionIdentityDescriptor(
    CompilationProfileId Profile,
    EntryPointId EntryPoint,
    string CallSiteId,
    string? ParentStepId,
    MethodId CallerMethod,
    MethodId TargetMethod,
    OperationId Operation,
    int Depth);

public sealed record EvidenceIdentityDescriptor(
    EvidenceKind Kind,
    string Artifact,
    DocumentId? Document,
    int? SourceStart,
    int? SourceLength,
    string? Symbol,
    CertaintyLevel Certainty,
    string? ProducerId = null,
    string? ProducerVersion = null,
    string? Detail = null);

public sealed record DiagnosticIdentityDescriptor(
    string Code,
    AnalysisStage Stage,
    CompilationProfileId? Profile,
    string? SubjectId,
    int Ordinal);

/// <summary>
/// Describes a revision-local extracted operation. Source edits may intentionally change this identity.
/// </summary>
public sealed record BehaviorOperationIdentityDescriptor(
    MethodId Method,
    string OperationKind,
    int BlockOrdinal,
    int EvaluationOrdinal,
    DocumentId? Document,
    int SourceStart,
    int SourceLength,
    int SameKindSiblingOrdinal);

/// <summary>
/// Describes a revision-local exception or loop region in one method flow.
/// </summary>
public sealed record FlowRegionIdentityDescriptor(
    MethodId Method,
    string RegionKind,
    int Ordinal);

/// <summary>
/// Describes a revision-local method-flow node. Source edits may intentionally change this identity.
/// </summary>
public sealed record FlowNodeIdentityDescriptor(
    MethodId Method,
    string NodeKind,
    int BlockOrdinal,
    int EvaluationOrdinal,
    string RoleDiscriminator);

/// <summary>
/// Describes a revision-local method-flow edge between two nodes.
/// </summary>
public sealed record FlowEdgeIdentityDescriptor(
    MethodId Method,
    string Source,
    string Target,
    string EdgeKind,
    int Ordinal);

/// <summary>
/// Describes a revision-local local value-graph node.
/// </summary>
public sealed record ValueNodeIdentityDescriptor(
    MethodId Method,
    string ValueKind,
    int BlockOrdinal,
    int EvaluationOrdinal,
    string RoleDiscriminator);

/// <summary>
/// Describes a revision-local local value-graph edge.
/// </summary>
public sealed record ValueEdgeIdentityDescriptor(
    MethodId Method,
    string Source,
    string Target,
    string EdgeKind,
    int Ordinal);

/// <summary>
/// Describes a revision-local call site in one method flow.
/// </summary>
public sealed record CallSiteIdentityDescriptor(
    MethodId Method,
    OperationId InvocationOperation,
    int Ordinal);

/// <summary>
/// Describes the typed anchor of one framework-model behavior fact. Every fact identity must carry a
/// document, symbol, operation, or project anchor so even documentless facts remain profile- and
/// project-scoped.
/// </summary>
public abstract record BehaviorFactAnchor;

/// <summary>Anchors a behavior fact to a source or generated document span.</summary>
public sealed record DocumentBehaviorFactAnchor(
    DocumentId Document,
    int SourceStart,
    int SourceLength,
    SymbolId? Symbol) : BehaviorFactAnchor;

/// <summary>Anchors a documentless behavior fact to an exact symbol in a project.</summary>
public sealed record SymbolBehaviorFactAnchor(
    ProjectId Project,
    SymbolId Symbol) : BehaviorFactAnchor;

/// <summary>Anchors a documentless behavior fact to one operation inside a method.</summary>
public sealed record OperationBehaviorFactAnchor(
    MethodId Method,
    OperationId Operation) : BehaviorFactAnchor;

/// <summary>Anchors a documentless behavior fact to a project alone.</summary>
public sealed record ProjectBehaviorFactAnchor(ProjectId Project) : BehaviorFactAnchor;

/// <summary>
/// Describes a revision-local framework-model behavior fact scoped by compilation profile and a typed
/// anchor. The producer is identified by stable model id and version so the identity changes only
/// when the model contract or the evidence anchor changes, never when a display name or registration
/// order changes.
/// </summary>
public sealed record BehaviorFactIdentityDescriptor(
    CompilationProfileId Profile,
    string ModelId,
    string ModelVersion,
    string FactKind,
    BehaviorFactAnchor Anchor,
    int SameKindSiblingOrdinal);

/// <summary>
/// Describes a revision-local semantic companion fact scoped by compilation profile, fact kind,
/// method, and the exact operation that grounds it. The detail discriminator carries binding-specific
/// values (for example the target method and compiler parameter ordinal) so identical operation
/// anchors with different binding semantics remain distinct. The operation anchor plus detail already
/// discriminate every fact of one kind in one method, so no sibling ordinal is required.
/// </summary>
public sealed record SemanticFactIdentityDescriptor(
    CompilationProfileId Profile,
    string FactKind,
    MethodId Method,
    OperationId Operation,
    string? Detail);

/// <summary>
/// Describes the canonical identity of one callback boundary scoped by compilation profile, caller
/// method, outer invocation operation, callback parameter ordinal, and every semantic anchor of the
/// boundary: exact target kind and anchor, exact source contract method/invoke anchors, cardinality,
/// trigger and condition anchor, callback-local completion, contract provenance, and the canonical
/// member-operation string. Nullable anchors are encoded explicitly so distinct anchor shapes never
/// collapse. Physical paths, traversal order, timestamps, debug text, and raw captured values never
/// contribute. The identity is canonical across repeated analysis of unchanged code and independent
/// of boundary construction order.
/// </summary>
public sealed record CallbackBoundaryIdentityDescriptor(
    CompilationProfileId Profile,
    MethodId CallerMethod,
    OperationId OuterInvocationOperation,
    int ParameterOrdinal,
    CallbackTargetKind TargetKind,
    MethodId? TargetMethod,
    OperationId? TargetBodyOperation,
    MethodId? ContractMethod,
    OperationId? ContractInvokeOperation,
    CallbackCardinality Cardinality,
    CallbackTriggerKind Trigger,
    OperationId? TriggerCondition,
    CallbackCompletionKind Completion,
    CallbackContractProvenance ContractProvenance,
    string CanonicalMembers);

/// <summary>
/// Describes one scenario callback region scoped by compilation profile, entry point, and the exact
/// callback boundary identity. The canonical region identity never includes member nodes, evidence,
/// checkout paths, traversal order, or debug text: a boundary change churns the region while member,
/// evidence, or certainty changes keep the identity.
/// </summary>
public sealed record ScenarioCallbackRegionIdentityDescriptor(
    CompilationProfileId Profile,
    EntryPointId EntryPoint,
    CallbackBoundaryId BoundaryId);

/// <summary>
/// Describes one scenario-graph node scoped by compilation profile, entry point, node kind, and a
/// canonical node key. The key is the smallest stable typed identity the graph join can prove (for
/// example the entry-point id, a method id, an operation id, or an outcome key), so identical
/// semantic nodes always share one identity and distinct nodes never collapse.
/// </summary>
public sealed record ScenarioNodeIdentityDescriptor(
    CompilationProfileId Profile,
    EntryPointId EntryPoint,
    string NodeKind,
    string Key);

/// <summary>
/// Describes one scenario-graph edge scoped by compilation profile, entry point, the two stable node
/// identities it connects, the edge kind, and an ordinal. The ordinal disambiguates only parallel
/// edges between the same pair of nodes with the same kind.
/// </summary>
public sealed record ScenarioEdgeIdentityDescriptor(
    CompilationProfileId Profile,
    EntryPointId EntryPoint,
    string SourceNode,
    string TargetNode,
    string EdgeKind,
    int Ordinal);

/// <summary>
/// Describes one scenario decision scoped by compilation profile, root/containing method, and the
/// controlling <c>DecisionFlowNode</c> identity. Per architecture decision the canonical decision identity never
/// includes the entry point, labels, source text, checkout paths, traversal order, or failure-first
/// display order.
/// </summary>
public sealed record ScenarioDecisionIdentityDescriptor(
    CompilationProfileId Profile,
    MethodId RootMethod,
    MethodId Method,
    FlowNodeId ControllingFlowNode);

/// <summary>
/// Describes one semantic true/false arm of a scenario decision scoped by compilation profile,
/// root/containing method, parent decision identity, and the semantic arm polarity (architecture decision). The
/// canonical arm identity never includes the entry point.
/// </summary>
public sealed record ScenarioArmIdentityDescriptor(
    CompilationProfileId Profile,
    MethodId RootMethod,
    ScenarioDecisionId Decision,
    bool IsTrue);

/// <summary>
/// Describes one membership of a controlled scenario node in one decision arm scoped by compilation
/// profile, root/containing method, parent arm identity, and the controlled scenario node identity
/// (architecture decision). The canonical membership identity never includes the entry point.
/// </summary>
public sealed record ScenarioMembershipIdentityDescriptor(
    CompilationProfileId Profile,
    MethodId RootMethod,
    ScenarioArmId Arm,
    ScenarioNodeId ScenarioNode);

/// <summary>
/// Describes one scenario service composition scoped by compilation profile, the conditional
/// top-level method, the service type, the exact condition/read operations, the configuration key,
/// and the two opposite registration identities. The canonical composition identity never includes
/// the entry point or route, labels, source text, checkout paths, checked-in values, traversal
/// order, or display order (accepted contract requirement 5/12): entry-point/route-only changes keep the identity
/// while top-level method, condition/read, key, service type, or registration changes churn it.
/// </summary>
public sealed record ScenarioCompositionIdentityDescriptor(
    CompilationProfileId Profile,
    MethodId ProgramMethod,
    string ServiceType,
    OperationId ConditionOperation,
    OperationId ReadOperation,
    string Key,
    SemanticFactId TrueRegistrationId,
    SemanticFactId FalseRegistrationId);

/// <summary>
/// Describes one wording phrase scoped by compilation profile, entry point, phrase kind, a canonical
/// key, and an ordinal that disambiguates repeated phrases with the same key (for example several
/// outcome phrases or several fallback phrases). The ordinal is part of the identity so distinct
/// phrases never collapse.
/// </summary>
public sealed record WordingPhraseIdentityDescriptor(
    CompilationProfileId Profile,
    EntryPointId EntryPoint,
    string PhraseKind,
    string Key,
    int Ordinal);

/// <summary>
/// Describes one diagram-plan element scoped by compilation profile, entry point, element kind, and a
/// canonical key. Participants, messages, and branches use distinct keys so one identity family never
/// collapses distinct elements.
/// </summary>
public sealed record DiagramPlanElementIdentityDescriptor(
    CompilationProfileId Profile,
    EntryPointId EntryPoint,
    string ElementKind,
    string Key);
