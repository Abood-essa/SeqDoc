using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

/// <summary>
/// Adversarial coverage for the exact direct-outcome recognition contract. The model must match a
/// ControllerBase result helper by assembly, containing metadata type, metadata method name, generic
/// arity, parameter signature, return type, and the supported version table. Wrong assemblies,
/// wrong containing types, lookalike helper names, mismatched signatures, and non-constant or
/// malformed StatusCode arguments must never produce a guessed status code.
/// </summary>
public sealed class AspNetCoreControllerModelExactMatchAndStatusTests
{
    private const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
    private const string RouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string HttpGetAttribute = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";

    private static readonly AspNetCoreControllerModel Model = new();

    [Fact]
    public async Task WrongAssemblyNameIsNotMatched()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            new FrameworkMethodIdentity(
                AssemblyIdentity: "Microsoft.AspNetCore.Mvc.Core.Extra",
                ContainingMetadataType: AspNetCoreTestIndexFactory.ControllerBaseType,
                MethodMetadataName: "Ok",
                GenericArity: 0,
                Parameters: []));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task WrongContainingTypeIsNotMatched()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            new FrameworkMethodIdentity(
                AssemblyIdentity: AspNetCoreTestIndexFactory.ControllerBaseAssembly,
                ContainingMetadataType: "Microsoft.AspNetCore.Mvc.Controller",
                MethodMetadataName: "Ok",
                GenericArity: 0,
                Parameters: []));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task NonzeroGenericArityIsNotMatched()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            new FrameworkMethodIdentity(
                AssemblyIdentity: AspNetCoreTestIndexFactory.ControllerBaseAssembly,
                ContainingMetadataType: AspNetCoreTestIndexFactory.ControllerBaseType,
                MethodMetadataName: "Ok",
                GenericArity: 1,
                Parameters: [new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Object")]));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("ok")]
    [InlineData("OkAsync")]
    [InlineData("NotFount")]
    [InlineData("StatusCodeAsync")]
    public async Task LookalikeHelperNamesAreNotMatched(string helperName)
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity(helperName));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task WrongReturnTypeIsNotMatched()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            new FrameworkMethodIdentity(
                AssemblyIdentity: AspNetCoreTestIndexFactory.ControllerBaseAssembly,
                ContainingMetadataType: AspNetCoreTestIndexFactory.ControllerBaseType,
                MethodMetadataName: "Ok",
                GenericArity: 0,
                Parameters: [],
                ReturnType: "System.Int32"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task SameArityParameterTypeMismatchEmitsDiagnosticNotGuessedStatus()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("Ok", "System.String"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS002", diagnostic.Code);
    }

    [Fact]
    public async Task NullableAnnotationIsCanonicalizedBeforeSignatureMatch()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("CreatedAtAction", "System.String?", "System.Object?", "System.Object?"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<HttpDirectOutcomeFact>());
        Assert.Equal(201, fact.StatusCode);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task StatusCodeConstantInWrongArgumentPositionIsNotAdmitted()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "System.Object", "System.Int32"),
            "400");

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS002", diagnostic.Code);
    }

    [Fact]
    public async Task StatusCodeWithWrongArgumentCountIsNotAdmitted()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "System.Int32", "System.Int32", "System.Int32"),
            "400");

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS002", diagnostic.Code);
    }

    [Fact]
    public async Task StatusCodeWithWrongOrdinalConstantEmitsDiagnosticNotGuessedStatus()
    {
        var context = CreateContext(OrdersIndex());
        // An otherwise fully admitted StatusCode(int) target whose only compiler-proven constant
        // sits at ordinal 1 (not ordinal 0) is not an exact status.
        var operation = AspNetCoreTestIndexFactory.InvocationWithConstantArguments(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "System.Int32"),
            AspNetCoreTestIndexFactory.ConstantArgument(1, "System.Int32", "400"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS003", diagnostic.Code);
    }

    [Fact]
    public async Task StatusCodeWithWrongTypeConstantEmitsDiagnosticNotGuessedStatus()
    {
        var context = CreateContext(OrdersIndex());
        // A compiler-proven constant at ordinal 0 whose type is not an integer cannot be an exact
        // status even though its textual value looks numeric.
        var operation = AspNetCoreTestIndexFactory.InvocationWithConstantArguments(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "System.Int32"),
            AspNetCoreTestIndexFactory.ConstantArgument(0, "System.Double", "400.0"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS003", diagnostic.Code);
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("4.5")]
    [InlineData("abc")]
    public async Task StatusCodeWithNonIntegerOrOverflowConstantEmitsDiagnosticNotGuessedStatus(string constant)
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "System.Int32"),
            constant);

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS003", diagnostic.Code);
    }

    [Fact]
    public async Task StatusCodeExactStatusIsPreservedWithoutClamping()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.Invocation(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "System.Int32"),
            "-1");

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<HttpDirectOutcomeFact>());
        Assert.Equal(-1, fact.StatusCode);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(SeqDoc.Core.Identity.ParameterRefKind.Ref)]
    [InlineData(SeqDoc.Core.Identity.ParameterRefKind.Out)]
    [InlineData(SeqDoc.Core.Identity.ParameterRefKind.In)]
    [InlineData(SeqDoc.Core.Identity.ParameterRefKind.RefReadOnly)]
    public async Task RefKindLookalikeParametersProduceNoOutcomeAndDiagnostic(SeqDoc.Core.Identity.ParameterRefKind refKind)
    {
        var context = CreateContext(OrdersIndex());
        // An admitted Ok(object) lookalike carrying a ref/out/in/ref-readonly parameter is not an
        // admitted by-value signature and must not produce an exact status.
        var identity = AspNetCoreTestIndexFactory.ControllerBaseIdentity("Ok", "System.Object")
            with
        {
            Parameters = [new SeqDoc.Core.Identity.ParameterIdentityDescriptor(refKind, "System.Object")],
        };
        var operation = AspNetCoreTestIndexFactory.Invocation("GetById", identity);

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS002", diagnostic.Code);
    }

    [Fact]
    public async Task StatusCodeTwoArgumentConstantsAdmitExactOrdinalZeroStatus()
    {
        var context = CreateContext(OrdersIndex());
        // StatusCode(400, "message") has an exact status at ordinal 0 plus an unrelated constant at
        // ordinal 1; the status is still proven.
        var operation = AspNetCoreTestIndexFactory.InvocationWithConstantArguments(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "System.Int32", "System.Object"),
            AspNetCoreTestIndexFactory.ConstantArgument(0, "System.Int32", "400"),
            AspNetCoreTestIndexFactory.ConstantArgument(1, "System.String", "\"message\""));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<HttpDirectOutcomeFact>());
        Assert.Equal(400, fact.StatusCode);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task StatusCodeWithDuplicateOrdinalZeroConstantsEmitsDiagnosticNotGuessedStatus()
    {
        var context = CreateContext(OrdersIndex());
        var operation = AspNetCoreTestIndexFactory.InvocationWithConstantArguments(
            "GetById",
            AspNetCoreTestIndexFactory.ControllerBaseIdentity("StatusCode", "System.Int32"),
            AspNetCoreTestIndexFactory.ConstantArgument(0, "System.Int32", "400"),
            AspNetCoreTestIndexFactory.ConstantArgument(0, "System.Int32", "500"));

        var result = await Model.AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQAS003", diagnostic.Code);
    }

    private static FrameworkAnalysisContext CreateContext(ProgramIndexSnapshot index)
        => new(AspNetCoreTestIndexFactory.Profile, index);

    private static ProgramIndexSnapshot OrdersIndex()
    {
        var attributes = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, ApiControllerAttribute),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.ControllerSymbol, RouteAttribute, "\"api/[controller]\""),
            AspNetCoreTestIndexFactory.Attribute(AspNetCoreTestIndexFactory.MethodSymbol("GetById"), HttpGetAttribute, "\"{id:guid}\""));
        var methods = ImmutableArray.Create(
            AspNetCoreTestIndexFactory.Method("GetById", [("id", "System.Guid")]));

        return AspNetCoreTestIndexFactory.ToIndex(
            [AspNetCoreTestIndexFactory.ControllerType()],
            methods,
            attributes);
    }
}
