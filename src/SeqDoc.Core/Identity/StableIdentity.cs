using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Core.Identity;

/// <summary>Derives SeqDoc-owned stable IDs from versioned canonical descriptors.</summary>
public static class StableIdentity
{
    public static AnalysisRunId CreateAnalysisRunId(long invocationSequence, CompilationProfileId profileId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(invocationSequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId.Value);
        return new AnalysisRunId(Hash(
            "run:v1:",
            $"{{\"invocationSequence\":{invocationSequence},\"profileId\":\"{profileId.Value}\"}}"));
    }

    /// <summary>Creates a project ID scoped to an exact compilation profile.</summary>
    public static ProjectId CreateProjectId(
        CompilationProfileId profileId,
        string repositoryRelativeProjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId.Value);
        var normalizedPath = RepositoryRelativePath.Normalize(repositoryRelativeProjectPath);
        var canonicalJson = CanonicalIdentityJson.WriteProject(profileId, normalizedPath);
        return new ProjectId(Hash("project:v1:", canonicalJson));
    }

    public static DocumentId CreateDocumentId(DocumentIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Project.Value);
        if (descriptor.Kind == DocumentIdentityKind.GeneratedSource
            && (string.IsNullOrWhiteSpace(descriptor.GeneratorIdentity)
                || string.IsNullOrWhiteSpace(descriptor.GeneratorHintName)))
        {
            throw new ArgumentException(
                "Generated documents require generator identity and hint name.",
                nameof(descriptor));
        }

        if (descriptor.Kind != DocumentIdentityKind.GeneratedSource
            && (descriptor.GeneratorIdentity is not null || descriptor.GeneratorHintName is not null))
        {
            throw new ArgumentException("Only generated documents can carry generator identity.", nameof(descriptor));
        }

        var identityPath = descriptor.Kind == DocumentIdentityKind.GeneratedSource
            ? null
            : RepositoryRelativePath.Normalize(descriptor.LogicalPath);
        var canonicalJson = CanonicalIdentityJson.WriteDocument(descriptor, identityPath);
        return new DocumentId(Hash("document:v1:", canonicalJson));
    }

    public static SymbolId CreateSymbolId(SymbolIdentityDescriptor descriptor)
    {
        ValidateSymbolDescriptor(descriptor);
        return new SymbolId(Hash("symbol:v1:", CanonicalIdentityJson.WriteSymbol(descriptor)));
    }

    public static MethodId CreateMethodId(SymbolIdentityDescriptor descriptor)
    {
        ValidateSymbolDescriptor(descriptor);
        if (descriptor.Kind != SymbolIdentityKind.Method)
        {
            throw new ArgumentException("Method IDs require a method symbol descriptor.", nameof(descriptor));
        }

        return new MethodId(Hash("method:v1:", CanonicalIdentityJson.WriteSymbol(descriptor)));
    }

    public static OperationId CreateOperationId(OperationIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Document.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.OperationKind);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.SourceStart);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.SourceLength);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.SameKindSiblingOrdinal);

        return new OperationId(Hash("operation:v1:", CanonicalIdentityJson.WriteOperation(descriptor)));
    }

    /// <summary>
    /// Creates the canonical identity of one HTTP entry point from the compilation profile, exact
    /// root method, typed HTTP method, and canonical route. The HTTP method must be a defined
    /// <see cref="HttpMethodKind"/> value; it is serialized through the canonical uppercase token so
    /// differently cased inputs can never produce distinct identities.
    /// </summary>
    public static EntryPointId CreateEntryPointId(HttpEntryPointIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RootMethod.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.CanonicalRoute, nameof(descriptor));
        if (!Enum.IsDefined(descriptor.HttpMethod))
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                $"The HTTP method value '{descriptor.HttpMethod}' is not a defined {nameof(HttpMethodKind)}.");
        }

        return new EntryPointId(Hash(
            "entry-point:v1:",
            CanonicalIdentityJson.WriteHttpEntryPoint(descriptor)));
    }

    public static EntryPointId CreateConfiguredMethodEntryPointId(ConfiguredMethodEntryPointIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RootMethod.Value, nameof(descriptor));
        return new EntryPointId(Hash("entry-point:v1:", CanonicalIdentityJson.WriteConfiguredMethodEntryPoint(descriptor)));
    }

    public static EntryPointId CreateHostedWorkerEntryPointId(HostedWorkerEntryPointIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.HostedType.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RootMethod.Value, nameof(descriptor));
        return new EntryPointId(Hash("entry-point:v1:", CanonicalIdentityJson.WriteHostedWorkerEntryPoint(descriptor)));
    }

    public static EntryPointId CreateServiceOperationEntryPointId(ServiceOperationEntryPointIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RootMethod.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.OperationKey, nameof(descriptor));
        return new EntryPointId(Hash("entry-point:v1:", CanonicalIdentityJson.WriteServiceOperationEntryPoint(descriptor)));
    }

    public static string CreateScenarioDirectCallExpansionId(ScenarioDirectCallExpansionIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EntryPoint.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.CallSiteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.CallerMethod.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.TargetMethod.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Operation.Value);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Depth);
        return Hash("scenario-direct-call:v1:", CanonicalIdentityJson.WriteScenarioDirectCallExpansion(descriptor));
    }

    public static EvidenceId CreateEvidenceId(EvidenceIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Artifact);
        ValidateOptionalRange(descriptor.SourceStart, descriptor.SourceLength, nameof(descriptor));
        if (descriptor.SourceStart.HasValue && descriptor.Document is null)
        {
            throw new ArgumentException("Source evidence ranges require a document ID.", nameof(descriptor));
        }

        if (descriptor.Kind == EvidenceKind.FrameworkModel)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ProducerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ProducerVersion);
        }
        else if (descriptor.ProducerId is not null || descriptor.ProducerVersion is not null)
        {
            throw new ArgumentException("Only framework-model evidence can carry model producer identity.", nameof(descriptor));
        }

        return new EvidenceId(Hash("evidence:v1:", CanonicalIdentityJson.WriteEvidence(descriptor)));
    }

    /// <summary>Creates detail-aware evidence identities without reinterpreting Version 1 hashes.</summary>
    public static EvidenceId CreateEvidenceIdV2(EvidenceIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new EvidenceId(Hash("evidence:v2:", CanonicalIdentityJson.WriteEvidenceV2(descriptor)));
    }

    public static DiagnosticId CreateDiagnosticId(DiagnosticIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Code);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Ordinal);
        if (descriptor.SubjectId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.SubjectId);
        }
        return new DiagnosticId(Hash("diagnostic:v1:", CanonicalIdentityJson.WriteDiagnostic(descriptor)));
    }

    public static OperationId CreateBehaviorOperationId(BehaviorOperationIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.OperationKind);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.BlockOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.EvaluationOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.SourceStart);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.SourceLength);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.SameKindSiblingOrdinal);
        if (descriptor.Document is not null && descriptor.SourceStart == 0 && descriptor.SourceLength == 0)
        {
            throw new ArgumentException(
                "Behavior operations with a source document must retain a non-empty source range.",
                nameof(descriptor));
        }

        return new OperationId(Hash(
            "behavior-operation:v1:",
            CanonicalIdentityJson.WriteBehaviorOperation(descriptor)));
    }

    public static FlowRegionId CreateFlowRegionId(FlowRegionIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RegionKind);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Ordinal);
        return new FlowRegionId(Hash(
            "flow-region:v1:",
            CanonicalIdentityJson.WriteFlowRegion(descriptor)));
    }

    public static FlowNodeId CreateFlowNodeId(FlowNodeIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.NodeKind);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.BlockOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.EvaluationOrdinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RoleDiscriminator);
        return new FlowNodeId(Hash(
            "flow-node:v1:",
            CanonicalIdentityJson.WriteFlowNode(descriptor)));
    }

    public static FlowEdgeId CreateFlowEdgeId(FlowEdgeIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EdgeKind);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Ordinal);
        return new FlowEdgeId(Hash(
            "flow-edge:v1:",
            CanonicalIdentityJson.WriteFlowEdge(descriptor)));
    }

    public static ValueNodeId CreateValueNodeId(ValueNodeIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ValueKind);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.BlockOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.EvaluationOrdinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RoleDiscriminator);
        return new ValueNodeId(Hash(
            "value-node:v1:",
            CanonicalIdentityJson.WriteValueNode(descriptor)));
    }

    public static ValueEdgeId CreateValueEdgeId(ValueEdgeIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Target);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EdgeKind);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Ordinal);
        return new ValueEdgeId(Hash(
            "value-edge:v1:",
            CanonicalIdentityJson.WriteValueEdge(descriptor)));
    }

    public static CallSiteId CreateCallSiteId(CallSiteIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.InvocationOperation.Value);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Ordinal);
        return new CallSiteId(Hash(
            "call-site:v1:",
            CanonicalIdentityJson.WriteCallSite(descriptor)));
    }

    public static BehaviorFactId CreateBehaviorFactId(BehaviorFactIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ModelId, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ModelVersion, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.FactKind, nameof(descriptor));
        ValidateBehaviorFactAnchor(descriptor.Anchor, nameof(descriptor));
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.SameKindSiblingOrdinal);
        return new BehaviorFactId(Hash(
            "behavior-fact:v1:",
            CanonicalIdentityJson.WriteBehaviorFact(descriptor)));
    }

    public static SemanticFactId CreateSemanticFactId(SemanticFactIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.FactKind, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Operation.Value, nameof(descriptor));
        if (descriptor.Detail is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Detail, nameof(descriptor));
        }

        return new SemanticFactId(Hash(
            "semantic-fact:v1:",
            CanonicalIdentityJson.WriteSemanticFact(descriptor)));
    }

    /// <summary>
    /// Creates the canonical identity of one callback boundary from the compilation profile and every
    /// semantic anchor of the boundary. Nullable anchors are encoded explicitly and the descriptor is
    /// validated against the same impossible-state invariants as <see cref="Semantics.CallbackBoundaryFact"/>
    /// so distinct anchor shapes never collapse and invalid combinations never receive an identity.
    /// </summary>
    public static CallbackBoundaryId CreateCallbackBoundaryId(CallbackBoundaryIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.CallerMethod.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.OuterInvocationOperation.Value, nameof(descriptor));
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.ParameterOrdinal);
        if (!Enum.IsDefined(descriptor.Completion))
        {
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Undefined callback completion kind.");
        }

        CallbackBoundaryFactContracts.ValidateTarget(
            descriptor.TargetKind,
            descriptor.TargetMethod,
            descriptor.TargetBodyOperation);
        CallbackBoundaryFactContracts.ValidateCardinalityTrigger(
            descriptor.Cardinality,
            descriptor.Trigger,
            descriptor.TriggerCondition);
        CallbackBoundaryFactContracts.ValidateContract(
            descriptor.ContractProvenance,
            descriptor.ContractMethod,
            descriptor.ContractInvokeOperation);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.CanonicalMembers, nameof(descriptor));

        return new CallbackBoundaryId(Hash(
            "callback-boundary:v1:",
            CanonicalIdentityJson.WriteCallbackBoundary(descriptor)));
    }

    public static ScenarioNodeId CreateScenarioNodeId(ScenarioNodeIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EntryPoint.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.NodeKind, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Key, nameof(descriptor));
        return new ScenarioNodeId(Hash(
            "scenario-node:v1:",
            CanonicalIdentityJson.WriteScenarioNode(descriptor)));
    }

    /// <summary>
    /// Creates the canonical identity of one scenario callback region from the compilation profile,
    /// entry point, and exact callback boundary identity. Physical paths, traversal order,
    /// timestamps, member order, evidence, and debug text never contribute.
    /// </summary>
    public static ScenarioCallbackRegionId CreateScenarioCallbackRegionId(ScenarioCallbackRegionIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EntryPoint.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.BoundaryId.Value, nameof(descriptor));
        return new ScenarioCallbackRegionId(Hash(
            "scenario-callback-region:v1:",
            CanonicalIdentityJson.WriteScenarioCallbackRegion(descriptor)));
    }

    public static ScenarioEdgeId CreateScenarioEdgeId(ScenarioEdgeIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EntryPoint.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.SourceNode, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.TargetNode, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EdgeKind, nameof(descriptor));
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Ordinal);
        return new ScenarioEdgeId(Hash(
            "scenario-edge:v1:",
            CanonicalIdentityJson.WriteScenarioEdge(descriptor)));
    }

    public static ScenarioDecisionId CreateScenarioDecisionId(ScenarioDecisionIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RootMethod.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Method.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ControllingFlowNode.Value, nameof(descriptor));
        return new ScenarioDecisionId(Hash(
            "scenario-decision:v1:",
            CanonicalIdentityJson.WriteScenarioDecision(descriptor)));
    }

    public static ScenarioArmId CreateScenarioArmId(ScenarioArmIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RootMethod.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Decision.Value, nameof(descriptor));
        return new ScenarioArmId(Hash(
            "scenario-arm:v1:",
            CanonicalIdentityJson.WriteScenarioArm(descriptor)));
    }

    public static ScenarioMembershipId CreateScenarioMembershipId(ScenarioMembershipIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RootMethod.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Arm.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ScenarioNode.Value, nameof(descriptor));
        return new ScenarioMembershipId(Hash(
            "scenario-membership:v1:",
            CanonicalIdentityJson.WriteScenarioMembership(descriptor)));
    }

    public static ScenarioCompositionId CreateScenarioCompositionId(ScenarioCompositionIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ProgramMethod.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ServiceType, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ConditionOperation.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ReadOperation.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Key, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.TrueRegistrationId.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.FalseRegistrationId.Value, nameof(descriptor));
        return new ScenarioCompositionId(Hash(
            "scenario-composition:v1:",
            CanonicalIdentityJson.WriteScenarioComposition(descriptor)));
    }

    public static WordingPhraseId CreateWordingPhraseId(WordingPhraseIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EntryPoint.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.PhraseKind, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Key, nameof(descriptor));
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Ordinal);
        return new WordingPhraseId(Hash(
            "wording-phrase:v1:",
            CanonicalIdentityJson.WriteWordingPhrase(descriptor)));
    }

    public static DiagramPlanElementId CreateDiagramPlanElementId(DiagramPlanElementIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Profile.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.EntryPoint.Value, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ElementKind, nameof(descriptor));
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Key, nameof(descriptor));
        return new DiagramPlanElementId(Hash(
            "diagram-element:v1:",
            CanonicalIdentityJson.WriteDiagramPlanElement(descriptor)));
    }

    private static void ValidateBehaviorFactAnchor(BehaviorFactAnchor anchor, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        switch (anchor)
        {
            case DocumentBehaviorFactAnchor document:
                ArgumentException.ThrowIfNullOrWhiteSpace(document.Document.Value, parameterName);
                if (document.SourceStart < 0 || document.SourceLength <= 0)
                {
                    throw new ArgumentException(
                        "Document behavior-fact anchors require a non-empty source range.",
                        parameterName);
                }

                if (document.Symbol is not null)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(document.Symbol.Value.Value, parameterName);
                }

                break;
            case SymbolBehaviorFactAnchor symbol:
                ArgumentException.ThrowIfNullOrWhiteSpace(symbol.Project.Value, parameterName);
                ArgumentException.ThrowIfNullOrWhiteSpace(symbol.Symbol.Value, parameterName);
                break;
            case OperationBehaviorFactAnchor operation:
                ArgumentException.ThrowIfNullOrWhiteSpace(operation.Method.Value, parameterName);
                ArgumentException.ThrowIfNullOrWhiteSpace(operation.Operation.Value, parameterName);
                break;
            case ProjectBehaviorFactAnchor project:
                ArgumentException.ThrowIfNullOrWhiteSpace(project.Project.Value, parameterName);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Unknown behavior-fact anchor kind.");
        }
    }

    internal static string Hash(string prefix, string canonicalJson)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return string.Concat(prefix, Convert.ToHexStringLower(digest));
    }

    private static void ValidateSymbolDescriptor(SymbolIdentityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Project.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.AssemblyIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.MetadataName);
        ArgumentOutOfRangeException.ThrowIfNegative(descriptor.GenericArity);
        if (descriptor.ExplicitInterfaceIdentity is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ExplicitInterfaceIdentity);
        }

        if (descriptor.ReturnType is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ReturnType);
        }

        if (descriptor.Parameters.IsDefault)
        {
            throw new ArgumentException("Symbol parameters must be an initialized immutable array.", nameof(descriptor));
        }

        foreach (var parameter in descriptor.Parameters)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(parameter.FullyQualifiedType);
        }

        if (descriptor.IncludeReturnTypeInIdentity)
        {
            if (descriptor.Kind != SymbolIdentityKind.Method)
            {
                throw new ArgumentException("Only method identities can require return-type disambiguation.", nameof(descriptor));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ReturnType);
        }
    }

    private static void ValidateOptionalRange(int? start, int? length, string parameterName)
    {
        if (start.HasValue != length.HasValue || start < 0 || length < 0)
        {
            throw new ArgumentException("Source start and length must be non-negative and supplied together.", parameterName);
        }
    }
}

