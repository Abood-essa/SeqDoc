using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests;

/// <summary>
/// Adversarial host tests for acceptance-critical risks that the main host suite does not cover:
/// input-order determinism, conflicting duplicate fact identities, invalid hint/rule payloads,
/// diagnostics-only unrecognized results, and cancellation between multiple inputs.
/// </summary>
public sealed class FrameworkModelHostAdversarialTests
{
    [Fact]
    public async Task AggregateIsIndependentOfInputOperationAndSymbolOrder()
    {
        var operationA = CreateOperation("Invocation");
        var operationB = CreateOperation("ObjectCreation");
        var symbolA = CreateSymbol("NamedType", "Company.App.ServiceA");
        var symbolB = CreateSymbol("NamedType", "Company.App.ServiceB");
        var hostForward = new FrameworkModelHost([new InputKeyedModel(Descriptor("input-order-model"))]);
        var hostReverse = new FrameworkModelHost([new InputKeyedModel(Descriptor("input-order-model"))]);

        var forward = await hostForward.AnalyzeAsync(
            CreateRequest(operations: [operationA, operationB], symbols: [symbolA, symbolB]),
            CancellationToken.None);
        var reverse = await hostReverse.AnalyzeAsync(
            CreateRequest(operations: [operationB, operationA], symbols: [symbolB, symbolA]),
            CancellationToken.None);

        // Compare stable identity and ordering only: record equality on ImmutableArray-backed
        // evidence is reference-based, so value comparison across independent runs is misleading.
        Assert.Equal(
            forward.Facts.Select(fact => fact.Id.Value).ToArray(),
            reverse.Facts.Select(fact => fact.Id.Value).ToArray());
        Assert.Equal(
            forward.ResolutionHints
                .Select(hint => (hint.Ordinal, hint.TargetMethod?.Value, hint.Reason))
                .ToArray(),
            reverse.ResolutionHints
                .Select(hint => (hint.Ordinal, hint.TargetMethod?.Value, hint.Reason))
                .ToArray());
        Assert.Equal(
            forward.Diagnostics.Select(diagnostic => diagnostic.Id.Value).ToArray(),
            reverse.Diagnostics.Select(diagnostic => diagnostic.Id.Value).ToArray());
        Assert.Equal(
            forward.AppliedModels.Select(descriptor => descriptor.ModelId).ToArray(),
            reverse.AppliedModels.Select(descriptor => descriptor.ModelId).ToArray());
    }

    [Fact]
    public async Task ConflictingDuplicateFactIdsExcludeBothAndEmitStableDiagnostic()
    {
        // A model may emit the same fact identity with conflicting payloads for different inputs.
        // The aggregate must never silently keep whichever input happened to run first; the ambiguous
        // identity is excluded entirely and reported once with a stable diagnostic.
        var sharedId = CreateFactId("conflicting-kind");
        var hostA = new FrameworkModelHost([new ConflictingDupModel(Descriptor("conflicting-model"), sharedId)]);
        var hostB = new FrameworkModelHost([new ConflictingDupModel(Descriptor("conflicting-model"), sharedId)]);

        var firstOrder = await hostA.AnalyzeAsync(
            CreateRequest(operations: [CreateOperation("Invocation"), CreateOperation("ObjectCreation")]),
            CancellationToken.None);
        var secondOrder = await hostB.AnalyzeAsync(
            CreateRequest(operations: [CreateOperation("ObjectCreation"), CreateOperation("Invocation")]),
            CancellationToken.None);

        Assert.Empty(firstOrder.Facts);
        Assert.Empty(secondOrder.Facts);
        Assert.Equal(
            firstOrder.Diagnostics.Select(diagnostic => diagnostic.Id.Value).ToArray(),
            secondOrder.Diagnostics.Select(diagnostic => diagnostic.Id.Value).ToArray());
        var diagnostic = Assert.Single(firstOrder.Diagnostics);
        Assert.Equal("SEQFW003", diagnostic.Code);
    }

