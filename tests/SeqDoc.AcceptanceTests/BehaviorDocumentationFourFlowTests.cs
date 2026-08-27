using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.Wording;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.FrameworkModels.EntityFramework;
using SeqDoc.Rendering.Markdown;
using Xunit;
using SeqDoc.Testing;

namespace SeqDoc.AcceptanceTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BehaviorDocumentationFourFlowGroup
{
    public const string Name = "Translation alpha FourFlow";
}

/// <summary>
/// Translation-alpha FourFlow acceptance. The generic unrelated FourFlows fixture proves every
/// admitted non-Get flow from the same compiler evidence as the accepted Get flow: exact status-switch
/// arms, distinct failure outcomes, authoritative ordered mutations/save, non-interaction source
/// observations, conservative relational/time facts, exact state assignments, ordered multi-query with
/// aggregation distinction, loop-backed collection mutation, the unique CreatedAtAction Get link, and
/// the Update inequality/order sequence. Claim coverage is consolidated into the fifteen assertions
/// below; the reproducible lane plans, renders, validates, and activates the complete four-flow
/// documentation set beneath a test-owned temporary root.
/// </summary>
[Collection(BehaviorDocumentationFourFlowGroup.Name)]
public sealed class BehaviorDocumentationFourFlowTests
{
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/FourFlows/FourFlows.csproj";
    private static string ExternalTicketReservationRoot => Path.Combine(
        ExternalCorpusResolver.Current.RequireGroup(ExternalCorpusGroup.Provided).Root, "TicketReservation-Solution");
    private const string ExternalTicketReservationTarget = "TicketReservation.Api/TicketReservation.Api.csproj";

    private static readonly string[] ExpectedOperationKeys =
    [
        "DELETE api/Widgets/{id}",
        "GET api/Widgets/{id}",
        "POST api/Widgets/{id}/reservations",
        "PUT api/Widgets/{id}",
    ];

