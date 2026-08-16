using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// Stable diagnostic codes the FusionCache framework model may emit. The codes are public and live
/// in Core so the Scenario Graph builder can join the exact code (never a substring search) and any
/// consumer can recognize the model's deterministic unsupported-shape diagnostics without depending
/// on the framework-model assembly.
/// </summary>
public static class FusionCacheDiagnosticCodes
{
    /// <summary>
    /// The operation is recognizably the FusionCache <c>GetOrSetAsync</c> family (exact invocation,
    /// assembly, assembly version, containing type, method name, and generic arity with the exact
    /// 2.6.0 package present) but the full supported signature, the compiler-supplied ordinals, or
    /// the exact anonymous callback-boundary proof is unsupported, missing, or multiple. The model
    /// emits this deterministic Warning and no fact; wrong assemblies, types, and method names and
    /// inapplicable or mixed package references stay silent <see cref="ModelResult.Unrecognized"/>.
    /// Every emitted diagnostic carries the canonical operation+reason detail
    /// (<see cref="UnsupportedShapeDetail"/>) so the Scenario Graph builder can bind the code to the
    /// exact diagnosed operation.
    /// </summary>
    public const string UnsupportedShape = "SEQFC001";

    /// <summary>
    /// Builds the canonical operation+reason detail an unsupported-shape diagnostic carries. The
    /// detail is the exact operation identity, a unit separator, and the failure reason; writers and
    /// matchers use this single canonical form so the code never needs a substring or summary search
    /// to learn which operation failed. A blank reason is rejected because every diagnostic must
    /// explain why the shape failed closed.
    /// </summary>
    public static string UnsupportedShapeDetail(OperationId operationId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));
        return string.Join('\u001f', operationId.Value, reason);
    }

    /// <summary>
    /// True when <paramref name="detail"/> is the exact canonical unsupported-shape detail for
    /// <paramref name="operationId"/>: the operation component equals the operation identity exactly
    /// and one non-blank reason follows the separator with no further separator. A detail for any
    /// other operation, a blank or missing reason, or a malformed shape never matches; the
    /// comparison is a precise canonical-format equality, never a substring or summary match.
    /// </summary>
    public static bool MatchesUnsupportedShapeOperation(string? detail, OperationId operationId)
    {
        if (detail is null)
        {
            return false;
        }

        var separator = detail.IndexOf('\u001f');
        return separator > 0
            && separator < detail.Length - 1
            && detail.AsSpan(0, separator).Equals(operationId.Value.AsSpan(), StringComparison.Ordinal)
            && detail.IndexOf('\u001f', separator + 1) < 0;
    }
}

/// <summary>
/// Closed vocabulary of the exact framework condition that guards one callback boundary. accepted contract admits
/// exactly <see cref="CacheMiss"/> for the FusionCache <c>GetOrSetAsync</c> value factory; the model
/// never claims a global cache hit rate, distributed-cache behavior, retries, or universal execution.
/// </summary>
public enum FrameworkCallbackConditionKind
{
    CacheMiss,
}

