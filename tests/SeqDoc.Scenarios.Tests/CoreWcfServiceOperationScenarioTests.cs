using SeqDoc.Analysis.Scenarios;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

/// <summary>
/// Proves a CoreWCF service operation root is admitted only by joining an independently proven
/// capability fact with a matching registration fact, projected through the existing Method-Flow-driven
/// topology path (issue #7): it never behaves as an HTTP action for wording purposes, never admits a
/// root from capability alone, respects profile/fingerprint binding, propagates the weaker of the two
/// facts' certainties, and its graph identity is deterministic.
/// </summary>
public sealed class CoreWcfServiceOperationScenarioTests
{
    [Fact]
    public void MatchedCapabilityAndRegistrationProjectActionPresentationAndDeterministicTopology()
    {
        var request = ScenarioTestFactory.CreateServiceOperationRequest();

        var first = ScenarioGraphBuilder.Build(request);
        var second = ScenarioGraphBuilder.Build(request);

        var graph = Assert.Single(first.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        Assert.Equal(ScenarioRootKind.HttpEntryPoint, graph.RootKind);
        Assert.Equal(ScenarioTestFactory.ServiceOperationKeyValue, graph.OperationKey);
        Assert.Empty(first.Diagnostics);

        var action = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
        Assert.Equal(ScenarioActionKind.ServiceOperation, action.Presentation?.ActionKind);
        Assert.Equal(ScenarioTestFactory.ServiceContractTypeName, action.Presentation?.ContractTypeName);
        Assert.Equal(ScenarioTestFactory.ServiceImplementationTypeName, action.Presentation?.ImplementationTypeName);
        Assert.Equal(ScenarioTestFactory.ServiceOperationName, action.Presentation?.ActionMethodName);

        var repeated = Assert.Single(second.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        Assert.Equal(graph.Nodes.Select(node => node.Id.Value), repeated.Nodes.Select(node => node.Id.Value));
        Assert.Equal(graph.Edges.Select(edge => edge.Id.Value), repeated.Edges.Select(edge => edge.Id.Value));
    }

    [Fact]
    public void CapabilityWithoutMatchingRegistrationAdmitsNoRootAndProducesAConservativeDiagnostic()
    {
        var request = ScenarioTestFactory.CreateUnregisteredServiceCapabilityRequest();

        var result = ScenarioGraphBuilder.Build(request);

        Assert.DoesNotContain(result.Graphs, graph => graph.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SC-SERVICE-UNSUPPORTED-DISPATCH");
        Assert.Equal(CertaintyLevel.Exact, diagnostic.Certainty);
        Assert.NotEmpty(diagnostic.Evidence);
    }

    [Fact]
    public void ForeignProfileServiceFactsCannotAdmitARoot()
    {
        var current = ScenarioTestFactory.CreateServiceOperationRequest();
        var request = current with
        {
            FrameworkFacts = current.FrameworkFacts with
            {
                ProfileId = ScenarioTestFactory.ForeignProfile.Id,
                ProgramIndexFingerprint = "foreign-index",
            },
        };

        var result = ScenarioGraphBuilder.Build(request);

        Assert.DoesNotContain(result.Graphs, graph => graph.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "SC-SERVICE-UNSUPPORTED-DISPATCH");
    }

    [Fact]
    public void MissingProgramIndexFingerprintCannotAdmitARoot()
    {
        var current = ScenarioTestFactory.CreateServiceOperationRequest();
        var request = current with
        {
            FrameworkFacts = current.FrameworkFacts with { ProgramIndexFingerprint = null },
        };

        var result = ScenarioGraphBuilder.Build(request);

        Assert.DoesNotContain(result.Graphs, graph => graph.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
    }

    [Fact]
    public void AdmittedRootCombinesCapabilityAndRegistrationEvidence()
    {
        var request = ScenarioTestFactory.CreateServiceOperationRequest();

        var result = ScenarioGraphBuilder.Build(request);

        var graph = Assert.Single(result.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        var action = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
        Assert.Contains(action.Evidence, item => item.Artifact == "service-operation-capability");
        Assert.Contains(action.Evidence, item => item.Artifact == "service-endpoint-registration");
    }
}
