using SeqDoc.Analysis.Scenarios;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// Proves a CoreWCF service operation root is admitted and projected through the existing
/// Method-Flow-driven topology path (issue #7): it never behaves as an HTTP action for wording
/// purposes, and its graph identity is deterministic.
/// </summary>
public sealed class CoreWcfServiceOperationScenarioTests
{
    [Fact]
    public void ServiceOperationRootProjectsActionPresentationAndDeterministicTopology()
    {
        var request = ScenarioTestFactory.CreateServiceOperationRequest();

        var first = ScenarioGraphBuilder.Build(request);
        var second = ScenarioGraphBuilder.Build(request);

        var graph = Assert.Single(first.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        Assert.Equal(ScenarioRootKind.HttpEntryPoint, graph.RootKind);
        Assert.Equal($"{ScenarioTestFactory.ServiceContractTypeName}.{ScenarioTestFactory.ServiceOperationName}", graph.OperationKey);

        var action = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
        Assert.Equal(ScenarioActionKind.ServiceOperation, action.Presentation?.ActionKind);
        Assert.Equal(ScenarioTestFactory.ServiceContractTypeName, action.Presentation?.ContractTypeName);
        Assert.Equal(ScenarioTestFactory.ServiceImplementationTypeName, action.Presentation?.ImplementationTypeName);
        Assert.Equal(ScenarioTestFactory.ServiceOperationName, action.Presentation?.ActionMethodName);

        var repeated = Assert.Single(second.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        Assert.Equal(graph.Nodes.Select(node => node.Id.Value), repeated.Nodes.Select(node => node.Id.Value));
        Assert.Equal(graph.Edges.Select(edge => edge.Id.Value), repeated.Edges.Select(edge => edge.Id.Value));
    }
}
