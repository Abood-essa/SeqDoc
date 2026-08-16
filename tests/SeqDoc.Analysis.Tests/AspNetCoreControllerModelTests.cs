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
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class AspNetCoreControllerModelTests
{
    private const string FixtureRelativePath = "tests/fixtures/PassC/AspNetCoreControllers/AspNetCoreControllers.csproj";
    private const string ControllerMetadataName = "AspNetCoreControllers.OrdersController";

    [Fact]
    public async Task PassCFixtureIndexSuppliesExactControllerActionAttributesRoutesParametersAndEvidence()
    {
        var request = CreateFixtureRequest();
        var result = await new RoslynProgramIndexBuilder().BuildAsync(request, CancellationToken.None);

        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));
        var index = Assert.IsType<ProgramIndexSnapshot>(result.Value);

        var controller = Assert.Single(index.Types, type => type.MetadataName == ControllerMetadataName);

        // Exact fully qualified controller attributes with source evidence.
        var apiController = Assert.Single(index.Attributes, attribute =>
            attribute.Target == controller.Id && attribute.AttributeType == "Microsoft.AspNetCore.Mvc.ApiControllerAttribute");
        Assert.NotEmpty(apiController.Evidence);
        Assert.All(apiController.Evidence, evidence => Assert.Equal(EvidenceKind.Source, evidence.Kind));

        var route = Assert.Single(index.Attributes, attribute =>
            attribute.Target == controller.Id && attribute.AttributeType == "Microsoft.AspNetCore.Mvc.RouteAttribute");
        Assert.Equal("\"api/[controller]\"", Assert.Single(route.Arguments));
        Assert.NotEmpty(route.Evidence);

        // Exact action attribute, route template, parameter, and method evidence.
        var getById = Assert.Single(index.Methods, method => method.Name == "GetById");
        var httpGet = Assert.Single(index.Attributes, attribute =>
            attribute.Target == getById.Symbol && attribute.AttributeType == "Microsoft.AspNetCore.Mvc.HttpGetAttribute");
        Assert.Equal("\"{id:guid}\"", Assert.Single(httpGet.Arguments));
        var idParameter = Assert.Single(getById.Parameters);
        Assert.Equal("id", idParameter.Name);
        Assert.Equal("System.Guid", idParameter.FullyQualifiedType);
        Assert.NotEmpty(getById.Evidence);
        Assert.All(getById.Evidence, evidence => Assert.NotNull(evidence.Range));

        var update = Assert.Single(index.Methods, method => method.Name == "Update");
        Assert.Contains(index.Attributes, attribute =>
            attribute.Target == update.Symbol && attribute.AttributeType == "Microsoft.AspNetCore.Mvc.HttpPutAttribute");
        Assert.Equal(2, update.Parameters.Length);

        // Lookalikes carry different fully qualified identities the model must never match by name.
        Assert.Contains(index.Attributes, attribute => attribute.AttributeType == "Fake.Web.ApiControllerAttribute");
        Assert.Contains(index.Attributes, attribute => attribute.AttributeType == "Fake.Web.HttpGetAttribute");
        Assert.DoesNotContain(index.Types, type => type.MetadataName == "Microsoft.AspNetCore.Mvc.ControllerBase");
    }

    [Fact]
    public async Task PassCFixtureCompilationDrivesEligibilityAndModelDiscovery()
    {
        var request = CreateFixtureRequest();
        await MsBuildRegistration.EnsureRegisteredAsync(request.RepositoryRoot, CancellationToken.None);
        var (loaded, loadDiagnostics) = await CompilationWorkspaceLoader.LoadAsync(request, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Empty(loadDiagnostics);
        using (loaded!)
        {
            // The unmodified extracted Program Index comes from the same loaded compilation.
            var index = await RoslynProgramIndexExtractor.ExtractAsync(
                loaded,
                request.Profile,
                request.RepositoryRoot,
                CancellationToken.None);
            var model = new AspNetCoreControllerModel();
            Assert.True(model.IsApplicable(new FrameworkDetectionContext(request.Profile, index)));

            // Project actual method symbols through the controlled eligibility projector and drive
            // the model with those descriptors; the Program Index snapshot is never patched.
            var compilation = Assert.Single(loaded.Projects).Compilation;
            var project = Assert.Single(loaded.Projects).StableId;
            var controller = compilation.GetTypeByMetadataName(ControllerMetadataName);
            Assert.NotNull(controller);
            var getById = Assert.Single(controller.GetMembers("GetById").OfType<IMethodSymbol>());

            // Relocation-safe logical evidence: projected evidence carries repository-relative
            // logical paths normalized to '/' on every platform, never the absolute checkout path.
            var projectedEvidence = FrameworkSymbolEligibilityProjector.ProjectSourceEvidence(getById, project, request.RepositoryRoot);
            var sourceEvidence = Assert.Single(projectedEvidence);
            Assert.DoesNotContain('\\', sourceEvidence.Artifact);
            Assert.Equal(
                "tests/fixtures/PassC/AspNetCoreControllers/Controllers/OrdersController.cs",
                sourceEvidence.Artifact);
            Assert.DoesNotContain(Path.GetFullPath(request.RepositoryRoot), sourceEvidence.Artifact, StringComparison.OrdinalIgnoreCase);

            // Rooted or escaping logical paths fail closed: no absolute checkout path ever reaches
            // document identities or evidence records.
            var outsideRoot = Path.Combine(request.RepositoryRoot, "..", "samples");
            Assert.Throws<InvalidOperationException>(() =>
                FrameworkSymbolEligibilityProjector.ProjectSourceEvidence(getById, project, outsideRoot));

            var symbols = controller.GetMembers()
                .OfType<IMethodSymbol>()
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
            var entries = aggregate.Facts.OfType<HttpEntryPointFact>().ToArray();
            Assert.Contains(entries, entry => entry.HttpMethod == HttpMethodKind.Post && entry.CanonicalRoute == "api/Orders");
            Assert.Contains(entries, entry => entry.HttpMethod == HttpMethodKind.Get && entry.CanonicalRoute == "api/Orders/{id:guid}");
            Assert.Contains(entries, entry => entry.HttpMethod == HttpMethodKind.Delete && entry.CanonicalRoute == "api/Orders/{id:guid}/cancel");
            Assert.Contains(entries, entry => entry.HttpMethod == HttpMethodKind.Put && entry.CanonicalRoute == "api/Orders/{id:guid}");
            Assert.Contains(entries, entry => entry.HttpMethod == HttpMethodKind.Delete && entry.CanonicalRoute == "api/Orders/unsupported");
            Assert.DoesNotContain(entries, entry => entry.CanonicalRoute == "api/Orders/fake-action");
            Assert.Equal(entries.Length, entries.Select(entry => entry.EntryPointId.Value).Distinct(StringComparer.Ordinal).Count());

            var bindings = aggregate.Facts.OfType<HttpRequestBindingFact>().ToArray();
            Assert.Contains(bindings, binding => binding.ParameterName == "id" && binding.BindingKind == HttpBindingKind.Route);
            Assert.Contains(bindings, binding => binding.ParameterName == "request" && binding.BindingKind == HttpBindingKind.Unknown);
            Assert.All(entries, entry => Assert.Equal(CertaintyLevel.Exact, entry.Certainty));
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
