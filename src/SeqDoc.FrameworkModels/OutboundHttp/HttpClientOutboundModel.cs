using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels.OutboundHttp;

/// <summary>
/// Versioned model for exact direct <c>System.Net.Http.HttpClient</c> outbound request boundaries.
/// Admits, atomically per compilation profile, only two overloads:
/// <c>HttpClient.GetAsync(string)</c> and <c>HttpClient.PostAsync(string, System.Net.Http.HttpContent)</c>,
/// each returning <c>Task&lt;HttpResponseMessage&gt;</c>, on assembly <c>System.Net.Http</c>
/// (public key token <c>b03f5f7f11d50a3a</c>) at version <c>9.0.0.0</c> for <c>net9.0</c> and
/// <c>10.0.0.0</c> for <c>net10.0</c>. A recognizable-family call (assembly name, token, containing
/// type, method name in GetAsync/PostAsync/SendAsync, arity 0) whose profile assembly version matches
/// but whose overload is not an admitted row emits exactly one deterministic <c>SEQHTTP001</c>
/// warning and no fact. For an applicable <c>net9.0</c>/<c>net10.0</c> profile, a recognizable-family
/// call whose assembly version is wrong or missing is itself a recognized-but-unsupported shape and
/// emits exactly one <c>SEQHTTP001</c> and no fact. Only an identity that fails family recognition
/// (missing or foreign assembly name, token, containing type, method, or arity), a non-<c>net9.0</c>/
/// <c>net10.0</c> profile, or a missing supplied-ordinal projection stays silent with no diagnostic.
/// This model never reads the URI argument and never claims a request completed.
/// </summary>
public sealed class HttpClientOutboundModel : IFrameworkBehaviorModel
{
    private const string ModelId = "seqdoc.system-net-http.outbound";
    private const string ModelVersion = "1.0.0";
    private const string Assembly = "System.Net.Http";
    private const string PublicKeyToken = "b03f5f7f11d50a3a";
    private const string ContainingType = "System.Net.Http.HttpClient";
    private const string ReturnType = "System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage>";
    private const string StringType = "System.String";
    private const string HttpContentType = "System.Net.Http.HttpContent";
    private const string UriType = "System.Uri";
    private const string CancellationTokenType = "System.Threading.CancellationToken";
    private const string CompletionOptionType = "System.Net.Http.HttpCompletionOption";

