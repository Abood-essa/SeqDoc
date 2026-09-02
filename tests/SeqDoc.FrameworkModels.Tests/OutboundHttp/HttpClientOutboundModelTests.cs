using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels.OutboundHttp;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.OutboundHttp;

/// <summary>
/// Hand-built <see cref="OperationDescriptor"/> unit tests for <see cref="HttpClientOutboundModel"/>'s
/// admission logic (orchestrator resolution 1): request-kind mapping, atomic profile/assembly-version
/// crossing, the coarse <see cref="IFrameworkBehaviorModel.IsApplicable"/> gate, the single ordered
/// <c>SEQHTTP001</c> for recognized-but-unsupported shapes, and missing-required-field fail-closed.
/// Real-Roslyn closure of every identity row plus the unsupported-sibling / foreign-lookalike /
/// missing-identity negatives lives in <c>SeqDoc.Analysis.Tests/OutboundHttpProjectionTests.cs</c>.
/// These tests are HARD RED until the seven production files exist.
/// </summary>
public sealed class HttpClientOutboundModelTests
{
    private const string PublicKeyToken = "b03f5f7f11d50a3a";
    private const string ReturnType = "System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage>";
    private static readonly MethodId CallerMethod = new("method:v1:BehaviorDocumentation.OutboundHttp.SupportedRequests.Get");
    private static readonly OperationId InvocationOperationId = new("operation:v1:outbound-http:get");

    private static FrameworkMethodIdentity GetIdentity(string assemblyVersion = "10.0.0.0")
        => new(
            "System.Net.Http",
            "System.Net.Http.HttpClient",
            "GetAsync",
            0,
            [new(ParameterRefKind.None, "System.String")],
            ReturnType,
            assemblyVersion,
            PublicKeyToken);

    private static FrameworkMethodIdentity PostIdentity(string assemblyVersion = "10.0.0.0")
        => new(
            "System.Net.Http",
            "System.Net.Http.HttpClient",
            "PostAsync",
            0,
            [new(ParameterRefKind.None, "System.String"), new(ParameterRefKind.None, "System.Net.Http.HttpContent")],
            ReturnType,
            assemblyVersion,
            PublicKeyToken);

    private static OperationDescriptor Operation(
        FrameworkMethodIdentity? identity,
        ImmutableArray<int> suppliedOrdinals,
        CertaintyLevel certainty = CertaintyLevel.Exact,
        ImmutableArray<EvidenceRef> evidence = default)
        => new(
            InvocationOperationId,
            CallerMethod,
            "Invocation",
            new DocumentId("document:v1:outbound-http"),
            10,
            4,
            evidence.IsDefault ? [Evidence()] : evidence,
            certainty,
            TargetIdentity: identity,
            SuppliedParameterOrdinals: suppliedOrdinals);

    private static FrameworkAnalysisContext Context(string targetFramework = "net10.0")
    {
        var profile = CompilationProfile.Create(
            "tests/fixtures/BehaviorDocumentation/OutboundHttp/OutboundHttp.csproj", "Release", targetFramework);
        return new(profile, EmptyIndex(profile));
    }

    private static FrameworkDetectionContext Detection(string targetFramework)
    {
        var profile = CompilationProfile.Create(
            "tests/fixtures/BehaviorDocumentation/OutboundHttp/OutboundHttp.csproj", "Release", targetFramework);
        return new(profile, EmptyIndex(profile));
    }

    [Fact]
    public async Task ExactSupportedGetAndPostMapToTheTypedRequestKind()
    {
        var model = new HttpClientOutboundModel();

        var get = await model.AnalyzeOperationAsync(
            Operation(GetIdentity(), [0]), Context(), CancellationToken.None);
        var post = await model.AnalyzeOperationAsync(
            Operation(PostIdentity(), [0, 1]), Context(), CancellationToken.None);

        var getFact = Assert.Single(get.Facts.OfType<OutboundHttpRequestFact>());
        Assert.Equal(OutboundHttpRequestKind.Get, getFact.RequestKind);
        Assert.Equal(CallerMethod, getFact.CallerMethod);
        Assert.Equal(InvocationOperationId, getFact.InvocationOperation);
        Assert.Equal("GetAsync", getFact.FrameworkMethodIdentity.MethodMetadataName);
        Assert.Equal(PublicKeyToken, getFact.FrameworkMethodIdentity.AssemblyPublicKeyToken);
        Assert.Empty(get.Diagnostics);

        var postFact = Assert.Single(post.Facts.OfType<OutboundHttpRequestFact>());
        Assert.Equal(OutboundHttpRequestKind.Post, postFact.RequestKind);
        Assert.Empty(post.Diagnostics);
    }

