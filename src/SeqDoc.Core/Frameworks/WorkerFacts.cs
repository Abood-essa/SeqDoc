using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Frameworks;

public enum HostedWorkerLifecycleStep
{
    Start,
    Execute,
    Stop,
}

public enum SchedulerKind
{
    Timer,
}

/// <summary>
/// Exact compiler-backed identity of a hosted worker and the lifecycle methods that are available in
/// the analyzed source. Inherited framework methods may be absent because no source body is available;
/// absence is preserved rather than replaced with inferred behavior.
/// </summary>
public sealed record HostedWorkerLifecycleFact : BehaviorFact
{
    public required EntryPointId EntryPointId { get; init; }
    public required MethodId RootMethod { get; init; }
    public required SymbolId HostedType { get; init; }
    public required string HostedTypeName { get; init; }
    public MethodId? StartMethod { get; init; }
    public MethodId? ExecuteMethod { get; init; }
    public MethodId? StopMethod { get; init; }
    public required bool IsBackgroundService { get; init; }
    public string? CancellationParameterName { get; init; }
}

/// <summary>Exact compiler-backed AddHostedService registration that admits one worker type.</summary>
public sealed record HostedWorkerRegistrationFact : BehaviorFact
{
    public required SymbolId HostedType { get; init; }
    public required MethodId RegistrationMethod { get; init; }
    public required OperationId RegistrationOperation { get; init; }
}

/// <summary>
/// Exact source callback registration for a supported timer constructor. The callback target is
/// retained as a method identity; runtime timing and callback order remain outside static evidence.
/// </summary>
public sealed record SchedulerJobFact : BehaviorFact
{
    public required SchedulerKind Scheduler { get; init; }
    public required MethodId RegistrationMethod { get; init; }
    public required OperationId RegistrationOperation { get; init; }
    public required MethodId JobMethod { get; init; }
    public required int SourceStart { get; init; }
    public required string CallbackTypeName { get; init; }
}
