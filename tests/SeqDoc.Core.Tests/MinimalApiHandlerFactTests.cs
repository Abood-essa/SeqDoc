using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.Diagnostics;
using Xunit;

namespace SeqDoc.Core.Tests;

public sealed class MinimalApiHandlerFactTests
{
    private static readonly CompilationProfile Profile = CompilationProfile.Create(
        "tests/fixtures/CorpusRoadmap/MinimalApis/MinimalApis.csproj",
        "Release",
        "net10.0");

    [Fact]
    public void HandlerFactSetValidatesIdentityEvidenceAndCanonicalOrder()
    {
        var exact = Evidence();
        var first = Fact("z", exact);
        var second = Fact("a", exact);

        IEnumerable<MinimalApiHandlerFact> facts = [first, second];
        IEnumerable<AnalysisDiagnostic> diagnostics = [];
        var set = new MinimalApiHandlerFactSet(Profile, "fingerprint:v1:fixture", facts, diagnostics, "minimal-api");

        Assert.Equal(
            ["callback-boundary:v1:a", "callback-boundary:v1:z"],
            set.Facts.Select(fact => fact.BoundaryId.Value));
        Assert.All(set.Facts, fact => Assert.NotEmpty(fact.Evidence));
        Assert.Throws<ArgumentException>(() => new MinimalApiHandlerFactSet(
            Profile, "", [first], [], "debug"));
        Assert.Throws<ArgumentException>(() => Fact("", exact));
        Assert.Throws<ArgumentException>(() => new MinimalApiHandlerFactSet(
            Profile, "fingerprint:v1:fixture", [first, first], [], "minimal-api"));
    }

    [Fact]
    public void HandlerFactsRejectIncompleteContradictoryOrUnbackedEvidence()
    {
        var exact = Evidence();
        var operation = new MinimalApiHandlerOperation(
            new OperationId("operation:v1:delay"), MinimalApiHandlerOperationKind.Delay,
            "System.Threading.Tasks.Task.Delay", 100, null, null, new(0), exact, CertaintyLevel.Exact);
        var outcome = new MinimalApiHandlerOutcome(
            new OperationId("operation:v1:outcome"), "Microsoft.AspNetCore.Http.Results.Ok", 200,
            new(20, true, 0), exact, CertaintyLevel.Exact);
        var outcomeOperation = new MinimalApiHandlerOperation(
            new OperationId("operation:v1:outcome"), MinimalApiHandlerOperationKind.Outcome,
            null, null, 200, "Microsoft.AspNetCore.Http.Results.Ok", new(10, true, 0), exact,
            CertaintyLevel.Exact);

        Assert.Throws<ArgumentException>(() => FactWith(exact: []));
        Assert.Throws<ArgumentException>(() => FactWith(exact, operations: [operation with { DelayMilliseconds = 100, StatusCode = 200 }]));
        Assert.Throws<ArgumentException>(() => FactWith(exact, operations: [operation], outcomes: [outcome]));
        Assert.NotNull(FactWith(exact, operations: [outcomeOperation], outcomes: [outcome]));
        Assert.Throws<ArgumentException>(() => FactWith(exact, operations: [operation, operation]));
        Assert.Throws<ArgumentException>(() => FactWith(exact, outcomes: [outcome, outcome]));
        Assert.Throws<ArgumentException>(() => FactWith(exact, operations: [operation with { DelayMilliseconds = -1 }]));
        Assert.Throws<ArgumentException>(() => FactWith(exact, operations: [new MinimalApiHandlerOperation(
            new OperationId("operation:v1:bad-status"), MinimalApiHandlerOperationKind.Outcome, "Results.Ok", null, 99,
            "Results.Ok", new(0), exact, CertaintyLevel.Exact)]));
        Assert.Throws<ArgumentException>(() => FactWith(exact, outcomes: [outcome with { Certainty = CertaintyLevel.Unknown }]));
        Assert.Throws<ArgumentException>(() => FactWith(Evidence(CertaintyLevel.Conservative), certainty: CertaintyLevel.Exact));
    }

    private static MinimalApiHandlerFact Fact(string boundary, ImmutableArray<EvidenceRef> evidence)
        => new(
            new CallbackBoundaryId($"callback-boundary:v1:{boundary}"),
            new MethodId("method:v1:Program.<Main>$"),
            new OperationId("operation:v1:telecom-body"),
            [], [], [], [], evidence, CertaintyLevel.Exact);

    private static MinimalApiHandlerFact FactWith(
        ImmutableArray<EvidenceRef> exact,
        ImmutableArray<MinimalApiHandlerOperation> operations = default,
        ImmutableArray<MinimalApiHandlerOutcome> outcomes = default,
        CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(
            new CallbackBoundaryId("callback-boundary:v1:contract"),
            new MethodId("method:v1:Program.<Main>$"),
            new OperationId("operation:v1:telecom-body"), [],
            operations.IsDefault ? [] : operations, [], outcomes.IsDefault ? [] : outcomes, exact, certainty);

    private static ImmutableArray<EvidenceRef> Evidence(CertaintyLevel certainty = CertaintyLevel.Exact)
        => [new(new EvidenceId("evidence:v1:minimal-handler"), EvidenceKind.Source, "Program.cs", null,
            "telecom", "test", certainty)];
}