    [Fact]
    public async Task UnrecognizedResultDiagnosticsRemainVisibleWithoutClaimingRecognition()
    {
        var diagnostic = CreateDiagnostic("SEQFW9100");
        var model = new StubModel(
            Descriptor("unrecognized-with-diagnostics"),
            result: new ModelResult(recognized: false, diagnostics: [diagnostic]));
        var host = new FrameworkModelHost([model]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.ResolutionHints);
        Assert.Empty(result.SuppressionHints);
        Assert.Empty(result.SummaryRules);
        Assert.Equal(diagnostic, Assert.Single(result.Diagnostics));
    }

    [Fact]
    public async Task CancellationBetweenInputsStopsLaterInvocations()
    {
        using var cts = new CancellationTokenSource();
        var model = new SelfCancelingModel(cts);
        var host = new FrameworkModelHost([model]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.AnalyzeAsync(
                CreateRequest(operations: [CreateOperation("Invocation"), CreateOperation("ObjectCreation")]),
                cts.Token).AsTask());

        // The first input canceled the token; the host must not invoke the model for the second input.
        Assert.Equal(1, model.OperationCalls);
    }

    [Fact]
    public async Task ResolutionHintWithoutTargetIsExcluded()
    {
        var invalidHint = new CallResolutionHint(
            SourceOperation: new OperationId("operation:v1:test"),
            TargetMethod: null,
            TargetType: null,
            Reason: "registered service",
            Ordinal: 0,
            Evidence: [CreateSourceEvidence()],
            CertaintyLevel.Conservative);
        var host = new FrameworkModelHost(
            [new StubModel(Descriptor("missing-target-model"), result: new ModelResult(true, resolutionHints: [invalidHint]))]);

        var result = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Empty(result.ResolutionHints);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("SEQFW004", diagnostic.Code);
    }

    [Theory]
    [InlineData("resolutionReason")]
    [InlineData("suppressionScope")]
    [InlineData("summaryScope")]
    public async Task BlankSemanticValueArtifactsAreExcluded(string artifactKind)
    {
        var result = artifactKind switch
        {
            "resolutionReason" => new ModelResult(
                true,
                resolutionHints:
                [
                    new CallResolutionHint(new OperationId("operation:v1:test"), new MethodId("method:v1:test"), null, " ", 0, [CreateSourceEvidence()], CertaintyLevel.Conservative),
                ]),
            "suppressionScope" => new ModelResult(
                true,
                suppressionHints: [new SuppressionHint(" ", "framework plumbing", 0, [CreateSourceEvidence()], CertaintyLevel.Conservative)]),
            _ => new ModelResult(
                true,
                summaryRules: [new MethodSummaryRule(" ", "boundary summary", 0, [CreateSourceEvidence()], CertaintyLevel.Conservative)]),
        };
        var host = new FrameworkModelHost([new StubModel(Descriptor($"blank-{artifactKind}"), result: result)]);

        var aggregate = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Empty(aggregate.ResolutionHints);
        Assert.Empty(aggregate.SuppressionHints);
        Assert.Empty(aggregate.SummaryRules);
        var diagnostic = Assert.Single(aggregate.Diagnostics);
        Assert.Equal("SEQFW004", diagnostic.Code);
    }