    [Fact]
    public void IsApplicableIsTrueOnlyForExactNet9OrNet10()
    {
        var model = new HttpClientOutboundModel();

        Assert.True(model.IsApplicable(Detection("net9.0")));
        Assert.True(model.IsApplicable(Detection("net10.0")));
        Assert.False(model.IsApplicable(Detection("net8.0")));
        Assert.False(model.IsApplicable(Detection("netstandard2.0")));
        Assert.False(model.IsApplicable(Detection("net472")));
    }

    [Fact]
    public async Task AtomicProfileAssemblyVersionCrossingEmitsWrongAssemblyVersionSeqHttp001AndNoFact()
    {
        var model = new HttpClientOutboundModel();

        // The recognizable family (assembly name + token + type + method + arity) is fully present and
        // the profile is applicable, but the assembly version is wrong or missing. Version is NOT a
        // family-recognition component, so each case emits exactly one SEQHTTP001 with reason
        // wrong-assembly-version and no fact — never silence.
        var crossings = new[]
        {
            await model.AnalyzeOperationAsync(Operation(GetIdentity("10.0.0.0"), [0]), Context("net9.0"), CancellationToken.None),
            await model.AnalyzeOperationAsync(Operation(GetIdentity("9.0.0.0"), [0]), Context("net10.0"), CancellationToken.None),
            await model.AnalyzeOperationAsync(Operation(GetIdentity("9.0.5.0"), [0]), Context("net9.0"), CancellationToken.None),
            await model.AnalyzeOperationAsync(Operation(GetIdentity(assemblyVersion: null!), [0]), Context("net10.0"), CancellationToken.None),
        };

        foreach (var result in crossings)
        {
            Assert.Empty(result.Facts);
            var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == OutboundHttpDiagnosticCodes.DiagnosticCode);
            Assert.Contains("wrong-assembly-version", diagnostic.InternalDetail);
            Assert.Contains(InvocationOperationId.Value, diagnostic.InternalDetail);
        }

        // A genuinely foreign / partial identity (family cannot be established) still stays silent.
        var foreign = await model.AnalyzeOperationAsync(
            Operation(GetIdentity("10.0.0.0") with { AssemblyIdentity = "Contoso.Net.Http", AssemblyPublicKeyToken = "0123456789abcdef" }, [0]),
            Context("net9.0"), CancellationToken.None);
        Assert.Empty(foreign.Facts);
        Assert.DoesNotContain(foreign.Diagnostics, d => d.Code == OutboundHttpDiagnosticCodes.DiagnosticCode);

