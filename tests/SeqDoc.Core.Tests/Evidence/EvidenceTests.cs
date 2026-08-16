using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Core.Tests.Evidence;

public sealed class EvidenceTests
{
    [Fact]
    public void SourceRangeRejectsEndBeforeStart()
    {
        var document = new DocumentId("document:v1:test");

        Assert.Throws<ArgumentException>(() => new SourceRange(
            document,
            new SourcePosition(4, 2),
            new SourcePosition(3, 8)));
    }

    [Fact]
    public void FrameworkModelEvidenceRequiresUnderlyingEvidence()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceRef(
            new EvidenceId("evidence:v1:model"),
            EvidenceKind.FrameworkModel,
            "aspnet-core-controller:v1",
            range: null,
            symbol: null,
            detail: null,
            CertaintyLevel.Exact,
            producerId: "aspnet-core-controller",
            producerVersion: "1"));
    }

    [Fact]
    public void FrameworkModelEvidenceRetainsUnderlyingSourceEvidence()
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
        var model = new EvidenceRef(
            new EvidenceId("evidence:v1:model"),
            EvidenceKind.FrameworkModel,
            "aspnet-core-controller:v1",
            range: null,
            symbol: "OrdersController.Create",
            detail: "ASP.NET Core controller model version 1",
            CertaintyLevel.Exact,
            [source],
            producerId: "aspnet-core-controller",
            producerVersion: "1");

        var retainedSource = Assert.Single(model.UnderlyingEvidence);
        Assert.Same(source, retainedSource);
        Assert.Equal(source.Id, retainedSource.Id);
    }

    [Fact]
    public void FrameworkModelEvidenceRejectsNonSourceProvenance()
    {
        var configuration = new EvidenceRef(
            new EvidenceId("evidence:v1:configuration"),
            EvidenceKind.Configuration,
            "seqdoc.yml",
            range: null,
            symbol: null,
            detail: null,
            CertaintyLevel.Exact);

        Assert.Throws<ArgumentException>(() => new EvidenceRef(
            new EvidenceId("evidence:v1:model"),
            EvidenceKind.FrameworkModel,
            "aspnet-core-controller:v1",
            range: null,
            symbol: null,
            detail: null,
            CertaintyLevel.Exact,
            [configuration],
            producerId: "aspnet-core-controller",
            producerVersion: "1"));
    }
}
