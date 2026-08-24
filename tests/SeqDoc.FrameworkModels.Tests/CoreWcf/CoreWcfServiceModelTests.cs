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
/// Program Index and eligibility-shape inputs. These prove the model's own admission and evidence
/// rules; the real Roslyn producer path is proven separately by the Analysis-layer fixture tests.
/// </summary>
public sealed class CoreWcfServiceModelTests
{
    [Fact]
    public async Task ExactServiceContractOperationAdmitsOneServiceOperationEntryPointFact()
    {
        var index = Index("Add", withServiceContract: true, withOperationContract: true);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add"), context, CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.Empty(result.Diagnostics);
        var fact = Assert.Single(result.Facts.OfType<ServiceOperationEntryPointFact>());
        Assert.Equal(CoreWcfTestIndexFactory.ContractMetadataName, fact.ServiceContractType);
        Assert.Equal(CoreWcfTestIndexFactory.ImplementationMetadataName, fact.ImplementationType);
        Assert.Equal("Add", fact.OperationName);
        Assert.Equal($"{CoreWcfTestIndexFactory.ContractMetadataName}.Add", fact.OperationKey);
        Assert.Equal(CoreWcfTestIndexFactory.ImplementationMethodId("Add"), fact.RootMethod);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.NotEmpty(fact.Evidence);
    }

    [Fact]
    public async Task InterfaceMissingServiceContractNeverAdmitsARoot()
    {
        var index = Index("Add", withServiceContract: false, withOperationContract: true);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task OperationMissingOperationContractNeverAdmitsARoot()
    {
        var index = Index("Add", withServiceContract: true, withOperationContract: false);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task MissingSourceBodyFailsClosedWithConservativeDiagnosticAndNoRoot()
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
    public async Task MissingEligibilityShapeFailsClosedWithConservativeDiagnosticAndNoRoot()
    {
        var index = Index("Add", withServiceContract: true, withOperationContract: true);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptorWithoutShape("Add"), context, CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF001", diagnostic.Code);
    }

    [Fact]
    public async Task ShapeBoundToAnotherMethodFailsClosedWithConservativeDiagnosticAndNoRoot()
    {
        var index = Index("Add", withServiceContract: true, withOperationContract: true);
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
        var index = Index("Add", withServiceContract: true, withOperationContract: true);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var symbol = CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", certainty: CertaintyLevel.Conservative);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(symbol, context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<ServiceOperationEntryPointFact>());
        Assert.Equal(CertaintyLevel.Conservative, fact.Certainty);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQWCF004", diagnostic.Code);
    }

    [Fact]
    public async Task MethodImplementingTwoAdmittedContractsIsAmbiguousAndAdmitsNoRoot()
    {
        const string secondContract = "CoreWcfServices.ISecondCalculator";
        var secondContractSymbol = new SymbolId($"symbol:v1:{secondContract}");
        var secondMethodSymbol = new SymbolId($"symbol:v1:{secondContract}.Add");
        var index = CoreWcfTestIndexFactory.ToIndex(
            [CoreWcfTestIndexFactory.ContractType(), CoreWcfTestIndexFactory.ImplementationType()],
            [CoreWcfTestIndexFactory.InterfaceMethod("Add"), CoreWcfTestIndexFactory.ImplementationMethod("Add")],
            [
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.ContractSymbol, CoreWcfTestIndexFactory.CoreWcfServiceContractAttribute),
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.InterfaceMethodSymbol("Add"), CoreWcfTestIndexFactory.CoreWcfOperationContractAttribute),
                CoreWcfTestIndexFactory.Attribute(secondContractSymbol, CoreWcfTestIndexFactory.CoreWcfServiceContractAttribute),
                CoreWcfTestIndexFactory.Attribute(secondMethodSymbol, CoreWcfTestIndexFactory.CoreWcfOperationContractAttribute),
            ]);
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
    public async Task AbstractImplementingTypeNeverAdmitsARoot()
    {
        var index = Index("Add", withServiceContract: true, withOperationContract: true);
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
    public async Task ExplicitInterfaceImplementationAdmitsARootDespiteNonPublicMethod()
    {
        var index = Index("Add", withServiceContract: true, withOperationContract: true);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var explicitShape = CoreWcfTestIndexFactory.EligibleMethodShape(
            "Add",
            members: [CoreWcfTestIndexFactory.InterfaceMember("Add", isExplicit: true)],
            isPublic: false);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add", explicitShape), context, CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.Single(result.Facts.OfType<ServiceOperationEntryPointFact>());
    }

    [Fact]
    public async Task ClassicSystemServiceModelIdentityIsAlsoAdmitted()
    {
        var index = CoreWcfTestIndexFactory.ToIndex(
            [CoreWcfTestIndexFactory.ContractType(), CoreWcfTestIndexFactory.ImplementationType()],
            [CoreWcfTestIndexFactory.InterfaceMethod("Add"), CoreWcfTestIndexFactory.ImplementationMethod("Add")],
            [
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.ContractSymbol, CoreWcfTestIndexFactory.SystemServiceModelServiceContractAttribute),
                CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.InterfaceMethodSymbol("Add"), CoreWcfTestIndexFactory.SystemServiceModelOperationContractAttribute),
            ]);
        var context = new FrameworkAnalysisContext(CoreWcfTestIndexFactory.Profile, index);
        var result = await new CoreWcfServiceModel().AnalyzeSymbolAsync(
            CoreWcfTestIndexFactory.MethodSymbolDescriptor("Add"), context, CancellationToken.None);

        Assert.True(result.Recognized);
        var fact = Assert.Single(result.Facts.OfType<ServiceOperationEntryPointFact>());
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
    }

    [Fact]
    public async Task DistinctOperationsProduceDistinctDeterministicEntryPointIdentitiesAndRepeatedAnalysisIsStable()
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

        var addFact = Assert.Single(addResult.Facts.OfType<ServiceOperationEntryPointFact>());
        var addFactAgain = Assert.Single(addResultAgain.Facts.OfType<ServiceOperationEntryPointFact>());
        var subtractFact = Assert.Single(subtractResult.Facts.OfType<ServiceOperationEntryPointFact>());

        Assert.Equal(addFact.EntryPointId, addFactAgain.EntryPointId);
        Assert.Equal(addFact.Id, addFactAgain.Id);
        Assert.NotEqual(addFact.EntryPointId, subtractFact.EntryPointId);
    }

    private static ProgramIndexSnapshot Index(string operationName, bool withServiceContract, bool withOperationContract)
    {
        var attributes = ImmutableArray.CreateBuilder<ProgramAttributeApplication>();
        if (withServiceContract)
        {
            attributes.Add(CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.ContractSymbol, CoreWcfTestIndexFactory.CoreWcfServiceContractAttribute));
        }

        if (withOperationContract)
        {
            attributes.Add(CoreWcfTestIndexFactory.Attribute(CoreWcfTestIndexFactory.InterfaceMethodSymbol(operationName), CoreWcfTestIndexFactory.CoreWcfOperationContractAttribute));
        }

        return CoreWcfTestIndexFactory.ToIndex(
            [CoreWcfTestIndexFactory.ContractType(), CoreWcfTestIndexFactory.ImplementationType()],
            [CoreWcfTestIndexFactory.InterfaceMethod(operationName), CoreWcfTestIndexFactory.ImplementationMethod(operationName)],
            attributes.ToImmutable());
    }
}
