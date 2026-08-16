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
    string? AssemblyVersion = null);

/// <summary>
/// One compiler-proven constant argument of an invoked method, ordered by declaration position. The
/// Roslyn adapter (deferred to C-5) fills these from constant compiler values; models never infer
/// constants from source text. <see cref="FullyQualifiedType"/> uses the same display form as
/// parameter types, so the controller model can require an exact <c>System.Int32</c>/<c>int</c>
/// argument before treating a value as an exact status.
/// </summary>
public sealed record CompilerProvenArgument
{
    public CompilerProvenArgument(int ordinal, string fullyQualifiedType, string value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullyQualifiedType);
        ArgumentNullException.ThrowIfNull(value);
        Ordinal = ordinal;
        FullyQualifiedType = fullyQualifiedType;
        Value = value;
    }

    public int Ordinal { get; }

    public string FullyQualifiedType { get; }

    public string Value { get; }
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
    FrameworkDispatchShapeDescriptor? DispatchShape = null);

/// <summary>
/// Exact, Roslyn-neutral identity of one named type. The controlled eligibility projector fills this
/// from compiler symbols; models never derive assembly, version, or metadata identity from names.
/// </summary>
public sealed record FrameworkTypeIdentity(
    string AssemblyIdentity,
    string AssemblyVersion,
    string MetadataName);

/// <summary>
/// Compiler-proven shape of one named type: kind, accessibility, abstract/static flags, generic
/// arity, and the exact base-type chain. The projector supplies these facts only; MVC controller
/// eligibility rules live in the modular framework model.
/// </summary>
public sealed record FrameworkTypeShape(
    FrameworkTypeIdentity Identity,
    bool IsClass,
    bool IsPublicOrNestedPublic,
    bool IsAbstract,
    bool IsStatic,
    int GenericArity,
    ImmutableArray<FrameworkTypeIdentity> BaseTypeChain);

/// <summary>
/// Compiler-proven shape of one method plus its declaring type, bound to the exact indexed symbols.
/// The controlled projector derives both symbol IDs with the same Program Index identity helpers, so
/// the model can require <see cref="MethodSymbol"/> to equal the indexed method symbol and
/// <see cref="DeclaringTypeSymbol"/> to equal the indexed containing type before eligibility can
/// support a root. Carried as the optional additive <see cref="SymbolDescriptor.MethodShape"/>;
/// missing, mismatched, or incomplete shape input makes the model fail closed with an eligibility
/// diagnostic and no root.
/// </summary>
public sealed record FrameworkMethodShape(
    SymbolId MethodSymbol,
    SymbolId DeclaringTypeSymbol,
    bool IsOrdinary,
    bool IsPublic,
    bool IsStatic,
    bool IsAbstract,
    int GenericArity,
    FrameworkTypeShape DeclaringType);

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
