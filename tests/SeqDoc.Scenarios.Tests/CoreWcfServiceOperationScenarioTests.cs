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
        Assert.Equal(ScenarioRootKind.ServiceOperation, graph.RootKind);
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

    [Fact]
    public void ConservativeRegistrationRetainsBothContributorsExactEvidenceIdsAndOwnCertainty()
    {
        // The exact underlying evidence ID of each contributing fact (not merely its artifact name) must
        // survive into the admitted root's evidence, and a Conservative registration's own evidence item
        // must keep its Conservative certainty even when unioned alongside an Exact capability's evidence
        // (node-presentation certainty is a separate, pre-existing evidence-array mechanism this test does
        // not assert on; the fact-level weakest-certainty rule is proven directly on
        // ServiceOperationEntryPointFact/ScenarioTestFactory-level fixtures elsewhere).
        var exactOnlyResult = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateServiceOperationRequest());
        var exactCapabilityId = exactOnlyResult.Graphs
            .Single(item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint)
            .Nodes.Single(node => node.Kind == ScenarioNodeKind.Action)
            .Evidence.Single(item => item.Artifact == "service-operation-capability").Id;

        var request = ScenarioTestFactory.CreateServiceOperationRequest(
            capabilityCertainty: CertaintyLevel.Exact,
            registrationCertainty: CertaintyLevel.Conservative);
        var result = ScenarioGraphBuilder.Build(request);

        var graph = Assert.Single(result.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        var action = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.Action);

        // The capability's own evidence ID is present by exact ID, not just artifact name.
        Assert.Contains(action.Evidence, item => item.Id == exactCapabilityId);
        Assert.Contains(action.Evidence, item => item.Artifact == "service-endpoint-registration" && item.Certainty == CertaintyLevel.Conservative);
    }

    [Fact]
    public void ConservativeCapabilityWithoutRegistrationDegradesTheUnsupportedDispatchDiagnosticsCertainty()
    {
        var request = ScenarioTestFactory.CreateUnregisteredServiceCapabilityRequest(capabilityCertainty: CertaintyLevel.Conservative);

        var result = ScenarioGraphBuilder.Build(request);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "SC-SERVICE-UNSUPPORTED-DISPATCH");
        Assert.Equal(CertaintyLevel.Conservative, diagnostic.Certainty);
        Assert.Contains(diagnostic.Evidence, item => item.Artifact == "service-operation-capability" && item.Certainty == CertaintyLevel.Conservative);
    }

    [Fact]
    public void TwoValidRegistrationsForTheSamePairAdmitExactlyOneRootWithUnionedEvidenceDeterministically()
    {
        var forward = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateServiceOperationRequestWithTwoRegistrations(reversed: false));
        var reversed = ScenarioGraphBuilder.Build(ScenarioTestFactory.CreateServiceOperationRequestWithTwoRegistrations(reversed: true));

        var forwardGraph = Assert.Single(forward.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        var reversedGraph = Assert.Single(reversed.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);
        var forwardAction = Assert.Single(forwardGraph.Nodes, node => node.Kind == ScenarioNodeKind.Action);
        var reversedAction = Assert.Single(reversedGraph.Nodes, node => node.Kind == ScenarioNodeKind.Action);

        // Exactly one root, not one per endpoint.
        Assert.Single(forward.Graphs, item => item.EntryPoint == ScenarioTestFactory.ServiceOperationEntryPoint);

        // Both registrations' evidence is unioned into the single admitted root.
        Assert.Contains(forwardAction.Evidence, item => item.Artifact == "service-endpoint-registration");
        Assert.Contains(forwardAction.Evidence, item => item.Artifact == "service-endpoint-registration-second");

        // Reversing the registration input order never changes the admitted identities, evidence, or certainty.
        Assert.Equal(forwardGraph.Nodes.Select(node => node.Id.Value), reversedGraph.Nodes.Select(node => node.Id.Value));
        Assert.Equal(
            forwardAction.Evidence.Select(item => item.Id.Value).OrderBy(id => id, StringComparer.Ordinal),
            reversedAction.Evidence.Select(item => item.Id.Value).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(forwardAction.Certainty, reversedAction.Certainty);
    }
}
