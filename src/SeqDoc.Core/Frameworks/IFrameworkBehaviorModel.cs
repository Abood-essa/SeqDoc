using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// Exact, Roslyn-neutral identity of one invoked method. The Roslyn adapter fills this from compiler
/// symbols so models can match direct framework helpers by assembly, assembly version, containing
/// metadata type, metadata method name, arity, parameter types, and return type instead of raw
/// method-name strings. Parameter types use fully qualified display strings, optionally carrying a
/// trailing '?' nullable annotation; the controller model canonicalizes primitive display names
/// before matching. <see cref="AssemblyVersion"/> is additive and defaulted to null so existing
/// callers compile unchanged; direct-outcome recognition requires the exact supported value.
/// </summary>
public sealed record FrameworkMethodIdentity(
    string AssemblyIdentity,
    string ContainingMetadataType,
    string MethodMetadataName,
    int GenericArity,
    ImmutableArray<ParameterIdentityDescriptor> Parameters,
    string? ReturnType = null,
    string? AssemblyVersion = null,
    string? AssemblyPublicKeyToken = null);

/// <summary>
/// One compiler-proven constant argument of an invoked method, ordered by declaration position. The
/// Roslyn adapter (deferred to C-5) fills these from constant compiler values; models never infer
/// constants from source text. <see cref="FullyQualifiedType"/> uses the same display form as
/// parameter types, so the controller model can require an exact <c>System.Int32</c>/<c>int</c>
/// argument before treating a value as an exact status.
/// </summary>
public sealed record CompilerProvenArgument
{
    public CompilerProvenArgument(int ordinal, string fullyQualifiedType, string? value, bool isNull = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullyQualifiedType);
        if (!isNull)
        {
            ArgumentNullException.ThrowIfNull(value);
        }
        Ordinal = ordinal;
        FullyQualifiedType = fullyQualifiedType;
        Value = value;
        IsNull = isNull;
    }

    public int Ordinal { get; }

    public string FullyQualifiedType { get; }

    public string? Value { get; }

    public bool IsNull { get; }
}

/// <summary>
/// Compiler-proven shape of one predicate expression supplied to an invoked method. The Roslyn
/// adapter fills this from the exact argument operations; models never infer predicate meaning from
/// source text. <see cref="EqualityComparison"/> admits only an equality-shaped body whose comparison
/// operation anchor is present so downstream joins can link it to comparison semantic facts.
/// </summary>
public enum PredicateShapeKind
{
    None,
    EqualityComparison,
    NotEqualityComparison,
    Unknown,
}

/// <summary>
/// One compiler-proven predicate anchor. <see cref="ComparisonOperation"/> names the exact comparison
/// operation inside the predicate that the Roslyn traversal assigned to the same operation identity
/// used by behavior extraction, so semantic companion facts can join to it.
/// </summary>
public sealed record PredicateShapeDescriptor(
    PredicateShapeKind Kind,
    OperationId? ComparisonOperation);

/// <summary>Compiler-bound callback target attached to an outer registration invocation.</summary>
public sealed record CallbackTargetDescriptor
{
    public CallbackTargetDescriptor(MethodId? targetMethod, OperationId? targetBodyOperation)
        : this(
            targetMethod is null ? CallbackTargetKind.AnonymousFunction : CallbackTargetKind.MethodGroup,
            targetMethod,
            targetBodyOperation,
            null)
    {
    }

    public CallbackTargetDescriptor(CallbackTargetKind kind, MethodId? targetMethod, OperationId? targetBodyOperation, CallbackBoundaryId? callbackBoundaryId)
    {
        Kind = kind;
        TargetMethod = targetMethod;
        TargetBodyOperation = targetBodyOperation;
        CallbackBoundaryId = callbackBoundaryId;
    }

    public CallbackTargetKind Kind { get; }
    public MethodId? TargetMethod { get; }
    public OperationId? TargetBodyOperation { get; }
    public CallbackBoundaryId? CallbackBoundaryId { get; }
}

/// <summary>One exact MapGroup step in a route receiver chain.</summary>
public sealed record FrameworkRouteGroupStepDescriptor(
    string Prefix,
    FrameworkMethodIdentity TargetIdentity);

/// <summary>Route-group steps proven from a compiler-bound receiver chain.</summary>
public sealed record FrameworkRouteGroupDescriptor(ImmutableArray<FrameworkRouteGroupStepDescriptor> Steps)
{
    /// <summary>Compatibility view of the ordered literal prefixes.</summary>
    public ImmutableArray<string> Prefixes => Steps.Select(step => step.Prefix).ToImmutableArray();
}

/// <summary>One compiler-proven source handler candidate for a framework dispatch.</summary>
public sealed record FrameworkDispatchCandidateDescriptor(
    MethodId Method,
    string DisplayName,
    bool BodyAvailable,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty,
    bool IsOpenGeneric = false);

