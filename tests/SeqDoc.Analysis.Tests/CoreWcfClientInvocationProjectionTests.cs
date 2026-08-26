using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.Toolchains;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.CoreWcf;
using Xunit;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// Producer proof for the client-invocation admission added on top of issues #5/#7's client-boundary
/// facts: the real Roslyn Program Index and eligibility projector drive
/// <see cref="CoreWcfServiceModel"/>'s new client-invocation branch through <see cref="FrameworkModelHost"/>
/// against the real <c>ClientCallers.cs</c> fixture call sites, proving exact result-claim classification
/// (Discarded/ResultAssigned/ResultReturned/Unclaimed) for every supported shape, multiplicity for two
/// distinct occurrences of the same operation, and that the same-shaped negatives (ambiguous
/// interface-typed receiver, mismatched-contract client) fail closed through the same producer rather
/// than being hand-built. This is a sibling of <see cref="CoreWcfServiceModelProjectionTests"/>, scoped
/// entirely to client-invocation admission; the existing file's service-side admission tests are
/// untouched.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class CoreWcfClientInvocationProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj";
    private const string CallerTypeMetadataName = "CoreWcfServices.CalculatorClientCaller";
    private const string CalculatorContractMetadataName = "CoreWcfServices.ICalculatorService";
    private const string CalculatorSourceClientMetadataName = "CoreWcfServices.CalculatorSourceClient";
    private const string CalculatorGeneratedClientMetadataName = "CoreWcfServices.CalculatorGeneratedClient";

    [Fact]
    public async Task RealFixtureCallSitesProduceTheExactResultClaimForEachSupportedShape()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var invocations = framework.Facts.OfType<ServiceClientInvocationFact>().ToArray();

        AssertClaim(programIndex, invocations, "CallDiscarded", ClientInvocationResultClaimKind.Discarded, null);
        AssertClaim(programIndex, invocations, "CallAssigned", ClientInvocationResultClaimKind.ResultAssigned, "sum");
        AssertClaim(programIndex, invocations, "CallReturned", ClientInvocationResultClaimKind.ResultReturned, null);
        AssertClaim(programIndex, invocations, "CallUnclaimed", ClientInvocationResultClaimKind.Unclaimed, null);

        // Same-shaped lookalikes that must also classify Unclaimed, never Discarded/ResultAssigned:
        // stored to a field, discarded via `_ = ...`, and passed as an argument.
        AssertClaim(programIndex, invocations, "CallStoredToField", ClientInvocationResultClaimKind.Unclaimed, null);
        AssertClaim(programIndex, invocations, "CallDiscardAssignment", ClientInvocationResultClaimKind.Unclaimed, null);
        AssertClaim(programIndex, invocations, "CallPassedAsArgument", ClientInvocationResultClaimKind.Unclaimed, null);

        Assert.All(invocations, fact => Assert.False(fact.IsAwaited));
        Assert.All(invocations, fact => Assert.Equal(CalculatorContractMetadataName, fact.ServiceContractType));
        Assert.All(invocations, fact => Assert.True(
            fact.OperationName is "Add" or "SquareRoot",
            $"Unexpected operation name '{fact.OperationName}'."));
    }

    [Fact]
    public async Task TwoDistinctCallOccurrencesToTheSameOperationBothAdmitIndependentInvocations()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallTwice");
        var invocations = framework.Facts.OfType<ServiceClientInvocationFact>()
            .Where(fact => fact.CallerMethod == caller.Id)
            .ToArray();

        Assert.Equal(2, invocations.Length);
        Assert.All(invocations, fact => Assert.Equal("Add", fact.OperationName));
        Assert.All(invocations, fact => Assert.Equal(ClientInvocationResultClaimKind.ResultAssigned, fact.ResultClaim));
        // Each occurrence keeps its own distinct invocation-operation anchor and fact identity.
        Assert.Equal(2, invocations.Select(fact => fact.InvocationOperation.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, invocations.Select(fact => fact.Id.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(invocations, fact => fact.ResultBindingName == "first");
        Assert.Contains(invocations, fact => fact.ResultBindingName == "second");
    }

    [Fact]
    public async Task GeneratedClientCallSiteAdmitsAnInvocationClassifiedGeneratedClient()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallGeneratedClient");
        var invocation = Assert.Single(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);

        Assert.Equal(CalculatorGeneratedClientMetadataName, invocation.ClientType);
        Assert.Equal("Add", invocation.OperationName);
        Assert.Equal(CertaintyLevel.Exact, invocation.Certainty);

        // ServiceClientBoundaryFact is anchored per admitting client method (see CoreWcfServiceModel's
        // AnalyzeMethod), so CalculatorGeneratedClient's five ICalculatorService operations each
        // independently contribute a boundary fact with the same ClientTypeSymbol; the join only needs
        // every one of them to agree on GeneratedClient, exactly as ScenarioGraphBuilder's join does.
        var boundaries = framework.Facts.OfType<ServiceClientBoundaryFact>()
            .Where(fact => fact.ClientTypeSymbol == invocation.ClientTypeSymbol)
            .ToArray();
        Assert.NotEmpty(boundaries);
        Assert.All(boundaries, boundary => Assert.Equal(ServiceClientKind.GeneratedClient, boundary.ClientKind));
    }

    [Fact]
    public async Task FaultDeclaringOperationCallSiteAdmitsAnInvocationForSquareRoot()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallFaultDeclaringOperation");
        var invocation = Assert.Single(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);

        Assert.Equal("SquareRoot", invocation.OperationName);
        Assert.Equal(CalculatorSourceClientMetadataName, invocation.ClientType);

        // The declaration-only fault fact for the exact same operation symbol already exists from the
        // service-side admission (proven independently in CoreWcfServiceModelProjectionTests); the
        // invocation joins to it later by exact OperationSymbol identity in the Scenario Graph, not here.
        Assert.Contains(
            framework.Facts.OfType<ServiceFaultContractFact>(),
            fact => fact.OperationSymbol == invocation.OperationSymbol && fact.FaultType == "CoreWcfServices.NegativeSquareRootFault");
    }

    [Fact]
    public async Task AmbiguousInterfaceTypedReceiverNeverAdmitsAnInvocation()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallThroughInterfaceTypedReceiver");

        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);
    }

    [Fact]
    public async Task MismatchedContractClientCallNeverAdmitsAnInvocation()
    {
        var (programIndex, framework) = await AnalyzeFixtureAsync();
        var caller = FindMethod(programIndex, CallerTypeMetadataName, "CallThroughMismatchedContractClient");

        // MismatchedContractClient derives ClientBase<ICalculatorService> but Echo implements the
        // separately admitted classic-family IClassicEchoService, which ClientBase was not constructed
        // with — the same-shaped foreign/mismatched-contract negative must fail closed through the real
        // producer, exactly like the existing client-boundary negative for this fixture type.
        Assert.DoesNotContain(
            framework.Facts.OfType<ServiceClientInvocationFact>(),
            fact => fact.CallerMethod == caller.Id);
    }

    private static void AssertClaim(
        ProgramIndexSnapshot programIndex,
        ServiceClientInvocationFact[] invocations,
        string methodName,
        ClientInvocationResultClaimKind expectedClaim,
        string? expectedBindingName)
    {
        var caller = FindMethod(programIndex, CallerTypeMetadataName, methodName);
        var invocation = Assert.Single(invocations, fact => fact.CallerMethod == caller.Id);
        Assert.Equal(expectedClaim, invocation.ResultClaim);
        Assert.Equal(expectedBindingName, invocation.ResultBindingName);
        Assert.Equal(CertaintyLevel.Exact, invocation.Certainty);
        Assert.False(invocation.Evidence.IsDefaultOrEmpty);
    }

    private static ProgramMethod FindMethod(ProgramIndexSnapshot programIndex, string containingTypeMetadataName, string methodName)
    {
        var containingType = programIndex.Types.Single(type => type.MetadataName == containingTypeMetadataName);
        return programIndex.Methods.Single(method => method.ContainingType == containingType.Id && method.Name == methodName);
    }

    private static async Task<(ProgramIndexSnapshot ProgramIndex, FrameworkAnalysisResult Framework)> AnalyzeFixtureAsync()
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0"));
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}\n{diagnostic.InternalDetail}")));

        var behaviorResult = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(behaviorResult.IsSuccess);

        var host = new FrameworkModelHost([new CoreWcfServiceModel()]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(request.Profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(request.Profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        return (extraction.Value.ProgramIndex, framework);
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
