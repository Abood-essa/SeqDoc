using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests;

public sealed class FrameworkModelHostTests
{
    [Fact]
    public async Task DescriptorsOrderIsIndependentOfRegistrationOrder()
    {
        var descriptorA = Descriptor("model-a", order: 2);
        var descriptorB = Descriptor("model-b", order: 1);
        var descriptorC = Descriptor("model-c", order: 1);
        var modelA = new StubModel(descriptorA, result: ResultWithFact("kind-a", descriptorA));
        var modelB = new StubModel(descriptorB, result: ResultWithFact("kind-b", descriptorB));
        var modelC = new StubModel(descriptorC, result: ResultWithFact("kind-c", descriptorC));

        var forward = new FrameworkModelHost([modelA, modelB, modelC]);
        var reverse = new FrameworkModelHost([modelC, modelB, modelA]);

        Assert.Equal(
            ["model-b", "model-c", "model-a"],
            forward.Descriptors.Select(descriptor => descriptor.ModelId).ToArray());
        Assert.Equal(forward.Descriptors.ToArray(), reverse.Descriptors.ToArray());
        var forwardResult = await forward.AnalyzeAsync(CreateRequest(), CancellationToken.None);
        var reverseResult = await reverse.AnalyzeAsync(CreateRequest(), CancellationToken.None);
        // ImmutableArray equality is reference-based, so compare elements explicitly.
        Assert.Equal(forwardResult.Facts.ToArray(), reverseResult.Facts.ToArray());
        Assert.Equal(forwardResult.AppliedModels.ToArray(), reverseResult.AppliedModels.ToArray());
    }

    [Fact]
    public void DuplicateModelRegistrationIsRejected()
    {
        var first = new StubModel(new FrameworkModelDescriptor("seqdoc.duplicate", "1.0.0", "Duplicate", 1));
        var second = new StubModel(new FrameworkModelDescriptor("seqdoc.duplicate", "1.0.0", "Duplicate", 1));

        Assert.Throws<ArgumentException>(() => new FrameworkModelHost([first, second]));
    }

    [Theory]
    [InlineData("modelId")]
    [InlineData("version")]
    [InlineData("displayName")]
    public void RegistrationRejectsBlankDescriptorField(string field)
    {
        var descriptor = field switch
        {
            "modelId" => new FrameworkModelDescriptor(" ", "1.0.0", "Name", 1),
            "version" => new FrameworkModelDescriptor("id", " ", "Name", 1),
            _ => new FrameworkModelDescriptor("id", "1.0.0", " ", 1),
        };

        Assert.Throws<ArgumentException>(() => new FrameworkModelHost([new StubModel(descriptor)]));
    }

    [Fact]
    public void RegistrationRejectsNegativeOrder()
    {
        var descriptor = new FrameworkModelDescriptor("id", "1.0.0", "Name", -1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameworkModelHost([new StubModel(descriptor)]));
    }

    [Fact]
    public async Task ApplicabilityControlsWhichModelsRun()
    {
        var applicable = new StubModel(Descriptor("applicable-model"), applicable: true);
        var nonApplicable = new StubModel(Descriptor("non-applicable-model"), applicable: false);
        var host = new FrameworkModelHost([nonApplicable, applicable]);

        var result = await host.AnalyzeAsync(CreateRequest(operations: [CreateOperation()]), CancellationToken.None);

        Assert.Equal(1, applicable.OperationCalls);
        Assert.Equal(0, nonApplicable.OperationCalls);
        var applied = Assert.Single(result.AppliedModels);
        Assert.Equal("applicable-model", applied.ModelId);
    }

