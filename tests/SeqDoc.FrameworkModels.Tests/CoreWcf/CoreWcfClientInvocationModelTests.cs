using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels.CoreWcf;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.CoreWcf;

/// <summary>
/// Contract/propagation/boundary tests for <see cref="CoreWcfServiceModel"/>'s client-invocation
/// admission (added on top of issue #5/#7's client-boundary facts), driven by hand-built exact
/// <see cref="FrameworkClientInvocationShapeDescriptor"/> inputs: exact receiver/target identity linking,
/// ClientBase-for-this-exact-contract matching, supported-shape gating, and weakest-certainty
/// propagation are proven here; the real Roslyn producer path and the Scenario Graph client-boundary
/// join are proven separately (Analysis.Tests, Scenarios.Tests).
/// </summary>
public sealed class CoreWcfClientInvocationModelTests
{
    private const string ClientTypeMetadataName = "CoreWcfServices.CalculatorSourceClient";
    private static readonly SymbolId ClientTypeSymbol = new($"symbol:v1:{ClientTypeMetadataName}");
    private static readonly MethodId CallerMethod = new("method:v1:CoreWcfServices.CalculatorClientCaller.CallAssigned");
    private static readonly OperationId InvocationOperationId = new("operation:v1:client-invocation:add");

    private static FrameworkMethodShape ClientMethodShape(
        ImmutableArray<FrameworkInterfaceMemberIdentity> members = default,
        bool isOrdinary = true,
        bool isStatic = false,
        bool isAbstract = false,
        int genericArity = 0,
        FrameworkTypeShape? declaringType = null,
        SymbolId? declaringTypeSymbol = null)
        => new(
            new SymbolId($"symbol:v1:{ClientTypeMetadataName}.Add"),
            declaringTypeSymbol ?? ClientTypeSymbol,
            IsOrdinary: isOrdinary,
            IsPublic: true,
            IsStatic: isStatic,
            IsAbstract: isAbstract,
            GenericArity: genericArity,
            DeclaringType: declaringType ?? CoreWcfTestIndexFactory.EligibleImplementationTypeShape(
                metadataName: ClientTypeMetadataName, clientBaseDerived: true),
            ImplementedInterfaceMembers: members.IsDefault ? [CoreWcfTestIndexFactory.InterfaceMember("Add")] : members);

