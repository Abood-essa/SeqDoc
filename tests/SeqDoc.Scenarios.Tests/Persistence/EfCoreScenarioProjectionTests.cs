using System.Collections.Immutable;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Scenarios.Tests.Persistence;

public sealed class EfCoreScenarioProjectionTests
{
    private static EvidenceRef CreateEvidence(string symbol)
        => new(
            new EvidenceId($"evidence:v1:{symbol}"),
            EvidenceKind.Source,
            "src/Services/GadgetService.cs",
            range: null,
            symbol: symbol,
            detail: null,
            CertaintyLevel.Exact);

    [Fact]
    public void ScenarioGraphBuilderProjectsEntityQueryWithExactPresentationAndEdge()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var result = ScenarioGraphBuilder.Build(baseRequest);
        var graph = Assert.Single(result.Graphs);

        var queryNode = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.NotNull(queryNode.Presentation);
        Assert.Equal("GetMeaning.Data.GadgetDbContext", queryNode.Presentation.DbContextTypeName);
        Assert.Equal("GetMeaning.Models.Gadget", queryNode.Presentation.EntityTypeName);
        Assert.Equal(EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync, queryNode.Presentation.QueryOperatorKind);
        Assert.Equal(CertaintyLevel.Exact, queryNode.Certainty);