    [Theory]
    [InlineData("resolution")]
    [InlineData("suppression")]
    [InlineData("summary")]
    public async Task NegativeOrdinalArtifactsAreExcluded(string artifactKind)
    {
        var result = artifactKind switch
        {
            "resolution" => new ModelResult(
                true,
                resolutionHints:
                [
                    new CallResolutionHint(new OperationId("operation:v1:test"), new MethodId("method:v1:test"), null, "reason", -1, [CreateSourceEvidence()], CertaintyLevel.Conservative),
                ]),
            "suppression" => new ModelResult(
                true,
                suppressionHints: [new SuppressionHint("scope", "reason", -1, [CreateSourceEvidence()], CertaintyLevel.Conservative)]),
            _ => new ModelResult(
                true,
                summaryRules: [new MethodSummaryRule("scope", "reason", -1, [CreateSourceEvidence()], CertaintyLevel.Conservative)]),
        };
        var host = new FrameworkModelHost([new StubModel(Descriptor($"negative-{artifactKind}"), result: result)]);

        var aggregate = await host.AnalyzeAsync(CreateRequest(), CancellationToken.None);

        Assert.Empty(aggregate.ResolutionHints);
        Assert.Empty(aggregate.SuppressionHints);
        Assert.Empty(aggregate.SummaryRules);
        var diagnostic = Assert.Single(aggregate.Diagnostics);
        Assert.Equal("SEQFW004", diagnostic.Code);
    }