    private static OperationDescriptor ClientInvocationOperation(
        FrameworkClientInvocationShapeDescriptor shape,
        CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(
            InvocationOperationId,
            CallerMethod,
            "Invocation",
            null,
            0,
            0,
            [CoreWcfTestIndexFactory.SourceEvidence("client-invocation")],
            certainty,
            ClientInvocationShape: shape);

    private static FrameworkClientInvocationShapeDescriptor Shape(
        FrameworkMethodShape? methodShape = null,
        SymbolId? receiverTypeSymbol = null,
        bool receiverIsConcreteType = true,
        ClientInvocationResultClaimKind resultClaim = ClientInvocationResultClaimKind.ResultAssigned,
        bool isAwaited = false,
        string? resultBindingName = "sum",
        string declaredResultType = "System.Double")
        => new(
            methodShape ?? ClientMethodShape(),
            receiverTypeSymbol ?? ClientTypeSymbol,
            receiverIsConcreteType,
            resultClaim,
            isAwaited,
            resultBindingName,
            declaredResultType);

    private static FrameworkAnalysisContext Context()
        => new(CoreWcfTestIndexFactory.Profile, CoreWcfTestIndexFactory.ToIndex([], [], []));

    [Fact]
    public async Task ExactClientInvocationAdmitsAServiceClientInvocationFactWithEveryFieldPropagated()
    {
        var operation = ClientInvocationOperation(Shape(isAwaited: true));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.Empty(result.Diagnostics);
        var fact = Assert.Single(result.Facts.OfType<ServiceClientInvocationFact>());
        Assert.Equal(CallerMethod, fact.CallerMethod);
        Assert.Equal(InvocationOperationId, fact.InvocationOperation);
        Assert.Equal(CoreWcfTestIndexFactory.ContractMetadataName, fact.ServiceContractType);
        Assert.Equal(CoreWcfTestIndexFactory.ContractSymbol, fact.ServiceContractTypeSymbol);
        Assert.Equal(ClientTypeMetadataName, fact.ClientType);
        Assert.Equal(ClientTypeSymbol, fact.ClientTypeSymbol);
        Assert.Equal("Add", fact.OperationName);
        Assert.Equal($"{CoreWcfTestIndexFactory.ContractMetadataName}.Add", fact.OperationKey);
        Assert.Equal(ClientInvocationResultClaimKind.ResultAssigned, fact.ResultClaim);
        Assert.True(fact.IsAwaited);
        Assert.Equal("sum", fact.ResultBindingName);
        Assert.Equal("System.Double", fact.DeclaredResultType);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.NotEmpty(fact.Evidence);
    }

    [Fact]
    public async Task AmbiguousInterfaceTypedReceiverNeverAdmitsAnInvocation()
    {
        var operation = ClientInvocationOperation(Shape(receiverIsConcreteType: false));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task ReceiverSymbolNotEqualToTheInvokedMethodsOwnDeclaringTypeNeverAdmitsAnInvocation()
    {
        // Simulates an invoked method inherited from a type other than the receiver's own exact static
        // type: even though the receiver is concrete, the identity link required to rule out a
        // reinterpreted-receiver ambiguity fails.
        var operation = ClientInvocationOperation(
            Shape(receiverTypeSymbol: new SymbolId("symbol:v1:CoreWcfServices.SomeOtherReceiverType")));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Theory]
    [InlineData(false, false, false, 1)] // generic
    [InlineData(false, true, false, 0)] // static
    [InlineData(false, false, true, 0)] // abstract
    [InlineData(true, false, false, 0)] // non-ordinary (e.g. property accessor)
    public async Task UnsupportedTargetMethodShapesNeverAdmitAnInvocation(bool isOrdinaryInverted, bool isStatic, bool isAbstract, int genericArity)
    {
        var operation = ClientInvocationOperation(Shape(
            methodShape: ClientMethodShape(isOrdinary: !isOrdinaryInverted, isStatic: isStatic, isAbstract: isAbstract, genericArity: genericArity)));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task DeclaringTypeNotDerivedFromClientBaseNeverAdmitsAnInvocation()
    {
        var operation = ClientInvocationOperation(Shape(
            methodShape: ClientMethodShape(declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(
                metadataName: ClientTypeMetadataName, clientBaseDerived: false))));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task ClientBaseConstructedForOneContractNeverAdmitsAnInvocationOfAnUnrelatedAdmittedContract()
    {
        // The type derives ClientBase<ICalculatorService> but the invoked method implements a
        // different, unrelated admitted interface directly, not through ClientBase. Matching per the
        // invoked member's own contract identity against ClientBase's constructed argument (not "some
        // admitted member exists somewhere on the type") must withhold admission.
        const string otherContractMetadataName = "CoreWcfServices.IClassicEchoService";
        var mismatchedMember = CoreWcfTestIndexFactory.InterfaceMember(
            "Echo", interfaceMetadataName: otherContractMetadataName);
        var operation = ClientInvocationOperation(Shape(
            methodShape: ClientMethodShape(
                members: [mismatchedMember],
                declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(
                    metadataName: ClientTypeMetadataName,
                    clientBaseDerived: true,
                    clientBaseContractMetadataName: CoreWcfTestIndexFactory.ContractMetadataName))));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task NoAdmittedContractMatchingMemberNeverAdmitsAnInvocation()
    {
        // Covers every same-shaped lookalike whose target method is not a genuine C# interface
        // implementation of the admitted operation — including a ref/out/in parameter-shape mismatch:
        // Roslyn's shared eligibility projector (FrameworkSymbolEligibilityProjector, exercised in
        // Analysis.Tests) never lists such a method in ImplementedInterfaceMembers, since the compiler
        // itself rejects it as an implementation. This is the same compiler-enforced guarantee the
        // already-accepted server-side IsEligibleServiceOperation relies on instead of re-deriving
        // parameter identity per model, so an empty member set is the single boundary that proves every
        // unsupported-shape negative (arity, ref/out/in, return type) fails closed here.
        var operation = ClientInvocationOperation(Shape(
            methodShape: ClientMethodShape(members: ImmutableArray<FrameworkInterfaceMemberIdentity>.Empty)));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task MoreThanOneAdmittedContractMatchingMemberNeverAdmitsAnInvocation()
    {
        var second = CoreWcfTestIndexFactory.InterfaceMember("Subtract");
        var operation = ClientInvocationOperation(Shape(
            methodShape: ClientMethodShape(members: [CoreWcfTestIndexFactory.InterfaceMember("Add"), second])));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task EmptyAttributeEvidenceFailsClosedWithAConservativeDiagnosticAndNoInvocation()
    {
        var memberWithoutEvidence = CoreWcfTestIndexFactory.InterfaceMember(
            "Add",
            typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(evidence: ImmutableArray<EvidenceRef>.Empty)],
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(evidence: ImmutableArray<EvidenceRef>.Empty)]);
        var operation = ClientInvocationOperation(Shape(
            methodShape: ClientMethodShape(members: [memberWithoutEvidence])));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF001", diagnostic.Code);
    }

    [Fact]
    public async Task DegradedOperationCertaintyPropagatesAsTheWeakestCertaintyOntoTheFactWithADiagnostic()
    {
        var operation = ClientInvocationOperation(Shape(), certainty: CertaintyLevel.Conservative);

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<ServiceClientInvocationFact>());
        Assert.Equal(CertaintyLevel.Conservative, fact.Certainty);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF004", diagnostic.Code);
    }

    [Theory]
    [InlineData(ClientInvocationResultClaimKind.Discarded)]
    [InlineData(ClientInvocationResultClaimKind.ResultReturned)]
    [InlineData(ClientInvocationResultClaimKind.Unclaimed)]
    public async Task EveryResultClaimKindPassesThroughUnchangedOntoTheFact(ClientInvocationResultClaimKind claim)
    {
        var operation = ClientInvocationOperation(Shape(resultClaim: claim, resultBindingName: null));

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        var fact = Assert.Single(result.Facts.OfType<ServiceClientInvocationFact>());
        Assert.Equal(claim, fact.ResultClaim);
        Assert.Null(fact.ResultBindingName);
    }

    [Fact]
    public async Task NonInvocationOperationKindNeverAdmitsAnInvocationEvenWithAClientInvocationShapePresent()
    {
        var operation = ClientInvocationOperation(Shape()) with { Kind = "Read" };

        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }
}
