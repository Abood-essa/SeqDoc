using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests;

public sealed class FrameworkModelContractTests
{
    [Fact]
    public void ModelResultNormalizesDefaultArraysToEmpty()
    {
        var result = new ModelResult(recognized: true);

        Assert.True(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.ResolutionHints);
        Assert.Empty(result.SuppressionHints);
        Assert.Empty(result.SummaryRules);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UnrecognizedResultCarriesNoBehaviorArtifacts()
    {
        var result = ModelResult.Unrecognized;

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Empty(result.ResolutionHints);
        Assert.Empty(result.SuppressionHints);
        Assert.Empty(result.SummaryRules);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UnrecognizedResultRejectsFactsHintsAndRules()
    {
        var fact = CreateFact("kind");

        Assert.Throws<ArgumentException>(() => new ModelResult(recognized: false, facts: [fact]));
        Assert.Throws<ArgumentException>(() => new ModelResult(recognized: false, resolutionHints: [CreateHint()]));
        Assert.Throws<ArgumentException>(() => new ModelResult(recognized: false, suppressionHints: [CreateSuppression()]));
        Assert.Throws<ArgumentException>(() => new ModelResult(recognized: false, summaryRules: [CreateRule()]));
    }

    [Fact]
    public void UnrecognizedResultMayCarryTypedDiagnostics()
    {
        // Unsupported patterns produce typed diagnostics without claiming behavior.
        var diagnostic = CreateDiagnostic("SEQFW9001");
        var result = new ModelResult(recognized: false, diagnostics: [diagnostic]);

        Assert.False(result.Recognized);
        Assert.Equal(diagnostic, Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void GeneralBehaviorFactRetainsKindAndDetail()
    {
        var fact = new GeneralBehaviorFact
        {
            Id = CreateFactId("http-entry-point"),
            Kind = "http-entry-point",
            Detail = "route template /api/tickets",
            Evidence = [CreateSourceEvidence()],
            Certainty = CertaintyLevel.Exact,
        };

        Assert.Equal("http-entry-point", fact.Kind);
        Assert.Equal("route template /api/tickets", fact.Detail);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
    }

    [Fact]
    public void ContextsExposeRoslynNeutralProgramIndexForSymbolAndPackageDetection()
    {
        var profile = CompilationProfile.Create("src/App/App.csproj", "Release", "net10.0");
        var programIndex = CreateProgramIndex(profile);
        var reference = new ProgramReference(
            "package:Microsoft.AspNetCore.Mvc",
            new ProjectId("project:v1:test"),
            ProgramReferenceKind.Package,
            "Microsoft.AspNetCore.Mvc",
            "9.0.0",
            [CreateSourceEvidence()]);

        var detection = new FrameworkDetectionContext(profile, programIndex);
        var analysis = new FrameworkAnalysisContext(profile, programIndex with
        {
            References = [reference],
        });

        Assert.Same(programIndex, detection.ProgramIndex);
        Assert.Equal("Microsoft.AspNetCore.Mvc", analysis.ProgramIndex.References[0].Identity);
        Assert.Equal(ProgramReferenceKind.Package, analysis.ProgramIndex.References[0].Kind);
    }

    [Fact]
    public void FrameworkModelEvidenceCannotBypassProducerAndSourceProvenance()
    {
        var source = new EvidenceRef(
            new EvidenceId("evidence:v1:source"),
            EvidenceKind.Source,
            "src/Controllers/OrdersController.cs",
            new SourceRange(
                new DocumentId("document:v1:source"),
                new SourcePosition(10, 4),
                new SourcePosition(10, 24)),
            symbol: "OrdersController.Create",
            detail: null,
            CertaintyLevel.Exact);

        Assert.Throws<ArgumentException>(() => new EvidenceRef(
            new EvidenceId("evidence:v1:model"),
            EvidenceKind.FrameworkModel,
            "aspnet-core-controller:v1",
            range: null,
            symbol: "OrdersController.Create",
            detail: null,
            CertaintyLevel.Exact,
            [source],
            producerId: " ",
            producerVersion: "1"));

        var model = new EvidenceRef(
            new EvidenceId("evidence:v1:model"),
            EvidenceKind.FrameworkModel,
            "aspnet-core-controller:v1",
            range: null,
            symbol: "OrdersController.Create",
            detail: null,
            CertaintyLevel.Exact,
            [source],
            producerId: "aspnet-core-controller",
            producerVersion: "1");

        Assert.Equal("aspnet-core-controller", model.ProducerId);
        Assert.Equal("1", model.ProducerVersion);
        Assert.Equal(source, Assert.Single(model.UnderlyingEvidence));
    }

    private static BehaviorFactId CreateFactId(string kind)
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
            SameKindSiblingOrdinal: 0));
    }

    private static GeneralBehaviorFact CreateFact(string kind)
    {
        return new GeneralBehaviorFact
        {
            Id = CreateFactId(kind),
            Kind = kind,
            Evidence = [CreateSourceEvidence()],
            Certainty = CertaintyLevel.Exact,
        };
    }

    private static CallResolutionHint CreateHint()
        => new(new OperationId("operation:v1:test"), new MethodId("method:v1:test"), null, "reason", 0, [CreateSourceEvidence()], CertaintyLevel.Conservative);

    private static SuppressionHint CreateSuppression()
        => new("scope", "reason", 0, [CreateSourceEvidence()], CertaintyLevel.Conservative);

    private static MethodSummaryRule CreateRule()
        => new("scope", "reason", 0, [CreateSourceEvidence()], CertaintyLevel.Conservative);

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

    private static EvidenceRef CreateSourceEvidence(string symbol = "Company.App.Run")
        => new(
            new EvidenceId("evidence:v1:test"),
            EvidenceKind.Source,
            "src/App.cs",
            new SourceRange(new DocumentId("document:v1:test"), new SourcePosition(10, 4), new SourcePosition(10, 24)),
            symbol,
            detail: null,
            CertaintyLevel.Exact);

    private static ProgramIndexSnapshot CreateProgramIndex(CompilationProfile profile)
    {
        return new ProgramIndexSnapshot(
            SchemaVersion: 1,
            ProducerVersion: "test",
            Profile: profile,
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
}
