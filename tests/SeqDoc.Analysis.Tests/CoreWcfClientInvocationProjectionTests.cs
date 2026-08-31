using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.Toolchains;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.CoreWcf;
using Xunit;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// Producer proof for the client-invocation admission added on top of issues #5/#7's client-boundary
/// facts: the real Roslyn Program Index and eligibility projector drive
/// <see cref="CoreWcfServiceModel"/>'s new client-invocation branch through <see cref="FrameworkModelHost"/>
/// against the real <c>ClientCallers.cs</c> fixture call sites, proving exact result-claim classification
/// (Discarded/ResultAssigned/ResultReturned/Unclaimed) for every supported shape, multiplicity for two
/// distinct occurrences of the same operation, and that the same-shaped negatives (ambiguous
/// interface-typed receiver, mismatched-contract client) fail closed through the same producer rather
/// than being hand-built. This is a sibling of <see cref="CoreWcfServiceModelProjectionTests"/>, scoped
/// entirely to client-invocation admission; the existing file's service-side admission tests are
/// untouched.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class CoreWcfClientInvocationProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj";
    private const string CallerTypeMetadataName = "CoreWcfServices.CalculatorClientCaller";
    private const string CalculatorContractMetadataName = "CoreWcfServices.ICalculatorService";
    private const string CalculatorSourceClientMetadataName = "CoreWcfServices.CalculatorSourceClient";
    private const string CalculatorGeneratedClientMetadataName = "CoreWcfServices.CalculatorGeneratedClient";

    [Fact]
    public async Task RealFixtureCallSitesProduceTheExactResultClaimForEachSupportedShape()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var invocations = framework.Facts.OfType<ServiceClientInvocationFact>().ToArray();

        AssertClaim(programIndex, invocations, "CallDiscarded", ClientInvocationResultClaimKind.Discarded, null);
        AssertClaim(programIndex, invocations, "CallAssigned", ClientInvocationResultClaimKind.ResultAssigned, "sum");
        AssertClaim(programIndex, invocations, "CallReturned", ClientInvocationResultClaimKind.ResultReturned, null);
        AssertClaim(programIndex, invocations, "CallUnclaimed", ClientInvocationResultClaimKind.Unclaimed, null);

        // Same-shaped lookalikes that must also classify Unclaimed, never Discarded/ResultAssigned:
        // stored to a field, discarded via `_ = ...`, and passed as an argument.
        AssertClaim(programIndex, invocations, "CallStoredToField", ClientInvocationResultClaimKind.Unclaimed, null);
        AssertClaim(programIndex, invocations, "CallDiscardAssignment", ClientInvocationResultClaimKind.Unclaimed, null);
        AssertClaim(programIndex, invocations, "CallPassedAsArgument", ClientInvocationResultClaimKind.Unclaimed, null);

        Assert.All(invocations, fact => Assert.False(fact.IsAwaited));
        Assert.All(invocations, fact => Assert.Equal(CalculatorContractMetadataName, fact.ServiceContractType));
        Assert.All(invocations, fact => Assert.True(
            fact.OperationName is "Add" or "SquareRoot",
            $"Unexpected operation name '{fact.OperationName}'."));
    }

    [Fact]
    public async Task TwoDistinctCallOccurrencesToTheSameOperationBothAdmitIndependentInvocations()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallTwice");
        var invocations = framework.Facts.OfType<ServiceClientInvocationFact>()
            .Where(fact => fact.CallerMethod == caller.Id)
            .ToArray();

        Assert.Equal(2, invocations.Length);
        Assert.All(invocations, fact => Assert.Equal("Add", fact.OperationName));
        Assert.All(invocations, fact => Assert.Equal(ClientInvocationResultClaimKind.ResultAssigned, fact.ResultClaim));
        // Each occurrence keeps its own distinct invocation-operation anchor and fact identity.
        Assert.Equal(2, invocations.Select(fact => fact.InvocationOperation.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, invocations.Select(fact => fact.Id.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(invocations, fact => fact.ResultBindingName == "first");
        Assert.Contains(invocations, fact => fact.ResultBindingName == "second");
    }

    [Fact]
    public async Task GeneratedClientCallSiteAdmitsAnInvocationClassifiedGeneratedClient()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallGeneratedClient");
        var invocation = Assert.Single(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);

        Assert.Equal(CalculatorGeneratedClientMetadataName, invocation.ClientType);
        Assert.Equal("Add", invocation.OperationName);
        Assert.Equal(CertaintyLevel.Exact, invocation.Certainty);

        // ServiceClientBoundaryFact is anchored per admitting client method (see CoreWcfServiceModel's
        // AnalyzeMethod), so CalculatorGeneratedClient's five ICalculatorService operations each
        // independently contribute a boundary fact with the same ClientTypeSymbol; the join only needs
        // every one of them to agree on GeneratedClient, exactly as ScenarioGraphBuilder's join does.
        var boundaries = framework.Facts.OfType<ServiceClientBoundaryFact>()
            .Where(fact => fact.ClientTypeSymbol == invocation.ClientTypeSymbol)
            .ToArray();
        Assert.NotEmpty(boundaries);
        Assert.All(boundaries, boundary => Assert.Equal(ServiceClientKind.GeneratedClient, boundary.ClientKind));
    }

    [Fact]
    public async Task FaultDeclaringOperationCallSiteAdmitsAnInvocationForSquareRoot()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallFaultDeclaringOperation");
        var invocation = Assert.Single(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);

        Assert.Equal("SquareRoot", invocation.OperationName);
        Assert.Equal(CalculatorSourceClientMetadataName, invocation.ClientType);

        // The declaration-only fault fact for the exact same operation symbol already exists from the
        // service-side admission (proven independently in CoreWcfServiceModelProjectionTests); the
        // invocation joins to it later by exact OperationSymbol identity in the Scenario Graph, not here.
        Assert.Contains(
            framework.Facts.OfType<ServiceFaultContractFact>(),
            fact => fact.OperationSymbol == invocation.OperationSymbol && fact.FaultType == "CoreWcfServices.NegativeSquareRootFault");
    }

    [Fact]
    public async Task AmbiguousInterfaceTypedReceiverNeverAdmitsAnInvocation()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallThroughInterfaceTypedReceiver");

        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);
    }

    [Fact]
    public async Task MismatchedContractClientCallNeverAdmitsAnInvocation()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallThroughMismatchedContractClient");

        // MismatchedContractClient derives ClientBase<ICalculatorService> but Echo implements the
        // separately admitted classic-family IClassicEchoService, which ClientBase was not constructed
        // with — the same-shaped foreign/mismatched-contract negative must fail closed through the real
        // producer, exactly like the existing client-boundary negative for this fixture type.
        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);
    }

    [Fact]
    public async Task RefParameterOverloadLookalikeNeverAdmitsAnInvocationThroughTheRealProducer()
    {
        // Real-producer proof for the exact signature/ref-kind boundary (mirrors
        // CoreWcfClientInvocationModelTests.NoAdmittedContractMatchingMemberNeverAdmitsAnInvocation's
        // hand-built empty-member-set substitute): CalculatorRefOverloadClient.Add(double, ref double)
        // is a real, compilable overload that is not the admitted contract operation's exact signature.
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallThroughRefParameterOverload");

        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);
    }

    [Fact]
    public async Task RealFixtureCallSiteProducesExactlyOneVisibleClientInvocationMessageThroughScenarioAndPlanner()
    {
        // Full producer-to-first-observable vertical proof: real source through the production Roslyn
        // projector, CoreWcfServiceModel, ScenarioGraphBuilder, and DocumentationPlanner, mirroring
        // CoreWcfServiceModelProjectionTests's equivalent full-vertical test for the service side.
        // A hand-built HttpEntryPointFact is the only non-Roslyn-producer wiring in this test — it only
        // roots the graph at the real caller method (this fixture's caller is a plain class method, not
        // an ASP.NET action), exactly like ScenarioTestFactory's own hand-built entry facts do for the
        // rest of this codebase's HTTP-rooted scenario tests; every client-invocation admission fact it
        // joins to is produced by the real Roslyn projector and CoreWcfServiceModel above.
        var (programIndex, behavior, framework, profile) = await AnalyzeFullPipelineAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallAssigned");
        var entryPointId = new EntryPointId("entry-point:v1:test:client-invocation-full-vertical");
        var entryFact = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:test:client-invocation-full-vertical"),
            Evidence = caller.Evidence,
            Certainty = CertaintyLevel.Exact,
            EntryPointId = entryPointId,
            RootMethod = caller.Id,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "test/call-assigned",
            OperationKey = "Test.CallAssigned",
        };
        framework = framework with { Facts = framework.Facts.Add(entryFact) };

        var request = new ScenarioAnalysisRequest(
            profile, programIndex, behavior, framework,
            new SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"));

        var graphSet = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(graphSet.Graphs, item => item.RootMethod == caller.Id);
        var node = Assert.Single(graph.Nodes, item => item.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, item => item.Kind == ScenarioNodeKind.MethodCall);
        Assert.Equal(CalculatorSourceClientMetadataName, node.Presentation?.ClientTypeName);
        Assert.Equal(CalculatorContractMetadataName, node.Presentation?.ContractTypeName);
        Assert.Equal("Add", node.Presentation?.CalledMemberName);
        Assert.Equal(ClientInvocationResultClaimKind.ResultAssigned, node.Presentation?.ResultClaimKind);
        Assert.Equal("sum", node.Presentation?.ResultBindingName);
        Assert.Equal(CertaintyLevel.Exact, node.Certainty);

        var plan = DocumentationPlanner.Plan(graph);
        var phrase = Assert.Single(plan.Wording.Phrases, item => item.Key == "client-operation-invocation");
        Assert.Equal(
            "The action calls CalculatorSourceClient.Add through the ICalculatorService service-client boundary; "
                + "the call result is assigned to sum.",
            phrase.Text);
        Assert.DoesNotContain("HTTP", phrase.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", phrase.Text, StringComparison.OrdinalIgnoreCase);

        var message = Assert.Single(plan.Diagram.Messages, item => item.Label == "Add");
        var participant = Assert.Single(plan.Diagram.Participants, item => item.Label == "CalculatorSourceClient");
        Assert.Equal(participant.Key, message.Target);
    }

    [Fact]
    public async Task TwoSequentialCallsToTheSameOperationOnAStraightLinePathAdmitTwoOrderedNodesRegardlessOfFrameworkFactOrder()
    {
        // Closes Risk #8's remaining gap: TwoDistinctClientInvocationCallSitesAdmitDeterministicallyRegardlessOfFrameworkFactInputOrder
        // (SeqDoc.Scenarios.Tests) only proves determinism across two *different* operations on two
        // mutually-exclusive branches of a guarded decision. This proves the narrower, same-operation,
        // straight-line case through the real ScenarioGraphBuilder/DocumentationPlanner pipeline:
        // CalculatorClientCaller.CallTwice calls client.Add(a, b) then client.Add(c, d) sequentially with
        // no branching, so the B3 fix that groups ServiceClientInvocationFacts by InvocationOperation must
        // never conflate these two genuinely distinct call sites into one node, and the resulting node
        // order/identity/evidence/certainty must not depend on the order framework facts are supplied in.
        var (programIndex, behavior, framework, profile) = await AnalyzeFullPipelineAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallTwice");
        var entryPointId = new EntryPointId("entry-point:v1:test:client-invocation-call-twice");
        var entryFact = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:test:client-invocation-call-twice"),
            Evidence = caller.Evidence,
            Certainty = CertaintyLevel.Exact,
            EntryPointId = entryPointId,
            RootMethod = caller.Id,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "test/call-twice",
            OperationKey = "Test.CallTwice",
        };
        var frameworkWithEntry = framework with { Facts = framework.Facts.Add(entryFact) };

        var forwardGraph = BuildCallTwiceGraph(programIndex, behavior, frameworkWithEntry, profile, caller.Id);
        var reversedFacts = frameworkWithEntry with { Facts = [.. frameworkWithEntry.Facts.Reverse()] };
        var reversedGraph = BuildCallTwiceGraph(programIndex, behavior, reversedFacts, profile, caller.Id);

        AssertCallTwiceGraphShape(forwardGraph);
        AssertCallTwiceGraphShape(reversedGraph);

        var forwardNodes = forwardGraph.Nodes.Where(node => node.Kind == ScenarioNodeKind.ClientOperationInvocation)
            .OrderBy(node => node.Presentation?.ResultBindingName, StringComparer.Ordinal)
            .ToArray();
        var reversedNodes = reversedGraph.Nodes.Where(node => node.Kind == ScenarioNodeKind.ClientOperationInvocation)
            .OrderBy(node => node.Presentation?.ResultBindingName, StringComparer.Ordinal)
            .ToArray();
        for (var i = 0; i < forwardNodes.Length; i++)
        {
            Assert.Equal(forwardNodes[i].Id, reversedNodes[i].Id);
            Assert.Equal(forwardNodes[i].Certainty, reversedNodes[i].Certainty);
            Assert.Equal(forwardNodes[i].Evidence.Length, reversedNodes[i].Evidence.Length);
            Assert.Equal(forwardNodes[i].Presentation?.ResultBindingName, reversedNodes[i].Presentation?.ResultBindingName);
        }

        // Rendered order must be source order (the `first`/(a,b) call before the `second`/(c,d) call)
        // regardless of the framework-fact input order.
        var forwardPlan = DocumentationPlanner.Plan(forwardGraph);
        var reversedPlan = DocumentationPlanner.Plan(reversedGraph);
        AssertCallTwiceMessageOrder(forwardPlan);
        AssertCallTwiceMessageOrder(reversedPlan);
    }

    private static ScenarioGraph BuildCallTwiceGraph(
        ProgramIndexSnapshot programIndex,
        SeqDoc.Core.Behavior.BehaviorSnapshot behavior,
        FrameworkAnalysisResult framework,
        CompilationProfile profile,
        MethodId callerId)
    {
        var request = new ScenarioAnalysisRequest(
            profile, programIndex, behavior, framework,
            new SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"));

        var graphSet = ScenarioGraphBuilder.Build(request);
        return Assert.Single(graphSet.Graphs, item => item.RootMethod == callerId);
    }

    private static void AssertCallTwiceGraphShape(ScenarioGraph graph)
    {
        var invocationNodes = graph.Nodes.Where(node => node.Kind == ScenarioNodeKind.ClientOperationInvocation).ToArray();
        Assert.Equal(2, invocationNodes.Length);
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.MethodCall);
        Assert.All(invocationNodes, node => Assert.Equal("Add", node.Presentation?.CalledMemberName));
        Assert.All(invocationNodes, node => Assert.Equal(CertaintyLevel.Exact, node.Certainty));
        Assert.Equal(2, invocationNodes.Select(node => node.Id.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(invocationNodes, node => node.Presentation?.ResultBindingName == "first");
        Assert.Contains(invocationNodes, node => node.Presentation?.ResultBindingName == "second");
    }

    private static void AssertCallTwiceMessageOrder(DocumentationPlan plan)
    {
        // plan.Diagram.Messages preserves the sequence-diagram rendering order; the "Add" label appears
        // exactly twice, and array position (not any explicit ordinal field) is the render order.
        var addIndexes = plan.Diagram.Messages
            .Select((message, index) => (message, index))
            .Where(item => item.message.Label == "Add")
            .Select(item => item.index)
            .ToArray();
        Assert.Equal(2, addIndexes.Length);
        Assert.True(addIndexes[0] < addIndexes[1], "Expected the 'first' (a,b) call to be rendered before the 'second' (c,d) call.");
    }

    // ---- Issue #41: measured net9.0 classic-WCF compatibility tuples through the real producer ----

    public static TheoryData<string, string> Net9CompatibilityFixtures() => new()
    {
        { "tests/fixtures/PassC/ClassicWcfNet9Compatibility/ClassicWcfNet9V800/ClassicWcfNet9V800.csproj", "ClassicWcfNet9V800" },
        { "tests/fixtures/PassC/ClassicWcfNet9Compatibility/ClassicWcfNet9V810/ClassicWcfNet9V810.csproj", "ClassicWcfNet9V810" },
    };

    [Theory]
    [MemberData(nameof(Net9CompatibilityFixtures))]
    public async Task Net9CompatibilityTupleGeneratedClientCallSiteAdmitsExactlyOneInvocationThroughTheRealProducer(
        string fixtureRelativePath,
        string rootNamespace)
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync(fixtureRelativePath, "net9.0");
        var caller = FindMethod(programIndex, $"{rootNamespace}.CalculatorCaller", "CallAdd");

        var invocation = Assert.Single(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);
        Assert.Equal($"{rootNamespace}.CalculatorClient", invocation.ClientType);
        Assert.Equal($"{rootNamespace}.ICalculatorClient", invocation.ServiceContractType);
        Assert.Equal("Add", invocation.OperationName);
        Assert.Equal(ClientInvocationResultClaimKind.ResultAssigned, invocation.ResultClaim);
        Assert.Equal("sum", invocation.ResultBindingName);
        Assert.Equal(CertaintyLevel.Exact, invocation.Certainty);

        var boundaries = framework.Facts.OfType<ServiceClientBoundaryFact>()
            .Where(fact => fact.ClientTypeSymbol == invocation.ClientTypeSymbol)
            .ToArray();
        Assert.NotEmpty(boundaries);
        Assert.All(boundaries, boundary => Assert.Equal(ServiceClientKind.GeneratedClient, boundary.ClientKind));

        // The net9.0 generated client type never admits ordinary service capability.
        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceOperationCapabilityFact>(),
            fact => fact.ImplementationType == $"{rootNamespace}.CalculatorClient");
    }

    [Theory]
    [MemberData(nameof(Net9CompatibilityFixtures))]
    public async Task Net9CompatibilityTupleCallSiteProducesExactlyOneVisibleClientInvocationMessageThroughScenarioAndPlanner(
        string fixtureRelativePath,
        string rootNamespace)
    {
        var (programIndex, behavior, framework, profile) = await AnalyzeFullPipelineAsync(fixtureRelativePath, "net9.0");
        var caller = FindMethod(programIndex, $"{rootNamespace}.CalculatorCaller", "CallAdd");
        var entryFact = new HttpEntryPointFact
        {
            Id = new BehaviorFactId($"behavior-fact:v1:test:net9-tuple-{rootNamespace}"),
            Evidence = caller.Evidence,
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId($"entry-point:v1:test:net9-tuple-{rootNamespace}"),
            RootMethod = caller.Id,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "test/net9-tuple",
            OperationKey = "Test.Net9Tuple",
        };
        framework = framework with { Facts = framework.Facts.Add(entryFact) };

        var request = new ScenarioAnalysisRequest(
            profile, programIndex, behavior, framework,
            new SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"));

        var graphSet = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(graphSet.Graphs, item => item.RootMethod == caller.Id);
        var node = Assert.Single(graph.Nodes, item => item.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, item => item.Kind == ScenarioNodeKind.MethodCall);
        Assert.Equal($"{rootNamespace}.CalculatorClient", node.Presentation?.ClientTypeName);
        Assert.Equal($"{rootNamespace}.ICalculatorClient", node.Presentation?.ContractTypeName);
        Assert.Equal("Add", node.Presentation?.CalledMemberName);
        Assert.Equal(ClientInvocationResultClaimKind.ResultAssigned, node.Presentation?.ResultClaimKind);
        Assert.Equal(CertaintyLevel.Exact, node.Certainty);

        var plan = DocumentationPlanner.Plan(graph);
        var message = Assert.Single(plan.Diagram.Messages, item => item.Label == "Add");
        var participant = Assert.Single(plan.Diagram.Participants, item => item.Label == "CalculatorClient");
        Assert.Equal(participant.Key, message.Target);
        var phrase = Assert.Single(plan.Wording.Phrases, item => item.Key == "client-operation-invocation");
        Assert.DoesNotContain("HTTP", phrase.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", phrase.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Net9UnattributedContractClientCallSiteNeverAdmitsAnythingThroughTheRealProducer()
    {
        // Issue #41 R2: same-shaped real-compilable negative on the changed boundary. UnattributedClient
        // derives the real 8.0.0.0 ClientBase<IUnattributedContract>, but IUnattributedContract carries
        // no [ServiceContract] so TryGetAdmittedContract resolves nothing — no invocation, no boundary,
        // no capability, and no ClientOperationInvocation scenario node or wording phrase.
        const string ns = "ClassicWcfNet9V800";
        var (programIndex, behavior, framework, profile) = await AnalyzeFullPipelineAsync(
            "tests/fixtures/PassC/ClassicWcfNet9Compatibility/ClassicWcfNet9V800/ClassicWcfNet9V800.csproj", "net9.0");
        var caller = FindMethod(programIndex, $"{ns}.UnattributedCaller", "Call");

        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);
        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceClientBoundaryFact>(),
            fact => fact.ClientType == $"{ns}.UnattributedClient");
        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceOperationCapabilityFact>(),
            fact => fact.ImplementationType == $"{ns}.UnattributedClient");

        // Positive control is unperturbed: CalculatorCaller.CallAdd still admits exactly one invocation.
        var positiveCaller = FindMethod(programIndex, $"{ns}.CalculatorCaller", "CallAdd");
        Assert.Single(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == positiveCaller.Id);

        var entryFact = new HttpEntryPointFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:test:net9-unattributed"),
            Evidence = caller.Evidence,
            Certainty = CertaintyLevel.Exact,
            EntryPointId = new EntryPointId("entry-point:v1:test:net9-unattributed"),
            RootMethod = caller.Id,
            HttpMethod = HttpMethodKind.Get,
            CanonicalRoute = "test/net9-unattributed",
            OperationKey = "Test.Net9Unattributed",
        };
        var frameworkWithEntry = framework with { Facts = framework.Facts.Add(entryFact) };

        var request = new ScenarioAnalysisRequest(
            profile, programIndex, behavior, frameworkWithEntry,
            new SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"));

        var graphSet = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(graphSet.Graphs, item => item.RootMethod == caller.Id);
        Assert.DoesNotContain(graph.Nodes, item => item.Kind == ScenarioNodeKind.ClientOperationInvocation);

        var plan = DocumentationPlanner.Plan(graph);
        Assert.DoesNotContain(plan.Wording.Phrases, item => item.Key == "client-operation-invocation");
    }

    [Fact]
    public async Task Net9CompatibilityTupleCallSiteRootedAsAConfiguredMethodProducesExactlyOneVisibleClientInvocationMessage()
    {
        // Issue #44: the configured-method root branch of ScenarioGraphBuilder must join a compiler-proven
        // ServiceClientInvocationFact exactly like the HTTP controller-action branch already does. This is
        // the real-producer vertical: CalculatorCaller.CallAdd is rooted via ConfiguredRoots (not an
        // HttpEntryPointFact), so the configured branch owns the whole join through DocumentationPlanner.
        const string ns = "ClassicWcfNet9V800";
        var (programIndex, behavior, framework, profile) = await AnalyzeFullPipelineAsync(
            "tests/fixtures/PassC/ClassicWcfNet9Compatibility/ClassicWcfNet9V800/ClassicWcfNet9V800.csproj", "net9.0");
        var caller = FindMethod(programIndex, $"{ns}.CalculatorCaller", "CallAdd");

        // The invocation fact is produced compilation-wide, independent of how the graph is rooted.
        Assert.Single(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);

        var request = new ScenarioAnalysisRequest(
            profile, programIndex, behavior, framework,
            new SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test"))
        {
            ConfiguredRoots = ImmutableArray.Create(caller.Id),
        };

        var graphSet = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(graphSet.Graphs, item => item.RootMethod == caller.Id);
        Assert.Equal(ScenarioRootKind.ConfiguredMethod, graph.RootKind);
        var node = Assert.Single(graph.Nodes, item => item.Kind == ScenarioNodeKind.ClientOperationInvocation);
        Assert.DoesNotContain(graph.Nodes, item => item.Kind == ScenarioNodeKind.MethodCall);
        Assert.Equal($"{ns}.CalculatorClient", node.Presentation?.ClientTypeName);
        Assert.Equal("Add", node.Presentation?.CalledMemberName);
        Assert.Equal(CertaintyLevel.Exact, node.Certainty);

        var plan = DocumentationPlanner.Plan(graph);
        var message = Assert.Single(plan.Diagram.Messages, item => item.Label == "Add");
        var participant = Assert.Single(plan.Diagram.Participants, item => item.Label == "CalculatorClient");
        Assert.Equal(participant.Key, message.Target);
        var phrase = Assert.Single(plan.Wording.Phrases, item => item.Key == "client-operation-invocation");
        Assert.DoesNotContain("HTTP", phrase.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertClaim(
        ProgramIndexSnapshot programIndex,
        ServiceClientInvocationFact[] invocations,
        string methodName,
        ClientInvocationResultClaimKind expectedClaim,
        string? expectedBindingName)
    {
        var caller = FindMethod(programIndex, CallerTypeMetadataName, methodName);
        var invocation = Assert.Single(invocations, fact => fact.CallerMethod == caller.Id);
        Assert.Equal(expectedClaim, invocation.ResultClaim);
        Assert.Equal(expectedBindingName, invocation.ResultBindingName);
        Assert.Equal(CertaintyLevel.Exact, invocation.Certainty);
        Assert.False(invocation.Evidence.IsDefaultOrEmpty);
    }

    private static ProgramMethod FindMethod(ProgramIndexSnapshot programIndex, string containingTypeMetadataName, string methodName)
    {
        var containingType = programIndex.Types.Single(type => type.MetadataName == containingTypeMetadataName);
        return programIndex.Methods.Single(method => method.ContainingType == containingType.Id && method.Name == methodName);
    }

    private static Task<(ProgramIndexSnapshot ProgramIndex, FrameworkAnalysisResult Framework)> AnalyzeFixtureAsync()
        => AnalyzeFixtureAsync(FixtureRelativePath, "net10.0");

    private static async Task<(ProgramIndexSnapshot ProgramIndex, FrameworkAnalysisResult Framework)> AnalyzeFixtureAsync(
        string fixtureRelativePath,
        string targetFramework)
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, fixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(fixtureRelativePath, "Release", targetFramework));
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));

        var behaviorResult = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(behaviorResult.IsSuccess);

        var host = new FrameworkModelHost([new CoreWcfServiceModel()]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(request.Profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(request.Profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        return (extraction.Value.ProgramIndex, framework);
    }

    private static Task<(ProgramIndexSnapshot ProgramIndex, SeqDoc.Core.Behavior.BehaviorSnapshot Behavior, FrameworkAnalysisResult Framework, CompilationProfile Profile)> AnalyzeFullPipelineAsync()
        => AnalyzeFullPipelineAsync(FixtureRelativePath, "net10.0");

    private static async Task<(ProgramIndexSnapshot ProgramIndex, SeqDoc.Core.Behavior.BehaviorSnapshot Behavior, FrameworkAnalysisResult Framework, CompilationProfile Profile)> AnalyzeFullPipelineAsync(
        string fixtureRelativePath,
        string targetFramework)
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, fixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(fixtureRelativePath, "Release", targetFramework));
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));

        var behaviorResult = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(
            behaviorResult.IsSuccess,
            string.Join(Environment.NewLine, behaviorResult.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var host = new FrameworkModelHost([new CoreWcfServiceModel()]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(request.Profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(request.Profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        return (extraction.Value.ProgramIndex, behaviorResult.Value!, framework, request.Profile);
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