    [Fact]
    public async Task UnrecognizedInputProducesEmptyHonestResult()
    {
        var host = new FrameworkModelHost([]);

        var result = await host.AnalyzeAsync(CreateRequest(operations: [CreateOperation()]), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.ResolutionHints);
        Assert.Empty(result.SuppressionHints);
        Assert.Empty(result.SummaryRules);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.AppliedModels);
    }

    [Fact]
    public async Task AggregateFactsAreCanonicallyOrderedIndependentOfRegistration()
    {
        var descriptorA = Descriptor("fact-model-a");
        var descriptorB = Descriptor("fact-model-b");
        var modelA = new StubModel(descriptorA, result: ResultWithFact("kind-a", descriptorA));
        var modelB = new StubModel(descriptorB, result: ResultWithFact("kind-b", descriptorB));
        var hostForward = new FrameworkModelHost([modelA, modelB]);
        var hostReverse = new FrameworkModelHost([modelB, modelA]);

        var forward = await hostForward.AnalyzeAsync(CreateRequest(), CancellationToken.None);
        var reverse = await hostReverse.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        // ImmutableArray equality is reference-based, so compare elements explicitly.
        Assert.Equal(forward.Facts.ToArray(), reverse.Facts.ToArray());
        Assert.Equal(
            forward.Facts.OrderBy(fact => fact.Id.Value, StringComparer.Ordinal).ToArray(),
            forward.Facts.ToArray());
    }

    [Fact]
    public async Task AggregatePreservesExplicitFactCertainty()
    {
        var producer = Descriptor("certainty-model");
        var exactFact = CreateFact("kind-exact", producer, certainty: CertaintyLevel.Exact);
        var conservativeFact = CreateFact("kind-conservative", producer, certainty: CertaintyLevel.Conservative, sibling: 1);
        var model = new StubModel(
            producer,
            result: new ModelResult(true, facts: [exactFact, conservativeFact]));
        var host = new FrameworkModelHost([model]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(2, result.Facts.Length);
        Assert.Contains(result.Facts, fact => fact.Id == exactFact.Id && fact.Certainty == CertaintyLevel.Exact);
        Assert.Contains(result.Facts, fact => fact.Id == conservativeFact.Id && fact.Certainty == CertaintyLevel.Conservative);
    }

    [Fact]
    public async Task FactWithoutEvidenceIsExcludedWithHostDiagnostic()
    {
        var producer = Descriptor("evidence-model");
        var validFact = CreateFact("kind-valid", producer);
        var invalidFact = new GeneralBehaviorFact
        {
            Id = CreateFactId("kind-invalid", sibling: 1),
            Kind = "kind-invalid",
            Evidence = [],
            Certainty = CertaintyLevel.Exact,
        };
        var model = new StubModel(
            producer,
            result: new ModelResult(true, facts: [validFact, invalidFact]));
        var host = new FrameworkModelHost([model]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        var aggregated = Assert.Single(result.Facts);
        Assert.Equal(validFact.Id, aggregated.Id);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQFW001", diagnostic.Code);
        Assert.Equal(AnalysisStage.FrameworkModel, diagnostic.Stage);
    }

    [Fact]
    public async Task HintWithoutEvidenceIsExcludedWithHostDiagnostic()
    {
        var producer = Descriptor("hint-evidence-model");
        var validHint = new CallResolutionHint(
            new OperationId("operation:v1:test"),
            new MethodId("method:v1:test"),
            null,
            "registered service",
            0,
            [CreateModelEvidence(producer)],
            CertaintyLevel.Conservative);
        var invalidHint = new CallResolutionHint(
            new OperationId("operation:v1:test"),
            new MethodId("method:v1:test"),
            null,
            "unproven guidance",
            1,
            [],
            CertaintyLevel.Unknown);
        var model = new StubModel(
            producer,
            result: new ModelResult(true, resolutionHints: [validHint, invalidHint]));
        var host = new FrameworkModelHost([model]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        var aggregated = Assert.Single(result.ResolutionHints);
        Assert.Equal(validHint, aggregated);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQFW002", diagnostic.Code);
        Assert.Equal(AnalysisStage.FrameworkModel, diagnostic.Stage);
    }

    [Fact]
    public async Task ModelDiagnosticsAreRetainedAndCanonicallyOrdered()
    {
        var model = new StubModel(
            Descriptor("diagnostic-model"),
            result: new ModelResult(
                true,
                diagnostics: [CreateDiagnostic("SEQFW9002", 0), CreateDiagnostic("SEQFW9001", 0)]));
        var host = new FrameworkModelHost([model]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(2, result.Diagnostics.Length);
        Assert.Equal(
            result.Diagnostics.OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal).ToArray(),
            result.Diagnostics.ToArray());
    }

    [Fact]
    public async Task CanceledTokenStopsAnalysisBeforeInvokingModels()
    {
        var model = new StubModel(Descriptor("cancel-before-model"), applicable: true);
        var host = new FrameworkModelHost([model]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.AnalyzeAsync(CreateRequest(), cts.Token).AsTask());

        Assert.Equal(0, model.OperationCalls);
    }

    [Fact]
    public async Task ModelCancellationIsPropagatedToCaller()
    {
        var host = new FrameworkModelHost([new CancelingModel()]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.AnalyzeAsync(CreateRequest(operations: [CreateOperation()]), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ApplicableModelReceivesEveryOperationAndSymbol()
    {
        var model = new StubModel(Descriptor("recording-model"), applicable: true);
        var host = new FrameworkModelHost([model]);

        await host.AnalyzeAsync(
            CreateRequest(
                operations: [CreateOperation(), CreateOperation("ObjectCreation")],
                symbols: [CreateSymbol(), CreateSymbol("Method", "Company.App.Run")]),
            CancellationToken.None);

        Assert.Equal(2, model.OperationCalls);
        Assert.Equal(2, model.SymbolCalls);
    }

    [Fact]
    public async Task AggregateRetainsFrameworkModelEvidenceProducerAndSourceProvenance()
    {
        var producer = Descriptor("seqdoc.aspnetcore.controllers");
        var modelEvidence = CreateModelEvidence(producer);
        var fact = new GeneralBehaviorFact
        {
            Id = CreateFactId("http-entry-point"),
            Kind = "http-entry-point",
            Evidence = [modelEvidence],
            Certainty = CertaintyLevel.Exact,
        };
        var host = new FrameworkModelHost([new StubModel(producer, result: new ModelResult(true, facts: [fact]))]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        var aggregated = Assert.Single(result.Facts);
        var evidence = Assert.Single(aggregated.Evidence);
        Assert.Equal(EvidenceKind.FrameworkModel, evidence.Kind);
        Assert.Equal("seqdoc.aspnetcore.controllers", evidence.ProducerId);
        Assert.Equal("1.0.0", evidence.ProducerVersion);
        var source = Assert.Single(evidence.UnderlyingEvidence);
        Assert.Equal(EvidenceKind.Source, source.Kind);
        Assert.NotNull(source.Range);
        Assert.Equal("Company.Web.TicketsController.Reserve", source.Symbol);
    }

    [Fact]
    public async Task FactWithDirectSourceEvidenceIsRejectedAsProducerMismatch()
    {
        var producer = Descriptor("producer-model");
        var fact = new GeneralBehaviorFact
        {
            Id = CreateFactId("kind-direct-source"),
            Kind = "kind-direct-source",
            Evidence = [CreateSourceEvidence()],
            Certainty = CertaintyLevel.Exact,
        };
        var host = new FrameworkModelHost([new StubModel(producer, result: new ModelResult(true, facts: [fact]))]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Empty(result.Facts);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQFW005", diagnostic.Code);
    }

    [Fact]
    public async Task ArtifactWithMismatchedProducerEvidenceIsRejected()
    {
        var producer = Descriptor("producer-model");
        var otherProducer = Descriptor("other-model");
        var hint = new CallResolutionHint(
            new OperationId("operation:v1:test"),
            new MethodId("method:v1:test"),
            null,
            "reason",
            0,
            [CreateModelEvidence(otherProducer)],
            CertaintyLevel.Conservative);
        var host = new FrameworkModelHost([new StubModel(producer, result: new ModelResult(true, resolutionHints: [hint]))]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Empty(result.ResolutionHints);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQFW005", diagnostic.Code);
    }

    [Fact]
    public async Task DuplicateFactsFromOneModelAreAggregatedOnce()
    {
        var producer = Descriptor("dup-model");
        var fact = CreateFact("kind-duplicate", producer);
        var model = new StubModel(producer, result: new ModelResult(true, facts: [fact, fact]));
        var host = new FrameworkModelHost([model]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Single(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task SemanticallyEqualDuplicateFactsDeduplicateWithoutConflict()
    {
        // Two separately constructed facts with identical ID, payload, certainty, and evidence values
        // are the same fact and must deduplicate rather than be treated as a conflict.
        var producer = Descriptor("equal-dup-model");
        var factA = CreateFact("kind-equal", producer);
        var factB = CreateFact("kind-equal", producer);
        var model = new StubModel(producer, result: new ModelResult(true, facts: [factA, factB]));
        var host = new FrameworkModelHost([model]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Single(result.Facts);
        Assert.Empty(result.Diagnostics);
    }

    private static FrameworkModelDescriptor Descriptor(string modelId, int order = 1, string version = "1.0.0")
        => new(modelId, version, modelId, order);

    private static ModelResult ResultWithFact(string kind, FrameworkModelDescriptor producer)
        => new(true, facts: [CreateFact(kind, producer)]);

    private static GeneralBehaviorFact CreateFact(
        string kind,
        FrameworkModelDescriptor producer,
        CertaintyLevel certainty = CertaintyLevel.Exact,
        int sibling = 0)
    {
        return new GeneralBehaviorFact
        {
            Id = CreateFactId(kind, sibling),
            Kind = kind,
            Evidence = [CreateModelEvidence(producer)],
            Certainty = certainty,
        };
    }

    private static BehaviorFactId CreateFactId(string kind, int sibling = 0)
    {
        return StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
            Profile: new CompilationProfileId("profile:v1:test"),
            ModelId: "test-model",
            ModelVersion: "1.0.0",
            FactKind: kind,
            Anchor: new DocumentBehaviorFactAnchor(
                new DocumentId("document:v1:test"),
                100,
                24,
                new SymbolId("Company.App.Run")),
            SameKindSiblingOrdinal: sibling));
    }

    private static CompilationProfile CreateProfile()
        => CompilationProfile.Create("src/App/App.csproj", "Release", "net10.0");

    private static ProgramIndexSnapshot CreateProgramIndex()
    {
        return new ProgramIndexSnapshot(
            SchemaVersion: 1,
            ProducerVersion: "test",
            Profile: CreateProfile(),
            Projects: [],
            Documents: [],
            Namespaces: [],
            Types: [],
            Members: [],
            Methods: [],
            Attributes: [],
            References: [],
            Invocations: [],
            InventoryMarkers: [],
            Diagnostics: [],
            InputManifestHash: "input-hash",
            IndexFingerprint: "index-fingerprint");
    }

    private static FrameworkDetectionContext CreateDetectionContext()
        => new(CreateProfile(), CreateProgramIndex());

    private static FrameworkAnalysisContext CreateAnalysisContext()
        => new(CreateProfile(), CreateProgramIndex());

    private static FrameworkAnalysisRequest CreateRequest(
        ImmutableArray<OperationDescriptor>? operations = null,
        ImmutableArray<SymbolDescriptor>? symbols = null)
    {
        return new FrameworkAnalysisRequest(
            CreateDetectionContext(),
            CreateAnalysisContext(),
            operations ?? [CreateOperation()],
            symbols ?? ImmutableArray<SymbolDescriptor>.Empty);
    }

    private static OperationDescriptor CreateOperation(string kind = "Invocation")
        => new(
            new OperationId("operation:v1:test"),
            new MethodId("method:v1:test"),
            kind,
            new DocumentId("document:v1:test"),
            100,
            24,
            [CreateSourceEvidence()],
            CertaintyLevel.Exact);

    private static SymbolDescriptor CreateSymbol(string kind = "NamedType", string metadataName = "Company.App.TicketService")
        => new(
            new SymbolId("symbol:v1:test"),
            kind,
            metadataName,
            new DocumentId("document:v1:test"),
            100,
            24,
            [CreateSourceEvidence()],
            CertaintyLevel.Exact);

    private static EvidenceRef CreateSourceEvidence(string symbol = "Company.App.Run")
        => new(
            new EvidenceId("evidence:v1:test"),
            EvidenceKind.Source,
            "src/App.cs",
            new SourceRange(new DocumentId("document:v1:test"), new SourcePosition(10, 4), new SourcePosition(10, 24)),
            symbol,
            detail: null,
            CertaintyLevel.Exact);

    private static EvidenceRef CreateModelEvidence(FrameworkModelDescriptor producer)
    {
        var source = CreateSourceEvidence("Company.Web.TicketsController.Reserve");
        return new EvidenceRef(
            new EvidenceId("evidence:v1:model"),
            EvidenceKind.FrameworkModel,
            $"{producer.ModelId}:{producer.Version}",
            range: null,
            symbol: "Company.Web.TicketsController.Reserve",
            detail: "framework-model evidence",
            CertaintyLevel.Exact,
            [source],
            producerId: producer.ModelId,
            producerVersion: producer.Version);
    }

    private static AnalysisDiagnostic CreateDiagnostic(string code, int ordinal)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.FrameworkModel,
            new CompilationProfileId("profile:v1:test"),
            SubjectId: null,
            ordinal));

        return new AnalysisDiagnostic(
            id,
            code,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            $"summary {code}",
            new DiagnosticLocation("test location"),
            "technical cause",
            "user impact",
            "next action",
            CertaintyLevel.Exact);
    }

    private sealed class StubModel : IFrameworkBehaviorModel
    {
        private readonly ModelResult _result;

        public StubModel(FrameworkModelDescriptor descriptor, bool applicable = true, ModelResult? result = null)
        {
            Descriptor = descriptor;
            Applicable = applicable;
            _result = result ?? ModelResult.Unrecognized;
        }

        public FrameworkModelDescriptor Descriptor { get; }

        public bool Applicable { get; set; }

        public int OperationCalls { get; private set; }

        public int SymbolCalls { get; private set; }

        public bool IsApplicable(FrameworkDetectionContext context) => Applicable;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            OperationCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_result);
        }

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            SymbolCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class CancelingModel : IFrameworkBehaviorModel
    {
        public FrameworkModelDescriptor Descriptor { get; } = new("cancel-model", "1.0.0", "Canceling Model", 1);

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => throw new OperationCanceledException(cancellationToken);

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => throw new OperationCanceledException(cancellationToken);
    }
}
