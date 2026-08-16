using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels.FusionCache;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.FusionCache;

/// <summary>
/// accepted contract risk-based model tests for the exact FusionCache 2.6.0 <c>GetOrSetAsync</c> contract. The
/// model admits exactly one declaration shape: extension receiver <c>IFusionCache</c>, string key,
/// <c>Func&lt;CancellationToken, Task&lt;CacheRecord&gt;&gt;</c> factory at declaration ordinal 2,
/// options callback, optional tags and token, generic arity 1, and a <c>ValueTask&lt;CacheRecord&gt;</c>
/// return, with compiler-supplied ordinals exactly [0,1,2,3] where the reduced extension receiver
/// is ordinal 0 and key, factory, options are 1, 2, 3. Every version (including a mixed
/// 2.6.0 plus 2.5.0 conflict), identity, parameter,
/// return, lookalike, tag/token, missing-options, and callback-boundary variant fails closed; the
/// admitted fact retains zero-or-one conditional cache-miss semantics, canonical framework-model
/// evidence, weakest certainty, a deterministic identity, and cancellation propagation.
/// </summary>
public sealed class FusionCacheGetOrSetModelTests
{
    private const string FusionCacheAssembly = "ZiggyCreatures.FusionCache";
    private const string FusionCacheAssemblyVersion = "2.6.0.0";
    private const string FusionCachePackageVersion = "2.6.0";
    private const string FusionCacheExtMethodsType = "ZiggyCreatures.Caching.Fusion.FusionCacheExtMethods";
    private const string CacheRecordType = "AdvancedAnalysis.FusionCacheCallbacks.CacheRecord";
    private const string FactoryParameterType = "System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<AdvancedAnalysis.FusionCacheCallbacks.CacheRecord>>";
    private const string OptionsParameterType = "System.Action<ZiggyCreatures.Caching.Fusion.FusionCacheEntryOptions>";
    private const string TagsParameterType = "System.Collections.Generic.IEnumerable<System.String>";
    private const string FusionCacheTokenParameterType = "System.Threading.CancellationToken";
    private const string FusionCacheReturnType = "System.Threading.Tasks.ValueTask<AdvancedAnalysis.FusionCacheCallbacks.CacheRecord>";
    private const string IndexFingerprint = "index-fingerprint";

    private static readonly CompilationProfile Profile = CompilationProfile.Create(
        "tests/fixtures/AdvancedAnalysis/FusionCacheCallbacks/FusionCacheCallbacks.csproj",
        "Release",
        "net10.0");

    private static readonly CompilationProfile OtherProfile = CompilationProfile.Create(
        "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj",
        "Release",
        "net10.0");

    private static readonly DocumentId DocumentId = new("document:v1:fusion-cache-callbacks");

    private static readonly MethodId ExactMethodId = new(
        "method:v1:AdvancedAnalysis.FusionCacheCallbacks.CacheCallbacks.GetByIdAsync");

    private static readonly OperationId ExactOperationId = new(
        "operation:v1:AdvancedAnalysis.FusionCacheCallbacks.CacheCallbacks.GetByIdAsync:GetOrSetAsync");

    private static readonly OperationId FactoryBodyOperationId = new(
        "operation:v1:AdvancedAnalysis.FusionCacheCallbacks.CacheCallbacks.GetByIdAsync:factory-body");

    private const string MatchingBoundaryId = "callback-boundary:v1:get-cache-record";

