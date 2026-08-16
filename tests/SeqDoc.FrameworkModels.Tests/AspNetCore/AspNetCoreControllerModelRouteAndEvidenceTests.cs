using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

/// <summary>
/// Adversarial coverage for route-template edges, per-entry-point binding association, and
/// framework-model evidence identity. The model must never manufacture a route from ambiguous input,
/// must suppress duplicate identical routes deterministically, must preserve exact token casing and
/// canonicalize slash boundaries, and must keep framework-model evidence identities specific to the
/// route or outcome they prove instead of reusing one identity for different evidence payloads.
/// </summary>
public sealed class AspNetCoreControllerModelRouteAndEvidenceTests
{
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string RouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string HttpGetAttribute = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";
    private const string HttpPostAttribute = "Microsoft.AspNetCore.Mvc.HttpPostAttribute";
    private const string HttpDeleteAttribute = "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute";

    private static readonly AspNetCoreControllerModel Model = new();

    /// <summary>Canonical sorted routes expected for the multi-route binding test.</summary>
    private static readonly string[] ExpectedSearchRoutes =
        ["api/Orders/{id:guid}", "api/Orders/{slug}"];

    [Fact]
    public async Task RouteBindingsStayAssociatedWithDistinctEntryPointRoutes()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Search"), HttpGetAttribute, "\"{id:guid}\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Search"), HttpGetAttribute, "\"{slug}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Search", [("id", "System.Guid"), ("slug", "System.String"), ("filter", "System.String")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Search"),
            context,
            CancellationToken.None);

        Assert.True(result.Recognized);
        var entries = result.Facts.OfType<HttpEntryPointFact>().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(2, entries.Select(entry => entry.EntryPointId.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ExpectedSearchRoutes,
            entries.Select(entry => entry.CanonicalRoute).OrderBy(route => route, StringComparer.Ordinal).ToArray());
        var idEntry = Assert.Single(entries, entry => entry.CanonicalRoute == "api/Orders/{id:guid}");
        var slugEntry = Assert.Single(entries, entry => entry.CanonicalRoute == "api/Orders/{slug}");

        // One binding fact per entry point per parameter: 2 entry points x 3 parameters = 6 facts.
        var bindings = result.Facts.OfType<HttpRequestBindingFact>().ToArray();
        Assert.Equal(6, bindings.Length);

        // On the id route, id is Route; slug and filter are Unknown because that route has no
        // placeholder for them.
        Assert.Equal(
            HttpBindingKind.Route,
            Assert.Single(bindings, binding => binding.EntryPointId == idEntry.EntryPointId && binding.ParameterName == "id").BindingKind);
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == idEntry.EntryPointId && binding.ParameterName == "slug").BindingKind);
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == idEntry.EntryPointId && binding.ParameterName == "filter").BindingKind);

        // On the slug route, slug is Route; id and filter are Unknown because that route has no
        // placeholder for them.
        Assert.Equal(
            HttpBindingKind.Route,
            Assert.Single(bindings, binding => binding.EntryPointId == slugEntry.EntryPointId && binding.ParameterName == "slug").BindingKind);
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == slugEntry.EntryPointId && binding.ParameterName == "id").BindingKind);
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == slugEntry.EntryPointId && binding.ParameterName == "filter").BindingKind);

        // Every binding stays rooted at the same action method, and every Route binding's
        // placeholder is proven by the exact entry point it is attached to.
        Assert.All(bindings, binding => Assert.Equal(AspNetCoreTestIndexFactory.MethodId("Search"), binding.RootMethod));
        foreach (var binding in bindings.Where(binding => binding.BindingKind == HttpBindingKind.Route))
        {
            var entry = Assert.Single(entries, candidate => candidate.EntryPointId == binding.EntryPointId);
            Assert.True(
                RouteContainsPlaceholder(entry.CanonicalRoute, binding.RoutePlaceholder!),
                $"Placeholder '{binding.RoutePlaceholder}' must exist on the associated route '{entry.CanonicalRoute}'.");
        }
    }

    [Fact]
    public async Task EntryPointFactsWithDifferentUnderlyingEvidenceNeverReuseOneEvidenceId()
    {
        var index = OrdersIndex();
        var host = new FrameworkModelHost([new AspNetCoreControllerModel()]);
        var aggregate = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(AspNetCoreTestIndexFactory.Profile, index),
                new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
                Operations: [],
                Symbols:
                [
                    AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"),
                    AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
                ]),
            CancellationToken.None);

        Assert.True(aggregate.Recognized);
        var entries = aggregate.Facts.OfType<HttpEntryPointFact>().ToArray();
        Assert.Equal(2, entries.Length);
        var createEntry = Assert.Single(entries, entry => entry.RootMethod == AspNetCoreTestIndexFactory.MethodId("Create"));
        var getByIdEntry = Assert.Single(entries, entry => entry.RootMethod == AspNetCoreTestIndexFactory.MethodId("GetById"));

        var createEvidence = Assert.Single(createEntry.Evidence);
        var getByIdEvidence = Assert.Single(getByIdEntry.Evidence);
        Assert.Equal(EvidenceKind.FrameworkModel, createEvidence.Kind);
        Assert.Equal(EvidenceKind.FrameworkModel, getByIdEvidence.Kind);
        Assert.Contains(createEvidence.UnderlyingEvidence, item => item.Symbol == "AspNetCoreControllers.OrdersController.Create");
        Assert.Contains(getByIdEvidence.UnderlyingEvidence, item => item.Symbol == "AspNetCoreControllers.OrdersController.GetById");

        // The two facts are proven by different source evidence (different root methods and HTTP
        // attributes), so reusing one EvidenceId for both payloads would conflate separate routes.
        Assert.NotEqual(createEvidence.Id, getByIdEvidence.Id);
        Assert.Empty(aggregate.Diagnostics);
    }

    [Fact]
    public async Task OutcomeFactsWithDifferentUnderlyingEvidenceNeverReuseOneEvidenceId()
    {
        var index = OrdersIndex();
        var host = new FrameworkModelHost([new AspNetCoreControllerModel()]);
        var ok = AspNetCoreTestIndexFactory.Invocation("GetById", AspNetCoreTestIndexFactory.ControllerBaseIdentity("Ok"));
        var badRequest = AspNetCoreTestIndexFactory.Invocation("Cancel", AspNetCoreTestIndexFactory.ControllerBaseIdentity("BadRequest"));
        var aggregate = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(AspNetCoreTestIndexFactory.Profile, index),
                new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
                Operations: [ok, badRequest],
                Symbols: []),
            CancellationToken.None);

        Assert.True(aggregate.Recognized);
        var outcomes = aggregate.Facts.OfType<HttpDirectOutcomeFact>().ToArray();
        Assert.Equal(2, outcomes.Length);
        var okOutcome = Assert.Single(outcomes, outcome => outcome.RootMethod == AspNetCoreTestIndexFactory.MethodId("GetById"));
        var badRequestOutcome = Assert.Single(outcomes, outcome => outcome.RootMethod == AspNetCoreTestIndexFactory.MethodId("Cancel"));

        var okEvidence = Assert.Single(okOutcome.Evidence);
        var badRequestEvidence = Assert.Single(badRequestOutcome.Evidence);
        Assert.Contains(okEvidence.UnderlyingEvidence, item => item.Symbol == "AspNetCoreControllers.OrdersController.GetById:Ok");
        Assert.Contains(badRequestEvidence.UnderlyingEvidence, item => item.Symbol == "AspNetCoreControllers.OrdersController.Cancel:BadRequest");

        // Different direct outcomes proven by different source operations must not share one
        // framework-model evidence identity.
        Assert.NotEqual(okEvidence.Id, badRequestEvidence.Id);
        Assert.Empty(aggregate.Diagnostics);
    }

    [Fact]
    public async Task UnquotedActionTemplateDoesNotInventRoute()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Create"), HttpGetAttribute, "api/Orders/unquoted"));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Create", [("request", "AspNetCoreControllers.OrderRequest")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"),
            context,
            CancellationToken.None);

        // The unquoted literal is ambiguous source; treating it as an empty template would invent a
        // controller-only route the action never declared.
        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task MalformedControllerRouteEmitsDiagnosticAndNoInventedEntryPoint()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "plain-not-quoted"),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            context,
            CancellationToken.None);

        // A malformed controller route is never replaced with an empty prefix, so no action-only
        // entry point is invented from the action template.
        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SEQAS004");
    }

    [Fact]
    public async Task MixedValidAndMalformedControllerRoutesKeepValidEntryPoint()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "plain-not-quoted"),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            context,
            CancellationToken.None);

        // The valid controller route still composes with the action template; the malformed one is
        // reported and contributes nothing.
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/Orders/{id:guid}", entry.CanonicalRoute);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SEQAS004");
    }

    [Fact]
    public async Task DuplicateRouteAttributesEmitOneCanonicalEntryPoint()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            context,
            CancellationToken.None);

        // Two identical attributes declare one identical route; emitting two facts that share one
        // EntryPointId would violate the identity contract, so exactly one canonical entry point
        // must be produced without inventing an extra route.
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/Orders/{id:guid}", entry.CanonicalRoute);
        Assert.Equal(
            StableIdentity.CreateEntryPointId(new HttpEntryPointIdentityDescriptor(
                AspNetCoreTestIndexFactory.Profile.Id,
                AspNetCoreTestIndexFactory.MethodId("GetById"),
                HttpMethodKind.Get,
                "api/Orders/{id:guid}")),
            entry.EntryPointId);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task UpperCaseControllerTokenIsPreservedNotSubstituted()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[Controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            context,
            CancellationToken.None);

        // Only the exact "[controller]" token is substituted; a differently cased token must be
        // preserved literally rather than guessed as a substitution.
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/[Controller]/{id:guid}", entry.CanonicalRoute);
        Assert.DoesNotContain("Orders", entry.CanonicalRoute, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeadingAndTrailingSlashesAreCanonicalized()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"/api/[controller]/\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}/\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            context,
            CancellationToken.None);

        // A non-rooted action template is canonicalized beneath the trimmed controller prefix.
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/Orders/{id:guid}", entry.CanonicalRoute);
        Assert.Equal("GET api/Orders/{id:guid}", entry.OperationKey);
    }

    [Fact]
    public async Task RootedActionTemplateOverridesControllerPrefixExactlyOnce()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Health"), HttpGetAttribute, "\"/health\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Health"), HttpGetAttribute, "\"~/health\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Health"), HttpGetAttribute, "\"relative\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Health", []));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Health"),
            context,
            CancellationToken.None);

        // "/health" and "~/health" both canonicalize from the app root and dedupe to one entry, never
        // multiplied beneath the controller prefix; "relative" stays beneath it.
        var entries = result.Facts.OfType<HttpEntryPointFact>().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Contains(entries, entry => entry.CanonicalRoute == "health");
        Assert.Contains(entries, entry => entry.CanonicalRoute == "api/Orders/relative");
        Assert.DoesNotContain(entries, entry => entry.CanonicalRoute == "api/Orders/health");
        Assert.Equal(2, entries.Select(entry => entry.EntryPointId.Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TildeWithoutSlashEmitsDiagnosticAndNoRootedGuess()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Health"), HttpGetAttribute, "\"~health\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Health", []));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Health"),
            context,
            CancellationToken.None);

        // '~' without '/' is malformed; no route is guessed from it.
        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SEQAS004");
    }

    [Fact]
    public async Task MethodRouteAttributeUnderAdmittedVerbSuppliesActionTemplates()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), RouteAttribute, "\"{id}\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"a\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), RouteAttribute, "\"b\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            context,
            CancellationToken.None);

        // [HttpGet][Route("{id}")] emits only GET prefix/{id} (never a controller-only route), and
        // [HttpGet("a")][Route("b")] emits both GET prefix/a and GET prefix/b, deduped canonically.
        var entries = result.Facts.OfType<HttpEntryPointFact>().ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Contains(entries, entry => entry.CanonicalRoute == "api/Orders/{id}");
        Assert.Contains(entries, entry => entry.CanonicalRoute == "api/Orders/a");
        Assert.Contains(entries, entry => entry.CanonicalRoute == "api/Orders/b");
        Assert.DoesNotContain(entries, entry => entry.CanonicalRoute == "api/Orders");
        Assert.All(entries, entry => Assert.Equal(AspNetCoreTestIndexFactory.MethodId("GetById"), entry.RootMethod));

        // Every candidate retains the exact admitted HTTP-method attribute as evidence for the verb,
        // including the Route-derived "b" fact from [HttpGet("a")][Route("b")]; the GET proof is
        // never dropped.
        foreach (var entry in entries)
        {
            var evidence = Assert.Single(entry.Evidence);
            Assert.Contains(
                evidence.UnderlyingEvidence,
                item => item.Symbol == "Microsoft.AspNetCore.Mvc.HttpGetAttribute");
        }

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task MethodRouteWithoutHttpVerbEmitsDiagnosticAndNoAnyVerbRoute()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), RouteAttribute, "\"{id}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            context,
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS005", diagnostic.Code);
    }

    [Fact]
    public async Task ConstrainedPlaceholderBindsOnlyExactParameterName()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Search"), HttpGetAttribute, "\"{id:int:range(1,100)}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Search", [("id", "System.Int32"), ("id2", "System.Int32"), ("filter", "System.String")]));
        var context = new FrameworkAnalysisContext(
            AspNetCoreTestIndexFactory.Profile,
            AspNetCoreTestIndexFactory.ToIndex([AspNetCoreTestIndexFactory.ControllerType()], methods, attributes));

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Search"),
            context,
            CancellationToken.None);

        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/Orders/{id:int:range(1,100)}", entry.CanonicalRoute);
        var idBinding = Assert.Single(result.Facts.OfType<HttpRequestBindingFact>(), binding => binding.ParameterName == "id");
        Assert.Equal(HttpBindingKind.Route, idBinding.BindingKind);
        Assert.Equal("id", idBinding.RoutePlaceholder);
        var id2Binding = Assert.Single(result.Facts.OfType<HttpRequestBindingFact>(), binding => binding.ParameterName == "id2");
        Assert.Equal(HttpBindingKind.Unknown, id2Binding.BindingKind);
        var filterBinding = Assert.Single(result.Facts.OfType<HttpRequestBindingFact>(), binding => binding.ParameterName == "filter");
        Assert.Equal(HttpBindingKind.Unknown, filterBinding.BindingKind);
    }

    private static bool RouteContainsPlaceholder(string route, string placeholderName)
        => route.Contains($"{{{placeholderName}:", StringComparison.Ordinal)
            || route.Contains($"{{{placeholderName}}}", StringComparison.Ordinal);

    private static ProgramIndexSnapshot OrdersIndex()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Create"), HttpPostAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Cancel"), HttpDeleteAttribute, "\"{id:guid}/cancel\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Create", [("request", "AspNetCoreControllers.OrderRequest")]),
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]),
            AspNetCoreTestIndexFactory.Method("Cancel", [("id", "System.Guid")]));

        return AspNetCoreTestIndexFactory.ToIndex(
            [AspNetCoreTestIndexFactory.ControllerType()],
            methods,
            attributes);
    }
}
