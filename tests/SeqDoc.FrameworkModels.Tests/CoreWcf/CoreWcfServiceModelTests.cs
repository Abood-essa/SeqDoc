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
    public async Task CapabilityAndFaultEvidenceUseOnlyExactTypedAttributesOnTheTarget()
    {
        var exactContract = CoreWcfTestIndexFactory.SourceEvidence("exact-contract");
        var exactOperation = CoreWcfTestIndexFactory.SourceEvidence("exact-operation");
        var exactFault = CoreWcfTestIndexFactory.SourceEvidence("exact-fault", CertaintyLevel.Conservative);
        var contaminant = CoreWcfTestIndexFactory.SourceEvidence("foreign-same-qualified-attribute", CertaintyLevel.Unknown);
        var faultType = new FrameworkTypeIdentity("CoreWcfServices", "1.0.0", "CoreWcfServices.NegativeSquareRootFault");

        var index = CoreWcfTestIndexFactory.ToIndex(
            [CoreWcfTestIndexFactory.ContractType(), CoreWcfTestIndexFactory.ImplementationType()],
            [CoreWcfTestIndexFactory.InterfaceMethod("SquareRoot"), CoreWcfTestIndexFactory.ImplementationMethod("SquareRoot")],
            [
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.ContractSymbol, CoreWcfTestIndexFactory.CoreWcfServiceContractAttribute, [exactContract, contaminant]),
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.InterfaceMethodSymbol("SquareRoot"), CoreWcfTestIndexFactory.CoreWcfOperationContractAttribute, [exactOperation, contaminant]),
            ]);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "SquareRoot",
            members: [CoreWcfTestIndexFactory.InterfaceMember(
                "SquareRoot",
                typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(evidence: [exactContract])],
                methodAttributes:
                [
                    CoreWcfTestIndexFactory.OperationContractAttribute(evidence: [exactOperation]),
                    CoreWcfTestIndexFactory.FaultContractAttribute(faultType, evidence: [exactFault]),
                ])]);

        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("SquareRoot", shape),
            new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.True(result.Recognized);
        var capability = Assert.Single(result.Facts.OfType<ServiceOperationCapabilityFact>());
        var fault = Assert.Single(result.Facts.OfType<ServiceFaultContractFact>());
        Assert.Equal(CertaintyLevel.Exact, capability.Certainty);
        Assert.Equal(CertaintyLevel.Conservative, fault.Certainty);
        Assert.Contains(capability.Evidence.Single().UnderlyingEvidence, evidence => evidence.Id.Value == exactContract.Id.Value);
        Assert.Contains(capability.Evidence.Single().UnderlyingEvidence, evidence => evidence.Id.Value == exactOperation.Id.Value);
        Assert.Contains(fault.Evidence.Single().UnderlyingEvidence, evidence => evidence.Id.Value == exactFault.Id.Value);
        Assert.Equal(CertaintyLevel.Conservative, fault.Evidence.Single().Certainty);
        Assert.DoesNotContain(capability.Evidence.Single().UnderlyingEvidence, evidence => evidence.Id.Value == contaminant.Id.Value);
        Assert.DoesNotContain(fault.Evidence.Single().UnderlyingEvidence, evidence => evidence.Id.Value == contaminant.Id.Value);

        var emptyEvidenceShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "SquareRoot",
            members: [CoreWcfTestIndexFactory.InterfaceMember(
                "SquareRoot",
                typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(evidence: ImmutableArray<EvidenceRef>.Empty)],
                methodAttributes:
                [
                    CoreWcfTestIndexFactory.OperationContractAttribute(evidence: ImmutableArray<EvidenceRef>.Empty),
                    CoreWcfTestIndexFactory.FaultContractAttribute(faultType, evidence: ImmutableArray<EvidenceRef>.Empty),
                ])]);
        var emptyEvidenceResult = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("SquareRoot", emptyEvidenceShape),
            new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.False(emptyEvidenceResult.Recognized);
        Assert.Empty(emptyEvidenceResult.Facts);
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
    public async Task ClientBoundaryCertaintyNeverExceedsTheTriggeringSymbolsCertainty()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var clientShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(clientBaseDerived: true));
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", clientShape, certainty: CertaintyLevel.Conservative),
            context, CancellationToken.None);

        Assert.True(result.Recognized);
        var clientFact = Assert.Single(result.Facts.OfType<ServiceClientBoundaryFact>());
        Assert.Equal(CertaintyLevel.Conservative, clientFact.Certainty);
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
    public async Task ClientBoundaryRequiresExactContractAndOperationEvidence()
    {
        var exactContract = CoreWcfTestIndexFactory.SourceEvidence("client-exact-contract", CertaintyLevel.Conservative);
        var exactOperation = CoreWcfTestIndexFactory.SourceEvidence("client-exact-operation");
        var contaminant = CoreWcfTestIndexFactory.SourceEvidence("client-foreign-same-qualified", CertaintyLevel.Unknown);
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members:
            [
                CoreWcfTestIndexFactory.InterfaceMember(
                    "Add",
                    typeAttributes:
                    [
                        CoreWcfTestIndexFactory.ServiceContractAttribute(evidence: [exactContract]),
                        new FrameworkAttributeApplicationIdentity(
                            new FrameworkTypeIdentity("Foreign.Assembly", "1.0.0.0", CoreWcfTestIndexFactory.CoreWcfServiceContractAttribute),
                            [], [contaminant]),
                    ],
                    methodAttributes:
                    [
                        CoreWcfTestIndexFactory.OperationContractAttribute(evidence: [exactOperation]),
                        new FrameworkAttributeApplicationIdentity(
                            new FrameworkTypeIdentity("Foreign.Assembly", "1.0.0.0", CoreWcfTestIndexFactory.CoreWcfOperationContractAttribute),
                            [], [contaminant]),
                    ]),
            ],
            declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(clientBaseDerived: true));

        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape),
            new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index),
            CancellationToken.None);

        var client = Assert.Single(result.Facts.OfType<ServiceClientBoundaryFact>());
        var underlying = client.Evidence.Single().UnderlyingEvidence;
        Assert.Contains(underlying, evidence => evidence.Id == exactContract.Id);
        Assert.Contains(underlying, evidence => evidence.Id == exactOperation.Id);
        Assert.DoesNotContain(underlying, evidence => evidence.Id == contaminant.Id);
        Assert.Equal(CertaintyLevel.Conservative, client.Certainty);

        var emptyEvidenceShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members:
            [
                CoreWcfTestIndexFactory.InterfaceMember(
                    "Add",
                    typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(evidence: ImmutableArray<EvidenceRef>.Empty)],
                    methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(evidence: ImmutableArray<EvidenceRef>.Empty)]),
            ],
            declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(clientBaseDerived: true));
        var emptyEvidenceResult = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", emptyEvidenceShape),
            new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index),
            CancellationToken.None);
        Assert.Empty(emptyEvidenceResult.Facts.OfType<ServiceClientBoundaryFact>());
    }

    [Fact]
    public async Task GeneratedClientWithEmptyMarkerEvidenceFailsClosed()
    {
        var index = Index("Add", typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()], methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(
                metadataName: "CoreWcfServices.CalculatorGeneratedClient", clientBaseDerived: true),
            declaringTypeAttributes: [CoreWcfTestIndexFactory.GeneratedCodeAttribute(ImmutableArray<EvidenceRef>.Empty)]);

        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape),
            new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index),
            CancellationToken.None);

        Assert.Empty(result.Facts.OfType<ServiceClientBoundaryFact>());
    }

    [Fact]
    public async Task ClientBaseConstructedForOneContractNeverEmitsABoundaryForAnUnrelatedAdmittedContract()
    {
        // The type derives ClientBase<ICalculatorService> (the admitted "Add" member's own contract) but
        // the triggering method here implements a DIFFERENT, unrelated admitted interface directly, not
        // through ClientBase. Finding ClientBase somewhere in the base chain must never be enough to
        // claim a client boundary for a contract ClientBase was never constructed with.
        const string otherContractMetadataName = "CoreWcfServices.IOtherContract";
        var index = Index(
            "Add",
            typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()],
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]);
        var mismatchedShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members:
            [
                CoreWcfTestIndexFactory.InterfaceMember(
                    "Add",
                    interfaceMetadataName: otherContractMetadataName,
                    typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute()],
                    methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute()]),
            ],
            declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(
                clientBaseDerived: true,
                clientBaseContractMetadataName: CoreWcfTestIndexFactory.ContractMetadataName));
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", mismatchedShape), context, CancellationToken.None);

        Assert.Empty(result.Facts.OfType<ServiceClientBoundaryFact>());
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
    public async Task UnprovenHostChainNeverProducesARegistrationFactDespiteTheExactEndpointShape()
    {
        // An exact AddServiceEndpoint<TService,TContract>(Binding,string) invocation proves only that
        // source contains a call with that compiler identity; it does not prove the application actually
        // registers or dispatches it. Only an invocation the CoreWcfHostChainScanner proved reachable
        // through the complete active host chain may produce registration evidence.
        var operation = CoreWcfTestIndexFactory.ServiceEndpointOperation("Add", hostChainProven: false);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, CoreWcfTestIndexFactory.ToIndex([], [], []));
        var result = await new CoreWcfServiceModel().AnalyzeOperationAsync(operation, context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
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

    // ---- Issue #41: measured net9.0 classic-WCF compatibility tuples, threaded atomically ----

    [Theory]
    [InlineData(CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800)]
    [InlineData(CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V810)]
    public async Task MeasuredNet9ClassicTupleAdmitsOneCapabilityFact(string serviceModelVersion)
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
                typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false, classicAssemblyVersion: serviceModelVersion)],
                methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false, classicAssemblyVersion: serviceModelVersion)])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<ServiceOperationCapabilityFact>());
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
    }

    [Fact]
    public async Task ClassicServiceContractAndOperationContractOnDifferentSupportedTuplesNeverAdmit()
    {
        // Mixed-tuple: an 8.0.0.0 ServiceContract paired with an 8.1.0.0 OperationContract. The two
        // attributes resolve to different atomic tuples, so the pair is never admitted (fails closed,
        // no diagnostic) exactly like the existing mixed-family case.
        var index = Index(
            "Add",
            typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false)],
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false)]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember(
                "Add",
                typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false, classicAssemblyVersion: CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800)],
                methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false, classicAssemblyVersion: CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V810)])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task UnsupportedSystemServiceModelVersionNeverAdmitsAndIsSilent()
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
                typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false, classicAssemblyVersion: "8.2.0.0")],
                methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false, classicAssemblyVersion: "8.2.0.0")])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ForeignAssemblyWithClassicMetadataNameAtSupportedVersionNeverAdmits()
    {
        var index = Index(
            "Add",
            typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false)],
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false)]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var foreignContract = new FrameworkAttributeApplicationIdentity(
            new FrameworkTypeIdentity("Foreign.Assembly", CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800, CoreWcfTestIndexFactory.SystemServiceModelServiceContractAttribute),
            [], [CoreWcfTestIndexFactory.SourceEvidence("foreign-classic-contract")]);
        var foreignOperation = new FrameworkAttributeApplicationIdentity(
            new FrameworkTypeIdentity("Foreign.Assembly", CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800, CoreWcfTestIndexFactory.SystemServiceModelOperationContractAttribute),
            [], [CoreWcfTestIndexFactory.SourceEvidence("foreign-classic-operation")]);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember("Add", typeAttributes: [foreignContract], methodAttributes: [foreignOperation])]);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Net9TupleGeneratedClientMarkerPromotesToGeneratedClientBoundary()
    {
        var result = await AnalyzeNet9ClientAsync(
            CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800,
            clientBaseAssemblyVersion: CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800,
            markerAssemblyVersion: CoreWcfTestIndexFactory.GeneratedMarkerAssemblyVersionNet9);

        Assert.True(result.Recognized);
        var clientFact = Assert.Single(result.Facts.OfType<ServiceClientBoundaryFact>());
        Assert.Equal(ServiceClientKind.GeneratedClient, clientFact.ClientKind);
    }

    [Fact]
    public async Task CrossTupleGeneratedMarkerNeverPromotesToGeneratedClient()
    {
        // An 8.0.0.0 contract with a 10.0.0.0 (net10 tuple) marker: the marker belongs to a different
        // atomic tuple, so it must not match. The boundary still admits, classified SourceClient.
        var result = await AnalyzeNet9ClientAsync(
            CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800,
            clientBaseAssemblyVersion: CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800,
            markerAssemblyVersion: CoreWcfTestIndexFactory.CoreLibAssemblyVersion);

        Assert.True(result.Recognized);
        var clientFact = Assert.Single(result.Facts.OfType<ServiceClientBoundaryFact>());
        Assert.Equal(ServiceClientKind.SourceClient, clientFact.ClientKind);
    }

    [Fact]
    public async Task ClientBaseOnADifferentTupleThanTheContractNeverEmitsABoundary()
    {
        // Contract attributes resolve to the 8.0.0.0 tuple but ClientBase<T> is the 8.1.2.0 identity:
        // the ClientBase version is threaded through the same resolved tuple, so this fails closed.
        var result = await AnalyzeNet9ClientAsync(
            CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800,
            clientBaseAssemblyVersion: CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersion,
            markerAssemblyVersion: CoreWcfTestIndexFactory.GeneratedMarkerAssemblyVersionNet9);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts.OfType<ServiceClientBoundaryFact>());
    }

    [Fact]
    public async Task Net9TupleClientBaseWhoseContractDoesNotResolveFailsClosedWithConservativeDiagnosticAndNoFacts()
    {
        // I41-F1: the coarse HasClientBase gate now accepts 8.0.0.0, so a type deriving
        // System.ServiceModel.ClientBase<T> at that version whose contract interface carries no admitted
        // [ServiceContract] enters the client branch of AnalyzeSymbol, finds clientMembers.Length == 0,
        // and fails closed with the conservative EligibilityShapeUnavailable (SEQWCF001) diagnostic --
        // the same conservative outcome the pre-existing 8.1.2.0 ClientBase shape already produced. No
        // fact is fabricated.
        const string v800 = CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800;
        var unresolvableMember = CoreWcfTestIndexFactory.InterfaceMember(
            "Add",
            typeAttributes: ImmutableArray<FrameworkAttributeApplicationIdentity>.Empty,
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false, classicAssemblyVersion: v800)]);
        var index = Index(
            "Add",
            typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false)],
            methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false)]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var declaringType = CoreWcfTestIndexFactory.EligibleImplementationTypeShape(
            metadataName: "CoreWcfServices.UnresolvableNet9Client",
            clientBaseDerived: true,
            clientBaseAssemblyVersion: v800);
        var shape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [unresolvableMember],
            declaringType: declaringType);

        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts.OfType<ServiceClientBoundaryFact>());
        Assert.Empty(result.Facts.OfType<ServiceClientInvocationFact>());
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF001", diagnostic.Code);

        // AnalyzeClientInvocation stays fully silent for the same shape: Unrecognized, no fact, no
        // diagnostic (the invocation path never emits the eligibility diagnostic on this branch).
        var invocationMethodShape = new FrameworkMethodShape(
            CoreWcfTestIndexFactory.ImplementationMethodSymbol("Add"),
            CoreWcfTestIndexFactory.ImplementationSymbol,
            IsOrdinary: true,
            IsPublic: true,
            IsStatic: false,
            IsAbstract: false,
            GenericArity: 0,
            DeclaringType: declaringType,
            ImplementedInterfaceMembers: [unresolvableMember]);
        var invocationShape = new FrameworkClientInvocationShapeDescriptor(
            invocationMethodShape,
            CoreWcfTestIndexFactory.ImplementationSymbol,
            true,
            ClientInvocationResultClaimKind.ResultAssigned,
            false,
            "sum",
            "System.Double");
        var invocationOperation = new OperationDescriptor(
            new OperationId("operation:v1:client-invocation:add"),
            new MethodId("method:v1:CoreWcfServices.Caller.Call"),
            "Invocation",
            null,
            0,
            0,
            [CoreWcfTestIndexFactory.SourceEvidence("client-invocation")],
            CertaintyLevel.Exact,
            ClientInvocationShape: invocationShape);
        var invocationResult = await new CoreWcfServiceModel().AnalyzeOperationAsync(
            invocationOperation, context, CancellationToken.None);

        Assert.False(invocationResult.Recognized);
        Assert.Empty(invocationResult.Facts);
        Assert.Empty(invocationResult.Diagnostics);
    }

    [Fact]
    public async Task Net9TupleClientBoundaryNeverStrengthensADegradedTriggeringCertainty()
    {
        // I41-F2: a net9 (8.0.0.0, 9.0.0.0) generated client whose triggering symbol certainty is
        // Conservative must carry that weaker certainty onto the ServiceClientBoundaryFact, never Exact.
        var result = await AnalyzeNet9ClientAsync(
            CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800,
            clientBaseAssemblyVersion: CoreWcfTestIndexFactory.SystemServiceModelAssemblyVersionNet9V800,
            markerAssemblyVersion: CoreWcfTestIndexFactory.GeneratedMarkerAssemblyVersionNet9,
            certainty: CertaintyLevel.Conservative);

        Assert.True(result.Recognized);
        var clientFact = Assert.Single(result.Facts.OfType<ServiceClientBoundaryFact>());
        Assert.NotEqual(CertaintyLevel.Exact, clientFact.Certainty);
        Assert.Equal(CertaintyLevel.Conservative, clientFact.Certainty);
    }

    private static async Task<ModelResult> AnalyzeNet9ClientAsync(
        string serviceModelVersion,
        string clientBaseAssemblyVersion,
        string markerAssemblyVersion,
        CertaintyLevel certainty = CertaintyLevel.Exact)
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
                typeAttributes: [CoreWcfTestIndexFactory.ServiceContractAttribute(coreWcf: false, classicAssemblyVersion: serviceModelVersion)],
                methodAttributes: [CoreWcfTestIndexFactory.OperationContractAttribute(coreWcf: false, classicAssemblyVersion: serviceModelVersion)])],
            declaringType: CoreWcfTestIndexFactory.EligibleImplementationTypeShape(
                metadataName: "CoreWcfServices.GeneratedNet9Client",
                clientBaseDerived: true,
                clientBaseAssemblyVersion: clientBaseAssemblyVersion),
            declaringTypeAttributes: [CoreWcfTestIndexFactory.GeneratedCodeAttribute(markerAssemblyVersion: markerAssemblyVersion)]);
        return await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", shape, certainty: certainty), context, CancellationToken.None);
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
