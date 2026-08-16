using SeqDoc.Core.Frameworks;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

public sealed class MinimalApiScenarioTests
{
    [Fact]
    public void MinimalApiEntryIsPresentedAsSparseHandlerWithoutControllerOrOutcomeClaims()
    {
        var request = ScenarioTestFactory.CreateMinimalApiRequest(new MinimalApiRouteFact
        {
            Id = new("behavior-fact:v1:minimal-api-route"),
            Evidence = [new SeqDoc.Core.Evidence.EvidenceRef(
                new SeqDoc.Core.Identity.EvidenceId("evidence:v1:minimal-api"),
                SeqDoc.Core.Evidence.EvidenceKind.Source,
                "Program.cs",
                null,
                "MapPost", null, SeqDoc.Core.Evidence.CertaintyLevel.Exact)],
            Certainty = SeqDoc.Core.Evidence.CertaintyLevel.Exact,
            EntryPointId = new("entry-point:v1:minimal-post"),
            HandlerRoot = new("method:v1:Program.PostItems"),
            HandlerKind = MinimalApiHandlerKind.NamedMethod,
            HttpMethod = HttpMethodKind.Post,
            CanonicalRoute = "/api/sms",
            OperationKey = "POST /api/sms",
        });
        var graph = Assert.Single(SeqDoc.Analysis.Scenarios.ScenarioGraphBuilder.Build(request).Graphs);

        Assert.Contains(graph.Nodes, node => node.Detail == "minimal API handler");
        Assert.DoesNotContain(graph.Nodes, node => node.Kind == SeqDoc.Core.ScenarioGraph.ScenarioNodeKind.Outcome);
        Assert.Contains(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
        Assert.DoesNotContain(graph.DebugProjection, Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactHandlerFactsProduceTypedSequentialTopologyAndSuppressSparseFallback()
    {
        var request = ScenarioTestFactory.CreateMinimalApiHandlerRequest();
        var graph = Assert.Single(SeqDoc.Analysis.Scenarios.ScenarioGraphBuilder.Build(request).Graphs);
        Assert.NotNull(graph.HandlerTopology);
        var topology = graph.HandlerTopology!;

        Assert.Equal(["roll is at most 30", "roll is at most 50"], topology.Decisions.Select(item => item.PredicateText));
        Assert.Null(topology.Decisions[0].ParentDecisionOrdinal);
        Assert.Equal(0, topology.Decisions[1].ParentDecisionOrdinal);
        Assert.False(topology.Decisions[1].ParentIsTrue);
        Assert.Equal(3, topology.Outcomes.Length);
        Assert.Equal([(0, 0, true), (2, 1, true), (3, 1, false)], topology.Outcomes.Select(item => (item.SourceOrdinal, item.DecisionOrdinal, item.IsTrue)));
        Assert.Equal((1, 1, true), (topology.Delays[0].SourceOrdinal, topology.Delays[0].DecisionOrdinal, topology.Delays[0].IsTrue));
        var parameter = Assert.Single(graph.Nodes, node => node.Key == "handler-parameter:request");
        Assert.Equal(HttpBindingKind.Body, parameter.Presentation?.HandlerBindingKind);
        Assert.Equal("request", parameter.Presentation?.HandlerParameterName);
        Assert.Equal("SmsRequest", parameter.Presentation?.HandlerParameterTypeName);
        Assert.Equal(0, parameter.Presentation?.SourceOrdinal);
        Assert.All(graph.Nodes.Where(node => node.Kind is SeqDoc.Core.ScenarioGraph.ScenarioNodeKind.Delay or SeqDoc.Core.ScenarioGraph.ScenarioNodeKind.Outcome), node =>
            Assert.Equal(SeqDoc.Core.ScenarioGraph.ScenarioActionKind.MinimalApiHandler, node.Presentation?.ActionKind));
        Assert.DoesNotContain(graph.Diagnostics, diagnostic => diagnostic.Code == "SC001");
        Assert.Contains(graph.Nodes, node => node.Kind == SeqDoc.Core.ScenarioGraph.ScenarioNodeKind.Delay);
    }
}