    /// <summary>
    /// Claim 1: the exact package 2.6.0, exact extension-method identity, exact declaration
    /// parameter/return shape, exact supplied ordinals [0,1,2,3] (reduced extension receiver 0,
    /// key 1, factory 2, options 3), and the matching unknown-contract
    /// anonymous-function boundary at factory ordinal 2 admit exactly one cache-miss fact with the
    /// zero-or-one conditional CacheMiss semantics and the exact profile, Program Index
    /// fingerprint, and callback boundary anchors.
    /// </summary>
    [Fact]
    public async Task ExactAdmittedCallEmitsOneCacheMissFact()
    {
        var model = new FusionCacheGetOrSetModel();

        Assert.True(model.IsApplicable(new FrameworkDetectionContext(Profile, EmptyIndex())));

        var result = await model.AnalyzeOperationAsync(
            ExactOperation(),
            AnalysisContext(boundaries: MatchingBoundarySet()),
            CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.IsType<FusionCacheGetOrSetFact>(Assert.Single(result.Facts));
        Assert.Equal(Profile.Id, fact.ProfileId);
        Assert.Equal(IndexFingerprint, fact.ProgramIndexFingerprint);
        Assert.Equal(new CallbackBoundaryId(MatchingBoundaryId), fact.CallbackBoundaryId);
        Assert.Equal(ExactMethodId, fact.Method);
        Assert.Equal(ExactOperationId, fact.Operation);
        Assert.Equal(2, fact.FactoryParameterOrdinal);
        Assert.Equal(FusionCachePackageVersion, fact.ContractVersion);
        Assert.Equal(CallbackCardinality.ZeroOrOne, fact.Cardinality);
        Assert.Equal(CallbackTriggerKind.Conditional, fact.Trigger);
        Assert.Equal(FrameworkCallbackConditionKind.CacheMiss, fact.Condition);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.NotEmpty(fact.Evidence);
    }

    /// <summary>
    /// Claims 1-2: package variants are inapplicable (never nearest-matched) — including a mixed
    /// 2.6.0 plus 2.5.0 conflict, which also fails closed during exact-operation analysis — and
    /// every identity, parameter, return, lookalike, fallback, supplied-tags, supplied-token, and
    /// missing-options variant fails closed with no exact fact.
    /// </summary>
    [Theory]
    [InlineData("package-version-2.5.0")]
    [InlineData("package-absent")]
    [InlineData("package-duplicate")]
    [InlineData("package-blank-version")]
    [InlineData("package-mixed-versions")]
    [InlineData("assembly")]
    [InlineData("assembly-version")]
    [InlineData("containing-type")]
    [InlineData("method-name")]
    [InlineData("arity")]
    [InlineData("factory-parameter")]
    [InlineData("return-type")]
    [InlineData("fallback-parameter")]
    [InlineData("missing-options")]
    [InlineData("supplied-tags")]
    [InlineData("supplied-token")]
    public async Task IdentityAndSuppliedOrdinalVariantsFailClosed(string variant)
    {
        var model = new FusionCacheGetOrSetModel();

        if (variant.StartsWith("package-", StringComparison.Ordinal))
        {
            var index = variant switch
            {
                "package-version-2.5.0" => EmptyIndex(packageVersion: "2.5.0"),
                "package-absent" => EmptyIndex(includeFusionCacheReference: false),
                "package-duplicate" => EmptyIndexWithDuplicatePackage(),
                "package-blank-version" => EmptyIndex(packageVersion: ""),
                "package-mixed-versions" => EmptyIndexWithMixedPackageVersions(),
                _ => throw new ArgumentOutOfRangeException(nameof(variant)),
            };

            // Exact applicability requires exactly one FusionCache package reference in total and
            // that sole version to be exactly 2.6.0; absent, duplicate, blank, different-version,
            // or mixed-version references are inapplicable, never nearest-matched.
            Assert.False(model.IsApplicable(new FrameworkDetectionContext(Profile, index)));

            if (variant == "package-mixed-versions")
            {
                // The mixed 2.6.0 plus 2.5.0 conflict must also fail closed during analysis of
                // the exact operation with a matching boundary: the canonical fact never anchors
                // to one version while another same-identity reference exists.
                var mixedVersionsResult = await model.AnalyzeOperationAsync(
                    ExactOperation(),
                    AnalysisContext(index: index, boundaries: MatchingBoundarySet()),
                    CancellationToken.None);
                Assert.False(mixedVersionsResult.Recognized);
                Assert.Empty(mixedVersionsResult.Facts);
                // An inapplicable or mixed package reference stays silent: no SEQFC001 and no fact.
                Assert.Empty(mixedVersionsResult.Diagnostics);
            }

            return;
        }

        var result = await model.AnalyzeOperationAsync(
            MutateOperation(variant),
            AnalysisContext(boundaries: MatchingBoundarySet()),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);

        // regression: wrong assembly/type/name/arity variants are not recognizably the FusionCache
        // family and stay silent; every exact-family variant whose full signature or supplied
        // ordinals are unsupported emits exactly the stable SEQFC001 diagnostic and no fact.
        if (variant is "assembly" or "assembly-version" or "containing-type" or "method-name" or "arity")
        {
            Assert.Empty(result.Diagnostics);
        }
        else
        {
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(FusionCacheDiagnosticCodes.UnsupportedShape, diagnostic.Code, StringComparer.Ordinal);
            // regression canonical detail: the diagnostic carries the exact operation+reason detail so
            // the Scenario Graph builder can bind SEQFC001 to the exact diagnosed operation without
            // a substring or summary match.
            Assert.True(
                FusionCacheDiagnosticCodes.MatchesUnsupportedShapeOperation(diagnostic.InternalDetail, ExactOperationId),
                "The unsupported-shape diagnostic detail must bind to the exact diagnosed operation.");
        }
    }

    /// <summary>
    /// Claim 3: the model consumes a profile/fingerprint-matching accepted contract unknown-contract
    /// anonymous-function boundary for the same outer operation and factory ordinal 2. Missing,
    /// multiple, non-anonymous, source-body, wrong-operation, wrong-ordinal, member-incomplete,
    /// profile-mismatched, or fingerprint-mismatched target evidence withholds the fact.
    /// </summary>
    [Theory]
    [InlineData("missing-boundaries")]
    [InlineData("empty-set")]
    [InlineData("wrong-operation")]
    [InlineData("wrong-ordinal")]
    [InlineData("non-anonymous-target")]
    [InlineData("source-body-contract")]
    [InlineData("profile-mismatch")]
    [InlineData("fingerprint-mismatch")]
    [InlineData("multiple-boundaries")]
    [InlineData("empty-member-operations")]
    public async Task CallbackBoundaryMismatchPartitionsFailClosed(string partition)
    {
        var model = new FusionCacheGetOrSetModel();
        var context = partition switch
        {
            "missing-boundaries" => new FrameworkAnalysisContext(Profile, EmptyIndex()),
            "empty-set" => AnalysisContext(boundaries: MatchingBoundarySet(boundaries: [])),
            "wrong-operation" => AnalysisContext(boundaries: MatchingBoundarySet(
                boundaries:
                [
                    CreateBoundary(new OperationId("operation:v1:AdvancedAnalysis.FusionCacheCallbacks.CacheCallbacks.GetWithTagsAsync:GetOrSetAsync")),
                ])),
            "wrong-ordinal" => AnalysisContext(boundaries: MatchingBoundarySet(
                boundaries: [CreateBoundary(ExactOperationId, parameterOrdinal: 1)])),
            "non-anonymous-target" => AnalysisContext(boundaries: MatchingBoundarySet(
                boundaries: [CreateBoundary(ExactOperationId, targetKind: CallbackTargetKind.LocalFunction)])),
            "source-body-contract" => AnalysisContext(boundaries: MatchingBoundarySet(
                boundaries: [CreateBoundary(ExactOperationId, provenance: CallbackContractProvenance.SourceBody)])),
            "profile-mismatch" => AnalysisContext(boundaries: MatchingBoundarySet(profile: OtherProfile)),
            "fingerprint-mismatch" => AnalysisContext(boundaries: MatchingBoundarySet(fingerprint: "other-fingerprint")),
            "multiple-boundaries" => AnalysisContext(boundaries: MatchingBoundarySet(
                boundaries:
                [
                    CreateBoundary(ExactOperationId, boundaryId: "callback-boundary:v1:first"),
                    CreateBoundary(ExactOperationId, boundaryId: "callback-boundary:v1:second"),
                ])),
            "empty-member-operations" => AnalysisContext(boundaries: MatchingBoundarySet(
                boundaries: [CreateBoundary(ExactOperationId, memberOperations: [])])),
            _ => throw new ArgumentOutOfRangeException(nameof(partition)),
        };

        var result = await model.AnalyzeOperationAsync(ExactOperation(), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);

        // regression: the operation is recognizably the FusionCache family with the exact package, so
        // missing, multiple, non-anonymous, source-body, member-incomplete, or
        // profile/fingerprint-mismatched boundary proof emits exactly one deterministic SEQFC001
        // diagnostic and never a fact.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(FusionCacheDiagnosticCodes.UnsupportedShape, diagnostic.Code, StringComparer.Ordinal);
        Assert.True(
            FusionCacheDiagnosticCodes.MatchesUnsupportedShapeOperation(diagnostic.InternalDetail, ExactOperationId),
            "The unsupported-shape diagnostic detail must bind to the exact diagnosed operation.");
    }

    /// <summary>
    /// Claim 4: the admitted fact retains exact evidence provenance and certainty, degrades to the
    /// weakest contributor when the boundary is conservative, retains package-reference provenance
    /// IDs in the evidence payload while underlying only direct source evidence, degrades when
    /// package provenance is conservative, and produces a deterministic fact and evidence identity
    /// across repeated analysis.
    /// </summary>
    [Fact]
    public async Task FactRetainsExactCertaintyCanonicalEvidenceAndDeterministicIdentity()
    {
        var model = new FusionCacheGetOrSetModel();

        var first = await model.AnalyzeOperationAsync(
            ExactOperation(),
            AnalysisContext(boundaries: MatchingBoundarySet()),
            CancellationToken.None);
        var second = await model.AnalyzeOperationAsync(
            ExactOperation(),
            AnalysisContext(boundaries: MatchingBoundarySet()),
            CancellationToken.None);

        var firstFact = Assert.IsType<FusionCacheGetOrSetFact>(Assert.Single(first.Facts));
        var secondFact = Assert.IsType<FusionCacheGetOrSetFact>(Assert.Single(second.Facts));

        Assert.Equal(CertaintyLevel.Exact, firstFact.Certainty);
        Assert.NotEmpty(firstFact.Evidence);
        Assert.All(firstFact.Evidence, evidence => Assert.Equal(EvidenceKind.FrameworkModel, evidence.Kind));
        Assert.All(firstFact.Evidence, evidence => Assert.Equal(FusionCacheGetOrSetModel.ModelIdValue, evidence.ProducerId));
        Assert.All(firstFact.Evidence, evidence => Assert.Equal(FusionCacheGetOrSetModel.ModelVersionValue, evidence.ProducerVersion));
        Assert.All(firstFact.Evidence.SelectMany(evidence => evidence.UnderlyingEvidence), underlying => Assert.Equal(EvidenceKind.Source, underlying.Kind));

        // The model never promotes or invents certainty: a conservative accepted contract boundary degrades the fact.
        var conservative = await model.AnalyzeOperationAsync(
            ExactOperation(),
            AnalysisContext(boundaries: MatchingBoundarySet(
                boundaries: [CreateBoundary(ExactOperationId, certainty: CertaintyLevel.Conservative)])),
            CancellationToken.None);
        var conservativeFact = Assert.IsType<FusionCacheGetOrSetFact>(Assert.Single(conservative.Facts));
        Assert.Equal(CertaintyLevel.Conservative, conservativeFact.Certainty);

        // Package-reference provenance that is metadata-only (non-source kind, no range, no symbol)
        // never blocks the exact fact while the operation and boundary supply direct source
        // evidence: the framework-model underlying collection holds only direct Source evidence,
        // and the package evidence ID remains part of the deterministic evidence payload.
        var metadataPackage = await model.AnalyzeOperationAsync(
            ExactOperation(),
            AnalysisContext(
                index: EmptyIndex(
                    packageEvidence: [PackageMetadataEvidence("evidence:v1:fusion-cache:package-metadata", CertaintyLevel.Exact)]),
                boundaries: MatchingBoundarySet()),
            CancellationToken.None);
        var metadataPackageFact = Assert.IsType<FusionCacheGetOrSetFact>(Assert.Single(metadataPackage.Facts));
        Assert.Equal(CertaintyLevel.Exact, metadataPackageFact.Certainty);
        var metadataPackageEvidence = Assert.Single(metadataPackageFact.Evidence);
        Assert.All(
            metadataPackageEvidence.UnderlyingEvidence,
            underlying => Assert.Equal(EvidenceKind.Source, underlying.Kind));
        Assert.Contains("evidence:v1:fusion-cache:package-metadata", metadataPackageEvidence.Detail);

        // Package-reference certainty participates in the weakest-certainty fold: conservative
        // package provenance degrades an otherwise exact fact and never promotes it.
        var conservativePackage = await model.AnalyzeOperationAsync(
            ExactOperation(),
            AnalysisContext(
                index: EmptyIndex(
                    packageEvidence: [PackageMetadataEvidence("evidence:v1:fusion-cache:package-metadata", CertaintyLevel.Conservative)]),
                boundaries: MatchingBoundarySet()),
            CancellationToken.None);
        var conservativePackageFact = Assert.IsType<FusionCacheGetOrSetFact>(Assert.Single(conservativePackage.Facts));
        Assert.Equal(CertaintyLevel.Conservative, conservativePackageFact.Certainty);

        // Deterministic fact identity and canonical evidence identity across repeated analysis.
        Assert.Equal(firstFact.Id.Value, secondFact.Id.Value);
        Assert.Equal(
            firstFact.Evidence.Select(evidence => evidence.Id.Value).ToArray(),
            secondFact.Evidence.Select(evidence => evidence.Id.Value).ToArray());
    }

    /// <summary>
    /// Claim 5: a pre-cancelled token stops model analysis before any fact is produced.
    /// </summary>
    [Fact]
    public async Task CanceledTokenStopsAnalysisBeforeRecognizing()
    {
        var model = new FusionCacheGetOrSetModel();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            model.AnalyzeOperationAsync(ExactOperation(), AnalysisContext(boundaries: MatchingBoundarySet()), cts.Token).AsTask());
    }

