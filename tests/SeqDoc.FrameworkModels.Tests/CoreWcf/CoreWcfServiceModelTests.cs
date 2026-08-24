using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels.CoreWcf;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.CoreWcf;

/// <summary>
/// Contract/propagation/boundary tests for <see cref="CoreWcfServiceModel"/> driven by hand-built exact
/// Program Index and eligibility-shape inputs. Capability admission, registration detection, fault and
/// client-boundary classification, strict identity/family matching, and weakest-certainty propagation
/// are proven here; the real Roslyn producer path and the capability-registration Scenario Graph join
/// are proven separately (Analysis.Tests, Scenarios.Tests).
/// </summary>
public sealed class CoreWcfServiceModelTests
{
    [Fact]
    public async Task ExactServiceContractOperationAdmitsOneCapabilityFact()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add"), context, CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.Empty(result.Diagnostics);
        var fact = Assert.Single(result.Facts.OfType<ServiceOperationCapabilityFact>());
        Assert.Equal(CoreWcfTestIndexFactory.ContractMetadataName, fact.ServiceContractType);
        Assert.Equal(CoreWcfTestIndexFactory.ImplementationMetadataName, fact.ImplementationType);
        Assert.Equal("Add", fact.OperationName);
        Assert.Equal($"{CoreWcfTestIndexFactory.ContractMetadataName}.Add", fact.OperationKey);
        Assert.Equal(CoreWcfTestIndexFactory.ImplementationMethodId("Add"), fact.RootMethod);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.NotEmpty(fact.Evidence);
    }

    [Fact]
    public async Task InterfaceMissingServiceContractNeverAdmitsCapability()
    {
        var index = Index("Add", typeAttributes: [], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember("Add", typeAttributes: [], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task OperationMissingOperationContractNeverAdmitsCapability()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: []);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task MixedFamilyAttributesNeverAdmitCapability()
    {
        var index = Index(
            "Add",
            typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: true)],
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false)]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember(
                "Add",
                typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: true)],
                methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false)])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ForeignAssemblySameQualifiedNameAttributeNeverAdmitsCapability()
    {
        var index = Index(
            "Add",
            typeAttributes: [CoreWcfTestIndexFactory.ForeignAssemblyServiceContractAttribute()],
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember(
                "Add",
                typeAttributes: [CoreWcfTestIndexFactory.ForeignAssemblyServiceContractAttribute()],
                methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task MissingSourceBodyFailsClosedWithConservativeDiagnosticAndNoCapability()
    {
        var index = CoreWcfTestIndexFactory.ToIndex(
            [CoreWcfTestIndexFactory.ContractType(), CoreWcfTestIndexFactory.ImplementationType()],
            [CoreWcfTestIndexFactory.InterfaceMethod("Add"), CoreWcfTestIndexFactory.ImplementationMethod("Add", withBody: false)],
            [
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.ContractSymbol, CoreWcfTestIndexFactory.CoreWcfServiceContractAttribute),
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.InterfaceMethodSymbol("Add"), CoreWcfTestIndexFactory.CoreWcfOperationContractAttribute),
            ]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF002", diagnostic.Code);
    }

    [Fact]
    public async Task MissingEligibilityShapeFailsClosedWithConservativeDiagnosticAndNoCapability()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptorWithoutShape("Add"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF001", diagnostic.Code);
    }

    [Fact]
    public async Task ShapeBoundToAnotherMethodFailsClosedWithConservativeDiagnosticAndNoCapability()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var mismatchedShape = CoreWcfTestIndexFactory.EligibleMethodShape("Subtract");
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", mismatchedShape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF001", diagnostic.Code);
    }

    [Fact]
    public async Task NonExactInputCertaintyIsNeverPromotedAndEmitsADiagnostic()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var symbol = CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", certainty: CertaintyLevel.Conservative);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(symbol, context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<ServiceOperationCapabilityFact>());
        Assert.Equal(CertaintyLevel.Conservative, fact.Certainty);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF004", diagnostic.Code);
    }

    [Fact]
    public async Task MethodImplementingTwoAdmittedContractsIsAmbiguousAndAdmitsNoCapability()
    {
        const string secondContract = "CoreWcfServices.ISecondCalculator";
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var ambiguousShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members:
            [
                CoreWcfTestIndexFactory.InterfaceMember("Add"),
                CoreWcfTestIndexFactory.InterfaceMember("Add", interfaceMetadataName: secondContract),
            ]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", ambiguousShape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF003", diagnostic.Code);
    }

    [Fact]
    public async Task AbstractImplementingTypeNeverAdmitsCapability()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var abstractShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add", declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(isAbstract: true));
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", abstractShape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ExplicitInterfaceImplementationAdmitsCapabilityDespiteNonPublicMethod()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        // Roslyn reports MethodKind.ExplicitInterfaceImplementation (never Ordinary) for an explicitly
        // implemented interface member, so the shape must reflect isOrdinary: false here to prove the
        // model admits capability from the real compiler-reported kind, not an idealized one.
        var explicitShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember("Add", isExplicit: true)],
            isPublic: false,
            isOrdinary: false);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", explicitShape), context, CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.Single(result.Facts.OfType<ServiceOperationCapabilityFact>());
    }

    [Fact]
    public async Task NonOrdinaryImplicitMethodKindNeverAdmitsCapability()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember("Add", isExplicit: false)],
            isOrdinary: false);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task ClassicSystemServiceModelIdentityIsAlsoAdmitted()
    {
        var index = Index(
            "Add",
            typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false)],
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false)]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember(
                "Add",
                typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false)],
                methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false)])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<ServiceOperationCapabilityFact>());
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
    }

    [Fact]
    public async Task FaultContractAttributeProducesAServiceFaultContractFact()
    {
        var faultType = new FrameworkTypeIdentity("CoreWcfServices", "1.0.0", "CoreWcfServices.NegativeSquareRootFault");
        var index = Index("SquareRoot", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "SquareRoot",
            members: [CoreWcfTestIndexFactory.InterfaceMember(
                "SquareRoot",
                methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(), CoreWcfTestIndexFactory.FaultContractAttribute(faultType)])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("SquareRoot", shape), context, CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.Single(result.Facts.OfType<ServiceOperationCapabilityFact>());
        var faultFact = Assert.Single(result.Facts.OfType<ServiceFaultContractFact>());
        Assert.Equal("CoreWcfServices.NegativeSquareRootFault", faultFact.FaultType);
        Assert.Equal("SquareRoot", faultFact.OperationName);
    }

    [Fact]
    public async Task ClientBaseDerivedTypeNeverAdmitsCapabilityButEmitsSourceClientBoundary()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var clientShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(clientBaseDerived: true));
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", clientShape), context, CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.Empty(result.Facts.OfType<ServiceOperationCapabilityFact>());
        var clientFact = Assert.Single(result.Facts.OfType<ServiceClientBoundaryFact>());
        Assert.Equal(ServiceClientKind.SourceClient, clientFact.ClientKind);
        // The client boundary fact's type name comes from the Program Index (the same "type" resolved
        // for the method), not the shape's own declaring-type identity string.
        Assert.Equal(CoreWcfTestIndexFactory.ImplementationMetadataName, clientFact.ClientType);
        Assert.Equal(CoreWcfTestIndexFactory.ContractMetadataName, clientFact.ServiceContractType);
    }

    [Fact]
    public async Task GeneratedCodeMarkedClientBaseDerivedTypeEmitsGeneratedClientBoundary()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var clientShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(metadataName: "CoreWcfServices.CalculatorGeneratedClient", clientBaseDerived: true),
            declaringTypeAttributes: [CoreWcfTestIndexFactory.GeneratedCodeAttribute()]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", clientShape), context, CancellationToken.None);

        Assert.True(result.Recognized);
        var clientFact = Assert.Single(result.Facts.OfType<ServiceClientBoundaryFact>());
        Assert.Equal(ServiceClientKind.GeneratedClient, clientFact.ClientKind);
    }

    [Fact]
    public async Task ExactAddServiceEndpointInvocationProducesARegistrationFact()
    {
        var operation = CoreWcfTestIndexFactory.ServiceEndpointOperation("Add");
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, CoreWcfTestIndexFactory.ToIndex([], [], []));
        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<ServiceEndpointRegistrationFact>());
        Assert.Equal(CoreWcfTestIndexFactory.ImplementationMetadataName, fact.ImplementationType);
        Assert.Equal(CoreWcfTestIndexFactory.ContractMetadataName, fact.ServiceContractType);
        Assert.Equal("CoreWCF.BasicHttpBinding", fact.BindingType);
        Assert.Equal("/CalculatorService/basicHttp", fact.Address);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
    }

    [Fact]
    public async Task NonConstantAddressStillProducesARegistrationFactWithoutAnAddress()
    {
        var operation = CoreWcfTestIndexFactory.ServiceEndpointOperation("Add", address: null);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, CoreWcfTestIndexFactory.ToIndex([], [], []));
        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<ServiceEndpointRegistrationFact>());
        Assert.Null(fact.Address);
    }

    [Fact]
    public async Task DistinctOperationsProduceDeterministicRepeatableCapabilityIdentities()
    {
        var index = CoreWcfTestIndexFactory.ToIndex(
            [CoreWcfTestIndexFactory.ContractType(), CoreWcfTestIndexFactory.ImplementationType()],
            [
                CoreWcfTestIndexFactory.InterfaceMethod("Add"),
                CoreWcfTestIndexFactory.InterfaceMethod("Subtract"),
                CoreWcfTestIndexFactory.ImplementationMethod("Add"),
                CoreWcfTestIndexFactory.ImplementationMethod("Subtract"),
            ],
            [
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.ContractSymbol, CoreWcfTestIndexFactory.CoreWcfServiceContractAttribute),
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.InterfaceMethodSymbol("Add"), CoreWcfTestIndexFactory.CoreWcfOperationContractAttribute),
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.InterfaceMethodSymbol("Subtract"), CoreWcfTestIndexFactory.CoreWcfOperationContractAttribute),
            ]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var model = new CoreWcfServiceModel();
        var addResult = await model.AnalyzeSymbolAsync(CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add"), context, CancellationToken.None);
        var addResultAgain = await model.AnalyzeSymbolAsync(CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add"), context, CancellationToken.None);
        var subtractResult = await model.AnalyzeSymbolAsync(CoreWcfTestIndexFactory.MethodSymbolDescriptor("Subtract"), context, CancellationToken.None);

        var addFact = Assert.Single(addResult.Facts.OfType<ServiceOperationCapabilityFact>());
        var addFactAgain = Assert.Single(addResultAgain.Facts.OfType<ServiceOperationCapabilityFact>());
        var subtractFact = Assert.Single(subtractResult.Facts.OfType<ServiceOperationCapabilityFact>());

        Assert.Equal(addFact.Id, addFactAgain.Id);
        Assert.Equal(addFact.OperationKey, addFactAgain.OperationKey);
        Assert.NotEqual(addFact.Id, subtractFact.Id);
        Assert.NotEqual(addFact.OperationKey, subtractFact.OperationKey);
    }

    private static ProgramIndexSnapshot Index(
        string operationName,
        ImmutableArray<FrameworkAttributeApplicationIdentity> typeAttributes,
        ImmutableArray<FrameworkAttributeApplicationIdentity> methodAttributes)
    {
        var attributes = ImmutableArray.CreateBuilder<ProgramAttributeApplication>();
        foreach (var attribute in typeAttributes)
        {
            attributes.Add(CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.ContractSymbol, attribute.AttributeType.MetadataName));
        }

        foreach (var attribute in methodAttributes)
        {
            attributes.Add(CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.InterfaceMethodSymbol(operationName), attribute.AttributeType.MetadataName));
        }

        return CoreWcfTestIndexFactory.ToIndex(
            [CoreWcfTestIndexFactory.ContractType(), CoreWcfTestIndexFactory.ImplementationType()],
            [CoreWcfTestIndexFactory.InterfaceMethod(operationName), CoreWcfTestIndexFactory.ImplementationMethod(operationName)],
            attributes.ToImmutable());
    }
}