/// <summary>Compiler-proven request/response dispatch shape attached to an invocation.</summary>
public sealed record FrameworkDispatchShapeDescriptor(
    string RequestType,
    string ResponseType,
    string RequestContractType,
    bool IsClosedConstructed,
    bool TokenSupplied,
    ImmutableArray<FrameworkDispatchCandidateDescriptor> Candidates);

/// <summary>
/// Compiler-proven CoreWCF/WCF service-endpoint registration shape attached to an
/// <c>IServiceBuilder.AddServiceEndpoint&lt;TService, TContract&gt;(Binding, string)</c> invocation.
/// <see cref="ServiceType"/> and <see cref="ContractType"/> are the exact original identities of the two
/// constructed generic type arguments (assembly, version, metadata name); <see cref="ServiceTypeSymbol"/>
/// and <see cref="ContractTypeSymbol"/> are the exact Program Index symbol identities when the argument is
/// a source-owned type, so a Scenario join can compare exact symbols instead of metadata-name strings.
/// <see cref="BindingType"/> is the exact identity of the compiler-proven type of the binding argument;
/// <see cref="Address"/> is the compiler-proven constant address string, or null when the address argument
/// is not a compile-time constant. <see cref="HostChainProven"/> is true only when this exact invocation
/// was proven reachable through the complete compiler-proven active host chain (generic-host construction
/// and execution, <c>UseStartup&lt;TStartup&gt;</c> selection, the selected startup's own
/// <c>Configure(IApplicationBuilder)</c> callback, its <c>UseServiceModel</c> callback body, and a matching
/// <c>AddService&lt;TService&gt;</c> receiver) rather than merely found anywhere in source;
/// <see cref="HostChainEvidence"/> carries the evidence for every link of that proof. A false
/// <see cref="HostChainProven"/> means this invocation exists in source with the exact registration shape
/// but is not proven reachable from an active host, so it must never promote a capability to an executable
/// root.
/// </summary>
public sealed record FrameworkServiceEndpointShapeDescriptor(
    FrameworkTypeIdentity ServiceType,
    SymbolId? ServiceTypeSymbol,
    FrameworkTypeIdentity ContractType,
    SymbolId? ContractTypeSymbol,
    FrameworkTypeIdentity BindingType,
    string? Address,
    bool HostChainProven,
    ImmutableArray<EvidenceRef> HostChainEvidence = default);

/// <summary>
/// Compiler-proven shape of one invocation whose target method implements at least one interface
/// member, attached so a client-invocation-aware framework model can prove exact operation identity
/// without rescanning symbols. <see cref="TargetMethodShape"/> is the exact same
/// <see cref="FrameworkMethodShape"/> the eligibility projector would produce for the target method
/// itself (declaring type, base-type chain with constructed generic arguments, declaring-type
/// attributes, and every implemented interface member with its own exact attribute identities) — reused
/// rather than re-derived, so a model matches an admitted contract operation the same way it already
/// does for a service implementation. <see cref="ReceiverTypeSymbol"/> is the exact Program Index
/// symbol identity of the invocation's receiver expression's own static type;
/// <see cref="ReceiverIsConcreteType"/> is true only when that static type is a class (never an
/// interface), so a model can reject an ambiguous interface-typed receiver. <see cref="ResultClaim"/>,
/// <see cref="IsAwaited"/>, and <see cref="ResultBindingName"/> are the compiler-proven syntactic
/// disposition of the call site's own result (discarded, assigned, returned, or unclaimed, optionally
/// awaited) — never a network or runtime claim.
/// </summary>
public sealed record FrameworkClientInvocationShapeDescriptor(
    FrameworkMethodShape TargetMethodShape,
    SymbolId? ReceiverTypeSymbol,
    bool ReceiverIsConcreteType,
    ClientInvocationResultClaimKind ResultClaim,
    bool IsAwaited,
    string? ResultBindingName,
    string DeclaredResultType);

/// <summary>
/// One compiler-proven step of an invocation receiver chain. The Roslyn adapter fills steps from the
/// exact nested invocation operations of an expression; models verify each step against exact
/// framework symbols. <see cref="NavigationMemberIdentity"/> carries the canonical identity of the
/// navigation member selected by an Include-style step.
/// </summary>
public sealed record FrameworkChainStepDescriptor(
    OperationId Operation,
    FrameworkMethodIdentity TargetIdentity,
    string? NavigationMemberIdentity);

/// <summary>
/// Compiler-proven shape of one query-style invocation receiver chain: the base member (for example
/// a DbSet) plus the ordered invocation steps applied to it. All values come from compiler symbols so
/// models never reconstruct chains from raw names or syntax strings. <see cref="EntityType"/> names
/// the entity element type of the base receiver when the compiler proved it.
/// </summary>
public sealed record FrameworkQueryChainDescriptor(
    string ReceiverType,
    string ContainingType,
    string MemberName,
    string EntityType,
    ImmutableArray<FrameworkChainStepDescriptor> Steps);

