using System.Collections.Immutable;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Wording.Tests;

public sealed class DocumentationPlannerTests
{
    [Fact]
    public void ConfiguredMethodUsesNeutralMethodWordingAndParticipants()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("configured-method"));
        var method = new MethodId("method:v1:Payments.TransferEngine.SubmitAsync");
        var entry = new ScenarioNode(
            new("scenario-node:v1:configured-method:entry"), ScenarioNodeKind.EntryPoint,
            "entry-point:v1:configured-method", method, null, "entry", evidence, CertaintyLevel.Exact);
        var action = new ScenarioNode(
            new("scenario-node:v1:configured-method:action"), ScenarioNodeKind.Action, "configured-method", method, null,
             "internal controller/service detail must not control wording", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.ConfiguredMethod,
                ConfiguredContainingTypeName: "Payments.TransferEngine",
                ConfiguredMethodName: "SubmitAsync",
                ConfiguredDisplaySignature: "Payments.TransferEngine.SubmitAsync()"));
        var graph = new ScenarioGraph(
            new("entry-point:v1:configured-method"), ScenarioGraphTestFactory.Profile.Id, method,
             HttpMethodKind.Unknown, "", "Payments.TransferEngine.SubmitAsync()", [entry, action],
             [new ScenarioEdge(new("scenario-edge:v1:configured-method:entry"), entry.Id, action.Id,
                 ScenarioEdgeKind.Entry, "", evidence, CertaintyLevel.Exact)], [], "configured-method", ScenarioTopology.Empty,
             rootKind: ScenarioRootKind.ConfiguredMethod);

        var plan = DocumentationPlanner.Plan(graph);
        var text = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "action").Text;

        // Behavior text preserves the full signature while the diagram participant is the concise
        // deterministic type.member label.
        Assert.Contains("Payments.TransferEngine.SubmitAsync()", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("controller", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("service", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Diagram.Participants, participant => participant.Label == "TransferEngine.SubmitAsync");
        // A configured root never invents a caller/client participant: the diagram begins at the
        // selected method, so no entry request message is planned for a root with no calls.
        Assert.DoesNotContain(plan.Diagram.Participants, participant => participant.Label == "Caller");
        Assert.Empty(plan.Diagram.Messages);
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("API", StringComparison.OrdinalIgnoreCase)
            || phrase.Text.Contains("controller", StringComparison.OrdinalIgnoreCase)
            || phrase.Text.Contains("service", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RecognizedLoggingCallsAreHiddenFromDiagramAndWording()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("configured-method"));
        var method = new MethodId("method:v1:Payments.TransferEngine.SubmitAsync");
        var entry = new ScenarioNode(
            new("scenario-node:v1:configured-method:entry"), ScenarioNodeKind.EntryPoint,
            "entry-point:v1:configured-method", method, null, "entry", evidence, CertaintyLevel.Exact);
        var action = new ScenarioNode(
            new("scenario-node:v1:configured-method:action"), ScenarioNodeKind.Action, "configured-method", method, null,
             "configured method", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.ConfiguredMethod,
                ConfiguredContainingTypeName: "Payments.TransferEngine",
                ConfiguredMethodName: "SubmitAsync",
                ConfiguredDisplaySignature: "Payments.TransferEngine.SubmitAsync()"));
        var logCall = new ScenarioNode(
            new("scenario-node:v1:configured-method:log"), ScenarioNodeKind.MethodCall,
            "method-call:log", new MethodId("method:v1:Microsoft.Extensions.Logging.LoggerExtensions.LogInformation"),
            new OperationId("operation:v1:log"), "calls Microsoft.Extensions.Logging.LoggerExtensions.LogInformation",
            evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                TargetContainingTypeName: "Microsoft.Extensions.Logging.LoggerExtensions",
                TargetMemberName: "LogInformation"));
        var transferCall = new ScenarioNode(
            new("scenario-node:v1:configured-method:transfer"), ScenarioNodeKind.MethodCall,
            "method-call:transfer", new MethodId("method:v1:Payments.TransferGateway.SendAsync"),
            new OperationId("operation:v1:send"), "calls Payments.TransferGateway.SendAsync",
            evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                TargetContainingTypeName: "Payments.TransferGateway",
                TargetMemberName: "SendAsync"));
        var graph = new ScenarioGraph(
            new("entry-point:v1:configured-method"), ScenarioGraphTestFactory.Profile.Id, method,
             HttpMethodKind.Unknown, "", "Payments.TransferEngine.SubmitAsync()", [entry, action, logCall, transferCall],
             [
                 new ScenarioEdge(new("scenario-edge:v1:configured-method:entry"), entry.Id, action.Id,
                     ScenarioEdgeKind.Entry, "", evidence, CertaintyLevel.Exact),
                 new ScenarioEdge(new("scenario-edge:v1:configured-method:log"), action.Id, logCall.Id,
                     ScenarioEdgeKind.Call, "direct method call", evidence, CertaintyLevel.Exact),
                 new ScenarioEdge(new("scenario-edge:v1:configured-method:transfer"), action.Id, transferCall.Id,
                     ScenarioEdgeKind.Call, "direct method call", evidence, CertaintyLevel.Exact),
             ], [], "configured-method", ScenarioTopology.Empty,
             rootKind: ScenarioRootKind.ConfiguredMethod);

        var plan = DocumentationPlanner.Plan(graph);

        // The recognized logging-framework call is hidden from messages, phrases, and participants;
        // the real call stays visible with its concise member label.
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("LogInformation", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("LogInformation", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Participants, participant => participant.Label.Contains("LoggerExtensions", StringComparison.Ordinal));
        Assert.Contains(plan.Diagram.Messages, message => message.Label == "SendAsync");
        Assert.Contains(plan.Diagram.Participants, participant => participant.Key == "payments_transfergateway");
    }

    [Fact]
    public void SameTypeCallReusesRootParticipantInsteadOfDuplicatingIt()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("configured-method"));
        var method = new MethodId("method:v1:Payments.TransferEngine.SubmitAsync");
        var entry = new ScenarioNode(
            new("scenario-node:v1:configured-method:entry"), ScenarioNodeKind.EntryPoint,
            "entry-point:v1:configured-method", method, null, "entry", evidence, CertaintyLevel.Exact);
        var action = new ScenarioNode(
            new("scenario-node:v1:configured-method:action"), ScenarioNodeKind.Action, "configured-method", method, null,
             "configured method", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.ConfiguredMethod,
                ConfiguredContainingTypeName: "Payments.TransferEngine",
                ConfiguredMethodName: "SubmitAsync",
                ConfiguredDisplaySignature: "Payments.TransferEngine.SubmitAsync()"));
        var helperCall = new ScenarioNode(
            new("scenario-node:v1:configured-method:helper"), ScenarioNodeKind.MethodCall,
            "method-call:helper", new MethodId("method:v1:Payments.TransferEngine.Helper"),
            new OperationId("operation:v1:helper"), "calls Payments.TransferEngine.Helper",
            evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                TargetContainingTypeName: "Payments.TransferEngine",
                TargetMemberName: "Helper"));
        var graph = new ScenarioGraph(
            new("entry-point:v1:configured-method"), ScenarioGraphTestFactory.Profile.Id, method,
             HttpMethodKind.Unknown, "", "Payments.TransferEngine.SubmitAsync()", [entry, action, helperCall],
             [
                 new ScenarioEdge(new("scenario-edge:v1:configured-method:entry"), entry.Id, action.Id,
                     ScenarioEdgeKind.Entry, "", evidence, CertaintyLevel.Exact),
                 new ScenarioEdge(new("scenario-edge:v1:configured-method:helper"), action.Id, helperCall.Id,
                     ScenarioEdgeKind.Call, "direct method call", evidence, CertaintyLevel.Exact),
             ], [], "configured-method", ScenarioTopology.Empty,
             rootKind: ScenarioRootKind.ConfiguredMethod);

        var plan = DocumentationPlanner.Plan(graph);

        // An exact same-type call renders against the single root participant instead of creating a
        // duplicate participant for the root's own type.
        Assert.Single(plan.Diagram.Participants, participant => participant.Key == "action");
        var message = Assert.Single(plan.Diagram.Messages);
        Assert.Equal(("action", "action"), (message.Source, message.Target));
        Assert.Equal("Helper", message.Label);
    }

    [Fact]
    public void GenericMethodCallUsesDeterministicParticipantAndMemberMessageWithoutServiceWording()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateGenericMethodCallGraph());

        var call = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "method-call");
        Assert.Contains("SendAsync", call.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("service", call.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["action", "payments_transfergateway"], plan.Diagram.Participants.Select(item => item.Key));
        Assert.Equal("TransferGateway", Assert.Single(plan.Diagram.Participants, item => item.Key == "payments_transfergateway").Label);
        var message = Assert.Single(plan.Diagram.Messages);
        Assert.Equal("SendAsync", message.Label);
        Assert.Equal(("action", "payments_transfergateway"), (message.Source, message.Target));
    }

    [Fact]
    public void GenericMethodCallParticipantsRemainCollisionFreeAndReservedSafe()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCollidingGenericMethodCallGraph());
        var participants = plan.Diagram.Participants;
        var targetParticipants = participants.Where(item => item.Key != "action").ToArray();
        var declaredKeys = participants.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(3, targetParticipants.Length);
        Assert.Equal(3, targetParticipants.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(targetParticipants, item => item.Key == "action");
        Assert.Equal("Acme.A_B", Assert.Single(targetParticipants, item => item.Label == "Acme.A_B").Label);
        Assert.Equal("Acme.A.B", Assert.Single(targetParticipants, item => item.Label == "Acme.A.B").Label);
        Assert.Equal("Action", Assert.Single(targetParticipants, item => item.Label == "Action").Label);
        Assert.All(plan.Diagram.Messages, message =>
        {
            Assert.Contains(message.Source, declaredKeys);
            Assert.Contains(message.Target, declaredKeys);
            Assert.NotEqual(("action", "action"), (message.Source, message.Target));
        });
    }

    [Fact]
    public void PresentationIntegrityKeepsConciseCollisionSafeLabelsAndConfiguredRootSelfMappingStable()
    {
        var collisionPlan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCollidingGenericMethodCallGraph());
        var selfCallPlan = DocumentationPlanner.Plan(CreateConfiguredSelfCallGraph());

        Assert.Equal(3, collisionPlan.Diagram.Participants.Count(item => item.Key != "action"));
        Assert.Equal(3, collisionPlan.Diagram.Participants
            .Where(item => item.Key != "action")
            .Select(item => item.Label)
            .Distinct(StringComparer.Ordinal)
            .Count());
        Assert.All(collisionPlan.Diagram.Participants.Where(item => item.Key != "action"), participant =>
            Assert.DoesNotContain(".", participant.Key, StringComparison.Ordinal));

        Assert.Single(selfCallPlan.Diagram.Participants, participant => participant.Key == "action");
        Assert.Equal(("action", "action"),
            (Assert.Single(selfCallPlan.Diagram.Messages).Source, Assert.Single(selfCallPlan.Diagram.Messages).Target));
        Assert.Equal("Helper", Assert.Single(selfCallPlan.Diagram.Messages).Label);
    }

    [Fact]
    public void PresentationIntegrityExactCallExclusionRemovesInteractionWordingEmptyFragmentsAndOrphanParticipant()
    {
        var graph = ScenarioGraphTestFactory.CreateCollidingGenericMethodCallGraph();
        var plan = DocumentationPlanner.Plan(
            graph,
            excludeCalls: ImmutableSortedSet.Create(StringComparer.Ordinal, "Acme.A.B.Second"));
        var typeWildcardPlan = DocumentationPlanner.Plan(
            graph,
            excludeCalls: ImmutableSortedSet.Create(StringComparer.Ordinal, "Acme.A_B.*"));

        Assert.DoesNotContain(plan.Diagram.Participants, participant => participant.Label == "Acme.A.B");
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label == "Second");
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("Acme.A.B", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Sequence.Fragments, fragment => fragment.MessageRefs.IsEmpty);
        Assert.Contains("filtered interaction count: 1", plan.Diagram.DebugProjection, StringComparison.Ordinal);
        Assert.DoesNotContain(typeWildcardPlan.Diagram.Participants, participant => participant.Label == "Acme.A_B");
        Assert.DoesNotContain(typeWildcardPlan.Diagram.Messages, message => message.Label == "First");
    }

    [Fact]
    public void PresentationIntegrityUnsupportedGuardedCallsRemainWithheldRatherThanUnconditional()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateBothMaterialAltGraph(
            predicateRole: ScenarioPredicateWordingRole.Owner,
            predicatePartition: "unsupported"));

        Assert.Empty(plan.Diagram.Sequence.Fragments);
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Key.Contains("guard", StringComparison.Ordinal));
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Key.StartsWith("fallback:DP005", StringComparison.Ordinal));
    }

    [Fact]
    public void PresentationIntegrityConfiguredOutcomesNeverTargetAnAbsentCaller()
    {
        var plan = DocumentationPlanner.Plan(CreateConfiguredOutcomeGraph());
        var participantKeys = plan.Diagram.Participants.Select(participant => participant.Key).ToHashSet(StringComparer.Ordinal);

        Assert.All(plan.Diagram.Messages, message =>
        {
            Assert.Contains(message.Source, participantKeys);
            Assert.Contains(message.Target, participantKeys);
        });
        Assert.DoesNotContain(plan.Diagram.Participants, participant => participant.Key == "caller");
    }

    [Fact]
    public void PresentationIntegrityRejectsConfiguredRootContainingTypeExclusionAfterGraphIdentityIsKnown()
    {
        var exception = Assert.Throws<ArgumentException>(() => DocumentationPlanner.Plan(
            CreateConfiguredSelfCallGraph(),
            excludeParticipants: ImmutableSortedSet.Create(StringComparer.Ordinal, "Payments.TransferEngine")));

        Assert.Contains("structural root participant type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationIntegrityCompleteRenderedOutputHasNoGenericControlPlaceholders()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateCompositionEmptyTopologyGraph(sourceConditionRegion: true));
        var rendered = MarkdownRenderer.RenderDocument(plan.Wording, plan.Diagram) + MermaidRenderer.Render(plan.Diagram);

        foreach (var token in new[] { "Condition", "Continue", "Continue evaluating condition", "Path terminates" })
        {
            Assert.DoesNotContain(token, rendered, StringComparison.Ordinal);
        }
    }

    private static ScenarioGraph CreateConfiguredSelfCallGraph()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("presentation-integrity-self"));
        var method = new MethodId("method:v1:Payments.TransferEngine.SubmitAsync");
        var entry = new ScenarioNode(new("scenario-node:v1:presentation-self:entry"), ScenarioNodeKind.EntryPoint,
            "entry", method, null, "entry", evidence, CertaintyLevel.Exact);
        var action = new ScenarioNode(new("scenario-node:v1:presentation-self:action"), ScenarioNodeKind.Action,
            "action", method, null, "action", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(ActionKind: ScenarioActionKind.ConfiguredMethod,
                ConfiguredContainingTypeName: "Payments.TransferEngine", ConfiguredMethodName: "SubmitAsync",
                ConfiguredDisplaySignature: "Payments.TransferEngine.SubmitAsync()"));
        var helper = new ScenarioNode(new("scenario-node:v1:presentation-self:helper"), ScenarioNodeKind.MethodCall,
            "helper", new MethodId("method:v1:Payments.TransferEngine.Helper"), new("operation:v1:helper"),
            "helper", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(TargetContainingTypeName: "Payments.TransferEngine", TargetMemberName: "Helper"));
        return new ScenarioGraph(new("entry-point:v1:presentation-self"), ScenarioGraphTestFactory.Profile.Id, method,
            HttpMethodKind.Unknown, "", "Payments.TransferEngine.SubmitAsync()", [entry, action, helper],
            [new ScenarioEdge(new("scenario-edge:v1:presentation-self"), action.Id, helper.Id, ScenarioEdgeKind.Call,
                "call", evidence, CertaintyLevel.Exact)], [], "presentation-self", ScenarioTopology.Empty,
            rootKind: ScenarioRootKind.ConfiguredMethod);
    }

    private static ScenarioGraph CreateConfiguredOutcomeGraph()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("presentation-integrity-outcome"));
        var method = new MethodId("method:v1:Payments.TransferEngine.SubmitAsync");
        var entry = new ScenarioNode(new("scenario-node:v1:presentation-outcome:entry"), ScenarioNodeKind.EntryPoint,
            "entry", method, null, "entry", evidence, CertaintyLevel.Exact);
        var action = new ScenarioNode(new("scenario-node:v1:presentation-outcome:action"), ScenarioNodeKind.Action,
            "action", method, null, "action", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(ActionKind: ScenarioActionKind.ConfiguredMethod,
                ConfiguredContainingTypeName: "Payments.TransferEngine", ConfiguredMethodName: "SubmitAsync",
                ConfiguredDisplaySignature: "Payments.TransferEngine.SubmitAsync()"));
        var outcome = new ScenarioNode(new("scenario-node:v1:presentation-outcome:ok"), ScenarioNodeKind.Outcome,
            "outcome", null, new("operation:v1:presentation-outcome:ok"), "HTTP 200", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(OutcomeStatusCode: 200));
        return new ScenarioGraph(new("entry-point:v1:presentation-outcome"), ScenarioGraphTestFactory.Profile.Id, method,
            HttpMethodKind.Unknown, "", "Payments.TransferEngine.SubmitAsync()", [entry, action, outcome],
            [new ScenarioEdge(new("scenario-edge:v1:presentation-outcome"), action.Id, outcome.Id,
                ScenarioEdgeKind.OutcomeSuccess, "success", evidence, CertaintyLevel.Exact)], [],
            "presentation-outcome", ScenarioTopology.Empty, rootKind: ScenarioRootKind.ConfiguredMethod);
    }

    [Fact]
    public void ExactDispatchUsesCanonicalRequestAndHandlerWording()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("dispatch-wording"));
        var dispatch = new ScenarioNode(
            new("scenario-node:v1:dispatch:create-order"), ScenarioNodeKind.Dispatch,
            "dispatch:create-order", new("method:v1:Program.CreateOrder"),
            new("operation:v1:send-create-order"), "CreateOrderDraftCommand", evidence, CertaintyLevel.Exact,
             presentation: new ScenarioNodePresentation(RequestTypeName: "eShop.Ordering.API.Application.CreateOrderDraftCommand"));
        var handler = new ScenarioNode(
            new("scenario-node:v1:handler:create-order"), ScenarioNodeKind.Handler,
            "handler:create-order", new("method:v1:Handlers.CreateOrderHandler.Handle"), null,
            "CreateOrderCommandHandler", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(HandlerTypeName: "CreateOrderCommandHandler", HandlerBodyAvailable: true));
        var graph = new ScenarioGraph(
            new("entry-point:v1:dispatch-wording"), ScenarioGraphTestFactory.Profile.Id,
            new("method:v1:Program.CreateOrder"), HttpMethodKind.Post, "/orders", "POST /orders",
            [dispatch, handler],
            [new ScenarioEdge(new("scenario-edge:v1:dispatch"), dispatch.Id, handler.Id,
                ScenarioEdgeKind.Dispatch, "dispatch", evidence, CertaintyLevel.Exact)],
            [], "dispatch-wording", ScenarioTopology.Empty);

        var texts = DocumentationPlanner.Plan(graph).Wording.Phrases.Select(phrase => phrase.Text).ToArray();

        Assert.Contains("Dispatches CreateOrderDraftCommand", texts);
        Assert.DoesNotContain(texts, text => text.Contains("eShop.Ordering.API.Application", StringComparison.Ordinal));
        Assert.Contains("Routes to CreateOrderCommandHandler", texts);
    }

    [Fact]
    public void DispatchWordingUsesTypedBodyAvailabilityAndPlanHasDeclaredOrderedParticipants()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("dispatch-plan"));
        var action = new ScenarioNode(
            new("scenario-node:v1:action:generated"), ScenarioNodeKind.Action,
            "action:generated", new("method:v1:Program.CreateOrder"), null,
            "typed minimal API action", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(ActionKind: ScenarioActionKind.MinimalApiHandler));
        var dispatch = new ScenarioNode(
            new("scenario-node:v1:dispatch:generated"), ScenarioNodeKind.Dispatch,
            "dispatch:generated", new("method:v1:Program.CreateOrder"),
            new("operation:v1:send-create-order"), "CreateOrderCommand", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(RequestTypeName: "CreateOrderCommand"));
        var handler = new ScenarioNode(
            new("scenario-node:v1:handler:generated"), ScenarioNodeKind.Handler,
            "handler:generated", new("method:v1:Generated.Handle"), null,
            "generated body unavailable; detail must not control wording", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(HandlerTypeName: "GeneratedHandler", HandlerBodyAvailable: true));
        var graph = new ScenarioGraph(
            new("entry-point:v1:dispatch-plan"), ScenarioGraphTestFactory.Profile.Id,
            new("method:v1:Program.CreateOrder"), HttpMethodKind.Post, "/orders", "POST /orders",
            [action, dispatch, handler],
            [
                new ScenarioEdge(new("scenario-edge:v1:action-dispatch-plan"), action.Id, dispatch.Id,
                    ScenarioEdgeKind.Dispatch, "dispatch", evidence, CertaintyLevel.Exact),
                new ScenarioEdge(new("scenario-edge:v1:dispatch-handler-plan"), dispatch.Id, handler.Id,
                    ScenarioEdgeKind.Dispatch, "dispatch", evidence, CertaintyLevel.Exact),
            ],
            [], "dispatch-plan", ScenarioTopology.Empty);

        var plan = DocumentationPlanner.Plan(graph);
        var phrase = Assert.Single(plan.Wording.Phrases, item => item.Key == "handler");
        Assert.Contains(plan.Wording.Phrases, item => item.Text == "Dispatches CreateOrderCommand");
        Assert.Equal("Routes to GeneratedHandler", phrase.Text);
        Assert.Equal(["action", "dispatch", "handler"], plan.Diagram.Participants.Select(item => item.Key));
        Assert.Equal(["CreateOrderCommand", "GeneratedHandler"], plan.Diagram.Messages.Select(item => item.Label));
        Assert.Equal(
            [("action", "dispatch"), ("dispatch", "handler")],
            plan.Diagram.Messages.Select(message => (message.Source, message.Target)));
        Assert.Equal(
            plan.Diagram.Messages.Select(message => message.Id),
            plan.Diagram.Sequence.Elements.Select(item => item.MessageRefId!.Value));
    }

    [Fact]
    public void DispatchWithoutRequestPresentationUsesNeutralWordingAndDoesNotLeakDetail()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("dispatch-neutral"));
        var dispatch = new ScenarioNode(
            new("scenario-node:v1:dispatch:opaque"), ScenarioNodeKind.Dispatch,
            "dispatch:opaque", new("method:v1:Program.Dispatch"), new("operation:v1:opaque"),
            "opaque internal dispatch detail", evidence, CertaintyLevel.Exact);
        var graph = new ScenarioGraph(
            new("entry-point:v1:dispatch-neutral"), ScenarioGraphTestFactory.Profile.Id,
            new("method:v1:Program.Dispatch"), HttpMethodKind.Post, "/opaque", "POST /opaque",
            [dispatch], [], [], "dispatch-neutral", ScenarioTopology.Empty);

        var texts = DocumentationPlanner.Plan(graph).Wording.Phrases.Select(phrase => phrase.Text).ToArray();

        Assert.Contains("Dispatches a request", texts);
        Assert.DoesNotContain(texts, text => text.Contains("opaque internal dispatch detail", StringComparison.Ordinal));
    }

    [Fact]
    public void MinimalApiActionUsesTypedHandlerWording()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("minimal-action"));
        var action = new ScenarioNode(
            new("scenario-node:v1:minimal-action"),
            ScenarioNodeKind.Action,
            "action:typed-handler",
            new("method:v1:Program.TypedHandler"),
            null,
            "controller action text must not be parsed",
            evidence,
            CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(ActionKind: ScenarioActionKind.MinimalApiHandler));
        var graph = new ScenarioGraph(
            new("entry-point:v1:minimal-wording"),
            ScenarioGraphTestFactory.Profile.Id,
            new("method:v1:Program.TypedHandler"),
            HttpMethodKind.Post,
            "/typed",
            "POST /typed",
            [action],
            [],
            [],
            "typed-minimal",
            ScenarioTopology.Empty);

        var plan = DocumentationPlanner.Plan(graph);
        var phrase = Assert.Single(
            plan.Wording.Phrases,
            item => item.Key == "action");

        Assert.Equal("The Minimal API handler executes.", phrase.Text);
        var participant = Assert.Single(plan.Diagram.Participants, item => item.Key == "action");
        Assert.Equal("Minimal API handler", participant.Label);
        Assert.Equal(DiagramParticipantKind.Controller, participant.Kind);
    }

    [Fact]
    public void NamedMinimalApiActionUsesCompilerDerivedParticipantName()
    {
        var plan = DocumentationPlanner.Plan(
            ScenarioGraphTestFactory.CreateMinimalApiHandlerGraph("OrdersApi.CreateOrderDraftAsync"));

        var participant = Assert.Single(plan.Diagram.Participants, item => item.Key == "action");
        Assert.Equal("OrdersApi.CreateOrderDraftAsync", participant.Label);
    }

    [Fact]
    public void ExactHttpControllerActionUsesConciseLabelAndRetainsIdentityWithoutSelfParticipant()
    {
        const string fullIdentity = "CreditTransfer.Api.Controllers.CreditTransferController.Post";
        var evidence = ImmutableArray.Create(new EvidenceRef(
            new EvidenceId("evidence:v1:http-controller-label"), EvidenceKind.Source,
            "CreditTransferController.cs", null, "CreditTransfer.Api.Controllers.CreditTransferController.Post", null,
            CertaintyLevel.Exact));
        var method = new MethodId("method:v1:" + fullIdentity);
        var action = new ScenarioNode(
            new("scenario-node:v1:http-controller-label:action"), ScenarioNodeKind.Action, "action", method, null,
            fullIdentity, evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.ControllerAction,
                ControllerTypeName: "CreditTransfer.Api.Controllers.CreditTransferController",
                ActionMethodName: "Post"));
        var selfCall = new ScenarioNode(
            new("scenario-node:v1:http-controller-label:self"), ScenarioNodeKind.MethodCall, "self-call", method,
            new("operation:v1:http-controller-label:self"), fullIdentity, evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                TargetContainingTypeName: "CreditTransfer.Api.Controllers.CreditTransferController",
                TargetMemberName: "Validate"));
        var graph = new ScenarioGraph(
            new("entry-point:v1:http-controller-label"), ScenarioGraphTestFactory.Profile.Id, method,
            HttpMethodKind.Post, "/credit-transfers", "POST /credit-transfers", [action, selfCall],
            [new ScenarioEdge(new("scenario-edge:v1:http-controller-label:self"), action.Id, selfCall.Id,
                ScenarioEdgeKind.Call, "call", evidence, CertaintyLevel.Exact)], [], fullIdentity,
            ScenarioTopology.Empty);

        var plan = DocumentationPlanner.Plan(graph);
        var participant = Assert.Single(plan.Diagram.Participants, item => item.Key == "action");

        Assert.Equal("CreditTransferController.Post", participant.Label);
        Assert.Equal(fullIdentity, method.Value["method:v1:".Length..]);
        Assert.Equal("CreditTransfer.Api.Controllers.CreditTransferController.Post", participant.Evidence[0].Symbol);
        Assert.Equal(("action", "action"), (Assert.Single(plan.Diagram.Messages).Source, Assert.Single(plan.Diagram.Messages).Target));
        Assert.Contains("label=CreditTransferController.Post", plan.Diagram.DebugProjection, StringComparison.Ordinal);
        Assert.Contains("canonical=CreditTransfer.Api.Controllers.CreditTransferController.Post", plan.Diagram.DebugProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("label=CreditTransfer.Api.Controllers.CreditTransferController.Post", plan.Diagram.DebugProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpActionLabelQualifiesAfterCollisionWithAnotherParticipant()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateHttpActionLabelCollisionGraph());

        Assert.Equal("Api.B.Method", Assert.Single(plan.Diagram.Participants, item => item.Key == "action").Label);
        Assert.Contains(plan.Diagram.Participants, item => item.Label == "B.Method");
    }

    [Theory]
    [InlineData("", "Post", "Controller action")]
    [InlineData("CreditTransfer.Api.Controllers.CreditTransferController", "", "CreditTransferController")]
    [InlineData("", "", "Controller action")]
    public void IncompleteControllerActionFactsKeepTheNeutralFallback(
        string controllerType, string actionMember, string expectedParticipantLabel)
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("incomplete-controller-action"));
        var method = new MethodId("method:v1:incomplete-controller-action");
        var action = new ScenarioNode(
            new("scenario-node:v1:incomplete-controller-action"), ScenarioNodeKind.Action, "action", method, null,
            "incomplete controller action", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.ControllerAction,
                ControllerTypeName: controllerType,
                ActionMethodName: actionMember));
        var graph = new ScenarioGraph(
            new("entry-point:v1:incomplete-controller-action"), ScenarioGraphTestFactory.Profile.Id,
            method, HttpMethodKind.Get, "/incomplete", "GET /incomplete", [action], [], [],
            "incomplete-controller-action", ScenarioTopology.Empty);

        var participant = Assert.Single(DocumentationPlanner.Plan(graph).Diagram.Participants, item => item.Key == "action");

        Assert.Equal(expectedParticipantLabel, participant.Label);
        Assert.DoesNotContain('.', participant.Label);
        Assert.DoesNotContain("Post", participant.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void DispatchExpansionPreservesNestedLoopAndReturnOrderWithoutDuplicatingMessages()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateExactDispatchExpansionGraph());

        var loop = Assert.Single(FindFragments(plan.Diagram.Sequence.Elements), fragment => fragment.Kind == DiagramFragmentKind.Loop);
        var loopMessageLabels = loop.MessageRefs
            .Select(reference => plan.Diagram.Messages.Single(message => message.Id == reference).Label)
            .ToArray();
        Assert.Equal(["Order.AddOrderItem"], loopMessageLabels);

        var flattenedLabels = FlattenSequence(plan.Diagram.Sequence)
            .Select(reference => plan.Diagram.Messages.Single(message => message.Id == reference).Label)
            .ToArray();
        Assert.Equal(
            ["Order.NewDraft", "Order.AddOrderItem", "OrderDraftDTO.FromOrder", "Order.GetTotal", "return OrderDraftDTO"],
            flattenedLabels);

        var flattenedIds = FlattenSequence(plan.Diagram.Sequence).ToArray();
        Assert.Equal(plan.Diagram.Messages.Select(message => message.Id).OrderBy(id => id.Value),
            flattenedIds.OrderBy(id => id.Value));
        Assert.Equal(flattenedIds.Length, flattenedIds.Distinct().Count());

        static IEnumerable<DiagramPlanElementId> FlattenSequence(DiagramSequence sequence)
            => sequence.Elements.SelectMany(FlattenElement);

        static IEnumerable<DiagramFragment> FindFragments(IEnumerable<DiagramSequenceElement> elements)
            => elements
                .Where(element => element.IsFragment)
                .SelectMany(element => FindFragmentAndNested(element.NestedFragment!));

        static IEnumerable<DiagramFragment> FindFragmentAndNested(DiagramFragment fragment)
        {
            yield return fragment;

            foreach (var nested in fragment.Fragments)
            {
                foreach (var descendant in FindFragmentAndNested(nested))
                {
                    yield return descendant;
                }
            }

            foreach (var arm in fragment.Arms)
            {
                foreach (var nested in arm.Fragments)
                {
                    foreach (var descendant in FindFragmentAndNested(nested))
                    {
                        yield return descendant;
                    }
                }
            }
        }

        static IEnumerable<DiagramPlanElementId> FlattenElement(DiagramSequenceElement element)
        {
            if (element.IsMessageRef)
            {
                yield return element.MessageRefId!.Value;
                yield break;
            }

            foreach (var reference in FlattenFragment(element.NestedFragment!))
            {
                yield return reference;
            }
        }

        static IEnumerable<DiagramPlanElementId> FlattenFragment(DiagramFragment fragment)
        {
            foreach (var reference in fragment.MessageRefs)
            {
                yield return reference;
            }

            foreach (var nested in fragment.Fragments)
            {
                foreach (var reference in FlattenFragment(nested))
                {
                    yield return reference;
                }
            }

            foreach (var arm in fragment.Arms)
            {
                foreach (var reference in arm.MessageRefs)
                {
                    yield return reference;
                }

                foreach (var nested in arm.Fragments)
                {
                    foreach (var reference in FlattenFragment(nested))
                    {
                        yield return reference;
                    }
                }
            }
        }
    }

    [Fact]
    public void CanonicalTargetAliasesRemainDistinctAndMessagesTargetEachParticipantOnceRegardlessOfParticipantInputOrder()
    {
        var normal = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCanonicalTargetGraph());
        var reversed = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCanonicalTargetGraph(reversed: true));

        Assert.Equal(["Alpha.Widget", "Beta.Widget"], normal.Diagram.Participants
            .Where(item => item.Key is "alpha_widget" or "beta_widget")
            .Select(item => item.Label));
        Assert.Equal(1, normal.Diagram.Messages.Count(item => item.Target == "alpha_widget"));
        Assert.Equal(1, normal.Diagram.Messages.Count(item => item.Target == "beta_widget"));
        Assert.Equal(normal.Diagram.DebugProjection, reversed.Diagram.DebugProjection);
        Assert.DoesNotContain(normal.Diagram.Participants, item => item.Label == "Widget");
    }

    [Fact]
    public void MinimalHandlerDiagramUsesCompilerPredicatesDelayAndExactStatuses()
    {
        var graph = ScenarioGraphTestFactory.CreateMinimalApiHandlerGraph();
        var plan = DocumentationPlanner.Plan(graph);

        Assert.Empty(plan.Diagram.Branches);
        var outer = Assert.Single(plan.Diagram.Sequence.Elements).NestedFragment!;
        Assert.Equal("roll is at most 30", outer.Label);
        var inner = Assert.Single(outer.Arms[1].Fragments);
        Assert.Equal("roll is at most 50", inner.Label);
        Assert.Equal(2, inner.Arms[0].MessageRefs.Length);
        Assert.Single(inner.Arms[1].MessageRefs);
        Assert.Contains(plan.Diagram.Messages, message => message.Label == "HTTP 500");
        Assert.Contains(plan.Diagram.Messages, message => message.Label == "HTTP 200");
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Text == "The handler requests a delay of 11 seconds.");
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Text == "The request body binds to SmsRequest request.");
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Text == "The Minimal API handler responds with HTTP 500.");
        Assert.Contains(plan.Wording.Phrases, phrase => phrase.Text == "The Minimal API handler responds with HTTP 200.");
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("controller", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Wording.Phrases, phrase => phrase.Text.Contains("technical factory", StringComparison.Ordinal));
        Assert.DoesNotContain("Condition", plan.Diagram.DebugProjection, StringComparison.Ordinal);
    }

    [Fact]
    public void MinimalHandlerBindingWordingUsesTypedSourcePartitions()
    {
        var evidence = ImmutableArray.Create(ScenarioGraphTestFactory.SourceEvidence("binding-wording"));
        var nodes = new[]
        {
            BindingNode("route", HttpBindingKind.Route, "id", "Guid"),
            BindingNode("query", HttpBindingKind.Query, "page", "int"),
            BindingNode("cancellation", HttpBindingKind.CancellationToken, "ct", "CancellationToken"),
            BindingNode("unknown", HttpBindingKind.Unknown, "value", "RequestValue"),
        };
        var graph = new ScenarioGraph(
            new EntryPointId("entry-point:v1:binding-wording"), ScenarioGraphTestFactory.Profile.Id,
            new MethodId("method:v1:Program.Handler"), HttpMethodKind.Post, "binding", "POST binding",
            nodes.ToImmutableArray(), [], [], "binding-wording", ScenarioTopology.Empty);

        var texts = DocumentationPlanner.Plan(graph).Wording.Phrases.Select(phrase => phrase.Text).ToArray();

        Assert.Contains("Route parameter id binds to Guid.", texts);
        Assert.Contains("Query parameter page binds to int.", texts);
        Assert.Contains("The framework supplies CancellationToken ct.", texts);
        Assert.Contains("The handler parameter RequestValue value has an unknown binding.", texts);
    }

    [Fact]
    public void MinimalHandlerDirectResultPreservesRequestAndResponseSequence()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateMinimalApiDirectResultGraph());

        Assert.Equal(["POST direct-result", "HTTP 200"], plan.Diagram.Messages.Select(message => message.Label));
        Assert.Equal(2, plan.Diagram.Sequence.Elements.Length);
        Assert.All(plan.Diagram.Sequence.Elements, element => Assert.True(element.IsMessageRef));
        Assert.DoesNotContain(plan.Diagram.Sequence.Elements, element => element.IsFragment);
    }

    [Fact]
    public void MinimalHandlerOneDecisionPlacesBothOutcomesOnceAfterRequest()
    {
        var plan = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateMinimalApiOneDecisionGraph());

        Assert.Equal(2, plan.Diagram.Sequence.Elements.Length);
        Assert.True(plan.Diagram.Sequence.Elements[0].IsMessageRef);
        var fragment = Assert.IsType<DiagramFragment>(plan.Diagram.Sequence.Elements[1].NestedFragment);
        Assert.Equal(2, fragment.Arms.Length);
        Assert.Equal(["value is positive", "Otherwise"], fragment.Arms.Select(arm => arm.Label));
        Assert.Equal(2, fragment.Arms.SelectMany(arm => arm.MessageRefs).Count());
        Assert.Equal(plan.Diagram.Messages.Select(message => message.Id),
            plan.Diagram.Sequence.Elements.SelectMany(References).Distinct());

        static IEnumerable<DiagramPlanElementId> References(DiagramSequenceElement element)
            => element.IsMessageRef
                ? [element.MessageRefId!.Value]
                : element.NestedFragment!.Arms.SelectMany(arm => arm.MessageRefs);
    }

    private static ScenarioNode BindingNode(string key, HttpBindingKind binding, string name, string type)
        => new(
            new ScenarioNodeId($"scenario-node:v1:binding:{key}"), ScenarioNodeKind.SourceObservation,
            $"handler-parameter:{key}", null, null, "technical detail must not be rendered",
            ImmutableArray.Create(new EvidenceRef(
                new EvidenceId("evidence:v1:binding-wording"), EvidenceKind.Source, "binding-wording",
                null, null, null, CertaintyLevel.Exact)),
            CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.MinimalApiHandler,
                HandlerBindingKind: binding,
                HandlerParameterName: name,
                HandlerParameterTypeName: type));
}
