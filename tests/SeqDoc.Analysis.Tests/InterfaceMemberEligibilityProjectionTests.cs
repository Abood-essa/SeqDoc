using Microsoft.CodeAnalysis;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Analysis.Roslyn.Toolchains;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// Producer-proof tests for the generic exact interface-member-implementation compiler fact
/// (issue #5): a concrete method's compiler-proven claim that it implements a specific interface
/// member, which lets a downstream model (issue #7's CoreWCF service model) find exact attributes
/// applied to that interface member (for example <c>[ServiceContract]</c>/<c>[OperationContract]</c>)
/// without rescanning source. The fixture is realistic CoreWCF-shaped source compiled through the real
/// Roslyn Program Index pipeline; this test never hand-builds the shape it verifies.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class InterfaceMemberEligibilityProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj";
    private const string CalculatorServiceMetadataName = "CoreWcfServices.CalculatorService";
    private const string ExplicitCalculatorServiceMetadataName = "CoreWcfServices.ExplicitCalculatorService";
    private const string UtilityHelperMetadataName = "CoreWcfServices.UtilityHelper";
    private const string FakeServiceMetadataName = "CoreWcfServices.FakeService";
    private const string CalculatorContractMetadataName = "CoreWcfServices.ICalculatorService";
    private const string ServiceContractAttribute = "CoreWCF.ServiceContractAttribute";
    private const string OperationContractAttribute = "CoreWCF.OperationContractAttribute";

    [Fact]
    public async Task PassCFixtureIndexSuppliesExactServiceContractAndOperationContractAttributesWithEvidence()
    {
        var request = CreateFixtureRequest();
        var result = await new RoslynProgramIndexBuilder().BuildAsync(request, CancellationToken.None);

        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));
        var index = Assert.IsType<ProgramIndexSnapshot>(result.Value);

        var contract = Assert.Single(index.Types, type => type.MetadataName == CalculatorContractMetadataName);
        var serviceContract = Assert.Single(index.Attributes, attribute =>
            attribute.Target == contract.Id && attribute.AttributeType == ServiceContractAttribute);
        Assert.NotEmpty(serviceContract.Evidence);

        var addOperation = Assert.Single(index.Methods, method => method.ContainingType == contract.Id && method.Name == "Add");
        var operationContract = Assert.Single(index.Attributes, attribute =>
            attribute.Target == addOperation.Symbol && attribute.AttributeType == OperationContractAttribute);
        Assert.NotEmpty(operationContract.Evidence);

        // The sibling operation is deliberately not [OperationContract]: the interface's own attribute
        // set must never invent that attribute for it.
        var modulo = Assert.Single(index.Methods, method => method.ContainingType == contract.Id && method.Name == "Modulo");
        Assert.DoesNotContain(index.Attributes, attribute => attribute.Target == modulo.Symbol && attribute.AttributeType == OperationContractAttribute);

        // IUtility carries an admitted OperationContract operation but never ServiceContract.
        var utilityContract = Assert.Single(index.Types, type => type.MetadataName == "CoreWcfServices.IUtility");
        Assert.DoesNotContain(index.Attributes, attribute => attribute.Target == utilityContract.Id && attribute.AttributeType == ServiceContractAttribute);
        var ping = Assert.Single(index.Methods, method => method.ContainingType == utilityContract.Id && method.Name == "Ping");
        Assert.Contains(index.Attributes, attribute => attribute.Target == ping.Symbol && attribute.AttributeType == OperationContractAttribute);

        // Lookalikes carry different fully qualified identities the model must never match by name.
        Assert.Contains(index.Attributes, attribute => attribute.AttributeType == "Fake.ServiceModel.ServiceContractAttribute");
        Assert.Contains(index.Attributes, attribute => attribute.AttributeType == "Fake.ServiceModel.OperationContractAttribute");
    }

    [Fact]
    public async Task ImplicitImplementationProjectsExactAdmittedInterfaceMemberIdentity()
    {
        await WithFixtureCompilation(async (index, project, repositoryRoot, compilation, documents) =>
        {
            var calculator = compilation.GetTypeByMetadataName(CalculatorServiceMetadataName);
            Assert.NotNull(calculator);
            var add = Assert.Single(calculator.GetMembers("Add").OfType<IMethodSymbol>());

            var shape = FrameworkSymbolEligibilityProjector.ProjectMethodShape(add, project, documents);
            Assert.NotNull(shape);
            var member = Assert.Single(shape!.ImplementedInterfaceMembers);
            Assert.False(member.IsExplicitImplementation);
            Assert.Equal("Add", member.InterfaceMethodMetadataName);
            Assert.Equal(0, member.GenericArity);
            Assert.Equal(2, member.Parameters.Length);
            Assert.All(member.Parameters, parameter => Assert.Equal("System.Double", parameter.FullyQualifiedType));
            Assert.Equal("System.Double", member.ReturnType);
            Assert.Equal(CalculatorContractMetadataName, member.InterfaceType.MetadataName);

            // The exact interface symbol identities resolve real Program Index attributes: the
            // downstream model's first observable consumer is this exact join, never a name rescan.
            Assert.Contains(index.Attributes, attribute =>
                attribute.Target == member.InterfaceTypeSymbol && attribute.AttributeType == ServiceContractAttribute);
            Assert.Contains(index.Attributes, attribute =>
                attribute.Target == member.InterfaceMethodSymbol && attribute.AttributeType == OperationContractAttribute);

            // Determinism: repeated projection of the same method produces the same identities.
            // ImmutableArray<T> equality is reference-based, so compare canonical identity keys
            // rather than the record instances themselves.
            var repeated = FrameworkSymbolEligibilityProjector.ProjectMethodShape(add, project, documents);
            Assert.Equal(
                shape.ImplementedInterfaceMembers.Select(item => (item.InterfaceTypeSymbol, item.InterfaceMethodSymbol, item.IsExplicitImplementation)),
                repeated!.ImplementedInterfaceMembers.Select(item => (item.InterfaceTypeSymbol, item.InterfaceMethodSymbol, item.IsExplicitImplementation)));
        });
    }

    [Fact]
    public async Task ExplicitImplementationProjectsTheSameAdmittedInterfaceMemberIdentity()
    {
        await WithFixtureCompilation(async (index, project, repositoryRoot, compilation, documents) =>
        {
            var explicitCalculator = compilation.GetTypeByMetadataName(ExplicitCalculatorServiceMetadataName);
            Assert.NotNull(explicitCalculator);
            var add = Assert.Single(
                explicitCalculator.GetMembers().OfType<IMethodSymbol>(),
                method => method.Name.EndsWith(".Add", StringComparison.Ordinal));

            var shape = FrameworkSymbolEligibilityProjector.ProjectMethodShape(add, project, documents);
            Assert.NotNull(shape);
            var member = Assert.Single(shape!.ImplementedInterfaceMembers);
            Assert.True(member.IsExplicitImplementation);
            Assert.Equal("Add", member.InterfaceMethodMetadataName);
            Assert.Equal(CalculatorContractMetadataName, member.InterfaceType.MetadataName);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SiblingOperationWithoutOperationContractStillProjectsInterfaceMembershipButNoAttribute()
    {
        await WithFixtureCompilation(async (index, project, repositoryRoot, compilation, documents) =>
        {
            var calculator = compilation.GetTypeByMetadataName(CalculatorServiceMetadataName);
            Assert.NotNull(calculator);
            var modulo = Assert.Single(calculator.GetMembers("Modulo").OfType<IMethodSymbol>());

            var shape = FrameworkSymbolEligibilityProjector.ProjectMethodShape(modulo, project, documents);
            Assert.NotNull(shape);
            var member = Assert.Single(shape!.ImplementedInterfaceMembers);
            Assert.Equal("Modulo", member.InterfaceMethodMetadataName);

            // The projector proves interface membership only; whether the interface member itself
            // carries [OperationContract] is a downstream admission decision, not a projector claim.
            Assert.DoesNotContain(index.Attributes, attribute =>
                attribute.Target == member.InterfaceMethodSymbol && attribute.AttributeType == OperationContractAttribute);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task OperationContractWithoutServiceContractOnTheInterfaceNeverProjectsTheServiceContractAttribute()
    {
        await WithFixtureCompilation(async (index, project, repositoryRoot, compilation, documents) =>
        {
            var utility = compilation.GetTypeByMetadataName(UtilityHelperMetadataName);
            Assert.NotNull(utility);
            var ping = Assert.Single(utility.GetMembers("Ping").OfType<IMethodSymbol>());

            var shape = FrameworkSymbolEligibilityProjector.ProjectMethodShape(ping, project, documents);
            Assert.NotNull(shape);
            var member = Assert.Single(shape!.ImplementedInterfaceMembers);
            Assert.Contains(index.Attributes, attribute =>
                attribute.Target == member.InterfaceMethodSymbol && attribute.AttributeType == OperationContractAttribute);
            Assert.DoesNotContain(index.Attributes, attribute =>
                attribute.Target == member.InterfaceTypeSymbol && attribute.AttributeType == ServiceContractAttribute);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task LookalikeAttributeNamespaceNeverResolvesToTheAdmittedCoreWcfIdentity()
    {
        await WithFixtureCompilation(async (index, project, repositoryRoot, compilation, documents) =>
        {
            var fakeService = compilation.GetTypeByMetadataName(FakeServiceMetadataName);
            Assert.NotNull(fakeService);
            var echo = Assert.Single(fakeService.GetMembers("Echo").OfType<IMethodSymbol>());

            var shape = FrameworkSymbolEligibilityProjector.ProjectMethodShape(echo, project, documents);
            Assert.NotNull(shape);
            var member = Assert.Single(shape!.ImplementedInterfaceMembers);

            var operationAttributes = index.Attributes.Where(attribute => attribute.Target == member.InterfaceMethodSymbol).ToArray();
            Assert.Contains(operationAttributes, attribute => attribute.AttributeType == "Fake.ServiceModel.OperationContractAttribute");
            Assert.DoesNotContain(operationAttributes, attribute => attribute.AttributeType == OperationContractAttribute);
            var typeAttributes = index.Attributes.Where(attribute => attribute.Target == member.InterfaceTypeSymbol).ToArray();
            Assert.Contains(typeAttributes, attribute => attribute.AttributeType == "Fake.ServiceModel.ServiceContractAttribute");
            Assert.DoesNotContain(typeAttributes, attribute => attribute.AttributeType == ServiceContractAttribute);
            await Task.CompletedTask;
        });
    }

    private static async Task WithFixtureCompilation(
        Func<ProgramIndexSnapshot, StableProjectId, string, Compilation, IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext>, Task> assertions)
    {
        var request = CreateFixtureRequest();
        await MsBuildRegistration.EnsureRegisteredAsync(request.RepositoryRoot, CancellationToken.None);
        var (loaded, loadDiagnostics) = await CompilationWorkspaceLoader.LoadAsync(request, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Empty(loadDiagnostics);
        using (loaded!)
        {
            var index = await RoslynProgramIndexExtractor.ExtractAsync(
                loaded,
                request.Profile,
                request.RepositoryRoot,
                CancellationToken.None);
            var project = Assert.Single(loaded.Projects);
            var contexts = await RoslynProgramIndexExtractor.ReadDocumentsAsync(project, request.RepositoryRoot, CancellationToken.None);
            var documents = RoslynProgramIndexExtractor.CreateDocumentIndex(contexts);
            await assertions(index, project.StableId, request.RepositoryRoot, project.Compilation, documents);
        }
    }

    private static CompilationAnalysisRequest CreateFixtureRequest()
    {
        var root = FindRepositoryRoot();
        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
