using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.FrameworkModels.Tests.CoreWcf;

/// <summary>
/// Builds deterministic Program Index snapshots and model descriptors for CoreWCF/WCF service
/// contract model tests. Every symbol and attribute uses the exact fully qualified identities the
/// Roslyn index and eligibility projector produce, so tests exercise the same semantic inventory
/// without raw name matching.
/// </summary>
internal static class CoreWcfTestIndexFactory
{
    public const string ProjectRelativePath = "tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj";
    public const string ContractMetadataName = "CoreWcfServices.ICalculatorService";
    public const string ImplementationMetadataName = "CoreWcfServices.CalculatorService";

    public const string CoreWcfAssembly = "CoreWCF.Primitives";
    public const string CoreWcfAssemblyVersion = "1.9.0.0";
    public const string SystemServiceModelAssembly = "System.ServiceModel.Primitives";
    public const string SystemServiceModelAssemblyVersion = "8.1.2.0";
    public const string CoreWcfServiceContractAttribute = "CoreWCF.ServiceContractAttribute";
    public const string CoreWcfOperationContractAttribute = "CoreWCF.OperationContractAttribute";
    public const string CoreWcfFaultContractAttribute = "CoreWCF.FaultContractAttribute";
    public const string SystemServiceModelServiceContractAttribute = "System.ServiceModel.ServiceContractAttribute";
    public const string SystemServiceModelOperationContractAttribute = "System.ServiceModel.OperationContractAttribute";
    public const string SystemServiceModelFaultContractAttribute = "System.ServiceModel.FaultContractAttribute";
    public const string ClientBaseMetadataName = "System.ServiceModel.ClientBase`1";
    public const string GeneratedCodeAttributeMetadataName = "System.CodeDom.Compiler.GeneratedCodeAttribute";

    // GeneratedCodeAttribute is implemented in System.Private.CoreLib at run time, but the compiler
    // resolves ContainingAssembly to the System.Runtime reference-assembly facade it is type-forwarded
    // through (confirmed by reflecting typeof(GeneratedCodeAttribute).Assembly.GetName() from a real
    // compilation and comparing against the Roslyn-projected identity), so the fixture must match that
    // compile-time assembly, not the runtime one, or it silently proves nothing about the real producer.
    public const string CoreLibAssembly = "System.Runtime";
    public const string CoreLibAssemblyVersion = "10.0.0.0";

    public static CompilationProfile Profile { get; } =
        CompilationProfile.Create(ProjectRelativePath, "Release", "net10.0");

    public static ProjectId ProjectId { get; } = new("project:v1:corewcf-services");

    public static DocumentId DocumentId { get; } = new("document:v1:calculator-service");

    public static SymbolId ContractSymbol { get; } = new("symbol:v1:CoreWcfServices.ICalculatorService");

    public static SymbolId ImplementationSymbol { get; } = new("symbol:v1:CoreWcfServices.CalculatorService");

    public static SymbolId InterfaceMethodSymbol(string name) => new($"symbol:v1:CoreWcfServices.ICalculatorService.{name}");

    public static SymbolId ImplementationMethodSymbol(string name) => new($"symbol:v1:CoreWcfServices.CalculatorService.{name}");

    public static MethodId ImplementationMethodId(string name) => new($"method:v1:CoreWcfServices.CalculatorService.{name}");

    public static EvidenceRef SourceEvidence(string symbol)
        => new(
            new EvidenceId($"evidence:v1:{symbol}"),
            EvidenceKind.Source,
            "Services/CalculatorService.cs",
            new SourceRange(DocumentId, new SourcePosition(10, 0), new SourcePosition(10, 30)),
            symbol,
            detail: null,
            CertaintyLevel.Exact);

    public static ProgramAttributeApplication Attribute(SymbolId target, string attributeType)
        => new(
            $"attribute:v1:{attributeType}|{target.Value}",
            target,
            attributeType,
            $"{attributeType}.ctor",
            [],
            [SourceEvidence(attributeType)]);

    public static ProgramProject Project()
        => new(ProjectId, "CoreWcfServices", ProjectRelativePath, Profile.Id, "net10.0", ProjectKind.Library, "project-build:v1:test", [], [SourceEvidence("project")]);

    public static ProgramType ContractType()
        => new(
            ContractSymbol,
            ProjectId,
            new SymbolId("symbol:v1:CoreWcfServices"),
            ContractMetadataName,
            ProgramTypeKind.Interface,
            BaseType: null,
            Interfaces: [],
            SignatureFingerprint: "type-signature:v1:contract",
            Evidence: [SourceEvidence(ContractMetadataName)]);

