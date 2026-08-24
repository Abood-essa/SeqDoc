using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Analysis.Roslyn.Toolchains;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.CoreWcf;
using Xunit;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// Producer proof for issue #7's CoreWCF service model: the real Roslyn Program Index and eligibility
/// projector drive <see cref="CoreWcfServiceModel"/> through <see cref="FrameworkModelHost"/> against
/// the realistic CoreWCF fixture, proving the complete admission chain end to end (not a hand-built
/// intermediate fact) and that the metadata-only/generated negative boundary fails closed through the
/// same producer.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class CoreWcfServiceModelProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj";
    private const string CalculatorServiceMetadataName = "CoreWcfServices.CalculatorService";
    private const string UtilityHelperMetadataName = "CoreWcfServices.UtilityHelper";
    private const string CalculatorContractMetadataName = "CoreWcfServices.ICalculatorService";

    [Fact]
    public async Task PassCFixtureCompilationAdmitsExactServiceOperationsAndWithholdsNegativeBoundaries()
    {
        var request = CreateFixtureRequest();
        await MsBuildRegistration.EnsureRegisteredAsync(request.RepositoryRoot, CancellationToken.None);
        var (loaded, loadDiagnostics) = await CompilationWorkspaceLoader.LoadAsync(request, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Empty(loadDiagnostics);
        using (loaded!)
        {
            var index = await RoslynProgramIndexExtractor.ExtractAsync(
                loaded,
                request.Profile,
                request.RepositoryRoot,
                CancellationToken.None);
            var model = new CoreWcfServiceModel();
            Assert.True(model.IsApplicable(new FrameworkDetectionContext(request.Profile, index)));

            var compilation = Assert.Single(loaded.Projects).Compilation;
            var project = Assert.Single(loaded.Projects).StableId;
            var calculator = compilation.GetTypeByMetadataName(CalculatorServiceMetadataName);
            Assert.NotNull(calculator);
            var utility = compilation.GetTypeByMetadataName(UtilityHelperMetadataName);
            Assert.NotNull(utility);

            var symbols = calculator.GetMembers().OfType<IMethodSymbol>()
                .Concat(utility.GetMembers().OfType<IMethodSymbol>())
                .Select(method => ToEligibleSymbolDescriptor(method, project, request.RepositoryRoot))
                .ToImmutableArray();
            var host = new FrameworkModelHost([model]);
            var aggregate = await host.AnalyzeAsync(
                new FrameworkAnalysisRequest(
                    new FrameworkDetectionContext(request.Profile, index),
                    new FrameworkAnalysisContext(request.Profile, index),
                    Operations: [],
                    Symbols: symbols),
                CancellationToken.None);

            Assert.True(aggregate.Recognized);
            var facts = aggregate.Facts.OfType<ServiceOperationEntryPointFact>().ToArray();

            // Positive: every admitted OperationContract operation on CalculatorService is present.
            foreach (var operation in new[] { "Add", "Subtract", "Multiply", "Divide" })
            {
                Assert.Contains(facts, fact =>
                    fact.ServiceContractType == CalculatorContractMetadataName
                    && fact.ImplementationType == CalculatorServiceMetadataName
                    && fact.OperationName == operation);
            }

            // Negative boundaries fail closed through the same producer: the sibling operation without
            // [OperationContract], and IUtility's operation without [ServiceContract] on the interface,
            // never admit a root.
            Assert.DoesNotContain(facts, fact => fact.OperationName == "Modulo");
            Assert.DoesNotContain(facts, fact => fact.ImplementationType == UtilityHelperMetadataName);

            Assert.Equal(facts.Length, facts.Select(fact => fact.EntryPointId.Value).Distinct(StringComparer.Ordinal).Count());
            Assert.All(facts, fact => Assert.Equal(CertaintyLevel.Exact, fact.Certainty));
            Assert.Empty(aggregate.Diagnostics);
        }
    }

    private static CompilationAnalysisRequest CreateFixtureRequest()
    {
        var root = FindRepositoryRoot();
        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0"));
    }

    private static SymbolDescriptor ToEligibleSymbolDescriptor(
        IMethodSymbol method,
        StableProjectId project,
        string repositoryRoot)
    {
        var shape = FrameworkSymbolEligibilityProjector.ProjectMethodShape(method, project);
        var evidence = FrameworkSymbolEligibilityProjector.ProjectSourceEvidence(method, project, repositoryRoot);
        return new SymbolDescriptor(
            shape!.MethodSymbol,
            "Method",
            method.MetadataName,
            null,
            0,
            0,
            evidence,
            CertaintyLevel.Exact,
            shape);
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
