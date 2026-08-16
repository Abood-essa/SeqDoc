using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.Analysis.Tests.MediatR;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class MediatRDispatchProjectionTests
{
    private const string Fixture = "tests/fixtures/CorpusRoadmap/MediatRDispatch/MediatRDispatch.csproj";

    [Fact]
    public async Task ProjectionCarriesExactMediatR13SendShapeAndStableCandidateIdentity()
    {
        var first = await ExtractAsync();
        var second = await ExtractAsync();
        var firstMatches = first.Operations.Where(item => item.DispatchShape?.RequestType.Contains("CreateOrderDraftCommand", StringComparison.Ordinal) == true).ToArray();
        var secondMatches = second.Operations.Where(item => item.DispatchShape?.RequestType.Contains("CreateOrderDraftCommand", StringComparison.Ordinal) == true).ToArray();
        Assert.True(firstMatches.Length == 1, string.Join(";", first.Operations.Where(item => item.DispatchShape is not null).Select(item => $"{item.DispatchShape!.RequestType}->{item.DispatchShape.ResponseType}")));
        Assert.Single(secondMatches);
        var firstOperation = firstMatches[0];
        var secondOperation = secondMatches[0];

        Assert.NotNull(firstOperation.DispatchShape);
        var firstShape = firstOperation.DispatchShape!;
        Assert.Equal("CorpusRoadmap.MediatRDispatch.CreateOrderDraftCommand", firstShape.RequestType);
        Assert.Equal("CorpusRoadmap.MediatRDispatch.CreateOrderDraftResponse", firstShape.ResponseType);
        Assert.Equal("MediatR.IRequest<CorpusRoadmap.MediatRDispatch.CreateOrderDraftResponse>", firstShape.RequestContractType);
        Assert.True(firstShape.IsClosedConstructed);
        Assert.False(firstShape.TokenSupplied);
        var candidate = Assert.Single(firstShape.Candidates);
        Assert.Equal("CreateOrderDraftCommandHandler.Handle", candidate.DisplayName);
        Assert.True(candidate.BodyAvailable);
        Assert.False(candidate.IsOpenGeneric);
        Assert.NotEmpty(candidate.Evidence);
        Assert.Equal(firstShape.RequestType, secondOperation.DispatchShape!.RequestType);
        Assert.Equal(firstShape.ResponseType, secondOperation.DispatchShape.ResponseType);
        Assert.Equal(firstShape.TokenSupplied, secondOperation.DispatchShape.TokenSupplied);
        var secondCandidate = Assert.Single(secondOperation.DispatchShape.Candidates);
        Assert.Equal(candidate.Method, secondCandidate.Method);
        Assert.Equal(candidate.DisplayName, secondCandidate.DisplayName);
        Assert.Equal(candidate.BodyAvailable, secondCandidate.BodyAvailable);
        Assert.Equal(candidate.Evidence.Select(item => item.Id), secondCandidate.Evidence.Select(item => item.Id));
        Assert.Equal(firstOperation.Id, secondOperation.Id);
    }

    [Fact]
    public async Task ClosedRequestsWithNoOrMultipleHandlersPreserveCandidateCardinalityAndIdentityOrder()
    {
        var first = await ExtractAsync();
        var second = await ExtractAsync();

        var noHandler = Assert.Single(first.Operations.Where(item =>
            item.DispatchShape?.RequestType.EndsWith("NoHandlerRequest", StringComparison.Ordinal) == true));
        Assert.Empty(noHandler.DispatchShape!.Candidates);

        var firstMultiple = Assert.Single(first.Operations.Where(item =>
            item.DispatchShape?.RequestType.EndsWith("MultipleRequest", StringComparison.Ordinal) == true));
        var secondMultiple = Assert.Single(second.Operations.Where(item =>
            item.DispatchShape?.RequestType.EndsWith("MultipleRequest", StringComparison.Ordinal) == true));
        Assert.Equal(2, firstMultiple.DispatchShape!.Candidates.Length);
        var firstIds = firstMultiple.DispatchShape.Candidates.Select(candidate => candidate.Method.Value).ToArray();
        var secondIds = secondMultiple.DispatchShape!.Candidates.Select(candidate => candidate.Method.Value).ToArray();
        Assert.Equal(firstIds.OrderBy(id => id, StringComparer.Ordinal), firstIds);
        Assert.Equal(firstIds, secondIds);
    }

    [Fact]
    public async Task GenericRequestResponseInvocationIsNotProjectedAsMediatRDispatch()
    {
        var extraction = await ExtractAsync();

        Assert.DoesNotContain(
            extraction.Operations,
            operation => operation.DispatchShape?.RequestType.Contains("TResponse", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task PreCancelledExtractionReturnsEstablishedCancelledOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await ExtractRawAsync(cancellation.Token);

        Assert.Equal(ApplicationOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Value);
    }

    [Fact]
    public void FixtureCleanupRemovesBuildArtifactDirectories()
    {
        var fixtureRoot = Path.Combine(FindRoot(), Fixture.Replace('/', Path.DirectorySeparatorChar));

        DeleteBuildArtifacts(Path.Combine(fixtureRoot, "bin"));
        DeleteBuildArtifacts(Path.Combine(fixtureRoot, "obj"));

        Assert.False(Directory.Exists(Path.Combine(fixtureRoot, "bin")));
        Assert.False(Directory.Exists(Path.Combine(fixtureRoot, "obj")));
    }

    private static async Task<ProfileAnalysisExtraction> ExtractAsync(
        string? rootOverride = null, string? fixtureOverride = null, string? targetOverride = null)
    {
        var result = await ExtractRawAsync(CancellationToken.None, rootOverride, fixtureOverride, targetOverride);
        Assert.True(result.IsSuccess, string.Join("; ", result.Diagnostics.Select(item => item.TechnicalCause)));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractRawAsync(
        CancellationToken cancellationToken,
        string? rootOverride = null, string? fixtureOverride = null, string? targetOverride = null)
    {
        var root = rootOverride ?? FindRoot();
        var fixture = fixtureOverride ?? Fixture;
        try
        {
            return await new RoslynProfileAnalysisExtractor().ExtractAsync(
                new CompilationAnalysisRequest(root, targetOverride ?? Path.Combine(root, fixture.Replace('/', Path.DirectorySeparatorChar)),
                    CompilationProfile.Create(fixture, "Release", "net10.0")), cancellationToken);
        }
        finally
        {
            var fixtureRoot = Path.Combine(root, fixture.Replace('/', Path.DirectorySeparatorChar));
            DeleteBuildArtifacts(Path.Combine(fixtureRoot, "bin"));
            DeleteBuildArtifacts(Path.Combine(fixtureRoot, "obj"));
            Assert.False(Directory.Exists(Path.Combine(fixtureRoot, "bin")));
            Assert.False(Directory.Exists(Path.Combine(fixtureRoot, "obj")));
        }
    }

    private static void DeleteBuildArtifacts(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
