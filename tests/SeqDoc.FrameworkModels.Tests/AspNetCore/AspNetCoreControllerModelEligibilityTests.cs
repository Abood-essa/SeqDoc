using System.Collections.Immutable;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

/// <summary>
/// Controlled compiler-shape eligibility coverage (C-1-F1). The model requires exact [ApiController],
/// an eligible controller type shape (class, public or nested-public, nonabstract, nonstatic,
/// nongeneric) whose exact base chain contains ControllerBase from Microsoft.AspNetCore.Mvc.Core
/// 10.0.0.0, and an eligible action method shape (ordinary public instance, nonabstract, nongeneric).
/// Foreign, plain, abstract, static, generic, nonpublic, and lookalike shapes produce no root, and
/// missing shape input fails closed with a stable eligibility diagnostic.
/// </summary>
public sealed class AspNetCoreControllerModelEligibilityTests
{
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string RouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string HttpGetAttribute = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";

    private static readonly AspNetCoreControllerModel Model = new();

    [Theory]
    [InlineData(false, true, true, true, 0)] // abstract method
    [InlineData(true, false, true, true, 0)] // static method
    [InlineData(true, true, false, true, 0)] // nonpublic method
    [InlineData(true, true, true, false, 0)] // nonordinary method
    [InlineData(true, true, true, true, 1)]  // generic method
    public async Task IneligibleActionMethodShapesProduceNoRoot(
        bool isOrdinary,
        bool isPublic,
        bool isStatic,
        bool isAbstract,
        int genericArity)
    {
        var index = OrdersIndex();
        var shape = AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with
        {
            IsOrdinary = isOrdinary,
            IsPublic = isPublic,
            IsStatic = isStatic,
            IsAbstract = isAbstract,
            GenericArity = genericArity,
        };

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById", shape),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(true, true, true, false, 0)]  // abstract controller
    [InlineData(true, true, false, true, 0)]  // static controller
    [InlineData(true, false, false, false, 0)] // nonpublic controller
    [InlineData(false, true, false, false, 0)] // non-class controller
    [InlineData(true, true, false, false, 1)]  // generic controller
    public async Task IneligibleControllerTypeShapesProduceNoRoot(
        bool isClass,
        bool isPublicOrNestedPublic,
        bool isAbstract,
        bool isStatic,
        int genericArity)
    {
        var index = OrdersIndex();
        var declaring = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with
        {
            IsClass = isClass,
            IsPublicOrNestedPublic = isPublicOrNestedPublic,
            IsAbstract = isAbstract,
            IsStatic = isStatic,
            GenericArity = genericArity,
        };
        var shape = AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with { DeclaringType = declaring };

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById", shape),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task BaseChainWithoutExactControllerBaseProducesNoRoot()
    {
        var index = OrdersIndex();
        var declaring = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with
        {
            BaseTypeChain =
            [
                new FrameworkTypeIdentity("Contoso.Framework", "1.0.0.0", "Contoso.Framework.BaseController"),
                new FrameworkTypeIdentity("System.Private.CoreLib", "10.0.0.0", "System.Object"),
            ],
        };
        var shape = AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with { DeclaringType = declaring };

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById", shape),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("8.0.0.0")]
    [InlineData("11.0.0.0")]
    public async Task BaseChainWithUnsupportedAssemblyVersionProducesNoRoot(string assemblyVersion)
    {
        var index = OrdersIndex();
        var declaring = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with
        {
            BaseTypeChain =
            [
                new FrameworkTypeIdentity(
                    AspNetCoreTestIndexFactory.ControllerBaseAssembly,
                    assemblyVersion,
                    AspNetCoreTestIndexFactory.ControllerBaseType),
                new FrameworkTypeIdentity("System.Private.CoreLib", "10.0.0.0", "System.Object"),
            ],
        };
        var shape = AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with { DeclaringType = declaring };

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById", shape),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task MissingShapeFailsClosedWithEligibilityDiagnostic()
    {
        var index = OrdersIndex();

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptorWithoutShape("GetById"),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS006", diagnostic.Code);
    }

    [Fact]
    public async Task ShapeFromAnotherMethodFailsClosedWithEligibilityDiagnostic()
    {
        var index = OrdersIndex();
        // The shape is otherwise fully eligible but bound to Cancel's method symbol; it can never
        // support GetById's root.
        var foreignShape = AspNetCoreTestIndexFactory.EligibleMethodShape("Cancel");

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById", foreignShape),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS006", diagnostic.Code);
    }

    [Fact]
    public async Task ShapeWithForeignDeclaringTypeFailsClosedWithEligibilityDiagnostic()
    {
        var index = OrdersIndex();
        var shape = AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with
        {
            DeclaringTypeSymbol = new SeqDoc.Core.Identity.SymbolId("symbol:v1:OtherController"),
        };

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById", shape),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS006", diagnostic.Code);
    }

    [Theory]
    [InlineData("negative-method-arity")]
    [InlineData("negative-type-arity")]
    [InlineData("uninitialized-base-chain")]
    [InlineData("blank-base-type-identity")]
    [InlineData("blank-declaring-type-identity")]
    [InlineData("declaring-metadata-name-mismatch")]
    public async Task IncompleteOrInconsistentShapesFailClosedWithEligibilityDiagnostic(string defect)
    {
        var index = OrdersIndex();
        var shape = defect switch
        {
            "negative-method-arity" => AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with { GenericArity = -1 },
            "negative-type-arity" => AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with
            {
                DeclaringType = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with { GenericArity = -1 },
            },
            "uninitialized-base-chain" => AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with
            {
                DeclaringType = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with { BaseTypeChain = default },
            },
            "blank-base-type-identity" => AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with
            {
                DeclaringType = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with
                {
                    BaseTypeChain =
                    [
                        new SeqDoc.Core.Frameworks.FrameworkTypeIdentity(string.Empty, string.Empty, "Microsoft.AspNetCore.Mvc.ControllerBase"),
                        new SeqDoc.Core.Frameworks.FrameworkTypeIdentity("System.Private.CoreLib", "10.0.0.0", "System.Object"),
                    ],
                },
            },
            "blank-declaring-type-identity" => AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with
            {
                DeclaringType = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with
                {
                    Identity = new SeqDoc.Core.Frameworks.FrameworkTypeIdentity(string.Empty, "1.0.0", AspNetCoreTestIndexFactory.ControllerMetadataName),
                },
            },
            _ => AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with
            {
                DeclaringType = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with
                {
                    Identity = AspNetCoreTestIndexFactory.EligibleControllerTypeIdentity() with
                    {
                        MetadataName = "AspNetCoreControllers.OtherController",
                    },
                },
            },
        };

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById", shape),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS006", diagnostic.Code);
    }

    [Fact]
    public async Task NonControllerAttributeProducesNoRoot()
    {
        // A ControllerBase-derived shape carrying the exact NonController attribute is deliberately
        // not a controller; no root is emitted even though every other eligibility fact holds.
        var index = OrdersIndex(includeNonController: true);

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task EligibleShapeStillEmitsExactRoot()
    {
        var index = OrdersIndex();

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById"),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.True(result.Recognized);
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/Orders/{id:guid}", entry.CanonicalRoute);
        Assert.Equal(SeqDoc.Core.Evidence.CertaintyLevel.Exact, entry.Certainty);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task VersionNineEligibleShapeEmitsExactRoot()
    {
        var index = OrdersIndex();
        var declaring = AspNetCoreTestIndexFactory.EligibleControllerTypeShape() with
        {
            BaseTypeChain =
            [
                new FrameworkTypeIdentity(
                    AspNetCoreTestIndexFactory.ControllerBaseAssembly,
                    "9.0.0.0",
                    AspNetCoreTestIndexFactory.ControllerBaseType),
                new FrameworkTypeIdentity("System.Private.CoreLib", "10.0.0.0", "System.Object"),
            ],
        };
        var shape = AspNetCoreTestIndexFactory.EligibleMethodShape("GetById") with { DeclaringType = declaring };

        var result = await Model.AnalyzeSymbolAsync(
            AspNetCoreTestIndexFactory.MethodSymbolDescriptor("GetById", shape),
            new FrameworkAnalysisContext(AspNetCoreTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.True(result.Recognized);
        var entry = Assert.Single(result.Facts.OfType<HttpEntryPointFact>());
        Assert.Equal("api/Orders/{id:guid}", entry.CanonicalRoute);
        Assert.Equal(SeqDoc.Core.Evidence.CertaintyLevel.Exact, entry.Certainty);
        Assert.Empty(result.Diagnostics);
    }

    private static ProgramIndexSnapshot OrdersIndex(bool includeNonController = false)
    {
        var attributes = ImmutableArray.CreateBuilder<ProgramAttributeApplication>();
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute));
        if (includeNonController)
        {
            attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, "Microsoft.AspNetCore.Mvc.NonControllerAttribute"));
        }

        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""));
        attributes.Add(AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));

        return AspNetCoreTestIndexFactory.ToIndex(
            [AspNetCoreTestIndexFactory.ControllerType()],
            methods,
            attributes.ToImmutable());
    }
}
