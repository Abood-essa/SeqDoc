using System.Collections.Immutable;
using System.Diagnostics;
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

        // Exactly one registration fact: the real, admitted Startup.Configure -> UseServiceModel ->
        // AddService<CalculatorService>().AddServiceEndpoint<CalculatorService,ICalculatorService>(...)
        // chain. UnusedRegistrationHelper.NeverCalled's exact same-shaped AddServiceEndpoint call (never
        // reachable from the admitted Configure method) and UnusedStartup's entire disconnected
        // Configure/UseServiceModel callback (never selected by any UseStartup<T>()) both exist in real
        // compiled source with the identical compiler-proven registration shape, yet neither contributes
        // a fact here — proving the host-chain gate, not mere textual presence, decides admission.
        var registration = Assert.Single(aggregate.Facts.OfType<ServiceEndpointRegistrationFact>());
        Assert.Equal(CalculatorServiceMetadataName, registration.ImplementationType);
        Assert.Equal(CalculatorContractMetadataName, registration.ServiceContractType);
        Assert.Equal("CoreWCF.BasicHttpBinding", registration.BindingType);
        Assert.Equal("/CalculatorService/basicHttp", registration.Address);
        Assert.Equal(CertaintyLevel.Exact, registration.Certainty);
    }

    [Fact]
    public async Task ConfiguredHostChainWithoutBuildOrRunProducesNoRegistrationOrRoot()
    {
        var (programIndex, behavior, framework, profile) = await BuildPipelineInputsAsync();

        // The fixture contains both the executed Program chain and a complete, same-shaped chain that
        // only returns an IHostBuilder. The latter must not promote configuration into registration or
        // an executable service-operation root.
        var registrations = framework.Facts.OfType<ServiceEndpointRegistrationFact>().ToArray();
        Assert.Single(registrations);
        Assert.DoesNotContain(registrations, fact => fact.Address == "/CalculatorService/unbuilt");

        var graphSet = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile, programIndex, behavior, framework,
            new SeqDoc.Core.Semantics.SemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
            new SeqDoc.Core.Semantics.DependencyInjectionFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "di-test"),
            new SeqDoc.Core.Semantics.StructuralResultFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], "structural-test"),
            new SeqDoc.Core.Semantics.NonGetSemanticFactSet(1, "test", profile, programIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test")));

        Assert.DoesNotContain(graphSet.Graphs, graph => graph.RootMethod.Value.Contains("UnbuiltStartup", StringComparison.Ordinal));
        Assert.Contains(graphSet.Graphs, graph => graph.OperationKey == $"{CalculatorContractMetadataName}.Add");
    }

    [Fact]
    public async Task ClientBaseConstructedForOneContractNeverEmitsABoundaryForASeparatelyImplementedContract()
    {
        var (aggregate, _) = await AnalyzeFixtureAsync();

        var clients = aggregate.Facts.OfType<ServiceClientBoundaryFact>()
            .Where(fact => fact.ClientType == "CoreWcfServices.MismatchedContractClient")
            .ToArray();

        // MismatchedContractClient derives ClientBase<ICalculatorService> (constructed with
        // ICalculatorService) but separately implements the unrelated admitted IClassicEchoService
        // directly. Only the constructed contract may ever get a client boundary.
        Assert.Contains(clients, fact => fact.ServiceContractType == CalculatorContractMetadataName);
        Assert.DoesNotContain(clients, fact => fact.ServiceContractType == ClassicContractMetadataName);
        Assert.DoesNotContain(
            aggregate.Facts.OfType<ServiceOperationCapabilityFact>(),
            fact => fact.ImplementationType == "CoreWcfServices.MismatchedContractClient" && fact.ServiceContractType == CalculatorContractMetadataName);
    }

    [Fact]
    public async Task RealRoslynCoexistenceKeepsForeignSameQualifiedAttributesOutOfTheDiagram()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"seqdoc-corewcf-coexistence-{Guid.NewGuid():N}");
        try
        {
            var sourceRoot = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "PassC", "CoreWcfServices");
            CopyFixture(sourceRoot, temporaryRoot);
            var repositoryRoot = FindRepositoryRoot();
            foreach (var fileName in new[] { "Directory.Build.props", "Directory.Packages.props", "NuGet.config", "global.json" })
            {
                var sourceFile = Path.Combine(repositoryRoot, fileName);
                if (File.Exists(sourceFile))
                {
                    File.Copy(sourceFile, Path.Combine(temporaryRoot, fileName));
                }
            }
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "foreign"));
            File.WriteAllText(Path.Combine(temporaryRoot, "foreign", "ForeignAttributes.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(temporaryRoot, "foreign", "Attributes.cs"), """
                namespace CoreWCF;
                [System.AttributeUsage(System.AttributeTargets.Interface | System.AttributeTargets.Method)]
                public sealed class ServiceContractAttribute : System.Attribute { }
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class OperationContractAttribute : System.Attribute { }
                """);
            File.WriteAllText(Path.Combine(temporaryRoot, "Coexistence.cs"), """
                extern alias foreign;
                using CoreWCF;
                using Microsoft.Extensions.Hosting;
                namespace CoreWcfServices;
                [ServiceContract]
                [foreign::CoreWCF.ServiceContract]
                public interface ICoexistenceService
                {
                    [OperationContract]
                    [foreign::CoreWCF.OperationContract]
                    string Echo(string value);
                }
                public sealed class CoexistenceService : ICoexistenceService
                {
                    public string Echo(string value) => value;
                }
                """);

            var projectFile = Path.Combine(temporaryRoot, "CoreWcfServices.csproj");
            var project = File.ReadAllText(projectFile)
                .Replace("</Project>", "<ItemGroup><Compile Remove=\"foreign/**/*.cs\" /><Reference Include=\"ForeignAttributes\"><HintPath>foreign/bin/Release/net10.0/ForeignAttributes.dll</HintPath><Aliases>foreign</Aliases></Reference></ItemGroup></Project>");
            File.WriteAllText(projectFile, project);
            var startupFile = Path.Combine(temporaryRoot, "Startup.cs");
            File.WriteAllText(startupFile, File.ReadAllText(startupFile).Replace(
                "});\n    }\n}",
                "builder.AddService<CoexistenceService>()\n                .AddServiceEndpoint<CoexistenceService, ICoexistenceService>(new BasicHttpBinding(), \"/Coexistence\");\n        });\n    }\n}"));

            await RunDotnetAsync(temporaryRoot, "build foreign/ForeignAttributes.csproj -c Release --nologo");
            await RunDotnetAsync(temporaryRoot, "restore CoreWcfServices.csproj --nologo --force");
            await RunDotnetAsync(temporaryRoot, "build CoreWcfServices.csproj -c Release --no-restore --nologo");
            var request = new CompilationAnalysisRequest(
                temporaryRoot,
                projectFile,
                CompilationProfile.Create("CoreWcfServices.csproj", "Release", "net10.0"));
            var extractionResult = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
            Assert.True(extractionResult.IsSuccess, string.Join(Environment.NewLine, extractionResult.Diagnostics.Select(d => d.TechnicalCause)));
            var extraction = extractionResult.Value!;
            var behaviorResult = await new BehaviorAnalyzer().AnalyzeAsync(
                new BehaviorAnalysisRequest(extraction.ProgramIndex, extraction.BehaviorInput), CancellationToken.None);
            Assert.True(behaviorResult.IsSuccess);
            var framework = await new FrameworkModelHost([new CoreWcfServiceModel()]).AnalyzeAsync(new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(request.Profile, extraction.ProgramIndex),
                new FrameworkAnalysisContext(request.Profile, extraction.ProgramIndex), extraction.Operations, extraction.Symbols), CancellationToken.None);
            var coexistenceCapability = Assert.Single(framework.Facts.OfType<ServiceOperationCapabilityFact>(),
                fact => fact.ImplementationType == "CoreWcfServices.CoexistenceService");
            var coexistenceMethod = Assert.Single(extraction.Symbols,
                symbol => symbol.MetadataName == "Echo"
                    && symbol.MethodShape?.DeclaringType.Identity.MetadataName == "CoreWcfServices.CoexistenceService");
            var coexistenceMember = Assert.Single(coexistenceMethod.MethodShape!.ImplementedInterfaceMembers,
                member => member.InterfaceType.MetadataName == "CoreWcfServices.ICoexistenceService"
                    && member.InterfaceMethodMetadataName == "Echo");
            var genuineApplications = coexistenceMember.InterfaceTypeAttributes
                .Concat(coexistenceMember.InterfaceMethodAttributes)
                .Where(application => application.AttributeType.AssemblyIdentity == "CoreWCF.Primitives")
                .ToArray();
            var foreignApplications = coexistenceMember.InterfaceTypeAttributes
                .Concat(coexistenceMember.InterfaceMethodAttributes)
                .Where(application => application.AttributeType.AssemblyIdentity == "ForeignAttributes")
                .ToArray();
            Assert.Equal(2, genuineApplications.Length);
            Assert.Equal(2, foreignApplications.Length);
            var genuineIds = genuineApplications.SelectMany(application => application.Evidence).Select(evidence => evidence.Id).ToHashSet();
            var foreignIds = foreignApplications.SelectMany(application => application.Evidence).Select(evidence => evidence.Id).ToHashSet();
            Assert.NotEmpty(genuineIds);
            Assert.NotEmpty(foreignIds);
            Assert.Empty(genuineIds.Intersect(foreignIds));
            var capabilityUnderlying = coexistenceCapability.Evidence.Single().UnderlyingEvidence;
            Assert.All(genuineIds, id => Assert.Contains(capabilityUnderlying, evidence => evidence.Id == id));
            Assert.All(foreignIds, id => Assert.DoesNotContain(capabilityUnderlying, evidence => evidence.Id == id));
            Assert.Equal(CertaintyLevel.Exact, coexistenceCapability.Certainty);

            var graphSet = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
                request.Profile, extraction.ProgramIndex, behaviorResult.Value!, framework,
                new SeqDoc.Core.Semantics.SemanticFactSet(1, "test", request.Profile, extraction.ProgramIndex.IndexFingerprint, [], [], [], [], "semantic-test"),
                new SeqDoc.Core.Semantics.DependencyInjectionFactSet(1, "test", request.Profile, extraction.ProgramIndex.IndexFingerprint, [], [], [], "di-test"),
                new SeqDoc.Core.Semantics.StructuralResultFactSet(1, "test", request.Profile, extraction.ProgramIndex.IndexFingerprint, [], [], [], "structural-test"),
                new SeqDoc.Core.Semantics.NonGetSemanticFactSet(1, "test", request.Profile, extraction.ProgramIndex.IndexFingerprint, [], [], [], [], [], [], [], [], "non-get-test")));
            var graph = Assert.Single(graphSet.Graphs, graph => graph.OperationKey == "CoreWcfServices.ICoexistenceService.Echo");
            Assert.Equal(ScenarioRootKind.ServiceOperation, graph.RootKind);
            var plan = DocumentationPlanner.Plan(graph);
            Assert.NotEmpty(plan.Diagram.Messages);
            Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("foreign", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
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
        Assert.Equal(ScenarioRootKind.ServiceOperation, addGraph.RootKind);
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

    private static void CopyFixture(string sourceDirectory, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            if (relative is "Directory.Build.props" or "packages.lock.json"
                || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj-custom"))
            {
                continue;
            }

            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static async Task RunDotnetAsync(string workingDirectory, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{output}\n{error}");
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
