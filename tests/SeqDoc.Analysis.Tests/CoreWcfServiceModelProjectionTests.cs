using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
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
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.CoreWcf;
using Xunit;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// Producer proof for issue #5's compiler facts and issue #7's CoreWCF service model: the real Roslyn
/// Program Index and eligibility projector drive <see cref="CoreWcfServiceModel"/> through
/// <see cref="FrameworkModelHost"/> against the realistic CoreWCF fixture, proving the complete
/// capability/registration/fault/client-boundary admission chain end to end (not hand-built
/// intermediate facts), that every declared Issue #5 fact area (contracts, operations, implementations,
/// generated/source clients, endpoint metadata, faults) is produced from the same producer, and that the
/// metadata-only/generated and mixed-family negative boundaries fail closed through that same producer.
/// A separate test proves the full producer-to-Diagram chain: real source through the production Roslyn
/// projector, framework model, <see cref="ScenarioGraphBuilder"/>, and <see cref="DocumentationPlanner"/>.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class CoreWcfServiceModelProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj";
    private const string CalculatorServiceMetadataName = "CoreWcfServices.CalculatorService";
    private const string ExplicitCalculatorServiceMetadataName = "CoreWcfServices.ExplicitCalculatorService";
    private const string UtilityHelperMetadataName = "CoreWcfServices.UtilityHelper";
    private const string FakeServiceMetadataName = "CoreWcfServices.FakeService";
    private const string MixedFamilyServiceMetadataName = "CoreWcfServices.MixedFamilyService";
    private const string ClassicEchoServiceMetadataName = "CoreWcfServices.ClassicEchoService";
    private const string CalculatorSourceClientMetadataName = "CoreWcfServices.CalculatorSourceClient";
    private const string CalculatorGeneratedClientMetadataName = "CoreWcfServices.CalculatorGeneratedClient";
    private const string CalculatorContractMetadataName = "CoreWcfServices.ICalculatorService";
    private const string ClassicContractMetadataName = "CoreWcfServices.IClassicEchoService";

    [Fact]
    public async Task PassCFixtureCompilationAdmitsExactCapabilitiesFaultsAndClientBoundariesAndWithholdsNegativeBoundaries()
    {
        var (aggregate, _) = await AnalyzeFixtureAsync();

        Assert.True(aggregate.Recognized);
        var capabilities = aggregate.Facts.OfType<ServiceOperationCapabilityFact>().ToArray();
        var faults = aggregate.Facts.OfType<ServiceFaultContractFact>().ToArray();
        var clients = aggregate.Facts.OfType<ServiceClientBoundaryFact>().ToArray();

        // Positive: every admitted OperationContract operation on CalculatorService, ExplicitCalculatorService
        // (unregistered — see the vertical test for the registration boundary), and the classic-family
        // ClassicEchoService is present.
        foreach (var (implementation, contract, operation) in new[]
                 {
                     (CalculatorServiceMetadataName, CalculatorContractMetadataName, "Add"),
                     (CalculatorServiceMetadataName, CalculatorContractMetadataName, "Subtract"),
                     (CalculatorServiceMetadataName, CalculatorContractMetadataName, "Multiply"),
                     (CalculatorServiceMetadataName, CalculatorContractMetadataName, "Divide"),
                     (CalculatorServiceMetadataName, CalculatorContractMetadataName, "SquareRoot"),
                     (ExplicitCalculatorServiceMetadataName, CalculatorContractMetadataName, "Add"),
                     (ClassicEchoServiceMetadataName, ClassicContractMetadataName, "Echo"),
                 })
        {
            Assert.Contains(capabilities, fact =>
                fact.ImplementationType == implementation && fact.ServiceContractType == contract && fact.OperationName == operation);
        }

        // Fault metadata: SquareRoot's [FaultContract(typeof(NegativeSquareRootFault))].
        Assert.Contains(faults, fact =>
            fact.ServiceContractType == CalculatorContractMetadataName
            && fact.OperationName == "SquareRoot"
            && fact.FaultType == "CoreWcfServices.NegativeSquareRootFault");

        // Generated/source client boundaries.
        Assert.Contains(clients, fact => fact.ClientType == CalculatorSourceClientMetadataName && fact.ClientKind == ServiceClientKind.SourceClient);
        Assert.Contains(clients, fact => fact.ClientType == CalculatorGeneratedClientMetadataName && fact.ClientKind == ServiceClientKind.GeneratedClient);
        Assert.DoesNotContain(capabilities, fact => fact.ImplementationType is CalculatorSourceClientMetadataName or CalculatorGeneratedClientMetadataName);

        // Negative boundaries fail closed through the same producer: the sibling operation without
        // [OperationContract], IUtility's operation without [ServiceContract] on the interface, the
        // foreign-namespace lookalike, and the mixed-family pair never admit capability.
        Assert.DoesNotContain(capabilities, fact => fact.OperationName == "Modulo");
        Assert.DoesNotContain(capabilities, fact => fact.ImplementationType == UtilityHelperMetadataName);
        Assert.DoesNotContain(capabilities, fact => fact.ImplementationType == FakeServiceMetadataName);
        Assert.DoesNotContain(capabilities, fact => fact.ImplementationType == MixedFamilyServiceMetadataName);

        Assert.Equal(capabilities.Length, capabilities.Select(fact => fact.Id.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.All(capabilities, fact => Assert.Equal(CertaintyLevel.Exact, fact.Certainty));
    }

    [Fact]
    public async Task PassCFixtureCompilationProducesTheExactAddServiceEndpointRegistrationFromStartup()
    {
        var (aggregate, _) = await AnalyzeFixtureAsync();

        var registration = Assert.Single(aggregate.Facts.OfType<ServiceEndpointRegistrationFact>());
        Assert.Equal(CalculatorServiceMetadataName, registration.ImplementationType);
        Assert.Equal(CalculatorContractMetadataName, registration.ServiceContractType);
        Assert.Equal("CoreWCF.BasicHttpBinding", registration.BindingType);
        Assert.Equal("/CalculatorService/basicHttp", registration.Address);
        Assert.Equal(CertaintyLevel.Exact, registration.Certainty);
    }

    [Fact]
    public async Task RegisteredCapabilityAdmitsARootAndUnregisteredCapabilityProducesAConservativeDiagramDiagnostic()
    {
        var (programIndex, behavior, framework, profile) = await BuildPipelineInputsAsync();
        var graphSet = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile, programIndex, behavior, framework,
            new SeqDoc.Core.Semantics.SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new SeqDoc.Core.Semantics.DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new SeqDoc.Core.Semantics.StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new SeqDoc.Core.Semantics.NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test")));

        var addGraph = Assert.Single(graphSet.Graphs, graph => graph.OperationKey == $"{CalculatorContractMetadataName}.Add");
        Assert.Equal(ScenarioRootKind.HttpEntryPoint, addGraph.RootKind);
        var action = Assert.Single(addGraph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
        Assert.Equal(ScenarioActionKind.ServiceOperation, action.Presentation?.ActionKind);

        // SquareRoot shares CalculatorService/ICalculatorService's single AddServiceEndpoint registration
        // with Add/Subtract/Multiply/Divide (registration is keyed by implementation+contract type, not
        // per operation), so it is legitimately admitted as a root alongside them.
        Assert.Contains(graphSet.Graphs, graph => graph.OperationKey == $"{CalculatorContractMetadataName}.SquareRoot");
        Assert.DoesNotContain(graphSet.Graphs, graph => graph.RootMethod.Value.Contains("ExplicitCalculatorService", StringComparison.Ordinal));
        Assert.Contains(graphSet.Diagnostics, diagnostic =>
            diagnostic.Code == "SC-SERVICE-UNSUPPORTED-DISPATCH"
            && diagnostic.TechnicalCause.Contains("ExplicitCalculatorService", StringComparison.Ordinal)
            && diagnostic.TechnicalCause.Contains($"{CalculatorContractMetadataName}.Add", StringComparison.Ordinal));

        var plan = DocumentationPlanner.Plan(addGraph);
        Assert.Contains(plan.Wording.Phrases, phrase =>
            phrase.Text.Contains("Service contract operation entry point", StringComparison.Ordinal)
            && phrase.Text.Contains($"{CalculatorContractMetadataName}.Add", StringComparison.Ordinal));
        Assert.Contains(plan.Diagram.Participants, participant => participant.Label == "CalculatorService.Add");
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("HTTP", StringComparison.Ordinal));
    }

    private static async Task<(FrameworkAnalysisResult Aggregate, ProgramIndexSnapshot Index)> AnalyzeFixtureAsync()
    {
        var (programIndex, _, framework, _) = await BuildPipelineInputsAsync();
        return (framework, programIndex);
    }

    private static async Task<(ProgramIndexSnapshot ProgramIndex, SeqDoc.Core.Behavior.BehaviorSnapshot Behavior, FrameworkAnalysisResult Framework, CompilationProfile Profile)> BuildPipelineInputsAsync()
    {
        var request = CreateFixtureRequest();
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

    private static CompilationAnalysisRequest CreateFixtureRequest()
    {
        var root = FindRepositoryRoot();
        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0"));
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
