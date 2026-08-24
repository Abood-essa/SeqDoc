using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.FrameworkModels.CoreWcf;

/// <summary>
/// Versioned CoreWCF/classic WCF service contract model. Admits an executable root only when a
/// concrete method is the exact compiler-proven implementation (implicit or explicit) of an interface
/// member that carries an admitted <c>[OperationContract]</c> attribute on an interface that itself
/// carries an admitted <c>[ServiceContract]</c> attribute — CoreWCF's <c>CoreWCF.ServiceContractAttribute</c>/
/// <c>CoreWCF.OperationContractAttribute</c> (assembly <c>CoreWCF.Primitives</c>) and classic WCF's
/// <c>System.ServiceModel.ServiceContractAttribute</c>/<c>System.ServiceModel.OperationContractAttribute</c>
/// (assembly <c>System.ServiceModel.Primitives</c>) are both admitted. The interface-member-implementation
/// mapping comes entirely from the controlled eligibility projector
/// (<see cref="FrameworkMethodShape.ImplementedInterfaceMembers"/>); this model applies eligibility
/// rules only, never symbol resolution. A method with no compiler-proven source body (for example a
/// generated or metadata-only client proxy) never admits a root, so client-side and generated-source
/// boundaries fail closed without a fabricated fact. Faults, generated client presentation, and
/// outbound HTTP/SOAP boundaries are out of scope for this model version; they are separate accepted
/// contract companions delivered by later work.
/// </summary>
public sealed class CoreWcfServiceModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.corewcf.services";
    public const string ModelVersionValue = "1.0.0";

    /// <summary>Exact fully qualified framework identities admitted by this model version.</summary>
    internal static class Identity
    {
        public const string CoreWcfServiceContractAttribute = "CoreWCF.ServiceContractAttribute";
        public const string CoreWcfOperationContractAttribute = "CoreWCF.OperationContractAttribute";
        public const string SystemServiceModelServiceContractAttribute = "System.ServiceModel.ServiceContractAttribute";
        public const string SystemServiceModelOperationContractAttribute = "System.ServiceModel.OperationContractAttribute";
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
            IsServiceContractAttribute(attribute.AttributeType) || IsOperationContractAttribute(attribute.AttributeType));
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
        // C-WCF-1: this model version emits only the exact operation entry point; outcomes, faults, and
        // outbound boundaries are separate accepted contract companions delivered by later work.
        return ValueTask.FromResult(ModelResult.Unrecognized);
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

        // Controlled eligibility facts: missing, mismatched, or incomplete shape input fails closed
        // with a stable diagnostic and no exact root.
        if (symbol.MethodShape is null)
        {
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.EligibilityShapeUnavailable(profileId, method.Id.Value)]);
        }

        var shape = symbol.MethodShape;
        if (shape.MethodSymbol != method.Symbol || shape.DeclaringTypeSymbol != type.Id)
        {
            // The shape must be bound to the exact indexed method and containing type; a shape from
            // another symbol can never support this root.
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.EligibilityShapeUnavailable(profileId, $"{method.Id.Value}\u001fshape-symbol-mismatch")]);
        }

        if (shape.ImplementedInterfaceMembers.IsDefaultOrEmpty)
        {
            return ModelResult.Unrecognized;
        }

        var admittedMembers = shape.ImplementedInterfaceMembers
            .Where(member => IsAdmittedOperation(index, member))
            .OrderBy(member => member.InterfaceMethodSymbol.Value, StringComparer.Ordinal)
            .ToArray();
        if (admittedMembers.Length == 0)
        {
            return ModelResult.Unrecognized;
        }

        if (admittedMembers.Length > 1)
        {
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.AmbiguousOperationImplementation(profileId, method.Id.Value)]);
        }

        var member = admittedMembers[0];
        if (!IsEligibleServiceOperation(shape, member))
        {
            return ModelResult.Unrecognized;
        }

        if (method.BodyFingerprint is null)
        {
            // The generated/source client boundary: a compiler-proven interface-member match with no
            // source body (for example a generated or metadata-only client proxy) never admits a root.
            return new ModelResult(false, diagnostics:
                [CoreWcfServiceModelDiagnostics.OperationImplementationUnavailable(profileId, method.Id.Value)]);
        }

        var serviceContractAttribute = index.Attributes.First(attribute =>
            attribute.Target == member.InterfaceTypeSymbol && IsServiceContractAttribute(attribute.AttributeType));
        var operationContractAttribute = index.Attributes.First(attribute =>
            attribute.Target == member.InterfaceMethodSymbol && IsOperationContractAttribute(attribute.AttributeType));

        var inputCertainty = symbol.Certainty;
        var effectiveCertainty = inputCertainty == CertaintyLevel.Exact ? CertaintyLevel.Exact : inputCertainty;
        var diagnostics = ImmutableArray<AnalysisDiagnostic>.Empty;
        if (inputCertainty != CertaintyLevel.Exact)
        {
            // Non-exact input certainty is never promoted.
            diagnostics = [CoreWcfServiceModelDiagnostics.DegradedInputCertainty(profileId, method.Id.Value)];
        }

        var serviceContractType = member.InterfaceType.MetadataName;
        var operationName = member.InterfaceMethodMetadataName;
        var operationKey = $"{serviceContractType}.{operationName}";
        var entryPointId = StableIdentity.CreateServiceOperationEntryPointId(
            new ServiceOperationEntryPointIdentityDescriptor(profileId, method.Id, operationKey));

        var underlyingEvidence = BuildUnderlyingEvidence(method, type, serviceContractAttribute, operationContractAttribute);
        var fact = new ServiceOperationEntryPointFact
        {
            Id = CreateBehaviorFactId(profileId, "service-operation-entry-point", new SymbolBehaviorFactAnchor(type.Project, symbol.Id), 0),
            EntryPointId = entryPointId,
            RootMethod = method.Id,
            ServiceContractType = serviceContractType,
            ImplementationType = type.MetadataName,
            OperationName = operationName,
            OperationKey = operationKey,
            Evidence = CreateModelEvidence($"service-operation:{entryPointId.Value}", underlyingEvidence, effectiveCertainty),
            Certainty = effectiveCertainty,
        };

        return new ModelResult(true, facts: [fact], diagnostics: diagnostics);
    }

    private static bool IsAdmittedOperation(ProgramIndexSnapshot index, FrameworkInterfaceMemberIdentity member)
        => index.Attributes.Any(attribute =>
                attribute.Target == member.InterfaceTypeSymbol && IsServiceContractAttribute(attribute.AttributeType))
            && index.Attributes.Any(attribute =>
                attribute.Target == member.InterfaceMethodSymbol && IsOperationContractAttribute(attribute.AttributeType));

    private static bool IsEligibleServiceOperation(FrameworkMethodShape shape, FrameworkInterfaceMemberIdentity member)
        => shape.IsOrdinary
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

    private static bool IsServiceContractAttribute(string attributeType)
        => attributeType is Identity.CoreWcfServiceContractAttribute or Identity.SystemServiceModelServiceContractAttribute;

    private static bool IsOperationContractAttribute(string attributeType)
        => attributeType is Identity.CoreWcfOperationContractAttribute or Identity.SystemServiceModelOperationContractAttribute;

    private static ImmutableArray<EvidenceRef> BuildUnderlyingEvidence(
        ProgramMethod method,
        ProgramType type,
        ProgramAttributeApplication serviceContractAttribute,
        ProgramAttributeApplication operationContractAttribute)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceRef>();
        builder.AddRange(method.Evidence);
        builder.AddRange(type.Evidence);
        builder.AddRange(serviceContractAttribute.Evidence);
        builder.AddRange(operationContractAttribute.Evidence);
        return builder
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
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