    public static ProgramType ImplementationType(string metadataName = ImplementationMetadataName)
        => new(
            new SymbolId($"symbol:v1:{metadataName}"),
            ProjectId,
            new SymbolId("symbol:v1:CoreWcfServices"),
            metadataName,
            ProgramTypeKind.Class,
            BaseType: null,
            Interfaces: [ContractSymbol],
            SignatureFingerprint: $"type-signature:v1:{metadataName}",
            Evidence: [SourceEvidence(metadataName)]);

    public static ProgramMethod InterfaceMethod(string name)
        => new(
            new MethodId($"method:v1:{ContractMetadataName}.{name}"),
            InterfaceMethodSymbol(name),
            ContractSymbol,
            name,
            $"{ContractMetadataName}.{name}(System.Double, System.Double)",
            [Param("n1"), Param("n2")],
            "System.Double",
            $"method-signature:v1:{name}",
            BodyFingerprint: null,
            [SourceEvidence($"{ContractMetadataName}.{name}")]);

    public static ProgramMethod ImplementationMethod(string name, bool withBody = true)
        => new(
            ImplementationMethodId(name),
            ImplementationMethodSymbol(name),
            ImplementationSymbol,
            name,
            $"{ImplementationMetadataName}.{name}(System.Double, System.Double)",
            [Param("n1"), Param("n2")],
            "System.Double",
            $"method-signature:v1:{name}",
            withBody ? $"method-body:v1:{name}" : null,
            [SourceEvidence($"{ImplementationMetadataName}.{name}")]);

    private static ParameterDescriptor Param(string name) => new(name, "System.Double", ParameterRefKind.None);

    public static ProgramIndexSnapshot ToIndex(
        ImmutableArray<ProgramType> types,
        ImmutableArray<ProgramMethod> methods,
        ImmutableArray<ProgramAttributeApplication> attributes)
        => new(
            SchemaVersion: 1,
            ProducerVersion: "test",
            Profile,
            [Project()],
            [
                new ProgramDocument(
                    DocumentId,
                    ProjectId,
                    "Services/CalculatorService.cs",
                    DocumentOrigin.Source,
                    "content:v1",
                    null,
                    [SourceEvidence("document")]),
            ],
            [],
            types,
            [],
            methods,
            attributes,
            [],
            [],
            [],
            [],
            "input-hash",
            "index-fingerprint");