    [Fact]
    public async Task DiagnosticsForMultipleInvalidArtifactsAreStableAcrossInputOrder()
    {
        var descriptor = Descriptor("invalid-duplicate-model");
        var hostForward = new FrameworkModelHost([new DuplicateInvalidHintModel(descriptor)]);
        var hostReverse = new FrameworkModelHost([new DuplicateInvalidHintModel(descriptor)]);

        var forward = await hostForward.AnalyzeAsync(
            CreateRequest(operations: [CreateOperation("Invocation"), CreateOperation("ObjectCreation")]),
            CancellationToken.None);
        var reverse = await hostReverse.AnalyzeAsync(
            CreateRequest(operations: [CreateOperation("ObjectCreation"), CreateOperation("Invocation")]),
            CancellationToken.None);

        Assert.Empty(forward.ResolutionHints);
        Assert.Empty(reverse.ResolutionHints);
        Assert.Equal(2, forward.Diagnostics.Count(diagnostic => diagnostic.Code == "SEQFW004"));
        Assert.Equal(
            forward.Diagnostics.Select(diagnostic => diagnostic.Id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            reverse.Diagnostics.Select(diagnostic => diagnostic.Id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task TiedOrdinalHintsOrderStablyAcrossInputOrder()
    {
        var descriptor = Descriptor("tied-ordinal-model");
        var hostForward = new FrameworkModelHost([new TiedOrdinalHintModel(descriptor)]);
        var hostReverse = new FrameworkModelHost([new TiedOrdinalHintModel(descriptor)]);

        var forward = await hostForward.AnalyzeAsync(
            CreateRequest(operations: [CreateOperation("Invocation"), CreateOperation("ObjectCreation")]),
            CancellationToken.None);
        var reverse = await hostReverse.AnalyzeAsync(
            CreateRequest(operations: [CreateOperation("ObjectCreation"), CreateOperation("Invocation")]),
            CancellationToken.None);

        Assert.Equal(
            forward.ResolutionHints.Select(hint => hint.SourceOperation.Value).ToArray(),
            reverse.ResolutionHints.Select(hint => hint.SourceOperation.Value).ToArray());
        // The ordinal is tied, so canonical ordering must follow the source operation identity.
        Assert.Equal(
            forward.ResolutionHints.Select(hint => hint.SourceOperation.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            forward.ResolutionHints.Select(hint => hint.SourceOperation.Value).ToArray());
    }

    [Fact]
    public async Task ModelsInvokeInputsInCanonicalIdentityOrderIndependentOfRequestOrder()
    {
        var descriptor = Descriptor("recording-model");
        var forwardModel = new RecordingInvocationModel(descriptor);
        var reverseModel = new RecordingInvocationModel(descriptor);
        var hostForward = new FrameworkModelHost([forwardModel]);
        var hostReverse = new FrameworkModelHost([reverseModel]);

        var forward = await hostForward.AnalyzeAsync(
            CreateRequest(
                operations: [CreateOperation("ObjectCreation"), CreateOperation("Invocation")],
                symbols: [CreateSymbol("Method", "Company.App.ServiceB"), CreateSymbol("NamedType", "Company.App.ServiceA")]),
            CancellationToken.None);
        var reverse = await hostReverse.AnalyzeAsync(
            CreateRequest(
                operations: [CreateOperation("Invocation"), CreateOperation("ObjectCreation")],
                symbols: [CreateSymbol("NamedType", "Company.App.ServiceA"), CreateSymbol("Method", "Company.App.ServiceB")]),
            CancellationToken.None);

        string[] expectedOrder =
        [
            "operation:operation:v1:Invocation",
            "operation:operation:v1:ObjectCreation",
            "symbol:symbol:v1:Method",
            "symbol:symbol:v1:NamedType",
        ];
        Assert.Equal(expectedOrder, forwardModel.InvocationOrder.ToArray());
        Assert.Equal(forwardModel.InvocationOrder.ToArray(), reverseModel.InvocationOrder.ToArray());
        Assert.Equal(
            forward.Facts.Select(fact => fact.Id.Value).ToArray(),
            reverse.Facts.Select(fact => fact.Id.Value).ToArray());
    }

    [Fact]
    public async Task ReversedEvidenceOrderDeduplicatesInsteadOfConflicting()
    {
        // The model emits the same fact identity with the same evidence values but reversed order for
        // different operations. Canonical evidence ordering must make the facts semantically equal so
        // they deduplicate instead of conflicting, and the stored evidence must be canonical.
        var descriptor = Descriptor("reversed-evidence-model");
        var hostForward = new FrameworkModelHost([new ReversedEvidenceModel(descriptor)]);
        var hostReverse = new FrameworkModelHost([new ReversedEvidenceModel(descriptor)]);

        var forward = await hostForward.AnalyzeAsync(
            CreateRequest(operations: [CreateOperation("Invocation"), CreateOperation("ObjectCreation")]),
            CancellationToken.None);
        var reverse = await hostReverse.AnalyzeAsync(
            CreateRequest(operations: [CreateOperation("ObjectCreation"), CreateOperation("Invocation")]),
            CancellationToken.None);

        Assert.Single(forward.Facts);
        Assert.Single(reverse.Facts);
        Assert.Empty(forward.Diagnostics);
        Assert.Empty(reverse.Diagnostics);
        var forwardEvidence = Assert.IsType<GeneralBehaviorFact>(Assert.Single(forward.Facts)).Evidence;
        var reverseEvidence = Assert.IsType<GeneralBehaviorFact>(Assert.Single(reverse.Facts)).Evidence;
        Assert.Equal(
            forwardEvidence.Select(evidence => evidence.Id.Value).ToArray(),
            reverseEvidence.Select(evidence => evidence.Id.Value).ToArray());
        Assert.Equal(
            forwardEvidence.OrderBy(evidence => evidence.Id.Value, StringComparer.Ordinal).Select(evidence => evidence.Id.Value).ToArray(),
            forwardEvidence.Select(evidence => evidence.Id.Value).ToArray());
    }

    private static FrameworkModelDescriptor Descriptor(string modelId, int order = 1)
        => new(modelId, "1.0.0", modelId, order);

    private static BehaviorFactId CreateFactId(string kind)
        => StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
            Profile: new CompilationProfileId("profile:v1:test"),
            ModelId: "test-model",
            ModelVersion: "1.0.0",
            FactKind: kind,
            Anchor: new DocumentBehaviorFactAnchor(
                new DocumentId("document:v1:test"),
                100,
                24,
                new SymbolId("Company.App.Run")),
            SameKindSiblingOrdinal: 0));

    private static OperationDescriptor CreateOperation(string kind = "Invocation")
        => new(
            new OperationId($"operation:v1:{kind}"),
            new MethodId("method:v1:test"),
            kind,
            new DocumentId("document:v1:test"),
            100,
            24,
            [CreateSourceEvidence()],
            CertaintyLevel.Exact);

    private static SymbolDescriptor CreateSymbol(string kind = "NamedType", string metadataName = "Company.App.TicketService")
        => new(
            new SymbolId($"symbol:v1:{kind}"),
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

    private static EvidenceRef CreateModelEvidence(FrameworkModelDescriptor producer, string suffix = "")
    {
        var source = CreateSourceEvidence("Company.Web.TicketsController.Reserve");
        return new EvidenceRef(
            new EvidenceId($"evidence:v1:model{suffix}"),
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

    private static AnalysisDiagnostic CreateDiagnostic(string code)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.FrameworkModel,
            new CompilationProfileId("profile:v1:test"),
            SubjectId: null,
            Ordinal: 0));

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

    private sealed class StubModel : IFrameworkBehaviorModel
    {
        private readonly ModelResult _result;

        public StubModel(FrameworkModelDescriptor descriptor, ModelResult? result = null)
        {
            Descriptor = descriptor;
            _result = result ?? ModelResult.Unrecognized;
        }

        public FrameworkModelDescriptor Descriptor { get; }

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_result);
        }

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_result);
        }
    }

