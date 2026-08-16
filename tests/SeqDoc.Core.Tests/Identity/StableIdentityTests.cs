using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Core.Tests.Identity;

public sealed class StableIdentityTests
{
    private static BehaviorFactIdentityDescriptor CreateFactDescriptor()
    {
        return new BehaviorFactIdentityDescriptor(
            Profile: new CompilationProfileId("profile:v1:test"),
            ModelId: "seqdoc.aspnetcore.controllers",
            ModelVersion: "1.0.0",
            FactKind: "http-entry-point",
            Anchor: new DocumentBehaviorFactAnchor(
                new DocumentId("document:v1:test"),
                100,
                24,
                new SymbolId("Company.Web.TicketsController.Reserve")),
            SameKindSiblingOrdinal: 0);
    }

    [Fact]
    public void BehaviorFactIdentityIsDeterministicAndVersioned()
    {
        var first = StableIdentity.CreateBehaviorFactId(CreateFactDescriptor());

        Assert.Equal(first, StableIdentity.CreateBehaviorFactId(CreateFactDescriptor()));
        Assert.StartsWith("behavior-fact:v1:", first.Value, StringComparison.Ordinal);
        // Compatibility vector: identical semantic inputs must produce identical bytes on every
        // platform, so this value is a locked contract and must not change.
        Assert.Equal(
            "behavior-fact:v1:aa4c2d4b254b28bcb63a0a4fcae7ebfcb8a3ba4446c643ed70d78ae634a70e0f",
            first.Value);
    }

    [Fact]
    public void BehaviorFactIdentityIsScopedByCompilationProfile()
    {
        Assert.NotEqual(
            StableIdentity.CreateBehaviorFactId(CreateFactDescriptor()),
            StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
            {
                Profile = new CompilationProfileId("profile:v1:other"),
            }));
    }

    [Fact]
    public void BehaviorFactIdentityChangesWhenModelIdentityChanges()
    {
        var baseId = StableIdentity.CreateBehaviorFactId(CreateFactDescriptor());

        Assert.NotEqual(
            baseId,
            StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with { ModelId = "seqdoc.dependencyinjection" }));
        Assert.NotEqual(
            baseId,
            StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with { ModelVersion = "1.1.0" }));
    }

    [Fact]
    public void BehaviorFactIdentityChangesWhenFactKindOrAnchorChanges()
    {
        var baseId = StableIdentity.CreateBehaviorFactId(CreateFactDescriptor());
        var document = new DocumentId("document:v1:test");
        var symbol = new SymbolId("Company.Web.TicketsController.Reserve");

        Assert.NotEqual(
            baseId,
            StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with { FactKind = "http-outcome" }));
        Assert.NotEqual(
            baseId,
            StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
            {
                Anchor = new DocumentBehaviorFactAnchor(document, 120, 24, symbol),
            }));
        Assert.NotEqual(
            baseId,
            StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
            {
                Anchor = new DocumentBehaviorFactAnchor(document, 100, 24, new SymbolId("Company.Web.TicketsController.Get")),
            }));
        Assert.NotEqual(
            baseId,
            StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with { SameKindSiblingOrdinal = 1 }));
    }

    [Fact]
    public void BehaviorFactIdentityDistinguishesAnchorKinds()
    {
        var documentId = new DocumentId("document:v1:test");
        var symbolId = new SymbolId("Company.Web.TicketsController.Reserve");

        var documentAnchored = StableIdentity.CreateBehaviorFactId(CreateFactDescriptor());
        var symbolAnchored = StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new SymbolBehaviorFactAnchor(new ProjectId("project:v1:test"), symbolId),
        });

        Assert.NotEqual(documentAnchored, symbolAnchored);
    }

    [Fact]
    public void SymbolAnchoredIdentityIsProjectScopedAcrossIdenticalMetadataNames()
    {
        var symbol = new SymbolId("Company.Web.TicketsController");

        var first = StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new SymbolBehaviorFactAnchor(new ProjectId("project:v1:first"), symbol),
        });
        var second = StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new SymbolBehaviorFactAnchor(new ProjectId("project:v1:second"), symbol),
        });

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("modelId")]
    [InlineData("modelVersion")]
    [InlineData("factKind")]
    public void BehaviorFactIdentityRejectsBlankSemanticInputs(string field)
    {
        var descriptor = field switch
        {
            "profile" => CreateFactDescriptor() with { Profile = new CompilationProfileId(" ") },
            "modelId" => CreateFactDescriptor() with { ModelId = " " },
            "modelVersion" => CreateFactDescriptor() with { ModelVersion = string.Empty },
            _ => CreateFactDescriptor() with { FactKind = " " },
        };

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(descriptor));
    }

    [Fact]
    public void BehaviorFactIdentityRejectsNegativeSiblingOrdinal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StableIdentity.CreateBehaviorFactId(
            CreateFactDescriptor() with { SameKindSiblingOrdinal = -1 }));
    }

    [Fact]
    public void DocumentAnchorRequiresNonEmptySourceRange()
    {
        var document = new DocumentId("document:v1:test");
        var symbol = new SymbolId("Company.Web.TicketsController.Reserve");

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new DocumentBehaviorFactAnchor(document, 0, 0, symbol),
        }));
    }

    [Fact]
    public void DocumentAnchorRejectsBlankDocumentOrSymbol()
    {
        var symbol = new SymbolId("Company.Web.TicketsController.Reserve");

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new DocumentBehaviorFactAnchor(new DocumentId(" "), 100, 24, symbol),
        }));
        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new DocumentBehaviorFactAnchor(new DocumentId("document:v1:test"), 100, 24, new SymbolId(" ")),
        }));
    }

    [Fact]
    public void SymbolAnchorRejectsBlankProjectOrSymbol()
    {
        var symbol = new SymbolId("Company.Web.TicketsController");

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new SymbolBehaviorFactAnchor(new ProjectId(" "), symbol),
        }));
        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new SymbolBehaviorFactAnchor(new ProjectId("project:v1:test"), new SymbolId(" ")),
        }));
    }

    [Fact]
    public void OperationAnchorRejectsBlankMethodOrOperation()
    {
        var operation = new OperationId("operation:v1:test");

        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new OperationBehaviorFactAnchor(new MethodId(" "), operation),
        }));
        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new OperationBehaviorFactAnchor(new MethodId("method:v1:test"), new OperationId(" ")),
        }));
    }

    [Fact]
    public void ProjectAnchorRejectsBlankProject()
    {
        Assert.Throws<ArgumentException>(() => StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new ProjectBehaviorFactAnchor(new ProjectId(" ")),
        }));
    }

    [Fact]
    public void DocumentlessFactWithTypedSymbolAnchorIsAllowed()
    {
        var id = StableIdentity.CreateBehaviorFactId(CreateFactDescriptor() with
        {
            Anchor = new SymbolBehaviorFactAnchor(
                new ProjectId("project:v1:test"),
                new SymbolId("Company.Web.TicketsController")),
        });

        Assert.StartsWith("behavior-fact:v1:", id.Value, StringComparison.Ordinal);
    }
}
