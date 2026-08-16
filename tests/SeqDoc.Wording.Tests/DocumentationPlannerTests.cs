using System.Collections.Immutable;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Wording.Tests;

public sealed class DocumentationPlannerTests
{
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
