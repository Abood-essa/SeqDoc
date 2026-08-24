using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.FrameworkModels.CoreWcf;

/// <summary>
/// Versioned CoreWCF/classic WCF service contract model. Emits two independently proven fact kinds
/// rather than one: <see cref="ServiceOperationCapabilityFact"/> (a method is the exact compiler-proven
/// implementation, implicit or explicit, of an interface member carrying an admitted
/// <c>[OperationContract]</c> attribute on an interface carrying an admitted <c>[ServiceContract]</c>
/// attribute, with a real source body) and <see cref="ServiceEndpointRegistrationFact"/> (an exact
/// <c>IServiceBuilder.AddServiceEndpoint&lt;TService, TContract&gt;(Binding, string)</c> invocation).
/// Capability alone never claims hosting, registration, dispatch, or execution — promoting a capability
/// to an executable Scenario Graph root requires joining it with a matching registration fact, which
/// this model never does itself (see <c>ScenarioGraphBuilder</c>). CoreWCF's
/// <c>CoreWCF.ServiceContractAttribute</c>/<c>CoreWCF.OperationContractAttribute</c>/<c>CoreWCF.FaultContractAttribute</c>
/// (assembly <c>CoreWCF.Primitives</c> 1.9.0.0) and classic WCF's
/// <c>System.ServiceModel.ServiceContractAttribute</c>/<c>System.ServiceModel.OperationContractAttribute</c>/<c>System.ServiceModel.FaultContractAttribute</c>
/// (assembly <c>System.ServiceModel.Primitives</c> 8.1.2.0) are both admitted, matched by exact original
/// attribute-class identity (never a display-name string) via
/// <see cref="FrameworkInterfaceMemberIdentity.InterfaceTypeAttributes"/>/<c>InterfaceMethodAttributes</c>;
/// a ServiceContract/OperationContract/FaultContract pair is admitted only when every attribute in the
/// pair resolves to the exact same family, rejecting foreign-assembly lookalikes and mixed families. A
/// concrete type whose exact base type is <c>System.ServiceModel.ClientBase&lt;TContract&gt;</c> for an
/// admitted contract is never treated as capability; it emits <see cref="ServiceClientBoundaryFact"/>
/// instead, classified as generated when the type carries the exact
/// <c>System.CodeDom.Compiler.GeneratedCodeAttribute</c> marker real generator tools apply. Effective
/// certainty is always the weakest (never the strongest) of the input certainty and every contributing
/// evidence item's own certainty. Faults, generated client presentation, and outbound HTTP/SOAP
/// boundaries beyond the compiler facts above are out of scope for this model version.
/// </summary>
public sealed class CoreWcfServiceModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.corewcf.services";
    public const string ModelVersionValue = "2.0.0";

    private enum ContractFamily
    {
        CoreWcf,
        ClassicWcf,
    }

    /// <summary>Exact fully qualified framework identities admitted by this model version.</summary>
    internal static class Identity
    {
        public const string CoreWcfAssembly = "CoreWCF.Primitives";
        public const string CoreWcfAssemblyVersion = "1.9.0.0";
        public const string CoreWcfServiceContractAttribute = "CoreWCF.ServiceContractAttribute";
        public const string CoreWcfOperationContractAttribute = "CoreWCF.OperationContractAttribute";
        public const string CoreWcfFaultContractAttribute = "CoreWCF.FaultContractAttribute";

        public const string SystemServiceModelAssembly = "System.ServiceModel.Primitives";
        public const string SystemServiceModelAssemblyVersion = "8.1.2.0";
        public const string SystemServiceModelServiceContractAttribute = "System.ServiceModel.ServiceContractAttribute";
        public const string SystemServiceModelOperationContractAttribute = "System.ServiceModel.OperationContractAttribute";
        public const string SystemServiceModelFaultContractAttribute = "System.ServiceModel.FaultContractAttribute";
        public const string ClientBaseMetadataName = "System.ServiceModel.ClientBase`1";

        public const string GeneratedCodeAttribute = "System.CodeDom.Compiler.GeneratedCodeAttribute";

        // GeneratedCodeAttribute is implemented in System.Private.CoreLib at run time, but the compiler
        // resolves ContainingAssembly to the System.Runtime reference-assembly facade it is type-forwarded
        // through, so the exact-identity match must target that compile-time assembly, not the runtime one.
        public const string CoreLibAssembly = "System.Runtime";
        public const string CoreLibAssemblyVersion = "10.0.0.0";
    }

    public FrameworkModelDescriptor Descriptor { get; } = new(
        ModelIdValue,
        ModelVersionValue,
        "CoreWCF/WCF Service Contracts",
        Order: 110);

    /// <summary>
    /// Applies when the unmodified Program Index contains an exact admitted ServiceContract or
    /// OperationContract attribute identity. A lookalike-only index without an exact admitted attribute
    /// identity remains non-applicable.
    /// </summary>
    public bool IsApplicable(FrameworkDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ProgramIndex.Attributes.Any(attribute =>
            attribute.AttributeType is Identity.CoreWcfServiceContractAttribute
                or Identity.CoreWcfOperationContractAttribute
                or Identity.SystemServiceModelServiceContractAttribute
                or Identity.SystemServiceModelOperationContractAttribute);
    }

    public ValueTask<ModelResult> AnalyzeSymbolAsync(
        SymbolDescriptor symbol,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(AnalyzeMethod(symbol, context));
    }

    public ValueTask<ModelResult> AnalyzeOperationAsync(
        OperationDescriptor operation,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(AnalyzeOperation(operation, context));
    }

    private ModelResult AnalyzeMethod(SymbolDescriptor symbol, FrameworkAnalysisContext context)
    {
        if (!string.Equals(symbol.Kind, "Method", StringComparison.Ordinal))
        {
            return ModelResult.Unrecognized;
        }

        var index = context.ProgramIndex;
        var method = index.Methods.FirstOrDefault(candidate => candidate.Symbol == symbol.Id);
        if (method is null)
        {
            return ModelResult.Unrecognized;
        }

        var type = index.Types.FirstOrDefault(candidate => candidate.Id == method.ContainingType);
        if (type is null)
        {
            return ModelResult.Unrecognized;
        }

        var profileId = context.Profile.Id;

        if (symbol.MethodShape is null)
        {
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.EligibilityShapeUnavailable(profileId, method.Id.Value)]);
        }

        var shape = symbol.MethodShape;
        if (shape.MethodSymbol != method.Symbol || shape.DeclaringTypeSymbol != type.Id)
        {
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.EligibilityShapeUnavailable(profileId, $"{method.Id.Value}shape-symbol-mismatch")]);
        }

        if (shape.ImplementedInterfaceMembers.IsDefaultOrEmpty)
        {
            return ModelResult.Unrecognized;
        }

        var admittedMembers = shape.ImplementedInterfaceMembers
            .Select(member => (Member: member, Family: TryGetAdmittedFamily(member)))
            .Where(entry => entry.Family is not null)
            .Select(entry => (entry.Member, Family: entry.Family!.Value))
            .OrderBy(entry => entry.Member.InterfaceMethodSymbol.Value, StringComparer.Ordinal)
            .ToArray();

        // A client boundary (an exact System.ServiceModel.ClientBase<TContract> derivation) is never
        // capability, regardless of how many admitted contracts it implements.
        if (IsClientBaseDerived(shape.DeclaringType) && admittedMembers.Length > 0)
        {
            var clientKind = HasGeneratedCodeMarker(shape.DeclaringTypeAttributes)
                ? ServiceClientKind.GeneratedClient
                : ServiceClientKind.SourceClient;
            // AnalyzeMethod runs once per admitting method on the client type (for example once per
            // ClientBase<T> method the interface declares), so the fact is anchored to this method's own
            // symbol rather than the declaring type alone: a type-level anchor would make every admitting
            // method on the same client independently emit the exact same BehaviorFactId, and because
            // ServiceClientBoundaryFact is not a GeneralBehaviorFact the host's exact-payload dedup never
            // applies, so the repeated identity would report as a genuine conflict and admit none of them.
            var clientFacts = admittedMembers
                .Select(entry => entry.Member.InterfaceType.MetadataName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(contractType => contractType, StringComparer.Ordinal)
                .Select((contractType, ordinal) => (BehaviorFact)BuildClientBoundaryFact(type, symbol.Id, contractType, clientKind, profileId, ordinal))
                .ToImmutableArray();
            return new ModelResult(true, facts: clientFacts);
        }

        if (admittedMembers.Length == 0)
        {
            return ModelResult.Unrecognized;
        }

        if (admittedMembers.Length > 1)
        {
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.AmbiguousOperationImplementation(profileId, method.Id.Value)]);
        }

        var (member, family) = admittedMembers[0];
        if (!IsEligibleServiceOperation(shape, member))
        {
            return ModelResult.Unrecognized;
        }

        if (method.BodyFingerprint is null)
        {
            // The generated/source client boundary for a *service implementation* shape: a
            // compiler-proven interface-member match with no source body (for example a partial
            // declaration with no implementing body) never admits capability.
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.OperationImplementationUnavailable(profileId, method.Id.Value)]);
        }

        var contractEvidence = FindAttributeEvidence(index, member.InterfaceTypeSymbol, ServiceContractMetadataName(family));
        var operationEvidence = FindAttributeEvidence(index, member.InterfaceMethodSymbol, OperationContractMetadataName(family));
        if (contractEvidence.IsDefaultOrEmpty || operationEvidence.IsDefaultOrEmpty)
        {
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.EligibilityShapeUnavailable(profileId, $"{method.Id.Value}attribute-evidence-unavailable")]);
        }

        var effectiveCertainty = WeakestCertainty(symbol.Certainty, method.Evidence, type.Evidence, contractEvidence, operationEvidence);
        var diagnostics = ImmutableArray<AnalysisDiagnostic>.Empty;
        if (symbol.Certainty != CertaintyLevel.Exact)
        {
            diagnostics = [CoreWcfServiceModelDiagnostics.DegradedInputCertainty(profileId, method.Id.Value)];
        }

        var serviceContractType = member.InterfaceType.MetadataName;
        var operationName = member.InterfaceMethodMetadataName;
        var operationKey = $"{serviceContractType}.{operationName}";
        var underlyingEvidence = BuildUnderlyingEvidence(method, type, contractEvidence, operationEvidence);

        var facts = new List<BehaviorFact>
        {
            new ServiceOperationCapabilityFact
            {
                Id = CreateBehaviorFactId(profileId, "service-operation-capability", new SymbolBehaviorFactAnchor(type.Project, symbol.Id), 0),
                RootMethod = method.Id,
                ServiceContractType = serviceContractType,
                ImplementationType = type.MetadataName,
                OperationName = operationName,
                OperationKey = operationKey,
                Evidence = CreateModelEvidence($"service-operation-capability:{operationKey}:{method.Id.Value}", underlyingEvidence, effectiveCertainty),
                Certainty = effectiveCertainty,
            },
        };

        var faultOrdinal = 0;
        var faultAttributes = member.InterfaceMethodAttributes.IsDefault
            ? Enumerable.Empty<FrameworkAttributeApplicationIdentity>()
            : member.InterfaceMethodAttributes
                .Where(attribute => FaultContractFamily(attribute.AttributeType) == family)
                .OrderBy(attribute => attribute.AttributeType.MetadataName, StringComparer.Ordinal);
        foreach (var faultAttribute in faultAttributes)
        {
            var faultTypes = faultAttribute.TypeArguments.IsDefault
                ? Enumerable.Empty<FrameworkTypeIdentity>()
                : faultAttribute.TypeArguments.OrderBy(t => t.MetadataName, StringComparer.Ordinal);
            foreach (var faultType in faultTypes)
            {
                facts.Add(new ServiceFaultContractFact
                {
                    Id = CreateBehaviorFactId(profileId, "service-fault-contract", new SymbolBehaviorFactAnchor(type.Project, symbol.Id), faultOrdinal++),
                    ServiceContractType = serviceContractType,
                    OperationName = operationName,
                    FaultType = faultType.MetadataName,
                    Evidence = CreateModelEvidence($"service-fault-contract:{operationKey}:{faultType.MetadataName}", operationEvidence, effectiveCertainty),
                    Certainty = effectiveCertainty,
                });
            }
        }

        return new ModelResult(true, facts: facts.ToImmutableArray(), diagnostics: diagnostics);
    }

    private ModelResult AnalyzeOperation(OperationDescriptor operation, FrameworkAnalysisContext context)
    {
        if (!string.Equals(operation.Kind, "Invocation", StringComparison.Ordinal) || operation.ServiceEndpointShape is not { } shape)
        {
            return ModelResult.Unrecognized;
        }

        var profileId = context.Profile.Id;
        var effectiveCertainty = WeakestCertainty(operation.Certainty, operation.Evidence);
        var diagnostics = ImmutableArray<AnalysisDiagnostic>.Empty;
        if (operation.Certainty != CertaintyLevel.Exact)
        {
            diagnostics = [CoreWcfServiceModelDiagnostics.DegradedInputCertainty(profileId, operation.Id.Value)];
        }

        var fact = new ServiceEndpointRegistrationFact
        {
            Id = CreateBehaviorFactId(profileId, "service-endpoint-registration", new OperationBehaviorFactAnchor(operation.Method, operation.Id), 0),
            ImplementationType = shape.ServiceType,
            ServiceContractType = shape.ContractType,
            BindingType = shape.BindingType,
            Address = shape.Address,
            Evidence = CreateModelEvidence($"service-endpoint-registration:{operation.Id.Value}", operation.Evidence, effectiveCertainty),
            Certainty = effectiveCertainty,
        };
        return new ModelResult(true, facts: [fact], diagnostics: diagnostics);
    }

    private static ContractFamily? TryGetAdmittedFamily(FrameworkInterfaceMemberIdentity member)
    {
        var contractFamilies = (member.InterfaceTypeAttributes.IsDefault ? [] : member.InterfaceTypeAttributes)
            .Select(attribute => ServiceContractFamily(attribute.AttributeType))
            .Where(family => family is not null)
            .Select(family => family!.Value)
            .Distinct()
            .ToArray();
        var operationFamilies = (member.InterfaceMethodAttributes.IsDefault ? [] : member.InterfaceMethodAttributes)
            .Select(attribute => OperationContractFamily(attribute.AttributeType))
            .Where(family => family is not null)
            .Select(family => family!.Value)
            .Distinct()
            .ToArray();
        if (contractFamilies.Length != 1 || operationFamilies.Length != 1 || contractFamilies[0] != operationFamilies[0])
        {
            // Missing, ambiguous-family, or mixed-family (CoreWCF ServiceContract with a classic
            // OperationContract, or vice versa) pairs are never admitted.
            return null;
        }

        return contractFamilies[0];
    }

    private static bool IsEligibleServiceOperation(FrameworkMethodShape shape, FrameworkInterfaceMemberIdentity member)
        => (shape.IsOrdinary || member.IsExplicitImplementation)
            && !shape.IsStatic
            && !shape.IsAbstract
            && shape.GenericArity == 0
            && (member.IsExplicitImplementation || shape.IsPublic)
            && IsEligibleImplementingType(shape.DeclaringType);

    private static bool IsEligibleImplementingType(FrameworkTypeShape type)
        => type.IsClass
            && type.IsPublicOrNestedPublic
            && !type.IsAbstract
            && !type.IsStatic
            && type.GenericArity == 0;

    private static bool IsClientBaseDerived(FrameworkTypeShape type)
        => !type.BaseTypeChain.IsDefault
            && type.BaseTypeChain.Any(identity =>
                identity.AssemblyIdentity == Identity.SystemServiceModelAssembly
                && identity.AssemblyVersion == Identity.SystemServiceModelAssemblyVersion
                && identity.MetadataName == Identity.ClientBaseMetadataName);

    private static bool HasGeneratedCodeMarker(ImmutableArray<FrameworkAttributeApplicationIdentity> declaringTypeAttributes)
        => !declaringTypeAttributes.IsDefault
            && declaringTypeAttributes.Any(attribute =>
                attribute.AttributeType.AssemblyIdentity == Identity.CoreLibAssembly
                && attribute.AttributeType.AssemblyVersion == Identity.CoreLibAssemblyVersion
                && attribute.AttributeType.MetadataName == Identity.GeneratedCodeAttribute);

    private static ContractFamily? ServiceContractFamily(FrameworkTypeIdentity attributeType)
        => IsExactAttribute(attributeType, Identity.CoreWcfAssembly, Identity.CoreWcfAssemblyVersion, Identity.CoreWcfServiceContractAttribute) ? ContractFamily.CoreWcf
            : IsExactAttribute(attributeType, Identity.SystemServiceModelAssembly, Identity.SystemServiceModelAssemblyVersion, Identity.SystemServiceModelServiceContractAttribute) ? ContractFamily.ClassicWcf
            : null;

    private static ContractFamily? OperationContractFamily(FrameworkTypeIdentity attributeType)
        => IsExactAttribute(attributeType, Identity.CoreWcfAssembly, Identity.CoreWcfAssemblyVersion, Identity.CoreWcfOperationContractAttribute) ? ContractFamily.CoreWcf
            : IsExactAttribute(attributeType, Identity.SystemServiceModelAssembly, Identity.SystemServiceModelAssemblyVersion, Identity.SystemServiceModelOperationContractAttribute) ? ContractFamily.ClassicWcf
            : null;

    private static ContractFamily? FaultContractFamily(FrameworkTypeIdentity attributeType)
        => IsExactAttribute(attributeType, Identity.CoreWcfAssembly, Identity.CoreWcfAssemblyVersion, Identity.CoreWcfFaultContractAttribute) ? ContractFamily.CoreWcf
            : IsExactAttribute(attributeType, Identity.SystemServiceModelAssembly, Identity.SystemServiceModelAssemblyVersion, Identity.SystemServiceModelFaultContractAttribute) ? ContractFamily.ClassicWcf
            : null;

    private static bool IsExactAttribute(FrameworkTypeIdentity attributeType, string assembly, string assemblyVersion, string metadataName)
        => attributeType.AssemblyIdentity == assembly
            && attributeType.AssemblyVersion == assemblyVersion
            && attributeType.MetadataName == metadataName;

    private static string ServiceContractMetadataName(ContractFamily family)
        => family == ContractFamily.CoreWcf ? Identity.CoreWcfServiceContractAttribute : Identity.SystemServiceModelServiceContractAttribute;

    private static string OperationContractMetadataName(ContractFamily family)
        => family == ContractFamily.CoreWcf ? Identity.CoreWcfOperationContractAttribute : Identity.SystemServiceModelOperationContractAttribute;

    /// <summary>
    /// Finds the source evidence for the exact Program Index attribute application matching
    /// <paramref name="target"/> and <paramref name="metadataName"/>. The strict identity decision
    /// (which family, which assembly) has already been made from
    /// <see cref="FrameworkInterfaceMemberIdentity"/>'s exact attribute-class identities; this lookup
    /// only recovers the evidence for the attribute this model already proved is the admitted one.
    /// </summary>
    private static ImmutableArray<EvidenceRef> FindAttributeEvidence(ProgramIndexSnapshot index, SymbolId target, string metadataName)
        => index.Attributes
            .Where(attribute => attribute.Target == target && attribute.AttributeType == metadataName)
            .SelectMany(attribute => attribute.Evidence)
            .ToImmutableArray();

    /// <summary>
    /// The effective certainty is always the weakest (highest-ordinal, per <see cref="CertaintyLevel"/>'s
    /// Exact/Conservative/Heuristic/Unknown declaration order) of the input certainty and every
    /// contributing evidence item's own certainty; a fact can never claim to be more certain than its
    /// weakest contributor.
    /// </summary>
    private static CertaintyLevel WeakestCertainty(CertaintyLevel input, params ImmutableArray<EvidenceRef>[] evidenceGroups)
    {
        var weakest = input;
        foreach (var group in evidenceGroups)
        {
            if (group.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var item in group)
            {
                if (item.Certainty > weakest)
                {
                    weakest = item.Certainty;
                }
            }
        }

        return weakest;
    }

    private static ImmutableArray<EvidenceRef> BuildUnderlyingEvidence(
        ProgramMethod method,
        ProgramType type,
        ImmutableArray<EvidenceRef> contractEvidence,
        ImmutableArray<EvidenceRef> operationEvidence)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceRef>();
        builder.AddRange(method.Evidence);
        builder.AddRange(type.Evidence);
        builder.AddRange(contractEvidence);
        builder.AddRange(operationEvidence);
        return builder
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private ServiceClientBoundaryFact BuildClientBoundaryFact(
        ProgramType type,
        SymbolId triggeringMethodSymbol,
        string serviceContractType,
        ServiceClientKind clientKind,
        CompilationProfileId profileId,
        int ordinal)
    {
        var effectiveCertainty = WeakestCertainty(CertaintyLevel.Exact, type.Evidence);
        return new ServiceClientBoundaryFact
        {
            Id = CreateBehaviorFactId(profileId, "service-client-boundary", new SymbolBehaviorFactAnchor(type.Project, triggeringMethodSymbol), ordinal),
            ServiceContractType = serviceContractType,
            ClientType = type.MetadataName,
            ClientKind = clientKind,
            Evidence = CreateModelEvidence($"service-client-boundary:{type.MetadataName}:{serviceContractType}", type.Evidence, effectiveCertainty),
            Certainty = effectiveCertainty,
        };
    }

    /// <summary>
    /// Builds the single framework-model evidence record for one fact, mirroring the deterministic
    /// evidence-identity construction every other framework model uses: the producing descriptor, a
    /// stable fact subject, the effective certainty, and the complete canonical underlying evidence-ID
    /// sequence, so records with different payloads never share one identity.
    /// </summary>
    private ImmutableArray<EvidenceRef> CreateModelEvidence(
        string subject,
        ImmutableArray<EvidenceRef> underlying,
        CertaintyLevel certainty)
    {
        var canonical = underlying
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var artifact = $"{Descriptor.ModelId}:{Descriptor.Version}";
        var evidencePayload = $"{subject}\u001f{string.Join('\u001f', canonical.Select(item => item.Id.Value))}";
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel,
            artifact,
            null,
            null,
            null,
            null,
            certainty,
            Descriptor.ModelId,
            Descriptor.Version,
            Detail: evidencePayload));
        return
        [
            new EvidenceRef(
                id,
                EvidenceKind.FrameworkModel,
                artifact,
                range: null,
                symbol: null,
                detail: evidencePayload,
                certainty,
                canonical,
                Descriptor.ModelId,
                Descriptor.Version),
        ];
    }

    private BehaviorFactId CreateBehaviorFactId(
        CompilationProfileId profileId,
        string factKind,
        BehaviorFactAnchor anchor,
        int siblingOrdinal)
        => StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
            profileId,
            Descriptor.ModelId,
            Descriptor.Version,
            factKind,
            anchor,
            siblingOrdinal));
}
