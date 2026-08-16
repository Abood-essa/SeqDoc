using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

public sealed class MinimalApiModelTests
{
    [Fact]
    public async Task ExactMapPostProjectsNamedHandlerAndLiteralRoute()
    {
        var operation = Operation("MapPost", "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions", "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "System.String", "System.Delegate") with
        {
            ConstantArguments =
            [
                new CompilerProvenArgument(0, "System.String", "metadata"),
                new CompilerProvenArgument(1, "System.String", "/api/items"),
            ],
        };
        var result = await new AspNetCoreMinimalApiModel().AnalyzeOperationAsync(
            operation with { CallbackTarget = new CallbackTargetDescriptor(new MethodId("method:v1:Program.PostItems"), null) },
            Context(), CancellationToken.None);

        var fact = Assert.Single(result.Facts.OfType<MinimalApiRouteFact>());
        Assert.Equal(HttpMethodKind.Post, fact.HttpMethod);
        Assert.Equal("/api/items", fact.CanonicalRoute);
        Assert.Equal(new MethodId("method:v1:Program.PostItems"), fact.HandlerRoot);
        Assert.Equal(MinimalApiHandlerKind.NamedMethod, fact.HandlerKind);
    }

    [Fact]
    public async Task GroupPrefixAndDuplicateRoutesRetainDistinctHandlerIdentities()
    {
        var first = await Analyze("GetItems", "/api/same");
        var second = await Analyze("GetSame", "/api/same");
        var facts = first.Facts.Concat(second.Facts).OfType<MinimalApiRouteFact>().ToArray();

        Assert.Equal(2, facts.Length);
        Assert.Equal(2, facts.Select(fact => fact.EntryPointId).Distinct().Count());
        Assert.NotEqual(facts[0].HandlerRoot, facts[1].HandlerRoot);
    }

    [Theory]
    [InlineData("MapGet", "Microsoft.AspNetCore.Builder.RouteHandlerBuilder")]
    [InlineData("MapPost", "Fake.Web.RouteHandlerBuilder")]
    public async Task ExactFrameworkIdentityRejectsLookalikeAndWrongOverload(string name, string containingType)
    {
        var result = await new AspNetCoreMinimalApiModel().AnalyzeOperationAsync(
            Operation(name, containingType, "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "System.String", "System.Delegate"),
            Context(), CancellationToken.None);

        Assert.DoesNotContain(result.Facts, fact => fact is MinimalApiRouteFact);
    }

    [Fact]
    public async Task LegacyHttpAbstractionsIdentityIsRejected()
    {
        var result = await new AspNetCoreMinimalApiModel().AnalyzeOperationAsync(
            Operation("MapGet", "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions", "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "System.String", "System.Delegate") with
            {
                TargetIdentity = Operation("MapGet", "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions", "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "System.String", "System.Delegate").TargetIdentity! with
                {
                    AssemblyIdentity = "Microsoft.AspNetCore.Http.Abstractions",
                },
            },
            Context(), CancellationToken.None);

        Assert.DoesNotContain(result.Facts, fact => fact is MinimalApiRouteFact);
    }

    [Theory]
    [InlineData("receiver")]
    [InlineData("return")]
    [InlineData("ref")]
    [InlineData("out")]
    [InlineData("in")]
    public async Task ExactRegistrationSignatureMutationsAreRejected(string mutation)
    {
        var operation = Operation("MapGet", "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions", "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "System.String", "System.Delegate");
        var target = operation.TargetIdentity!;
        target = mutation switch
        {
            "receiver" => target with
            {
                Parameters = target.Parameters.SetItem(
                    0,
                    new ParameterIdentityDescriptor(ParameterRefKind.None, "Fake.Routing.IEndpointRouteBuilder")),
            },
            "return" => target with { ReturnType = "Microsoft.AspNetCore.Builder.RouteHandlerBuilder`1" },
            _ => target with { Parameters = target.Parameters.SetItem(0, target.Parameters[0] with { RefKind = RefKind(mutation) }) },
        };

        var result = await new AspNetCoreMinimalApiModel().AnalyzeOperationAsync(
            operation with { TargetIdentity = target }, Context(), CancellationToken.None);

        Assert.DoesNotContain(result.Facts, fact => fact is MinimalApiRouteFact);
    }

    [Theory]
    [InlineData(CallbackTargetKind.AnonymousFunction)]
    [InlineData(CallbackTargetKind.Unknown)]
    public async Task InvalidCallbackDescriptorsAreRejected(CallbackTargetKind kind)
    {
        var result = await new AspNetCoreMinimalApiModel().AnalyzeOperationAsync(
            Operation("MapGet", "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions", "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "System.String", "System.Delegate") with
            {
                CallbackTarget = new CallbackTargetDescriptor(kind, null, null, null),
            }, Context(), CancellationToken.None);

        Assert.DoesNotContain(result.Facts, fact => fact is MinimalApiRouteFact);
    }

    [Theory]
    [InlineData("unsupported-overload")]
    [InlineData("dynamic-route")]
    [InlineData("delegate-variable")]
    [InlineData("invalid-callback")]
    public async Task RecognizedUnsupportedRegistrationsHaveStableDiagnosticAndNoFact(string partition)
    {
        var operation = Operation("MapGet", "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions", "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "System.String", "System.Delegate");
        operation = partition switch
        {
            "unsupported-overload" => operation with
            {
                TargetIdentity = operation.TargetIdentity! with
                {
                    Parameters = operation.TargetIdentity!.Parameters.SetItem(2, new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Func<System.String>")),
                },
            },
            "dynamic-route" => operation with { ConstantArguments = [] },
            "delegate-variable" => operation with { CallbackTarget = null },
            _ => operation with { CallbackTarget = new CallbackTargetDescriptor(CallbackTargetKind.Unknown, null, null, null) },
        };

        var model = new AspNetCoreMinimalApiModel();
        var first = await model.AnalyzeOperationAsync(operation, Context(), CancellationToken.None);
        var second = await model.AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.DoesNotContain(first.Facts, fact => fact is MinimalApiRouteFact);
        var diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal("MA001", diagnostic.Code);
        Assert.Equal(diagnostic.Id, Assert.Single(second.Diagnostics).Id);
    }

    [Fact]
    public void RouteGroupDescriptorRetainsExactMapGroupIdentitySteps()
    {
        var steps = typeof(FrameworkRouteGroupDescriptor).GetProperty("Steps");

        Assert.NotNull(steps);
        Assert.NotNull(steps!.PropertyType.GetGenericArguments().Single().GetProperty("TargetIdentity"));
    }

    private static async Task<ModelResult> Analyze(string handler, string route)
        => await new AspNetCoreMinimalApiModel().AnalyzeOperationAsync(
            Operation("MapGet", "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions", "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "System.String", "System.Delegate") with
            {
                ConstantArguments = [new CompilerProvenArgument(1, "System.String", route)],
                CallbackTarget = new CallbackTargetDescriptor(new MethodId($"method:v1:Program.{handler}"), null),
            }, Context(), CancellationToken.None);

    private static ParameterRefKind RefKind(string mutation)
        => mutation switch
        {
            "ref" => ParameterRefKind.Ref,
            "out" => ParameterRefKind.Out,
            _ => ParameterRefKind.In,
        };

    private static OperationDescriptor Operation(string name, string containingType, params string[] parameters)
        => new(
            new OperationId($"operation:v1:{name}"), new MethodId("method:v1:Program.<Main>$"), "Invocation", null, 0, 1,
             [new EvidenceRef(new EvidenceId("evidence:v1:minimal-api"), EvidenceKind.Source, "Program.cs", new SourceRange(new DocumentId("Program.cs"), new SourcePosition(0, 0), new SourcePosition(0, 3)), "Map", "test", CertaintyLevel.Exact)],
            CertaintyLevel.Exact,
             new FrameworkMethodIdentity("Microsoft.AspNetCore.Routing", containingType, name, 0,
                parameters.Select(type => new ParameterIdentityDescriptor(ParameterRefKind.None, type)).ToImmutableArray(),
                "Microsoft.AspNetCore.Builder.RouteHandlerBuilder", "10.0.0.0"),
             [new CompilerProvenArgument(1, "System.String", "/api/items")]);

    private static FrameworkAnalysisContext Context() => new(
        AspNetCoreTestIndexFactory.Profile,
        AspNetCoreTestIndexFactory.ToIndex([], [], []));
}