/// <summary>
/// Framework-neutral facade over one extracted operation for model analysis. The Roslyn adapter
/// constructs these; Core IR never references Roslyn. <see cref="TargetIdentity"/> and
/// <see cref="ConstantArguments"/> are additive fields used only by direct-outcome recognition and
/// default to values that leave existing callers unchanged. <see cref="QueryChain"/> and
/// <see cref="PredicateShape"/> are additive compiler-proven anchors for query-style framework
/// models; they default to values that leave existing callers unchanged.
/// <see cref="SuppliedParameterOrdinals"/> is the final additive field: the canonical, ascending,
/// distinct compiler declaration ordinals actually supplied at the invocation. It defaults to an
/// uninitialized array so existing callers compile unchanged; models that require exact supplied
/// arguments treat a default or mismatched array as unsupported rather than assuming any parameter.
/// </summary>
public sealed record OperationDescriptor(
    OperationId Id,
    MethodId Method,
    string Kind,
    DocumentId? Document,
    int SourceStart,
    int SourceLength,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty,
    FrameworkMethodIdentity? TargetIdentity = null,
    ImmutableArray<CompilerProvenArgument> ConstantArguments = default,
    FrameworkQueryChainDescriptor? QueryChain = null,
    PredicateShapeDescriptor? PredicateShape = null,
    ImmutableArray<int> SuppliedParameterOrdinals = default,
    CallbackTargetDescriptor? CallbackTarget = null,
    FrameworkRouteGroupDescriptor? RouteGroup = null,
    FrameworkDispatchShapeDescriptor? DispatchShape = null,
    FrameworkTypeIdentity? ConstructedType = null,
    SymbolId? ConstructedTypeSymbol = null,
    FrameworkServiceEndpointShapeDescriptor? ServiceEndpointShape = null,
    FrameworkClientInvocationShapeDescriptor? ClientInvocationShape = null);

/// <summary>
/// Exact, Roslyn-neutral identity of one named type. The controlled eligibility projector fills this
/// from compiler symbols; models never derive assembly, version, or metadata identity from names.
/// </summary>
public sealed record FrameworkTypeIdentity(
    string AssemblyIdentity,
    string AssemblyVersion,
    string MetadataName,
    string? AssemblyPublicKeyToken = null);

/// <summary>
/// One compiler-proven base-type-chain entry: the exact original identity plus, for a constructed
/// generic base type (for example <c>ClientBase&lt;ICalculatorService&gt;</c>), the exact resolved
/// identity of every constructed type argument in declaration order. This is what lets a model prove
/// the *constructed* generic argument matches a specific admitted contract, rather than merely proving
/// the open generic definition appears somewhere in the base-type chain.
/// </summary>
public sealed record FrameworkBaseTypeIdentity(
    FrameworkTypeIdentity Identity,
    ImmutableArray<FrameworkTypeIdentity> TypeArguments);

/// <summary>
/// Compiler-proven shape of one named type: kind, accessibility, abstract/static flags, generic
/// arity, and the exact base-type chain. The projector supplies these facts only; MVC controller
/// eligibility rules live in the modular framework model. <see cref="BaseTypeChainWithArguments"/> is an
/// additive companion to <see cref="BaseTypeChain"/> that also carries each base type's constructed
/// generic type arguments; it defaults to an uninitialized array so existing callers compile unchanged.
/// </summary>
public sealed record FrameworkTypeShape(
    FrameworkTypeIdentity Identity,
    bool IsClass,
    bool IsPublicOrNestedPublic,
    bool IsAbstract,
    bool IsStatic,
    int GenericArity,
    ImmutableArray<FrameworkTypeIdentity> BaseTypeChain,
    ImmutableArray<FrameworkTypeIdentity> Interfaces = default,
    ImmutableArray<FrameworkBaseTypeIdentity> BaseTypeChainWithArguments = default);

/// <summary>
/// One compiler-proven attribute application resolved to its exact original attribute class identity
/// (assembly, assembly version, metadata name) rather than a display-name string, so a model can reject
/// a same-qualified-name attribute defined in a foreign assembly. <see cref="TypeArguments"/> carries
/// the exact resolved type identity of every <c>typeof(...)</c> constructor argument, in declaration
/// order, for attributes whose meaning depends on a type argument (for example
/// <c>[FaultContract(typeof(X))]</c>); it is empty when the attribute has no such argument.
/// <see cref="Evidence"/> is the exact source evidence for this specific attribute application, so a
/// model that already resolved the admitted exact identity never has to recover evidence later through a
/// separate target-symbol-plus-metadata-name-string lookup (a lookup a foreign same-qualified-name
/// attribute application on the same target could otherwise contaminate). It is an additive field that
/// defaults to an uninitialized array so existing callers compile unchanged.
/// </summary>
public sealed record FrameworkAttributeApplicationIdentity(
    FrameworkTypeIdentity AttributeType,
    ImmutableArray<FrameworkTypeIdentity> TypeArguments,
    ImmutableArray<EvidenceRef> Evidence = default);