    /// <summary>Claim 12: automatic four-flow discovery emits every admitted flow in canonical order.</summary>
    [Fact]
    public async Task FourFlowsAreDiscoveredAutomaticallyInCanonicalOrder()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var keys = bundle.Graphs.Graphs
            .Select(graph => graph.OperationKey)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedOperationKeys, keys);

        foreach (var graph in bundle.Graphs.Graphs)
        {
            Assert.NotEmpty(graph.Nodes);
            foreach (var node in graph.Nodes)
            {
                Assert.NotEmpty(node.Evidence);
                Assert.NotEqual(CertaintyLevel.Unknown, node.Certainty);
            }

            foreach (var edge in graph.Edges)
            {
                Assert.NotEmpty(edge.Evidence);
                Assert.NotEqual(CertaintyLevel.Unknown, edge.Certainty);
            }
        }

        Assert.DoesNotContain(FindRepositoryRoot(), bundle.Graphs.DebugProjection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Claims 1+2: Cancel joins exact switch arms to distinct failure outcomes unique by status.</summary>
    [Fact]
    public async Task CancelJoinsExactStatusSwitchArmsToDistinctOutcomesByStatus()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var cancel = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Delete);

        var arms = bundle.Extraction.NonGetSemanticFacts.StatusSwitchArms
            .Where(arm => arm.Method == cancel.RootMethod)
            .ToArray();
        Assert.Equal(3, arms.Length);
        Assert.Contains(arms, arm => arm.StatusMemberName == "NotFound" && arm.HelperKind == HttpOutcomeHelperKind.NotFound);
        Assert.Contains(arms, arm => arm.StatusMemberName == "Conflict" && arm.HelperKind == HttpOutcomeHelperKind.Conflict);
        Assert.Contains(arms, arm => arm.StatusMemberName == "default" && arm.HelperKind == HttpOutcomeHelperKind.Ok);
        Assert.All(arms, arm => Assert.Equal(CertaintyLevel.Exact, arm.Certainty));
        Assert.All(arms, arm => Assert.NotEmpty(arm.Evidence));

        var outcomes = cancel.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 404", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 409", StringComparison.Ordinal));
        Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Equal(3, outcomes.Select(node => node.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(cancel.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeFailure && edge.Detail.Contains("NotFound", StringComparison.Ordinal));
        Assert.Contains(cancel.Edges, edge => edge.Kind == ScenarioEdgeKind.OutcomeFailure && edge.Detail.Contains("Conflict", StringComparison.Ordinal));
        Assert.Contains(cancel.Edges, edge => edge.Kind == ScenarioEdgeKind.ResultStatus);
        Assert.DoesNotContain(cancel.Edges, edge => edge.Kind is ScenarioEdgeKind.ResultSuccess or ScenarioEdgeKind.ResultFailure);
    }

    /// <summary>Claims 3+4: Cancel orders authoritative mutations/save and keeps observations non-interaction.</summary>
    [Fact]
    public async Task CancelOrdersMutationsAndSaveAndKeepsObservationNonInteraction()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var cancel = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Delete);
        var serviceMethod = cancel.Nodes
            .Single(node => node.Kind == ScenarioNodeKind.ServiceCall)
            .Method!.Value;

        var mutationNodes = cancel.Nodes
            .Where(node => node.Kind == ScenarioNodeKind.EntityMutation)
            .Select(node => node.Detail)
            .ToArray();
        Assert.Equal(2, mutationNodes.Length);
        Assert.Contains(mutationNodes, detail => detail == "removes Widget records");
        Assert.Contains(mutationNodes, detail => detail == "saves changes to WidgetDbContext");

        var mutationFacts = bundle.Extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == serviceMethod)
            .OrderBy(fact => fact.SequenceOrdinal)
            .ToArray();
        Assert.Equal(
            new[] { EntityFrameworkMutationKind.RemoveRange, EntityFrameworkMutationKind.SaveChangesAsync },
            mutationFacts.Select(fact => fact.MutationKind));
        Assert.True(
            mutationFacts[0].SequenceOrdinal < mutationFacts[1].SequenceOrdinal,
            "RemoveRange must precede SaveChangesAsync in source order.");
        Assert.All(mutationFacts, fact => Assert.Equal(CertaintyLevel.Exact, fact.Certainty));
        Assert.All(mutationFacts, fact => Assert.NotEmpty(fact.Evidence));

        Assert.Contains(cancel.Edges, edge => edge.Kind == ScenarioEdgeKind.Mutation);
        Assert.Contains(cancel.Edges, edge => edge.Kind == ScenarioEdgeKind.Save);

        var observation = Assert.Single(cancel.Nodes, node => node.Kind == ScenarioNodeKind.SourceObservation);
        Assert.Contains("notify the warehouse", observation.Detail, StringComparison.Ordinal);
        Assert.Equal(CertaintyLevel.Conservative, observation.Certainty);
        var observationFact = Assert.Single(
            bundle.Extraction.NonGetSemanticFacts.SourceObservations,
            fact => fact.Kind == Core.Semantics.SourceObservationKind.Todo);
        Assert.Equal(CertaintyLevel.Conservative, observationFact.Certainty);
        Assert.Contains("TODO", observationFact.Text, StringComparison.OrdinalIgnoreCase);

        var plan = DocumentationPlanner.Plan(cancel);
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Key == "source-observation");
        Assert.DoesNotContain(
            plan.Diagram.Messages,
            message => message.Label.Contains("observation", StringComparison.OrdinalIgnoreCase)
                || message.Label.Contains("TODO", StringComparison.OrdinalIgnoreCase)
                || message.Label.Contains("notify", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Claim 5: Reserve carries conservative relational patterns and DateTime comparisons.</summary>
    [Fact]
    public async Task ReserveCarriesConservativeRelationalPatternAndTimeComparisonFacts()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var reserve = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var serviceMethod = reserve.Nodes
            .Single(node => node.Kind == ScenarioNodeKind.ServiceCall)
            .Method!.Value;
        var facts = bundle.Extraction.NonGetSemanticFacts.RelationalTimeFacts
            .Where(fact => fact.Method == serviceMethod)
            .ToArray();
        var pattern = Assert.Single(facts, fact => fact.Kind == Core.Semantics.RelationalTimeFactKind.RelationalPattern);
        Assert.Equal(ComparisonOperatorKind.LessThanOrEqual, pattern.Operator);
        Assert.Equal("0", pattern.ThresholdValue);
        var time = Assert.Single(facts, fact => fact.Kind == Core.Semantics.RelationalTimeFactKind.TimeComparison);
        Assert.Equal(ComparisonOperatorKind.LessThan, time.Operator);
        Assert.Equal(CertaintyLevel.Conservative, pattern.Certainty);
        Assert.Equal(CertaintyLevel.Conservative, time.Certainty);
        Assert.All(facts, fact => Assert.NotEmpty(fact.Evidence));
    }

    /// <summary>Claim 6: Reserve carries exact property and enum state assignments.</summary>
    [Fact]
    public async Task ReserveCarriesExactStateAssignments()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var reserve = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var serviceMethod = reserve.Nodes
            .Single(node => node.Kind == ScenarioNodeKind.ServiceCall)
            .Method!.Value;
        var assignments = bundle.Extraction.NonGetSemanticFacts.StateAssignments
            .Where(fact => fact.Method == serviceMethod)
            .ToArray();
        var statusAssignment = Assert.Single(
            assignments,
            fact => fact.TargetMember.EndsWith("Reservation.Status", StringComparison.Ordinal));
        Assert.Equal(Core.Semantics.StateAssignmentValueKind.EnumConstant, statusAssignment.ValueKind);
        Assert.Equal("Active", statusAssignment.Value);
        Assert.Equal(CertaintyLevel.Exact, statusAssignment.Certainty);
        Assert.Contains(assignments, fact => fact.TargetMember.EndsWith("Reservation.Quantity", StringComparison.Ordinal));

        var stateNodes = reserve.Nodes
            .Where(node => node.Kind == ScenarioNodeKind.StateAssignment)
            .Select(node => node.Detail)
            .ToArray();
        Assert.Contains(stateNodes, detail => detail.Contains("Status = Active", StringComparison.Ordinal));
        Assert.Contains(reserve.Edges, edge => edge.Kind == ScenarioEdgeKind.StateAssignment);
    }

    /// <summary>Claims 7+8: Reserve orders multiple aggregation queries and distinguishes CountAsync.</summary>
    [Fact]
    public async Task ReserveOrdersMultipleAggregationQueriesDistinctFromSingleLookup()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var reserve = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var serviceMethod = reserve.Nodes
            .Single(node => node.Kind == ScenarioNodeKind.ServiceCall)
            .Method!.Value;

        var queryNodes = reserve.Nodes
            .Where(node => node.Kind == ScenarioNodeKind.EntityQuery)
            .ToArray();
        Assert.Equal(3, queryNodes.Length);
        var countQueries = queryNodes.Where(node => node.Detail.Contains("CountAsync", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, countQueries.Length);
        foreach (var query in countQueries)
        {
            Assert.DoesNotContain("SingleOrDefaultAsync", query.Detail, StringComparison.Ordinal);
            Assert.Equal(CertaintyLevel.Exact, query.Certainty);
        }

        var lookup = Assert.Single(queryNodes, node => node.Detail.Contains("SingleOrDefaultAsync", StringComparison.Ordinal));
        Assert.DoesNotContain("CountAsync", lookup.Detail, StringComparison.Ordinal);
        Assert.Equal(CertaintyLevel.Exact, lookup.Certainty);

        // Source-order aggregation distinction comes from the collector sequence, never from graph
        // identity hashing.
        var queryOrder = bundle.Extraction.NonGetSemanticFacts.EfOperationSequence
            .Where(item => item.Method == serviceMethod && item.Kind == EfOperationSequenceKind.QueryTerminal)
            .ToDictionary(item => item.Operation.Value, item => item.Ordinal);
        var reservationQuery = Assert.Single(countQueries, node => node.Detail.Contains("Reservation", StringComparison.Ordinal));
        var partsQuery = Assert.Single(countQueries, node => node.Detail.Contains("Part", StringComparison.Ordinal));
        Assert.True(
            queryOrder[reservationQuery.Operation!.Value.Value] < queryOrder[partsQuery.Operation!.Value.Value],
            "The Reservations aggregation must precede the Parts aggregation in source order.");

        // Aggregation distinction against the accepted single-value lookup of the Get flow.
        var get = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Get);
        var getQuery = Assert.Single(get.Nodes, node => node.Kind == ScenarioNodeKind.EntityQuery);
        Assert.Contains("SingleOrDefaultAsync", getQuery.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("CountAsync", getQuery.Detail, StringComparison.Ordinal);

        // Markdown must render the lookup before the Reservations aggregation before the Parts
        // aggregation, matching compiler source order through the additive sequence ordinal. The
        // concise sentence-case labels come from typed presentation facts, never from detail parsing.
        var reservePlan = DocumentationPlanner.Plan(reserve);
        Assert.True(
            PhraseIndex(reservePlan.Wording, "entity-query", "finds at most one Widget")
                < PhraseIndex(reservePlan.Wording, "entity-query", "counts Reservations"),
            "Markdown must render the Widget lookup before the Reservations aggregation.");
        Assert.True(
            PhraseIndex(reservePlan.Wording, "entity-query", "counts Reservations")
                < PhraseIndex(reservePlan.Wording, "entity-query", "counts Parts"),
            "Markdown must render the Reservations aggregation before the Parts aggregation.");

        // The Mermaid lane fails closed for the guarded aggregations: their owning decisions carry
        // no exact predicate wording in this fixture, so they are withheld (DP002) rather than
        // rendered under a generic label. The ordering guarantee lives in the wording lane above;
        // the diagram keeps the unconditional lookup, ordered before the outcome.
        Assert.Contains(reservePlan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP002");
        Assert.DoesNotContain(
            reservePlan.Diagram.Messages,
            message => message.Label.Contains("Count", StringComparison.Ordinal));
        Assert.True(
            FirstMessageIndex(reservePlan.Diagram, "Find at most one Widget")
                < FirstMessageIndex(reservePlan.Diagram, "Return a status outcome"),
            "Mermaid must render the unconditional Widget lookup before the outcome.");
    }

    /// <summary>Claim 9: Reserve joins the loop-backed collection mutation exactly once.</summary>
    [Fact]
    public async Task ReserveJoinsLoopBackedCollectionMutationOnce()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var reserve = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var serviceMethod = reserve.Nodes
            .Single(node => node.Kind == ScenarioNodeKind.ServiceCall)
            .Method!.Value;

        var reserveFlow = bundle.Behavior.MethodFlows
            .Single(flow => flow.Method == serviceMethod);
        Assert.Contains(reserveFlow.Nodes, node => node.Kind == FlowNodeKind.Loop);

        // The graph must join the loop-backed Add mutation exactly once; node membership is asserted
        // before the fact-level filter so a mismatch is attributable.
        Assert.Single(
            reserve.Nodes,
            node => node.Kind == ScenarioNodeKind.EntityMutation
                && node.Detail.Contains("adds PartLink", StringComparison.Ordinal));

        var partLinkMutations = bundle.Extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == serviceMethod
                && fact.EntityType.EndsWith("PartLink", StringComparison.Ordinal))
            .ToArray();
        var allReserveMutations = bundle.Extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == serviceMethod)
            .ToArray();
        Assert.True(
            partLinkMutations.Length == 1,
            "ReserveAsync EF mutations: "
            + string.Join("; ", allReserveMutations.Select(fact => $"{fact.MutationKind}:{fact.EntityType}:{fact.SequenceOrdinal}")));
        var add = partLinkMutations[0];
        Assert.Equal(EntityFrameworkMutationKind.Add, add.MutationKind);
        Assert.Equal(CertaintyLevel.Exact, add.Certainty);
    }

    /// <summary>Claim 10: Reserve links CreatedAtAction to the unique Get entry point.</summary>
    [Fact]
    public async Task ReserveCreatedAtActionLinksToUniqueGetRoute()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var reserve = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var created = Assert.Single(
            reserve.Nodes,
            node => node.Kind == ScenarioNodeKind.Outcome
                && node.Detail.Contains("HTTP 201", StringComparison.Ordinal));
        Assert.Contains("links to GET api/Widgets/{id}", created.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(reserve.Diagnostics, diagnostic => diagnostic.Code == "SC010");
    }

    /// <summary>Claim 11: Update excludes identifiers by inequality and orders remove/clear/add/save.</summary>
    [Fact]
    public async Task UpdateExcludesIdentifierAndOrdersRemoveClearAddSave()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        var update = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Put);
        var serviceMethod = update.Nodes
            .Single(node => node.Kind == ScenarioNodeKind.ServiceCall)
            .Method!.Value;

        Assert.Contains(
            bundle.Extraction.SemanticFacts.Comparisons,
            fact => fact.Method == serviceMethod
                && fact.Operator == ComparisonOperatorKind.NotEqual);

        var mutationNodes = update.Nodes
            .Where(node => node.Kind == ScenarioNodeKind.EntityMutation)
            .Select(node => node.Detail)
            .ToArray();
        Assert.Equal(4, mutationNodes.Length);
        Assert.Contains(mutationNodes, detail => detail == "removes Part records");
        Assert.Contains(mutationNodes, detail => detail == "clears the tracked Part set");
        Assert.Contains(mutationNodes, detail => detail == "adds Widget");
        Assert.Contains(mutationNodes, detail => detail == "saves changes to WidgetDbContext");

        var mutationKinds = bundle.Extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == serviceMethod)
            .OrderBy(fact => fact.SequenceOrdinal)
            .Select(fact => fact.MutationKind)
            .ToArray();
        Assert.Equal(
            new[]
            {
                EntityFrameworkMutationKind.RemoveRange,
                EntityFrameworkMutationKind.Clear,
                EntityFrameworkMutationKind.Add,
                EntityFrameworkMutationKind.SaveChangesAsync,
            },
            mutationKinds);

        // Markdown must render the mutation/save phrases in compiler source order through the
        // additive sequence ordinal, and Mermaid must place the save after every distinct
        // kind-specific mutation message.
        var updatePlan = DocumentationPlanner.Plan(update);
        Assert.True(
            PhraseIndex(updatePlan.Wording, "entity-mutation", "removes Part records")
                < PhraseIndex(updatePlan.Wording, "entity-mutation", "clears the tracked Part set"),
            "Markdown must render RemoveRange before Clear.");
        Assert.True(
            PhraseIndex(updatePlan.Wording, "entity-mutation", "clears the tracked Part set")
                < PhraseIndex(updatePlan.Wording, "entity-mutation", "adds Widget"),
            "Markdown must render Clear before Add.");
        Assert.True(
            PhraseIndex(updatePlan.Wording, "entity-mutation", "adds Widget")
                < PhraseIndex(updatePlan.Wording, "entity-save", "calls SaveChanges"),
            "Markdown must render Add before the save.");
        foreach (string mutationLabel in new[] { "Remove Part range", "Clear tracked Parts", "Add Widget" })
        {
            Assert.True(
                LastMessageIndex(updatePlan.Diagram, mutationLabel) < FirstMessageIndex(updatePlan.Diagram, "calls SaveChanges"),
                $"Mermaid must render every mutation message ({mutationLabel}) before the save message.");
        }
    }

    /// <summary>Claim 15a: the unrelated fixture never shares TicketReservation vocabulary in facts or wording.</summary>
    [Fact]
    public async Task FourFlowFactsAndDocsRemainUnrelatedToTicketReservation()
    {
        var bundle = await BuildAsync(FindRepositoryRoot());
        Assert.DoesNotContain("TicketReservation", bundle.Extraction.NonGetSemanticFacts.DebugProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("Ticket", bundle.Extraction.NonGetSemanticFacts.DebugProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("ReservationService", bundle.Extraction.NonGetSemanticFacts.DebugProjection, StringComparison.Ordinal);

        foreach (var graph in bundle.Graphs.Graphs)
        {
            var plan = DocumentationPlanner.Plan(graph);
            string markdown = MarkdownRenderer.RenderDocument(plan.Wording, plan.Diagram);
            Assert.DoesNotContain("TicketReservation", markdown, StringComparison.Ordinal);
        }
    }

    /// <summary>Claim 15b: external TicketReservation acceptance produces all four evidence-backed flows.</summary>
    [Fact]
    public async Task TicketReservationAcceptanceProducesFourEvidenceBackedFlows()
    {
        var root = FindRepositoryRoot();
        var target = Path.Combine(ExternalTicketReservationRoot, ExternalTicketReservationTarget.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(target), target);

        var profile = CompilationProfile.Create(ExternalTicketReservationTarget, "Release", "net10.0");
        var bundle = await BuildAsync(ExternalTicketReservationRoot, target, profile);

        var graphs = bundle.Graphs.Graphs
            .OrderBy(graph => graph.OperationKey, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            graphs.Length == 4,
            "Expected four external flows; actual keys: " + string.Join("; ", graphs.Select(graph => graph.OperationKey)));
        var get = Assert.Single(graphs, graph => graph.HttpMethod == HttpMethodKind.Get);
        var cancel = Assert.Single(graphs, graph => graph.HttpMethod == HttpMethodKind.Delete);
        var reserve = Assert.Single(graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var update = Assert.Single(graphs, graph => graph.HttpMethod == HttpMethodKind.Put);

        var predicateFacts = bundle.Extraction.PredicateSemanticFacts;
        var serviceMethods = new[] { cancel, reserve, update }
            .Select(graph => graph.Nodes.Single(node => node.Kind == ScenarioNodeKind.ServiceCall).Method!.Value)
            .ToHashSet();
        var serviceFacts = predicateFacts.Predicates.Where(fact => serviceMethods.Contains(fact.Method)).ToArray();
        Assert.NotEmpty(serviceFacts);
        Assert.Contains(serviceFacts, fact => fact.Root.Kind == PredicateExpressionKind.Comparison
            && fact.Root.Children.Any(child => child.Kind == PredicateExpressionKind.NullConstant)
            && fact.Root.Children.Any(child => child.Kind == PredicateExpressionKind.SymbolValue));
        Assert.All(serviceFacts, fact =>
        {
            Assert.Equal(predicateFacts.Profile.Id, fact.ProfileId);
            Assert.Equal(predicateFacts.ProgramIndexFingerprint, fact.ProgramIndexFingerprint);
            Assert.NotEmpty(fact.Evidence);
        });
        var serviceFactIds = serviceFacts.Select(fact => fact.Id).ToHashSet();
        var serviceMappings = predicateFacts.Mappings.Where(mapping => serviceFactIds.Contains(mapping.PredicateId)).ToArray();
        Assert.NotEmpty(serviceMappings);
        Assert.All(serviceMappings, mapping =>
        {
            Assert.NotEmpty(mapping.LoweredConditionOperations);
            Assert.Equal(mapping.LoweredConditionOperations.Length, mapping.LoweredConditionOperations.Distinct().Count());
            Assert.Equal(predicateFacts.Profile.Id, mapping.ProfileId);
            Assert.Equal(predicateFacts.ProgramIndexFingerprint, mapping.ProgramIndexFingerprint);
            Assert.NotEmpty(mapping.Evidence);
        });

        // Every node and edge carries evidence and explicit certainty, and the debug projection is
        // canonical and path-free.
        foreach (var graph in graphs)
        {
            foreach (var node in graph.Nodes)
            {
                Assert.NotEmpty(node.Evidence);
                Assert.NotEqual(CertaintyLevel.Unknown, node.Certainty);
            }

            foreach (var edge in graph.Edges)
            {
                Assert.NotEmpty(edge.Evidence);
                Assert.NotEqual(CertaintyLevel.Unknown, edge.Certainty);
            }
        }

        Assert.DoesNotContain(ExternalTicketReservationRoot, bundle.Graphs.DebugProjection, StringComparison.OrdinalIgnoreCase);

        // Each non-Get action carries the three named status arms plus the default 500 arm, and the
        // graph joins the 404/409/400/500 outcomes distinct by status.
        foreach (var nonGet in new[] { cancel, reserve, update })
        {
            var arms = bundle.Extraction.NonGetSemanticFacts.StatusSwitchArms
                .Where(arm => arm.Method == nonGet.RootMethod)
                .ToArray();
            Assert.Contains(arms, arm => arm.StatusMemberName == "NotFound" && arm.HelperKind == HttpOutcomeHelperKind.NotFound);
            Assert.Contains(arms, arm => arm.StatusMemberName == "Conflict" && arm.HelperKind == HttpOutcomeHelperKind.Conflict);
            Assert.Contains(arms, arm => arm.StatusMemberName == "ValidationError" && arm.HelperKind == HttpOutcomeHelperKind.BadRequest);
            Assert.Contains(arms, arm => arm.StatusMemberName == "default" && arm.HelperKind == HttpOutcomeHelperKind.StatusCode);
            Assert.All(arms.Where(arm => arm.StatusMemberName != "success"), arm => Assert.Equal(CertaintyLevel.Exact, arm.Certainty));

            var outcomes = nonGet.Nodes.Where(node => node.Kind == ScenarioNodeKind.Outcome).ToArray();
            Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 404", StringComparison.Ordinal));
            Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 409", StringComparison.Ordinal));
            Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 400", StringComparison.Ordinal));
            Assert.Contains(outcomes, node => node.Detail.Contains("HTTP 500", StringComparison.Ordinal));
        }

        // Cancel: exact save mutation plus a conservative TODO observation that is never an interaction.
        var cancelService = cancel.Nodes.Single(node => node.Kind == ScenarioNodeKind.ServiceCall).Method!.Value;
        var cancelMutations = bundle.Extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == cancelService)
            .ToArray();
        Assert.Single(cancelMutations, fact => fact.MutationKind == EntityFrameworkMutationKind.SaveChangesAsync);
        var cancelTodo = Assert.Single(
            bundle.Extraction.NonGetSemanticFacts.SourceObservations,
            fact => fact.Method == cancelService && fact.Kind == Core.Semantics.SourceObservationKind.Todo);
        Assert.Equal(CertaintyLevel.Conservative, cancelTodo.Certainty);
        Assert.Contains("refund", cancelTodo.Text, StringComparison.OrdinalIgnoreCase);
        var cancelPlan = DocumentationPlanner.Plan(cancel);
        Assert.DoesNotContain(
            cancelPlan.Diagram.Messages,
            message => message.Label.Contains("refund", StringComparison.OrdinalIgnoreCase)
                || message.Label.Contains("TODO", StringComparison.OrdinalIgnoreCase));

        // Reserve: lookup plus a Where/SelectMany/CountAsync aggregation, a loop-backed Add mutation,
        // and the CreatedAtAction link to the unique Get entry point.
        var reserveService = reserve.Nodes.Single(node => node.Kind == ScenarioNodeKind.ServiceCall).Method!.Value;
        var reserveQueries = reserve.Nodes.Where(node => node.Kind == ScenarioNodeKind.EntityQuery).ToArray();
        Assert.Contains(reserveQueries, node => node.Detail.Contains("SingleOrDefaultAsync", StringComparison.Ordinal));
        var reservedTicketCount = Assert.Single(reserveQueries, node => node.Detail.Contains("CountAsync", StringComparison.Ordinal));
        var reservedTicketOperation = Assert.Single(
            bundle.Extraction.Operations,
            operation => operation.Id == reservedTicketCount.Operation);
        Assert.Equal("TicketReservation.Api.Models.Ticket", reservedTicketOperation.QueryChain!.EntityType);
        Assert.Single(
            bundle.Extraction.NonGetSemanticFacts.EfOperationSequence,
            sequence => sequence.Operation == reservedTicketCount.Operation
                && sequence.Method == reserveService
                && sequence.Kind == EfOperationSequenceKind.QueryTerminal);
        Assert.Contains(
            bundle.Extraction.NonGetSemanticFacts.EntityFrameworkMutations,
            fact => fact.Method == reserveService
                && fact.MutationKind == EntityFrameworkMutationKind.Add
                && fact.EntityType.EndsWith("Ticket", StringComparison.Ordinal));
        var created = Assert.Single(
            reserve.Nodes,
            node => node.Kind == ScenarioNodeKind.Outcome && node.Detail.Contains("HTTP 201", StringComparison.Ordinal));
        Assert.Contains("links to GET api/Reservations/{id:guid}", created.Detail, StringComparison.Ordinal);

        // Update: the Where exclusion and the ordered remove/clear/add/save mutation sequence.
        var updateService = update.Nodes.Single(node => node.Kind == ScenarioNodeKind.ServiceCall).Method!.Value;
        Assert.Contains(
            update.Nodes,
            node => node.Kind == ScenarioNodeKind.EntityQuery && node.Detail.Contains("Where", StringComparison.Ordinal));
        var updateKinds = bundle.Extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(fact => fact.Method == updateService)
            .OrderBy(fact => fact.SequenceOrdinal)
            .Select(fact => fact.MutationKind)
            .ToArray();
        Assert.Equal(
            new[]
            {
                EntityFrameworkMutationKind.RemoveRange,
                EntityFrameworkMutationKind.Clear,
                EntityFrameworkMutationKind.Add,
                EntityFrameworkMutationKind.SaveChangesAsync,
            },
            updateKinds);

        // CT-8/CT-11 retain the compiler facts but conservatively withhold these guarded external
        // interactions when their owning predicate cannot be placed exactly. The useful root,
        // service, and lookup facts remain; no mutation or save is invented as unconditional output.
        var updatePlan = DocumentationPlanner.Plan(update);
        Assert.Contains(updatePlan.Diagram.Messages, message => message.Label == "UpdateAsync");
        Assert.Contains(updatePlan.Diagram.Messages, message => message.Label == "Find at most one Reservation");
        Assert.DoesNotContain(updatePlan.Diagram.Messages, message => message.Label is "Remove Ticket range" or "Clear tracked Tickets" or "Add Ticket" or "calls SaveChanges");
        Assert.Contains(updatePlan.Wording.Phrases, phrase => phrase.Kind == WordingPhraseKind.TechnicalFallback);

        var reservePlan = DocumentationPlanner.Plan(reserve);
        Assert.Contains(reservePlan.Diagram.Messages, message => message.Label == "Find at most one Event");
        Assert.DoesNotContain(reservePlan.Diagram.Messages, message => message.Label is "Count Reservations" or "Add Reservation" or "Add Ticket" or "calls SaveChanges");
        Assert.Contains(reservePlan.Wording.Phrases, phrase => phrase.Kind == WordingPhraseKind.TechnicalFallback);

        var plannedDocuments = graphs
            .Select(graph => (Graph: graph, Plan: DocumentationPlanner.Plan(graph)))
            .ToArray();
        // The withhold contract replaces the old generic-label baseline: every admitted fragment
        // carries exact owner predicate wording, so no generic Condition label or Continue note
        // survives anywhere, while guarded interactions whose every owning decision lacks exact
        // wording are withheld (DP002) instead of rendered under a meaningless label. Exact counts
        // are not pinned (they depend on the external fixture's predicate facts); the contract is.
        var metrics = plannedDocuments
            .Select(item => StructuralMetrics(item.Plan.Diagram))
            .Aggregate((Conditions: 0, Subordinates: 0, Fragments: 0), (total, current) =>
                (total.Conditions + current.Conditions,
                 total.Subordinates + current.Subordinates,
                 total.Fragments + current.Fragments));
        Assert.Equal(0, metrics.Conditions);
        Assert.Equal(0, metrics.Subordinates);
        Assert.True(metrics.Fragments > 0, "Exact owner wording must still admit guarded fragments in the external flows.");
        string combinedMermaid = string.Join(
            Environment.NewLine,
            plannedDocuments.Select(item => MermaidRenderer.Render(item.Plan.Diagram)));
        string combinedMarkdown = string.Join(
            Environment.NewLine,
            plannedDocuments.Select(item => MarkdownRenderer.RenderDocument(item.Plan.Wording, item.Plan.Diagram)));
        string combinedOutput = combinedMermaid + Environment.NewLine + combinedMarkdown;

        var ownerLabels = plannedDocuments
            .SelectMany(item => item.Graph.Topology.Decisions)
            .Where(decision => decision.PredicateWording?.Role == ScenarioPredicateWordingRole.Owner)
            .Select(decision => PredicateWordingFormatter.Format(decision.PredicateWording!.Root))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(ownerLabels);
        Assert.Contains(ownerLabels, label => combinedOutput.Contains(label, StringComparison.Ordinal));

        int conditionCount = Regex.Count(
            combinedMermaid,
            @"(?m)^\s*(?:alt|opt|break) Condition(?:\s|$)|\[Condition\]");
        Assert.Equal(0, conditionCount);
        if (bundle.Extraction.PredicateSemanticFacts.Diagnostics.Any(diagnostic => diagnostic.Code == "PRED001")
            && plannedDocuments.SelectMany(item => item.Graph.Topology.Decisions)
                .Any(decision => decision.PredicateWording is null))
        {
            // Decisions without exact wording never degrade to a generic label; the withheld
            // boundary is retained as a technical fallback phrase in the Markdown lane.
            Assert.Contains("Technical fallback", combinedMarkdown, StringComparison.Ordinal);
        }

        foreach (var item in plannedDocuments)
        {
            // Subordinate decisions are never renderable on their own (they are absorbed into their
            // exact owner group or withheld), so the generic Continue note can never appear; each
            // exact owner label renders exactly once as its fragment header.
            string mermaid = MermaidRenderer.Render(item.Plan.Diagram);
            Assert.DoesNotContain("Continue evaluating condition", mermaid, StringComparison.Ordinal);
            foreach (var owner in item.Graph.Topology.Decisions
                         .Where(decision => decision.PredicateWording is { Role: ScenarioPredicateWordingRole.Owner }))
            {
                string ownerLabel = PredicateWordingFormatter.Format(owner.PredicateWording!.Root);
                Assert.InRange(Regex.Count(mermaid, Regex.Escape(ownerLabel)), 0, 1);
            }
        }

        // DQ-1 regression: the concrete owner-observed signature is a collapsed empty `break`
        // region in the external TicketReservation Mermaid. Every admitted external flow must
        // render with no empty Break block (and stay structurally valid), otherwise the
        // terminating region collapses layout in Visual Studio Code and Mermaid Live while
        // remaining syntactically valid.
        foreach (var graph in graphs)
        {
            var diagram = DocumentationPlanner.Plan(graph).Diagram;
            string mermaid = MermaidRenderer.Render(diagram);
            Assert.Empty(MermaidValidator.Validate(mermaid));
            Assert.True(
                EmptyBreakBlocks(mermaid).IsEmpty,
                "External Mermaid must not contain an empty break block for " + graph.OperationKey);
        }

        // Activation is always isolated under a test-owned temporary directory; the external checkout
        // is never used as an output root.
        string outputRoot = Path.Combine(Path.GetTempPath(), $"seqdoc-ta4-ticket-{Guid.NewGuid():N}");
        try
        {
            var entries = graphs
                .OrderBy(graph => graph.OperationKey, StringComparer.Ordinal)
                .ThenBy(graph => graph.EntryPoint.Value, StringComparer.Ordinal)
                .Select(graph =>
                {
                    var plan = DocumentationPlanner.Plan(graph);
                    string fileName = DocumentationFileNaming.EntryKey(graph.EntryPoint, graph.OperationKey);
                    return new DocumentSetEntry(fileName, plan.Wording, plan.Diagram);
                })
                .ToList();
            var built = DocumentationSetBuilder.Build(bundle.Graphs.Profile.Id.Value, bundle.Graphs.ProgramIndexFingerprint, entries);
            Assert.True(built.Succeeded, string.Join("; ", built.Errors));
            AssertDocsLintCompliant(built.Files);

            var activation = OutputSetActivator.Activate(outputRoot, built.Files);
            Assert.True(activation.Succeeded, activation.FailureMessage);
            foreach (var file in built.Files)
            {
                Assert.True(
                    File.Exists(Path.Combine(outputRoot, file.RelativePath)),
                    $"Activated file '{file.RelativePath}' is missing from '{outputRoot}'.");
            }

            Assert.True(File.Exists(Path.Combine(outputRoot, "seqdoc.manifest.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "index.md")));
            Assert.Equal(
                4,
                Directory.GetFiles(outputRoot, "*.md", SearchOption.TopDirectoryOnly).Count(file => !string.Equals(Path.GetFileName(file), "index.md", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Claims 14: reproducible accepted contract verification lane. The lane plans every admitted flow through the real
    /// planner, renders and validates the complete output set in memory, activates it into a temporary
    /// root by default, and asserts the generated Markdown satisfies the repository documentation-lint
    /// invariants. All output is activated beneath a test-owned temporary root.
    /// </summary>
    [Fact]
    public async Task FourFlowEvidenceLaneRendersAndActivatesReproducibly()
    {
        var root = FindRepositoryRoot();
        var bundle = await BuildAsync(root);
        var graphs = bundle.Graphs.Graphs
            .OrderBy(graph => graph.OperationKey, StringComparer.Ordinal)
            .ThenBy(graph => graph.EntryPoint.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(4, graphs.Length);

        var entries = graphs.Select(graph =>
        {
            var plan = DocumentationPlanner.Plan(graph);
            string fileName = DocumentationFileNaming.EntryKey(graph.EntryPoint, graph.OperationKey);
            return new DocumentSetEntry(fileName, plan.Wording, plan.Diagram);
        }).ToList();
        var built = DocumentationSetBuilder.Build(bundle.Graphs.Profile.Id.Value, bundle.Graphs.ProgramIndexFingerprint, entries);
        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        AssertDocsLintCompliant(built.Files);

        string outputRoot = Path.Combine(Path.GetTempPath(), $"seqdoc-ta4-evidence-{Guid.NewGuid():N}");
        try
        {
            var activation = OutputSetActivator.Activate(outputRoot, built.Files);
            Assert.True(activation.Succeeded, activation.FailureMessage);
            foreach (var file in built.Files)
            {
                Assert.True(
                    File.Exists(Path.Combine(outputRoot, file.RelativePath)),
                    $"Activated file '{file.RelativePath}' is missing from '{outputRoot}'.");
            }

            Assert.True(File.Exists(Path.Combine(outputRoot, "seqdoc.manifest.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "index.md")));
            Assert.Equal(
                4,
                Directory.GetFiles(outputRoot, "*.md", SearchOption.TopDirectoryOnly).Count(file => !string.Equals(Path.GetFileName(file), "index.md", StringComparison.Ordinal)));

        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private sealed record FourFlowBundle(ScenarioGraphSet Graphs, ProfileAnalysisExtraction Extraction, BehaviorSnapshot Behavior);

    [Fact]
    public async Task ConfiguredSourceRootPlacesGuardedCallAndWithholdsUnsupportedLoopCall()
    {
        var bundle = await BuildAsync(FindRepositoryRoot(), includeConfiguredRoot: true);
        var rootType = Assert.Single(bundle.Extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.FourFlows.Services.ConfiguredRoot");
        var childType = Assert.Single(bundle.Extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.FourFlows.Services.GuardedChild");
        var leafType = Assert.Single(bundle.Extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.FourFlows.Services.GuardedLeaf");
        var rootMethod = Assert.Single(bundle.Extraction.ProgramIndex.Methods,
            method => method.ContainingType == rootType.Id && method.Name == "Execute"
                && method.Parameters.Length == 1 && method.Parameters[0].FullyQualifiedType == "System.Boolean");
        var childMethod = Assert.Single(bundle.Extraction.ProgramIndex.Methods,
            method => method.ContainingType == childType.Id && method.Name == "Execute"
                && method.Parameters.Length == 1 && method.Parameters[0].FullyQualifiedType == "System.Boolean");
        var emitMethod = Assert.Single(bundle.Extraction.ProgramIndex.Methods,
            method => method.ContainingType == leafType.Id && method.Name == "Emit" && method.Parameters.IsEmpty);
        var noiseMethod = Assert.Single(bundle.Extraction.ProgramIndex.Methods,
            method => method.ContainingType == leafType.Id && method.Name == "Noise" && method.Parameters.IsEmpty);
        var tailMethod = Assert.Single(bundle.Extraction.ProgramIndex.Methods,
            method => method.ContainingType == leafType.Id && method.Name == "Tail" && method.Parameters.IsEmpty);
        var graph = Assert.Single(bundle.Graphs.Graphs, candidate => candidate.RootMethod == rootMethod.Id);
        var plan = DocumentationPlanner.Plan(graph);
        var mermaid = MermaidRenderer.Render(plan.Diagram);
        var repeatedPlan = DocumentationPlanner.Plan(graph);
        Assert.Equal(plan.Diagram.DebugProjection, repeatedPlan.Diagram.DebugProjection);
        Assert.Equal(mermaid, MermaidRenderer.Render(repeatedPlan.Diagram));

        var childFlow = Assert.Single(bundle.Behavior.MethodFlows, flow => flow.Method == childMethod.Id);
        var childInvocations = childFlow.Nodes.OfType<InvocationFlowNode>().ToArray();
        var emitInvocation = Assert.Single(childInvocations, invocation => invocation.Target == emitMethod.Id);
        Assert.Contains(childInvocations, invocation => invocation.Target == noiseMethod.Id);
        var emitDependence = Assert.Single(childFlow.ControlDependences,
            dependence => dependence.ControlledNode == emitInvocation.Id);
        var emitDecision = Assert.Single(childFlow.Nodes.OfType<DecisionFlowNode>(),
            decision => decision.Id == emitDependence.ControllingDecision);
        Assert.All(new FlowNode[] { emitDecision, emitInvocation }, node =>
        {
            Assert.NotEmpty(node.Evidence);
            Assert.Equal(CertaintyLevel.Exact, node.Certainty);
        });
        Assert.Contains(bundle.Behavior.CallGraph.CallSites, site => site.ContainingMethod == childMethod.Id
            && site.DeclaredTarget == emitMethod.Id && site.Resolution.Kind == CallResolutionKind.DirectExact);
        Assert.Contains(bundle.Behavior.CallGraph.CallSites, site => site.ContainingMethod == childMethod.Id
            && site.DeclaredTarget == noiseMethod.Id && site.Resolution.Kind == CallResolutionKind.DirectExact);
        Assert.Contains(bundle.Extraction.PredicateSemanticFacts.Mappings, mapping =>
            mapping.Method == childMethod.Id
            && mapping.LoweredConditionOperations.Contains(emitDecision.Condition));
        var emitNode = Assert.Single(graph.Nodes, node => node.Method == emitMethod.Id);
        var emitEdge = Assert.Single(graph.Edges, edge => edge.Target == emitNode.Id && edge.Kind == ScenarioEdgeKind.Call);
        var emitMessageId = new DiagramPlanElementId("diagram-element:v1:message:" + emitEdge.Id.Value);
        var emit = Assert.Single(plan.Diagram.Messages, message => message.Id == emitMessageId);
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Target == noiseMethod.Id.Value);
        Assert.DoesNotContain(graph.Edges, edge => graph.Nodes.Any(node => node.Id == edge.Target && node.Method == noiseMethod.Id));
        Assert.DoesNotContain(graph.DirectCallExpansion.Steps, step => step.TargetMethod == noiseMethod.Id);
        var boundary = Assert.Single(graph.Diagnostics, diagnostic => diagnostic.Code == "SC013");
        Assert.Equal("SC013", boundary.Code);
        Assert.NotEmpty(boundary.Evidence);
        Assert.Equal(boundary.Evidence.Max(item => item.Certainty), boundary.Certainty);
        Assert.DoesNotContain(graph.Nodes, node => node.Method == noiseMethod.Id);
        // Noise is a decision-free callee whose call-site is inside the unsupported loop. Its
        // descendant must not survive as a flattened unconditional call: pruning is transitive,
        // while the independent Emit sibling remains present.
        Assert.DoesNotContain(graph.Nodes, node => node.Method == tailMethod.Id);
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Target == tailMethod.Id.Value);

        var emitFragment = AllFragments(plan.Diagram.Sequence.Fragments)
            .Single(fragment => FragmentContainsMessage(fragment, emit.Id));
        Assert.Contains(mermaid.Split('\n'), line => line.Contains("Emit", StringComparison.Ordinal));
        Assert.DoesNotContain(mermaid, "Noise", StringComparison.Ordinal);
        Assert.NotEmpty(emit.Evidence);
        Assert.Equal(CertaintyLevel.Exact, emit.Certainty);
        Assert.NotEmpty(emitFragment.Evidence);
        Assert.Equal(new[] { emitDecision.Certainty, emitDependence.Certainty, emitInvocation.Certainty, emit.Certainty }.Max(), emitFragment.Certainty);
        if (emitFragment.Kind == DiagramFragmentKind.Alt)
        {
            var emitArm = Assert.Single(emitFragment.Arms, arm => arm.MessageRefs.Contains(emit.Id));
            Assert.NotEmpty(emitArm.Evidence);
            Assert.Equal(new[] { emitDecision.Certainty, emitDependence.Certainty, emitInvocation.Certainty, emit.Certainty }.Max(), emitArm.Certainty);
            var scenarioDecision = Assert.Single(graph.Topology.Decisions,
                decision => decision.ControllingFlowNode == emitDecision.Id);
            var scenarioArm = Assert.Single(graph.Topology.Arms,
                arm => arm.Decision == scenarioDecision.Id && arm.IsTrue == emitDependence.ControlledOnTrue);
            Assert.Contains(graph.Topology.Memberships,
                membership => membership.Arm == scenarioArm.Id && membership.ScenarioNode == emitNode.Id);
            Assert.Equal(emitDependence.ControlledOnTrue,
                scenarioArm.IsTrue);
            Assert.NotEmpty(scenarioArm.Evidence);
            Assert.Equal(new[] { scenarioDecision.Certainty, scenarioArm.Certainty }.Max(), emitArm.Certainty);
        }
        Assert.DoesNotContain(plan.Diagram.Sequence.Elements, element => element.IsMessageRef && element.MessageRefId == emit.Id);
        Assert.DoesNotContain(plan.Diagram.Sequence.MessageRefs, id => id == emit.Id);
        Assert.Equal(1, AllFragmentMessageRefs(plan.Diagram.Sequence.Fragments).Count(id => id == emit.Id));
        var renderedLines = mermaid.Split('\n');
        string[] fragmentOpeners = ["alt", "opt"];
        int fragmentStart = Array.FindIndex(renderedLines, line => fragmentOpeners.Any(opener =>
            line.Contains($"{opener} {emitFragment.Label}", StringComparison.Ordinal)));
        Assert.True(fragmentStart >= 0, $"The emitted fragment '{emitFragment.Label}' was not rendered.");
        int armBoundary = -1;
        for (int index = fragmentStart + 1; index < renderedLines.Length; index++)
        {
            if (renderedLines[index].Trim() is "else" or "end")
            {
                armBoundary = index;
                break;
            }
        }
        int emitLine = Array.FindIndex(renderedLines, line => line.Contains("Emit", StringComparison.Ordinal));
        Assert.InRange(emitLine, fragmentStart + 1, armBoundary - 1);
        Assert.DoesNotContain(plan.Diagram.Sequence.Elements, element =>
            element.IsMessageRef && plan.Diagram.Messages.Single(message => message.Id == element.MessageRefId)
                .Label.Contains("Noise", StringComparison.Ordinal));

        // P2R-R1: carry this compiler-backed plan through the real document-set decomposition seam.
        int fullMermaidLength = MermaidRenderer.Render(plan.Diagram).Length;
        var headerPlan = new DiagramPlan(
            plan.Diagram.EntryPoint, plan.Diagram.Profile, plan.Diagram.OperationKey,
            plan.Diagram.Participants, [], [], "configured-root-header");
        int headerLength = MermaidRenderer.Render(headerPlan).Length;
        int decompositionLimit = headerLength + Math.Max(80, (fullMermaidLength - headerLength) / 2);
        var budget = new DiagramBudget(1024, 4096, 1024, 256, decompositionLimit);
        Assert.True(fullMermaidLength > budget.MaxMermaidCharacters, "Precondition: budget must force decomposition.");
        Assert.True(MermaidRenderer.Render(plan.Diagram).Length <= budget.MaxMermaidCharacters * 2,
            "Precondition: the fixture must be small enough for decomposition without truncation.");

        string fileName = DocumentationFileNaming.EntryKey(graph.EntryPoint, graph.OperationKey);
        DocumentSetEntry[] entries = [new(fileName, plan.Wording, plan.Diagram)];
        var built = DocumentationSetBuilder.Build(
            bundle.Graphs.Profile.Id.Value, bundle.Graphs.ProgramIndexFingerprint, entries, budget,
            new DiagramDecompositionOptions(Enabled: true));
        var rebuilt = DocumentationSetBuilder.Build(
            bundle.Graphs.Profile.Id.Value, bundle.Graphs.ProgramIndexFingerprint, entries, budget,
            new DiagramDecompositionOptions(Enabled: true));
        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        Assert.Equal(1, built.Diagnostics.Count(item => item.Code == "DP-DIAGRAM-DECOMPOSED"));
        Assert.DoesNotContain(built.Diagnostics, item => item.Code == "DP-MERMAID-TRUNCATED");
        Assert.Equal(built.Files.Select(file => (file.RelativePath, file.Content)),
            rebuilt.Files.Select(file => (file.RelativePath, file.Content)));
        AssertMarkdownLinksResolve(built.Files);

        var mermaidFiles = built.Files.Where(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal))
            .Select(file => Encoding.UTF8.GetString(file.Content)).ToArray();
        Assert.True(mermaidFiles.Length >= 2, "Precondition: enabled decomposition must emit an overview and a part.");
        Assert.All(mermaidFiles, text =>
        {
            Assert.True(text.Length <= budget.MaxMermaidCharacters);
            Assert.Empty(MermaidValidator.Validate(text));
            Assert.DoesNotContain("Noise", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Tail", text, StringComparison.Ordinal);
        });
        string emitTuple = $"{emit.Source}{(emit.Kind == DiagramMessageKind.Request ? "->>" : "-->>")}{emit.Target}: {emit.Label}";
        var renderedMessageLines = mermaidFiles.SelectMany(text => text.Split('\n')).Select(line => line.Trim()).ToArray();
        Assert.Equal(1, renderedMessageLines.Count(line => line == emitTuple));
        Assert.Equal(1, renderedMessageLines.Count(line => line.Contains("Emit", StringComparison.Ordinal)));
        string[] emitDocument = mermaidFiles.Single(text => text.Contains(emitTuple, StringComparison.Ordinal)).Split('\n');
        int emitLineIndex = Array.FindIndex(emitDocument, line => line.Trim() == emitTuple);
        int opener = Array.FindLastIndex(emitDocument, emitLineIndex, line =>
            line.TrimStart().StartsWith("alt ", StringComparison.Ordinal)
            || line.TrimStart().StartsWith("opt ", StringComparison.Ordinal));
        int closer = Array.FindIndex(emitDocument, emitLineIndex + 1, line => line.Trim() == "end");
        Assert.True(opener >= 0 && opener < emitLineIndex && closer > emitLineIndex,
            "Emit must remain guarded after decomposition.");
        foreach (var message in plan.Diagram.Messages)
        {
            string tuple = $"{message.Source}{(message.Kind == DiagramMessageKind.Request ? "->>" : "-->>")}{message.Target}: {message.Label}";
            Assert.Equal(1, renderedMessageLines.Count(line => line == tuple));
        }
    }

    private static async Task<FourFlowBundle> BuildAsync(string root, bool includeConfiguredRoot = false)
    {
        string target = Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var profile = CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0");
        return await BuildAsync(root, target, profile, includeConfiguredRoot);
    }

    private static async Task<FourFlowBundle> BuildAsync(string root, string target, CompilationProfile profile, bool includeConfiguredRoot = false)
    {
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, target, profile),
            CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var analysis = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(
            analysis.IsSuccess,
            string.Join(Environment.NewLine, analysis.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var host = new FrameworkModelHost(
        [
            new AspNetCoreControllerModel(),
            new EntityFrameworkQueryModel(),
        ]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        var configuredRoots = includeConfiguredRoot
            ? [extraction.Value.ProgramIndex.Methods.Single(method =>
                method.ContainingType == extraction.Value.ProgramIndex.Types.Single(type =>
                    type.MetadataName == "BehaviorDocumentation.FourFlows.Services.ConfiguredRoot").Id
                && method.Name == "Execute"
                && method.Parameters.Length == 1
                && method.Parameters[0].FullyQualifiedType == "System.Boolean").Id]
            : ImmutableArray<MethodId>.Empty;
        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            extraction.Value.ProgramIndex,
            analysis.Value!,
            framework,
            extraction.Value.SemanticFacts,
            extraction.Value.DependencyInjectionFacts,
            extraction.Value.StructuralResultFacts,
            extraction.Value.NonGetSemanticFacts,
            PredicateSemanticFacts: extraction.Value.PredicateSemanticFacts,
            ConfiguredRoots: configuredRoots));
        return new FourFlowBundle(graphs, extraction.Value, analysis.Value!);
    }

    private static IEnumerable<DiagramFragment> AllFragments(IEnumerable<DiagramFragment> fragments)
    {
        foreach (var fragment in fragments)
        {
            yield return fragment;
            foreach (var nested in AllFragments(fragment.Fragments))
            {
                yield return nested;
            }

            foreach (var nested in AllFragments(fragment.Arms.SelectMany(arm => arm.Fragments)))
            {
                yield return nested;
            }
        }
    }

    private static bool FragmentContainsMessage(DiagramFragment fragment, DiagramPlanElementId messageId)
        => fragment.MessageRefs.Contains(messageId)
            || fragment.Arms.Any(arm => arm.MessageRefs.Contains(messageId)
                || arm.Fragments.Any(child => FragmentContainsMessage(child, messageId)))
            || fragment.Fragments.Any(child => FragmentContainsMessage(child, messageId));

    private static IEnumerable<DiagramPlanElementId> AllFragmentMessageRefs(IEnumerable<DiagramFragment> fragments)
        => AllFragments(fragments).SelectMany(fragment => fragment.MessageRefs.Concat(
            fragment.Arms.SelectMany(arm => arm.MessageRefs)));

    private static void AssertMarkdownLinksResolve(IReadOnlyList<RenderedOutputFile> files)
    {
        var paths = files.Select(file => file.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var file in files.Where(file => file.RelativePath.EndsWith(".md", StringComparison.Ordinal)))
        {
            string markdown = Encoding.UTF8.GetString(file.Content);
            foreach (Match match in Regex.Matches(markdown, @"\[[^\]]+\]\((?<target>[^)]+)\)"))
            {
                string target = match.Groups["target"].Value.Trim().Trim('<', '>').Split('#')[0];
                if (target.Length == 0) { continue; }
                Assert.Contains(target.TrimStart('.', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), paths);
            }
        }
    }

    private static int PhraseIndex(WordingDocument wording, string key, string contains)
    {
        // Positions are measured in the full phrase list so cross-key ordering (entity-mutation
        // before entity-save) is comparable; repeated keys carry ordinal suffixes (entity-query:1).
        var phrases = wording.Phrases.ToArray();
        int index = Array.FindIndex(phrases, phrase =>
            (phrase.Key == key || phrase.Key.StartsWith(key + ":", StringComparison.Ordinal))
            && phrase.Text.Contains(contains, StringComparison.Ordinal));
        Assert.True(
            index >= 0,
            $"No phrase of key '{key}' containing '{contains}' in ["
            + string.Join(" | ", phrases.Select(phrase => phrase.Text)) + "]");
        return index;
    }

    private static int FirstMessageIndex(DiagramPlan plan, string contains)
    {
        string[] labels = plan.Messages.Select(message => message.Label).ToArray();
        int index = Array.FindIndex(labels, label => label.Contains(contains, StringComparison.Ordinal));
        Assert.True(
            index >= 0,
            $"No message containing '{contains}' in [{string.Join(" | ", labels)}]");
        return index;
    }

    private static int LastMessageIndex(DiagramPlan plan, string contains)
    {
        string[] labels = plan.Messages.Select(message => message.Label).ToArray();
        int index = Array.FindLastIndex(labels, label => label.Contains(contains, StringComparison.Ordinal));
        Assert.True(
            index >= 0,
            $"No message containing '{contains}' in [{string.Join(" | ", labels)}]");
        return index;
    }

    private static (int Conditions, int Subordinates, int Fragments) StructuralMetrics(DiagramPlan plan)
    {
        var fragments = plan.Sequence.Fragments.SelectMany(AllFragments).ToArray();
        return (
            fragments.Count(fragment => fragment.Label == "Condition"),
            fragments.Count(fragment => fragment.Label == "Continue evaluating condition"),
            fragments.Length);
    }

    private static IEnumerable<DiagramFragment> AllFragments(DiagramFragment fragment)
    {
        yield return fragment;
        foreach (var nested in fragment.Fragments.SelectMany(AllFragments))
        {
            yield return nested;
        }
        foreach (var nested in fragment.Arms.SelectMany(arm => arm.Fragments).SelectMany(AllFragments))
        {
            yield return nested;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }

    private static void AssertDocsLintCompliant(IReadOnlyList<RenderedOutputFile> files)
    {
        RenderedOutputFile[] markdownFiles = files
            .Where(file => file.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(markdownFiles);
        foreach (var file in markdownFiles)
        {
            string content = Encoding.UTF8.GetString(file.Content);
            Assert.DoesNotContain("\r", content, StringComparison.Ordinal);
            string[] lines = content.Split('\n');
            Assert.Equal(1, lines.Count(line => line.TrimStart().StartsWith("# ", StringComparison.Ordinal)));
            Assert.DoesNotContain("synergize", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("As an automated assistant", content, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, lines.Count(line => line.StartsWith("```", StringComparison.Ordinal)) % 2);
        }
    }

    /// <summary>
    /// Every break block that has zero non-comment Mermaid statements between its opener and the
    /// matching end. A comment-only or blank interior is still an empty block; only a statement
    /// the structural validator accepts (message or participant line) counts as content. Mirrors
    /// the renderer-level DQ-1 regression predicate.
    /// </summary>
    private static ImmutableArray<string> EmptyBreakBlocks(string mermaid)
    {
        string[] lines = mermaid.Split('\n');
        var stack = new Stack<(string Kind, int Line)>();
        var empty = new List<string>();
        for (int index = 0; index < lines.Length; index++)
        {
            string trimmed = lines[index].Trim();
            string? kind = trimmed.StartsWith("break ", StringComparison.Ordinal) ? "break"
                : trimmed.StartsWith("alt ", StringComparison.Ordinal) ? "alt"
                : trimmed.StartsWith("opt ", StringComparison.Ordinal) ? "opt"
                : trimmed.StartsWith("loop ", StringComparison.Ordinal) ? "loop"
                : null;
            if (kind is not null)
            {
                stack.Push((kind, index));
                continue;
            }

            if (trimmed != "end" || stack.Count == 0)
            {
                continue;
            }

            var (poppedKind, opener) = stack.Pop();
            if (poppedKind == "break" && !BlockHasStatement(lines, opener, index))
            {
                empty.Add(lines[opener].Trim());
            }
        }

        return [.. empty];
    }

    private static bool BlockHasStatement(string[] lines, int opener, int closer)
    {
        for (int index = opener + 1; index < closer; index++)
        {
            string trimmed = lines[index].Trim();
            if (trimmed.Length == 0
                || trimmed.StartsWith("%%", StringComparison.Ordinal)
                || trimmed == "end"
                || trimmed.StartsWith("alt ", StringComparison.Ordinal)
                || trimmed.StartsWith("else ", StringComparison.Ordinal)
                || trimmed.StartsWith("opt ", StringComparison.Ordinal)
                || trimmed.StartsWith("loop ", StringComparison.Ordinal)
                || trimmed.StartsWith("break ", StringComparison.Ordinal)
                || trimmed.StartsWith("par ", StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