        var queryEdge = Assert.Single(graph.Edges, edge => edge.Target == queryNode.Id);
        Assert.Equal(ScenarioEdgeKind.Query, queryEdge.Kind);
        Assert.Equal(CertaintyLevel.Exact, queryEdge.Certainty);
    }

    [Fact]
    public void ScenarioGraphBuilderProjectsMutationsAndSaveWithDistinctEdgeKinds()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();

        var mutations = ImmutableArray.Create(
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:1"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:add"),
                MutationKind = EntityFrameworkMutationKind.Add,
                SequenceOrdinal = 1,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = "GetMeaning.Models.Gadget",
                Evidence = [CreateEvidence("DbSet.Add")],
                Certainty = CertaintyLevel.Exact,
            },
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:2"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:remove"),
                MutationKind = EntityFrameworkMutationKind.RemoveRange,
                SequenceOrdinal = 2,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = "GetMeaning.Models.Gadget",
                Evidence = [CreateEvidence("DbSet.RemoveRange")],
                Certainty = CertaintyLevel.Exact,
            },
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:3"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:clear"),
                MutationKind = EntityFrameworkMutationKind.Clear,
                SequenceOrdinal = 3,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = "GetMeaning.Models.Gadget",
                Evidence = [CreateEvidence("DbSet.Clear")],
                Certainty = CertaintyLevel.Exact,
            },
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:4"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:save-async"),
                MutationKind = EntityFrameworkMutationKind.SaveChangesAsync,
                SequenceOrdinal = 4,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = string.Empty,
                Evidence = [CreateEvidence("DbContext.SaveChangesAsync")],
                Certainty = CertaintyLevel.Exact,
            },
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:5"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:save-sync"),
                MutationKind = EntityFrameworkMutationKind.SaveChanges,
                SequenceOrdinal = 5,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = string.Empty,
                Evidence = [CreateEvidence("DbContext.SaveChanges")],
                Certainty = CertaintyLevel.Exact,
            });

        var nonGetFacts = baseRequest.NonGetSemanticFacts with
        {
            EntityFrameworkMutations = mutations,
        };

        var request = baseRequest with { NonGetSemanticFacts = nonGetFacts };
        var result = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(result.Graphs);

        var mutationNodes = graph.Nodes
            .Where(node => node.Kind == ScenarioNodeKind.EntityMutation)
            .OrderBy(node => node.SequenceOrdinal)
            .ToArray();
        Assert.Equal(5, mutationNodes.Length);

        // Add
        Assert.Equal(EntityFrameworkMutationKind.Add, mutationNodes[0].Presentation?.MutationKind);
        Assert.Equal("adds Gadget", mutationNodes[0].Detail);
        var addEdge = Assert.Single(graph.Edges, edge => edge.Target == mutationNodes[0].Id);
        Assert.Equal(ScenarioEdgeKind.Mutation, addEdge.Kind);

        // RemoveRange
        Assert.Equal(EntityFrameworkMutationKind.RemoveRange, mutationNodes[1].Presentation?.MutationKind);
        Assert.Equal("removes Gadget records", mutationNodes[1].Detail);
        var removeEdge = Assert.Single(graph.Edges, edge => edge.Target == mutationNodes[1].Id);
        Assert.Equal(ScenarioEdgeKind.Mutation, removeEdge.Kind);

        // Clear
        Assert.Equal(EntityFrameworkMutationKind.Clear, mutationNodes[2].Presentation?.MutationKind);
        Assert.Equal("clears the tracked Gadget set", mutationNodes[2].Detail);
        var clearEdge = Assert.Single(graph.Edges, edge => edge.Target == mutationNodes[2].Id);
        Assert.Equal(ScenarioEdgeKind.Mutation, clearEdge.Kind);

        // SaveChangesAsync -> Save edge
        Assert.Equal(EntityFrameworkMutationKind.SaveChangesAsync, mutationNodes[3].Presentation?.MutationKind);
        Assert.Equal("saves changes to GadgetDbContext", mutationNodes[3].Detail);
        var saveAsyncEdge = Assert.Single(graph.Edges, edge => edge.Target == mutationNodes[3].Id);
        Assert.Equal(ScenarioEdgeKind.Save, saveAsyncEdge.Kind);

        // SaveChanges -> Save edge
        Assert.Equal(EntityFrameworkMutationKind.SaveChanges, mutationNodes[4].Presentation?.MutationKind);
        Assert.Equal("saves changes to GadgetDbContext", mutationNodes[4].Detail);
        var saveSyncEdge = Assert.Single(graph.Edges, edge => edge.Target == mutationNodes[4].Id);
        Assert.Equal(ScenarioEdgeKind.Save, saveSyncEdge.Kind);
    }

    [Fact]
    public void ScenarioGraphBuilderProjectsStateAssignmentWithExactValueAndEdge()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();

        var stateAssignments = ImmutableArray.Create(
            new StateAssignmentSemanticFact(
                new SemanticFactId("state-assignment:1"),
                ScenarioTestFactory.ServiceMethod,
                new OperationId("op:assign:1"),
                "GetMeaning.Models.Gadget.Status",
                "GetMeaning.Models.GadgetStatus",
                StateAssignmentValueKind.EnumConstant,
                "Cancelled",
                [CreateEvidence("Gadget.Status = GadgetStatus.Cancelled")],
                CertaintyLevel.Exact,
                sequenceOrdinal: 2));

        var nonGetFacts = baseRequest.NonGetSemanticFacts with
        {
            StateAssignments = stateAssignments,
        };

        var request = baseRequest with { NonGetSemanticFacts = nonGetFacts };
        var result = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(result.Graphs);

        var stateNode = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.StateAssignment);
        Assert.Equal("Status = Cancelled", stateNode.Detail);
        Assert.Equal(CertaintyLevel.Exact, stateNode.Certainty);

        var stateEdge = Assert.Single(graph.Edges, edge => edge.Target == stateNode.Id);
        Assert.Equal(ScenarioEdgeKind.StateAssignment, stateEdge.Kind);
        Assert.Equal(CertaintyLevel.Exact, stateEdge.Certainty);
    }

    [Fact]
    public void ScenarioGraphBuilderOrderingIsDeterministic()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest(statusSwitchFlow: true);
        var reversedFacts = baseRequest.NonGetSemanticFacts with
        {
            EntityFrameworkMutations = baseRequest.NonGetSemanticFacts.EntityFrameworkMutations.Reverse().ToImmutableArray(),
            EfOperationSequence = baseRequest.NonGetSemanticFacts.EfOperationSequence.Reverse().ToImmutableArray(),
        };

        var forward = ScenarioGraphBuilder.Build(baseRequest);
        var reversed = ScenarioGraphBuilder.Build(baseRequest with { NonGetSemanticFacts = reversedFacts });

        Assert.Equal(forward.DebugProjection, reversed.DebugProjection);
        Assert.Equal(
            forward.Graphs[0].Nodes.Select(node => node.Id.Value),
            reversed.Graphs[0].Nodes.Select(node => node.Id.Value));
    }

    [Fact]
    public void ScenarioGraphBuilderInterleavesMutationsAndQueriesInSourceOrder()
    {
        var request = ScenarioTestFactory.CreateInterleavedSourceOrderRequest();
        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);

        var persistenceNodes = graph.Nodes
            .Where(node => node.Kind is ScenarioNodeKind.EntityQuery or ScenarioNodeKind.EntityMutation)
            .OrderBy(node => node.SequenceOrdinal)
            .ToArray();

        Assert.Equal(3, persistenceNodes.Length);
        Assert.Equal(ScenarioNodeKind.EntityMutation, persistenceNodes[0].Kind);
        Assert.Equal(EntityFrameworkMutationKind.Add, persistenceNodes[0].Presentation?.MutationKind);

        Assert.Equal(ScenarioNodeKind.EntityQuery, persistenceNodes[1].Kind);
        Assert.Equal(EntityFrameworkQueryOperatorKind.CountAsync, persistenceNodes[1].Presentation?.QueryOperatorKind);

        Assert.Equal(ScenarioNodeKind.EntityMutation, persistenceNodes[2].Kind);
        Assert.Equal(EntityFrameworkMutationKind.SaveChangesAsync, persistenceNodes[2].Presentation?.MutationKind);
    }

    [Theory]
    [InlineData("missing-anchor", "SC011")]
    [InlineData("duplicate-agreeing-anchor", "SC011")]
    [InlineData("dual-polarity-conflict", "SC012")]
    [InlineData("unsupported-placement", "SC013")]
    public void PersistenceBoundaryAmbiguityWithholdsOnlyAffectedClaimAndKeepsSibling(
        string partition, string diagnosticCode)
    {
        var request = partition switch
        {
            "missing-anchor" => ScenarioTestFactory.CreateMissingAnchorTopologyRequest(),
            "duplicate-agreeing-anchor" => ScenarioTestFactory.CreateDuplicateAnchorTopologyRequest(agreeing: true),
            "dual-polarity-conflict" => ScenarioTestFactory.CreateDualPolarityConflictRequest(),
            "unsupported-placement" => ScenarioTestFactory.CreateUnsupportedTopologyRequest(),
            _ => throw new ArgumentOutOfRangeException(nameof(partition), partition, null),
        };
        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);
        var query = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        var save = Assert.Single(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityMutation
            && node.Presentation?.MutationKind == EntityFrameworkMutationKind.SaveChangesAsync);
        var affected = partition is "missing-anchor" or "duplicate-agreeing-anchor" ? query : save;
        var sibling = partition is "missing-anchor" or "duplicate-agreeing-anchor" ? save : query;

        Assert.Contains(graph.Diagnostics, item => item.Code == diagnosticCode);
        Assert.Contains(graph.Nodes, item => item.Id == sibling.Id);
        Assert.DoesNotContain(graph.Topology.Memberships, item => item.ScenarioNode == affected.Id);

        var plan = SeqDoc.Application.Documentation.DocumentationPlanner.Plan(graph);
        var affectedLabel = affected == save ? "calls SaveChanges" : "Find at most one Gadget";
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label == affectedLabel);
    }

    [Fact]
    public void ForeignProfileIdProducesNoPersistenceNodes()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var mutations = ImmutableArray.Create(
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:1"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:add"),
                MutationKind = EntityFrameworkMutationKind.Add,
                SequenceOrdinal = 1,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = "GetMeaning.Models.Gadget",
                Evidence = [CreateEvidence("DbSet.Add")],
                Certainty = CertaintyLevel.Exact,
            });

        var foreignFacts = baseRequest.NonGetSemanticFacts with
        {
            Profile = CompilationProfile.Create("foreign/path", "Release", "net10.0"),
            EntityFrameworkMutations = mutations,
        };

        var request = baseRequest with { NonGetSemanticFacts = foreignFacts };
        var result = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(result.Graphs);

        Assert.Empty(graph.Nodes.Where(n => n.Kind is ScenarioNodeKind.EntityMutation or ScenarioNodeKind.StateAssignment));
        Assert.Contains(graph.Nodes, n => n.Kind == ScenarioNodeKind.EntityQuery);
    }

    [Fact]
    public void ForeignProgramIndexFingerprintProducesNoPersistenceNodes()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var mutations = ImmutableArray.Create(
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:1"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:add"),
                MutationKind = EntityFrameworkMutationKind.Add,
                SequenceOrdinal = 1,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = "GetMeaning.Models.Gadget",
                Evidence = [CreateEvidence("DbSet.Add")],
                Certainty = CertaintyLevel.Exact,
            });

        var foreignFacts = baseRequest.NonGetSemanticFacts with
        {
            ProgramIndexFingerprint = "foreign-program-index-fingerprint",
            EntityFrameworkMutations = mutations,
        };

        var request = baseRequest with { NonGetSemanticFacts = foreignFacts };
        var result = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(result.Graphs);

        Assert.Empty(graph.Nodes.Where(n => n.Kind is ScenarioNodeKind.EntityMutation or ScenarioNodeKind.StateAssignment));
        Assert.Contains(graph.Nodes, n => n.Kind == ScenarioNodeKind.EntityQuery);
    }

    [Fact]
    public void ForeignFrameworkFactsAreWithheldWhileValidSiblingFactsRemainDeterministic()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var mutation = new EntityFrameworkMutationFact
        {
            Id = new BehaviorFactId("ef-mut:sibling"),
            Method = ScenarioTestFactory.ServiceMethod,
            Operation = new OperationId("op:mut:sibling"),
            MutationKind = EntityFrameworkMutationKind.SaveChanges,
            SequenceOrdinal = 1,
            DbContextType = "GetMeaning.Data.GadgetDbContext",
            EntityType = string.Empty,
            Evidence = [CreateEvidence("DbContext.SaveChanges")],
            Certainty = CertaintyLevel.Exact,
        };
        var valid = baseRequest with
        {
            NonGetSemanticFacts = baseRequest.NonGetSemanticFacts with
            {
                EntityFrameworkMutations = [mutation],
            },
        };
        var foreign = valid with
        {
            FrameworkFacts = valid.FrameworkFacts with { ProfileId = ScenarioTestFactory.ForeignProfile.Id },
        };

        var forward = ScenarioGraphBuilder.Build(foreign);
        var reversed = ScenarioGraphBuilder.Build(foreign with
        {
            NonGetSemanticFacts = foreign.NonGetSemanticFacts with
            {
                EntityFrameworkMutations = foreign.NonGetSemanticFacts.EntityFrameworkMutations.Reverse().ToImmutableArray(),
            },
        });
        var forwardGraph = Assert.Single(forward.Graphs);
        Assert.Single(forwardGraph.Nodes, node => node.Kind == ScenarioNodeKind.EntityMutation);
        Assert.DoesNotContain(forwardGraph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.Equal(forward.DebugProjection, reversed.DebugProjection);
    }

    [Theory]
    [InlineData("framework-missing-profile")]
    [InlineData("framework-foreign-profile")]
    [InlineData("framework-missing-fingerprint")]
    [InlineData("framework-foreign-fingerprint")]
    [InlineData("nonget-missing-profile")]
    [InlineData("nonget-foreign-profile")]
    [InlineData("nonget-missing-fingerprint")]
    [InlineData("nonget-foreign-fingerprint")]
    public void MissingAndForeignPersistenceIdentityWithholdsOnlyUnboundFacts(string partition)
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var mutation = new EntityFrameworkMutationFact
        {
            Id = new BehaviorFactId("ef-mut:partition"),
            Method = ScenarioTestFactory.ServiceMethod,
            Operation = new OperationId("op:mut:partition"),
            MutationKind = EntityFrameworkMutationKind.SaveChanges,
            SequenceOrdinal = 1,
            DbContextType = "GetMeaning.Data.GadgetDbContext",
            EntityType = string.Empty,
            Evidence = [CreateEvidence("DbContext.SaveChanges")],
            Certainty = CertaintyLevel.Exact,
        };
        var observation = new SourceObservationSemanticFact(
            new SemanticFactId("observation:partition"),
            ScenarioTestFactory.ServiceMethod,
            new OperationId("op:observation:partition"),
            SourceObservationKind.Note,
            "EF boundary",
            [CreateEvidence("raw-sql")],
            CertaintyLevel.Exact);
        var nonGet = baseRequest.NonGetSemanticFacts with
        {
            EntityFrameworkMutations = [mutation],
            SourceObservations = [observation],
        };
        var request = baseRequest with { NonGetSemanticFacts = nonGet };
        request = partition switch
        {
            "framework-missing-profile" => request with { FrameworkFacts = request.FrameworkFacts with { ProfileId = null } },
            "framework-foreign-profile" => request with { FrameworkFacts = request.FrameworkFacts with { ProfileId = ScenarioTestFactory.ForeignProfile.Id } },
            "framework-missing-fingerprint" => request with { FrameworkFacts = request.FrameworkFacts with { ProgramIndexFingerprint = null } },
            "framework-foreign-fingerprint" => request with { FrameworkFacts = request.FrameworkFacts with { ProgramIndexFingerprint = "foreign" } },
            "nonget-missing-profile" => request with { NonGetSemanticFacts = nonGet with { Profile = null! } },
            "nonget-foreign-profile" => request with { NonGetSemanticFacts = nonGet with { Profile = ScenarioTestFactory.ForeignProfile } },
            "nonget-missing-fingerprint" => request with { NonGetSemanticFacts = nonGet with { ProgramIndexFingerprint = "" } },
            "nonget-foreign-fingerprint" => request with { NonGetSemanticFacts = nonGet with { ProgramIndexFingerprint = "foreign" } },
            _ => throw new ArgumentOutOfRangeException(nameof(partition), partition, null),
        };

        var forward = ScenarioGraphBuilder.Build(request);
        var reversed = ScenarioGraphBuilder.Build(request with
        {
            NonGetSemanticFacts = request.NonGetSemanticFacts with
            {
                EntityFrameworkMutations = request.NonGetSemanticFacts.EntityFrameworkMutations.Reverse().ToImmutableArray(),
                SourceObservations = request.NonGetSemanticFacts.SourceObservations.Reverse().ToImmutableArray(),
            },
        });
        var graph = Assert.Single(forward.Graphs);
        var reversedGraph = Assert.Single(reversed.Graphs);
        Assert.Equal(forward.DebugProjection, reversed.DebugProjection);
        if (partition.StartsWith("framework", StringComparison.Ordinal))
        {
            Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityMutation);
            Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        }
        else
        {
            Assert.Contains(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
            Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.EntityMutation);
            Assert.DoesNotContain(graph.Nodes, node => node.Kind == ScenarioNodeKind.SourceObservation);
        }
    }

    [Fact]
    public void DtoStateAssignmentsDoNotClaimPersistenceInDocumentation()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();

        var stateAssignments = ImmutableArray.Create(
            new StateAssignmentSemanticFact(
                new SemanticFactId("state-assignment:dto"),
                ScenarioTestFactory.ServiceMethod,
                new OperationId("op:assign:dto"),
                "GetMeaning.Models.GadgetDto.Label",
                "System.String",
                StateAssignmentValueKind.Literal,
                "UpdatedLabel",
                [CreateEvidence("GadgetDto.Label = 'UpdatedLabel'")],
                CertaintyLevel.Exact,
                sequenceOrdinal: 1));

        var request = baseRequest with
        {
            NonGetSemanticFacts = baseRequest.NonGetSemanticFacts with
            {
                StateAssignments = stateAssignments,
            },
        };

        var graph = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs);
        var plan = SeqDoc.Application.Documentation.DocumentationPlanner.Plan(graph);

        // Verify wording phrase contains assigns and never mentions persistence words
        var phrase = Assert.Single(plan.Wording.Phrases, p => p.Key == "state-assignment");
        Assert.StartsWith("The service assigns: ", phrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("saves", phrase.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("persists", phrase.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commits", phrase.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", phrase.Text, StringComparison.OrdinalIgnoreCase);

        // State assignment is a non-interaction fact and produces no diagram message
        Assert.DoesNotContain(plan.Diagram.Messages, m => m.Label.Contains("UpdatedLabel", StringComparison.Ordinal));
    }

    [Fact]
    public void GuardedPersistenceOperationsPreserveArmMembershipAndDiagramFragmentPlacement()
    {
        // Build a decision-guarded request where the service contains a decision and guarded mutations
        var baseRequest = ScenarioTestFactory.CreateGetRequest(decisionGuarded: true);

        // Add a save mutation that shares the operation ID of the guarded call
        var guardedMutations = ImmutableArray.Create(
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:guarded-save"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = ScenarioTestFactory.ServiceCallOperation,
                MutationKind = EntityFrameworkMutationKind.SaveChanges,
                SequenceOrdinal = 1,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = string.Empty,
                Evidence = [CreateEvidence("DbContext.SaveChanges")],
                Certainty = CertaintyLevel.Exact,
            });

        var request = baseRequest with
        {
            NonGetSemanticFacts = baseRequest.NonGetSemanticFacts with
            {
                EntityFrameworkMutations = guardedMutations,
            },
        };

        var result = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(result.Graphs);

        // Assert topology decisions and arms exist
        Assert.NotEmpty(graph.Topology.Decisions);
        Assert.NotEmpty(graph.Topology.Arms);

        // Verify save mutation exists in the scenario graph and has proven arm membership
        var saveNode = Assert.Single(graph.Nodes, n => n.Kind == ScenarioNodeKind.EntityMutation && n.Presentation?.MutationKind == EntityFrameworkMutationKind.SaveChanges);
        Assert.Contains(graph.Topology.Memberships, m => m.ScenarioNode == saveNode.Id);

        // Verify that the Save node is projected as ScenarioEdgeKind.Save
        var saveEdge = Assert.Single(graph.Edges, e => e.Target == saveNode.Id);
        Assert.Equal(ScenarioEdgeKind.Save, saveEdge.Kind);
        Assert.Equal("calls SaveChanges", saveEdge.Detail);

        // Verify that unrenderable/predicate-free guarded decisions fail closed: the message is withheld with DP002
        // rather than leaking as an unconditional top-level diagram message
        var plan = SeqDoc.Application.Documentation.DocumentationPlanner.Plan(graph);
        Assert.Contains(plan.Diagram.Diagnostics, d => d.Code == "DP002");
        Assert.DoesNotContain(plan.Diagram.Messages, m => m.Label == "calls SaveChanges");

        // When exact owner wording is supplied, verify diagram renders the save message inside the fragment arm
        var wordedGraph = ScenarioTestFactory.WithExactOwnerWording(graph);
        var wordedPlan = SeqDoc.Application.Documentation.DocumentationPlanner.Plan(wordedGraph);
        Assert.DoesNotContain(wordedPlan.Diagram.Diagnostics, d => d.Code == "DP002");
        Assert.NotEmpty(wordedPlan.Diagram.Sequence.Fragments);
    }

    [Fact]
    public void ForeignFrameworkFactsProfileIdProducesNoEntityQueryNodes()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var foreignFrameworkFacts = baseRequest.FrameworkFacts with
        {
            ProfileId = ScenarioTestFactory.ForeignProfile.Id,
        };

        var request = baseRequest with { FrameworkFacts = foreignFrameworkFacts };
        var result = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(result.Graphs);

        Assert.Empty(graph.Nodes.Where(n => n.Kind == ScenarioNodeKind.EntityQuery));
    }

    [Fact]
    public void ForeignFrameworkFactsProgramIndexFingerprintProducesNoEntityQueryNodes()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var foreignFrameworkFacts = baseRequest.FrameworkFacts with
        {
            ProgramIndexFingerprint = "foreign-framework-fingerprint",
        };

        var request = baseRequest with { FrameworkFacts = foreignFrameworkFacts };
        var result = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(result.Graphs);

        Assert.Empty(graph.Nodes.Where(n => n.Kind == ScenarioNodeKind.EntityQuery));
    }

    [Fact]
    public void ScaffoldLikeMultiEntityContextScenarioProjectionProducesDistinctNodesAndEdges()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();

        var multiEntityMutations = ImmutableArray.Create(
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:multi-1"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:gadget"),
                MutationKind = EntityFrameworkMutationKind.Add,
                SequenceOrdinal = 1,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = "GetMeaning.Models.Gadget",
                Evidence = [CreateEvidence("DbSet<Gadget>.Add")],
                Certainty = CertaintyLevel.Exact,
            },
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:multi-2"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:category"),
                MutationKind = EntityFrameworkMutationKind.RemoveRange,
                SequenceOrdinal = 2,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = "GetMeaning.Models.Category",
                Evidence = [CreateEvidence("DbSet<Category>.RemoveRange")],
                Certainty = CertaintyLevel.Exact,
            },
            new EntityFrameworkMutationFact
            {
                Id = new BehaviorFactId("ef-mut:multi-3"),
                Method = ScenarioTestFactory.ServiceMethod,
                Operation = new OperationId("op:mut:save"),
                MutationKind = EntityFrameworkMutationKind.SaveChanges,
                SequenceOrdinal = 3,
                DbContextType = "GetMeaning.Data.GadgetDbContext",
                EntityType = string.Empty,
                Evidence = [CreateEvidence("DbContext.SaveChanges")],
                Certainty = CertaintyLevel.Exact,
            });

        var request = baseRequest with
        {
            NonGetSemanticFacts = baseRequest.NonGetSemanticFacts with
            {
                EntityFrameworkMutations = multiEntityMutations,
            },
        };

        var result = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(result.Graphs);

        var gadgetNode = Assert.Single(graph.Nodes, n => n.Kind == ScenarioNodeKind.EntityMutation && n.Presentation?.EntityTypeName == "GetMeaning.Models.Gadget");
        Assert.Equal("adds Gadget", gadgetNode.Detail);

        var categoryNode = Assert.Single(graph.Nodes, n => n.Kind == ScenarioNodeKind.EntityMutation && n.Presentation?.EntityTypeName == "GetMeaning.Models.Category");
        Assert.Equal("removes Category records", categoryNode.Detail);

        var saveNode = Assert.Single(graph.Nodes, n => n.Kind == ScenarioNodeKind.EntityMutation && n.Presentation?.MutationKind == EntityFrameworkMutationKind.SaveChanges);
        Assert.Equal("saves changes to GadgetDbContext", saveNode.Detail);

        // Verify Diagram Plan reflects distinct mutations and save
        var plan = SeqDoc.Application.Documentation.DocumentationPlanner.Plan(graph);
        Assert.Contains(plan.Diagram.Messages, m => m.Label == "calls SaveChanges");
    }
}
