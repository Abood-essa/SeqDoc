using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels.MediatR;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.MediatR;

public sealed class MediatRDispatchModelTests
{
    [Fact]
    public async Task ExactShapeProducesSingleDispatchAndPreservesTokenMetadata()
    {
        var operation = CreateOperation(tokenSupplied: false, candidates: [Candidate("handler")]);
        var result = await new MediatRDispatchModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        var fact = Assert.Single(result.Facts.OfType<DispatchFact>());
        Assert.Equal(DispatchResolution.ExactSingle, fact.Resolution);
        Assert.Equal("Request", fact.RequestType);
        Assert.Equal("MediatR.IRequest<Response>", operation.DispatchShape!.RequestContractType);
        Assert.False(operation.DispatchShape!.TokenSupplied);
    }

    [Fact]
    public async Task MultipleCandidatesAreAmbiguousAndWrongVersionIsRejected()
    {
        var model = new MediatRDispatchModel();
        var ambiguous = await model.AnalyzeOperationAsync(
            CreateOperation(false, [Candidate("a"), Candidate("b")]), Context(), CancellationToken.None);
        Assert.Equal(DispatchResolution.Ambiguous, Assert.Single(ambiguous.Facts.OfType<DispatchFact>()).Resolution);

        var unresolved = await model.AnalyzeOperationAsync(
            CreateOperation(false, []), Context(), CancellationToken.None);
        Assert.Equal(DispatchResolution.Unresolved, Assert.Single(unresolved.Facts.OfType<DispatchFact>()).Resolution);

        var rejected = await model.AnalyzeOperationAsync(
            CreateOperation(false, [Candidate("handler")]) with
            {
                TargetIdentity = CreateIdentity() with { AssemblyVersion = "12.0.0.0" }
            }, Context(), CancellationToken.None);
        Assert.Empty(rejected.Facts);
    }

    [Fact]
    public async Task GenericRequestResponseShapeFailsClosedWithoutADispatchFact()
    {
        var operation = CreateOperation(false, [Candidate("handler")]) with
        {
            TargetIdentity = CreateIdentity() with
            {
                Parameters = [
                    new(ParameterRefKind.None, "MediatR.IRequest<TResponse>"),
                    new(ParameterRefKind.None, "System.Threading.CancellationToken")],
                ReturnType = "System.Threading.Tasks.Task<TResponse>"
            },
            DispatchShape = new FrameworkDispatchShapeDescriptor(
                "MediatR.IRequest<TResponse>", "TResponse", "MediatR.IRequest<TResponse>", false, false, [Candidate("handler")])
        };

        var result = await new MediatRDispatchModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task ClosedShapeWithDisagreeingConstructedIdentitiesFailsWithMr001()
    {
        var operation = CreateOperation(false, [Candidate("handler")]) with
        {
            DispatchShape = new FrameworkDispatchShapeDescriptor(
                "WrongRequest", "WrongResponse", "MediatR.IRequest<WrongResponse>", true, false, [Candidate("handler")])
        };

        var result = await new MediatRDispatchModel().AnalyzeOperationAsync(operation, Context(), CancellationToken.None);

        Assert.Empty(result.Facts);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MR001");
    }

    private static OperationDescriptor CreateOperation(bool tokenSupplied, ImmutableArray<FrameworkDispatchCandidateDescriptor> candidates)
        => new(
            new OperationId("operation:test"), new MethodId("method:test"), "Invocation",
            new DocumentId("document:test"), 10, 4, [Evidence()], CertaintyLevel.Exact,
            CreateIdentity(), DispatchShape: new FrameworkDispatchShapeDescriptor(
                "Request", "Response", "MediatR.IRequest<Response>", true, tokenSupplied, candidates));

    private static FrameworkMethodIdentity CreateIdentity()
        => new("MediatR", "MediatR.ISender", "Send", 1,
            [new(ParameterRefKind.None, "MediatR.IRequest<Response>"), new(ParameterRefKind.None, "System.Threading.CancellationToken")],
            "System.Threading.Tasks.Task<Response>", "13.0.0.0");

    private static FrameworkDispatchCandidateDescriptor Candidate(
        string name, bool bodyAvailable = true)
        => new(new MethodId($"method:{name}"), $"Handler.{name}", bodyAvailable, [Evidence()], CertaintyLevel.Exact);

    private static FrameworkAnalysisContext Context()
    {
        var profile = CompilationProfile.Create("fixture.csproj", "Release", "net10.0");
        return new(profile, new ProgramIndexSnapshot(
            1, "test", profile, Projects: [], Documents: [], Namespaces: [], Types: [], Members: [], Methods: [],
            Attributes: [], References: [], Invocations: [], InventoryMarkers: [], Diagnostics: [],
            InputManifestHash: "input", IndexFingerprint: "fingerprint"));
    }

    private static EvidenceRef Evidence()
    {
        var document = new DocumentId("document:test");
        var range = new SourceRange(document, new SourcePosition(0, 0), new SourcePosition(0, 4));
        return new EvidenceRef(new EvidenceId("evidence:test"), EvidenceKind.Source, "fixture.cs", range, "Fixture.Run", null, CertaintyLevel.Exact);
    }
}