        // Exact match for the matching profile still admits.
        var net9Ok = await model.AnalyzeOperationAsync(
            Operation(GetIdentity("9.0.0.0"), [0]), Context("net9.0"), CancellationToken.None);
        Assert.Single(net9Ok.Facts.OfType<OutboundHttpRequestFact>());
    }

    [Fact]
    public async Task RecognizedButUnsupportedShapeEmitsExactlyOneOrderedSeqHttp001AndNoFact()
    {
        var model = new HttpClientOutboundModel();

        // Full recognizable family identity (assembly + token + type + GetAsync/PostAsync/SendAsync + arity)
        // but a shape that is not an admitted row.
        var sendAsync = GetIdentity() with { MethodMetadataName = "SendAsync", Parameters = [new(ParameterRefKind.None, "System.Net.Http.HttpRequestMessage")] };
        var uriOverload = GetIdentity() with { Parameters = [new(ParameterRefKind.None, "System.Uri")] };
        var cancellationOverload = GetIdentity() with
        {
            Parameters = [new(ParameterRefKind.None, "System.String"), new(ParameterRefKind.None, "System.Threading.CancellationToken")],
        };
        var completionOverload = GetIdentity() with
        {
            Parameters = [new(ParameterRefKind.None, "System.String"), new(ParameterRefKind.None, "System.Net.Http.HttpCompletionOption")],
        };
        var wrongReturn = GetIdentity() with { ReturnType = "System.Threading.Tasks.Task<System.String>" };
        var wrongRefKind = GetIdentity() with { Parameters = [new(ParameterRefKind.Ref, "System.String")] };
        var mismatchedOrdinals = GetIdentity();

        var descriptors = new (FrameworkMethodIdentity Identity, ImmutableArray<int> Ordinals)[]
        {
            (sendAsync, [0]),
            (uriOverload, [0]),
            (cancellationOverload, [0, 1]),
            (completionOverload, [0, 1]),
            (wrongReturn, [0]),
            (wrongRefKind, [0]),
            (mismatchedOrdinals, [0, 1]),
        };

        foreach (var (identity, ordinals) in descriptors)
        {
            var result = await model.AnalyzeOperationAsync(
                Operation(identity, ordinals), Context(), CancellationToken.None);
            Assert.Empty(result.Facts);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(OutboundHttpDiagnosticCodes.DiagnosticCode, diagnostic.Code);
            Assert.Contains(InvocationOperationId.Value, diagnostic.InternalDetail);
        }

        // Reversed input order produces the same single-diagnostic-per-operation outcome (deterministic).
        foreach (var (identity, ordinals) in descriptors.Reverse())
        {
            var result = await model.AnalyzeOperationAsync(
                Operation(identity, ordinals), Context(), CancellationToken.None);
            Assert.Single(result.Diagnostics, d => d.Code == OutboundHttpDiagnosticCodes.DiagnosticCode);
        }
    }

    [Fact]
    public async Task PartialIdentityForeignLookalikeAndMissingRequiredFieldsFailClosedSilently()
    {
        var model = new HttpClientOutboundModel();

        // Foreign assembly / partial identity: family cannot be established, so no diagnostic at all.
        var foreignAssembly = GetIdentity() with { AssemblyIdentity = "Contoso.Net.Http", AssemblyPublicKeyToken = "0123456789abcdef" };
        var missingToken = GetIdentity() with { AssemblyPublicKeyToken = null };
        var missingAssemblyName = GetIdentity() with { AssemblyIdentity = "" };
        var missingContainingType = GetIdentity() with { ContainingMetadataType = "" };
        var wrongArity = GetIdentity() with { GenericArity = 1 };

        foreach (var identity in new[] { foreignAssembly, missingToken, missingAssemblyName, missingContainingType, wrongArity })
        {
            var result = await model.AnalyzeOperationAsync(
                Operation(identity, [0]), Context(), CancellationToken.None);
            Assert.Empty(result.Facts);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == OutboundHttpDiagnosticCodes.DiagnosticCode);
        }

        // Missing target identity entirely, missing supplied-ordinal projection, empty evidence,
        // unknown certainty: no fact, fail closed.
        Assert.Empty((await model.AnalyzeOperationAsync(
            Operation(identity: null, [0]), Context(), CancellationToken.None)).Facts);
        // A missing supplied-ordinal projection is a missing required admission field: it fails closed
        // fully silent — no fact AND no SEQHTTP001 (never coerced into a mismatched-ordinals diagnostic).
        var missingOrdinals = await model.AnalyzeOperationAsync(
            Operation(GetIdentity(), suppliedOrdinals: default), Context(), CancellationToken.None);
        Assert.Empty(missingOrdinals.Facts);
        Assert.DoesNotContain(missingOrdinals.Diagnostics, d => d.Code == OutboundHttpDiagnosticCodes.DiagnosticCode);
        Assert.Empty((await model.AnalyzeOperationAsync(
            Operation(GetIdentity(), [0], evidence: ImmutableArray<EvidenceRef>.Empty), Context(), CancellationToken.None)).Facts);
        Assert.Empty((await model.AnalyzeOperationAsync(
            Operation(GetIdentity(), [0], certainty: CertaintyLevel.Unknown), Context(), CancellationToken.None)).Facts);
    }

    [Fact]
    public async Task Seqhttp001IdentityIsReasonIndependentForTheSameProfileAndOperation()
    {
        var model = new HttpClientOutboundModel();

        // Same profile + same operation subject, two different unsupported reasons (send-async vs a
        // wrong-return-type GetAsync). The explanatory reason must not contribute to the diagnostic ID.
        var sendAsync = GetIdentity() with
        {
            MethodMetadataName = "SendAsync",
            Parameters = [new(ParameterRefKind.None, "System.Net.Http.HttpRequestMessage")],
        };
        var wrongReturn = GetIdentity() with { ReturnType = "System.Threading.Tasks.Task<System.String>" };

        var first = Assert.Single((await model.AnalyzeOperationAsync(
            Operation(sendAsync, [0]), Context(), CancellationToken.None)).Diagnostics);
        var second = Assert.Single((await model.AnalyzeOperationAsync(
            Operation(wrongReturn, [0]), Context(), CancellationToken.None)).Diagnostics);

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.InternalDetail, second.InternalDetail);
    }

    [Fact]
    public async Task AnalyzeOperationHonoursCancellationBeforeEmittingAnyFact()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new HttpClientOutboundModel().AnalyzeOperationAsync(
                Operation(GetIdentity(), [0]), Context(), cts.Token));
    }

    private static ProgramIndexSnapshot EmptyIndex(CompilationProfile profile)
        => new(
            1, "test", profile, Projects: [], Documents: [], Namespaces: [], Types: [], Members: [], Methods: [],
            Attributes: [], References: [], Invocations: [], InventoryMarkers: [], Diagnostics: [],
            InputManifestHash: "input", IndexFingerprint: "fingerprint");

    private static EvidenceRef Evidence()
    {
        var document = new DocumentId("document:v1:outbound-http");
        var range = new SourceRange(document, new SourcePosition(0, 0), new SourcePosition(0, 4));
        return new EvidenceRef(
            new EvidenceId("evidence:v1:outbound-http"), EvidenceKind.Source, "SupportedRequests.cs",
            range, "BehaviorDocumentation.OutboundHttp.SupportedRequests.Get", null, CertaintyLevel.Exact);
    }
}
