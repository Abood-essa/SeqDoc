using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.ScenarioGraph;

namespace SeqDoc.Wording.Tests;

/// <summary>
/// Builds hand-authored Scenario Graph inputs for the planner so wording tests run as a small pure
/// layer. The graph shapes mirror the admitted GetMeaning Get flow without requiring a compiler
/// session; identities are stable test anchors and evidence is source-shaped and deterministic.
/// </summary>
internal static class ScenarioGraphTestFactory
{
    internal static readonly CompilationProfile Profile = CompilationProfile.Create(
        "tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj",
        "Release",
        "net10.0");

    internal static readonly EntryPointId GetEntryPoint = new("entry-point:v1:GET-api-Gadgets");

    internal static ScenarioGraph CreateGenericMethodCallGraph()
    {
        var evidence = ImmutableArray.Create(SourceEvidence("generic-method-call"));
        var action = new ScenarioNode(
            new("scenario-node:v1:generic-call:action"), ScenarioNodeKind.Action, "action", new("method:v1:Payments.Api.Transfer"), null,
            "controller action", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(ActionKind: ScenarioActionKind.ControllerAction));
        var call = new ScenarioNode(
            new("scenario-node:v1:generic-call:call"), ScenarioNodeKind.MethodCall, "method-call", new("method:v1:Payments.TransferGateway.SendAsync"),
            new("operation:v1:root.transfer"), "internal call detail must not control wording", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(TargetContainingTypeName: "Payments.TransferGateway", TargetMemberName: "SendAsync"));
        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:generic-call"), Profile.Id, new("method:v1:Payments.Api.Transfer"), HttpMethodKind.Post,
            "/transfers", "POST /transfers", [action, call],
            [new ScenarioEdge(new("scenario-edge:v1:generic-call"), action.Id, call.Id, ScenarioEdgeKind.Call, "generic call", evidence, CertaintyLevel.Exact)],
            [], "generic-method-call", ScenarioTopology.Empty);
    }

    internal static ScenarioGraph CreateCollidingGenericMethodCallGraph()
    {
        var evidence = ImmutableArray.Create(SourceEvidence("colliding-generic-method-call"));
        var action = new ScenarioNode(
            new("scenario-node:v1:collision:action"), ScenarioNodeKind.Action, "action", new("method:v1:Collision.Api.Run"), null,
            "controller action", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(ActionKind: ScenarioActionKind.ControllerAction));
        var targets = new[]
        {
            ("Acme.A_B", "First", "one"),
            ("Acme.A.B", "Second", "two"),
            ("Action", "Third", "three"),
        }.Select(item => new ScenarioNode(
            new($"scenario-node:v1:collision:{item.Item3}"), ScenarioNodeKind.MethodCall,
            $"method-call:{item.Item3}", new($"method:v1:{item.Item1}.{item.Item2}"), new($"operation:v1:collision:{item.Item3}"),
            $"calls {item.Item1}.{item.Item2}", evidence, CertaintyLevel.Exact,
            presentation: new ScenarioNodePresentation(TargetContainingTypeName: item.Item1, TargetMemberName: item.Item2)))
            .ToArray();
        var edges = targets.Select((target, index) => new ScenarioEdge(
            new($"scenario-edge:v1:collision:{index}"), action.Id, target.Id, ScenarioEdgeKind.Call,
            "generic call", evidence, CertaintyLevel.Exact, index)).ToImmutableArray();
        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:collision"), Profile.Id, new("method:v1:Collision.Api.Run"), HttpMethodKind.Post,
            "/collision", "POST /collision", [action, .. targets], edges, [], "collision", ScenarioTopology.Empty);
    }

    internal static ScenarioGraph CreateMinimalApiHandlerGraph(string? actionParticipant = null)
    {
        var evidence = ImmutableArray.Create(SourceEvidence("minimal-handler"));
        var action = Node("scenario-node:v1:minimal-handler:action", ScenarioNodeKind.Action,
            "action:typed-handler", "minimal API handler", "minimal-handler",
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.MinimalApiHandler,
                ControllerTypeName: actionParticipant));
        var parameter = Node("scenario-node:v1:minimal-handler:parameter", ScenarioNodeKind.SourceObservation,
            "handler-parameter:request", "receives SmsRequest request", "minimal-handler",
            sequenceOrdinal: 0,
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.MinimalApiHandler,
                HandlerBindingKind: HttpBindingKind.Body,
                HandlerParameterName: "request",
                HandlerParameterTypeName: "SmsRequest",
                SourceOrdinal: 0));
        var delay = Node("scenario-node:v1:minimal-handler:delay", ScenarioNodeKind.Delay,
            "handler-operation:delay", "requested delay 11000 milliseconds", "minimal-handler",
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.MinimalApiHandler,
                SourceOrdinal: 1));
        var problem = Node("scenario-node:v1:minimal-handler:problem", ScenarioNodeKind.Outcome,
            "handler-operation:problem", "HTTP 500 (technical factory detail)", "minimal-handler",
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.MinimalApiHandler,
                OutcomeStatusCode: 500,
                SourceOrdinal: 0));
        var delayedOk = Node("scenario-node:v1:minimal-handler:delayed-ok", ScenarioNodeKind.Outcome,
            "handler-operation:delayed-ok", "HTTP 200 (technical factory detail)", "minimal-handler",
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.MinimalApiHandler,
                OutcomeStatusCode: 200,
                SourceOrdinal: 2));
        var immediateOk = Node("scenario-node:v1:minimal-handler:immediate-ok", ScenarioNodeKind.Outcome,
            "handler-operation:immediate-ok", "HTTP 200 (technical factory detail)", "minimal-handler",
            presentation: new ScenarioNodePresentation(
                ActionKind: ScenarioActionKind.MinimalApiHandler,
                OutcomeStatusCode: 200,
                SourceOrdinal: 3));
        var topology = new ScenarioHandlerTopology(
            [new ScenarioHandlerParameter("request", "SmsRequest", HttpBindingKind.Body, evidence, CertaintyLevel.Exact)],
             [new ScenarioHandlerDecision(0, null, null, "roll is at most 30", evidence, CertaintyLevel.Exact),
              new ScenarioHandlerDecision(1, 0, false, "roll is at most 50", evidence, CertaintyLevel.Exact)],
             [new ScenarioHandlerOutcome(0, 0, true, 500, "Microsoft.AspNetCore.Http.Results.Problem", evidence, CertaintyLevel.Exact),
              new ScenarioHandlerOutcome(2, 1, true, 200, "Microsoft.AspNetCore.Http.Results.Ok", evidence, CertaintyLevel.Exact),
              new ScenarioHandlerOutcome(3, 1, false, 200, "Microsoft.AspNetCore.Http.Results.Ok", evidence, CertaintyLevel.Exact)],
              [new ScenarioHandlerDelay(1, 1, true, 11000, evidence, CertaintyLevel.Exact)]);
        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:POST-api-sms"), Profile.Id,
            new MethodId("method:v1:Program.Telecom"), HttpMethodKind.Post, "api/sms", "POST api/sms",
             [action, parameter, problem, delay, delayedOk, immediateOk], [], [], "minimal-handler", ScenarioTopology.Empty, handlerTopology: topology);
    }

    internal static ScenarioGraph CreateExactDispatchExpansionGraph()
    {
        var evidence = ImmutableArray.Create(SourceEvidence("dispatch-expansion"));
        var handler = new MethodId("method:v1:Orders.CreateOrderDraft");
        var dto = new MethodId("method:v1:OrderDraftDTO.FromOrder");
        var create = Step("create", 0, 0, handler, "method:v1:Order.NewDraft", "Order.NewDraft");
        var add = Step("add", 1, 0, handler, "method:v1:Order.AddOrderItem", "Order.AddOrderItem", "order-items");
        var fromOrder = Step("dto", 2, 0, handler, dto.Value, "OrderDraftDTO.FromOrder");
        var total = Step("total", 3, 1, dto, "method:v1:Order.GetTotal", "Order.GetTotal", parentStepId: "dto");
        var loop = new ScenarioDispatchHandlerLoop(
            "order-items",
            new FlowRegionId("flow-region:v1:order-items"),
            new FlowNodeId("flow-node:v1:order-items:header"),
            [new FlowNodeId("flow-node:v1:order-items:body")],
            [new FlowNodeId("flow-node:v1:order-items:exit")],
            new FlowEdgeId("flow-edge:v1:order-items:back"),
            "for each order item",
            [add], evidence, CertaintyLevel.Exact);
        var expansion = new ScenarioDispatchHandlerExpansion(
            new DispatchCandidate(handler, "Orders.CreateOrderDraft", true, evidence, CertaintyLevel.Exact),
            "Orders.CreateOrderDraft",
            [create, add, fromOrder, total],
            [create, add, fromOrder, total],
            [loop],
            new ScenarioDispatchHandlerReturn(
                new OperationId("operation:v1:Orders.CreateOrderDraft:return"), "OrderDraftDTO", handler, evidence,
                CertaintyLevel.Exact),
            true, [],
            [new("request", "request"), new("dispatch", "dispatch"), new("handler", "Orders.CreateOrderDraft")],
            evidence, CertaintyLevel.Exact, "dispatch-expansion");

        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:dispatch-expansion"), Profile.Id, handler, HttpMethodKind.Post,
            "/orders/draft", "POST /orders/draft", [], [], [], "dispatch-expansion", ScenarioTopology.Empty,
            dispatchHandlerExpansion: expansion);

        ScenarioDispatchHandlerStep Step(
            string id, int sourceOrdinal, int parentDepth, MethodId caller, string target, string label,
            string? loopMembershipKey = null, string? parentStepId = null)
            => new(id, sourceOrdinal, parentDepth, caller, new MethodId(target),
                new OperationId($"operation:v1:{id}"), label, loopMembershipKey, evidence, CertaintyLevel.Exact,
                parentStepId);
    }

    internal static ScenarioGraph CreateCanonicalTargetGraph(bool reversed = false)
    {
        var evidence = ImmutableArray.Create(SourceEvidence("canonical-targets"));
        var handler = new MethodId("method:v1:Handler.Handle");
        var alpha = new ScenarioDispatchHandlerStep(
            "alpha", 0, 0, handler, new MethodId("method:v1:Alpha.Widget.Send"), new OperationId("operation:v1:alpha"),
            "Alpha.Widget.Send", null, evidence, CertaintyLevel.Exact, TargetParticipantIdentity: "Alpha.Widget");
        var beta = new ScenarioDispatchHandlerStep(
            "beta", 1, 0, handler, new MethodId("method:v1:Beta.Widget.Send"), new OperationId("operation:v1:beta"),
            "Beta.Widget.Send", null, evidence, CertaintyLevel.Exact, TargetParticipantIdentity: "Beta.Widget");
        var participants = new[]
        {
            new ScenarioDispatchParticipant("alpha_widget", "Alpha.Widget", "Alpha.Widget"),
            new ScenarioDispatchParticipant("beta_widget", "Beta.Widget", "Beta.Widget"),
        };
        var expansion = new ScenarioDispatchHandlerExpansion(
            new DispatchCandidate(handler, "Handler.Handle", true, evidence, CertaintyLevel.Exact),
            "Handler.Handle", [alpha, beta], [alpha, beta], [], null, true, [], participants.ToImmutableArray(),
            evidence, CertaintyLevel.Exact, "canonical-targets");
        if (reversed)
        {
            expansion = expansion with { Participants = expansion.Participants.Reverse().ToImmutableArray() };
        }

        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:canonical-targets"), Profile.Id, handler, HttpMethodKind.Post,
            "/canonical", "POST /canonical", [], [], [], "canonical-targets", ScenarioTopology.Empty,
            dispatchHandlerExpansion: expansion);
    }

    internal static ScenarioGraph CreateMinimalApiDirectResultGraph()
        => CreateHandlerTopologyGraph(
            "direct-result",
            [],
            [new ScenarioHandlerOutcome(0, 0, true, 200, "Microsoft.AspNetCore.Http.Results.Ok", [SourceEvidence("direct-result")], CertaintyLevel.Exact)]);

    internal static ScenarioGraph CreateMinimalApiOneDecisionGraph()
    {
        var evidence = ImmutableArray.Create(SourceEvidence("one-decision"));
        return CreateHandlerTopologyGraph(
            "one-decision",
            [new ScenarioHandlerDecision(0, null, null, "value is positive", evidence, CertaintyLevel.Exact)],
            [
                new ScenarioHandlerOutcome(0, 0, true, 200, "Microsoft.AspNetCore.Http.Results.Ok", evidence, CertaintyLevel.Exact),
                new ScenarioHandlerOutcome(1, 0, false, 400, "Microsoft.AspNetCore.Http.Results.BadRequest", evidence, CertaintyLevel.Exact),
            ]);
    }

    private static ScenarioGraph CreateHandlerTopologyGraph(
        string key,
        ImmutableArray<ScenarioHandlerDecision> decisions,
        ImmutableArray<ScenarioHandlerOutcome> outcomes)
    {
        var evidence = ImmutableArray.Create(SourceEvidence(key));
        var action = Node(
            $"scenario-node:v1:{key}:action", ScenarioNodeKind.Action, "action:typed-handler", "minimal API handler", key,
            presentation: new ScenarioNodePresentation(ActionKind: ScenarioActionKind.MinimalApiHandler));
        var entry = Node(
            $"scenario-node:v1:{key}:entry", ScenarioNodeKind.EntryPoint, $"entry-point:v1:{key}", $"POST {key}", key);
        var entryEdge = Edge($"scenario-edge:v1:{key}:entry", entry, action, ScenarioEdgeKind.Entry, key);
        var topology = new ScenarioHandlerTopology([], decisions, outcomes, []);
        return new ScenarioGraph(
            new EntryPointId($"entry-point:v1:{key}"), Profile.Id, new MethodId($"method:v1:Program.{key}"),
            HttpMethodKind.Post, key, $"POST {key}", [entry, action], [entryEdge], [], $"handler:{key}",
            ScenarioTopology.Empty, handlerTopology: topology);
    }

    /// <summary>Profile anchor for the TicketReservation-shaped presentation graphs.</summary>
    internal static readonly CompilationProfile TicketPresentationProfile = CompilationProfile.Create(
        "TicketReservation.Api/TicketReservation.Api.csproj",
        "Release",
        "net10.0");

    internal static readonly EntryPointId ReserveEntryPoint = new("entry-point:v1:POST-api-Reservations");

    internal static ScenarioGraph CreateCompleteGetGraph()
        => CreateGraph(false);

    internal static ScenarioGraph CreateDegradedGuidQueryGraph()
        => CreateGraph(true);

    /// <summary>
    /// Mirrors the TicketReservation Reserve flow: DI-resolved ReservationService, Event lookup,
    /// Reservations CountAsync aggregation, Ticket Add, save, status-switch result, four status arms,
    /// and the CreatedAtAction link. Node and edge details intentionally carry the fully-qualified
    /// display strings the current planner emits so presentation readability regressions are
    /// observable at the pure wording layer.
    /// </summary>
    internal static ScenarioGraph CreateReservePresentationGraph()
    {
        const string actionMethod = "method:v1:TicketReservation.Api.Controllers.ReservationsController.ReserveTickets";
        const string serviceMethod = "method:v1:TicketReservation.Api.Services.ReservationService.ReserveAsync";

        var entry = Node(
            "scenario-node:v1:reserve:entry",
            ScenarioNodeKind.EntryPoint,
            ReserveEntryPoint.Value,
            "POST api/Reservations",
            "entry-point",
            method: actionMethod);
        var action = Node(
            "scenario-node:v1:reserve:action",
            ScenarioNodeKind.Action,
            $"action:{actionMethod}",
            "controller action",
            "action",
            method: actionMethod,
            presentation: new ScenarioNodePresentation(ControllerTypeName: "TicketReservation.Api.Controllers.ReservationsController"));
        var service = Node(
            "scenario-node:v1:reserve:service",
            ScenarioNodeKind.ServiceCall,
            $"service:{serviceMethod}",
            "resolved service implementation TicketReservation.Api.Services.ReservationService",
            "service",
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                ContractTypeName: "TicketReservation.Api.Services.IReservationService",
                ImplementationTypeName: "TicketReservation.Api.Services.ReservationService",
                CalledMemberName: "ReserveAsync"));
        var lookup = Node(
            "scenario-node:v1:reserve:query-event",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-event",
            "TicketReservation.Api.Data.AppDbContext.Microsoft.EntityFrameworkCore.DbSet<TicketReservation.Api.Models.Event> SingleOrDefaultAsync on TicketReservation.Api.Models.Event",
            "ef-query",
            sequenceOrdinal: 1,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "TicketReservation.Api.Data.AppDbContext",
                EntityTypeName: "TicketReservation.Api.Models.Event",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync));
        var count = Node(
            "scenario-node:v1:reserve:query-reservation",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:CountAsync-reservation",
            "TicketReservation.Api.Data.AppDbContext.Microsoft.EntityFrameworkCore.DbSet<TicketReservation.Api.Models.Reservation> Where,SelectMany,CountAsync on TicketReservation.Api.Models.Reservation",
            "ef-query",
            sequenceOrdinal: 2,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "TicketReservation.Api.Data.AppDbContext",
                EntityTypeName: "TicketReservation.Api.Models.Reservation",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.CountAsync));
        var mutation = Node(
            "scenario-node:v1:reserve:mutation-add-ticket",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:Add-ticket",
            "adds Ticket",
            "mutation",
            sequenceOrdinal: 3,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                EntityTypeName: "TicketReservation.Api.Models.Ticket",
                MutationKind: EntityFrameworkMutationKind.Add));
        var save = Node(
            "scenario-node:v1:reserve:save",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:SaveChanges",
            "saves changes to AppDbContext",
            "save",
            sequenceOrdinal: 4,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "TicketReservation.Api.Data.AppDbContext",
                MutationKind: EntityFrameworkMutationKind.SaveChangesAsync));
        var resultStatus = Node(
            "scenario-node:v1:reserve:result-status",
            ScenarioNodeKind.Result,
            "result-status",
            "status result of TicketReservation.Api.Models.ServiceResultStatus",
            "result-status",
            method: actionMethod);
        var outcomeNotFound = Node(
            "scenario-node:v1:reserve:outcome-404",
            ScenarioNodeKind.Outcome,
            "outcome:404:NotFound",
            "NotFound -> HTTP 404",
            "outcome-404",
            presentation: new ScenarioNodePresentation(OutcomeHelperKind: HttpOutcomeHelperKind.NotFound, OutcomeStatusCode: 404));
        var outcomeBadRequest = Node(
            "scenario-node:v1:reserve:outcome-400",
            ScenarioNodeKind.Outcome,
            "outcome:400:BadRequest",
            "BadRequest -> HTTP 400",
            "outcome-400",
            presentation: new ScenarioNodePresentation(OutcomeHelperKind: HttpOutcomeHelperKind.BadRequest, OutcomeStatusCode: 400));
        var outcomeConflict = Node(
            "scenario-node:v1:reserve:outcome-409",
            ScenarioNodeKind.Outcome,
            "outcome:409:Conflict",
            "Conflict -> HTTP 409",
            "outcome-409",
            presentation: new ScenarioNodePresentation(OutcomeHelperKind: HttpOutcomeHelperKind.Conflict, OutcomeStatusCode: 409));
        var outcomeStatus = Node(
            "scenario-node:v1:reserve:outcome-500",
            ScenarioNodeKind.Outcome,
            "outcome:500:StatusCode",
            "StatusCode -> HTTP 500",
            "outcome-500",
            presentation: new ScenarioNodePresentation(OutcomeHelperKind: HttpOutcomeHelperKind.StatusCode, OutcomeStatusCode: 500));
        var outcomeCreated = Node(
            "scenario-node:v1:reserve:outcome-201",
            ScenarioNodeKind.Outcome,
            "outcome:201:CreatedAtAction",
            "CreatedAtAction -> HTTP 201 links to GET api/Reservations/{id:guid}",
            "outcome-201",
            presentation: new ScenarioNodePresentation(
                OutcomeHelperKind: HttpOutcomeHelperKind.CreatedAtAction,
                OutcomeStatusCode: 201,
                OutcomeCreatedRoute: "GET api/Reservations/{id:guid}"));

        var nodes = ImmutableArray.Create(
            entry, action, service, lookup, count, mutation, save, resultStatus,
            outcomeNotFound, outcomeBadRequest, outcomeConflict, outcomeStatus, outcomeCreated);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:reserve:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:reserve:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through TicketReservation.Api.Services.IReservationService"),
            Edge("scenario-edge:v1:reserve:query-event", service, lookup, ScenarioEdgeKind.Query, "query", sequenceOrdinal: 1, detail: "single-or-default on TicketReservation.Api.Models.Event"),
            Edge("scenario-edge:v1:reserve:query-reservation", service, count, ScenarioEdgeKind.Query, "query", sequenceOrdinal: 2, detail: "count on TicketReservation.Api.Models.Reservation"),
            Edge("scenario-edge:v1:reserve:mutation-add-ticket", service, mutation, ScenarioEdgeKind.Mutation, "mutation", sequenceOrdinal: 3, detail: "mutates tracked entities"),
            Edge("scenario-edge:v1:reserve:save", service, save, ScenarioEdgeKind.Save, "save", sequenceOrdinal: 4, detail: "persists changes"),
            Edge("scenario-edge:v1:reserve:result-status", service, resultStatus, ScenarioEdgeKind.ResultStatus, "result-status", detail: "status result"),
            Edge("scenario-edge:v1:reserve:outcome-404", resultStatus, outcomeNotFound, ScenarioEdgeKind.OutcomeFailure, "outcome-404", detail: "NotFound outcome"),
            Edge("scenario-edge:v1:reserve:outcome-400", resultStatus, outcomeBadRequest, ScenarioEdgeKind.OutcomeFailure, "outcome-400", detail: "BadRequest outcome"),
            Edge("scenario-edge:v1:reserve:outcome-409", resultStatus, outcomeConflict, ScenarioEdgeKind.OutcomeFailure, "outcome-409", detail: "Conflict outcome"),
            Edge("scenario-edge:v1:reserve:outcome-500", resultStatus, outcomeStatus, ScenarioEdgeKind.OutcomeFailure, "outcome-500", detail: "StatusCode outcome"),
            Edge("scenario-edge:v1:reserve:outcome-201", resultStatus, outcomeCreated, ScenarioEdgeKind.OutcomeSuccess, "outcome-201", detail: "CreatedAtAction outcome"));

        return new ScenarioGraph(
            ReserveEntryPoint,
            TicketPresentationProfile.Id,
            new MethodId(actionMethod),
            HttpMethodKind.Post,
            "api/Reservations",
            "POST api/Reservations",
            nodes,
            edges,
            [],
            "scenario-graph:v1:reserve-presentation");
    }

    /// <summary>
    /// Mirrors the TicketReservation Update mutation sequence: RemoveRange, Clear, Add, then save.
    /// Every mutation edge intentionally repeats the generic "mutates tracked entities" text the
    /// current builder emits so the kind-distinction regression is observable at the pure layer.
    /// </summary>
    internal static ScenarioGraph CreateUpdatePresentationGraph()
    {
        const string actionMethod = "method:v1:TicketReservation.Api.Controllers.ReservationsController.UpdateReservation";
        const string serviceMethod = "method:v1:TicketReservation.Api.Services.ReservationService.UpdateAsync";

        var entry = Node(
            "scenario-node:v1:update:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:PUT-api-Reservations",
            "PUT api/Reservations/{id:guid}",
            "entry-point",
            method: actionMethod);
        var action = Node(
            "scenario-node:v1:update:action",
            ScenarioNodeKind.Action,
            $"action:{actionMethod}",
            "controller action",
            "action",
            method: actionMethod,
            presentation: new ScenarioNodePresentation(ControllerTypeName: "TicketReservation.Api.Controllers.ReservationsController"));
        var service = Node(
            "scenario-node:v1:update:service",
            ScenarioNodeKind.ServiceCall,
            $"service:{serviceMethod}",
            "resolved service implementation TicketReservation.Api.Services.ReservationService",
            "service",
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                ContractTypeName: "TicketReservation.Api.Services.IReservationService",
                ImplementationTypeName: "TicketReservation.Api.Services.ReservationService",
                CalledMemberName: "UpdateAsync"));
        var remove = Node(
            "scenario-node:v1:update:mutation-remove-range",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:RemoveRange-tickets",
            "removes Ticket records",
            "mutation",
            sequenceOrdinal: 1,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                EntityTypeName: "TicketReservation.Api.Models.Ticket",
                MutationKind: EntityFrameworkMutationKind.RemoveRange));
        var clear = Node(
            "scenario-node:v1:update:mutation-clear",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:Clear-tickets",
            "clears the tracked Ticket set",
            "mutation",
            sequenceOrdinal: 2,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                EntityTypeName: "TicketReservation.Api.Models.Ticket",
                MutationKind: EntityFrameworkMutationKind.Clear));
        var add = Node(
            "scenario-node:v1:update:mutation-add",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:Add-ticket",
            "adds Ticket",
            "mutation",
            sequenceOrdinal: 3,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                EntityTypeName: "TicketReservation.Api.Models.Ticket",
                MutationKind: EntityFrameworkMutationKind.Add));
        var save = Node(
            "scenario-node:v1:update:save",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:SaveChanges",
            "saves changes to AppDbContext",
            "save",
            sequenceOrdinal: 4,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "TicketReservation.Api.Data.AppDbContext",
                MutationKind: EntityFrameworkMutationKind.SaveChangesAsync));
        var resultStatus = Node(
            "scenario-node:v1:update:result-status",
            ScenarioNodeKind.Result,
            "result-status",
            "status result of TicketReservation.Api.Models.ServiceResultStatus",
            "result-status",
            method: actionMethod);
        var outcomeOk = Node(
            "scenario-node:v1:update:outcome-200",
            ScenarioNodeKind.Outcome,
            "outcome:200:Ok",
            "Ok -> HTTP 200",
            "outcome-200",
            presentation: new ScenarioNodePresentation(OutcomeHelperKind: HttpOutcomeHelperKind.Ok, OutcomeStatusCode: 200));
        var outcomeStatus = Node(
            "scenario-node:v1:update:outcome-500",
            ScenarioNodeKind.Outcome,
            "outcome:500:StatusCode",
            "StatusCode -> HTTP 500",
            "outcome-500",
            presentation: new ScenarioNodePresentation(OutcomeHelperKind: HttpOutcomeHelperKind.StatusCode, OutcomeStatusCode: 500));

        var nodes = ImmutableArray.Create(
            entry, action, service, remove, clear, add, save, resultStatus, outcomeOk, outcomeStatus);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:update:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:update:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through TicketReservation.Api.Services.IReservationService"),
            Edge("scenario-edge:v1:update:mutation-remove-range", service, remove, ScenarioEdgeKind.Mutation, "mutation", sequenceOrdinal: 1, detail: "mutates tracked entities"),
            Edge("scenario-edge:v1:update:mutation-clear", service, clear, ScenarioEdgeKind.Mutation, "mutation", sequenceOrdinal: 2, detail: "mutates tracked entities"),
            Edge("scenario-edge:v1:update:mutation-add", service, add, ScenarioEdgeKind.Mutation, "mutation", sequenceOrdinal: 3, detail: "mutates tracked entities"),
            Edge("scenario-edge:v1:update:save", service, save, ScenarioEdgeKind.Save, "save", sequenceOrdinal: 4, detail: "persists changes"),
            Edge("scenario-edge:v1:update:result-status", service, resultStatus, ScenarioEdgeKind.ResultStatus, "result-status", detail: "status result"),
            Edge("scenario-edge:v1:update:outcome-200", resultStatus, outcomeOk, ScenarioEdgeKind.OutcomeSuccess, "outcome-200", detail: "Ok outcome"),
            Edge("scenario-edge:v1:update:outcome-500", resultStatus, outcomeStatus, ScenarioEdgeKind.OutcomeFailure, "outcome-500", detail: "StatusCode outcome"));

        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:PUT-api-Reservations"),
            TicketPresentationProfile.Id,
            new MethodId(actionMethod),
            HttpMethodKind.Put,
            "api/Reservations/{id:guid}",
            "PUT api/Reservations/{id:guid}",
            nodes,
            edges,
            [],
            "scenario-graph:v1:update-presentation");
    }

    /// <summary>
    /// Two distinct symbols whose concise names collide: the DI-resolved service implementation
    /// Acme.Api.Services.WidgetService and the DbContext Acme.Api.Data.WidgetService. Both would
    /// short-name to "WidgetService", so deterministic minimal qualification must keep them distinct
    /// without exposing the full application namespace.
    /// </summary>
    internal static ScenarioGraph CreateCollisionPresentationGraph()
    {
        const string actionMethod = "method:v1:Acme.Api.Controllers.WidgetsController.Get";
        const string serviceMethod = "method:v1:Acme.Api.Services.WidgetService.GetByIdAsync";

        var entry = Node(
            "scenario-node:v1:collision:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:GET-api-Widgets",
            "GET api/Widgets",
            "entry-point",
            method: actionMethod);
        var action = Node(
            "scenario-node:v1:collision:action",
            ScenarioNodeKind.Action,
            $"action:{actionMethod}",
            "controller action",
            "action",
            method: actionMethod,
            presentation: new ScenarioNodePresentation(ControllerTypeName: "Acme.Api.Controllers.WidgetsController"));
        var service = Node(
            "scenario-node:v1:collision:service",
            ScenarioNodeKind.ServiceCall,
            $"service:{serviceMethod}",
            "resolved service implementation Acme.Api.Services.WidgetService",
            "service",
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                ContractTypeName: "Acme.Api.Services.IWidgetService",
                ImplementationTypeName: "Acme.Api.Services.WidgetService",
                CalledMemberName: "GetByIdAsync"));
        var query = Node(
            "scenario-node:v1:collision:query",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-widget",
            "Acme.Api.Data.WidgetService.Microsoft.EntityFrameworkCore.DbSet<Acme.Api.Models.Widget> AsNoTracking,SingleOrDefaultAsync on Acme.Api.Models.Widget",
            "ef-query",
            sequenceOrdinal: 1,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "Acme.Api.Data.WidgetService",
                EntityTypeName: "Acme.Api.Models.Widget",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync));

        var nodes = ImmutableArray.Create(entry, action, service, query);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:collision:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:collision:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through Acme.Api.Services.IWidgetService"),
            Edge("scenario-edge:v1:collision:query", service, query, ScenarioEdgeKind.Query, "query", sequenceOrdinal: 1, detail: "single-or-default on Acme.Api.Models.Widget"));

        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:GET-api-Widgets"),
            TicketPresentationProfile.Id,
            new MethodId(actionMethod),
            HttpMethodKind.Get,
            "api/Widgets",
            "GET api/Widgets",
            nodes,
            edges,
            [],
            "scenario-graph:v1:collision-presentation");
    }

    /// <summary>
    /// Mirrors a CountAsync aggregation on the consonant+y entity Category. The graph carries the
    /// fully-qualified DbSet display string the current builder emits so the deterministic
    /// pluralization defect (Count Categorys instead of Count Categories) is observable at the pure
    /// wording layer without inventing a broad natural-language engine.
    /// </summary>
    internal static ScenarioGraph CreateCategoryCountPresentationGraph()
    {
        const string actionMethod = "method:v1:TicketReservation.Api.Controllers.CategoriesController.List";
        const string serviceMethod = "method:v1:TicketReservation.Api.Services.CategoryService.ListAsync";

        var entry = Node(
            "scenario-node:v1:category:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:GET-api-Categories",
            "GET api/Categories",
            "entry-point",
            method: actionMethod);
        var action = Node(
            "scenario-node:v1:category:action",
            ScenarioNodeKind.Action,
            $"action:{actionMethod}",
            "controller action",
            "action",
            method: actionMethod,
            presentation: new ScenarioNodePresentation(ControllerTypeName: "TicketReservation.Api.Controllers.CategoriesController"));
        var service = Node(
            "scenario-node:v1:category:service",
            ScenarioNodeKind.ServiceCall,
            $"service:{serviceMethod}",
            "resolved service implementation TicketReservation.Api.Services.CategoryService",
            "service",
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                ContractTypeName: "TicketReservation.Api.Services.ICategoryService",
                ImplementationTypeName: "TicketReservation.Api.Services.CategoryService",
                CalledMemberName: "ListAsync"));
        var count = Node(
            "scenario-node:v1:category:query-count",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:CountAsync-category",
            "TicketReservation.Api.Data.AppDbContext.Microsoft.EntityFrameworkCore.DbSet<TicketReservation.Api.Models.Category> CountAsync on TicketReservation.Api.Models.Category",
            "ef-query",
            sequenceOrdinal: 1,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "TicketReservation.Api.Data.AppDbContext",
                EntityTypeName: "TicketReservation.Api.Models.Category",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.CountAsync));

        var nodes = ImmutableArray.Create(entry, action, service, count);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:category:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:category:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through TicketReservation.Api.Services.ICategoryService"),
            Edge("scenario-edge:v1:category:query-count", service, count, ScenarioEdgeKind.Query, "query", sequenceOrdinal: 1, detail: "count on TicketReservation.Api.Models.Category"));

        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:GET-api-Categories"),
            TicketPresentationProfile.Id,
            new MethodId(actionMethod),
            HttpMethodKind.Get,
            "api/Categories",
            "GET api/Categories",
            nodes,
            edges,
            [],
            "scenario-graph:v1:category-count-presentation");
    }

    /// <summary>
    /// Conflicting-detail adversarial graph: typed presentation facts disagree with the node detail
    /// strings. The planner must build primary labels and classifications from the typed facts only
    /// (outcome helper/status/created route and mutation/save kind), and nodes without typed facts
    /// must receive a neutral technical fallback that never leaks the internal "resolved service
    /// implementation" phrase or the application namespace.
    /// </summary>
    internal static ScenarioGraph CreateTypedFactsOverrideDetailGraph()
    {
        const string actionMethod = "method:v1:Acme.Api.Controllers.WidgetsController.Reserve";
        const string serviceMethod = "method:v1:Acme.Api.Services.WidgetService.ReserveAsync";

        var entry = Node(
            "scenario-node:v1:override:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:POST-api-Widgets",
            "POST api/Widgets",
            "entry-point",
            method: actionMethod);
        var action = Node(
            "scenario-node:v1:override:action",
            ScenarioNodeKind.Action,
            $"action:{actionMethod}",
            "controller action",
            "action",
            method: actionMethod,
            presentation: new ScenarioNodePresentation(ControllerTypeName: "Acme.Api.Controllers.WidgetsController"));
        // No typed presentation facts: the fallback must be neutral and never leak the detail.
        var service = Node(
            "scenario-node:v1:override:service",
            ScenarioNodeKind.ServiceCall,
            $"service:{serviceMethod}",
            "resolved service implementation Acme.Internal.LeakyService",
            "service",
            method: serviceMethod);
        // Typed kind Add but the detail claims a save; the typed mutation kind must win.
        var mutation = Node(
            "scenario-node:v1:override:mutation",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:Add-widget",
            "saves changes to AppDbContext",
            "mutation",
            sequenceOrdinal: 1,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                EntityTypeName: "Acme.Api.Models.Widget",
                MutationKind: EntityFrameworkMutationKind.Add));
        var resultStatus = Node(
            "scenario-node:v1:override:result-status",
            ScenarioNodeKind.Result,
            "result-status",
            "status result of Acme.Api.Models.ServiceResultStatus",
            "result-status",
            method: actionMethod);
        // Typed helper/status disagree with the detail; typed facts must win.
        var outcomeNotFound = Node(
            "scenario-node:v1:override:outcome-404",
            ScenarioNodeKind.Outcome,
            "outcome:404:NotFound",
            "Ok -> HTTP 999 links to GET api/Evil",
            "outcome-404",
            presentation: new ScenarioNodePresentation(
                OutcomeHelperKind: HttpOutcomeHelperKind.NotFound,
                OutcomeStatusCode: 404));
        // Typed created route disagrees with the detail route; typed facts must win.
        var outcomeCreated = Node(
            "scenario-node:v1:override:outcome-201",
            ScenarioNodeKind.Outcome,
            "outcome:201:CreatedAtAction",
            "CreatedAtAction -> HTTP 999 links to GET api/Evil",
            "outcome-201",
            presentation: new ScenarioNodePresentation(
                OutcomeHelperKind: HttpOutcomeHelperKind.CreatedAtAction,
                OutcomeStatusCode: 201,
                OutcomeCreatedRoute: "GET api/Widgets/{id}"));

        var nodes = ImmutableArray.Create(entry, action, service, mutation, resultStatus, outcomeNotFound, outcomeCreated);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:override:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:override:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through Acme.Api.Services.IWidgetService"),
            Edge("scenario-edge:v1:override:mutation", service, mutation, ScenarioEdgeKind.Mutation, "mutation", sequenceOrdinal: 1, detail: "mutates tracked entities"),
            Edge("scenario-edge:v1:override:result-status", service, resultStatus, ScenarioEdgeKind.ResultStatus, "result-status", detail: "status result"),
            Edge("scenario-edge:v1:override:outcome-404", resultStatus, outcomeNotFound, ScenarioEdgeKind.OutcomeFailure, "outcome-404", detail: "NotFound outcome"),
            Edge("scenario-edge:v1:override:outcome-201", resultStatus, outcomeCreated, ScenarioEdgeKind.OutcomeSuccess, "outcome-201", detail: "CreatedAtAction outcome"));

        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:POST-api-Widgets"),
            TicketPresentationProfile.Id,
            new MethodId(actionMethod),
            HttpMethodKind.Post,
            "api/Widgets",
            "POST api/Widgets",
            nodes,
            edges,
            [],
            "scenario-graph:v1:typed-facts-override-detail");
    }

    /// <summary>
    /// Collision-group adversarial graph: the DI-resolved implementation and the DbContext are
    /// constructed generic canonical type names that share the same type-argument fragment suffix and
    /// the same structural short name. The planner must qualify only the colliding group without
    /// expanding the unrelated controller participant, and must parse canonical generic names
    /// structurally so no label ever becomes a type-argument fragment ("Widget>") or exposes metadata
    /// arity ("`1").
    /// </summary>
    internal static ScenarioGraph CreateCollisionGroupLocalGenericGraph()
    {
        const string actionMethod = "method:v1:Acme.Api.Controllers.WidgetsController.Get";
        const string serviceMethod = "method:v1:Acme.Api.Services.WidgetService.GetByIdAsync";
        const string genericTypeName = "WidgetService`1<Acme.Api.Models.Widget>";

        var entry = Node(
            "scenario-node:v1:generic-collision:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:GET-api-Widgets",
            "GET api/Widgets",
            "entry-point",
            method: actionMethod);
        var action = Node(
            "scenario-node:v1:generic-collision:action",
            ScenarioNodeKind.Action,
            $"action:{actionMethod}",
            "controller action",
            "action",
            method: actionMethod,
            presentation: new ScenarioNodePresentation(ControllerTypeName: "Acme.Api.Controllers.WidgetsController"));
        var service = Node(
            "scenario-node:v1:generic-collision:service",
            ScenarioNodeKind.ServiceCall,
            $"service:{serviceMethod}",
            $"resolved service implementation Acme.Api.Services.{genericTypeName}",
            "service",
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                ContractTypeName: "Acme.Api.Services.IWidgetService",
                ImplementationTypeName: $"Acme.Api.Services.{genericTypeName}",
                CalledMemberName: "GetByIdAsync"));
        var query = Node(
            "scenario-node:v1:generic-collision:query",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-widget",
            $"Acme.Api.Data.{genericTypeName}.Microsoft.EntityFrameworkCore.DbSet<Acme.Api.Models.Widget> SingleOrDefaultAsync on Acme.Api.Models.Widget",
            "ef-query",
            sequenceOrdinal: 1,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: $"Acme.Api.Data.{genericTypeName}",
                EntityTypeName: "Acme.Api.Models.Widget",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync));

        var nodes = ImmutableArray.Create(entry, action, service, query);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:generic-collision:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:generic-collision:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through Acme.Api.Services.IWidgetService"),
            Edge("scenario-edge:v1:generic-collision:query", service, query, ScenarioEdgeKind.Query, "query", sequenceOrdinal: 1, detail: "single-or-default on Acme.Api.Models.Widget"));

        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:GET-api-Widgets"),
            TicketPresentationProfile.Id,
            new MethodId(actionMethod),
            HttpMethodKind.Get,
            "api/Widgets",
            "GET api/Widgets",
            nodes,
            edges,
            [],
            "scenario-graph:v1:generic-collision-presentation");
    }

    /// <summary>
    /// Unsupported pluralization adversarial graph: CountAsync aggregations over Box, Class, and
    /// Status entities. No supported English plural form is proven for these names, so the planner
    /// must emit an honest neutral label ("Count items of type Box") instead of the invalid plain -s
    /// forms ("Boxs", "Classs", "Statuss").
    /// </summary>
    internal static ScenarioGraph CreateUnsupportedPluralPresentationGraph()
    {
        const string actionMethod = "method:v1:TicketReservation.Api.Controllers.WidgetsController.List";
        const string serviceMethod = "method:v1:TicketReservation.Api.Services.WidgetService.ListAsync";

        var entry = Node(
            "scenario-node:v1:unsupported-plural:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:GET-api-Widgets",
            "GET api/Widgets",
            "entry-point",
            method: actionMethod);
        var action = Node(
            "scenario-node:v1:unsupported-plural:action",
            ScenarioNodeKind.Action,
            $"action:{actionMethod}",
            "controller action",
            "action",
            method: actionMethod,
            presentation: new ScenarioNodePresentation(ControllerTypeName: "TicketReservation.Api.Controllers.WidgetsController"));
        var service = Node(
            "scenario-node:v1:unsupported-plural:service",
            ScenarioNodeKind.ServiceCall,
            $"service:{serviceMethod}",
            "resolved service implementation TicketReservation.Api.Services.WidgetService",
            "service",
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                ContractTypeName: "TicketReservation.Api.Services.IWidgetService",
                ImplementationTypeName: "TicketReservation.Api.Services.WidgetService",
                CalledMemberName: "ListAsync"));
        var countBox = Node(
            "scenario-node:v1:unsupported-plural:query-box",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:CountAsync-box",
            "TicketReservation.Api.Data.AppDbContext.Microsoft.EntityFrameworkCore.DbSet<TicketReservation.Api.Models.Box> CountAsync on TicketReservation.Api.Models.Box",
            "ef-query",
            sequenceOrdinal: 1,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "TicketReservation.Api.Data.AppDbContext",
                EntityTypeName: "TicketReservation.Api.Models.Box",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.CountAsync));
        var countClass = Node(
            "scenario-node:v1:unsupported-plural:query-class",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:CountAsync-class",
            "TicketReservation.Api.Data.AppDbContext.Microsoft.EntityFrameworkCore.DbSet<TicketReservation.Api.Models.Class> CountAsync on TicketReservation.Api.Models.Class",
            "ef-query",
            sequenceOrdinal: 2,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "TicketReservation.Api.Data.AppDbContext",
                EntityTypeName: "TicketReservation.Api.Models.Class",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.CountAsync));
        var countStatus = Node(
            "scenario-node:v1:unsupported-plural:query-status",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:CountAsync-status",
            "TicketReservation.Api.Data.AppDbContext.Microsoft.EntityFrameworkCore.DbSet<TicketReservation.Api.Models.Status> CountAsync on TicketReservation.Api.Models.Status",
            "ef-query",
            sequenceOrdinal: 3,
            method: serviceMethod,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "TicketReservation.Api.Data.AppDbContext",
                EntityTypeName: "TicketReservation.Api.Models.Status",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.CountAsync));

        var nodes = ImmutableArray.Create(entry, action, service, countBox, countClass, countStatus);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:unsupported-plural:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:unsupported-plural:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through TicketReservation.Api.Services.IWidgetService"),
            Edge("scenario-edge:v1:unsupported-plural:query-box", service, countBox, ScenarioEdgeKind.Query, "query", sequenceOrdinal: 1, detail: "count on TicketReservation.Api.Models.Box"),
            Edge("scenario-edge:v1:unsupported-plural:query-class", service, countClass, ScenarioEdgeKind.Query, "query", sequenceOrdinal: 2, detail: "count on TicketReservation.Api.Models.Class"),
            Edge("scenario-edge:v1:unsupported-plural:query-status", service, countStatus, ScenarioEdgeKind.Query, "query", sequenceOrdinal: 3, detail: "count on TicketReservation.Api.Models.Status"));

        return new ScenarioGraph(
            new EntryPointId("entry-point:v1:GET-api-Widgets"),
            TicketPresentationProfile.Id,
            new MethodId(actionMethod),
            HttpMethodKind.Get,
            "api/Widgets",
            "GET api/Widgets",
            nodes,
            edges,
            [],
            "scenario-graph:v1:unsupported-plural-presentation");
    }

    private static ScenarioGraph CreateGraph(bool degradedQuery)
    {
        var entry = Node(
            "scenario-node:v1:test:entry",
            ScenarioNodeKind.EntryPoint,
            GetEntryPoint.Value,
            "GET api/Gadgets/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:test:action",
            ScenarioNodeKind.Action,
            "action:method:v1:GetMeaning.Controllers.GadgetsController.GetById",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:test:service",
            ScenarioNodeKind.ServiceCall,
            "service:method:v1:GetMeaning.Services.GadgetService.GetByIdAsync",
            "resolved service implementation GetMeaning.Services.GadgetService",
            "service");
        var query = Node(
            "scenario-node:v1:test:query",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync",
            "GetMeaning.Data.GadgetDbContext DbSet<GetMeaning.Models.Gadget> AsNoTracking,Include,Include,SingleOrDefaultAsync on GetMeaning.Models.Gadget",
            "ef-query",
            degradedQuery ? CertaintyLevel.Conservative : CertaintyLevel.Exact);
        var resultSuccess = Node(
            "scenario-node:v1:test:result-success",
            ScenarioNodeKind.Result,
            "result-success",
            "success result with data of GetMeaning.Services.GadgetResult<GetMeaning.Models.Gadget>",
            "result-success");
        var resultFailure = Node(
            "scenario-node:v1:test:result-failure",
            ScenarioNodeKind.Result,
            "result-failure",
            "failure result with status NotFound of GetMeaning.Services.GadgetResult<GetMeaning.Models.Gadget>",
            "result-failure");
        var outcomeOk = Node(
            "scenario-node:v1:test:outcome-200",
            ScenarioNodeKind.Outcome,
            "outcome:200:Ok",
            "Ok -> HTTP 200",
            "outcome-ok");
        var outcomeNotFound = Node(
            "scenario-node:v1:test:outcome-404",
            ScenarioNodeKind.Outcome,
            "outcome:404:NotFound",
            "NotFound -> HTTP 404",
            "outcome-not-found");

        var nodes = ImmutableArray.Create(entry, action, service, query, resultSuccess, resultFailure, outcomeOk, outcomeNotFound);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:test:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:test:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:test:query", service, query, ScenarioEdgeKind.Query, "query", degradedQuery ? CertaintyLevel.Conservative : CertaintyLevel.Exact),
            Edge("scenario-edge:v1:test:result-success", service, resultSuccess, ScenarioEdgeKind.ResultSuccess, "result-success"),
            Edge("scenario-edge:v1:test:result-failure", service, resultFailure, ScenarioEdgeKind.ResultFailure, "result-failure"),
            Edge("scenario-edge:v1:test:outcome-success", resultSuccess, outcomeOk, ScenarioEdgeKind.OutcomeSuccess, "outcome-success"),
            Edge("scenario-edge:v1:test:outcome-failure", resultFailure, outcomeNotFound, ScenarioEdgeKind.OutcomeFailure, "outcome-failure"));

        var diagnostics = degradedQuery
            ? ImmutableArray.Create(new ScenarioGraphDiagnostic(
                new DiagnosticId("diagnostic:v1:test:SC005"),
                "SC005",
                "The EF query predicate comparison has no linked comparison semantic fact.",
                "query detail withheld"))
            : [];

        return new ScenarioGraph(
            GetEntryPoint,
            Profile.Id,
            new MethodId("method:v1:GetMeaning.Controllers.GadgetsController.GetById"),
            HttpMethodKind.Get,
            "api/Gadgets/{id}",
            "GET api/Gadgets/{id}",
            nodes,
            edges,
            diagnostics,
            "scenario-graph:v1:test");
    }

    private static ScenarioNode Node(
        string id,
        ScenarioNodeKind kind,
        string key,
        string detail,
        string artifact,
        CertaintyLevel certainty = CertaintyLevel.Exact,
        int sequenceOrdinal = 0,
        string? method = null,
        ScenarioNodePresentation? presentation = null)
        => new(
            new ScenarioNodeId(id),
            kind,
            key,
            method is null ? null : new MethodId(method),
            null,
            detail,
            [SourceEvidence(artifact)],
            certainty,
            sequenceOrdinal,
            presentation);

    private static ScenarioEdge Edge(
        string id,
        ScenarioNode source,
        ScenarioNode target,
        ScenarioEdgeKind kind,
        string artifact,
        CertaintyLevel certainty = CertaintyLevel.Exact,
        int sequenceOrdinal = 0,
        string? detail = null)
        => new(
            new ScenarioEdgeId(id),
            source.Id,
            target.Id,
            kind,
            detail ?? artifact,
            [SourceEvidence(artifact)],
            certainty,
            sequenceOrdinal);

    internal static EvidenceRef SourceEvidence(string artifact, CertaintyLevel certainty = CertaintyLevel.Exact)
        => new(
            new EvidenceId($"evidence:v1:{artifact}"),
            EvidenceKind.Source,
            artifact,
            new SourceRange(
                new DocumentId("document:v1:test"),
                new SourcePosition(1, 0),
                new SourcePosition(1, 10)),
            "test-symbol",
            null,
            certainty);
}