    private static OperationDescriptor ExactOperation()
        => new(
            ExactOperationId,
            ExactMethodId,
            "Invocation",
            DocumentId,
            100,
            24,
            [SourceEvidence("GetOrSetAsync")],
            CertaintyLevel.Exact,
            TargetIdentity: ExactTargetIdentity(),
            SuppliedParameterOrdinals: [0, 1, 2, 3]);

    private static FrameworkMethodIdentity ExactTargetIdentity()
        => new(
            FusionCacheAssembly,
            FusionCacheExtMethodsType,
            "GetOrSetAsync",
            GenericArity: 1,
            [
                new ParameterIdentityDescriptor(ParameterRefKind.None, "ZiggyCreatures.Caching.Fusion.IFusionCache"),
                new ParameterIdentityDescriptor(ParameterRefKind.None, "System.String"),
                new ParameterIdentityDescriptor(ParameterRefKind.None, FactoryParameterType),
                new ParameterIdentityDescriptor(ParameterRefKind.None, OptionsParameterType),
                new ParameterIdentityDescriptor(ParameterRefKind.None, TagsParameterType),
                new ParameterIdentityDescriptor(ParameterRefKind.None, FusionCacheTokenParameterType),
            ],
            ReturnType: FusionCacheReturnType,
            AssemblyVersion: FusionCacheAssemblyVersion);

