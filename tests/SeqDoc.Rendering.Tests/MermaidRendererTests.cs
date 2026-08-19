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
        Assert.Contains("participant client as Client #quot;quoted#quot; & Co#59; Ltd. (UK) {x}", mermaid, StringComparison.Ordinal);
        Assert.EndsWith("end", mermaid.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParticipantAliasesHaveNoLiteralWrappingQuotesForConciseLabels()
    {
        string mermaid = MermaidRenderer.Render(PlanTestFactory.CreateDiagramPlan(participantLabel: "API"));

        string[] participantLines = mermaid.Split('\n')
            .Where(line => line.TrimStart().StartsWith("participant ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, participantLines.Length);
        Assert.All(participantLines, line => Assert.DoesNotContain('"', line));
        Assert.Contains("participant client as API", mermaid, StringComparison.Ordinal);
        Assert.Contains("participant service as GadgetService", mermaid, StringComparison.Ordinal);
        Assert.Empty(MermaidValidator.Validate(mermaid));
    }

    [Fact]
    public void HostileParticipantAliasTextIsEscapedAndRemainsValidatorSafe()
    {
        const string hostile = "API\r\n\t\u0001\";drop;`fence`";
        string mermaid = MermaidRenderer.Render(PlanTestFactory.CreateDiagramPlan(participantLabel: hostile));
        string participantLine = mermaid.Split('\n').Single(line => line.Contains("participant client as ", StringComparison.Ordinal));

        Assert.DoesNotContain('\r', mermaid);
        Assert.DoesNotContain('\n', participantLine);
        Assert.DoesNotContain('"', participantLine);
        Assert.DoesNotContain(";drop;", participantLine, StringComparison.Ordinal);
        Assert.DoesNotContain('`', participantLine);
        Assert.Contains("#59;", participantLine, StringComparison.Ordinal);
        Assert.Contains("#96;", participantLine, StringComparison.Ordinal);
        Assert.Empty(MermaidValidator.Validate(mermaid));
    }
}
