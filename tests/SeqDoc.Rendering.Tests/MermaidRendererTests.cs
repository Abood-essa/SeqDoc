using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

public sealed class MermaidRendererTests
{
    [Fact]
    public void EscapesSpecialLabelsAndProducesStructurallyValidMermaid()
    {
        var plan = PlanTestFactory.CreateDiagramPlan(
            participantLabel: "Client \"quoted\" & Co; Ltd. (UK) {x}",
            messageLabel: "call: GetById(\"id\"); async");

        string mermaid = MermaidRenderer.Render(plan);

        Assert.DoesNotContain("\r", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", mermaid[..mermaid.IndexOf('\n')], StringComparison.Ordinal);
        Assert.Contains("#quot;", mermaid, StringComparison.Ordinal);
        Assert.Empty(MermaidValidator.Validate(mermaid));
        Assert.StartsWith("sequenceDiagram", mermaid, StringComparison.Ordinal);
        Assert.Contains("participant client as \"Client #quot;quoted#quot; & Co; Ltd. (UK) {x}\"", mermaid, StringComparison.Ordinal);
        Assert.EndsWith("end", mermaid.TrimEnd(), StringComparison.Ordinal);
    }
}
