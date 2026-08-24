using System.Collections.Immutable;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Wording.Tests;

/// <summary>
/// Observable-proof test for issue #7's acceptance criterion "deterministic service diagram": a
/// CoreWCF service operation entry point renders protocol-neutral wording and participant labels
/// rather than HTTP-formatted text, and the plan is deterministic across repeated runs.
/// </summary>
public sealed class CoreWcfServiceOperationWordingTests
{
    private const string ContractTypeName = "CoreWcfServices.ICalculatorService";
    private const string ImplementationTypeName = "CoreWcfServices.CalculatorService";
    private const string OperationName = "Add";

    [Fact]
    public void ServiceOperationGraphRendersProtocolNeutralWordingAndParticipantLabels()
    {
        var graph = CreateGraph();

        var plan = DocumentationPlanner.Plan(graph);
        var repeated = DocumentationPlanner.Plan(graph);

        Assert.Equal($"{ContractTypeName}.{OperationName}", plan.Wording.OperationKey);
        Assert.Contains(
            plan.Wording.Phrases,
            phrase => phrase.Text.Contains("Service contract operation entry point", StringComparison.Ordinal)
                && phrase.Text.Contains($"{ContractTypeName}.{OperationName}", StringComparison.Ordinal));
        Assert.Contains(
            plan.Wording.Phrases,
            phrase => phrase.Text == "The service contract operation executes.");
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("HTTP", StringComparison.Ordinal));

        var action = Assert.Single(plan.Diagram.Participants, participant => participant.Key == "action");
        Assert.Equal("CalculatorService.Add", action.Label);
        var caller = Assert.Single(plan.Diagram.Participants, participant => participant.Kind == DiagramParticipantKind.Client);
        Assert.Equal("Service client", caller.Label);

        Assert.Equal(
            plan.Wording.Phrases.Select(phrase => phrase.Id.Value),
            repeated.Wording.Phrases.Select(phrase => phrase.Id.Value));
    }

    private static ScenarioGraph CreateGraph()
    {
        var evidence = ImmutableArray.Create(SourceEvidence("service-operation"));
        var entry = new ScenarioNode(
            new("scenario-node:v1:service-operation:entry"), ScenarioNodeKind.EntryPoint, "entry",
            new("method:v1:CoreWcfServices.CalculatorService.Add"), null, $"{ContractTypeName}.{OperationName}",
            evidence, CertaintyLevel.Exact);
        var action = new ScenarioNode(
            new("scenario-node:v1:service-operation:action"), ScenarioNodeKind.Action, "action",
            new("method:v1:CoreWcfServices.CalculatorService.Add"), null, "CoreWCF service operation",
            evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.ServiceOperation,
                ContractTypeName: ContractTypeName,
                ImplementationTypeName: ImplementationTypeName,
                ActionMethodName: OperationName));
        var edge = new ScenarioEdge(
            new("scenario-edge:v1:service-operation:entry"), entry.Id, action.Id, ScenarioEdgeKind.Entry,
            string.Empty, evidence, CertaintyLevel.Exact);
        return new ScenarioGraph(
            new("entry-point:v1:service-operation"), ScenarioGraphTestFactory.Profile.Id,
            new("method:v1:CoreWcfServices.CalculatorService.Add"), HttpMethodKind.Unknown, string.Empty,
            $"{ContractTypeName}.{OperationName}", [entry, action], [edge], [], "service-operation",
            ScenarioTopology.Empty);
    }

    private static EvidenceRef SourceEvidence(string artifact)
        => new(
            new EvidenceId($"evidence:v1:{artifact}"),
            EvidenceKind.Source,
            artifact,
            new SourceRange(new DocumentId("document:v1:test"), new SourcePosition(1, 0), new SourcePosition(1, 10)),
            "test-symbol",
            null,
            CertaintyLevel.Exact);
}
