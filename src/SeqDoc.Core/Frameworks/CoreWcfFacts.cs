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

    public required string ImplementationType { get; init; }

    public required string OperationName { get; init; }

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

    public required string ServiceContractType { get; init; }

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

    public required string FaultType { get; init; }
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

    public required string ClientType { get; init; }

    public required ServiceClientKind ClientKind { get; init; }
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

    public required string ImplementationType { get; init; }

    public required string OperationName { get; init; }

    public required string OperationKey { get; init; }
}