/// <summary>
/// One compiler-proven interface member that a method implements, implicitly or explicitly. The
/// eligibility projector fills this from <c>INamedTypeSymbol.FindImplementationForInterfaceMember</c>
/// (implicit implementation) and <c>IMethodSymbol.ExplicitInterfaceImplementations</c> (explicit
/// implementation); models never derive interface implementation from names or signatures written as
/// strings. <see cref="InterfaceTypeSymbol"/> and <see cref="InterfaceMethodSymbol"/> are the same
/// Program Index symbol identities used elsewhere. <see cref="InterfaceTypeAttributes"/> and
/// <see cref="InterfaceMethodAttributes"/> are the exact resolved attribute-class identities applied to
/// the interface type and interface method (for example <c>[ServiceContract]</c>/<c>[OperationContract]</c>),
/// so a model matches by original assembly/version/metadata-name identity instead of a display-name
/// string and never accepts a same-qualified-name attribute from a foreign assembly. <see cref="InterfaceType"/>,
/// <see cref="InterfaceMethodMetadataName"/>, <see cref="GenericArity"/>, <see cref="Parameters"/>, and
/// <see cref="ReturnType"/> are the exact interface method signature, so a model can additionally guard
/// against a same-named lookalike overload.
/// </summary>
public sealed record FrameworkInterfaceMemberIdentity(
    SymbolId InterfaceTypeSymbol,
    SymbolId InterfaceMethodSymbol,
    FrameworkTypeIdentity InterfaceType,
    string InterfaceMethodMetadataName,
    int GenericArity,
    ImmutableArray<ParameterIdentityDescriptor> Parameters,
    string ReturnType,
    bool IsExplicitImplementation,
    ImmutableArray<FrameworkAttributeApplicationIdentity> InterfaceTypeAttributes = default,
    ImmutableArray<FrameworkAttributeApplicationIdentity> InterfaceMethodAttributes = default);

/// <summary>
/// Compiler-proven shape of one method plus its declaring type, bound to the exact indexed symbols.
/// The controlled projector derives both symbol IDs with the same Program Index identity helpers, so
/// the model can require <see cref="MethodSymbol"/> to equal the indexed method symbol and
/// <see cref="DeclaringTypeSymbol"/> to equal the indexed containing type before eligibility can
/// support a root. Carried as the optional additive <see cref="SymbolDescriptor.MethodShape"/>;
/// missing, mismatched, or incomplete shape input makes the model fail closed with an eligibility
/// diagnostic and no root. <see cref="ImplementedInterfaceMembers"/> is the additive exact
/// interface-member-implementation mapping used by interface-contract-driven models (for example a
/// service contract's operations); it defaults to an uninitialized array so existing callers compile
/// unchanged, and a default or empty array means no interface member implementation was proven.
/// </summary>
public sealed record FrameworkMethodShape(
    SymbolId MethodSymbol,
    SymbolId DeclaringTypeSymbol,
    bool IsOrdinary,
    bool IsPublic,
    bool IsStatic,
    bool IsAbstract,
    int GenericArity,
    FrameworkTypeShape DeclaringType,
    ImmutableArray<FrameworkInterfaceMemberIdentity> ImplementedInterfaceMembers = default,
    ImmutableArray<FrameworkAttributeApplicationIdentity> DeclaringTypeAttributes = default);

/// <summary>
/// Framework-neutral facade over one symbol for model analysis. The Roslyn adapter constructs these;
/// Core IR never references Roslyn. <see cref="MethodShape"/> is an additive controlled compiler
/// fact projected by the eligibility projector; it never decides MVC eligibility itself.
/// </summary>
public sealed record SymbolDescriptor(
    SymbolId Id,
    string Kind,
    string MetadataName,
    DocumentId? Document,
    int SourceStart,
    int SourceLength,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty,
    FrameworkMethodShape? MethodShape = null);

/// <summary>
/// Deterministic, versioned framework knowledge unit. Models match exact symbols and semantic
/// patterns; they never match raw method-name strings.
/// </summary>
public interface IFrameworkBehaviorModel
{
    FrameworkModelDescriptor Descriptor { get; }

    bool IsApplicable(FrameworkDetectionContext context);

    ValueTask<ModelResult> AnalyzeOperationAsync(
        OperationDescriptor operation,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken);

    ValueTask<ModelResult> AnalyzeSymbolAsync(
        SymbolDescriptor symbol,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken);
}
