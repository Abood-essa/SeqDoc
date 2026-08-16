using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;

namespace SeqDoc.FrameworkModels.FusionCache;

/// <summary>
/// Versioned FusionCache <c>GetOrSetAsync</c> callback model. It admits exactly one declaration
/// shape: the <c>FusionCacheExtMethods.GetOrSetAsync&lt;T&gt;</c> extension over
/// <c>IFusionCache</c> with a string key, a <c>Func&lt;CancellationToken, Task&lt;T&gt;&gt;</c>
/// value factory at declaration ordinal 2, an options callback, optional tags and token, and a
/// <c>ValueTask&lt;T&gt;</c> return, with compiler-supplied ordinals exactly [0,1,2,3] where the
/// reduced extension receiver is ordinal 0 and key, factory, options are 1, 2, 3. The factory is
/// represented as zero-or-one conditional cache-miss work only when the exact package reference, the
/// exact method identity, the exact supplied arguments, and one matching accepted contract unknown-contract
/// anonymous-function boundary all agree. It never matches raw names, nearest-matches a version,
/// infers a callback target, or claims runtime cache behavior.
/// </summary>
public sealed class FusionCacheGetOrSetModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.fusioncache.get-or-set";
    public const string ModelVersionValue = "1.0.0";

    private const string FusionCacheAssembly = "ZiggyCreatures.FusionCache";
    private const string FusionCacheAssemblyVersion = "2.6.0.0";
    private const string FusionCachePackageVersion = "2.6.0";
    private const string FusionCacheExtMethodsType = "ZiggyCreatures.Caching.Fusion.FusionCacheExtMethods";
    private const string MethodMetadataName = "GetOrSetAsync";
    private const int FactoryParameterOrdinal = 2;
    private const string ReturnTypeValueTaskPrefix = "System.Threading.Tasks.ValueTask<";
    private const string FactoryParameterFuncPrefix = "System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<";

    /// <summary>
    /// Exact fully qualified declaration parameter display strings admitted by this model version.
    /// The factory parameter is matched structurally against the generic element of the return type
    /// so the model requires the exact same <c>T</c> without ever reading an application name.
    /// </summary>
    private static class Parameters
    {
        public const string Receiver = "ZiggyCreatures.Caching.Fusion.IFusionCache";
        public const string Key = "System.String";
        public const string Options = "System.Action<ZiggyCreatures.Caching.Fusion.FusionCacheEntryOptions>";
        public const string Tags = "System.Collections.Generic.IEnumerable<System.String>";
        public const string Token = "System.Threading.CancellationToken";
    }

    public FrameworkModelDescriptor Descriptor { get; } = new(
        ModelIdValue,
        ModelVersionValue,
        "FusionCache GetOrSetAsync",
        Order: 210);

    /// <summary>
    /// Applies when the unmodified Program Index contains exactly one package reference with the
    /// exact FusionCache identity regardless of version, and that sole reference's version is
    /// exactly 2.6.0. Absent, duplicate, different-version, blank-version, or mixed-version (for
    /// example 2.6.0 plus 2.5.0) references are inapplicable, never nearest-matched.
    /// </summary>
    public bool IsApplicable(FrameworkDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var references = FindFusionCachePackageReferences(context.ProgramIndex);
        return references.Length == 1
            && string.Equals(references[0].Version, FusionCachePackageVersion, StringComparison.Ordinal);
    }

    public ValueTask<ModelResult> AnalyzeSymbolAsync(
        SymbolDescriptor symbol,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ModelResult.Unrecognized);
    }

    public ValueTask<ModelResult> AnalyzeOperationAsync(
        OperationDescriptor operation,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(AnalyzeGetOrSet(operation, context));
    }

    private ModelResult AnalyzeGetOrSet(OperationDescriptor operation, FrameworkAnalysisContext context)
    {
        if (!string.Equals(operation.Kind, "Invocation", StringComparison.Ordinal)
            || operation.TargetIdentity is null)
        {
            return ModelResult.Unrecognized;
        }

        var identity = operation.TargetIdentity;
        if (!IsRecognizableFamilyIdentity(identity))
        {
            // A different assembly, assembly version, containing type, method name, or arity is not
            // recognizably the FusionCache GetOrSetAsync family. Wrong assemblies/types/names stay
            // silent: nothing is diagnosed and nothing is guessed.
            return ModelResult.Unrecognized;
        }

        var packageReference = FindExactPackageReference(context);
        if (packageReference is null)
        {
            // An absent, duplicate, blank, different-version, or mixed-version package reference is
            // inapplicable, never nearest-matched, and stays silent: without the sole exact 2.6.0
            // package the model never manufactures a contract from the operation shape alone.
            return ModelResult.Unrecognized;
        }

        if (!MatchesExactIdentity(identity))
        {
            // The operation is recognizably the FusionCache family with the exact package present,
            // but the complete declaration parameter/return shape is unsupported (for example a
            // lookalike factory element, a non-ValueTask return, a fallback-value parameter, or a
            // ref-kind). Fail closed with the deterministic unsupported-shape diagnostic and no fact.
            return UnsupportedShape(operation, context, "unsupported-signature");
        }

        if (!MatchesExactSuppliedOrdinals(operation.SuppliedParameterOrdinals))
        {
            // The compiler-supplied ordinals are not exactly [0,1,2,3] (for example supplied tags,
            // supplied token, or missing options). The optional declaration parameters are never
            // treated as supplied merely because they exist on the selected overload, so the shape
            // fails closed with the unsupported-shape diagnostic and no fact.
            return UnsupportedShape(operation, context, "unsupported-supplied-ordinals");
        }

        var boundary = ResolveMatchingBoundary(operation, context);
        if (boundary is null)
        {
            // Missing, multiple, non-anonymous, source-body, member-incomplete, or
            // profile/fingerprint-mismatched target evidence withholds the fact. The model never
            // infers a callback target from the operation or the package; because the operation is
            // recognizably the FusionCache family with the exact package, the unsupported or missing
            // boundary proof is the deterministic unsupported-shape diagnostic with no fact.
            return UnsupportedShape(operation, context, "unsupported-boundary");
        }

        ImmutableArray<EvidenceRef> combinedEvidence = [.. operation.Evidence, .. boundary.Evidence, .. packageReference.Evidence];

        // Certainty stays the weakest contributor and never promotes: the operation and boundary
        // fact certainties plus every retained evidence entry, including package-reference
        // provenance, may only weaken the fact.
        var certainty = WeakestCertainty(operation.Certainty, boundary.Certainty);
        foreach (var evidence in combinedEvidence)
        {
            certainty = WeakestCertainty(certainty, evidence.Certainty);
        }

        var modelEvidence = CreateModelEvidence(
            $"get-or-set:{operation.Id.Value}:{FactoryParameterOrdinal}",
            combinedEvidence,
            certainty);
        if (modelEvidence.IsEmpty)
        {
            // No eligible direct source/generated-source evidence survives the framework-model
            // invariant; fail closed rather than emitting a contract fact without source provenance.
            return ModelResult.Unrecognized;
        }

        // The fact retains the exact profile, Program Index fingerprint, and matched callback
        // boundary as anchors, and the deterministic identity detail carries the boundary identity
        // and fingerprint so a fact joined to a different boundary or fingerprint never produces
        // the same behavior-fact identity. Method Flow data and fingerprints themselves are never
        // modified by this model.
        var fact = new FusionCacheGetOrSetFact(
            context.Profile.Id,
            context.ProgramIndex.IndexFingerprint,
            boundary.Id,
            operation.Method,
            operation.Id,
            FactoryParameterOrdinal,
            FusionCachePackageVersion,
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Conditional,
            FrameworkCallbackConditionKind.CacheMiss,
            StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                context.Profile.Id,
                Descriptor.ModelId,
                Descriptor.Version,
                $"fusion-cache-get-or-set:ordinal:{FactoryParameterOrdinal}:contract:{FusionCachePackageVersion}:boundary:{boundary.Id.Value}:fingerprint:{context.ProgramIndex.IndexFingerprint}",
                new OperationBehaviorFactAnchor(operation.Method, operation.Id),
                0)),
            modelEvidence,
            certainty);
        return new ModelResult(true, facts: [fact]);
    }

    /// <summary>
    /// True when the operation is recognizably the FusionCache <c>GetOrSetAsync</c> family: the
    /// exact assembly, assembly version, containing metadata type, metadata method name, and generic
    /// arity 1. A wrong assembly, version, containing type, method name, or arity is not
    /// recognizably FusionCache and stays silent; only an operation that passes this family gate may
    /// ever produce the unsupported-shape diagnostic when the rest of the contract fails.
    /// </summary>
    private static bool IsRecognizableFamilyIdentity(FrameworkMethodIdentity identity)
        => string.Equals(identity.AssemblyIdentity, FusionCacheAssembly, StringComparison.Ordinal)
            && string.Equals(identity.AssemblyVersion, FusionCacheAssemblyVersion, StringComparison.Ordinal)
            && string.Equals(identity.ContainingMetadataType, FusionCacheExtMethodsType, StringComparison.Ordinal)
            && string.Equals(identity.MethodMetadataName, MethodMetadataName, StringComparison.Ordinal)
            && identity.GenericArity == 1;

    /// <summary>
    /// Emits the deterministic unsupported-shape diagnostic for a recognizably FusionCache family
    /// operation whose exact supported contract cannot be proven. The result is deliberately
    /// unrecognized with no facts: the model never manufactures cache-miss behavior from an
    /// unsupported shape.
    /// </summary>
    private static ModelResult UnsupportedShape(
        OperationDescriptor operation,
        FrameworkAnalysisContext context,
        string reason)
        => new(false, diagnostics:
        [
            FusionCacheGetOrSetModelDiagnostics.UnsupportedShape(
                context.Profile.Id,
                operation.Id,
                reason),
        ]);

    /// <summary>
    /// Matches the exact extension-method identity and complete declaration parameter/return shape.
    /// The factory parameter and the return type must carry the exact same generic element <c>T</c>;
    /// a lookalike element, a different return kind, or any other parameter/ref-kind mismatch fails
    /// closed. Parameter types are compiler display strings, never source names.
    /// </summary>
    private static bool MatchesExactIdentity(FrameworkMethodIdentity identity)
    {
        if (!string.Equals(identity.AssemblyIdentity, FusionCacheAssembly, StringComparison.Ordinal)
            || !string.Equals(identity.AssemblyVersion, FusionCacheAssemblyVersion, StringComparison.Ordinal)
            || !string.Equals(identity.ContainingMetadataType, FusionCacheExtMethodsType, StringComparison.Ordinal)
            || !string.Equals(identity.MethodMetadataName, MethodMetadataName, StringComparison.Ordinal)
            || identity.GenericArity != 1
            || identity.Parameters.IsDefault
            || identity.Parameters.Length != 6
            || !TryResolveReturnTypeElement(identity.ReturnType, out var returnElement))
        {
            return false;
        }

        for (var index = 0; index < identity.Parameters.Length; index++)
        {
            var parameter = identity.Parameters[index];
            if (parameter.RefKind != ParameterRefKind.None)
            {
                return false;
            }

            var expected = index switch
            {
                0 => Parameters.Receiver,
                1 => Parameters.Key,
                2 => BuildFactoryParameterType(returnElement),
                3 => Parameters.Options,
                4 => Parameters.Tags,
                5 => Parameters.Token,
                _ => null,
            };
            if (expected is null
                || !string.Equals(parameter.FullyQualifiedType, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Matches the exact compiler-supplied declaration ordinals [0,1,2,3]: the reduced extension
    /// receiver at ordinal 0 followed by key 1, factory 2, and options 3. The real compiler call
    /// includes the reduced receiver; an exact model never accepts a shape that omits it and never
    /// treats optional tags/token as supplied merely because they exist on the selected overload.
    /// </summary>
    private static bool MatchesExactSuppliedOrdinals(ImmutableArray<int> ordinals)
        => !ordinals.IsDefault
            && ordinals.Length == 4
            && ordinals[0] == 0
            && ordinals[1] == 1
            && ordinals[2] == 2
            && ordinals[3] == 3;

    /// <summary>
    /// Resolves the generic element <c>T</c> of the exact <c>System.Threading.Tasks.ValueTask&lt;T&gt;</c>
    /// return display string. Every other return shape fails closed.
    /// </summary>
    private static bool TryResolveReturnTypeElement(string? returnType, out string element)
    {
        element = string.Empty;
        if (string.IsNullOrWhiteSpace(returnType)
            || !returnType.StartsWith(ReturnTypeValueTaskPrefix, StringComparison.Ordinal)
            || !returnType.EndsWith('>')
            || returnType.Length <= ReturnTypeValueTaskPrefix.Length + 1)
        {
            return false;
        }

        element = returnType.Substring(
            ReturnTypeValueTaskPrefix.Length,
            returnType.Length - ReturnTypeValueTaskPrefix.Length - 1);
        return element.Length > 0;
    }

    /// <summary>
    /// Builds the exact factory parameter display string
    /// <c>System.Func&lt;System.Threading.CancellationToken, System.Threading.Tasks.Task&lt;T&gt;&gt;</c>
    /// for the resolved return element. The model requires the exact same <c>T</c>; it never reads an
    /// application or entity name.
    /// </summary>
    private static string BuildFactoryParameterType(string returnElement)
        => $"{FactoryParameterFuncPrefix}{returnElement}>>";

    /// <summary>
    /// Resolves the sole exact FusionCache package reference. Every same-identity package
    /// reference participates regardless of version; the total must be exactly one and that sole
    /// version must be exactly 2.6.0. Absent, duplicate, different-version, blank-version, or
    /// mixed-version (for example 2.6.0 plus 2.5.0) references are ambiguous and withhold rather
    /// than picking one.
    /// </summary>
    private static ProgramReference? FindExactPackageReference(FrameworkAnalysisContext context)
    {
        var references = FindFusionCachePackageReferences(context.ProgramIndex);
        if (references.Length != 1
            || !string.Equals(references[0].Version, FusionCachePackageVersion, StringComparison.Ordinal))
        {
            return null;
        }

        return references[0];
    }

    /// <summary>
    /// Enumerates every Program Index reference whose kind is Package and whose identity is
    /// exactly <c>ZiggyCreatures.FusionCache</c>, regardless of version. Exact applicability and
    /// the canonical fact both require exactly one such reference and that sole version to be
    /// exactly 2.6.0; the identity comparison stays case-sensitive and never nearest-matches a
    /// lookalike identity or version.
    /// </summary>
    private static ImmutableArray<ProgramReference> FindFusionCachePackageReferences(ProgramIndexSnapshot index)
        => index.References
            .Where(reference =>
                reference.Kind == ProgramReferenceKind.Package
                && string.Equals(reference.Identity, FusionCacheAssembly, StringComparison.Ordinal))
            .ToImmutableArray();

    /// <summary>
    /// Resolves the exact one matching accepted contract boundary: same profile, same Program Index fingerprint,
    /// same caller method and outer operation, factory ordinal 2, an anonymous-function target with
    /// a body operation, unknown contract provenance/cardinality/trigger, and initialized non-empty
    /// member and evidence collections. A null set, a foreign profile or fingerprint, zero or
    /// multiple matching boundaries, a source-body or non-anonymous contract, or incomplete members
    /// withholds the fact; the model never infers a target.
    /// </summary>
    private static CallbackBoundaryFact? ResolveMatchingBoundary(
        OperationDescriptor operation,
        FrameworkAnalysisContext context)
    {
        var set = context.CallbackBoundaryFacts;
        if (set is null
            || !Equals(set.Profile, context.Profile)
            || !string.Equals(
                set.ProgramIndexFingerprint,
                context.ProgramIndex.IndexFingerprint,
                StringComparison.Ordinal))
        {
            return null;
        }

        CallbackBoundaryFact? matched = null;
        foreach (var boundary in set.Boundaries)
        {
            if (!Equals(boundary.CallerMethod, operation.Method)
                || !Equals(boundary.OuterInvocationOperation, operation.Id)
                || boundary.ParameterOrdinal != FactoryParameterOrdinal)
            {
                continue;
            }

            if (boundary.TargetKind != CallbackTargetKind.AnonymousFunction
                || boundary.TargetBodyOperation is null
                || boundary.ContractProvenance != CallbackContractProvenance.Unknown
                || boundary.Cardinality != CallbackCardinality.Unknown
                || boundary.Trigger != CallbackTriggerKind.Unknown
                || boundary.MemberOperations.IsDefaultOrEmpty
                || boundary.Evidence.IsDefaultOrEmpty)
            {
                return null;
            }

            if (matched is not null)
            {
                return null;
            }

            matched = boundary;
        }

        return matched;
    }

    private static CertaintyLevel WeakestCertainty(CertaintyLevel first, CertaintyLevel second)
        => (CertaintyLevel)Math.Max((int)first, (int)second);

    /// <summary>
    /// Builds the single framework-model evidence record for one cache-miss fact. The identity
    /// payload hashes the complete canonical union of the operation, the matching callback
    /// boundary, and the exact package-reference evidence IDs, so package provenance and version
    /// support stay deterministic without polluting the underlying collection. Only direct source
    /// or generated-source evidence with a non-null range and a non-blank symbol may sit beneath
    /// the framework-model artifact, matching the <c>EvidenceRef</c> FrameworkModel invariant;
    /// when no eligible direct evidence remains the model fails closed by returning an empty
    /// collection instead of manufacturing an artifact without source provenance.
    /// </summary>
    private ImmutableArray<EvidenceRef> CreateModelEvidence(
        string subject,
        ImmutableArray<EvidenceRef> underlying,
        CertaintyLevel certainty)
    {
        var canonical = underlying
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var direct = canonical
            .Where(item => item.Kind is EvidenceKind.Source or EvidenceKind.GeneratedSource
                && item.Range is not null
                && !string.IsNullOrWhiteSpace(item.Symbol))
            .ToImmutableArray();
        if (direct.IsEmpty)
        {
            return [];
        }

        var artifact = $"{Descriptor.ModelId}:{Descriptor.Version}";
        var evidencePayload = $"{subject}\u001f{string.Join('\u001f', canonical.Select(item => item.Id.Value))}";
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel,
            artifact,
            null,
            null,
            null,
            null,
            certainty,
            Descriptor.ModelId,
            Descriptor.Version,
            Detail: evidencePayload));
        return
        [
            new EvidenceRef(
                id,
                EvidenceKind.FrameworkModel,
                artifact,
                range: null,
                symbol: null,
                detail: evidencePayload,
                certainty,
                direct,
                Descriptor.ModelId,
                Descriptor.Version),
        ];
    }
}
