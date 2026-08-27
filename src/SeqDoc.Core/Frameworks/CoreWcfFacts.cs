using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// Evidenced service-contract operation capability admitted from an exact compiler-proven
/// <c>[ServiceContract]</c>/<c>[OperationContract]</c> pair (CoreWCF or classic WCF, never mixed) with a
/// real compiler-proven source body. This proves capability only — that the concrete method could serve
/// the contract operation — never hosting, registration, dispatch, or execution. A capability fact alone
/// never admits an executable Scenario Graph root; <see cref="ServiceEndpointRegistrationFact"/> is the
/// separate, independently compiler-proven registration evidence required before a root and execution
/// wording are admitted. <see cref="ServiceContractType"/>, <see cref="ImplementationType"/>, and
/// <see cref="OperationName"/> are exact Program Index metadata names.
/// </summary>
public sealed record ServiceOperationCapabilityFact : BehaviorFact
{
    public required MethodId RootMethod { get; init; }

    public required string ServiceContractType { get; init; }

    public required SymbolId ServiceContractTypeSymbol { get; init; }

    public required string ImplementationType { get; init; }

    public required SymbolId ImplementationTypeSymbol { get; init; }

    public required string OperationName { get; init; }

    public required SymbolId OperationSymbol { get; init; }

    public required string OperationKey { get; init; }
}

/// <summary>
/// Evidenced CoreWCF/WCF service-endpoint registration proven from an exact
/// <c>IServiceBuilder.AddServiceEndpoint&lt;TService, TContract&gt;(Binding, string)</c> invocation. This
/// is both the endpoint-metadata compiler fact issue #5 requires and the registration/dispatch evidence
/// issue #7 requires before a capability may be promoted to an executable root: joined against a
/// <see cref="ServiceOperationCapabilityFact"/> by exact (<see cref="ImplementationType"/>,
/// <see cref="ServiceContractType"/>) match.
/// </summary>
public sealed record ServiceEndpointRegistrationFact : BehaviorFact
{
    public required string ImplementationType { get; init; }

    public required SymbolId ImplementationTypeSymbol { get; init; }

    public required string ServiceContractType { get; init; }

    public required SymbolId ServiceContractTypeSymbol { get; init; }

    public required string BindingType { get; init; }

    public required string? Address { get; init; }
}

/// <summary>
/// Evidenced <c>[FaultContract(typeof(TFault))]</c> metadata on an admitted service contract operation.
/// This is a compiler fact only; it carries no Scenario/Diagram presentation.
/// </summary>
public sealed record ServiceFaultContractFact : BehaviorFact
{
    public required string ServiceContractType { get; init; }

    public required string OperationName { get; init; }

    public required SymbolId OperationSymbol { get; init; }

    public required string FaultType { get; init; }

    public required FrameworkTypeIdentity FaultTypeIdentity { get; init; }
}

/// <summary>Classifies a service-client boundary by whether the compiler proved a source or generated body.</summary>
public enum ServiceClientKind
{
    Unknown,
    SourceClient,
    GeneratedClient,
}

/// <summary>
/// Evidenced service-client boundary: a concrete type whose exact base type is
/// <c>System.ServiceModel.ClientBase&lt;TContract&gt;</c> for an admitted service contract.
/// <see cref="ClientKind"/> is <see cref="ServiceClientKind.GeneratedClient"/> when the type carries the
/// exact <c>System.CodeDom.Compiler.GeneratedCodeAttribute</c> marker real code-generation tools (for
/// example dotnet-svcutil) apply, otherwise <see cref="ServiceClientKind.SourceClient"/>. This is a
/// compiler fact only; it carries no Scenario/Diagram presentation.
/// </summary>
public sealed record ServiceClientBoundaryFact : BehaviorFact
{
    public required string ServiceContractType { get; init; }

    public required SymbolId ServiceContractTypeSymbol { get; init; }

    public required string ClientType { get; init; }

    public required SymbolId ClientTypeSymbol { get; init; }

    public required ServiceClientKind ClientKind { get; init; }
}

/// <summary>Classifies the compiler-proven syntactic disposition of one client invocation's result.</summary>
public enum ClientInvocationResultClaimKind
{
    /// <summary>The call is a discarded statement (<c>client.Op(...);</c>); no response claim.</summary>
    Discarded,

