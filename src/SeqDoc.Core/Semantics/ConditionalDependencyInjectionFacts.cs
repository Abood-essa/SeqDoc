using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

/// <summary>
/// One top-level configuration-arm membership of an exact Microsoft dependency-injection
/// registration. The arm fact is projected only for an already admitted exact generic
/// <c>AddScoped/AddSingleton/AddTransient&lt;TService, TImplementation&gt;</c> registration in the
/// synthesized top-level method, directly enclosed by one exact compiler <c>if</c> statement whose
/// direct-local boolean condition is admitted by the accepted contract configuration facts. The fact records the
/// registration operation, the condition/read operations that anchor the arm to the accepted contract condition,
/// the canonical configuration key, the bound registration identity, the semantic true/false polarity
/// of the enclosing arm, evidence, and certainty. Companion arm facts are never projected inside an
/// extracted method because accepted Method Flow remains the sole generic local-control authority
/// there.
/// </summary>
public sealed record ConditionalDependencyInjectionRegistrationArmFact
{
    public ConditionalDependencyInjectionRegistrationArmFact(
        SemanticFactId id,
        MethodId programMethod,
        OperationId registrationOperation,
        OperationId conditionOperation,
        OperationId readOperation,
        string key,
        SemanticFactId registrationId,
        string serviceType,
        string implementationType,
        DependencyInjectionLifetime lifetime,
        bool IsTrueArm,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ConditionalDependencyInjectionFactContracts.Validate(id, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(programMethod.Value, nameof(programMethod));
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationOperation.Value, nameof(registrationOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(conditionOperation.Value, nameof(conditionOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(readOperation.Value, nameof(readOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        if (ConfigurationSemanticKeyPolicy.IsSensitive(key))
        {
            throw new ArgumentException("A conditional dependency-injection arm fact must never carry a sensitive key.", nameof(key));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId.Value, nameof(registrationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType, nameof(serviceType));
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationType, nameof(implementationType));
        if (!Enum.IsDefined(lifetime))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Undefined dependency-injection lifetime.");
        }

        Id = id;
        ProgramMethod = programMethod;
        RegistrationOperation = registrationOperation;
        ConditionOperation = conditionOperation;
        ReadOperation = readOperation;
        Key = key;
        RegistrationId = registrationId;
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
        this.IsTrueArm = IsTrueArm;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    /// <summary>Exact synthesized top-level method that owns the enclosing <c>if</c> statement.</summary>
    public MethodId ProgramMethod { get; }

    public OperationId RegistrationOperation { get; }

    /// <summary>Exact compiler condition operation of the enclosing top-level <c>if</c> statement.</summary>
    public OperationId ConditionOperation { get; }

    /// <summary>Exact compiler operation of the admitted boolean read that flows into the condition.</summary>
    public OperationId ReadOperation { get; }

    /// <summary>Canonical non-sensitive configuration key selected by the enclosing arm.</summary>
    public string Key { get; }

    public SemanticFactId RegistrationId { get; }

    public string ServiceType { get; }

    public string ImplementationType { get; }

    public DependencyInjectionLifetime Lifetime { get; }

    /// <summary>True when the registration sits in the arm entered when the read is true.</summary>
    public bool IsTrueArm { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One complete mutually exclusive alternative group: the same service type is registered by exactly
/// one admitted registration in the true arm and exactly one admitted registration in the opposite
/// false arm of the same exact compiler condition operation, both share one supported lifetime, and
/// no admitted unguarded or additional registration overlaps that service type. The group references
/// the exact registration identities of both arms and retains both implementation types, the shared
/// condition/read operations, the canonical key, evidence, and certainty. Independent <c>if</c>
/// statements, missing else arms, same-polarity registrations, overlapping or additional
/// registrations, unresolved conditions, and unsupported registration shapes never form a group.
/// </summary>
public sealed record ConditionalDependencyInjectionGroupFact
{
    public ConditionalDependencyInjectionGroupFact(
        SemanticFactId id,
        MethodId programMethod,
        OperationId conditionOperation,
        OperationId readOperation,
        string key,
        string serviceType,
        SemanticFactId trueRegistrationId,
        SemanticFactId falseRegistrationId,
        string trueImplementationType,
        string falseImplementationType,
        DependencyInjectionLifetime lifetime,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ConditionalDependencyInjectionFactContracts.Validate(id, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(programMethod.Value, nameof(programMethod));
        ArgumentException.ThrowIfNullOrWhiteSpace(conditionOperation.Value, nameof(conditionOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(readOperation.Value, nameof(readOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        if (ConfigurationSemanticKeyPolicy.IsSensitive(key))
        {
            throw new ArgumentException("A conditional dependency-injection group fact must never carry a sensitive key.", nameof(key));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType, nameof(serviceType));
        ArgumentException.ThrowIfNullOrWhiteSpace(trueRegistrationId.Value, nameof(trueRegistrationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(falseRegistrationId.Value, nameof(falseRegistrationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(trueImplementationType, nameof(trueImplementationType));
        ArgumentException.ThrowIfNullOrWhiteSpace(falseImplementationType, nameof(falseImplementationType));
        if (!Enum.IsDefined(lifetime))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Undefined dependency-injection lifetime.");
        }

        if (string.Equals(trueRegistrationId.Value, falseRegistrationId.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException("An alternative group requires two distinct registrations.", nameof(trueRegistrationId));
        }

        Id = id;
        ProgramMethod = programMethod;
        ConditionOperation = conditionOperation;
        ReadOperation = readOperation;
        Key = key;
        ServiceType = serviceType;
        TrueRegistrationId = trueRegistrationId;
        FalseRegistrationId = falseRegistrationId;
        TrueImplementationType = trueImplementationType;
        FalseImplementationType = falseImplementationType;
        Lifetime = lifetime;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId ProgramMethod { get; }

    public OperationId ConditionOperation { get; }

    public OperationId ReadOperation { get; }

    public string Key { get; }

    public string ServiceType { get; }

    public SemanticFactId TrueRegistrationId { get; }

    public SemanticFactId FalseRegistrationId { get; }

    public string TrueImplementationType { get; }

    public string FalseImplementationType { get; }

    public DependencyInjectionLifetime Lifetime { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of conditional dependency-injection companion facts for one
/// compilation profile: exact top-level configuration-arm membership facts and the complete mutually
/// exclusive alternative groups that survive the fail-closed grouping rules. The set records schema
/// and producer versions, the compilation profile, the Program Index fingerprint, canonically ordered
/// arms and groups, diagnostics, and a deterministic debug representation free of absolute paths and
/// raw values. Persistence and cache reconstruction are explicitly out of scope for this contract.
/// </summary>
public sealed class ConditionalDependencyInjectionFactSet
{
    public ConditionalDependencyInjectionFactSet(
        int SchemaVersion,
        string ProducerVersion,
        CompilationProfile Profile,
        string ProgramIndexFingerprint,
        ImmutableArray<ConditionalDependencyInjectionRegistrationArmFact> RegistrationArms,
        ImmutableArray<ConditionalDependencyInjectionGroupFact> Groups,
        ImmutableArray<AnalysisDiagnostic> Diagnostics,
        string DebugProjection)
    {
        if (SchemaVersion != 1)
        {
            throw new ArgumentException("The conditional dependency-injection fact set schema version must be exactly 1.", nameof(SchemaVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProducerVersion, nameof(ProducerVersion));
        if (Profile is null)
        {
            throw new ArgumentException("The conditional dependency-injection fact set requires a non-null compilation profile.", nameof(Profile));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProgramIndexFingerprint, nameof(ProgramIndexFingerprint));
        ArgumentException.ThrowIfNullOrWhiteSpace(DebugProjection, nameof(DebugProjection));
        if (RegistrationArms.IsDefault
            || Groups.IsDefault
            || Diagnostics.IsDefault)
        {
            throw new ArgumentException("The conditional dependency-injection fact set collections and diagnostics must be initialized.", nameof(RegistrationArms));
        }

        this.SchemaVersion = SchemaVersion;
        this.ProducerVersion = ProducerVersion;
        this.Profile = Profile;
        this.ProgramIndexFingerprint = ProgramIndexFingerprint;
        this.RegistrationArms = RegistrationArms;
        this.Groups = Groups;
        this.Diagnostics = Diagnostics;
        this.DebugProjection = DebugProjection;
    }

    public int SchemaVersion { get; }

    public string ProducerVersion { get; }

    public CompilationProfile Profile { get; }

    public string ProgramIndexFingerprint { get; }

    public ImmutableArray<ConditionalDependencyInjectionRegistrationArmFact> RegistrationArms { get; }

    public ImmutableArray<ConditionalDependencyInjectionGroupFact> Groups { get; }

    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    public string DebugProjection { get; }
}

internal static class ConditionalDependencyInjectionFactContracts
{
    public static void Validate(
        SemanticFactId id,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A conditional dependency-injection fact requires non-empty evidence.", nameof(evidence));
        }

        if (evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Artifact)))
        {
            throw new ArgumentException("Conditional dependency-injection evidence must reference a non-empty artifact.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A conditional dependency-injection fact requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Fact certainty must never exceed its strongest evidence.", nameof(certainty));
        }
    }
}
