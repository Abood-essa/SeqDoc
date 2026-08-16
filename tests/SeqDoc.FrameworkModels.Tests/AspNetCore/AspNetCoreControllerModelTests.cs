using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

public sealed class AspNetCoreControllerModelTests
{
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string RouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string NonActionAttribute = "Microsoft.AspNetCore.Mvc.NonActionAttribute";
    private const string HttpGetAttribute = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";
    private const string HttpPostAttribute = "Microsoft.AspNetCore.Mvc.HttpPostAttribute";
    private const string HttpPutAttribute = "Microsoft.AspNetCore.Mvc.HttpPutAttribute";
    private const string HttpDeleteAttribute = "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute";

    private static readonly AspNetCoreControllerModel Model = new();

    [Fact]
    public void DescriptorIsVersionedAndDeterministicallyOrdered()
    {
        Assert.Equal("seqdoc.aspnetcore.controllers", Model.Descriptor.ModelId);
        Assert.Equal("1.0.0", Model.Descriptor.Version);
        Assert.Equal(100, Model.Descriptor.Order);
    }

    [Fact]
    public void IsApplicableAcceptsExactAttributesWithoutWebKindOrFrameworkReference()
    {
        // The extractor reports Web SDK libraries as Library and may omit framework references;
        // exact applied attribute identities alone must be authoritative applicability evidence.
        var index = OrdersIndex(projectKind: ProjectKind.Library, includeMvcReference: false);

        Assert.True(Model.IsApplicable(new FrameworkDetectionContext(AspNetCoreTestIndexFactory.Profile, index)));
    }

    [Fact]
    public void IsApplicableAcceptsWebProjectKindWhenPresent()
    {
        var index = OrdersIndex(projectKind: ProjectKind.Web);

        Assert.True(Model.IsApplicable(new FrameworkDetectionContext(AspNetCoreTestIndexFactory.Profile, index)));
    }

    [Fact]
    public void IsApplicableRejectsPlainLibraryWithoutExactAttributes()
    {
        var index = AspNetCoreTestIndexFactory.ToIndex(
            [],
            [],
            [],
            projectKind: ProjectKind.Library,
            includeMvcReference: true);

        Assert.False(Model.IsApplicable(new FrameworkDetectionContext(AspNetCoreTestIndexFactory.Profile, index)));
    }

    [Fact]
    public void IsApplicableRejectsProfileWithOnlyMvcReferenceAndNoAttributes()
    {
        var index = OrdersIndex(includeMvcReference: true, includeApiController: false);

        // Library kind with a reference but no exact ASP.NET attributes remains non-applicable.
        var attributesOnlyRoute = index with
        {
            Attributes = [AspNetCoreTestIndexFactory.Attribute(
                AspNetCoreTestIndexFactory.ControllerSymbol,
                "Fake.Web.ApiControllerAttribute")],
        };

        Assert.False(Model.IsApplicable(new FrameworkDetectionContext(AspNetCoreTestIndexFactory.Profile, attributesOnlyRoute)));
    }

    [Fact]
    public async Task RecognizesPostActionWithCombinedControllerTokenRoute()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"), context, CancellationToken.None);

        Assert.True(result.Recognized);
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal(HttpMethodKind.Post, entry.HttpMethod);
        Assert.Equal("api/Orders", entry.CanonicalRoute);
        Assert.Equal(AspNetCoreTestIndexFactory.MethodId("Create"), entry.RootMethod);
        Assert.Equal("POST api/Orders", entry.OperationKey);
        Assert.Equal(StableIdentity.CreateEntryPointId(new HttpEntryPointIdentityDescriptor(
            AspNetCoreTestIndexFactory.Profile.Id,
            AspNetCoreTestIndexFactory.MethodId("Create"),
            HttpMethodKind.Post,
            "api/Orders")), entry.EntryPointId);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task SubstitutesControllerTokenFromExactContainingType()
    {
        var index = OrdersIndex(controllerRoute: "\"api/[controller]/v1\"");
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"), context, CancellationToken.None);

        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/Orders/v1", entry.CanonicalRoute);
        Assert.DoesNotContain("OrdersController", entry.CanonicalRoute, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreservesRouteConstraintsAndBindsExactPlaceholder()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, CancellationToken.None);

        Assert.True(result.Recognized);
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal(HttpMethodKind.Get, entry.HttpMethod);
        Assert.Equal("api/Orders/{id:guid}", entry.CanonicalRoute);
        var binding = Assert.Single(result.Facts.OfType<HttpRequestBindingFact>());
        Assert.Equal(entry.EntryPointId, binding.EntryPointId);
        Assert.Equal("id", binding.ParameterName);
        Assert.Equal(HttpBindingKind.Route, binding.BindingKind);
        Assert.Equal("id", binding.RoutePlaceholder);
        Assert.Equal(CertaintyLevel.Exact, binding.Certainty);
    }