    /// <summary>The call result is assigned to a local (<see cref="ServiceClientInvocationFact.ResultBindingName"/>).</summary>
    ResultAssigned,

    /// <summary>The call result is directly returned (<c>return client.Op(...);</c>).</summary>
    ResultReturned,

    /// <summary>
    /// Anything else the compiler proves about the call site's own syntax (chained member access,
    /// passed as an argument, stored to a field, discarded via <c>_ = ...</c>): the call was made and
    /// its declared result type is known, but no assignment/return claim is proven.
    /// </summary>
    Unclaimed,
}

/// <summary>
/// Evidenced invocation of an admitted client operation through an admitted service-client boundary:
/// an <c>IInvocationOperation</c> whose <c>TargetMethod.OriginalDefinition</c> resolves, by exact
/// compiler identity, to a contract operation already admitted (via the same
/// <see cref="FrameworkInterfaceMemberIdentity"/> machinery that proves
/// <see cref="ServiceOperationCapabilityFact"/> elsewhere) on a receiver whose exact static type
/// derives <c>System.ServiceModel.ClientBase&lt;TContract&gt;</c> constructed with that same admitted
/// contract. This fact proves the invocation's own identity and syntactic result disposition only; it
/// never claims a network call, a runtime response, or a runtime fault. Whether the receiver's client
/// type actually carries an admitted <see cref="ServiceClientBoundaryFact"/> with
/// <see cref="ServiceClientKind.SourceClient"/> or <see cref="ServiceClientKind.GeneratedClient"/> is
/// proven separately and joined later (mirroring how <see cref="ServiceOperationCapabilityFact"/> and
/// <see cref="ServiceEndpointRegistrationFact"/> are proven independently and joined by
/// <c>ScenarioGraphBuilder</c>): a metadata-only/unclassified client boundary, or no boundary at all,
/// means this fact simply never reaches the join and admits no outbound message.
/// </summary>
public sealed record ServiceClientInvocationFact : BehaviorFact
{
    public required MethodId CallerMethod { get; init; }

    public required OperationId InvocationOperation { get; init; }

    public required string ServiceContractType { get; init; }

    public required SymbolId ServiceContractTypeSymbol { get; init; }

    public required string ClientType { get; init; }

    public required SymbolId ClientTypeSymbol { get; init; }

    public required string OperationName { get; init; }

    public required SymbolId OperationSymbol { get; init; }

    public required string OperationKey { get; init; }

    public required ClientInvocationResultClaimKind ResultClaim { get; init; }

    public required bool IsAwaited { get; init; }

    /// <summary>The bound local/parameter name when <see cref="ResultClaim"/> is <see cref="ClientInvocationResultClaimKind.ResultAssigned"/>; null otherwise.</summary>
    public required string? ResultBindingName { get; init; }

    /// <summary>The compiler-declared return type of the invoked operation (for example <c>System.Double</c>), presented only when no stronger claim is proven.</summary>
    public required string DeclaredResultType { get; init; }
}

/// <summary>
/// The admitted, registration-proven service-operation entry point. Unlike the fact types above, this is
/// never emitted directly by a framework model — it exists only as the internal shape
/// <see cref="Analysis.Scenarios.ScenarioGraphBuilder" /> (referenced by namespace in documentation only;
/// this type has no assembly dependency on it) synthesizes after successfully joining one
/// <see cref="ServiceOperationCapabilityFact"/> with a matching <see cref="ServiceEndpointRegistrationFact"/>,
/// combining their evidence and taking the weaker of their two certainties. Its presence is exactly the
/// "exact supported host-registration/dispatch chain" proof required before an executable root or
/// execution wording may be produced; a capability with no matching registration never produces one.
/// </summary>
public sealed record ServiceOperationEntryPointFact : BehaviorFact
{
    public required EntryPointId EntryPointId { get; init; }

    public required MethodId RootMethod { get; init; }

    public required string ServiceContractType { get; init; }

    public required SymbolId ServiceContractTypeSymbol { get; init; }

    public required string ImplementationType { get; init; }

    public required SymbolId ImplementationTypeSymbol { get; init; }

    public required string OperationName { get; init; }

    public required string OperationKey { get; init; }
}
