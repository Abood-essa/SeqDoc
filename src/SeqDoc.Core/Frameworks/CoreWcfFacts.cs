using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// Evidenced service-contract operation entry point admitted from an exact compiler-proven
/// <c>[ServiceContract]</c>/<c>[OperationContract]</c> pair (CoreWCF or classic WCF). The root is the
/// exact Program Index method of the concrete service implementation; <see cref="ServiceContractType"/>
/// and <see cref="OperationName"/> are the exact interface and operation identity the implementation
/// proves it admits, and <see cref="OperationKey"/> identifies the operation the same way
/// <see cref="HttpEntryPointFact.OperationKey"/> identifies an HTTP entry point. Faults, generated
/// clients, and outbound boundaries are not represented by this fact; they are separate accepted
/// contract companions delivered by later work.
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
