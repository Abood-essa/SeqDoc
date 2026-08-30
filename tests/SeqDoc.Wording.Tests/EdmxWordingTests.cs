using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Wording.Tests;

public sealed class EdmxWordingTests
{
    [Fact]
    public void EdmxFunctionImportWordingIsDeclarationOnly()
    {
        var evidence = new EvidenceRef(
            new EvidenceId("evidence:edmx"), EvidenceKind.Source, "tests/fixtures/PassC/EntityFramework6Edmx/Model.edmx",
            new SourceRange(new DocumentId("document:edmx"), new SourcePosition(1, 0), new SourcePosition(1, 1)),
            "Model.edmx", null, CertaintyLevel.Exact);
        var action = new ScenarioNode(
            new ScenarioNodeId("scenario-node:edmx:action"), ScenarioNodeKind.Action, "action", new MethodId("method:edmx:root"), null,
            "action", [evidence], CertaintyLevel.Exact);
        var metadata = new ScenarioNode(
            new ScenarioNodeId("scenario-node:edmx:metadata"), ScenarioNodeKind.SourceObservation, "metadata", null, null,
            "EDMX metadata boundary: tests/fixtures/PassC/EntityFramework6Edmx/Model.edmx; FunctionImport declaration present: True; store-function declaration present: True; database mapping and runtime behavior are not inferred.",
            [evidence], CertaintyLevel.Exact);
        var graph = new ScenarioGraph(
            new EntryPointId("entry-point:edmx"), ScenarioGraphTestFactory.Profile.Id, new MethodId("method:edmx:root"), HttpMethodKind.Post,
            "/edmx", "POST /edmx", [action, metadata],
            [new ScenarioEdge(new ScenarioEdgeId("edge:edmx"), action.Id, metadata.Id, ScenarioEdgeKind.Observation, "independent metadata boundary", [evidence], CertaintyLevel.Exact)],
            [], "edmx", ScenarioTopology.Empty);

        var plan = DocumentationPlanner.Plan(graph);
        var text = string.Join("\n", plan.Wording.Phrases.Select(phrase => phrase.Text));
        Assert.Contains("FunctionImport declaration", text, StringComparison.Ordinal);
        Assert.Contains("store-function declaration", text, StringComparison.Ordinal);
        Assert.DoesNotContain("execution", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rows", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commit", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transaction", text, StringComparison.OrdinalIgnoreCase);
    }
}
