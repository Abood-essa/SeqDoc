using System.Collections.Immutable;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

/// <summary>
/// Adversarial model tests for acceptance-critical risks: delivery-order determinism, evidence-order
/// stability, profile isolation, and the no-inference guarantee for unproven bindings and outcomes.
/// </summary>
public sealed class AspNetCoreControllerModelAdversarialTests
{
    private const string HttpGetAttribute = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";
    private const string HttpPostAttribute = "Microsoft.AspNetCore.Mvc.HttpPostAttribute";
    private const string HttpPutAttribute = "Microsoft.AspNetCore.Mvc.HttpPutAttribute";
    private const string HttpDeleteAttribute = "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute";
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string RouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";

    [Fact]
    public async Task ModelOutputIsIndependentOfSymbolDeliveryOrder()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var symbolsForward = new[] { AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create") };
        var symbolsReverse = new[] { AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"), AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById") };

        var forward = await RunAll(symbolsForward, context);
        var reverse = await RunAll(symbolsReverse, context);

        Assert.Equal(
            forward.SelectMany(result => result.Facts).Select(fact => fact.Id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            reverse.SelectMany(result => result.Facts).Select(fact => fact.Id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ModelOutputIsIndependentOfOperationDeliveryOrder()
    {
        var index = OrdersIndex();
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var ok = AspNetCoreTestIndexFactory.Invocation("GetById", AspNetCoreTestIndexFactory.ControllerBaseIdentity("Ok"));
        var badRequest = AspNetCoreTestIndexFactory.Invocation("Cancel", AspNetCoreTestIndexFactory.ControllerBaseIdentity("BadRequest"));

        var forward = await Task.WhenAll(new[] { ok, badRequest }.Select(operation => new AspNetCoreControllerModel().AnalyzeOperationAsync(operation, context, CancellationToken.None).AsTask()));
        var reverse = await Task.WhenAll(new[] { badRequest, ok }.Select(operation => new AspNetCoreControllerModel().AnalyzeOperationAsync(operation, context, CancellationToken.None).AsTask()));

        Assert.Equal(
            forward.SelectMany(result => result.Facts).Select(fact => fact.Id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            reverse.SelectMany(result => result.Facts).Select(fact => fact.Id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ReversedAttributeAndEvidenceOrderProducesIdenticalAggregates()
    {
        var forwardAttributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""));
        var reverseAttributes = forwardAttributes.Reverse().ToImmutableArray();
        var forwardIndex = OrdersIndex(attributeOverrides: forwardAttributes);
        var reverseIndex = OrdersIndex(attributeOverrides: reverseAttributes);
        var forwardHost = new FrameworkModelHost([new AspNetCoreControllerModel()]);
        var reverseHost = new FrameworkModelHost([new AspNetCoreControllerModel()]);

        var forward = await forwardHost.AnalyzeAsync(
            CreateRequest(forwardIndex, symbols: [AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById")]),
            CancellationToken.None);
        var reverse = await reverseHost.AnalyzeAsync(
            CreateRequest(reverseIndex, symbols: [AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById")]),
            CancellationToken.None);

        Assert.Equal(
            forward.Facts.Select(fact => fact.Id.Value).ToArray(),
            reverse.Facts.Select(fact => fact.Id.Value).ToArray());
        var forwardEntry = Assert.Single(forward.Facts.OfType<HttpEntryPointFact>());
        var reverseEntry = Assert.Single(reverse.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal(forwardEntry.CanonicalRoute, reverseEntry.CanonicalRoute);
        Assert.Equal(forwardEntry.Evidence[0].Id, reverseEntry.Evidence[0].Id);
        Assert.Empty(forward.Diagnostics);
        Assert.Empty(reverse.Diagnostics);
    }

    [Fact]
    public async Task FactsAreScopedByCompilationProfile()
    {
        var index = OrdersIndex();
        var contextA = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);
        var profileB = Core.Identity.CompilationProfile.Create(
            "other/App.csproj",
            "Release",
            "net10.0");
        var indexB = index with { Profile = profileB };
        var contextB = new FrameworkAnalysisContext(profileB, indexB);

        var resultA = await new AspNetCoreControllerModel().AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), contextA, CancellationToken.None);
        var resultB = await new AspNetCoreControllerModel().AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), contextB, CancellationToken.None);

        var factA = Assert.Single(resultA.Facts.OfType<HttpEntryPointFact>());
        var factB = Assert.Single(resultB.Facts.OfType<HttpEntryPointFact>());
        Assert.NotEqual(factA.Id, factB.Id);
        Assert.Equal(factA.CanonicalRoute, factB.CanonicalRoute);
    }

    [Fact]
    public async Task UnprovenBindingsNeverInferBodyQueryOrHeader()
    {
        // Two routes: the standard "{id:guid}" and an extra "alternate" without a placeholder.
        var extraAttributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"alternate\""));
        var methodOverrides = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid"), ("body", "AspNetCoreControllers.OrderRequest"), ("query", "System.String")]));
        var index = OrdersIndex(extraAttributes: extraAttributes, methodOverrides: methodOverrides);
        var context = new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index);

        var result = await new AspNetCoreControllerModel().AnalyzeSymbolAsync(AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"), context, CancellationToken.None);

        var entries = result.Facts.OfType<HttpEntryPointFact>().ToArray();
        Assert.Equal(2, entries.Length);
        var constrainedEntry = Assert.Single(entries, entry => entry.CanonicalRoute == "api/Orders/{id:guid}");
        var alternateEntry = Assert.Single(entries, entry => entry.CanonicalRoute == "api/Orders/alternate");

        var bindings = result.Facts.OfType<HttpRequestBindingFact>().ToArray();
        Assert.Equal(6, bindings.Length);

        // On the route that actually declares the placeholder, id is Route.
        Assert.Equal(
            HttpBindingKind.Route,
            Assert.Single(bindings, binding => binding.EntryPointId == constrainedEntry.EntryPointId && binding.ParameterName == "id").BindingKind);
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == constrainedEntry.EntryPointId && binding.ParameterName == "body").BindingKind);
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == constrainedEntry.EntryPointId && binding.ParameterName == "query").BindingKind);