    [Fact]
    public async Task UnknownBindingsStayUnknownWithoutBodyOrQueryInference()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Update"), context, CancellationToken.None);

        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        var bindings = result.Facts.OfType<HttpRequestBindingFact>().ToArray();
        Assert.Equal(2, bindings.Length);
        Assert.All(bindings, binding => Assert.Equal(entry.EntryPointId, binding.EntryPointId));
        var routeBinding = Assert.Single(bindings, binding => binding.ParameterName == "id");
        Assert.Equal(HttpBindingKind.Route, routeBinding.BindingKind);
        var unprovenBinding = Assert.Single(bindings, binding => binding.ParameterName == "request");
        Assert.Equal(HttpBindingKind.Unknown, unprovenBinding.BindingKind);
        Assert.Null(unprovenBinding.RoutePlaceholder);
        Assert.Equal(CertaintyLevel.Unknown, unprovenBinding.Certainty);
    }

    [Fact]
    public async Task MultipleActionTemplatesEmitDeterministicDistinctEntryPoints()
    {
        var extra = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"alternate\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"other\""));
        var index = OrdersIndex(extraAttributes: extra);
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, CancellationToken.None);

        var entries = result.Facts.OfType<HttpEntryPointFact>().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Contains(entries, entry => entry.CanonicalRoute == "api/Orders/{id:guid}");
        Assert.Contains(entries, entry => entry.CanonicalRoute == "api/Orders/alternate");
        Assert.Contains(entries, entry => entry.CanonicalRoute == "api/Orders/other");
        Assert.Equal(3, entries.Select(entry => entry.EntryPointId.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.All(entries, entry => Assert.Equal(AspNetCoreTestIndexFactory.MethodId("GetById"), entry.RootMethod));
    }

    [Fact]
    public async Task IdenticalHttpMethodAndRouteDeclarationsDeduplicateToOneEntryPoint()
    {
        var extra = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        var index = OrdersIndex(extraAttributes: extra);
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, CancellationToken.None);

        var entries = result.Facts.OfType<HttpEntryPointFact>().ToArray();
        Assert.Single(entries);
        Assert.Equal("api/Orders/{id:guid}", Assert.Single(entries).CanonicalRoute);
        // One entry point per identity: never two facts sharing one EntryPointId.
        Assert.DoesNotContain(entries.GroupBy(entry => entry.EntryPointId.Value), group => group.Count() > 1);
    }

    [Fact]
    public async Task MalformedTemplateEmitsDiagnosticAndNoControllerOnlyRoute()
    {
        var malformed = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "plain-not-quoted"));
        var index = OrdersIndex(extraAttributes: malformed);
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, CancellationToken.None);

        // The valid "{id:guid}" declaration still produces its own entry point.
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/Orders/{id:guid}", entry.CanonicalRoute);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS004", diagnostic.Code);
    }

    [Fact]
    public async Task MalformedOnlyTemplateEmitsDiagnosticAndNoEntryPoint()
    {
        var index = OrdersIndex(
            methodOverrides: ImmutableArray.Create(
                AspNetCoreTestIndexFactory.Method("Create", [("request", "AspNetCoreControllers.OrderRequest")])),
            attributeOverrides: ImmutableArray.Create(
                AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
                AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
                AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Create"), HttpPostAttribute, "plain-not-quoted")));
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS004", diagnostic.Code);
    }

    [Fact]
    public async Task HonorsNonActionAttributeExactly()
    {
        var index = OrdersIndex(includeNonActionOnGetById: true);
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task UnsupportedRouteEmitsDeterministicDiagnosticNotConventionalGuess()
    {
        var index = OrdersIndex(controllerRoute: null);
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS001", diagnostic.Code);
        Assert.Equal(AnalysisStage.FrameworkModel, diagnostic.Stage);
    }

    [Fact]
    public async Task NonApiControllerTypeIsNotRecognized()
    {
        var index = OrdersIndex(includeApiController: false);
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task LookalikeAttributesInForeignNamespacesAreNotRecognized()
    {
        var lookalike = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, "Fake.Web.ApiControllerAttribute"),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), "Fake.Web.HttpGetAttribute", "\"{id:guid}\""));
        var index = OrdersIndex(attributeOverrides: lookalike);
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task NonMethodSymbolIsNotRecognized()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var typeSymbol = new SymbolDescriptor(
            AspNetCoreTestIndexFactory.ControllerSymbol,
            "NamedType",
            AspNetCoreTestIndexFactory.ControllerMetadataName,
            AspNetCoreTestIndexFactory.DocumentId,
            10,
            20,
            [AspNetCoreTestIndexFactory.SourceEvidence(AspNetCoreTestIndexFactory.ControllerMetadataName)],
            CertaintyLevel.Exact);

        var result = await Model.AnalyzeSymbolAsync(typeSymbol, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task DirectOutcomesMapToExactStatusCodes()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var cases = new (string Helper, string[] ParameterTypes, int Status, string[] Constants)[]
        {
            ("Ok", [], 200, []),
            ("Ok", ["object"], 200, []),
            ("CreatedAtAction", ["string", "object", "object"], 201, []),
            ("BadRequest", [], 400, []),
            ("BadRequest", ["object"], 400, []),
            ("NotFound", [], 404, []),
            ("Conflict", [], 409, []),
            ("StatusCode", ["int"], 400, ["400"]),
            ("StatusCode", ["int", "object"], 503, ["503"]),
        };

        foreach (var test in cases)
        {
            var operation = AspNetCoreTestIndexFactory.Invocation(
                "GetById",
                AspNetCoreTestIndexFactory.ControllerBaseIdentity(test.Helper, test.ParameterTypes),
                test.Constants);

            var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

            Assert.True(result.Recognized, $"Expected {test.Helper}({string.Join(", ", test.ParameterTypes)}) to be recognized.");
            var fact = Assert.Single(result.Facts.OfType<HttpDirectOutcomeFact>());
            Assert.Equal(test.Status, fact.StatusCode);
            Assert.Equal(AspNetCoreTestIndexFactory.MethodId("GetById"), fact.RootMethod);
            Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        }
    }

    [Fact]
    public async Task StatusCodeWithoutConstantEmitsDiagnosticNotGuessedStatus()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "Cancel",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "int"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS003", diagnostic.Code);
    }

    [Fact]
    public async Task UnsupportedOutcomeOverloadEmitsDiagnosticNotGuessedStatus()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("Ok", "string", "string"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS002", diagnostic.Code);
    }

    [Fact]
    public async Task WrongReturnTypeProducesUnsupportedOverloadDiagnostic()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        // Admitted Ok() returns OkResult; an Ok() identity with the wrong return type is an
        // unsupported overload, not a recognized 200.
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("Ok")
                with
            { ReturnType = "Microsoft.AspNetCore.Mvc.OkObjectResult" });

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Equal("SEQAS002", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("8.0.0.0")]
    [InlineData("11.0.0.0")]
    public async Task UnsupportedAssemblyVersionProducesNoExactOutcome(string assemblyVersion)
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentityWithVersion(assemblyVersion, "Ok"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task VersionNineDirectOutcomeMapsToExactStatus()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentityWithVersion("9.0.0.0", "Ok"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<HttpDirectOutcomeFact>());
        Assert.Equal(200, fact.StatusCode);
        Assert.Equal(AspNetCoreTestIndexFactory.MethodId("GetById"), fact.RootMethod);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task MissingAssemblyVersionProducesNoExactOutcome()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentityWithVersion(null, "Ok"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task LookalikeAssemblyAndContainingTypeAreNotMatched()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            new FrameworkMethodIdentity(
                "AspNetCoreControllers",
                "Fake.Web.ControllerBase",
                "Ok",
                0,
                []));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task NonInvocationOperationIsNotRecognized()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var operation = new OperationDescriptor(
            new OperationId("operation:v1:object-creation"),
            AspNetCoreTestIndexFactory.MethodId("GetById"),
            "ObjectCreation",
            AspNetCoreTestIndexFactory.DocumentId,
            200,
            16,
            [AspNetCoreTestIndexFactory.SourceEvidence("object-creation")],
            CertaintyLevel.Exact);

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task HostAggregationRetainsProducerChainAndDirectSourceProvenance()
    {
        var index = OrdersIndex();
        var host = new FrameworkModelHost([Model]);
        var request = new FrameworkAnalysisRequest(
            new FrameworkDetectionContext(AspNetCoreTestIndexFactory.Profile, index),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            Operations: [],
            Symbols: [AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById")]);

        var aggregate = await host.AnalyzeAsync(request, CancellationToken.None);

        Assert.True(aggregate.Recognized);
        var entry = Assert.Single(aggregate.Facts.OfType<HttpEntryPointFact>());
        var evidence = Assert.Single(entry.Evidence);
        Assert.Equal(EvidenceKind.FrameworkModel, evidence.Kind);
        Assert.Equal("seqdoc.aspnetcore.controllers", evidence.ProducerId);
        Assert.Equal("1.0.0", evidence.ProducerVersion);
        Assert.Contains(evidence.UnderlyingEvidence, item => item.Kind == EvidenceKind.Source && item.Range is not null && !string.IsNullOrWhiteSpace(item.Symbol));
        Assert.Empty(aggregate.Diagnostics);
    }

    [Fact]
    public async Task CanceledTokenPropagatesFromBothAnalysisEntryPoints()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Model.AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Model.AnalyzeOperationAsync(
                AspNetCoreTestIndexFactory.Invocation("GetById", AspNetCoreTestIndexFactory.ControllerBaseIdentity("Ok")),
                context,
                cts.Token).AsTask());
    }

    private static ProgramIndexSnapshot OrdersIndex(
        ProjectKind projectKind = ProjectKind.Library,
        bool includeMvcReference = true,
        bool includeApiController = true,
        string? controllerRoute = "\"api/[controller]\"",
        bool includeNonActionOnGetById = false,
        ImmutableArray<ProgramAttributeApplication>? extraAttributes = null,
        ImmutableArray<ProgramMethod>? methodOverrides = null,
        ImmutableArray<ProgramType>? typeOverrides = null,
        ImmutableArray<ProgramAttributeApplication>? attributeOverrides = null)
    {
        var attributes = ImmutableArray.CreateBuilder<ProgramAttributeApplication>();
        if (includeApiController)
        {
            attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute));
        }

        if (controllerRoute is not null)
        {
            attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, controllerRoute));
        }

        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Create"), HttpPostAttribute));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Cancel"), HttpDeleteAttribute, "\"{id:guid}/cancel\""));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Update"), HttpPutAttribute, "\"{id:guid}\""));
        if (includeNonActionOnGetById)
        {
            attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), NonActionAttribute));
        }

        if (extraAttributes is not null)
        {
            attributes.AddRange(extraAttributes.Value);
        }

        var methods = methodOverrides ?? ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Create", [("request", "AspNetCoreControllers.OrderRequest")], "Microsoft.AspNetCore.Mvc.ActionResult<AspNetCoreControllers.Order>"),
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")], "Microsoft.AspNetCore.Mvc.ActionResult<AspNetCoreControllers.Order>"),
            AspNetCoreTestIndexFactory.Method("Cancel", [("id", "System.Guid")]),
            AspNetCoreTestIndexFactory.Method("Update", [("id", "System.Guid"), ("request", "AspNetCoreControllers.OrderRequest")]));

        return AspNetCoreTestIndexFactory.ToIndex(
            typeOverrides ?? [AspNetCoreTestIndexFactory.ControllerType()],
            methods,
            attributeOverrides ?? attributes.ToImmutable(),
            projectKind,
            includeMvcReference);
    }
}