    /// <summary>Emits facts, hints, and diagnostics keyed by the input itself, never by arrival order.</summary>
    private sealed class InputKeyedModel : IFrameworkBehaviorModel
    {
        public InputKeyedModel(FrameworkModelDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public FrameworkModelDescriptor Descriptor { get; }

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(ResultForKey(operation.Kind));

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(ResultForKey(symbol.MetadataName));

        private ModelResult ResultForKey(string key)
        {
            var ordinal = OrdinalFor(key);
            var fact = new GeneralBehaviorFact
            {
                Id = CreateFactId($"kind-{key}"),
                Kind = $"kind-{key}",
                Evidence = [CreateModelEvidence(Descriptor)],
                Certainty = CertaintyLevel.Exact,
            };
            var hint = new CallResolutionHint(
                new OperationId($"operation:v1:{key}"),
                new MethodId($"method:v1:{key}"),
                null,
                key,
                ordinal,
                [CreateModelEvidence(Descriptor)],
                CertaintyLevel.Conservative);
            return new ModelResult(true, facts: [fact], resolutionHints: [hint], diagnostics: [CreateDiagnostic($"SEQFW9{ordinal}")]);
        }

        private static int OrdinalFor(string key)
            => key switch
            {
                "Invocation" => 0,
                "ObjectCreation" => 1,
                _ => 2,
            };
    }

    /// <summary>
    /// Emits the same fact identity for every input with a conflicting payload keyed by the input's
    /// own identity, so a first-occurrence-based dedupe produces different aggregates for different
    /// input orders.
    /// </summary>
    private sealed class ConflictingDupModel : IFrameworkBehaviorModel
    {
        private readonly BehaviorFactId _factId;

        public ConflictingDupModel(FrameworkModelDescriptor descriptor, BehaviorFactId factId)
        {
            Descriptor = descriptor;
            _factId = factId;
        }

        public FrameworkModelDescriptor Descriptor { get; }

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            var fact = new GeneralBehaviorFact
            {
                Id = _factId,
                Kind = "conflicting-kind",
                Detail = operation.Kind == "Invocation" ? "from-invocation" : "from-objectcreation",
                Evidence = [CreateModelEvidence(Descriptor)],
                Certainty = CertaintyLevel.Exact,
            };
            return ValueTask.FromResult(new ModelResult(true, facts: [fact]));
        }

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(ModelResult.Unrecognized);
    }

    /// <summary>
    /// Cancels the provided token on the first operation call and otherwise completes normally, so the
    /// host alone is responsible for not invoking later inputs after cancellation occurs between them.
    /// </summary>
    private sealed class SelfCancelingModel : IFrameworkBehaviorModel
    {
        private readonly CancellationTokenSource _cts;
        private int _operationCalls;

        public SelfCancelingModel(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public FrameworkModelDescriptor Descriptor { get; } = new("self-cancel-model", "1.0.0", "Self Canceling", 1);

        public int OperationCalls => _operationCalls;

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            _operationCalls++;
            if (_operationCalls == 1)
            {
                _cts.Cancel();
            }

            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(ModelResult.Unrecognized);
    }

