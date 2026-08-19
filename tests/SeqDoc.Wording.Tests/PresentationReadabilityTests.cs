using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using Xunit;

namespace SeqDoc.Wording.Tests;

/// <summary>
/// manual acceptance partitions for readable accepted contract presentation (concise participant roles,
/// deterministic collision qualification, exact called-member call labels with Markdown contract/
/// implementation distinction, sentence-case namespace-free query wording, kind-distinct mutation and
/// save messages, and exact readable HTTP labels). The planner is the only wording/DiagramPlan
/// authority, so these pure tests assert the planned labels from hand-authored graphs that carry the
/// same fully-qualified node details the real builder emits today. They are intentionally RED against
/// the current candidate; display naming must come from the wording/DiagramPlan layer, never from
/// renderer inference. The supplemental partitions add the SF2 typed-facts-over-detail contract, the
/// SF4 group-local and generic-safe collision qualification, and the SF5 unsupported-plural neutral
/// wording contract.
/// </summary>
public sealed class PresentationReadabilityTests
{
    [Fact]
    public void ParticipantLabelsAreConciseRolesWithoutInternalPhrases()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateReservePresentationGraph());

        var participants = plan.Diagram.Participants.ToDictionary(participant => participant.Key, StringComparer.Ordinal);
        Assert.Equal("API client", participants["client"].Label);
        Assert.Equal("ReservationsController", participants["action"].Label);
        Assert.Equal("ReservationService", participants["service"].Label);
        Assert.Equal("AppDbContext", participants["data"].Label);

        Assert.DoesNotContain(
            plan.Diagram.Participants,
            participant => participant.Label.Contains("resolved service implementation", StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan.Diagram.Participants,
            participant => participant.Label.Contains("Microsoft.EntityFrameworkCore.DbSet", StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan.Diagram.Participants,
            participant => participant.Label.Contains("TicketReservation.Api", StringComparison.Ordinal));
    }

    [Fact]
    public void CollidingShortNamesReceiveDeterministicMinimalQualification()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCollisionPresentationGraph());

        // The DI-resolved implementation and the DbContext both short-name to WidgetService, so an
        // unambiguous concise label exists for neither; each must stay distinct and minimally
        // qualified without exposing the full application namespace.
        string serviceLabel = Assert.Single(plan.Diagram.Participants, participant => participant.Key == "service").Label;
        string dataLabel = Assert.Single(plan.Diagram.Participants, participant => participant.Key == "data").Label;

        Assert.Contains("WidgetService", serviceLabel, StringComparison.Ordinal);
        Assert.Contains("WidgetService", dataLabel, StringComparison.Ordinal);
        Assert.NotEqual(serviceLabel, dataLabel);
        Assert.DoesNotContain("Acme.Api", serviceLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme.Api", dataLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("resolved service implementation", serviceLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore.DbSet", dataLabel, StringComparison.Ordinal);

        var repeated = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCollisionPresentationGraph());
        Assert.Equal(
            plan.Diagram.Participants.Select(participant => participant.Label),
            repeated.Diagram.Participants.Select(participant => participant.Label));
    }

    [Fact]
    public void PrimaryCallUsesExactCalledMemberWhileMarkdownKeepsContractAndImplementation()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateReservePresentationGraph());

        var call = Assert.Single(
            plan.Diagram.Messages,
            message => message.Source == "action" && message.Target == "service");
        Assert.Equal("ReserveAsync", call.Label);

        var phrase = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "service-call");
        Assert.Contains("IReservationService", phrase.Text, StringComparison.Ordinal);
        Assert.Contains("ReservationService", phrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("resolved service implementation", phrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("TicketReservation.Api", phrase.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryLabelsAreSentenceCaseAndConciseWithoutRawNamespaces()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateReservePresentationGraph());

        var queryMessages = plan.Diagram.Messages
            .Where(message => message.Source == "service" && message.Target == "data" && message.Kind == DiagramMessageKind.Request)
            .Select(message => message.Label)
            .ToArray();
        Assert.Contains("Find at most one Event", queryMessages);
        Assert.Contains("Count Reservations", queryMessages);
        Assert.All(
            queryMessages,
            label => Assert.DoesNotContain("TicketReservation.Api", label, StringComparison.Ordinal));

        Assert.DoesNotContain(
            plan.Wording.Phrases,
            phrase => phrase.Key.StartsWith("entity-query", StringComparison.Ordinal)
                && phrase.Text.Contains("TicketReservation.Api", StringComparison.Ordinal));
    }

    [Fact]
    public void CountLabelPluralizesConsonantYEntityNameAsCategories()
    {
        // The deterministic concise pluralization must yield the valid English plural for
        // consonant+y entity names; the current candidate renders the visibly invalid
        // "Count Categorys" from the plain -s suffix.
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCategoryCountPresentationGraph());

        string[] countLabels = plan.Diagram.Messages
            .Where(message => message.Source == "service" && message.Target == "data" && message.Kind == DiagramMessageKind.Request)
            .Select(message => message.Label)
            .ToArray();

        Assert.Contains("Count Categories", countLabels);
        Assert.DoesNotContain("Count Categorys", countLabels);
    }

    [Fact]
    public void MutationAndSaveMessagesDistinguishKindsWithoutGenericText()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateUpdatePresentationGraph());

        string[] dataMessages = plan.Diagram.Messages
            .Where(message => message.Source == "service" && message.Target == "data")
            .Select(message => message.Label)
            .ToArray();

        Assert.DoesNotContain("mutates tracked entities", dataMessages);
        string[] mutations = dataMessages.Where(label => label != "Save changes").ToArray();
        Assert.Equal(3, mutations.Length);
        Assert.Equal(3, mutations.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(
            mutations,
            label => label.Contains("remove", StringComparison.OrdinalIgnoreCase) && label.Contains("Ticket", StringComparison.Ordinal));
        Assert.Contains(
            mutations,
            label => label.Contains("clear", StringComparison.OrdinalIgnoreCase) && label.Contains("Ticket", StringComparison.Ordinal));
        Assert.Contains(
            mutations,
            label => label.Contains("add", StringComparison.OrdinalIgnoreCase) && label.Contains("Ticket", StringComparison.Ordinal));
        // The save message is the exact sentence-case kind-specific label, never the lowercase
        // generic "persists changes" edge detail of the graph.
        Assert.Contains("Save changes", dataMessages);
    }

    [Fact]
    public void HttpOutcomeLabelsRemainExactAndReadableForStatus500()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateReservePresentationGraph());

        Assert.Contains(plan.Diagram.Messages, message => message.Label == "NotFound -> HTTP 404");
        Assert.Contains(
            plan.Diagram.Messages,
            message => message.Label == "CreatedAtAction -> HTTP 201 links to GET api/Reservations/{id:guid}");
        Assert.Contains(plan.Diagram.Messages, message => message.Label.Contains("HTTP 500", StringComparison.Ordinal));

        // The failure/success path labels are sentence-case readable path names, never the lowercase
        // "failure path"/"success path" branch text.
        Assert.Equal(
            "Failure path",
            Assert.Single(plan.Diagram.Branches, branch => branch.Kind == DiagramBranchKind.Failure).Label);
        Assert.Equal(
            "Success path",
            Assert.Single(plan.Diagram.Branches, branch => branch.Kind == DiagramBranchKind.Success).Label);

        // The generic helper vocabulary must never become the visible meaning of the default arm;
        // the compiler-proven HTTP status meaning remains exact and readable.
        Assert.DoesNotContain(
            plan.Diagram.Messages,
            message => message.Label.Contains("StatusCode -> HTTP", StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan.Wording.Phrases,
            phrase => phrase.Key.StartsWith("outcome", StringComparison.Ordinal)
                && phrase.Text.Contains("StatusCode -> HTTP", StringComparison.Ordinal));
    }

    /// <summary>
    /// SF2: conflicting ScenarioNode.Detail must never override typed presentation facts. Outcome
    /// helper/status/created-route labels and the mutation/save classification come from the typed
    /// facts only; nodes without typed facts receive a neutral technical fallback that never leaks
    /// the internal "resolved service implementation" phrase or the application namespace.
    /// </summary>
    [Fact]
    public void SupplementalPresentationTypedFactsOverrideConflictingDetailAndNeutralFallback()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateTypedFactsOverrideDetailGraph());

        // Outcome labels come from typed helper/status/created-route facts; the conflicting detail
        // text must never override them.
        Assert.Contains(
            plan.Diagram.Messages,
            message => message.Label == "NotFound -> HTTP 404");
        Assert.Contains(
            plan.Diagram.Messages,
            message => message.Label == "CreatedAtAction -> HTTP 201 links to GET api/Widgets/{id}");
        Assert.DoesNotContain(
            plan.Diagram.Messages,
            message => message.Label.Contains("HTTP 999", StringComparison.Ordinal));
        Assert.DoesNotContain(
            plan.Diagram.Messages,
            message => message.Label.Contains("api/Evil", StringComparison.Ordinal));

        // The mutation/save classification comes from the typed mutation kind, never the conflicting
        // "saves changes" detail: the Add node is an entity-mutation, not an entity-save.
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Key == "entity-mutation");
        Assert.DoesNotContain(
            plan.Wording.Phrases,
            phrase => phrase.Key.StartsWith("entity-save", StringComparison.Ordinal));
        var mutationPhrase = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "entity-mutation");
        Assert.DoesNotContain("saves changes to AppDbContext", mutationPhrase.Text, StringComparison.Ordinal);

        // Absent typed facts use a neutral technical fallback, never the leaked internal phrase or
        // the application namespace, in both the participant label and the wording.
        string serviceLabel = Assert.Single(plan.Diagram.Participants, participant => participant.Key == "service").Label;
        Assert.DoesNotContain("resolved service implementation", serviceLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme.Internal", serviceLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("LeakyService", serviceLabel, StringComparison.Ordinal);
        var callPhrase = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "service-call");
        Assert.DoesNotContain("resolved service implementation", callPhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme.Internal", callPhrase.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// SF4: collision qualification affects only the colliding group, and nested/constructed generic
    /// canonical type names are parsed structurally so no label short-names to a type-argument
    /// fragment ("Widget>") or exposes metadata arity ("`1"). The unrelated controller participant
    /// stays concise while the colliding generic implementation and DbContext stay distinct.
    /// </summary>
    [Fact]
    public void SupplementalPresentationCollisionQualificationIsGroupLocalAndGenericSafe()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCollisionGroupLocalGenericGraph());

        // Only the colliding group (service implementation and DbContext) is qualified; the
        // unambiguous controller short name stays concise and never expands to its namespace.
        string actionLabel = Assert.Single(plan.Diagram.Participants, participant => participant.Key == "action").Label;
        Assert.Equal("WidgetsController", actionLabel);
        Assert.DoesNotContain("Acme.Api.Controllers", actionLabel, StringComparison.Ordinal);

        string serviceLabel = Assert.Single(plan.Diagram.Participants, participant => participant.Key == "service").Label;
        string dataLabel = Assert.Single(plan.Diagram.Participants, participant => participant.Key == "data").Label;
        Assert.NotEqual(serviceLabel, dataLabel);
        Assert.Contains("WidgetService", serviceLabel, StringComparison.Ordinal);
        Assert.Contains("WidgetService", dataLabel, StringComparison.Ordinal);

        // Constructed generic canonical names are parsed structurally: never a type-argument
        // fragment ("Widget>") and never metadata arity ("`1") in a user-facing label.
        Assert.DoesNotContain("`", serviceLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("`", dataLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("<", serviceLabel, StringComparison.Ordinal);
        Assert.DoesNotContain(">", dataLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("Widget>", serviceLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// SF5: narrow pluralization applies only to supported forms. Box, Class, and Status have no
    /// proven safe plural, so Count/Clear wording uses an honest neutral label ("Count items of type
    /// Box") rather than the visibly invalid plain -s forms ("Boxs", "Classs", "Statuss").
    /// </summary>
    [Fact]
    public void SupplementalPresentationUnsupportedPluralFormsUseNeutralTypeLabel()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateUnsupportedPluralPresentationGraph());

        string[] countLabels = plan.Diagram.Messages
            .Where(message => message.Source == "service" && message.Target == "data" && message.Kind == DiagramMessageKind.Request)
            .Select(message => message.Label)
            .ToArray();

        Assert.Contains("Count items of type Box", countLabels);
        Assert.Contains("Count items of type Class", countLabels);
        Assert.Contains("Count items of type Status", countLabels);
        Assert.DoesNotContain("Count Boxs", countLabels);
        Assert.DoesNotContain("Count Classs", countLabels);
        Assert.DoesNotContain("Count Statuss", countLabels);

        // The wording phrase has the same neutral contract.
        Assert.DoesNotContain(
            plan.Wording.Phrases,
            phrase => phrase.Key.StartsWith("entity-query", StringComparison.Ordinal)
                && (phrase.Text.Contains("Boxs", StringComparison.Ordinal)
                    || phrase.Text.Contains("Classs", StringComparison.Ordinal)
                    || phrase.Text.Contains("Statuss", StringComparison.Ordinal)));
    }

    /// <summary>
    /// CA1: Compiler-proven constant arguments append a parenthesized summary to the call label,
    /// distinguishing repeated calls to the same member by their argument values. String arguments
    /// are quoted; numeric arguments are bare.
    /// </summary>
    [Fact]
    public void CallArgumentLabelsDistinguishSameMemberWithDifferentConstantArguments()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCallArgumentPresentationGraph());

        var callLabels = plan.Diagram.Messages
            .Where(message => message.Source == "action" && message.Target == "service")
            .Select(message => message.Label)
            .ToArray();

        Assert.Equal(2, callLabels.Length);
        Assert.Contains("GetItem(1)", callLabels);
        Assert.Contains("GetItem(\"alpha\")", callLabels);
        // The two calls must be distinct labels, never both "GetItem".
        Assert.NotEqual(callLabels[0], callLabels[1]);
    }
}