internal static class CanonicalIdentityJson
{
    public static string WriteCompilationProfile(
        string repositoryRelativeTargetPath,
        string configuration,
        string targetFramework,
        string? runtimeIdentifier,
        ImmutableSortedDictionary<string, string> msBuildProperties,
        ImmutableSortedDictionary<string, string> analysisProperties)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("repositoryRelativeTargetPath", repositoryRelativeTargetPath);
            writer.WriteString("configuration", configuration);
            writer.WriteString("targetFramework", targetFramework);
            if (runtimeIdentifier is null)
            {
                writer.WriteNull("runtimeIdentifier");
            }
            else
            {
                writer.WriteString("runtimeIdentifier", runtimeIdentifier);
            }

            WriteProperties(writer, "msBuildProperties", msBuildProperties);
            WriteProperties(writer, "analysisProperties", analysisProperties);
        });
    }

    public static string WriteProject(CompilationProfileId profileId, string repositoryRelativeProjectPath)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", profileId.Value);
            writer.WriteString("repositoryRelativeProjectPath", repositoryRelativeProjectPath);
        });
    }

    public static string WriteDocument(DocumentIdentityDescriptor descriptor, string? identityPath)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("projectId", descriptor.Project.Value);
            writer.WriteString("kind", descriptor.Kind.ToString());
            WriteNullableString(writer, "logicalPath", identityPath);
            WriteNullableString(writer, "generatorIdentity", descriptor.GeneratorIdentity);
            WriteNullableString(writer, "generatorHintName", descriptor.GeneratorHintName);
        });
    }

    public static string WriteSymbol(SymbolIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("projectId", descriptor.Project.Value);
            writer.WriteString("assemblyIdentity", descriptor.AssemblyIdentity);
            writer.WriteString("containingMetadataName", descriptor.ContainingMetadataName);
            writer.WriteString("kind", descriptor.Kind.ToString());
            writer.WriteString("metadataName", descriptor.MetadataName);
            writer.WriteNumber("genericArity", descriptor.GenericArity);
            WriteNullableString(writer, "explicitInterfaceIdentity", descriptor.ExplicitInterfaceIdentity);
            writer.WriteStartArray("parameters");
            foreach (var parameter in descriptor.Parameters)
            {
                writer.WriteStartObject();
                writer.WriteString("refKind", parameter.RefKind.ToString());
                writer.WriteString("type", parameter.FullyQualifiedType);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteBoolean("includeReturnType", descriptor.IncludeReturnTypeInIdentity);
            WriteNullableString(
                writer,
                "returnType",
                descriptor.IncludeReturnTypeInIdentity ? descriptor.ReturnType : null);
        });
    }

    public static string WriteOperation(OperationIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("documentId", descriptor.Document.Value);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("operationKind", descriptor.OperationKind);
            writer.WriteNumber("sourceStart", descriptor.SourceStart);
            writer.WriteNumber("sourceLength", descriptor.SourceLength);
            writer.WriteNumber("sameKindSiblingOrdinal", descriptor.SameKindSiblingOrdinal);
        });
    }

    public static string WriteHttpEntryPoint(HttpEntryPointIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("rootMethodId", descriptor.RootMethod.Value);
            writer.WriteString("httpMethod", HttpMethodCanonicalToken.Get(descriptor.HttpMethod));
            writer.WriteString("canonicalRoute", descriptor.CanonicalRoute);
        });
    }

    public static string WriteConfiguredMethodEntryPoint(ConfiguredMethodEntryPointIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("rootMethodId", descriptor.RootMethod.Value);
            writer.WriteString("kind", "ConfiguredMethod");
        });
    }

    public static string WriteHostedWorkerEntryPoint(HostedWorkerEntryPointIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("hostedTypeId", descriptor.HostedType.Value);
            writer.WriteString("rootMethodId", descriptor.RootMethod.Value);
            writer.WriteString("kind", "HostedWorker");
        });
    }

    public static string WriteServiceOperationEntryPoint(ServiceOperationEntryPointIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("rootMethodId", descriptor.RootMethod.Value);
            writer.WriteString("operationKey", descriptor.OperationKey);
            writer.WriteString("kind", "ServiceOperation");
        });
    }

    public static string WriteScenarioDirectCallExpansion(ScenarioDirectCallExpansionIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("entryPointId", descriptor.EntryPoint.Value);
            writer.WriteString("callSiteId", descriptor.CallSiteId);
            WriteNullableString(writer, "parentStepId", descriptor.ParentStepId);
            writer.WriteString("callerMethodId", descriptor.CallerMethod.Value);
            writer.WriteString("targetMethodId", descriptor.TargetMethod.Value);
            writer.WriteString("operationId", descriptor.Operation.Value);
            writer.WriteNumber("depth", descriptor.Depth);
        });
    }

    public static string WriteEvidence(EvidenceIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("kind", descriptor.Kind.ToString());
            writer.WriteString("artifact", descriptor.Artifact);
            WriteNullableString(writer, "documentId", descriptor.Document?.Value);
            WriteNullableNumber(writer, "sourceStart", descriptor.SourceStart);
            WriteNullableNumber(writer, "sourceLength", descriptor.SourceLength);
            WriteNullableString(writer, "symbol", descriptor.Symbol);
            writer.WriteString("certainty", descriptor.Certainty.ToString());
            WriteNullableString(writer, "producerId", descriptor.ProducerId);
            WriteNullableString(writer, "producerVersion", descriptor.ProducerVersion);
        });
    }

    public static string WriteEvidenceV2(EvidenceIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 2);
            writer.WriteString("kind", descriptor.Kind.ToString());
            writer.WriteString("artifact", descriptor.Artifact);
            WriteNullableString(writer, "documentId", descriptor.Document?.Value);
            WriteNullableNumber(writer, "sourceStart", descriptor.SourceStart);
            WriteNullableNumber(writer, "sourceLength", descriptor.SourceLength);
            WriteNullableString(writer, "symbol", descriptor.Symbol);
            WriteNullableString(writer, "detail", descriptor.Detail);
            writer.WriteString("certainty", descriptor.Certainty.ToString());
            WriteNullableString(writer, "producerId", descriptor.ProducerId);
            WriteNullableString(writer, "producerVersion", descriptor.ProducerVersion);
        });
    }

    public static string WriteDiagnostic(DiagnosticIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("code", descriptor.Code);
            writer.WriteString("stage", descriptor.Stage.ToString());
            WriteNullableString(writer, "profileId", descriptor.Profile?.Value);
            WriteNullableString(writer, "subjectId", descriptor.SubjectId);
            writer.WriteNumber("ordinal", descriptor.Ordinal);
        });
    }

    public static string WriteBehaviorOperation(BehaviorOperationIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("operationKind", descriptor.OperationKind);
            writer.WriteNumber("blockOrdinal", descriptor.BlockOrdinal);
            writer.WriteNumber("evaluationOrdinal", descriptor.EvaluationOrdinal);
            WriteNullableString(writer, "documentId", descriptor.Document?.Value);
            writer.WriteNumber("sourceStart", descriptor.SourceStart);
            writer.WriteNumber("sourceLength", descriptor.SourceLength);
            writer.WriteNumber("sameKindSiblingOrdinal", descriptor.SameKindSiblingOrdinal);
        });
    }

    public static string WriteFlowRegion(FlowRegionIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("regionKind", descriptor.RegionKind);
            writer.WriteNumber("ordinal", descriptor.Ordinal);
        });
    }

    public static string WriteFlowNode(FlowNodeIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("nodeKind", descriptor.NodeKind);
            writer.WriteNumber("blockOrdinal", descriptor.BlockOrdinal);
            writer.WriteNumber("evaluationOrdinal", descriptor.EvaluationOrdinal);
            writer.WriteString("roleDiscriminator", descriptor.RoleDiscriminator);
        });
    }

    public static string WriteFlowEdge(FlowEdgeIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("source", descriptor.Source);
            writer.WriteString("target", descriptor.Target);
            writer.WriteString("edgeKind", descriptor.EdgeKind);
            writer.WriteNumber("ordinal", descriptor.Ordinal);
        });
    }

    public static string WriteValueNode(ValueNodeIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("valueKind", descriptor.ValueKind);
            writer.WriteNumber("blockOrdinal", descriptor.BlockOrdinal);
            writer.WriteNumber("evaluationOrdinal", descriptor.EvaluationOrdinal);
            writer.WriteString("roleDiscriminator", descriptor.RoleDiscriminator);
        });
    }

    public static string WriteValueEdge(ValueEdgeIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("source", descriptor.Source);
            writer.WriteString("target", descriptor.Target);
            writer.WriteString("edgeKind", descriptor.EdgeKind);
            writer.WriteNumber("ordinal", descriptor.Ordinal);
        });
    }

    public static string WriteCallSite(CallSiteIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("invocationOperation", descriptor.InvocationOperation.Value);
            writer.WriteNumber("ordinal", descriptor.Ordinal);
        });
    }

    public static string WriteBehaviorFact(BehaviorFactIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("modelId", descriptor.ModelId);
            writer.WriteString("modelVersion", descriptor.ModelVersion);
            writer.WriteString("factKind", descriptor.FactKind);
            writer.WriteStartObject("anchor");
            switch (descriptor.Anchor)
            {
                case DocumentBehaviorFactAnchor document:
                    writer.WriteString("kind", "document");
                    writer.WriteString("documentId", document.Document.Value);
                    writer.WriteNumber("sourceStart", document.SourceStart);
                    writer.WriteNumber("sourceLength", document.SourceLength);
                    WriteNullableString(writer, "symbol", document.Symbol?.Value);
                    break;
                case SymbolBehaviorFactAnchor symbol:
                    writer.WriteString("kind", "symbol");
                    writer.WriteString("projectId", symbol.Project.Value);
                    writer.WriteString("symbolId", symbol.Symbol.Value);
                    break;
                case OperationBehaviorFactAnchor operation:
                    writer.WriteString("kind", "operation");
                    writer.WriteString("methodId", operation.Method.Value);
                    writer.WriteString("operationId", operation.Operation.Value);
                    break;
                case ProjectBehaviorFactAnchor project:
                    writer.WriteString("kind", "project");
                    writer.WriteString("projectId", project.Project.Value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        "Unknown behavior-fact anchor kind.");
            }

            writer.WriteEndObject();
            writer.WriteNumber("sameKindSiblingOrdinal", descriptor.SameKindSiblingOrdinal);
        });
    }

    public static string WriteSemanticFact(SemanticFactIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("factKind", descriptor.FactKind);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("operationId", descriptor.Operation.Value);
            WriteNullableString(writer, "detail", descriptor.Detail);
        });
    }

    public static string WriteCallbackBoundary(CallbackBoundaryIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("callerMethodId", descriptor.CallerMethod.Value);
            writer.WriteString("outerInvocationOperationId", descriptor.OuterInvocationOperation.Value);
            writer.WriteNumber("parameterOrdinal", descriptor.ParameterOrdinal);
            writer.WriteString("targetKind", descriptor.TargetKind.ToString());
            WriteNullableString(writer, "targetMethodId", descriptor.TargetMethod?.Value);
            WriteNullableString(writer, "targetBodyOperationId", descriptor.TargetBodyOperation?.Value);
            WriteNullableString(writer, "contractMethodId", descriptor.ContractMethod?.Value);
            WriteNullableString(writer, "contractInvokeOperationId", descriptor.ContractInvokeOperation?.Value);
            writer.WriteString("cardinality", descriptor.Cardinality.ToString());
            writer.WriteString("trigger", descriptor.Trigger.ToString());
            WriteNullableString(writer, "triggerConditionId", descriptor.TriggerCondition?.Value);
            writer.WriteString("completion", descriptor.Completion.ToString());
            writer.WriteString("contractProvenance", descriptor.ContractProvenance.ToString());
            writer.WriteString("canonicalMembers", descriptor.CanonicalMembers);
        });
    }

    public static string WriteScenarioNode(ScenarioNodeIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("entryPointId", descriptor.EntryPoint.Value);
            writer.WriteString("nodeKind", descriptor.NodeKind);
            writer.WriteString("key", descriptor.Key);
        });
    }

    public static string WriteScenarioCallbackRegion(ScenarioCallbackRegionIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("entryPointId", descriptor.EntryPoint.Value);
            writer.WriteString("boundaryId", descriptor.BoundaryId.Value);
        });
    }

    public static string WriteScenarioEdge(ScenarioEdgeIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("entryPointId", descriptor.EntryPoint.Value);
            writer.WriteString("sourceNode", descriptor.SourceNode);
            writer.WriteString("targetNode", descriptor.TargetNode);
            writer.WriteString("edgeKind", descriptor.EdgeKind);
            writer.WriteNumber("ordinal", descriptor.Ordinal);
        });
    }

    public static string WriteScenarioDecision(ScenarioDecisionIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("rootMethodId", descriptor.RootMethod.Value);
            writer.WriteString("methodId", descriptor.Method.Value);
            writer.WriteString("controllingFlowNodeId", descriptor.ControllingFlowNode.Value);
        });
    }

    public static string WriteScenarioArm(ScenarioArmIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("rootMethodId", descriptor.RootMethod.Value);
            writer.WriteString("decisionId", descriptor.Decision.Value);
            writer.WriteBoolean("isTrue", descriptor.IsTrue);
        });
    }

    public static string WriteScenarioMembership(ScenarioMembershipIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("rootMethodId", descriptor.RootMethod.Value);
            writer.WriteString("armId", descriptor.Arm.Value);
            writer.WriteString("scenarioNodeId", descriptor.ScenarioNode.Value);
        });
    }

    public static string WriteScenarioComposition(ScenarioCompositionIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("programMethodId", descriptor.ProgramMethod.Value);
            writer.WriteString("serviceType", descriptor.ServiceType);
            writer.WriteString("conditionOperationId", descriptor.ConditionOperation.Value);
            writer.WriteString("readOperationId", descriptor.ReadOperation.Value);
            writer.WriteString("key", descriptor.Key);
            writer.WriteString("trueRegistrationId", descriptor.TrueRegistrationId.Value);
            writer.WriteString("falseRegistrationId", descriptor.FalseRegistrationId.Value);
        });
    }

    public static string WriteWordingPhrase(WordingPhraseIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("entryPointId", descriptor.EntryPoint.Value);
            writer.WriteString("phraseKind", descriptor.PhraseKind);
            writer.WriteString("key", descriptor.Key);
            writer.WriteNumber("ordinal", descriptor.Ordinal);
        });
    }

    public static string WriteDiagramPlanElement(DiagramPlanElementIdentityDescriptor descriptor)
    {
        return Write(writer =>
        {
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("profileId", descriptor.Profile.Value);
            writer.WriteString("entryPointId", descriptor.EntryPoint.Value);
            writer.WriteString("elementKind", descriptor.ElementKind);
            writer.WriteString("key", descriptor.Key);
        });
    }

    private static string Write(Action<Utf8JsonWriter> writeProperties)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeProperties(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteProperties(
        Utf8JsonWriter writer,
        string propertyName,
        ImmutableSortedDictionary<string, string> properties)
    {
        writer.WriteStartObject(propertyName);
        foreach (var property in properties)
        {
            writer.WriteString(property.Key, property.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, int? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }
}