    /// <summary>
    /// Emits one invalid resolution hint (blank reason) per operation with matching framework
    /// evidence, so the host must emit one stable diagnostic per distinct invalid artifact.
    /// </summary>
    private sealed class DuplicateInvalidHintModel : IFrameworkBehaviorModel
    {
        public DuplicateInvalidHintModel(FrameworkModelDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public FrameworkModelDescriptor Descriptor { get; }

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            var invalid = new CallResolutionHint(
                operation.Id,
                new MethodId($"method:v1:{operation.Kind}"),
                null,
                " ",
                0,
                [CreateModelEvidence(Descriptor)],
                CertaintyLevel.Conservative);
            return ValueTask.FromResult(new ModelResult(true, resolutionHints: [invalid]));
        }

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(ModelResult.Unrecognized);
    }

    /// <summary>
    /// Emits one valid hint per operation with the same ordinal but different targets, so the host's
    /// canonical comparer must break the tie by source operation identity.
    /// </summary>
    private sealed class TiedOrdinalHintModel : IFrameworkBehaviorModel
    {
        public TiedOrdinalHintModel(FrameworkModelDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public FrameworkModelDescriptor Descriptor { get; }

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            var hint = new CallResolutionHint(
                operation.Id,
                new MethodId($"method:v1:{operation.Kind}"),
                null,
                operation.Kind,
                0,
                [CreateModelEvidence(Descriptor)],
                CertaintyLevel.Conservative);
            return ValueTask.FromResult(new ModelResult(true, resolutionHints: [hint]));
        }

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(ModelResult.Unrecognized);
    }

    /// <summary>
    /// Records the exact order in which operations and symbols are delivered, and emits one fact per
    /// operation keyed by the operation's own identity.
    /// </summary>
    private sealed class RecordingInvocationModel : IFrameworkBehaviorModel
    {
        public RecordingInvocationModel(FrameworkModelDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public FrameworkModelDescriptor Descriptor { get; }

        public List<string> InvocationOrder { get; } = [];

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            InvocationOrder.Add($"operation:{operation.Id.Value}");
            var fact = new GeneralBehaviorFact
            {
                Id = CreateFactId($"kind-{operation.Id.Value}"),
                Kind = $"kind-{operation.Id.Value}",
                Evidence = [CreateModelEvidence(Descriptor)],
                Certainty = CertaintyLevel.Exact,
            };
            return ValueTask.FromResult(new ModelResult(true, facts: [fact]));
        }

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            InvocationOrder.Add($"symbol:{symbol.Id.Value}");
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }
    }

    /// <summary>
    /// Emits the same fact identity for every operation with the same evidence values in reversed
    /// order, so only canonical evidence ordering can make the two facts semantically equal.
    /// </summary>
    private sealed class ReversedEvidenceModel : IFrameworkBehaviorModel
    {
        public ReversedEvidenceModel(FrameworkModelDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public FrameworkModelDescriptor Descriptor { get; }

        public bool IsApplicable(FrameworkDetectionContext context) => true;

        public ValueTask<ModelResult> AnalyzeOperationAsync(
            OperationDescriptor operation,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
        {
            var fact = new GeneralBehaviorFact
            {
                Id = CreateFactId("kind-reversed"),
                Kind = "kind-reversed",
                Evidence = operation.Kind == "Invocation"
                    ? [CreateModelEvidence(Descriptor, "a"), CreateModelEvidence(Descriptor, "b")]
                    : [CreateModelEvidence(Descriptor, "b"), CreateModelEvidence(Descriptor, "a")],
                Certainty = CertaintyLevel.Exact,
            };
            return ValueTask.FromResult(new ModelResult(true, facts: [fact]));
        }

        public ValueTask<ModelResult> AnalyzeSymbolAsync(
            SymbolDescriptor symbol,
            FrameworkAnalysisContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(ModelResult.Unrecognized);
    }
}