    private static OperationDescriptor MutateOperation(string variant)
    {
        var operation = ExactOperation();
        var identity = operation.TargetIdentity!;
        return variant switch
        {
            "assembly" => operation with
            {
                TargetIdentity = identity with { AssemblyIdentity = "ZiggyCreatures.FusionCache.Lookalike" },
            },
            "assembly-version" => operation with
            {
                TargetIdentity = identity with { AssemblyVersion = "2.6.1.0" },
            },
            "containing-type" => operation with
            {
                TargetIdentity = identity with { ContainingMetadataType = "ZiggyCreatures.Caching.Fusion.OtherExtMethods" },
            },
            "method-name" => operation with
            {
                TargetIdentity = identity with { MethodMetadataName = "GetOrAddAsync" },
            },
            "arity" => operation with
            {
                TargetIdentity = identity with { GenericArity = 0 },
            },
            "factory-parameter" => operation with
            {
                TargetIdentity = identity with
                {
                    Parameters = identity.Parameters.SetItem(
                        2,
                        new ParameterIdentityDescriptor(
                            ParameterRefKind.None,
                            "System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<AdvancedAnalysis.FusionCacheCallbacks.CacheRecordFallback>>")),
                },
            },
            "return-type" => operation with
            {
                TargetIdentity = identity with { ReturnType = "System.Threading.Tasks.Task<AdvancedAnalysis.FusionCacheCallbacks.CacheRecord>" },
            },
            "fallback-parameter" => operation with
            {
                TargetIdentity = identity with
                {
                    Parameters =
                    [
                        identity.Parameters[0],
                        identity.Parameters[1],
                        identity.Parameters[2],
                        new ParameterIdentityDescriptor(ParameterRefKind.None, CacheRecordType),
                        identity.Parameters[3],
                        identity.Parameters[4],
                        identity.Parameters[5],
                    ],
                },
            },
            "missing-options" => operation with { SuppliedParameterOrdinals = [0, 1, 2] },
            "supplied-tags" => operation with { SuppliedParameterOrdinals = [0, 1, 2, 3, 4] },
            "supplied-token" => operation with { SuppliedParameterOrdinals = [0, 1, 2, 3, 5] },
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
    }

    /// <summary>
    /// Builds one accepted contract callback boundary fact. The matching default is the unknown-contract
    /// anonymous-function factory at ordinal 2 with one canonical member operation; the parameters
    /// let tests reshape every mismatch partition through the same factory.
    /// </summary>
    private static CallbackBoundaryFact CreateBoundary(
        OperationId outerOperation,
        int parameterOrdinal = 2,
        CallbackTargetKind targetKind = CallbackTargetKind.AnonymousFunction,
        CallbackContractProvenance provenance = CallbackContractProvenance.Unknown,
        ImmutableArray<string> memberOperations = default,
        CertaintyLevel certainty = CertaintyLevel.Exact,
        string? boundaryId = null)
    {
        var isAnonymous = targetKind == CallbackTargetKind.AnonymousFunction;
        var isSourceBody = provenance == CallbackContractProvenance.SourceBody;
        return new CallbackBoundaryFact(
            new CallbackBoundaryId(boundaryId ?? MatchingBoundaryId),
            callerMethod: ExactMethodId,
            outerInvocationOperation: outerOperation,
            parameterOrdinal: parameterOrdinal,
            targetKind: targetKind,
            targetMethod: isAnonymous ? null : new MethodId("method:v1:AdvancedAnalysis.FusionCacheCallbacks.CacheCallbacks.Factory"),
            targetBodyOperation: isAnonymous ? FactoryBodyOperationId : null,
            contractMethod: isSourceBody ? new MethodId("method:v1:AdvancedAnalysis.FusionCacheCallbacks.CacheCallbacks.Factory") : null,
            contractInvokeOperation: isSourceBody ? new OperationId("operation:v1:contract-invoke") : null,
            cardinality: isSourceBody ? CallbackCardinality.ExactlyOnce : CallbackCardinality.Unknown,
            trigger: isSourceBody ? CallbackTriggerKind.Unconditional : CallbackTriggerKind.Unknown,
            triggerCondition: null,
            completion: isSourceBody ? CallbackCompletionKind.RejoinsCaller : CallbackCompletionKind.Unknown,
            contractProvenance: provenance,
            memberOperations: memberOperations.IsDefault ? [FactoryBodyOperationId.Value] : memberOperations,
            evidence: [SourceEvidence("factory", certainty)],
            certainty: certainty);
    }

    private static CallbackBoundaryFactSet MatchingBoundarySet(
        ImmutableArray<CallbackBoundaryFact> boundaries = default,
        CompilationProfile? profile = null,
        string? fingerprint = null)
        => new(
            SchemaVersion: 1,
            ProducerVersion: "callback-boundary-projection:v1",
            Profile: profile ?? Profile,
            ProgramIndexFingerprint: fingerprint ?? IndexFingerprint,
            Boundaries: boundaries.IsDefault ? [CreateBoundary(ExactOperationId)] : boundaries,
            Diagnostics: [],
            DebugProjection: "callback-boundaries");

    private static FrameworkAnalysisContext AnalysisContext(
        ProgramIndexSnapshot? index = null,
        CallbackBoundaryFactSet? boundaries = null)
        => new(
            Profile,
            index ?? EmptyIndex(),
            CallbackBoundaryFacts: boundaries);

    private static ProgramIndexSnapshot EmptyIndex(
        bool includeFusionCacheReference = true,
        string? packageVersion = FusionCachePackageVersion,
        ImmutableArray<EvidenceRef>? packageEvidence = null)
        => new(
            SchemaVersion: 1,
            ProducerVersion: "test",
            Profile,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            includeFusionCacheReference
                ? [
                    new ProgramReference(
                        "reference:v1:package|ZiggyCreatures.FusionCache",
                        new ProjectId("project:v1:fusion-cache"),
                        ProgramReferenceKind.Package,
                        FusionCacheAssembly,
                        packageVersion,
                        packageEvidence ?? [SourceEvidence("reference")]),
                ]
                : [],
            [],
            [],
            [],
            "input-hash",
            IndexFingerprint);

    private static ProgramIndexSnapshot EmptyIndexWithDuplicatePackage()
        => EmptyIndex() with
        {
            References =
            [
                new ProgramReference(
                    "reference:v1:package|ZiggyCreatures.FusionCache",
                    new ProjectId("project:v1:fusion-cache"),
                    ProgramReferenceKind.Package,
                    FusionCacheAssembly,
                    FusionCachePackageVersion,
                    [SourceEvidence("reference")]),
                new ProgramReference(
                    "reference:v1:package|ZiggyCreatures.FusionCache-2",
                    new ProjectId("project:v1:fusion-cache"),
                    ProgramReferenceKind.Package,
                    FusionCacheAssembly,
                    FusionCachePackageVersion,
                    [SourceEvidence("reference-2")]),
            ],
        };

    private static ProgramIndexSnapshot EmptyIndexWithMixedPackageVersions()
        => EmptyIndex() with
        {
            References =
            [
                new ProgramReference(
                    "reference:v1:package|ZiggyCreatures.FusionCache",
                    new ProjectId("project:v1:fusion-cache"),
                    ProgramReferenceKind.Package,
                    FusionCacheAssembly,
                    FusionCachePackageVersion,
                    [SourceEvidence("reference")]),
                new ProgramReference(
                    "reference:v1:package|ZiggyCreatures.FusionCache-2.5.0",
                    new ProjectId("project:v1:fusion-cache"),
                    ProgramReferenceKind.Package,
                    FusionCacheAssembly,
                    "2.5.0",
                    [SourceEvidence("reference-2.5.0")]),
            ],
        };

    private static EvidenceRef SourceEvidence(string symbol, CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(
            new EvidenceId($"evidence:v1:fusion-cache:{symbol}"),
            EvidenceKind.Source,
            "CacheCallbacks.cs",
            new SourceRange(DocumentId, new SourcePosition(10, 0), new SourcePosition(10, 30)),
            symbol,
            detail: null,
            certainty);

    /// <summary>
    /// Builds package-reference provenance as metadata-only evidence: a non-source/build kind with
    /// no range and no symbol. Such evidence is never eligible for the FrameworkModel underlying
    /// collection but its ID and certainty still participate in the evidence payload and fold.
    /// </summary>
    private static EvidenceRef PackageMetadataEvidence(string id, CertaintyLevel certainty)
        => new(
            new EvidenceId(id),
            EvidenceKind.AssemblyMetadata,
            "ZiggyCreatures.FusionCache.2.6.0.nupkg",
            range: null,
            symbol: null,
            detail: "package:ZiggyCreatures.FusionCache@2.6.0",
            certainty);
}