        // On the alternate route there is no placeholder, so the same parameter is Unknown there:
        // per-entrypoint association, never root-level any-route association.
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == alternateEntry.EntryPointId && binding.ParameterName == "id").BindingKind);
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == alternateEntry.EntryPointId && binding.ParameterName == "body").BindingKind);
        Assert.Equal(
            HttpBindingKind.Unknown,
            Assert.Single(bindings, binding => binding.EntryPointId == alternateEntry.EntryPointId && binding.ParameterName == "query").BindingKind);
        Assert.All(bindings, binding => Assert.True(binding.RoutePlaceholder is null || binding.ParameterName == binding.RoutePlaceholder));
    }

    [Fact]
    public async Task HostAggregationIsIndependentOfSymbolOrder()
    {
        var index = OrdersIndex();
        var symbolsForward = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Update"),
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"),
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"));
        var symbolsReverse = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Create"),
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Update"));
        var forwardHost = new FrameworkModelHost([new AspNetCoreControllerModel()]);
        var reverseHost = new FrameworkModelHost([new AspNetCoreControllerModel()]);

        var forward = await forwardHost.AnalyzeAsync(CreateRequest(index, symbols: symbolsForward), CancellationToken.None);
        var reverse = await reverseHost.AnalyzeAsync(CreateRequest(index, symbols: symbolsReverse), CancellationToken.None);

        Assert.Equal(
            forward.Facts.Select(fact => fact.Id.Value).ToArray(),
            reverse.Facts.Select(fact => fact.Id.Value).ToArray());
        Assert.Equal(
            forward.Facts.OrderBy(fact => fact.Id.Value, StringComparer.Ordinal).Select(fact => fact.Id.Value).ToArray(),
            forward.Facts.Select(fact => fact.Id.Value).ToArray());
    }

    private static async Task<ModelResult[]> RunAll(
        IEnumerable<SymbolDescriptor> symbols,
        FrameworkAnalysisContext context)
        => await Task.WhenAll(symbols.Select(symbol => new AspNetCoreControllerModel().AnalyzeSymbolAsync(symbol, context, CancellationToken.None).AsTask()));

    private static FrameworkAnalysisRequest CreateRequest(
        ProgramIndexSnapshot index,
        ImmutableArray<SymbolDescriptor> symbols)
        => new(
            new FrameworkDetectionContext(AspNetCoreTestIndexFactory.Profile, index),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            Operations: [],
            Symbols: symbols);

    private static ProgramIndexSnapshot OrdersIndex(
        ImmutableArray<ProgramAttributeApplication>? attributeOverrides = null,
        ImmutableArray<ProgramAttributeApplication>? extraAttributes = null,
        ImmutableArray<ProgramMethod>? methodOverrides = null)
    {
        var attributes = ImmutableArray.CreateBuilder<ProgramAttributeApplication>();
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Create"), HttpPostAttribute));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Cancel"), HttpDeleteAttribute, "\"{id:guid}/cancel\""));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Update"), HttpPutAttribute, "\"{id:guid}\""));
        if (extraAttributes is not null)
        {
            attributes.AddRange(extraAttributes.Value);
        }

        var methods = methodOverrides ?? ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Create", [("request", "AspNetCoreControllers.OrderRequest")]),
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]),
            AspNetCoreTestIndexFactory.Method("Cancel", [("id", "System.Guid")]),
            AspNetCoreTestIndexFactory.Method("Update", [("id", "System.Guid"), ("request", "AspNetCoreControllers.OrderRequest")]));

        return AspNetCoreTestIndexFactory.ToIndex(
            [AspNetCoreTestIndexFactory.ControllerType()],
            methods,
            attributeOverrides ?? attributes.ToImmutable());
    }
}
