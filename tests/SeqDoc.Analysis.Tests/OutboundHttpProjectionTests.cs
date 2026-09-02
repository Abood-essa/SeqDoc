using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.Toolchains;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.OutboundHttp;
using Xunit;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// Producer-discipline closure for issue 54: real Roslyn <see cref="FrameworkModelHost"/> +
/// <c>CSharpCompilation</c> of the <c>OutboundHttp</c> fixture for both reference packs
/// (<c>net9.0</c>/<c>net10.0</c>), mirroring <see cref="CoreWcfClientInvocationProjectionTests"/>.
/// Every supported identity row, every recognized-unsupported sibling, the foreign <c>extern alias</c>
/// lookalike, and the missing-identity close are proven here through the real projector — not
/// hand-built descriptors. HARD RED until the seven production files exist.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class OutboundHttpProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/OutboundHttp/OutboundHttp.csproj";
    private const string SupportedType = "BehaviorDocumentation.OutboundHttp.SupportedRequests";
    private const string UnsupportedType = "BehaviorDocumentation.OutboundHttp.UnsupportedRequests";
    private const string LookalikeType = "BehaviorDocumentation.OutboundHttp.LookalikeCalls";
    private const string ModelId = "seqdoc.system-net-http.outbound";
    private const string ModelVersion = "1.0.0";
    private const string PublicKeyToken = "b03f5f7f11d50a3a";
    private const string ReturnType = "System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage>";

    public static TheoryData<string, string> SupportedProfiles() => new()
    {
        { "net9.0", "9.0.0.0" },
        { "net10.0", "10.0.0.0" },
    };

    [Theory]
    [MemberData(nameof(SupportedProfiles))]
    public async Task RealRoslynFixtureProjectsExactFactsAndEvidence(string targetFramework, string assemblyVersion)
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync(targetFramework);
        var facts = framework.Facts.OfType<OutboundHttpRequestFact>().ToArray();

        var getMethod = FindMethod(programIndex, SupportedType, "Get");
        var postMethod = FindMethod(programIndex, SupportedType, "Post");

        var get = Assert.Single(facts, fact => fact.CallerMethod == getMethod.Id);
        var post = Assert.Single(facts, fact => fact.CallerMethod == postMethod.Id);
        Assert.Equal(OutboundHttpRequestKind.Get, get.RequestKind);
        Assert.Equal(OutboundHttpRequestKind.Post, post.RequestKind);

        AssertIdentityRow(get.FrameworkMethodIdentity, "GetAsync", assemblyVersion, expectedParamCount: 1);
        AssertIdentityRow(post.FrameworkMethodIdentity, "PostAsync", assemblyVersion, expectedParamCount: 2);

        foreach (var fact in new[] { get, post })
        {
            Assert.False(fact.Evidence.IsDefaultOrEmpty);
            // Certainty is never strengthened across the projection stage.
            Assert.True(fact.Certainty <= CertaintyLevel.Exact);
        }

        var descriptor = Assert.Single(
            framework.AppliedModels, model => model.ModelId == ModelId);
        Assert.Equal(ModelVersion, descriptor.Version);
        Assert.Equal(programIndex.IndexFingerprint, framework.ProgramIndexFingerprint);

        // Negatives through the same producer.
        Assert.DoesNotContain(facts, fact => fact.CallerMethod.Value.Contains(UnsupportedType, StringComparison.Ordinal));
        Assert.DoesNotContain(facts, fact => fact.CallerMethod.Value.Contains(LookalikeType, StringComparison.Ordinal));

        foreach (var unsupported in new[]
                 {
                     "SendAsyncSibling", "GetAsyncUriOverload",
                     "GetAsyncCancellationTokenOverload", "GetAsyncCompletionOptionOverload",
                 })
        {
            var caller = FindMethod(programIndex, UnsupportedType, unsupported);
            var diagnostics = framework.Diagnostics
                .Where(d => d.Code == OutboundHttpDiagnosticCodes.DiagnosticCode
                    && (d.InternalDetail ?? string.Empty).Contains(caller.Name, StringComparison.Ordinal))
                .ToArray();
            Assert.Single(diagnostics);
        }

        // The foreign extern-alias lookalike calls contribute no SEQHTTP001 at all: exactly the four
        // recognized-but-unsupported BCL siblings are diagnosed (a bare "Get"/"Post" method-name
        // substring would otherwise collide with "GetAsync*"/"PostAsync*" diagnostic detail).
        Assert.Equal(4, framework.Diagnostics.Count(d => d.Code == OutboundHttpDiagnosticCodes.DiagnosticCode));
        foreach (var lookalike in new[] { "Get", "Post" })
        {
            var caller = FindMethod(programIndex, LookalikeType, lookalike);
            Assert.DoesNotContain(facts, fact => fact.CallerMethod == caller.Id);
        }
    }

    [Fact]
    public async Task TypedHttpNodeDoesNotDuplicateGenericDirectCall()
    {
        // Run the real pipeline twice through ScenarioGraphBuilder.Build: once with the outbound-HTTP
        // model registered and once with an empty model list.
        var withModel = await AnalyzeScenarioGraphsAsync("net10.0", [new HttpClientOutboundModel()]);
        var baseline = await AnalyzeScenarioGraphsAsync("net10.0", []);

        var getMethod = FindMethod(withModel.ProgramIndex, SupportedType, "Get");
        var httpGraph = Assert.Single(withModel.Graphs, g => g.RootMethod == getMethod.Id);

        // (a) With the model, the supported HTTP operation produces exactly one OutboundHttpRequest
        // node and NO generic MethodCall node for that same operation id.
        var httpNode = Assert.Single(httpGraph.Nodes, n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);
        Assert.Equal(OutboundHttpRequestKind.Get, httpNode.Presentation?.OutboundHttpRequestKind);
        var httpOperation = httpNode.Operation!.Value.Value;
        Assert.DoesNotContain(
            httpGraph.Nodes,
            n => n.Kind == ScenarioNodeKind.MethodCall && n.Operation == httpNode.Operation);

        // Without the model, that same operation never becomes an OutboundHttpRequest node.
        Assert.DoesNotContain(
            baseline.Graphs.SelectMany(g => g.Nodes),
            n => n.Kind == ScenarioNodeKind.OutboundHttpRequest);

        // (b) Enabling the HTTP model changes nothing for any operation OTHER than the HTTP one:
        // every scenario diagnostic that does not reference the HTTP operation id is identical
        // (same codes, same anchors/detail) between the two runs. The plain Describe -> Format
        // in-fixture call is the unrelated subject.
        static string[] UnrelatedDiagnostics(IEnumerable<ScenarioGraph> graphs, string httpOperation)
            => graphs
                .SelectMany(g => g.Diagnostics)
                .Where(d => d.Code != "SC-HTTP-CONFLICT"
                    && !(d.Detail ?? string.Empty).Contains(httpOperation, StringComparison.Ordinal))
                .Select(d => d.Code + "|" + (d.Detail ?? string.Empty))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            UnrelatedDiagnostics(baseline.Graphs, httpOperation),
            UnrelatedDiagnostics(withModel.Graphs, httpOperation));
    }

    [Fact]
    public async Task ResolvedHttpRootRetainsItsRootLocalOutboundHttpFact()
    {
        var result = await AnalyzeScenarioGraphsAsync("net10.0", [new HttpClientOutboundModel()]);
        var root = FindMethod(result.ProgramIndex, "BehaviorDocumentation.OutboundHttp.ResolvedHttpRoot", "GetWithResolvedService");
        var serviceType = "BehaviorDocumentation.OutboundHttp.IResolvedDependency";
        var serviceCall = result.Behavior.CallGraph.CallSites.Single(site =>
            site.ContainingMethod == root.Id && site.DeclaredTarget is { } target
            && target == FindMethod(result.ProgramIndex, serviceType, "Execute").Id);
        Assert.Equal(serviceType, result.ProgramIndex.Types.Single(type =>
            type.Id == result.ProgramIndex.Methods.Single(method => method.Id == serviceCall.DeclaredTarget!.Value).ContainingType).MetadataName);
        Assert.Contains(FindMethod(result.ProgramIndex,
            "BehaviorDocumentation.OutboundHttp.ResolvedDependency", "Execute").Id,
            serviceCall.Resolution.Candidates);

        var registration = new DependencyInjectionRegistrationFact(
            new SemanticFactId("semantic-fact:v1:test:resolved-http-registration"),
            root.Id, serviceCall.InvocationOperation, serviceType,
            "BehaviorDocumentation.OutboundHttp.ResolvedDependency",
            DependencyInjectionLifetime.Singleton, root.Evidence, CertaintyLevel.Exact);
        var binding = new DependencyInjectionBindingFact(
            new SemanticFactId("semantic-fact:v1:test:resolved-http-binding"),
            result.ProgramIndex.Methods.Single(method => method.Name == ".ctor"
                && method.ContainingType == root.ContainingType).Id,
            0, "dependency", serviceType, registration.Id, serviceType,
            "BehaviorDocumentation.OutboundHttp.ResolvedDependency",
            DependencyInjectionLifetime.Singleton, root.Evidence, CertaintyLevel.Exact);
        var request = new ScenarioAnalysisRequest(
            result.Profile, result.ProgramIndex, result.Behavior,
            result.FrameworkFacts with { Facts = result.FrameworkFacts.Facts.Add(EntryFact(root, "resolved")) },
            new SemanticFactSet(1, "test", result.Profile, result.ProgramIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", result.Profile, result.ProgramIndex.IndexFingerprint,
                [registration], [binding], [], "di-test"),
            new StructuralResultFactSet(1, "test", result.Profile, result.ProgramIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", result.Profile, result.ProgramIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"));

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs, item => item.RootMethod == root.Id);
        Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.ServiceCall);
        var outbound = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.OutboundHttpRequest);
        Assert.Equal(OutboundHttpRequestKind.Get, outbound.Presentation?.OutboundHttpRequestKind);
    }

    [Fact]
    public async Task ForeignFrameworkProfileAndFingerprintCannotJoinScenarioGraph()
    {
        var net9 = await AnalyzeScenarioGraphsAsync("net9.0", [new HttpClientOutboundModel()]);
        var net10 = await AnalyzeScenarioGraphsAsync("net10.0", [new HttpClientOutboundModel()]);
        var get = FindMethod(net9.ProgramIndex, SupportedType, "Get");
        var request = new ScenarioAnalysisRequest(
            net9.Profile, net9.ProgramIndex, net9.Behavior, net10.FrameworkFacts,
            new SemanticFactSet(1, "test", net9.Profile, net9.ProgramIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", net9.Profile, net9.ProgramIndex.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", net9.Profile, net9.ProgramIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", net9.Profile, net9.ProgramIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"))
        {
            ConfiguredRoots = [get.Id],
        };

        var graphs = ScenarioGraphBuilder.Build(request).Graphs;
        var graph = Assert.Single(graphs, item => item.RootMethod == get.Id);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.OutboundHttpRequest);
    }

    private static async Task<(ProgramIndexSnapshot ProgramIndex, ImmutableArray<ScenarioGraph> Graphs, BehaviorSnapshot Behavior, FrameworkAnalysisResult FrameworkFacts, CompilationProfile Profile)>
        AnalyzeScenarioGraphsAsync(string targetFramework, IReadOnlyList<IFrameworkBehaviorModel> models)
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", targetFramework));
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(d => $"{d.Code}: {d.TechnicalCause}\n{d.InternalDetail}")));

        var behaviorResult = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(behaviorResult.IsSuccess);

        var host = new FrameworkModelHost([.. models]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(request.Profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(request.Profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        var programIndex = extraction.Value.ProgramIndex;
        var getMethod = FindMethod(programIndex, SupportedType, "Get");
        var describeMethod = FindMethod(programIndex, SupportedType, "Describe");
        var frameworkWithEntries = framework with
        {
            Facts = framework.Facts
                .Add(EntryFact(getMethod, "get"))
                .Add(EntryFact(describeMethod, "describe")),
        };

        var profile = request.Profile;
        var scenarioRequest = new ScenarioAnalysisRequest(
            profile, programIndex, behaviorResult.Value!, frameworkWithEntries,
            new SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"));

        return (programIndex, ScenarioGraphBuilder.Build(scenarioRequest).Graphs, behaviorResult.Value!, framework, profile);
    }

    private static HttpEntryPointFact EntryFact(ProgramMethod method, string slug)
        => new()
        {
            Id = new BehaviorFactId($"behavior-fact:v1:test:outbound-http-{slug}"),
            Evidence = method.Evidence,
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId($"entry-point:v1:test:outbound-http-{slug}"),
            RootMethod = method.Id,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = $"test/outbound-http-{slug}",
            OperationKey = $"Test.OutboundHttp{slug}",
        };

    [Fact]
    public async Task ForeignProfileFingerprintAndProjectCannotJoin()
    {
        var (net9Index, net9Framework) = await AnalyzeFixtureAsync("net9.0");
        var (net10Index, net10Framework) = await AnalyzeFixtureAsync("net10.0");

        var net9Get = Assert.Single(net9Framework.Facts.OfType<OutboundHttpRequestFact>(),
            f => f.CallerMethod == FindMethod(net9Index, SupportedType, "Get").Id);
        var net10Get = Assert.Single(net10Framework.Facts.OfType<OutboundHttpRequestFact>(),
            f => f.CallerMethod == FindMethod(net10Index, SupportedType, "Get").Id);

        // A net9 fact never carries the net10 assembly version and vice versa — the profile-atomic
        // identity row is what a downstream request context joins on.
        Assert.Equal("9.0.0.0", net9Get.FrameworkMethodIdentity.AssemblyVersion);
        Assert.Equal("10.0.0.0", net10Get.FrameworkMethodIdentity.AssemblyVersion);
        Assert.NotEqual(net9Framework.ProgramIndexFingerprint, net10Framework.ProgramIndexFingerprint);
        Assert.NotEqual(net9Get.FrameworkMethodIdentity, net10Get.FrameworkMethodIdentity);
    }

    [Fact]
    public async Task RealFixtureGetCallProducesExactlyOneVisibleOutboundHttpMessageThroughScenarioAndPlanner()
    {
        var (programIndex, behavior, framework, profile) = await AnalyzeFullPipelineAsync("net10.0");
        var getMethod = FindMethod(programIndex, SupportedType, "Get");

        var fact = Assert.Single(
            framework.Facts.OfType<OutboundHttpRequestFact>(), f => f.CallerMethod == getMethod.Id);
        Assert.Equal(OutboundHttpRequestKind.Get, fact.RequestKind);

        var entryFact = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:test:outbound-http-get"),
            Evidence = getMethod.Evidence,
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:test:outbound-http-get"),
            RootMethod = getMethod.Id,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "test/outbound-http-get",
            OperationKey = "Test.OutboundHttpGet",
        };
        var frameworkWithEntry = framework with { Facts = framework.Facts.Add(entryFact) };

        var request = new ScenarioAnalysisRequest(
            profile, programIndex, behavior, frameworkWithEntry,
            new SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"));

        var graph = Assert.Single(
            ScenarioGraphBuilder.Build(request).Graphs, item => item.RootMethod == getMethod.Id);

        var node = Assert.Single(graph.Nodes, item => item.Kind == ScenarioNodeKind.OutboundHttpRequest);
        Assert.Equal(OutboundHttpRequestKind.Get, node.Presentation?.OutboundHttpRequestKind);
        Assert.Equal(getMethod.Id, node.Method);
        Assert.False(node.Evidence.IsDefaultOrEmpty);
        var edge = Assert.Single(graph.Edges, e => e.Target == node.Id && e.Kind == ScenarioEdgeKind.Call);
        Assert.True(node.Certainty >= edge.Evidence.Max(item => item.Certainty));

        var plan = DocumentationPlanner.Plan(graph);
        var phrase = Assert.Single(plan.Wording.Phrases, p => p.Key == "outbound-http-request");
        Assert.Equal(
            "The method calls HttpClient.GetAsync at an outbound HTTP GET request boundary.", phrase.Text);
        var message = Assert.Single(plan.Diagram.Messages, m => m.Label == "HTTP GET request");
        var participant = Assert.Single(plan.Diagram.Participants, p => p.Label == "HTTP boundary");
        Assert.Equal(participant.Key, message.Target);
    }

    private static async Task<(ProgramIndexSnapshot ProgramIndex, BehaviorSnapshot Behavior, FrameworkAnalysisResult Framework, CompilationProfile Profile)>
        AnalyzeFullPipelineAsync(string targetFramework)
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", targetFramework));
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(d => $"{d.Code}: {d.TechnicalCause}\n{d.InternalDetail}")));

        var behaviorResult = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(behaviorResult.IsSuccess);

        var host = new FrameworkModelHost([new HttpClientOutboundModel()]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(request.Profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(request.Profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        return (extraction.Value.ProgramIndex, behaviorResult.Value!, framework, request.Profile);
    }

    private static void AssertIdentityRow(
        FrameworkMethodIdentity identity, string methodName, string assemblyVersion, int expectedParamCount)
    {
        Assert.Equal("System.Net.Http.HttpClient", identity.ContainingMetadataType);
        Assert.Equal("System.Net.Http", identity.AssemblyIdentity);
        Assert.Equal(PublicKeyToken, identity.AssemblyPublicKeyToken);
        Assert.Equal(methodName, identity.MethodMetadataName);
        Assert.Equal(0, identity.GenericArity);
        Assert.Equal(assemblyVersion, identity.AssemblyVersion);
        Assert.Equal(ReturnType, identity.ReturnType);
        Assert.Equal(expectedParamCount, identity.Parameters.Length);
        Assert.All(identity.Parameters, p => Assert.Equal(ParameterRefKind.None, p.RefKind));
    }

    private static ProgramMethod FindMethod(ProgramIndexSnapshot programIndex, string containingTypeMetadataName, string methodName)
    {
        var containingType = programIndex.Types.Single(type => type.MetadataName == containingTypeMetadataName);
        return programIndex.Methods.Single(method => method.ContainingType == containingType.Id && method.Name == methodName);
    }

    private static Task<(ProgramIndexSnapshot ProgramIndex, FrameworkAnalysisResult Framework)> AnalyzeFixtureAsync(string targetFramework)
        => AnalyzeFixtureAsync(targetFramework, [new HttpClientOutboundModel()]);

    private static async Task<(ProgramIndexSnapshot ProgramIndex, FrameworkAnalysisResult Framework)> AnalyzeFixtureAsync(
        string targetFramework,
        IReadOnlyList<IFrameworkBehaviorModel> models)
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", targetFramework));
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(d => $"{d.Code}: {d.TechnicalCause}\n{d.InternalDetail}")));

        var behaviorResult = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(behaviorResult.IsSuccess);

        var host = new FrameworkModelHost([.. models]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(request.Profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(request.Profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        return (extraction.Value.ProgramIndex, framework);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