    private static readonly ImmutableHashSet<string> FamilyMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal, "GetAsync", "PostAsync", "SendAsync");

    public FrameworkModelDescriptor Descriptor { get; } =
        new(ModelId, ModelVersion, "System.Net.Http outbound requests", 120);

    public bool IsApplicable(FrameworkDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RequiredAssemblyVersion(context.Profile.TargetFramework) is not null;
    }

    public ValueTask<ModelResult> AnalyzeSymbolAsync(
        SymbolDescriptor symbol, FrameworkAnalysisContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(ModelResult.Unrecognized);

    public ValueTask<ModelResult> AnalyzeOperationAsync(
        OperationDescriptor operation, FrameworkAnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (!string.Equals(operation.Kind, "Invocation", StringComparison.Ordinal)
            || operation.TargetIdentity is not { } identity)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        // Recognizable family: every component must be present and exact. Partial identity never
        // establishes the family, so it stays silent (no diagnostic).
        if (string.IsNullOrEmpty(identity.AssemblyIdentity)
            || !string.Equals(identity.AssemblyIdentity, Assembly, StringComparison.Ordinal)
            || !string.Equals(identity.AssemblyPublicKeyToken, PublicKeyToken, StringComparison.Ordinal)
            || string.IsNullOrEmpty(identity.ContainingMetadataType)
            || !string.Equals(identity.ContainingMetadataType, ContainingType, StringComparison.Ordinal)
            || string.IsNullOrEmpty(identity.MethodMetadataName)
            || !FamilyMethods.Contains(identity.MethodMetadataName)
            || identity.GenericArity != 0)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        // Non-net9/net10 profile: this model does not run for the target framework at all.
        var requiredVersion = RequiredAssemblyVersion(context.Profile.TargetFramework);
        if (requiredVersion is null)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        // Missing required admission field: a recognizable family with no supplied-ordinal projection
        // fails closed silently (no fact, no SEQHTTP001). A present-but-unsupported ordinal set stays
        // on the recognized-but-unsupported path below.
        if (operation.SuppliedParameterOrdinals.IsDefault)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var ordinals = operation.SuppliedParameterOrdinals;

        // Atomic profile/assembly-version admission: no range, nearest, facade, or missing version.
        // Version is NOT a family-recognition component, so a recognizable-family call on an
        // applicable net9/net10 profile whose assembly version is wrong or missing is a
        // recognized-but-unsupported boundary (one deterministic SEQHTTP001), never silence.
        var versionMatches = !string.IsNullOrEmpty(identity.AssemblyVersion)
            && string.Equals(identity.AssemblyVersion, requiredVersion, StringComparison.Ordinal);

        var admittedKind = versionMatches ? AdmittedRequestKind(identity, ordinals) : null;
        if (admittedKind is { } kind)
        {
            // Fact-shape guards: fail closed and silent when the operation lacks the evidence /
            // certainty needed to carry a proven boundary.
            if (operation.Certainty == CertaintyLevel.Unknown
                || operation.Evidence.IsDefaultOrEmpty
                || !operation.Evidence.Any(item => item.Kind is EvidenceKind.Source or EvidenceKind.GeneratedSource))
            {
                return ValueTask.FromResult(ModelResult.Unrecognized);
            }

            var certainty = WeakestCertainty(operation.Certainty, operation.Evidence);
            var fact = new OutboundHttpRequestFact
            {
                Id = StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                    context.Profile.Id,
                    ModelId,
                    ModelVersion,
                    FactKind(kind),
                    new OperationBehaviorFactAnchor(operation.Method, operation.Id),
                    0)),
                Evidence = CreateModelEvidence(
                    $"outbound-http-request:{kind}:{operation.Id.Value}", operation.Evidence, certainty),
                Certainty = certainty,
                CallerMethod = operation.Method,
                InvocationOperation = operation.Id,
                RequestKind = kind,
                FrameworkMethodIdentity = identity,
            };
            return ValueTask.FromResult(new ModelResult(true, [fact]));
        }

        // Recognizable family + matching profile version, but not an admitted row: one deterministic
        // SEQHTTP001, no fact.
        var diagnostic = OutboundHttpDiagnosticCodes.RecognizedUnsupportedOverload(
            context.Profile.Id,
            operation.Id.Value,
            CallerDetail(operation, context),
            versionMatches
                ? ClassifyUnsupported(identity, ordinals)
                : OutboundHttpUnsupportedReason.WrongAssemblyVersion);
        return ValueTask.FromResult(new ModelResult(false, diagnostics: [diagnostic]));
    }

    private static string? RequiredAssemblyVersion(string? targetFramework) => targetFramework switch
    {
        "net9.0" => "9.0.0.0",
        "net10.0" => "10.0.0.0",
        _ => null,
    };

    private static OutboundHttpRequestKind? AdmittedRequestKind(FrameworkMethodIdentity identity, ImmutableArray<int> ordinals)
    {
        if (!string.Equals(identity.ReturnType, ReturnType, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(identity.MethodMetadataName, "GetAsync", StringComparison.Ordinal)
            && ParametersAre(identity.Parameters, StringType)
            && ordinals.SequenceEqual([0]))
        {
            return OutboundHttpRequestKind.Get;
        }

        if (string.Equals(identity.MethodMetadataName, "PostAsync", StringComparison.Ordinal)
            && ParametersAre(identity.Parameters, StringType, HttpContentType)
            && ordinals.SequenceEqual([0, 1]))
        {
            return OutboundHttpRequestKind.Post;
        }

        return null;
    }

    private static bool ParametersAre(ImmutableArray<ParameterIdentityDescriptor> parameters, params string[] types)
    {
        if (parameters.IsDefault || parameters.Length != types.Length)
        {
            return false;
        }

        for (var i = 0; i < types.Length; i++)
        {
            if (parameters[i].RefKind != ParameterRefKind.None
                || !string.Equals(parameters[i].FullyQualifiedType, types[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static OutboundHttpUnsupportedReason ClassifyUnsupported(
        FrameworkMethodIdentity identity, ImmutableArray<int> ordinals)
    {
        if (string.Equals(identity.MethodMetadataName, "SendAsync", StringComparison.Ordinal))
        {
            return OutboundHttpUnsupportedReason.SendAsync;
        }

        var parameterTypes = identity.Parameters.IsDefault
            ? []
            : identity.Parameters.Select(parameter => parameter.FullyQualifiedType).ToArray();
        if (parameterTypes.Contains(UriType, StringComparer.Ordinal))
        {
            return OutboundHttpUnsupportedReason.UriParameter;
        }

        if (parameterTypes.Contains(CancellationTokenType, StringComparer.Ordinal))
        {
            return OutboundHttpUnsupportedReason.CancellationTokenOverload;
        }

        if (parameterTypes.Contains(CompletionOptionType, StringComparer.Ordinal))
        {
            return OutboundHttpUnsupportedReason.CompletionOptionOverload;
        }

        var expected = string.Equals(identity.MethodMetadataName, "PostAsync", StringComparison.Ordinal)
            ? new[] { 0, 1 }
            : [0];
        if (!ordinals.SequenceEqual(expected))
        {
            return OutboundHttpUnsupportedReason.MismatchedSuppliedOrdinals;
        }

        return OutboundHttpUnsupportedReason.WrongShape;
    }

    private static string FactKind(OutboundHttpRequestKind kind)
        => kind == OutboundHttpRequestKind.Post
            ? "outbound-http-request:post"
            : "outbound-http-request:get";

    private static string CallerDetail(OperationDescriptor operation, FrameworkAnalysisContext context)
    {
        var name = context.ProgramIndex.Methods
            .FirstOrDefault(method => method.Id == operation.Method)?.Name;
        return string.IsNullOrEmpty(name) ? operation.Method.Value : name;
    }

    private static CertaintyLevel WeakestCertainty(CertaintyLevel input, ImmutableArray<EvidenceRef> evidence)
    {
        var weakest = input;
        if (!evidence.IsDefaultOrEmpty)
        {
            foreach (var item in evidence)
            {
                if (item.Certainty > weakest)
                {
                    weakest = item.Certainty;
                }
            }
        }

        return weakest;
    }

    private static ImmutableArray<EvidenceRef> CreateModelEvidence(
        string subject, ImmutableArray<EvidenceRef> underlying, CertaintyLevel certainty)
    {
        var canonical = (underlying.IsDefault ? [] : underlying)
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var artifact = $"{ModelId}:{ModelVersion}";
        var payload = $"{subject}|{string.Join("|", canonical.Select(item => item.Id.Value))}";
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel, artifact, null, null, null, null, certainty, ModelId, ModelVersion, payload));
        return
        [
            new EvidenceRef(
                id, EvidenceKind.FrameworkModel, artifact, range: null, symbol: null, detail: payload,
                certainty, canonical, ModelId, ModelVersion),
        ];
    }
}
