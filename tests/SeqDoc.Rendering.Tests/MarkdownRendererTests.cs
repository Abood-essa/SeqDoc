using SeqDoc.Core.Evidence;
using SeqDoc.Core.Wording;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void RenderDocumentPlacesSequenceBeforeBehaviorAndFallback()
    {
        var wording = new WordingDocument(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            "Credit transfer",
            [
                new WordingPhrase(
                    new("wording-phrase:v1:test:behavior"),
                    "behavior",
                    WordingPhraseKind.Statement,
                    "Behavior phrase.",
                    [PlanTestFactory.SourceEvidence("behavior")],
                    CertaintyLevel.Exact),
                new WordingPhrase(
                    new("wording-phrase:v1:test:fallback"),
                    "fallback",
                    WordingPhraseKind.TechnicalFallback,
                    "Fallback phrase.",
                    [PlanTestFactory.SourceEvidence("fallback")],
                    CertaintyLevel.Conservative),
            ],
            "wording-document:v1:test");
        var diagram = PlanTestFactory.CreateDiagramPlan();

        string rendered = MarkdownRenderer.RenderDocument(wording, diagram);

        const string expected = "# Credit transfer\n\n"
            + "SeqDoc generated this documentation from compiler evidence. Every statement retains supporting evidence and explicit certainty.\n\n"
            + "## Sequence diagram\n\n"
            + "```mermaid\n"
            + "sequenceDiagram\n"
            + "    participant client as \"Client\"\n"
            + "    participant service as \"GadgetService\"\n"
            + "    client->>service: GET api/Test\n"
            + "    alt success path\n"
            + "        service-->>client: Ok -> HTTP 200\n"
            + "    end\n"
            + "```\n"
            + "## Behavior\n\n"
            + "- Behavior phrase. _(certainty: Exact; evidence: behavior)_\n\n"
            + "## Technical fallback\n\n"
            + "- Fallback phrase. _(certainty: Conservative; evidence: fallback)_\n";

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void RenderDocumentPlacesSequenceBeforeBehaviorWithoutFallbackAndPreservesPayload()
    {
        var wording = PlanTestFactory.CreateWordingDocument();
        var diagram = PlanTestFactory.CreateDiagramPlan(messageLabel: "payload stays byte-identical");

        string rendered = MarkdownRenderer.RenderDocument(wording, diagram);
        Assert.DoesNotContain("## Technical fallback", rendered, StringComparison.Ordinal);
    }
}
