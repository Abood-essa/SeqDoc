using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

/// <summary>
/// Input-certainty coverage (C-1-F3). The model never promotes SymbolDescriptor/OperationDescriptor
/// uncertainty: exact compiler inputs emit Exact, non-exact input certainty propagates to facts and
/// model evidence, unknown bindings stay Unknown, and a stable degradation diagnostic is emitted.
/// Canonical fact identities remain stable because they never include certainty.
/// </summary>
public sealed class AspNetCoreControllerModelCertaintyTests
{
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string RouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string HttpGetAttribute = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";
    private const string HttpPostAttribute = "Microsoft.AspNetCore.Mvc.HttpPostAttribute";
    private const string HttpDeleteAttribute = "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute";

    private static readonly AspNetCoreControllerModel Model = new();

    [Theory]
    [InlineData(CertaintyLevel.Conservative)]
    [InlineData(CertaintyLevel.Heuristic)]
    [InlineData(CertaintyLevel.Unknown)]
    public async Task NonExactSymbolCertaintyDegradesEntryPointAndProvenBinding(CertaintyLevel inputCertainty)
    {
        var index = OrdersIndex();
        var descriptor = AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Update", AspNetCoreTestIndexFactory.EligibleMethodShape("Update"))
            with
        { Certainty = inputCertainty };

        var result = await Model.AnalyzeSymbolAsync(
            descriptor,
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.True(result.Recognized);
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal(inputCertainty, entry.Certainty);
        var bindings = result.Facts.OfType<HttpRequestBindingFact>().ToArray();
        var routeBinding = Assert.Single(bindings, binding => binding.ParameterName == "id");
        Assert.Equal(inputCertainty, routeBinding.Certainty);
        var unknownBinding = Assert.Single(bindings, binding => binding.ParameterName == "request");
        Assert.Equal(CertaintyLevel.Unknown, unknownBinding.Certainty);
        Assert.Equal(inputCertainty, Assert.Single(entry.Evidence).Certainty);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SEQAS007");
    }

    [Fact]
    public async Task ExactSymbolCertaintyEmitsExactWithoutDegradationDiagnostic()
    {
        var index = OrdersIndex();
        var descriptor = AspNetCoreTestIndexFactory.MethodSymbolDescriptor("Update", AspNetCoreTestIndexFactory.EligibleMethodShape("Update"));

        var result = await Model.AnalyzeSymbolAsync(
            descriptor,
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.True(result.Recognized);
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal(CertaintyLevel.Exact, entry.Certainty);
        Assert.Equal(CertaintyLevel.Exact, Assert.Single(entry.Evidence).Certainty);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "SEQAS007");
    }

    [Fact]
    public async Task NonExactOperationCertaintyDegradesOutcome()
    {
        var index = OrdersIndex();
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("Ok"))
            with
        { Certainty = CertaintyLevel.Heuristic };

        var result = await Model.AnalyzeOperationAsync(
            operation,
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<HttpDirectOutcomeFact>());
        Assert.Equal(CertaintyLevel.Heuristic, fact.Certainty);
        Assert.Equal(CertaintyLevel.Heuristic, Assert.Single(fact.Evidence).Certainty);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SEQAS007");
    }

    private static ProgramIndexSnapshot OrdersIndex()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Create"), HttpPostAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Cancel"), HttpDeleteAttribute, "\"{id:guid}/cancel\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("Update"), HttpGetAttribute, "\"{id:guid}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("Create", [("request", "AspNetCoreControllers.OrderRequest")]),
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]),
            AspNetCoreTestIndexFactory.Method("Cancel", [("id", "System.Guid")]),
            AspNetCoreTestIndexFactory.Method("Update", [("id", "System.Guid"), ("request", "AspNetCoreControllers.OrderRequest")]));

        return AspNetCoreTestIndexFactory.ToIndex(
            [AspNetCoreTestIndexFactory.ControllerType()],
            methods,
            attributes);
    }
}
