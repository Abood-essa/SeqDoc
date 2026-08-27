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
    /// A coarse, inexpensive pre-filter: applies when the Program Index contains any attribute whose
    /// metadata name matches an admitted ServiceContract or OperationContract name. This is a
    /// <see cref="SeqDoc.Core.ProgramIndex.ProgramAttributeApplication.AttributeType"/> string match
    /// only — it cannot distinguish the real CoreWCF/<c>System.ServiceModel</c> identity from a
    /// same-qualified-name lookalike in a foreign assembly, so a lookalike-only index still returns
    /// applicable here. The real exact-identity decision (assembly, version, metadata name, and family
    /// consistency) is made later, per member, in <see cref="AnalyzeMethod"/> against
    /// <see cref="FrameworkInterfaceMemberIdentity.InterfaceTypeAttributes"/>/<c>InterfaceMethodAttributes</c>;
    /// this method only decides whether the model runs at all, never whether any fact is admitted.
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

        // A client boundary (a constructed System.ServiceModel.ClientBase<TContract> derivation for THIS
        // exact admitted contract) is never capability. Matching is per admitted member's own contract
        // identity against ClientBase's constructed generic argument, not "ClientBase appears somewhere
        // in the base-type chain": a class deriving ClientBase<IContractA> that also separately
        // implements an unrelated admitted IContractB must never emit a client boundary for IContractB.
        var clientMembers = admittedMembers
            .Where(entry => IsClientBaseDerivedForContract(shape.DeclaringType, entry.Member.InterfaceType))
            .ToArray();
        if (HasClientBase(shape.DeclaringType))
        {
            if (clientMembers.Length == 0 || clientMembers.Any(entry => !HasRequiredClientAttributeEvidence(entry.Member, entry.Family)))
            {
                return new ModelResult(false, diagnostics:
                    [CoreWcfServiceModelDiagnostics.EligibilityShapeUnavailable(profileId, $"{method.Id.Value}\u001fclient-evidence-unavailable")]);
            }

            var hasGeneratedMarker = HasGeneratedCodeMarker(shape.DeclaringTypeAttributes);
            var generatedMarkerEvidence = hasGeneratedMarker
                ? FindGeneratedCodeMarkerEvidence(shape.DeclaringTypeAttributes)
                : ImmutableArray<EvidenceRef>.Empty;
            if (hasGeneratedMarker && generatedMarkerEvidence.IsDefaultOrEmpty)
            {
                return new ModelResult(false, diagnostics:
                    [CoreWcfServiceModelDiagnostics.EligibilityShapeUnavailable(profileId, $"{method.Id.Value}\u001fgenerated-marker-evidence-unavailable")]);
            }

            var clientKind = hasGeneratedMarker
                ? ServiceClientKind.GeneratedClient
                : ServiceClientKind.SourceClient;
            // AnalyzeMethod runs once per admitting method on the client type (for example once per
            // ClientBase<T> method the interface declares), so the fact is anchored to this method's own
            // symbol rather than the declaring type alone: a type-level anchor would make every admitting
            // method on the same client independently emit the exact same BehaviorFactId, and because
            // ServiceClientBoundaryFact is not a GeneralBehaviorFact the host's exact-payload dedup never
            // applies, so the repeated identity would report as a genuine conflict and admit none of them.
            var clientFacts = clientMembers
                .DistinctBy(entry => (entry.Member.InterfaceType.MetadataName, entry.Family))
                .OrderBy(entry => entry.Member.InterfaceType.MetadataName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Family)
                .Select((entry, ordinal) => (BehaviorFact)BuildClientBoundaryFact(
                    type, symbol.Id, symbol.Certainty, symbol.Evidence, generatedMarkerEvidence,
                    entry.Member, entry.Family, clientKind, profileId, ordinal))
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

        var contractEvidence = member.InterfaceTypeAttributes
            .Where(attribute => ServiceContractFamily(attribute.AttributeType) == family)
            .SelectMany(attribute => attribute.Evidence.IsDefault ? [] : attribute.Evidence)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var operationEvidence = member.InterfaceMethodAttributes
            .Where(attribute => OperationContractFamily(attribute.AttributeType) == family)
            .SelectMany(attribute => attribute.Evidence.IsDefault ? [] : attribute.Evidence)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
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
                ServiceContractTypeSymbol = member.InterfaceTypeSymbol,
                ImplementationType = type.MetadataName,
                ImplementationTypeSymbol = type.Id,
                OperationName = operationName,
                OperationSymbol = member.InterfaceMethodSymbol,
                OperationKey = operationKey,
                Evidence = CreateModelEvidence($"service-operation-capability:{operationKey}:{method.Id.Value}", underlyingEvidence, effectiveCertainty),
                Certainty = effectiveCertainty,
            },
        };

        var faultOrdinal = 0;
        var faultAttributes = member.InterfaceMethodAttributes.IsDefault
            ? Enumerable.Empty<FrameworkAttributeApplicationIdentity>()
            : member.InterfaceMethodAttributes
                .Where(attribute => FaultContractFamily(attribute.AttributeType) == family
                    && !attribute.Evidence.IsDefaultOrEmpty)
                .OrderBy(attribute => attribute.AttributeType.MetadataName, StringComparer.Ordinal);
        foreach (var faultAttribute in faultAttributes)
        {
            var faultTypes = faultAttribute.TypeArguments.IsDefault
                ? Enumerable.Empty<FrameworkTypeIdentity>()
                : faultAttribute.TypeArguments.OrderBy(t => t.MetadataName, StringComparer.Ordinal);
            var faultAttributeEvidence = faultAttribute.Evidence.IsDefault
                ? ImmutableArray<EvidenceRef>.Empty
                : faultAttribute.Evidence;
            foreach (var faultType in faultTypes)
            {
                var faultUnderlyingEvidence = underlyingEvidence
                    .Concat(faultAttributeEvidence)
                    .DistinctBy(item => item.Id.Value)
                    .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                    .ToImmutableArray();
                var faultCertainty = WeakestCertainty(effectiveCertainty, faultAttributeEvidence);
                facts.Add(new ServiceFaultContractFact
                {
                    Id = CreateBehaviorFactId(profileId, "service-fault-contract", new SymbolBehaviorFactAnchor(type.Project, symbol.Id), faultOrdinal++),
                    ServiceContractType = serviceContractType,
                    OperationName = operationName,
                    OperationSymbol = member.InterfaceMethodSymbol,
                    FaultType = faultType.MetadataName,
                    FaultTypeIdentity = faultType,
                    Evidence = CreateModelEvidence($"service-fault-contract:{operationKey}:{faultType.MetadataName}", faultUnderlyingEvidence, faultCertainty),
                    Certainty = faultCertainty,
                });
            }
        }

        return new ModelResult(true, facts: facts.ToImmutableArray(), diagnostics: diagnostics);
    }

    private ModelResult AnalyzeOperation(OperationDescriptor operation, FrameworkAnalysisContext context)
    {
        if (!string.Equals(operation.Kind, "Invocation", StringComparison.Ordinal))
        {
            return ModelResult.Unrecognized;
        }

        if (operation.ClientInvocationShape is { } invocationShape)
        {
            return AnalyzeClientInvocation(operation, invocationShape, context);
        }

        if (operation.ServiceEndpointShape is not { } shape)
        {
            return ModelResult.Unrecognized;
        }

        // An exact AddServiceEndpoint invocation proves only that source contains a call with that
        // compiler identity; it does not prove the application registers or dispatches it. Only an
        // invocation the compiler-proven active host chain (generic-host construction/execution,
        // UseStartup<TStartup> selection, TStartup's own Configure/UseServiceModel callback, and a
        // matching AddService<TService> receiver) reaches promotes to registration evidence — an
        // unreachable/unsupported chain never emits a fact here, so it falls through to the existing
        // capability-without-registration conservative diagnostic in ScenarioGraphBuilder instead of
        // inventing execution.
        if (!shape.HostChainProven || shape.ServiceTypeSymbol is not { } serviceTypeSymbol || shape.ContractTypeSymbol is not { } contractTypeSymbol)
        {
            return ModelResult.Unrecognized;
        }

        var profileId = context.Profile.Id;
        var underlyingEvidence = operation.Evidence
            .Concat(shape.HostChainEvidence.IsDefault ? [] : shape.HostChainEvidence)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var effectiveCertainty = WeakestCertainty(operation.Certainty, underlyingEvidence);
        var diagnostics = ImmutableArray<AnalysisDiagnostic>.Empty;
        if (operation.Certainty != CertaintyLevel.Exact)
        {
            diagnostics = [CoreWcfServiceModelDiagnostics.DegradedInputCertainty(profileId, operation.Id.Value)];
        }

        var fact = new ServiceEndpointRegistrationFact
        {
            Id = CreateBehaviorFactId(profileId, "service-endpoint-registration", new OperationBehaviorFactAnchor(operation.Method, operation.Id), 0),
            ImplementationType = shape.ServiceType.MetadataName,
            ImplementationTypeSymbol = serviceTypeSymbol,
            ServiceContractType = shape.ContractType.MetadataName,
            ServiceContractTypeSymbol = contractTypeSymbol,
            BindingType = shape.BindingType.MetadataName,
            Address = shape.Address,
            Evidence = CreateModelEvidence($"service-endpoint-registration:{operation.Id.Value}", underlyingEvidence, effectiveCertainty),
            Certainty = effectiveCertainty,
        };
        return new ModelResult(true, facts: [fact], diagnostics: diagnostics);
    }

    /// <summary>
    /// Admits an <see cref="ServiceClientInvocationFact"/> only when every identity link holds: the
    /// invocation's receiver is a concrete (never interface-typed) type whose exact symbol equals the
    /// invoked method's own declaring type (ruling out an inherited-method or reinterpreted-receiver
    /// ambiguity), that declaring type's exact base-type chain derives
    /// <c>System.ServiceModel.ClientBase&lt;TContract&gt;</c>, the invoked method is an ordinary
    /// non-generic instance method, and it implements exactly one admitted CoreWCF/classic-WCF
    /// interface member whose contract is the exact contract <c>ClientBase</c> was constructed with.
    /// Whether the receiver's client type actually carries an admitted <see cref="ServiceClientBoundaryFact"/>
    /// with a <see cref="ServiceClientKind.SourceClient"/>/<see cref="ServiceClientKind.GeneratedClient"/>
    /// classification is proven independently and joined later by the Scenario Graph, mirroring how
    /// capability and registration are proven separately and joined there.
    /// </summary>
    private ModelResult AnalyzeClientInvocation(
        OperationDescriptor operation,
        FrameworkClientInvocationShapeDescriptor shape,
        FrameworkAnalysisContext context)
    {
        var profileId = context.Profile.Id;
        var methodShape = shape.TargetMethodShape;

        if (!shape.ReceiverIsConcreteType
            || shape.ReceiverTypeSymbol is not { } receiverTypeSymbol
            || receiverTypeSymbol != methodShape.DeclaringTypeSymbol)
        {
            // Ambiguous (interface-typed) receiver, or the invoked method is inherited from a type
            // other than the receiver's own exact static type: never admitted.
            return ModelResult.Unrecognized;
        }

        if (!methodShape.IsOrdinary || methodShape.IsStatic || methodShape.IsAbstract || methodShape.GenericArity != 0)
        {
            return ModelResult.Unrecognized;
        }

        if (!HasClientBase(methodShape.DeclaringType))
        {
            return ModelResult.Unrecognized;
        }

        var admittedMembers = (methodShape.ImplementedInterfaceMembers.IsDefaultOrEmpty
                ? Enumerable.Empty<FrameworkInterfaceMemberIdentity>()
                : methodShape.ImplementedInterfaceMembers)
            .Select(member => (Member: member, Family: TryGetAdmittedFamily(member)))
            .Where(entry => entry.Family is not null)
            .Select(entry => (entry.Member, Family: entry.Family!.Value))
            .Where(entry => IsClientBaseDerivedForContract(methodShape.DeclaringType, entry.Member.InterfaceType))
            .ToArray();
        if (admittedMembers.Length != 1)
        {
            // Zero admitted contract-matching members (not a call to a supported client operation) or
            // more than one (ambiguous) never admits an invocation fact.
            return ModelResult.Unrecognized;
        }

        var (member, family) = admittedMembers[0];
        var contractEvidence = member.InterfaceTypeAttributes
            .Where(attribute => ServiceContractFamily(attribute.AttributeType) == family)
            .SelectMany(attribute => attribute.Evidence.IsDefault ? [] : attribute.Evidence)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var operationEvidence = member.InterfaceMethodAttributes
            .Where(attribute => OperationContractFamily(attribute.AttributeType) == family)
            .SelectMany(attribute => attribute.Evidence.IsDefault ? [] : attribute.Evidence)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        if (contractEvidence.IsDefaultOrEmpty || operationEvidence.IsDefaultOrEmpty)
        {
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.EligibilityShapeUnavailable(profileId, $"{operation.Id.Value}client-invocation-attribute-evidence-unavailable")]);
        }

        var underlyingEvidence = operation.Evidence
            .Concat(contractEvidence)
            .Concat(operationEvidence)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var effectiveCertainty = WeakestCertainty(operation.Certainty, underlyingEvidence);
        var diagnostics = ImmutableArray<AnalysisDiagnostic>.Empty;
        if (operation.Certainty != CertaintyLevel.Exact)
        {
            diagnostics = [CoreWcfServiceModelDiagnostics.DegradedInputCertainty(profileId, operation.Id.Value)];
        }

        var serviceContractType = member.InterfaceType.MetadataName;
        var operationName = member.InterfaceMethodMetadataName;
        var operationKey = $"{serviceContractType}.{operationName}";
        var fact = new ServiceClientInvocationFact
        {
            Id = CreateBehaviorFactId(profileId, "service-client-invocation", new OperationBehaviorFactAnchor(operation.Method, operation.Id), 0),
            CallerMethod = operation.Method,
            InvocationOperation = operation.Id,
            ServiceContractType = serviceContractType,
            ServiceContractTypeSymbol = member.InterfaceTypeSymbol,
            ClientType = methodShape.DeclaringType.Identity.MetadataName,
            ClientTypeSymbol = receiverTypeSymbol,
            OperationName = operationName,
            OperationSymbol = member.InterfaceMethodSymbol,
            OperationKey = operationKey,
            ResultClaim = shape.ResultClaim,
            IsAwaited = shape.IsAwaited,
            ResultBindingName = shape.ResultBindingName,
            DeclaredResultType = shape.DeclaredResultType,
            Evidence = CreateModelEvidence($"service-client-invocation:{operationKey}:{operation.Id.Value}", underlyingEvidence, effectiveCertainty),
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

    /// <summary>
    /// True only when <paramref name="type"/>'s base-type chain contains the exact
    /// <c>System.ServiceModel.ClientBase&lt;TContract&gt;</c> identity *constructed with the exact
    /// admitted <paramref name="contract"/> as its type argument*. Finding <c>ClientBase\`1</c> anywhere
    /// in the chain without proving the constructed argument matches this specific contract is not
    /// sufficient.
    /// </summary>
    private static bool IsClientBaseDerivedForContract(FrameworkTypeShape type, FrameworkTypeIdentity contract)
        => !type.BaseTypeChainWithArguments.IsDefault
            && type.BaseTypeChainWithArguments.Any(baseType =>
                baseType.Identity.AssemblyIdentity == Identity.SystemServiceModelAssembly
                && baseType.Identity.AssemblyVersion == Identity.SystemServiceModelAssemblyVersion
                && baseType.Identity.MetadataName == Identity.ClientBaseMetadataName
                && !baseType.TypeArguments.IsDefault
                && baseType.TypeArguments.Length == 1
                && baseType.TypeArguments[0] == contract);

    private static bool HasClientBase(FrameworkTypeShape type)
        => !type.BaseTypeChainWithArguments.IsDefault
            && type.BaseTypeChainWithArguments.Any(baseType =>
                baseType.Identity.AssemblyIdentity == Identity.SystemServiceModelAssembly
                && baseType.Identity.AssemblyVersion == Identity.SystemServiceModelAssemblyVersion
                && baseType.Identity.MetadataName == Identity.ClientBaseMetadataName
                && !baseType.TypeArguments.IsDefault
                && baseType.TypeArguments.Length == 1);

    private static bool HasRequiredClientAttributeEvidence(FrameworkInterfaceMemberIdentity member, ContractFamily family)
    {
        var contractEvidence = member.InterfaceTypeAttributes
            .Where(attribute => ServiceContractFamily(attribute.AttributeType) == family)
            .SelectMany(attribute => attribute.Evidence.IsDefault ? [] : attribute.Evidence);
        var operationEvidence = member.InterfaceMethodAttributes
            .Where(attribute => OperationContractFamily(attribute.AttributeType) == family)
            .SelectMany(attribute => attribute.Evidence.IsDefault ? [] : attribute.Evidence);
        return contractEvidence.Any() && operationEvidence.Any();
    }

    private static bool HasGeneratedCodeMarker(ImmutableArray<FrameworkAttributeApplicationIdentity> declaringTypeAttributes)
        => !declaringTypeAttributes.IsDefault
            && declaringTypeAttributes.Any(attribute =>
                attribute.AttributeType.AssemblyIdentity == Identity.CoreLibAssembly
                && attribute.AttributeType.AssemblyVersion == Identity.CoreLibAssemblyVersion
                && attribute.AttributeType.MetadataName == Identity.GeneratedCodeAttribute);

    private static ImmutableArray<EvidenceRef> FindGeneratedCodeMarkerEvidence(
        ImmutableArray<FrameworkAttributeApplicationIdentity> declaringTypeAttributes)
    {
        if (declaringTypeAttributes.IsDefault)
        {
            return ImmutableArray<EvidenceRef>.Empty;
        }

        var marker = declaringTypeAttributes.FirstOrDefault(attribute =>
            attribute.AttributeType.AssemblyIdentity == Identity.CoreLibAssembly
            && attribute.AttributeType.AssemblyVersion == Identity.CoreLibAssemblyVersion
            && attribute.AttributeType.MetadataName == Identity.GeneratedCodeAttribute);
        return marker is null || marker.Evidence.IsDefault ? ImmutableArray<EvidenceRef>.Empty : marker.Evidence;
    }

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
        CertaintyLevel triggeringMethodCertainty,
        ImmutableArray<EvidenceRef> triggeringMethodEvidence,
        ImmutableArray<EvidenceRef> generatedMarkerEvidence,
        FrameworkInterfaceMemberIdentity member,
        ContractFamily family,
        ServiceClientKind clientKind,
        CompilationProfileId profileId,
        int ordinal)
    {
        var underlyingEvidence = type.Evidence
            .Concat(triggeringMethodEvidence.IsDefault ? [] : triggeringMethodEvidence)
            .Concat(member.InterfaceTypeAttributes
                .Where(attribute => ServiceContractFamily(attribute.AttributeType) == family)
                .SelectMany(attribute => attribute.Evidence.IsDefault ? [] : attribute.Evidence))
            .Concat(member.InterfaceMethodAttributes
                .Where(attribute => OperationContractFamily(attribute.AttributeType) == family)
                .SelectMany(attribute => attribute.Evidence.IsDefault ? [] : attribute.Evidence))
            .Concat(generatedMarkerEvidence.IsDefault ? [] : generatedMarkerEvidence)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var effectiveCertainty = WeakestCertainty(triggeringMethodCertainty, underlyingEvidence);
        var serviceContractType = member.InterfaceType.MetadataName;
        return new ServiceClientBoundaryFact
        {
            Id = CreateBehaviorFactId(profileId, "service-client-boundary", new SymbolBehaviorFactAnchor(type.Project, triggeringMethodSymbol), ordinal),
            ServiceContractType = serviceContractType,
            ServiceContractTypeSymbol = member.InterfaceTypeSymbol,
            ClientType = type.MetadataName,
            ClientTypeSymbol = type.Id,
            ClientKind = clientKind,
            Evidence = CreateModelEvidence($"service-client-boundary:{type.MetadataName}:{serviceContractType}", underlyingEvidence, effectiveCertainty),
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
