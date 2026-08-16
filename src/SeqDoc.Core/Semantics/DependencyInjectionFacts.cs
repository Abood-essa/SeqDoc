using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

/// <summary>
/// Closed set of Microsoft dependency injection lifetimes admitted by the translation-alpha DI
/// projection. Only the exact generic two-type-argument extension methods on
/// <c>ServiceCollectionServiceExtensions</c> project into this vocabulary; factory, instance,
/// non-generic, collection, keyed, TryAdd, and lookalike helper forms produce no invented fact.
/// </summary>
public enum DependencyInjectionLifetime
{
    Scoped,
    Singleton,
    Transient,
}

/// <summary>
/// One exact generic <c>AddScoped&lt;TService, TImplementation&gt;</c>, <c>AddSingleton</c>, or
/// <c>AddTransient</c> registration on Microsoft <c>IServiceCollection</c>. The fact is
/// revision-local and anchored to the exact invocation operation that grounds it. The service and
/// implementation types are canonical fully qualified display identities produced from compiler
/// symbols, so lookalike helpers and raw name matching can never admit a registration. One
/// registration fact exists per source operation.
/// </summary>
public sealed record DependencyInjectionRegistrationFact
{
    public DependencyInjectionRegistrationFact(
        SemanticFactId id,
        MethodId sourceMethod,
        OperationId operation,
        string serviceType,
        string implementationType,
        DependencyInjectionLifetime lifetime,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        DependencyInjectionFactContracts.Validate(id, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMethod.Value, nameof(sourceMethod));
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType, nameof(serviceType));
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationType, nameof(implementationType));
        if (!Enum.IsDefined(lifetime))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Undefined dependency-injection lifetime.");
        }

        Id = id;
        SourceMethod = sourceMethod;
        Operation = operation;
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId SourceMethod { get; }

    public OperationId Operation { get; }

    public string ServiceType { get; }

    public string ImplementationType { get; }

    public DependencyInjectionLifetime Lifetime { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One exact constructor-parameter-to-registration binding. The constructor parameter is identified
/// by its compiler parameter ordinal and canonical parameter type; the bound registration is
/// identified by its registration fact id. Every exact matching admitted registration produces its
/// own binding fact, so when several registrations match one parameter every match remains distinct
/// and visible and no single implementation is ever selected. Collection-typed parameters and
/// collection registrations never produce a binding because collection injection is unsupported.
/// </summary>
public sealed record DependencyInjectionBindingFact
{
    public DependencyInjectionBindingFact(
        SemanticFactId id,
        MethodId constructorMethod,
        int parameterOrdinal,
        string parameterName,
        string parameterType,
        SemanticFactId registrationId,
        string serviceType,
        string implementationType,
        DependencyInjectionLifetime lifetime,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        DependencyInjectionFactContracts.Validate(id, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(constructorMethod.Value, nameof(constructorMethod));
        ArgumentOutOfRangeException.ThrowIfNegative(parameterOrdinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName, nameof(parameterName));
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterType, nameof(parameterType));
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId.Value, nameof(registrationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType, nameof(serviceType));
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationType, nameof(implementationType));
        if (!Enum.IsDefined(lifetime))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Undefined dependency-injection lifetime.");
        }

        Id = id;
        ConstructorMethod = constructorMethod;
        ParameterOrdinal = parameterOrdinal;
        ParameterName = parameterName;
        ParameterType = parameterType;
        RegistrationId = registrationId;
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId ConstructorMethod { get; }

    public int ParameterOrdinal { get; }

    public string ParameterName { get; }

    public string ParameterType { get; }

    public SemanticFactId RegistrationId { get; }

    public string ServiceType { get; }

    public string ImplementationType { get; }

    public DependencyInjectionLifetime Lifetime { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of dependency-injection companion facts for one compilation
/// profile. The set records schema and producer versions, the compilation profile, the Program Index
/// fingerprint, canonically ordered registrations and bindings, diagnostics, and a deterministic
/// debug representation free of absolute paths. Persistence and cache reconstruction are explicitly
/// out of scope for this contract; only the accepted <c>AnalysisProfileSnapshot</c> is persisted.
/// </summary>
public sealed record DependencyInjectionFactSet(
    int SchemaVersion,
    string ProducerVersion,
    CompilationProfile Profile,
    string ProgramIndexFingerprint,
    ImmutableArray<DependencyInjectionRegistrationFact> Registrations,
    ImmutableArray<DependencyInjectionBindingFact> Bindings,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string DebugProjection);

internal static class DependencyInjectionFactContracts
{
    public static void Validate(
        SemanticFactId id,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A dependency-injection fact requires non-empty evidence.", nameof(evidence));
        }

        if (evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Artifact)))
        {
            throw new ArgumentException("Dependency-injection evidence must reference a non-empty artifact.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A dependency-injection fact requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Fact certainty must never exceed its strongest evidence.", nameof(certainty));
        }
    }
}