/// <summary>
/// One exact, evidence-backed FusionCache 2.6.0 <c>GetOrSetAsync</c> callback contract admitted by
/// the accepted contract FusionCache model. The fact records the exact compilation profile, Program Index
/// fingerprint, and accepted contract callback boundary it was joined from, the exact method and outer
/// invocation operation, the compiler factory parameter ordinal (2), the exact package contract
/// version, the bounded zero-or-one conditional cache-miss semantics, canonical framework-model
/// evidence, and the weakest contributor certainty. The profile/fingerprint/boundary anchors bind
/// the fact to its exact analysis context: a foreign profile, foreign fingerprint, or different
/// boundary never matches at the Scenario join. Missing, multiple, non-anonymous, or
/// member-incomplete callback target evidence, unsupported overloads, and unproven
/// supplied-argument shapes never produce a fact.
/// </summary>
public sealed record FusionCacheGetOrSetFact : BehaviorFact
{
    /// <summary>
    /// Constructs the fact with constructor validation in the codebase record style and assigns
    /// every required base member (<see cref="BehaviorFact.Id"/>, <see cref="BehaviorFact.Evidence"/>,
    /// <see cref="BehaviorFact.Certainty"/>) in the constructor body, so the
    /// <see cref="SetsRequiredMembersAttribute"/> claim is exact.
    /// </summary>
    [SetsRequiredMembers]
    public FusionCacheGetOrSetFact(
        CompilationProfileId profileId,
        string programIndexFingerprint,
        CallbackBoundaryId callbackBoundaryId,
        MethodId method,
        OperationId operation,
        int factoryParameterOrdinal,
        string contractVersion,
        CallbackCardinality cardinality,
        CallbackTriggerKind trigger,
        FrameworkCallbackConditionKind condition,
        BehaviorFactId id,
        ImmutableArray<EvidenceRef> evidence,
        CertaintyLevel certainty)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId.Value, nameof(profileId));
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackBoundaryId.Value, nameof(callbackBoundaryId));
        ArgumentException.ThrowIfNullOrWhiteSpace(method.Value, nameof(method));
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.Value, nameof(operation));
        ArgumentOutOfRangeException.ThrowIfNegative(factoryParameterOrdinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractVersion, nameof(contractVersion));
        if (!Enum.IsDefined(cardinality))
        {
            throw new ArgumentOutOfRangeException(nameof(cardinality), "Undefined callback cardinality.");
        }

        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger), "Undefined callback trigger kind.");
        }

        if (!Enum.IsDefined(condition))
        {
            throw new ArgumentOutOfRangeException(nameof(condition), "Undefined framework callback condition kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        if (evidence.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A FusionCache get-or-set fact requires non-empty evidence.", nameof(evidence));
        }

        if (certainty == CertaintyLevel.Unknown)
        {
            throw new ArgumentException("A FusionCache get-or-set fact requires explicit certainty.", nameof(certainty));
        }

        ProfileId = profileId;
        ProgramIndexFingerprint = programIndexFingerprint;
        CallbackBoundaryId = callbackBoundaryId;
        Method = method;
        Operation = operation;
        FactoryParameterOrdinal = factoryParameterOrdinal;
        ContractVersion = contractVersion;
        Cardinality = cardinality;
        Trigger = trigger;
        Condition = condition;
        Id = id;
        Evidence = evidence;
        Certainty = certainty;
    }

    /// <summary>
    /// Exact compilation profile that owns the fact. The Scenario join requires this to equal the
    /// request profile before any cache-miss region construction.
    /// </summary>
    public CompilationProfileId ProfileId { get; }

    /// <summary>
    /// Exact non-blank Program Index fingerprint of the profile at analysis time. The Scenario join
    /// requires ordinal equality with the request fingerprint before any cache-miss region
    /// construction.
    /// </summary>
    public string ProgramIndexFingerprint { get; }

    /// <summary>
    /// Exact accepted contract callback boundary the model joined to this fact. The Scenario join requires this
    /// to equal the current boundary identity; a fact anchored to a different boundary never selects
    /// or forms a region.
    /// </summary>
    public CallbackBoundaryId CallbackBoundaryId { get; }

    public MethodId Method { get; }

    public OperationId Operation { get; }

    /// <summary>
    /// Compiler declaration ordinal of the value-factory callback parameter; always 2 for the
    /// admitted shape, never the source argument position.
    /// </summary>
    public int FactoryParameterOrdinal { get; }

    /// <summary>
    /// Exact package contract version the model matched; always <c>2.6.0</c> for this model version.
    /// </summary>
    public string ContractVersion { get; }

    /// <summary>
    /// Bounded cardinality of the factory callback: exactly <see cref="CallbackCardinality.ZeroOrOne"/>.
    /// </summary>
    public CallbackCardinality Cardinality { get; }

    /// <summary>
    /// Trigger that guards the factory callback: exactly <see cref="CallbackTriggerKind.Conditional"/>.
    /// </summary>
    public CallbackTriggerKind Trigger { get; }

    /// <summary>
    /// Exact framework condition that guards the factory: exactly <see cref="FrameworkCallbackConditionKind.CacheMiss"/>.
    /// </summary>
    public FrameworkCallbackConditionKind Condition { get; }
}
