using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

public sealed class EdmxScenarioProjectionTests
{
    [Fact]
    public void EdmxIsAnIndependentNeutralObservationAndForeignProfileIsWithheld()
    {
        var request = ScenarioTestFactory.CreateConfiguredRootRequest(includeFrameworkRoot: true);
        var rootMethod = request.ConfiguredRoots.Last();
        var owningMethod = Assert.Single(request.ProgramIndex.Methods, method => method.Id == rootMethod);
        var owningProject = Assert.Single(request.ProgramIndex.Types, type => type.Id == owningMethod.ContainingType).Project;
        var fact = new EntityFrameworkEdmxMetadataFact
        {
            Id = new BehaviorFactId("edmx:one"),
            Project = owningProject,
            RepositoryRelativePath = "Data/Model.edmx",
            ContentFingerprint = "sha256:fingerprint",
            HasFunctionImport = true,
            HasStoreFunction = true,
            Evidence = [new EvidenceRef(new EvidenceId("evidence:edmx"), EvidenceKind.Source, "Data/Model.edmx", null, null, null, CertaintyLevel.Exact)],
            Certainty = CertaintyLevel.Exact,
        };
        Assert.Equal(owningProject, fact.Project);
        var valid = request with
        {
            FrameworkFacts = request.FrameworkFacts with
            {
                Facts = [fact],
                ProfileId = request.Profile.Id,
                ProgramIndexFingerprint = request.ProgramIndex.IndexFingerprint,
            },
        };
        var graph = Assert.Single(SeqDoc.Analysis.Scenarios.ScenarioGraphBuilder.Build(valid).Graphs,
            candidate => candidate.RootMethod == rootMethod);
        var node = Assert.Single(graph.Nodes, candidate => candidate.Kind == ScenarioNodeKind.SourceObservation && candidate.Detail.Contains("EDMX metadata boundary", StringComparison.Ordinal));
        Assert.Contains(graph.Edges, edge => edge.Target == node.Id && edge.Kind == ScenarioEdgeKind.Observation);
        Assert.Contains("runtime behavior are not inferred", node.Detail, StringComparison.Ordinal);

        var repeated = Assert.Single(SeqDoc.Analysis.Scenarios.ScenarioGraphBuilder.Build(valid).Graphs,
            candidate => candidate.RootMethod == rootMethod);
        Assert.Equal(graph.DebugProjection, repeated.DebugProjection);
        var firstPlan = SeqDoc.Application.Documentation.DocumentationPlanner.Plan(graph);
        var repeatedPlan = SeqDoc.Application.Documentation.DocumentationPlanner.Plan(repeated);
        Assert.Equal(firstPlan.Diagram.Messages.Select(message => message.Id), repeatedPlan.Diagram.Messages.Select(message => message.Id));
        Assert.Equal(firstPlan.Wording.DebugProjection, repeatedPlan.Wording.DebugProjection);

        var foreign = valid with { FrameworkFacts = valid.FrameworkFacts with { ProfileId = ScenarioTestFactory.ForeignProfile.Id } };
        var foreignGraph = Assert.Single(SeqDoc.Analysis.Scenarios.ScenarioGraphBuilder.Build(foreign).Graphs,
            candidate => candidate.RootMethod == rootMethod);
        Assert.DoesNotContain(foreignGraph.Nodes, candidate => candidate.Detail.Contains("EDMX metadata boundary", StringComparison.Ordinal));

        var foreignProject = valid with
        {
            FrameworkFacts = valid.FrameworkFacts with
            {
                Facts = [fact with { Project = new ProjectId("project:foreign") }],
            },
        };
        var foreignProjectGraph = Assert.Single(SeqDoc.Analysis.Scenarios.ScenarioGraphBuilder.Build(foreignProject).Graphs,
            candidate => candidate.RootMethod == rootMethod);
        Assert.DoesNotContain(foreignProjectGraph.Nodes, candidate => candidate.Detail.Contains("EDMX metadata boundary", StringComparison.Ordinal));

        var foreignSnapshot = valid with
        {
            FrameworkFacts = valid.FrameworkFacts with
            {
                ProgramIndexFingerprint = "foreign-program-index-fingerprint",
            },
        };
        var foreignSnapshotGraph = Assert.Single(SeqDoc.Analysis.Scenarios.ScenarioGraphBuilder.Build(foreignSnapshot).Graphs,
            candidate => candidate.RootMethod == rootMethod);
        Assert.DoesNotContain(foreignSnapshotGraph.Nodes, candidate => candidate.Detail.Contains("EDMX metadata boundary", StringComparison.Ordinal));
    }
}