    public static SymbolDescriptor MethodSymbolDescriptor(string name, FrameworkMethodShape? shape = null, CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(
            ImplementationMethodSymbol(name),
            "Method",
            name,
            DocumentId,
            100,
            24,
            [SourceEvidence($"{ImplementationMetadataName}.{name}")],
            certainty,
            shape ?? EligibleMethodShape(name));

    public static SymbolDescriptor MethodSymbolDescriptorWithoutShape(string name)
        => new(
            ImplementationMethodSymbol(name),
            "Method",
            name,
            DocumentId,
            100,
            24,
            [SourceEvidence($"{ImplementationMetadataName}.{name}")],
            CertaintyLevel.Exact,
            MethodShape: null);

    public static FrameworkTypeShape EligibleImplementationTypeShape(
        bool isAbstract = false, bool isStatic = false, int genericArity = 0, bool isPublic = true,
        string metadataName = ImplementationMetadataName, bool clientBaseDerived = false)
        => new(
            Identity: new FrameworkTypeIdentity("CoreWcfServices", "1.0.0", metadataName),
            IsClass: true,
            IsPublicOrNestedPublic: isPublic,
            IsAbstract: isAbstract,
            IsStatic: isStatic,
            GenericArity: genericArity,
            BaseTypeChain: clientBaseDerived
                ? [new FrameworkTypeIdentity(SystemServiceModelAssembly, SystemServiceModelAssemblyVersion, ClientBaseMetadataName), new FrameworkTypeIdentity("System.Private.CoreLib", "10.0.0.0", "System.Object")]
                : [new FrameworkTypeIdentity("System.Private.CoreLib", "10.0.0.0", "System.Object")]);

    /// <summary>Exact ServiceContract/OperationContract/FaultContract attribute identities for either admitted family.</summary>
    public static FrameworkAttributeApplicationIdentity ServiceContractAttribute(bool coreWcf = true)
        => coreWcf
            ? new FrameworkAttributeApplicationIdentity(new FrameworkTypeIdentity(CoreWcfAssembly, CoreWcfAssemblyVersion, CoreWcfServiceContractAttribute), [])
            : new FrameworkAttributeApplicationIdentity(new FrameworkTypeIdentity(SystemServiceModelAssembly, SystemServiceModelAssemblyVersion, SystemServiceModelServiceContractAttribute), []);

    public static FrameworkAttributeApplicationIdentity OperationContractAttribute(bool coreWcf = true)
        => coreWcf
            ? new FrameworkAttributeApplicationIdentity(new FrameworkTypeIdentity(CoreWcfAssembly, CoreWcfAssemblyVersion, CoreWcfOperationContractAttribute), [])
            : new FrameworkAttributeApplicationIdentity(new FrameworkTypeIdentity(SystemServiceModelAssembly, SystemServiceModelAssemblyVersion, SystemServiceModelOperationContractAttribute), []);

    public static FrameworkAttributeApplicationIdentity FaultContractAttribute(FrameworkTypeIdentity faultType, bool coreWcf = true)
        => coreWcf
            ? new FrameworkAttributeApplicationIdentity(new FrameworkTypeIdentity(CoreWcfAssembly, CoreWcfAssemblyVersion, CoreWcfFaultContractAttribute), [faultType])
            : new FrameworkAttributeApplicationIdentity(new FrameworkTypeIdentity(SystemServiceModelAssembly, SystemServiceModelAssemblyVersion, SystemServiceModelFaultContractAttribute), [faultType]);

    public static FrameworkAttributeApplicationIdentity ForeignAssemblyServiceContractAttribute()
        => new(new FrameworkTypeIdentity("Foreign.Assembly", "1.0.0.0", CoreWcfServiceContractAttribute), []);

    public static FrameworkAttributeApplicationIdentity GeneratedCodeAttribute()
        => new(new FrameworkTypeIdentity(CoreLibAssembly, CoreLibAssemblyVersion, GeneratedCodeAttributeMetadataName), []);

    public static FrameworkInterfaceMemberIdentity InterfaceMember(
        string name,
        bool isExplicit = false,
        string interfaceMetadataName = ContractMetadataName,
        ImmutableArray<FrameworkAttributeApplicationIdentity> typeAttributes = default,
        ImmutableArray<FrameworkAttributeApplicationIdentity> methodAttributes = default)
        => new(
            interfaceMetadataName == ContractMetadataName ? ContractSymbol : new SymbolId($"symbol:v1:{interfaceMetadataName}"),
            interfaceMetadataName == ContractMetadataName ? InterfaceMethodSymbol(name) : new SymbolId($"symbol:v1:{interfaceMetadataName}.{name}"),
            new FrameworkTypeIdentity("CoreWcfServices", "1.0.0", interfaceMetadataName),
            name,
            GenericArity: 0,
            Parameters: [new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Double"), new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Double")],
            ReturnType: "System.Double",
            IsExplicitImplementation: isExplicit,
            InterfaceTypeAttributes: typeAttributes.IsDefault ? [ServiceContractAttribute()] : typeAttributes,
            InterfaceMethodAttributes: methodAttributes.IsDefault ? [OperationContractAttribute()] : methodAttributes);

    public static FrameworkMethodShape EligibleMethodShape(
        string name,
        ImmutableArray<FrameworkInterfaceMemberIdentity> members = default,
        bool isPublic = true,
        bool isStatic = false,
        bool isAbstract = false,
        int genericArity = 0,
        FrameworkTypeShape? declaringType = null,
        ImmutableArray<FrameworkAttributeApplicationIdentity> declaringTypeAttributes = default,
        bool isOrdinary = true)
        => new(
            ImplementationMethodSymbol(name),
            ImplementationSymbol,
            IsOrdinary: isOrdinary,
            IsPublic: isPublic,
            IsStatic: isStatic,
            IsAbstract: isAbstract,
            GenericArity: genericArity,
            DeclaringType: declaringType ?? EligibleImplementationTypeShape(),
            ImplementedInterfaceMembers: members.IsDefault ? [InterfaceMember(name)] : members,
            DeclaringTypeAttributes: declaringTypeAttributes);

    /// <summary>Builds the exact <c>AddServiceEndpoint&lt;TService, TContract&gt;</c> operation shape.</summary>
    public static OperationDescriptor ServiceEndpointOperation(
        string operationSuffix,
        string serviceType = ImplementationMetadataName,
        string contractType = ContractMetadataName,
        string bindingType = "CoreWCF.BasicHttpBinding",
        string? address = "/CalculatorService/basicHttp",
        CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(
            new OperationId($"operation:v1:registration:{operationSuffix}"),
            new MethodId("method:v1:CoreWcfServices.Startup.Configure"),
            "Invocation",
            DocumentId,
            300,
            40,
            [SourceEvidence($"registration:{operationSuffix}")],
            certainty,
            ServiceEndpointShape: new FrameworkServiceEndpointShapeDescriptor(serviceType, contractType, bindingType, address));
}
