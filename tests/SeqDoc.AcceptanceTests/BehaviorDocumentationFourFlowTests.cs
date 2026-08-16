using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
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
/// below; the reproducible SEQDOC_TA4_EVIDENCE_ROOT lane plans, renders, validates, and activates the
/// complete four-flow documentation set deterministically.
/// </summary>
[Collection(BehaviorDocumentationFourFlowGroup.Name)]
public sealed class BehaviorDocumentationFourFlowTests
{
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/FourFlows/FourFlows.csproj";
    private const string ExternalTicketReservationRoot = "samples/Provided/TicketReservation-Solution";
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

        // The Mermaid diagram must order the same query messages by the same sequence ordinal.
        Assert.True(
            FirstMessageIndex(reservePlan.Diagram, "Find at most one Widget")
                < FirstMessageIndex(reservePlan.Diagram, "Count Reservations"),
            "Mermaid must render the Widget lookup before the Reservations aggregation.");
        Assert.True(
            FirstMessageIndex(reservePlan.Diagram, "Count Reservations")
                < FirstMessageIndex(reservePlan.Diagram, "Count Parts"),
            "Mermaid must render the Reservations aggregation before the Parts aggregation.");
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
                < PhraseIndex(updatePlan.Wording, "entity-save", "saves changes to WidgetDbContext"),
            "Markdown must render Add before the save.");
        foreach (string mutationLabel in new[] { "Remove Part range", "Clear tracked Parts", "Add Widget" })
        {
            Assert.True(
                LastMessageIndex(updatePlan.Diagram, mutationLabel) < FirstMessageIndex(updatePlan.Diagram, "Save changes"),
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
        if (!File.Exists(target))
        {
            return;
        }

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
        Assert.Contains(reserveQueries, node => node.Detail.Contains("CountAsync", StringComparison.Ordinal));
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

        // The external presentation order follows the same additive sequence ordinal: Markdown
        // renders the remove/clear/add/save phrases in source order and Reserve queries lookup-before-
        // aggregation.
        var updatePlan = DocumentationPlanner.Plan(update);
        Assert.True(
            PhraseIndex(updatePlan.Wording, "entity-mutation", "removes Ticket records")
                < PhraseIndex(updatePlan.Wording, "entity-mutation", "clears the tracked Ticket set"),
            "External Markdown must render RemoveRange before Clear.");
        Assert.True(
            PhraseIndex(updatePlan.Wording, "entity-mutation", "clears the tracked Ticket set")
                < PhraseIndex(updatePlan.Wording, "entity-mutation", "adds Ticket"),
            "External Markdown must render Clear before Add.");
        Assert.True(
            PhraseIndex(updatePlan.Wording, "entity-mutation", "adds Ticket")
                < PhraseIndex(updatePlan.Wording, "entity-save", "saves changes to AppDbContext"),
            "External Markdown must render Add before the save.");
        // Renderable guarded mutations stay before the save message, including the exact
        // own-header-loop-backed Add Ticket that is now structurally nested in its loop body under
        // the guards.
        foreach (string mutationLabel in new[] { "Remove Ticket range", "Clear tracked Tickets", "Add Ticket" })
        {
            Assert.True(
                LastMessageIndex(updatePlan.Diagram, mutationLabel) < FirstMessageIndex(updatePlan.Diagram, "Save changes"),
                $"External Mermaid must render every mutation message ({mutationLabel}) before the save message.");
        }

        // Add Ticket is rendered exactly once and never as an unconditional top-level message: the
        // loop body message is planned but its reference lives in the guarded fragment tree, never
        // in the sequence-level message refs.
        var addTicket = Assert.Single(updatePlan.Diagram.Messages, message => message.Label == "Add Ticket");
        Assert.DoesNotContain(updatePlan.Diagram.Sequence.MessageRefs, reference => reference == addTicket.Id);

        var reservePlan = DocumentationPlanner.Plan(reserve);
        Assert.True(
            PhraseIndex(reservePlan.Wording, "entity-query", "finds at most one Event")
                < PhraseIndex(reservePlan.Wording, "entity-query", "counts Reservations"),
            "External Markdown must render the lookup before the aggregation.");

        var plannedDocuments = graphs
            .Select(graph => (Graph: graph, Plan: DocumentationPlanner.Plan(graph)))
            .ToArray();
        var expectedMetricsByMethod = new Dictionary<HttpMethodKind, (int Conditions, int Subordinates, int Fragments)>
        {
            [HttpMethodKind.Post] = (12, 0, 13),
            [HttpMethodKind.Get] = (1, 0, 6),
            [HttpMethodKind.Delete] = (8, 0, 9),
            [HttpMethodKind.Put] = (14, 0, 15),
        };
        foreach (var item in plannedDocuments)
        {
            Assert.True(expectedMetricsByMethod.ContainsKey(item.Graph.HttpMethod));
            Assert.Equal(expectedMetricsByMethod[item.Graph.HttpMethod], StructuralMetrics(item.Plan.Diagram));
        }

        var metrics = plannedDocuments
            .Select(item => StructuralMetrics(item.Plan.Diagram))
            .Aggregate((Conditions: 0, Subordinates: 0, Fragments: 0), (total, current) =>
                (total.Conditions + current.Conditions,
                 total.Subordinates + current.Subordinates,
                 total.Fragments + current.Fragments));
        Assert.Equal(35, metrics.Conditions);
        Assert.Equal(0, metrics.Subordinates);
        Assert.Equal(43, metrics.Fragments);
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
        Assert.True(conditionCount < 36, $"Expected fewer than the CR-0 baseline of 36 Condition labels, got {conditionCount}.");
        if (bundle.Extraction.PredicateSemanticFacts.Diagnostics.Any(diagnostic => diagnostic.Code == "PRED001")
            && plannedDocuments.SelectMany(item => item.Graph.Topology.Decisions)
                .Any(decision => decision.PredicateWording is null))
        {
            Assert.Contains("Condition", combinedMermaid, StringComparison.Ordinal);
        }

        foreach (var item in plannedDocuments)
        {
            foreach (var group in item.Graph.Topology.Decisions
                         .Where(decision => decision.PredicateWording is not null)
                         .GroupBy(decision => decision.PredicateWording!.PredicateId))
            {
                var owners = group.Where(decision => decision.PredicateWording!.Role == ScenarioPredicateWordingRole.Owner).ToArray();
                var subordinates = group.Where(decision => decision.PredicateWording!.Role == ScenarioPredicateWordingRole.Subordinate).ToArray();
                if (owners.Length == 0 || subordinates.Length == 0)
                {
                    continue;
                }
                string ownerLabel = PredicateWordingFormatter.Format(owners[0].PredicateWording!.Root);
                bool hasSafeSubordinate = subordinates.All(decision =>
                    item.Graph.Topology.Arms
                        .Where(arm => arm.Decision == decision.Id)
                        .All(arm => item.Graph.Topology.Terminals
                            .Where(terminal => terminal.Arm == arm.Id)
                            .All(terminal => terminal.Kind != ScenarioTerminalKind.Terminates)));
                string mermaid = MermaidRenderer.Render(item.Plan.Diagram);
                if (hasSafeSubordinate)
                {
                    Assert.DoesNotContain("Continue evaluating condition", mermaid, StringComparison.Ordinal);
                    Assert.Equal(1, Regex.Count(mermaid, Regex.Escape(ownerLabel)));
                }
                else
                {
                    Assert.Contains("Continue evaluating condition", mermaid, StringComparison.Ordinal);
                }
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

        // Optional external evidence output lane (acceptance evidence plumbing only). When
        // SEQDOC_TA4_TICKET_EVIDENCE_ROOT is non-empty, the same four already-built external graphs
        // are planned, rendered, docs-lint validated, and activated under that root so the owner can
        // inspect external evidence without tracked repository output or production changes.
        string? ticketEvidenceRoot = Environment.GetEnvironmentVariable("SEQDOC_TA4_TICKET_EVIDENCE_ROOT");
        if (!string.IsNullOrWhiteSpace(ticketEvidenceRoot))
        {
            string outputRoot = Path.GetFullPath(ticketEvidenceRoot);
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
    }

    /// <summary>
    /// Claims 14: reproducible accepted contract verification lane. The lane plans every admitted flow through the real
    /// planner, renders and validates the complete output set in memory, activates it into a temporary
    /// root by default, and asserts the generated Markdown satisfies the repository documentation-lint
    /// invariants. When SEQDOC_TA4_EVIDENCE_ROOT is non-empty, the same deterministic output is
    /// activated under that repository-relative root and compared byte-for-byte against a fresh
    /// temporary activation, producing tracked owner evidence without changing production behavior.
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

        string? evidenceRoot = Environment.GetEnvironmentVariable("SEQDOC_TA4_EVIDENCE_ROOT");
        bool evidenceLane = !string.IsNullOrWhiteSpace(evidenceRoot);
        string outputRoot = evidenceLane
            ? Path.GetFullPath(evidenceRoot!, root)
            : Path.Combine(Path.GetTempPath(), $"seqdoc-ta4-evidence-{Guid.NewGuid():N}");
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

            if (evidenceLane)
            {
                string tempRoot = Path.Combine(Path.GetTempPath(), $"seqdoc-ta4-evidence-{Guid.NewGuid():N}");
                try
                {
                    var tempActivation = OutputSetActivator.Activate(tempRoot, built.Files);
                    Assert.True(tempActivation.Succeeded, tempActivation.FailureMessage);
                    foreach (var file in built.Files.Where(file =>
                                 file.RelativePath.EndsWith(".md", StringComparison.Ordinal)
                                 || file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)))
                    {
                        Assert.Equal(
                            File.ReadAllBytes(Path.Combine(tempRoot, file.RelativePath)),
                            File.ReadAllBytes(Path.Combine(outputRoot, file.RelativePath)));
                    }
                }
                finally
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
            }
        }
        finally
        {
            if (!evidenceLane && Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private sealed record FourFlowBundle(ScenarioGraphSet Graphs, ProfileAnalysisExtraction Extraction, BehaviorSnapshot Behavior);

    private static async Task<FourFlowBundle> BuildAsync(string root)
    {
        string target = Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var profile = CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0");
        return await BuildAsync(root, target, profile);
    }

    private static async Task<FourFlowBundle> BuildAsync(string root, string target, CompilationProfile profile)
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

        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            extraction.Value.ProgramIndex,
            analysis.Value!,
            framework,
            extraction.Value.SemanticFacts,
            extraction.Value.DependencyInjectionFacts,
            extraction.Value.StructuralResultFacts,
            extraction.Value.NonGetSemanticFacts,
            PredicateSemanticFacts: extraction.Value.PredicateSemanticFacts));
        return new FourFlowBundle(graphs, extraction.Value, analysis.Value!);
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
