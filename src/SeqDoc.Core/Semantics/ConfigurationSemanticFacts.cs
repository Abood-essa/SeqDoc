using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Semantics;

/// <summary>
/// Closed vocabulary of the standard configuration providers installed by
/// <c>WebApplication.CreateBuilder</c> in precedence order. These facts describe possible later
/// override only; they never claim a provider is present, an environment name, or an effective value.
/// </summary>
public enum StandardConfigurationProviderKind
{
    BaseJson,
    EnvironmentJson,
    DevelopmentUserSecrets,
    EnvironmentVariables,
    CommandLine,
}

/// <summary>
/// One exact compiler-resolved Microsoft <c>ConfigurationBinder.GetValue&lt;bool&gt;</c> read. The
/// receiver is assignable to the exact <c>IConfiguration</c>, the key is one compile-time constant
/// non-sensitive string, and the overload/default shape is explicitly supported. Same-name lookalikes,
/// dynamic keys, non-boolean generics, section calls, and custom receivers fail closed and never
/// project this fact. The fact is anchored to the exact invocation operation with non-empty evidence
/// and Exact certainty that never exceeds its strongest evidence.
/// </summary>
public sealed record ConfigurationReadSemanticFact
{
    public ConfigurationReadSemanticFact(
        SemanticFactId id,
        MethodId method,
        OperationId operation,
        string key,
        bool? defaultValue,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ConfigurationSemanticFactContracts.ValidateWithMethod(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        if (ConfigurationSemanticKeyPolicy.IsSensitive(key))
        {
            throw new ArgumentException("A configuration read fact must never carry a sensitive key.", nameof(key));
        }

        Id = id;
        Method = method;
        Operation = operation;
        Key = key;
        DefaultValue = defaultValue;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public OperationId Operation { get; }

    /// <summary>Canonical non-sensitive configuration key read by the invocation.</summary>
    public string Key { get; }

    /// <summary>
    /// Compiler-proven boolean default when the supported explicit-default overload was used; null
    /// for the no-default shape. An unsupported default shape never projects a read fact.
    /// </summary>
    public bool? DefaultValue { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One exact boolean condition association: an admitted read flows through exactly one compiler-bound
/// local assigned once from the read into an <c>if</c> boolean condition. The fact anchors to the
/// read operation and to a real source-backed condition operation of the accepted behavior input; it
/// never recomputes generic control flow or infers through arbitrary dataflow. Evidence retains the
/// canonical union of the admitted read evidence and the exact condition-operation evidence, and the
/// fact certainty degrades to the weakest contributor. The semantic relationship records which branch
/// the read selects (the branch is taken when the read is true).
/// </summary>
public sealed record ConfigurationConditionSemanticFact
{
    public ConfigurationConditionSemanticFact(
        SemanticFactId id,
        MethodId method,
        OperationId readOperation,
        OperationId conditionOperation,
        bool trueWhenReadTrue,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ConfigurationSemanticFactContracts.ValidateWithMethod(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(readOperation.Value, nameof(readOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(conditionOperation.Value, nameof(conditionOperation));
        Id = id;
        Method = method;
        ReadOperation = readOperation;
        ConditionOperation = conditionOperation;
        TrueWhenReadTrue = trueWhenReadTrue;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    /// <summary>Exact operation of the admitted read whose local selects the branch.</summary>
    public OperationId ReadOperation { get; }

    /// <summary>
    /// Exact compiler operation of the <c>if</c> boolean condition in the accepted behavior input.
    /// </summary>
    public OperationId ConditionOperation { get; }

    /// <summary>
    /// True when the taken branch is entered when the read value is true. The direct local shape
    /// admitted by accepted contract always records this positive relationship.
    /// </summary>
    public bool TrueWhenReadTrue { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One standard-provider precedence observation for an exact <c>WebApplication.CreateBuilder</c>
/// call. The five observations describe the framework's default configuration precedence (base JSON,
/// environment JSON, development user secrets, environment variables, then command line) as possible
/// later override; they are Conservative and never claim a provider is present, an environment name,
/// or an effective value.
/// </summary>
public sealed record StandardProviderObservationFact
{
    public StandardProviderObservationFact(
        SemanticFactId id,
        MethodId method,
        OperationId operation,
        StandardConfigurationProviderKind providerKind,
        int precedenceOrdinal,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ConfigurationSemanticFactContracts.ValidateWithMethod(id, method, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentOutOfRangeException.ThrowIfNegative(precedenceOrdinal);
        if (!Enum.IsDefined(providerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKind), "Undefined standard configuration provider kind.");
        }

        if (certainty != CertaintyLevel.Conservative)
        {
            throw new ArgumentException("A standard provider observation is Conservative and never Exact.", nameof(certainty));
        }

        Id = id;
        Method = method;
        Operation = operation;
        ProviderKind = providerKind;
        PrecedenceOrdinal = precedenceOrdinal;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public MethodId Method { get; }

    public OperationId Operation { get; }

    public StandardConfigurationProviderKind ProviderKind { get; }

    /// <summary>Deterministic precedence position of the provider in the default configuration chain.</summary>
    public int PrecedenceOrdinal { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One checked-in boolean observation read from the analyzed project's owned <c>appsettings.json</c>
/// or <c>appsettings.&lt;Environment&gt;.json</c> file. Only matching non-sensitive boolean keys are
/// retained; unrelated keys and raw string/number/object values never enter the payload. The
/// observation is Conservative and explicitly <see cref="MayBeOverridden"/>; it is never runtime
/// truth and never claims a provider overrode it. The source file is repository-relative only.
/// </summary>
public sealed record CheckedInConfigurationValueFact
{
    public CheckedInConfigurationValueFact(
        SemanticFactId id,
        string key,
        bool value,
        string sourceFile,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty,
        bool mayBeOverridden = true)
    {
        ConfigurationSemanticFactContracts.ValidateWithoutMethod(id, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        if (ConfigurationSemanticKeyPolicy.IsSensitive(key))
        {
            throw new ArgumentException("A checked-in configuration fact must never carry a sensitive key.", nameof(key));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile, nameof(sourceFile));
        if (certainty != CertaintyLevel.Conservative)
        {
            throw new ArgumentException("A checked-in configuration observation is Conservative and never Exact.", nameof(certainty));
        }

        if (mayBeOverridden != true)
        {
            throw new ArgumentException("A checked-in configuration observation is explicitly MayBeOverridden and never carries the false marker.", nameof(mayBeOverridden));
        }

        Id = id;
        Key = key;
        Value = value;
        SourceFile = sourceFile;
        MayBeOverridden = mayBeOverridden;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public string Key { get; }

    public bool Value { get; }

    /// <summary>Repository-relative path of the appsettings file that owns the observation.</summary>
    public string SourceFile { get; }

    /// <summary>
    /// True because later providers in the standard chain may override a checked-in value; the flag
    /// makes the override risk explicit rather than claiming runtime truth.
    /// </summary>
    public bool MayBeOverridden { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// One explicit analysis-profile known boolean value read from
/// <c>CompilationProfile.AnalysisProperties</c>. The key must also be an admitted read candidate key
/// and must be non-sensitive, and the value must parse exactly as a boolean; unrelated boolean
/// properties fail closed. The fact carries analysis-profile provenance and is not a universal
/// deployment claim; unsupported or conflicting values fail closed.
/// </summary>
public sealed record ProfileKnownConfigurationValueFact
{
    public ProfileKnownConfigurationValueFact(
        SemanticFactId id,
        string key,
        bool value,
        string analysisProfileSource,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ConfigurationSemanticFactContracts.ValidateWithoutMethod(id, evidence, certainty);
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));
        if (ConfigurationSemanticKeyPolicy.IsSensitive(key))
        {
            throw new ArgumentException("A profile-known configuration fact must never carry a sensitive key.", nameof(key));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(analysisProfileSource, nameof(analysisProfileSource));
        Id = id;
        Key = key;
        Value = value;
        AnalysisProfileSource = analysisProfileSource;
        Evidence = evidence;
        Certainty = certainty;
    }

    public SemanticFactId Id { get; }

    public string Key { get; }

    public bool Value { get; }

    /// <summary>Provenance token identifying the analysis-profile source of the known value.</summary>
    public string AnalysisProfileSource { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    public CertaintyLevel Certainty { get; }
}

/// <summary>
/// Roslyn-neutral, memory-only set of configuration semantic companion facts for one compilation
/// profile: exact boolean reads, direct local-to-<c>if</c> condition associations, standard-provider
/// precedence observations, checked-in appsettings observations, and explicit analysis-profile known
/// values. The set records schema and producer versions, the compilation profile, the Program Index
/// fingerprint, canonically ordered facts, diagnostics, and a deterministic debug representation free
/// of absolute paths and raw values. Construction enforces the impossible-state invariants: schema
/// version exactly 1, a non-blank producer and fingerprint, a non-null profile, initialized (never
/// default) fact/diagnostic collections, and non-blank debug text. Persistence, cache reconstruction,
/// and Scenario Graph joining are explicitly out of scope for this contract; accepted contract owns the DI branch
/// association.
/// </summary>
public sealed class ConfigurationSemanticFactSet
{
    public ConfigurationSemanticFactSet(
        int SchemaVersion,
        string ProducerVersion,
        CompilationProfile Profile,
        string ProgramIndexFingerprint,
        ImmutableArray<ConfigurationReadSemanticFact> Reads,
        ImmutableArray<ConfigurationConditionSemanticFact> Conditions,
        ImmutableArray<StandardProviderObservationFact> ProviderObservations,
        ImmutableArray<CheckedInConfigurationValueFact> CheckedInValues,
        ImmutableArray<ProfileKnownConfigurationValueFact> ProfileKnownValues,
        ImmutableArray<AnalysisDiagnostic> Diagnostics,
        string DebugProjection)
    {
        if (SchemaVersion != 1)
        {
            throw new ArgumentException("The configuration fact set schema version must be exactly 1.", nameof(SchemaVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProducerVersion, nameof(ProducerVersion));
        if (Profile is null)
        {
            throw new ArgumentException("The configuration fact set requires a non-null compilation profile.", nameof(Profile));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProgramIndexFingerprint, nameof(ProgramIndexFingerprint));
        ArgumentException.ThrowIfNullOrWhiteSpace(DebugProjection, nameof(DebugProjection));
        if (Reads.IsDefault
            || Conditions.IsDefault
            || ProviderObservations.IsDefault
            || CheckedInValues.IsDefault
            || ProfileKnownValues.IsDefault
            || Diagnostics.IsDefault)
        {
            throw new ArgumentException("The configuration fact set collections and diagnostics must be initialized.", nameof(Reads));
        }

        this.SchemaVersion = SchemaVersion;
        this.ProducerVersion = ProducerVersion;
        this.Profile = Profile;
        this.ProgramIndexFingerprint = ProgramIndexFingerprint;
        this.Reads = Reads;
        this.Conditions = Conditions;
        this.ProviderObservations = ProviderObservations;
        this.CheckedInValues = CheckedInValues;
        this.ProfileKnownValues = ProfileKnownValues;
        this.Diagnostics = Diagnostics;
        this.DebugProjection = DebugProjection;
    }

    public int SchemaVersion { get; }

    public string ProducerVersion { get; }

    public CompilationProfile Profile { get; }

    public string ProgramIndexFingerprint { get; }

    public ImmutableArray<ConfigurationReadSemanticFact> Reads { get; }

    public ImmutableArray<ConfigurationConditionSemanticFact> Conditions { get; }

    public ImmutableArray<StandardProviderObservationFact> ProviderObservations { get; }

    public ImmutableArray<CheckedInConfigurationValueFact> CheckedInValues { get; }

    public ImmutableArray<ProfileKnownConfigurationValueFact> ProfileKnownValues { get; }

    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    public string DebugProjection { get; }
}

/// <summary>
/// Sensitive-key policy for configuration semantic facts, mirroring the SD3011 known-value
/// validation so connection strings, API keys, passwords, and tokens never enter fact payloads,
/// diagnostics, evidence detail, or debug projection.
/// </summary>
public static class ConfigurationSemanticKeyPolicy
{
    private static readonly string[] SensitiveKeyFragments =
        ["PASSWORD", "PASSPHRASE", "PWD", "SECRET", "TOKEN", "APIKEY", "ACCESSKEY", "PRIVATEKEY", "CONNECTIONSTRING", "CREDENTIAL", "AUTHORIZATION"];

    public static bool IsSensitive(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        // Canonicalize every non-alphanumeric separator (colon, dot, slash, underscore, dash, and
        // any other punctuation) so hierarchical spellings such as Api:Key, Private.Key,
        // Access/Key, and Pass:Word cannot bypass the fragment policy while ordinary safe feature
        // keys remain unaffected.
        string normalized = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return SensitiveKeyFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }
}

internal static class ConfigurationSemanticFactContracts
{
    public static void ValidateWithMethod(
        SemanticFactId id,
        MethodId method,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value, nameof(method));
        ValidateEvidence(evidence, certainty);
    }

    public static void ValidateWithoutMethod(
        SemanticFactId id,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ValidateEvidence(evidence, certainty);
    }

    private static void ValidateEvidence(
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A configuration semantic fact requires non-empty evidence.", nameof(evidence));
        }

        if (evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.Artifact)))
        {
            throw new ArgumentException("Configuration semantic-fact evidence must reference a non-empty artifact.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A configuration semantic fact requires explicit certainty.", nameof(certainty));
        }

        if (certainty < evidence.Max(item => item.Certainty))
        {
            throw new ArgumentException("Fact certainty must never exceed its strongest evidence.", nameof(certainty));
        }
    }
}
