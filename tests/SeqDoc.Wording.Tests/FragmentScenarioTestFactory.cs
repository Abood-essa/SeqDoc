using System.Collections.Immutable;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Wording.Tests;

/// <summary>
/// Builds hand-authored Scenario Graphs with explicit accepted contract decision topologies so accepted contract fragment
/// planning tests run as a small pure layer. The graph shapes mirror the reviewed accepted contract WorkItem
/// absent/locked chain and the admitted decision partitions (terminating guard, one-sided material,
/// both-material failure/success) without requiring a compiler or topology-builder session.
/// Identities are stable test anchors and evidence is source-shaped and deterministic.
///
/// The planner contract under test (from contract stage accepted contract "Required Model"):
/// <code>
/// public enum DiagramFragmentKind { Alt, Opt, Break, Loop }
/// public sealed record DiagramAltArm(
///     DiagramPlanElementId Id, string Key, string Label, bool IsElse,
///     ImmutableArray&lt;DiagramPlanElementId&gt; MessageRefs,
///     ImmutableArray&lt;DiagramFragment&gt; Fragments,
///     ImmutableArray&lt;EvidenceRef&gt; Evidence, CertaintyLevel Certainty);
/// public sealed record DiagramFragment(
///     DiagramPlanElementId Id, string Key, string Label, DiagramFragmentKind Kind,
///     ImmutableArray&lt;DiagramAltArm&gt; Arms,
///     ImmutableArray&lt;DiagramPlanElementId&gt; MessageRefs,
///     ImmutableArray&lt;DiagramFragment&gt; Fragments,
///     ImmutableArray&lt;EvidenceRef&gt; Evidence, CertaintyLevel Certainty);
/// public sealed record DiagramSequence(
///     ImmutableArray&lt;DiagramPlanElementId&gt; MessageRefs,
///     ImmutableArray&lt;DiagramFragment&gt; Fragments);
/// public sealed record DiagramPlanDiagnostic(
///     DiagnosticId Id, string Code, string Summary, string Detail);
/// </code>
/// plus <c>DiagramPlan.Sequence</c> and <c>DiagramPlan.Diagnostics</c> (legacy 7-argument
/// construction yields a non-null empty sequence and empty diagnostics).
///
/// Planner derivation rules pinned by these graphs: fragments are derived only from topology
/// membership containment and <see cref="ScenarioArmTerminal"/> kinds, never from labels or
/// traversal order; a Terminates arm becomes an Alt arm holding one Break fragment; an Alt arm that
/// is not first in visual order is marked <c>IsElse</c>; a decision with one material arm and one
/// empty Rejoins arm becomes an Opt with no arms; an Unknown/SC013 decision produces no fragment.
/// The F1-F7 review graphs additionally pin ordered element placement, explicit Alt roles,
/// fail-closed reference coverage, mixed-certainty evidence combination, equal/ambiguous membership
/// containment, and stable-identity ID scoping.
/// </summary>
internal static class FragmentScenarioTestFactory
{
    internal static class PredicateWordingTestFactory
    {
        internal static PredicateExpression Create(string partition)
            => partition switch
            {
                "backticks in string" => Compare(Symbol("reservation"), PredicateExpressionKind.StringConstant, "a`b```c"),
                "backticks in char" => Compare(Symbol("reservation"), PredicateExpressionKind.CharacterConstant, "`"),
                "null/member" => Compare(Symbol("reservation"), PredicateExpressionKind.NullConstant, null),
                "enum/string/char/constants" => Logical(
                    Compare(Symbol("status"), PredicateExpressionKind.EnumConstant, "Cancelled"),
                    Logical(Compare(Symbol("name"), PredicateExpressionKind.StringConstant, "a\n\"b"),
                        Logical(Compare(Symbol("marker"), PredicateExpressionKind.CharacterConstant, "\n"), Compare(Symbol("count"), PredicateExpressionKind.NumericConstant, "2")))),
                "arithmetic precedence" => new(PredicateExpressionKind.Comparison, [Arithmetic(Symbol("requestedCount"), PredicateArithmeticOperatorKind.Add,
                    Arithmetic(Numeric("1"), PredicateArithmeticOperatorKind.Multiply, Symbol("pageSize"))), Symbol("remainingCapacity")], "System.Boolean", PredicateComparisonOperatorKind.GreaterThan),
                "logical parentheses" => new(PredicateExpressionKind.LogicalOr, [Symbol("ready"), Logical(Symbol("enabled"), Compare(Symbol("count"), PredicateExpressionKind.NumericConstant, "0"))], "System.Boolean"),
                "negation" => new(PredicateExpressionKind.Negation, [Compare(Symbol("reservation"), PredicateExpressionKind.NullConstant, null)], "System.Boolean"),
                _ => CreateUnsupported(),
            };

        internal static PredicateExpression CreateComparison(string comparison)
            => new(PredicateExpressionKind.Comparison, [Symbol("status"), new(PredicateExpressionKind.EnumConstant, [], "Status", constantValue: "Cancelled")], "System.Boolean",
                Enum.Parse<PredicateComparisonOperatorKind>(comparison));

        internal static PredicateExpression CreateGrouped()
            => Logical(Symbol("ready"), Symbol("enabled"));

        internal static PredicateExpression CreateUnsupported()
            => new(PredicateExpressionKind.OpaqueValue, [], "System.DateTime", displayName: "DateTime.UtcNow");

        internal static PredicateExpression FormatSubordinate() => Create("null/member");

        internal static ScenarioPredicateWording CreatePresentation(ScenarioPredicateWordingRole role)
            => CreatePresentation(role, "null/member");

        internal static ScenarioPredicateWording CreatePresentation(ScenarioPredicateWordingRole role, string partition)
            => new(
                new SemanticFactId("semantic-fact:v1:predicate:wording"),
                Create(partition),
                role,
                [ScenarioGraphTestFactory.SourceEvidence("predicate")],
                CertaintyLevel.Exact);

        private static PredicateExpression Symbol(string name)
            => new(PredicateExpressionKind.SymbolValue, [], "System.Boolean", displayName: name);

        private static PredicateExpression Numeric(string value)
            => new(PredicateExpressionKind.NumericConstant, [], "System.Int32", constantValue: value);

        private static PredicateExpression Compare(PredicateExpression left, PredicateExpressionKind rightKind, string? value)
            => new(PredicateExpressionKind.Comparison, [left, rightKind == PredicateExpressionKind.NullConstant
                ? new(PredicateExpressionKind.NullConstant, [], "System.Object")
                : new(rightKind, [], rightKind == PredicateExpressionKind.EnumConstant ? "Status" : "System.String", constantValue: value)],
                "System.Boolean", PredicateComparisonOperatorKind.Equal);

        private static PredicateExpression Logical(PredicateExpression left, PredicateExpression right)
            => new(PredicateExpressionKind.LogicalAnd, [left, right], "System.Boolean");

        private static PredicateExpression Arithmetic(PredicateExpression left, PredicateArithmeticOperatorKind op, PredicateExpression right)
            => new(PredicateExpressionKind.BinaryArithmetic, [left, right], "System.Int32", arithmeticOperator: op);
    }
    internal static readonly EntryPointId WorkItemEntryPoint = new("entry-point:v1:GET-api-WorkItems");
    internal static readonly MethodId ActionMethod = new("method:v1:Acme.Controllers.WorkItemsController.Get");
    internal static readonly OperationId AbsentCondition = new("operation:v1:decision.WorkItemAbsent");
    internal static readonly OperationId LockedCondition = new("operation:v1:decision.WorkItemLocked");

    /// <summary>Both-material Alt condition whose failure-arm memberships are Conservative.</summary>
    internal static readonly OperationId MixedDecisionCondition = new("operation:v1:decision.Mixed");

    /// <summary>Two unrelated one-sided decisions guarding the exact same message set.</summary>
    internal static readonly OperationId EqualAlphaCondition = new("operation:v1:decision.EqualAlpha");
    internal static readonly OperationId EqualBetaCondition = new("operation:v1:decision.EqualBeta");

    /// <summary>Ambiguous-parent topology: the child set is contained in two minimal parent arms.</summary>
    internal static readonly OperationId GuardP1Condition = new("operation:v1:decision.GuardP1");
    internal static readonly OperationId GuardP2Condition = new("operation:v1:decision.GuardP2");
    internal static readonly OperationId GuardCCondition = new("operation:v1:decision.GuardC");

    /// <summary>
    /// Mirrors the accepted contract WorkItem absent/locked chain: the absent true arm terminates, the locked true
    /// arm terminates, and the continuing path (query-before, query2, state assignment, save) is a
    /// member of the absent false arm while only (query2, state assignment, save) is a member of the
    /// locked false arm. The absent false arm is therefore a genuine proper superset of the locked
    /// decision's membership set, so the locked decision nests unambiguously inside the absent false
    /// arm under proper minimal containment. Entry, call, and query1 are unscoped pre-decision facts.
    /// When <paramref name="reverseConstruction"/> is set, every topology array is supplied in
    /// reversed order to prove identity/order stability. The optional
    /// <paramref name="profileId"/>/<paramref name="entryPointId"/> parameters produce the same
    /// topology under a different Diagram Plan identity scope so stable-identity profile isolation is
    /// observable.
    /// </summary>
    internal static ScenarioGraph CreateNestedAbsentLockedGraph(
        bool reverseConstruction = false,
        CompilationProfileId? profileId = null,
        EntryPointId? entryPointId = null,
        ScenarioPredicateWordingRole? predicateRole = null,
        string predicatePartition = "null/member")
    {
        var entry = Node(
            "scenario-node:v1:workitem:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/WorkItems/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:workitem:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:workitem:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation WorkItemService",
            "service");
        var queryBefore = Node(
            "scenario-node:v1:workitem:query-before",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-workitem-before",
            "find work item before guard",
            "ef-query-before");
        var query1 = Node(
            "scenario-node:v1:workitem:query1",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-workitem",
            "find work item",
            "ef-query-1",
            sequenceOrdinal: 1);
        var query2 = Node(
            "scenario-node:v1:workitem:query2",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:CountAsync-workitem",
            "count work items",
            "ef-query-2",
            sequenceOrdinal: 2);
        var state = Node(
            "scenario-node:v1:workitem:state",
            ScenarioNodeKind.StateAssignment,
            "state:operation:v1:Assign",
            "assigns state",
            "state",
            sequenceOrdinal: 3);
        var save = Node(
            "scenario-node:v1:workitem:save",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:SaveChanges",
            "saves changes",
            "save",
            sequenceOrdinal: 4);

        var nodes = ImmutableArray.Create(entry, action, service, queryBefore, query1, query2, state, save);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:workitem:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:workitem:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:workitem:query-before", service, queryBefore, ScenarioEdgeKind.Query, "query-before", sequenceOrdinal: 0),
            Edge("scenario-edge:v1:workitem:query1", service, query1, ScenarioEdgeKind.Query, "query1", sequenceOrdinal: 1),
            Edge("scenario-edge:v1:workitem:query2", service, query2, ScenarioEdgeKind.Query, "query2", sequenceOrdinal: 2),
            Edge("scenario-edge:v1:workitem:state", service, state, ScenarioEdgeKind.StateAssignment, "state", sequenceOrdinal: 3),
            Edge("scenario-edge:v1:workitem:save", service, save, ScenarioEdgeKind.Save, "save", sequenceOrdinal: 4));

        var absent = Decision(
            "scenario-decision:v1:workitem:absent",
            AbsentCondition,
            "flow:v1:absent",
            predicateRole,
            predicatePartition);
        var locked = Decision(
            "scenario-decision:v1:workitem:locked",
            LockedCondition,
            "flow:v1:locked");
        var absentTrue = Arm("scenario-arm:v1:workitem:absent:true", absent.Id, IsTrue: true);
        var absentFalse = Arm("scenario-arm:v1:workitem:absent:false", absent.Id, IsTrue: false);
        var lockedTrue = Arm("scenario-arm:v1:workitem:locked:true", locked.Id, IsTrue: true);
        var lockedFalse = Arm("scenario-arm:v1:workitem:locked:false", locked.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:workitem:absent:false:query-before", absentFalse.Id, queryBefore),
            Membership("scenario-membership:v1:workitem:absent:false:query2", absentFalse.Id, query2),
            Membership("scenario-membership:v1:workitem:absent:false:state", absentFalse.Id, state),
            Membership("scenario-membership:v1:workitem:absent:false:save", absentFalse.Id, save),
            Membership("scenario-membership:v1:workitem:locked:false:query2", lockedFalse.Id, query2),
            Membership("scenario-membership:v1:workitem:locked:false:state", lockedFalse.Id, state),
            Membership("scenario-membership:v1:workitem:locked:false:save", lockedFalse.Id, save));

        var terminals = ImmutableArray.Create(
            Terminal(absentTrue.Id, ScenarioTerminalKind.Terminates),
            Terminal(absentFalse.Id, ScenarioTerminalKind.Rejoins),
            Terminal(lockedTrue.Id, ScenarioTerminalKind.Terminates),
            Terminal(lockedFalse.Id, ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            "scenario-graph:v1:workitem-nested",
            nodes,
            edges,
            [],
            BuildTopology(
                [absent, locked],
                [absentTrue, absentFalse, lockedTrue, lockedFalse],
                memberships,
                terminals,
                reverseConstruction),
            profileId,
            entryPointId);
    }

    /// <summary>CR-3 synthetic owner with two nested/contiguous subordinate decisions sharing one exact predicate fact.</summary>
    internal static ScenarioGraph CreatePredicateOwnerGroupGraph(
        bool ambiguousOwners = false,
        bool reverseConstruction = false,
        bool subordinateHasTerminatingArm = false)
    {
        var baseGraph = CreateNestedAbsentLockedGraph();
        var baseTopology = baseGraph.Topology;
        var predicateId = new SemanticFactId("semantic-fact:v1:predicate:owner-group");
        var owner = WithPredicateWording(baseTopology.Decisions[0], predicateId, ScenarioPredicateWordingRole.Owner, "owner-decision");
        var firstSubordinate = WithPredicateWording(baseTopology.Decisions[1], predicateId,
            ambiguousOwners ? ScenarioPredicateWordingRole.Owner : ScenarioPredicateWordingRole.Subordinate,
            "subordinate-decision");

        var extraDecision = Decision(
            "scenario-decision:v1:workitem:subordinate-2",
            LockedCondition,
            "flow:v1:locked-subordinate-2",
            ScenarioPredicateWordingRole.Subordinate,
            "null/member",
            "subordinate-decision");
        extraDecision = WithPredicateWording(
            extraDecision,
            predicateId,
            ScenarioPredicateWordingRole.Subordinate,
            "subordinate-decision");
        var extraTrue = Arm("scenario-arm:v1:workitem:subordinate-2:true", extraDecision.Id, true);
        var extraFalse = Arm("scenario-arm:v1:workitem:subordinate-2:false", extraDecision.Id, false);
        var save = baseGraph.Nodes.Single(node => node.Id.Value.EndsWith(":save", StringComparison.Ordinal));
        var extraMembership = Membership(
            "scenario-membership:v1:workitem:subordinate-2:false:save",
            extraFalse.Id,
            save,
            CertaintyLevel.Exact,
            "subordinate-membership");
        var extraTerminals = ImmutableArray.Create(
            Terminal(extraTrue.Id, ScenarioTerminalKind.Terminates),
            Terminal(extraFalse.Id, ScenarioTerminalKind.Rejoins));

        var decisions = ImmutableArray.Create(owner, firstSubordinate, extraDecision);
        var arms = baseTopology.Arms.Add(extraTrue).Add(extraFalse);
        var memberships = baseTopology.Memberships.Add(extraMembership);
        // CR-3 safe groups may be collapsed only when subordinate arms do not terminate.  The
        // terminating variant is a valid regression shape: its Break must remain conditional.
        var terminals = baseTopology.Terminals.AddRange(extraTerminals);
        if (!subordinateHasTerminatingArm)
        {
            terminals = terminals
                .Select(terminal => terminal.Arm == baseTopology.Arms[2].Id
                    ? Terminal(terminal.Arm, ScenarioTerminalKind.Rejoins)
                    : terminal)
                .ToImmutableArray();
            terminals = terminals
                .Select(terminal => terminal.Arm == extraTrue.Id
                    ? Terminal(terminal.Arm, ScenarioTerminalKind.Rejoins)
                    : terminal)
                .ToImmutableArray();
        }
        var topology = BuildTopology(decisions, arms, memberships, terminals, reverseConstruction);
        return new ScenarioGraph(
            baseGraph.EntryPoint,
            baseGraph.Profile,
            baseGraph.RootMethod,
            baseGraph.HttpMethod,
            baseGraph.CanonicalRoute,
            baseGraph.OperationKey,
            baseGraph.Nodes,
            baseGraph.Edges,
            baseGraph.Diagnostics,
            baseGraph.DebugProjection,
            topology,
            baseGraph.Composition,
            baseGraph.CallbackRegions);
    }

    private static ScenarioDecision WithPredicateWording(
        ScenarioDecision decision,
        SemanticFactId predicateId,
        ScenarioPredicateWordingRole role,
        string evidenceArtifact)
        => new(
            decision.Id,
            decision.Method,
            decision.ControllingFlowNode,
            decision.Condition,
            decision.Evidence,
            decision.Certainty,
            new ScenarioPredicateWording(
                predicateId,
                PredicateWordingTestFactory.Create("null/member"),
                role,
                [ScenarioGraphTestFactory.SourceEvidence(evidenceArtifact)],
                CertaintyLevel.Exact));

    /// <summary>
    /// One-sided decision: the true arm is empty and rejoins, the false arm carries the material
    /// continuing-path messages (query2 and save). The planner must emit a single Opt fragment whose
    /// message refs are the material messages and that has no arms — the empty Rejoins arm must never
    /// become an invented else.
    /// </summary>
    internal static ScenarioGraph CreateOneSidedOptGraph(ScenarioPredicateWordingRole? predicateRole = null, string predicatePartition = "null/member")
    {
        var entry = Node(
            "scenario-node:v1:opt:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/WorkItems/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:opt:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:opt:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation WorkItemService",
            "service");
        var query1 = Node(
            "scenario-node:v1:opt:query1",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-workitem",
            "find work item",
            "ef-query-1",
            sequenceOrdinal: 1);
        var query2 = Node(
            "scenario-node:v1:opt:query2",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:CountAsync-workitem",
            "count work items",
            "ef-query-2",
            sequenceOrdinal: 2);
        var save = Node(
            "scenario-node:v1:opt:save",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:SaveChanges",
            "saves changes",
            "save",
            sequenceOrdinal: 3);

        var nodes = ImmutableArray.Create(entry, action, service, query1, query2, save);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:opt:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:opt:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:opt:query1", service, query1, ScenarioEdgeKind.Query, "query1", sequenceOrdinal: 1),
            Edge("scenario-edge:v1:opt:query2", service, query2, ScenarioEdgeKind.Query, "query2", sequenceOrdinal: 2),
            Edge("scenario-edge:v1:opt:save", service, save, ScenarioEdgeKind.Save, "save", sequenceOrdinal: 3));

        var guard = Decision("scenario-decision:v1:opt:guard", LockedCondition, "flow:v1:guard", predicateRole, predicatePartition);
        var trueArm = Arm("scenario-arm:v1:opt:guard:true", guard.Id, IsTrue: true);
        var falseArm = Arm("scenario-arm:v1:opt:guard:false", guard.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:opt:guard:false:query2", falseArm.Id, query2),
            Membership("scenario-membership:v1:opt:guard:false:save", falseArm.Id, save));

        var terminals = ImmutableArray.Create(
            Terminal(trueArm.Id, ScenarioTerminalKind.Rejoins),
            Terminal(falseArm.Id, ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            "scenario-graph:v1:one-sided-opt",
            nodes,
            edges,
            [],
            BuildTopology([guard], [trueArm, falseArm], memberships, terminals, reverseConstruction: false));
    }

    /// <summary>
    /// Both-material decision: the true arm carries failure result/outcome messages and terminates;
    /// the false arm carries success result/outcome messages and rejoins. The planner must emit an
    /// Alt with both arms material and visual failure-first order (terminating arm first) while the
    /// semantic arm identities stay tied to polarity, never to display position.
    /// </summary>
    internal static ScenarioGraph CreateBothMaterialAltGraph(ScenarioPredicateWordingRole? predicateRole = null, string predicatePartition = "null/member")
    {
        var entry = Node(
            "scenario-node:v1:both:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/Gadgets/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:both:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:both:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation GadgetService",
            "service");
        var failResult = Node(
            "scenario-node:v1:both:fail-result",
            ScenarioNodeKind.Result,
            "result-failure",
            "failure result with status NotFound",
            "result-failure");
        var failOutcome = Node(
            "scenario-node:v1:both:fail-outcome",
            ScenarioNodeKind.Outcome,
            "outcome:404:NotFound",
            "NotFound -> HTTP 404",
            "outcome-not-found");
        var okResult = Node(
            "scenario-node:v1:both:ok-result",
            ScenarioNodeKind.Result,
            "result-success",
            "success result with data",
            "result-success");
        var okOutcome = Node(
            "scenario-node:v1:both:ok-outcome",
            ScenarioNodeKind.Outcome,
            "outcome:200:Ok",
            "Ok -> HTTP 200",
            "outcome-ok");

        var nodes = ImmutableArray.Create(entry, action, service, failResult, failOutcome, okResult, okOutcome);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:both:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:both:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:both:fail-result", service, failResult, ScenarioEdgeKind.ResultFailure, "fail-result"),
            Edge("scenario-edge:v1:both:fail-outcome", failResult, failOutcome, ScenarioEdgeKind.OutcomeFailure, "fail-outcome"),
            Edge("scenario-edge:v1:both:ok-result", service, okResult, ScenarioEdgeKind.ResultSuccess, "ok-result"),
            Edge("scenario-edge:v1:both:ok-outcome", okResult, okOutcome, ScenarioEdgeKind.OutcomeSuccess, "ok-outcome"));

        var decision = Decision("scenario-decision:v1:both:result", LockedCondition, "flow:v1:result", predicateRole, predicatePartition);
        var failureArm = Arm("scenario-arm:v1:both:result:true", decision.Id, IsTrue: true);
        var successArm = Arm("scenario-arm:v1:both:result:false", decision.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:both:result:true:fail-result", failureArm.Id, failResult),
            Membership("scenario-membership:v1:both:result:true:fail-outcome", failureArm.Id, failOutcome),
            Membership("scenario-membership:v1:both:result:false:ok-result", successArm.Id, okResult),
            Membership("scenario-membership:v1:both:result:false:ok-outcome", successArm.Id, okOutcome));

        var terminals = ImmutableArray.Create(
            Terminal(failureArm.Id, ScenarioTerminalKind.Terminates),
            Terminal(successArm.Id, ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            "scenario-graph:v1:both-material-alt",
            nodes,
            edges,
            [],
            BuildTopology([decision], [failureArm, successArm], memberships, terminals, reverseConstruction: false));
    }

    /// <summary>
    /// Unsupported loop-back/exception topology: the only decision has Unknown terminal
    /// classifications and no memberships, and SC013 is recorded on the graph. The planner must
    /// produce no fragment (never an automatic Loop or Break) while every known message stays visible
    /// flat at the enclosing sequence level.
    /// </summary>
    internal static ScenarioGraph CreateUnknownSc013Graph()
    {
        var entry = Node(
            "scenario-node:v1:unknown:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/WorkItems/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:unknown:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:unknown:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation WorkItemService",
            "service");
        var query1 = Node(
            "scenario-node:v1:unknown:query1",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-workitem",
            "find work item",
            "ef-query-1",
            sequenceOrdinal: 1);
        var save = Node(
            "scenario-node:v1:unknown:save",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:SaveChanges",
            "saves changes",
            "save",
            sequenceOrdinal: 2);

        var nodes = ImmutableArray.Create(entry, action, service, query1, save);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:unknown:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:unknown:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:unknown:query1", service, query1, ScenarioEdgeKind.Query, "query1", sequenceOrdinal: 1),
            Edge("scenario-edge:v1:unknown:save", service, save, ScenarioEdgeKind.Save, "save", sequenceOrdinal: 2));

        var decision = Decision("scenario-decision:v1:unknown:loop", LockedCondition, "flow:v1:loop");
        var trueArm = Arm("scenario-arm:v1:unknown:loop:true", decision.Id, IsTrue: true);
        var falseArm = Arm("scenario-arm:v1:unknown:loop:false", decision.Id, IsTrue: false);

        var terminals = ImmutableArray.Create(
            Terminal(trueArm.Id, ScenarioTerminalKind.Unknown),
            Terminal(falseArm.Id, ScenarioTerminalKind.Unknown));

        var diagnostics = ImmutableArray.Create(new ScenarioGraphDiagnostic(
            new DiagnosticId("diagnostic:v1:unknown:SC013"),
            "SC013",
            "Loop-back or exception topology is not supported.",
            "The decision arm continuation could not be classified exactly."));

        return CreateGraph(
            "scenario-graph:v1:unknown-sc013",
            nodes,
            edges,
            diagnostics,
            BuildTopology([decision], [trueArm, falseArm], [], terminals, reverseConstruction: false));
    }

    /// <summary>
    /// DP002 partition: one SC013 decision (Unknown terminal classifications) that exactly owns one
    /// guarded mutation message — the save is a member of the decision's true arm. The planner must
    /// withhold that message with DP002 because its only owning decision is unsupported and can
    /// never render a continuing arm; it must never fall back to an unconditional top-level message
    /// before the guards. The truly unscoped query (no membership) keeps the accepted flat behavior.
    /// </summary>
    internal static ScenarioGraph CreateGuardedUnsupportedDecisionGraph()
    {
        var entry = Node(
            "scenario-node:v1:guarded:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/WorkItems/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:guarded:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:guarded:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation WorkItemService",
            "service");
        var query1 = Node(
            "scenario-node:v1:guarded:query1",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-workitem",
            "find work item",
            "ef-query-1",
            sequenceOrdinal: 1);
        var save = Node(
            "scenario-node:v1:guarded:save",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:SaveChanges",
            "saves changes",
            "save",
            sequenceOrdinal: 2);

        var nodes = ImmutableArray.Create(entry, action, service, query1, save);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:guarded:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:guarded:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:guarded:query1", service, query1, ScenarioEdgeKind.Query, "query1", sequenceOrdinal: 1),
            Edge("scenario-edge:v1:guarded:save", service, save, ScenarioEdgeKind.Save, "save", sequenceOrdinal: 2));

        var decision = Decision("scenario-decision:v1:guarded:loop", LockedCondition, "flow:v1:loop");
        var trueArm = Arm("scenario-arm:v1:guarded:loop:true", decision.Id, IsTrue: true);
        var falseArm = Arm("scenario-arm:v1:guarded:loop:false", decision.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:guarded:loop:true:save", trueArm.Id, save));

        var terminals = ImmutableArray.Create(
            Terminal(trueArm.Id, ScenarioTerminalKind.Unknown),
            Terminal(falseArm.Id, ScenarioTerminalKind.Unknown));

        var diagnostics = ImmutableArray.Create(new ScenarioGraphDiagnostic(
            new DiagnosticId("diagnostic:v1:guarded:SC013"),
            "SC013",
            "Loop-back or exception topology is not supported.",
            "The decision arm continuation could not be classified exactly."));

        return CreateGraph(
            "scenario-graph:v1:guarded-sc013",
            nodes,
            edges,
            diagnostics,
            BuildTopology([decision], [trueArm, falseArm], memberships, terminals, reverseConstruction: false));
    }

    /// <summary>
    /// Four-decision unambiguous nesting chain (guard1 contains guard2 contains guard3 contains
    /// guard4), deeper than the default maximum fragment depth of three. The planner must emit a
    /// stable DP001 planning diagnostic and a non-truncated flat fallback (every known message at the
    /// sequence level exactly once) instead of a partial or invalid fragment tree.
    /// </summary>
    internal static ScenarioGraph CreateDeepNestedGraph()
    {
        var entry = Node(
            "scenario-node:v1:deep:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/WorkItems/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:deep:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:deep:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation WorkItemService",
            "service");
        var q2 = Node(
            "scenario-node:v1:deep:q2",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:Guard2",
            "guarded query 2",
            "ef-query-2",
            sequenceOrdinal: 2);
        var q3 = Node(
            "scenario-node:v1:deep:q3",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:Guard3",
            "guarded query 3",
            "ef-query-3",
            sequenceOrdinal: 3);
        var q4 = Node(
            "scenario-node:v1:deep:q4",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:Guard4",
            "guarded query 4",
            "ef-query-4",
            sequenceOrdinal: 4);
        var q5 = Node(
            "scenario-node:v1:deep:q5",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:Guard5",
            "guarded query 5",
            "ef-query-5",
            sequenceOrdinal: 5);
        var state = Node(
            "scenario-node:v1:deep:state",
            ScenarioNodeKind.StateAssignment,
            "state:operation:v1:Assign",
            "assigns state",
            "state",
            sequenceOrdinal: 6);
        var save = Node(
            "scenario-node:v1:deep:save",
            ScenarioNodeKind.EntityMutation,
            "mutation:operation:v1:SaveChanges",
            "saves changes",
            "save",
            sequenceOrdinal: 7);

        var nodes = ImmutableArray.Create(entry, action, service, q2, q3, q4, q5, state, save);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:deep:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:deep:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:deep:q2", service, q2, ScenarioEdgeKind.Query, "q2", sequenceOrdinal: 2),
            Edge("scenario-edge:v1:deep:q3", service, q3, ScenarioEdgeKind.Query, "q3", sequenceOrdinal: 3),
            Edge("scenario-edge:v1:deep:q4", service, q4, ScenarioEdgeKind.Query, "q4", sequenceOrdinal: 4),
            Edge("scenario-edge:v1:deep:q5", service, q5, ScenarioEdgeKind.Query, "q5", sequenceOrdinal: 5),
            Edge("scenario-edge:v1:deep:state", service, state, ScenarioEdgeKind.StateAssignment, "state", sequenceOrdinal: 6),
            Edge("scenario-edge:v1:deep:save", service, save, ScenarioEdgeKind.Save, "save", sequenceOrdinal: 7));

        var guard1 = Decision("scenario-decision:v1:deep:guard1", new OperationId("operation:v1:decision.Guard1"), "flow:v1:guard1");
        var guard2 = Decision("scenario-decision:v1:deep:guard2", new OperationId("operation:v1:decision.Guard2"), "flow:v1:guard2");
        var guard3 = Decision("scenario-decision:v1:deep:guard3", new OperationId("operation:v1:decision.Guard3"), "flow:v1:guard3");
        var guard4 = Decision("scenario-decision:v1:deep:guard4", new OperationId("operation:v1:decision.Guard4"), "flow:v1:guard4");

        var g1True = Arm("scenario-arm:v1:deep:guard1:true", guard1.Id, IsTrue: true);
        var g1False = Arm("scenario-arm:v1:deep:guard1:false", guard1.Id, IsTrue: false);
        var g2True = Arm("scenario-arm:v1:deep:guard2:true", guard2.Id, IsTrue: true);
        var g2False = Arm("scenario-arm:v1:deep:guard2:false", guard2.Id, IsTrue: false);
        var g3True = Arm("scenario-arm:v1:deep:guard3:true", guard3.Id, IsTrue: true);
        var g3False = Arm("scenario-arm:v1:deep:guard3:false", guard3.Id, IsTrue: false);
        var g4True = Arm("scenario-arm:v1:deep:guard4:true", guard4.Id, IsTrue: true);
        var g4False = Arm("scenario-arm:v1:deep:guard4:false", guard4.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:deep:guard1:false:q2", g1False.Id, q2),
            Membership("scenario-membership:v1:deep:guard1:false:q3", g1False.Id, q3),
            Membership("scenario-membership:v1:deep:guard1:false:q4", g1False.Id, q4),
            Membership("scenario-membership:v1:deep:guard1:false:q5", g1False.Id, q5),
            Membership("scenario-membership:v1:deep:guard1:false:state", g1False.Id, state),
            Membership("scenario-membership:v1:deep:guard1:false:save", g1False.Id, save),
            Membership("scenario-membership:v1:deep:guard2:false:q3", g2False.Id, q3),
            Membership("scenario-membership:v1:deep:guard2:false:q4", g2False.Id, q4),
            Membership("scenario-membership:v1:deep:guard2:false:q5", g2False.Id, q5),
            Membership("scenario-membership:v1:deep:guard2:false:state", g2False.Id, state),
            Membership("scenario-membership:v1:deep:guard2:false:save", g2False.Id, save),
            Membership("scenario-membership:v1:deep:guard3:false:q4", g3False.Id, q4),
            Membership("scenario-membership:v1:deep:guard3:false:q5", g3False.Id, q5),
            Membership("scenario-membership:v1:deep:guard3:false:state", g3False.Id, state),
            Membership("scenario-membership:v1:deep:guard3:false:save", g3False.Id, save),
            Membership("scenario-membership:v1:deep:guard4:false:q5", g4False.Id, q5),
            Membership("scenario-membership:v1:deep:guard4:false:state", g4False.Id, state),
            Membership("scenario-membership:v1:deep:guard4:false:save", g4False.Id, save));

        var terminals = ImmutableArray.Create(
            Terminal(g1True.Id, ScenarioTerminalKind.Terminates),
            Terminal(g1False.Id, ScenarioTerminalKind.Rejoins),
            Terminal(g2True.Id, ScenarioTerminalKind.Terminates),
            Terminal(g2False.Id, ScenarioTerminalKind.Rejoins),
            Terminal(g3True.Id, ScenarioTerminalKind.Terminates),
            Terminal(g3False.Id, ScenarioTerminalKind.Rejoins),
            Terminal(g4True.Id, ScenarioTerminalKind.Terminates),
            Terminal(g4False.Id, ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            "scenario-graph:v1:deep-nested",
            nodes,
            edges,
            [],
            BuildTopology(
                [guard1, guard2, guard3, guard4],
                [g1True, g1False, g2True, g2False, g3True, g3False, g4True, g4False],
                memberships,
                terminals,
                reverseConstruction: false));
    }

    /// <summary>
    /// Both-material Alt (mirrors the both-material shape) whose terminating failure-arm memberships
    /// carry Conservative evidence while the decision, arms, and terminals stay Exact. Fragment, arm,
    /// and Break evidence must combine every supporting fact (decision, arm, membership, terminal)
    /// and certainty must degrade to the weakest contributor instead of promoting the fragment to the
    /// decision's Exact certainty.
    /// </summary>
    internal static ScenarioGraph CreateMixedCertaintyGraph()
    {
        var entry = Node(
            "scenario-node:v1:mixed:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/Gadgets/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:mixed:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:mixed:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation GadgetService",
            "service");
        var failResult = Node(
            "scenario-node:v1:mixed:fail-result",
            ScenarioNodeKind.Result,
            "result-failure",
            "failure result with status NotFound",
            "result-failure");
        var failOutcome = Node(
            "scenario-node:v1:mixed:fail-outcome",
            ScenarioNodeKind.Outcome,
            "outcome:404:NotFound",
            "NotFound -> HTTP 404",
            "outcome-not-found");
        var okResult = Node(
            "scenario-node:v1:mixed:ok-result",
            ScenarioNodeKind.Result,
            "result-success",
            "success result with data",
            "result-success");
        var okOutcome = Node(
            "scenario-node:v1:mixed:ok-outcome",
            ScenarioNodeKind.Outcome,
            "outcome:200:Ok",
            "Ok -> HTTP 200",
            "outcome-ok");

        var nodes = ImmutableArray.Create(entry, action, service, failResult, failOutcome, okResult, okOutcome);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:mixed:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:mixed:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:mixed:fail-result", service, failResult, ScenarioEdgeKind.ResultFailure, "fail-result"),
            Edge("scenario-edge:v1:mixed:fail-outcome", failResult, failOutcome, ScenarioEdgeKind.OutcomeFailure, "fail-outcome"),
            Edge("scenario-edge:v1:mixed:ok-result", service, okResult, ScenarioEdgeKind.ResultSuccess, "ok-result"),
            Edge("scenario-edge:v1:mixed:ok-outcome", okResult, okOutcome, ScenarioEdgeKind.OutcomeSuccess, "ok-outcome"));

        var decision = Decision("scenario-decision:v1:mixed:result", MixedDecisionCondition, "flow:v1:mixed");
        var failureArm = Arm("scenario-arm:v1:mixed:result:true", decision.Id, IsTrue: true);
        var successArm = Arm("scenario-arm:v1:mixed:result:false", decision.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:mixed:result:true:fail-result", failureArm.Id, failResult, CertaintyLevel.Conservative),
            Membership("scenario-membership:v1:mixed:result:true:fail-outcome", failureArm.Id, failOutcome, CertaintyLevel.Conservative),
            Membership("scenario-membership:v1:mixed:result:false:ok-result", successArm.Id, okResult),
            Membership("scenario-membership:v1:mixed:result:false:ok-outcome", successArm.Id, okOutcome));

        var terminals = ImmutableArray.Create(
            Terminal(failureArm.Id, ScenarioTerminalKind.Terminates),
            Terminal(successArm.Id, ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            "scenario-graph:v1:mixed-certainty",
            nodes,
            edges,
            [],
            BuildTopology([decision], [failureArm, successArm], memberships, terminals, reverseConstruction: false));
    }

    /// <summary>
    /// Two unrelated one-sided decisions whose material arms guard the exact same message set
    /// (alpha true = beta true = {q1, q2, q3}) with empty rejoining arms. Equal membership sets do
    /// not prove guard containment, so neither decision may nest inside the other: both fail flat and
    /// the shared messages stay at the enclosing sequence level exactly once.
    /// </summary>
    internal static ScenarioGraph CreateEqualMembershipGraph()
    {
        var entry = Node(
            "scenario-node:v1:equal:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/WorkItems/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:equal:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:equal:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation WorkItemService",
            "service");
        var q1 = Node(
            "scenario-node:v1:equal:q1",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:Q1",
            "guarded query 1",
            "ef-query-1",
            sequenceOrdinal: 1);
        var q2 = Node(
            "scenario-node:v1:equal:q2",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:Q2",
            "guarded query 2",
            "ef-query-2",
            sequenceOrdinal: 2);
        var q3 = Node(
            "scenario-node:v1:equal:q3",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:Q3",
            "guarded query 3",
            "ef-query-3",
            sequenceOrdinal: 3);

        var nodes = ImmutableArray.Create(entry, action, service, q1, q2, q3);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:equal:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:equal:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:equal:q1", service, q1, ScenarioEdgeKind.Query, "q1", sequenceOrdinal: 1),
            Edge("scenario-edge:v1:equal:q2", service, q2, ScenarioEdgeKind.Query, "q2", sequenceOrdinal: 2),
            Edge("scenario-edge:v1:equal:q3", service, q3, ScenarioEdgeKind.Query, "q3", sequenceOrdinal: 3));

        var alpha = Decision("scenario-decision:v1:equal:alpha", EqualAlphaCondition, "flow:v1:equal-alpha");
        var beta = Decision("scenario-decision:v1:equal:beta", EqualBetaCondition, "flow:v1:equal-beta");
        var alphaTrue = Arm("scenario-arm:v1:equal:alpha:true", alpha.Id, IsTrue: true);
        var alphaFalse = Arm("scenario-arm:v1:equal:alpha:false", alpha.Id, IsTrue: false);
        var betaTrue = Arm("scenario-arm:v1:equal:beta:true", beta.Id, IsTrue: true);
        var betaFalse = Arm("scenario-arm:v1:equal:beta:false", beta.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:equal:alpha:true:q1", alphaTrue.Id, q1),
            Membership("scenario-membership:v1:equal:alpha:true:q2", alphaTrue.Id, q2),
            Membership("scenario-membership:v1:equal:alpha:true:q3", alphaTrue.Id, q3),
            Membership("scenario-membership:v1:equal:beta:true:q1", betaTrue.Id, q1),
            Membership("scenario-membership:v1:equal:beta:true:q2", betaTrue.Id, q2),
            Membership("scenario-membership:v1:equal:beta:true:q3", betaTrue.Id, q3));

        var terminals = ImmutableArray.Create(
            Terminal(alphaTrue.Id, ScenarioTerminalKind.Rejoins),
            Terminal(alphaFalse.Id, ScenarioTerminalKind.Rejoins),
            Terminal(betaTrue.Id, ScenarioTerminalKind.Rejoins),
            Terminal(betaFalse.Id, ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            "scenario-graph:v1:equal-membership",
            nodes,
            edges,
            [],
            BuildTopology([alpha, beta], [alphaTrue, alphaFalse, betaTrue, betaFalse], memberships, terminals, reverseConstruction: false));
    }

    /// <summary>
    /// Ambiguous-parent topology: the child decision's full membership set {m1, m2} is contained in
    /// P1's false arm {m1, m2, m4} AND P2's false arm {m1, m2, m3}, and neither parent arm's set
    /// contains the other, so the child has two minimal parents and no unique containment. The child
    /// must fail flat at the enclosing sequence level (a root sibling) instead of nesting under
    /// either parent.
    /// </summary>
    internal static ScenarioGraph CreateAmbiguousParentGraph()
    {
        var entry = Node(
            "scenario-node:v1:ambiguous:entry",
            ScenarioNodeKind.EntryPoint,
            "entry",
            "HTTP GET api/WorkItems/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:ambiguous:action",
            ScenarioNodeKind.Action,
            "action",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:ambiguous:service",
            ScenarioNodeKind.ServiceCall,
            "service",
            "resolved service implementation WorkItemService",
            "service");
        var m1 = Node(
            "scenario-node:v1:ambiguous:m1",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:M1",
            "shared query 1",
            "ef-query-1",
            sequenceOrdinal: 1);
        var m2 = Node(
            "scenario-node:v1:ambiguous:m2",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:M2",
            "shared query 2",
            "ef-query-2",
            sequenceOrdinal: 2);
        var m3 = Node(
            "scenario-node:v1:ambiguous:m3",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:M3",
            "guarded query 3",
            "ef-query-3",
            sequenceOrdinal: 3);
        var m4 = Node(
            "scenario-node:v1:ambiguous:m4",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:M4",
            "guarded query 4",
            "ef-query-4",
            sequenceOrdinal: 4);
        var m5 = Node(
            "scenario-node:v1:ambiguous:m5",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:M5",
            "guarded query 5",
            "ef-query-5",
            sequenceOrdinal: 5);

        var nodes = ImmutableArray.Create(entry, action, service, m1, m2, m3, m4, m5);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:ambiguous:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:ambiguous:call", action, service, ScenarioEdgeKind.Call, "call"),
            Edge("scenario-edge:v1:ambiguous:m1", service, m1, ScenarioEdgeKind.Query, "m1", sequenceOrdinal: 1),
            Edge("scenario-edge:v1:ambiguous:m2", service, m2, ScenarioEdgeKind.Query, "m2", sequenceOrdinal: 2),
            Edge("scenario-edge:v1:ambiguous:m3", service, m3, ScenarioEdgeKind.Query, "m3", sequenceOrdinal: 3),
            Edge("scenario-edge:v1:ambiguous:m4", service, m4, ScenarioEdgeKind.Query, "m4", sequenceOrdinal: 4),
            Edge("scenario-edge:v1:ambiguous:m5", service, m5, ScenarioEdgeKind.Query, "m5", sequenceOrdinal: 5));

        var p1 = Decision("scenario-decision:v1:ambiguous:p1", GuardP1Condition, "flow:v1:p1");
        var p2 = Decision("scenario-decision:v1:ambiguous:p2", GuardP2Condition, "flow:v1:p2");
        var child = Decision("scenario-decision:v1:ambiguous:child", GuardCCondition, "flow:v1:child");
        var p1True = Arm("scenario-arm:v1:ambiguous:p1:true", p1.Id, IsTrue: true);
        var p1False = Arm("scenario-arm:v1:ambiguous:p1:false", p1.Id, IsTrue: false);
        var p2True = Arm("scenario-arm:v1:ambiguous:p2:true", p2.Id, IsTrue: true);
        var p2False = Arm("scenario-arm:v1:ambiguous:p2:false", p2.Id, IsTrue: false);
        var childTrue = Arm("scenario-arm:v1:ambiguous:child:true", child.Id, IsTrue: true);
        var childFalse = Arm("scenario-arm:v1:ambiguous:child:false", child.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:ambiguous:p1:true:m3", p1True.Id, m3),
            Membership("scenario-membership:v1:ambiguous:p1:true:m5", p1True.Id, m5),
            Membership("scenario-membership:v1:ambiguous:p1:false:m1", p1False.Id, m1),
            Membership("scenario-membership:v1:ambiguous:p1:false:m2", p1False.Id, m2),
            Membership("scenario-membership:v1:ambiguous:p1:false:m4", p1False.Id, m4),
            Membership("scenario-membership:v1:ambiguous:p2:true:m4", p2True.Id, m4),
            Membership("scenario-membership:v1:ambiguous:p2:true:m5", p2True.Id, m5),
            Membership("scenario-membership:v1:ambiguous:p2:false:m1", p2False.Id, m1),
            Membership("scenario-membership:v1:ambiguous:p2:false:m2", p2False.Id, m2),
            Membership("scenario-membership:v1:ambiguous:p2:false:m3", p2False.Id, m3),
            Membership("scenario-membership:v1:ambiguous:child:true:m1", childTrue.Id, m1),
            Membership("scenario-membership:v1:ambiguous:child:false:m2", childFalse.Id, m2));

        var terminals = ImmutableArray.Create(
            Terminal(p1True.Id, ScenarioTerminalKind.Terminates),
            Terminal(p1False.Id, ScenarioTerminalKind.Rejoins),
            Terminal(p2True.Id, ScenarioTerminalKind.Terminates),
            Terminal(p2False.Id, ScenarioTerminalKind.Rejoins),
            Terminal(childTrue.Id, ScenarioTerminalKind.Terminates),
            Terminal(childFalse.Id, ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            "scenario-graph:v1:ambiguous-parent",
            nodes,
            edges,
            [],
            BuildTopology(
                [p1, p2, child],
                [p1True, p1False, p2True, p2False, childTrue, childFalse],
                memberships,
                terminals,
                reverseConstruction: false));
    }

    /// <summary>
    /// accepted contract typed-result decision graph: one IsSuccess decision whose failure arm terminates with one
    /// typed result/outcome pair and whose success arm rejoins. Result nodes carry the accepted contract typed
    /// <see cref="StructuralResultFactoryKind"/> presentation the builder must expose on
    /// <see cref="ScenarioNodePresentation"/>; outcome nodes carry typed helper/status presentation.
    /// Node and edge details intentionally contain the current compiler-oriented phrases ("failure
    /// factory carries status", "success result with data of ...") so the typed-wording regression is
    /// observable at the pure wording layer. When <paramref name="poisoned"/> is set, every detail
    /// string contradicts the typed facts so the tests prove Detail never overrides typed wording or
    /// element identities. A Conservative <paramref name="failureMembershipCertainty"/> degrades the
    /// fragment, its terminating arm, and its Break without changing typed wording. When
    /// <paramref name="successArmTerminates"/> is set, the success arm is the unique Terminates arm
    /// (and the failure arm rejoins) so the terminating-success wording partition is observable: the
    /// arm and its Break must render "Return success data", never the failure vocabulary.
    /// </summary>
    internal static ScenarioGraph CreateTypedResultDecisionGraph(
        StructuralResultFactoryKind? failureFactoryKind = StructuralResultFactoryKind.NotFound,
        StructuralResultFactoryKind? successFactoryKind = StructuralResultFactoryKind.Success,
        HttpOutcomeHelperKind failureHelperKind = HttpOutcomeHelperKind.NotFound,
        int failureStatusCode = 404,
        bool poisoned = false,
        CertaintyLevel failureMembershipCertainty = CertaintyLevel.Exact,
        bool successArmTerminates = false)
        => CreateResultDecisionCore(
            "scenario-graph:v1:typed-result",
            "operation:v1:decision.ServiceResultOk",
            poisoned,
            failureFactoryKind,
            successFactoryKind,
            failureHelperKind,
            failureStatusCode,
            failureMembershipCertainty,
            successArmTerminates);

    /// <summary>
    /// Unknown/custom factory partition: both result nodes expose
    /// <see cref="StructuralResultFactoryKind.Unknown"/> (the closed-vocabulary value the compiler
    /// collector returns for unrecognized factory names). Wording must fall back conservatively to
    /// "Return a failure status" for the failure path and "Return success data" for the success path
    /// instead of inventing a status meaning.
    /// </summary>
    internal static ScenarioGraph CreateUnknownResultDecisionGraph()
        => CreateResultDecisionCore(
            "scenario-graph:v1:unknown-result",
            "operation:v1:decision.CustomResult",
            poisoned: false,
            StructuralResultFactoryKind.Unknown,
            StructuralResultFactoryKind.Unknown,
            HttpOutcomeHelperKind.NotFound,
            404,
            CertaintyLevel.Exact);

    /// <summary>
    /// Status-switch partition: the result-status node has no structural factory kind (the switch
    /// selects among several status members), so the terminating arm carries exactly one typed
    /// OUTCOME terminal and the rejoining arm carries the success outcome. The planner must render
    /// "Return HTTP 404" for the terminating arm/Break and "Continue" for the rejoining arm, and the
    /// "status result" compiler phrase must never appear in output.
    /// </summary>
    internal static ScenarioGraph CreateStatusSwitchTopologyGraph()
    {
        var entry = Node(
            "scenario-node:v1:status:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:GET-api-Gadgets",
            "GET api/Gadgets",
            "entry-point");
        var action = Node(
            "scenario-node:v1:status:action",
            ScenarioNodeKind.Action,
            "action:method:v1:Acme.Controllers.GadgetsController.GetById",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:status:service",
            ScenarioNodeKind.ServiceCall,
            "service:method:v1:Acme.Services.GadgetService.GetByIdAsync",
            "resolved service implementation Acme.Services.GadgetService",
            "service");
        var resultStatus = Node(
            "scenario-node:v1:status:result-status",
            ScenarioNodeKind.Result,
            "result-status",
            "status result of Acme.Models.ServiceResultStatus",
            "structural-result");
        var outcomeNotFound = Node(
            "scenario-node:v1:status:outcome-404",
            ScenarioNodeKind.Outcome,
            "outcome:404:NotFound",
            "NotFound -> HTTP 404",
            "outcome-not-found",
            presentation: new ScenarioNodePresentation(
                OutcomeHelperKind: HttpOutcomeHelperKind.NotFound,
                OutcomeStatusCode: 404));
        var outcomeOk = Node(
            "scenario-node:v1:status:outcome-200",
            ScenarioNodeKind.Outcome,
            "outcome:200:Ok",
            "Ok -> HTTP 200",
            "outcome-ok",
            presentation: new ScenarioNodePresentation(
                OutcomeHelperKind: HttpOutcomeHelperKind.Ok,
                OutcomeStatusCode: 200));

        var nodes = ImmutableArray.Create(entry, action, service, resultStatus, outcomeNotFound, outcomeOk);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:status:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:status:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through Acme.Services.IGadgetService"),
            Edge("scenario-edge:v1:status:result-status", service, resultStatus, ScenarioEdgeKind.ResultStatus, "result-status", detail: "status result"),
            Edge("scenario-edge:v1:status:outcome-404", resultStatus, outcomeNotFound, ScenarioEdgeKind.OutcomeFailure, "outcome-not-found", detail: "NotFound outcome"),
            Edge("scenario-edge:v1:status:outcome-200", resultStatus, outcomeOk, ScenarioEdgeKind.OutcomeSuccess, "outcome-ok", detail: "Ok outcome"));

        var decision = Decision(
            "scenario-decision:v1:status:outcome",
            new OperationId("operation:v1:decision.StatusOutcome"),
            "flow:v1:status");
        var terminatingArm = Arm("scenario-arm:v1:status:outcome:true", decision.Id, IsTrue: true);
        var continuingArm = Arm("scenario-arm:v1:status:outcome:false", decision.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:status:outcome:true:result-status", terminatingArm.Id, resultStatus),
            Membership("scenario-membership:v1:status:outcome:true:outcome-404", terminatingArm.Id, outcomeNotFound),
            Membership("scenario-membership:v1:status:outcome:false:outcome-200", continuingArm.Id, outcomeOk));

        var terminals = ImmutableArray.Create(
            Terminal(terminatingArm.Id, ScenarioTerminalKind.Terminates),
            Terminal(continuingArm.Id, ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            "scenario-graph:v1:status-switch",
            nodes,
            edges,
            [],
            BuildTopology([decision], [terminatingArm, continuingArm], memberships, terminals, reverseConstruction: false),
            entryPointId: new EntryPointId("entry-point:v1:GET-api-Gadgets"));
    }

    /// <summary>
    /// accepted contract planner slice: one synthetic topology-empty graph that carries a typed conditional
    /// service composition and one framework cache-miss callback region. The true (SQL) arm member
    /// nodes are the SQL service node and its EF query node; the false (JSON) arm member node is the
    /// JSON service node only. The flat graph holds the entry request, one Call edge from the action
    /// to each service node, and one Query edge from the SQL service to the query node, so the query
    /// is never presented as unconditional SQL work. The composition service type is the
    /// <c>ICustomerService</c> contract role and the implementation types are
    /// <c>SqlCustomerService</c>/<c>JsonCustomerService</c> so the planner's namespace-free,
    /// humanized role labels ("Customer service", "SQL customer service", "JSON customer service")
    /// are pinned exactly. When <paramref name="reverseConstruction"/> is set the node, edge, and
    /// member-node arrays are supplied in reversed order so the planner must derive identical
    /// sequence, fragment tree, and debug projection from stable semantic identities and canonical
    /// ordering rather than input order. When <paramref name="unsupported"/> is set the graph
    /// mirrors the post-scenario unsupported output: the same composition and both service
    /// nodes/calls remain, the EF query node/edge/region are withheld (no cache-miss Opt), and the
    /// exact SC014 diagnostic is recorded so the planner surfaces a Conservative evidence-backed
    /// technical fallback instead of presenting the unsupported cache work.
    /// </summary>
    internal static ScenarioGraph CreateCompositionEmptyTopologyGraph(
        bool reverseConstruction = false,
        bool unsupported = false)
    {
        var entryPointId = new EntryPointId("entry-point:v1:GET-api-Customers");
        var entry = Node(
            "scenario-node:v1:composition:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:GET-api-Customers",
            "GET api/Customers/{id}",
            "entry-point");
        var action = Node(
            "scenario-node:v1:composition:action",
            ScenarioNodeKind.Action,
            "action:method:v1:Acme.Controllers.CustomersController.GetById",
            "controller action",
            "action",
            presentation: new ScenarioNodePresentation(ControllerTypeName: "Acme.Controllers.CustomersController"));
        var sqlService = Node(
            "scenario-node:v1:composition:sql-service",
            ScenarioNodeKind.ServiceCall,
            "service:method:v1:Acme.Services.SqlCustomerService.GetByIdAsync",
            "resolved service implementation Acme.Services.SqlCustomerService",
            "service",
            presentation: new ScenarioNodePresentation(
                ContractTypeName: "Acme.Services.ICustomerService",
                ImplementationTypeName: "Acme.Services.SqlCustomerService",
                CalledMemberName: "GetByIdAsync"));
        var jsonService = Node(
            "scenario-node:v1:composition:json-service",
            ScenarioNodeKind.ServiceCall,
            "service:method:v1:Acme.Services.JsonCustomerService.GetByIdAsync",
            "resolved service implementation Acme.Services.JsonCustomerService",
            "service",
            presentation: new ScenarioNodePresentation(
                ContractTypeName: "Acme.Services.ICustomerService",
                ImplementationTypeName: "Acme.Services.JsonCustomerService",
                CalledMemberName: "GetByIdAsync"));
        var query = Node(
            "scenario-node:v1:composition:query",
            ScenarioNodeKind.EntityQuery,
            "query:operation:v1:SingleOrDefaultAsync-customer",
            "find customer",
            "ef-query",
            sequenceOrdinal: 1,
            presentation: new ScenarioNodePresentation(
                DbContextTypeName: "Acme.Data.SalesDbContext",
                EntityTypeName: "Acme.Models.Customer",
                QueryOperatorKind: EntityFrameworkQueryOperatorKind.SingleOrDefaultAsync));

        var nodes = unsupported
            ? ImmutableArray.Create(entry, action, sqlService, jsonService)
            : ImmutableArray.Create(entry, action, sqlService, jsonService, query);
        var edges = unsupported
            ? ImmutableArray.Create(
                Edge("scenario-edge:v1:composition:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
                Edge("scenario-edge:v1:composition:call-sql", action, sqlService, ScenarioEdgeKind.Call, "call-sql"),
                Edge("scenario-edge:v1:composition:call-json", action, jsonService, ScenarioEdgeKind.Call, "call-json"))
            : ImmutableArray.Create(
                Edge("scenario-edge:v1:composition:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
                Edge("scenario-edge:v1:composition:call-sql", action, sqlService, ScenarioEdgeKind.Call, "call-sql"),
                Edge("scenario-edge:v1:composition:call-json", action, jsonService, ScenarioEdgeKind.Call, "call-json"),
                Edge("scenario-edge:v1:composition:query-sql", sqlService, query, ScenarioEdgeKind.Query, "query-sql", sequenceOrdinal: 1));

        var composition = new ScenarioServiceComposition(
            new ScenarioCompositionId("scenario-composition:v1:customer"),
            "Acme.Services.ICustomerService",
            new ScenarioConfigurationDecision(
                new OperationId("operation:v1:decision.UseSqlDatabase"),
                new OperationId("operation:v1:config:UseSqlDatabase"),
                "FeatureToggles:UseSqlDatabase",
                [ScenarioGraphTestFactory.SourceEvidence("composition-decision")],
                CertaintyLevel.Exact),
            new ScenarioServiceAlternativeArm(
                IsTrue: true,
                new SemanticFactId("semantic-fact:v1:registration:sql"),
                "Acme.Services.SqlCustomerService",
                new MethodId("method:v1:Acme.Services.SqlCustomerService.GetByIdAsync"),
                [ScenarioGraphTestFactory.SourceEvidence("composition-arm-sql")],
                CertaintyLevel.Exact,
                unsupported
                    ? [sqlService.Id]
                    : reverseConstruction ? [query.Id, sqlService.Id] : [sqlService.Id, query.Id]),
            new ScenarioServiceAlternativeArm(
                IsTrue: false,
                new SemanticFactId("semantic-fact:v1:registration:json"),
                "Acme.Services.JsonCustomerService",
                new MethodId("method:v1:Acme.Services.JsonCustomerService.GetByIdAsync"),
                [ScenarioGraphTestFactory.SourceEvidence("composition-arm-json")],
                CertaintyLevel.Exact,
                [jsonService.Id]),
            profileSelection: null);

        var cacheMissRegion = new ScenarioCallbackRegion(
            new ScenarioCallbackRegionId("scenario-callback-region:v1:cache-miss"),
            new CallbackBoundaryId("callback-boundary:v1:fusion:get-or-set"),
            CallbackCardinality.ZeroOrOne,
            CallbackTriggerKind.Conditional,
            triggerCondition: null,
            CallbackCompletionKind.Unknown,
            [query.Id],
            [ScenarioGraphTestFactory.SourceEvidence("callback-region")],
            CertaintyLevel.Exact,
            FrameworkCallbackConditionKind.CacheMiss);

        // The unsupported shape records the exact SC014 diagnostic the Scenario Graph emits after
        // callback processing; the planner maps it to a Conservative technical fallback phrase and
        // (because no EntityQuery node survives) grounds it in the entry-point evidence.
        var diagnostics = unsupported
            ? ImmutableArray.Create(new ScenarioGraphDiagnostic(
                new DiagnosticId("diagnostic:v1:composition:SC014"),
                "SC014",
                "The FusionCache callback boundary has no exact supported GetOrSetAsync contract; cache-miss membership is withheld.",
                "The FusionCache GetOrSetAsync call has an unsupported shape; no cache-miss contract was admitted."))
            : [];

        return new ScenarioGraph(
            entryPointId,
            ScenarioGraphTestFactory.Profile.Id,
            new MethodId("method:v1:Acme.Controllers.CustomersController.GetById"),
            HttpMethodKind.Get,
            "api/Customers/{id}",
            "GET api/Customers/{id}",
            reverseConstruction ? nodes.Reverse().ToImmutableArray() : nodes,
            reverseConstruction ? edges.Reverse().ToImmutableArray() : edges,
            diagnostics,
            "scenario-graph:v1:composition-empty-topology",
            ScenarioTopology.Empty,
            composition,
            unsupported ? [] : [cacheMissRegion]);
    }

    /// <summary>
    /// Core typed-result graph builder shared by the typed and unknown partitions. By default the
    /// failure arm terminates with the result/outcome pair and the success arm rejoins; when
    /// <paramref name="successArmTerminates"/> is set the polarity is flipped so the success arm is
    /// the unique Terminates arm. When <paramref name="poisoned"/> is set every Detail string
    /// contradicts the typed presentation facts while node, edge, decision, arm, membership, and
    /// terminal identities stay identical.
    /// </summary>
    private static ScenarioGraph CreateResultDecisionCore(
        string graphId,
        string conditionValue,
        bool poisoned,
        StructuralResultFactoryKind? failureFactoryKind,
        StructuralResultFactoryKind? successFactoryKind,
        HttpOutcomeHelperKind failureHelperKind,
        int failureStatusCode,
        CertaintyLevel failureMembershipCertainty,
        bool successArmTerminates = false)
    {
        string failureKindName = failureFactoryKind?.ToString() ?? "Unknown";
        var entry = Node(
            "scenario-node:v1:typed-result:entry",
            ScenarioNodeKind.EntryPoint,
            "entry-point:v1:POST-api-Gadgets",
            "POST api/Gadgets",
            "entry-point");
        var action = Node(
            "scenario-node:v1:typed-result:action",
            ScenarioNodeKind.Action,
            "action:method:v1:Acme.Controllers.GadgetsController.Reserve",
            "controller action",
            "action");
        var service = Node(
            "scenario-node:v1:typed-result:service",
            ScenarioNodeKind.ServiceCall,
            "service:method:v1:Acme.Services.GadgetService.ReserveAsync",
            "resolved service implementation Acme.Services.GadgetService",
            "service");
        var failResult = ResultNode(
            "scenario-node:v1:typed-result:fail-result",
            "result-failure",
            poisoned
                ? "Ok -> HTTP 999 links to GET api/Evil"
                : $"failure result with status {failureKindName} of Acme.Services.GadgetResult<Acme.Models.Gadget>",
            failureFactoryKind);
        var failOutcome = Node(
            "scenario-node:v1:typed-result:fail-outcome",
            ScenarioNodeKind.Outcome,
            $"outcome:{failureStatusCode}:{failureHelperKind}",
            poisoned
                ? "Ok -> HTTP 999 links to GET api/Evil"
                : $"{failureHelperKind} -> HTTP {failureStatusCode}",
            "outcome-failure",
            presentation: new ScenarioNodePresentation(
                OutcomeHelperKind: failureHelperKind,
                OutcomeStatusCode: failureStatusCode));
        var okResult = ResultNode(
            "scenario-node:v1:typed-result:ok-result",
            "result-success",
            poisoned
                ? "status result of Acme.Models.ServiceResultStatus"
                : "success result with data of Acme.Services.GadgetResult<Acme.Models.Gadget>",
            successFactoryKind);
        var okOutcome = Node(
            "scenario-node:v1:typed-result:ok-outcome",
            ScenarioNodeKind.Outcome,
            "outcome:200:Ok",
            poisoned ? "status result of Acme.Models.ServiceResultStatus" : "Ok -> HTTP 200",
            "outcome-ok",
            presentation: new ScenarioNodePresentation(
                OutcomeHelperKind: HttpOutcomeHelperKind.Ok,
                OutcomeStatusCode: 200));

        var nodes = ImmutableArray.Create(entry, action, service, failResult, failOutcome, okResult, okOutcome);
        var edges = ImmutableArray.Create(
            Edge("scenario-edge:v1:typed-result:entry", entry, action, ScenarioEdgeKind.Entry, "entry"),
            Edge("scenario-edge:v1:typed-result:call", action, service, ScenarioEdgeKind.Call, "call", detail: "call through Acme.Services.IGadgetService"),
            Edge(
                "scenario-edge:v1:typed-result:fail-result",
                service,
                failResult,
                ScenarioEdgeKind.ResultFailure,
                "result-failure",
                detail: poisoned ? "success factory carries data" : "failure factory carries status"),
            Edge(
                "scenario-edge:v1:typed-result:fail-outcome",
                failResult,
                failOutcome,
                ScenarioEdgeKind.OutcomeFailure,
                "outcome-failure",
                detail: poisoned ? "Ok -> HTTP 999 links to GET api/Evil" : $"{failureHelperKind} outcome"),
            Edge(
                "scenario-edge:v1:typed-result:ok-result",
                service,
                okResult,
                ScenarioEdgeKind.ResultSuccess,
                "result-success",
                detail: poisoned ? "status result" : "success factory carries data"),
            Edge(
                "scenario-edge:v1:typed-result:ok-outcome",
                okResult,
                okOutcome,
                ScenarioEdgeKind.OutcomeSuccess,
                "outcome-ok",
                detail: poisoned ? "status result" : "Ok outcome"));

        var decision = Decision(
            "scenario-decision:v1:typed-result:result",
            new OperationId(conditionValue),
            "flow:v1:typed-result");
        var failureArm = Arm("scenario-arm:v1:typed-result:result:true", decision.Id, IsTrue: true);
        var successArm = Arm("scenario-arm:v1:typed-result:result:false", decision.Id, IsTrue: false);

        var memberships = ImmutableArray.Create(
            Membership("scenario-membership:v1:typed-result:result:true:fail-result", failureArm.Id, failResult, failureMembershipCertainty),
            Membership("scenario-membership:v1:typed-result:result:true:fail-outcome", failureArm.Id, failOutcome, failureMembershipCertainty),
            Membership("scenario-membership:v1:typed-result:result:false:ok-result", successArm.Id, okResult),
            Membership("scenario-membership:v1:typed-result:result:false:ok-outcome", successArm.Id, okOutcome));

        var terminals = ImmutableArray.Create(
            Terminal(failureArm.Id, successArmTerminates ? ScenarioTerminalKind.Rejoins : ScenarioTerminalKind.Terminates),
            Terminal(successArm.Id, successArmTerminates ? ScenarioTerminalKind.Terminates : ScenarioTerminalKind.Rejoins));

        return CreateGraph(
            graphId,
            nodes,
            edges,
            [],
            BuildTopology([decision], [failureArm, successArm], memberships, terminals, reverseConstruction: false),
            entryPointId: new EntryPointId("entry-point:v1:POST-api-Gadgets"));
    }

    /// <summary>
    /// Result node carrying the typed structural-result factory kind plus both supporting evidence
    /// artifacts (the structural-result fact and the IsSuccess decision) so phrase evidence and
    /// certainty assertions have typed support to observe.
    /// </summary>
    private static ScenarioNode ResultNode(
        string id,
        string key,
        string detail,
        StructuralResultFactoryKind? factoryKind)
        => new(
            new ScenarioNodeId(id),
            ScenarioNodeKind.Result,
            key,
            null,
            null,
            detail,
            [ScenarioGraphTestFactory.SourceEvidence("structural-result"), ScenarioGraphTestFactory.SourceEvidence("decision")],
            CertaintyLevel.Exact,
            sequenceOrdinal: 0,
            factoryKind is null ? null : new ScenarioNodePresentation(ResultFactoryKind: factoryKind));

    private static ScenarioGraph CreateGraph(
        string debugProjection,
        ImmutableArray<ScenarioNode> nodes,
        ImmutableArray<ScenarioEdge> edges,
        ImmutableArray<ScenarioGraphDiagnostic> diagnostics,
        ScenarioTopology topology,
        CompilationProfileId? profileId = null,
        EntryPointId? entryPointId = null)
        => new(
            entryPointId ?? WorkItemEntryPoint,
            profileId ?? ScenarioGraphTestFactory.Profile.Id,
            ActionMethod,
            HttpMethodKind.Get,
            "api/WorkItems/{id}",
            "GET api/WorkItems/{id}",
            nodes,
            edges,
            diagnostics,
            debugProjection,
            topology);

    /// <summary>Reverses every topology array so the planner must derive identical fragments from stable semantic keys, not input order.</summary>
    private static ScenarioTopology BuildTopology(
        ImmutableArray<ScenarioDecision> decisions,
        ImmutableArray<ScenarioArm> arms,
        ImmutableArray<ScenarioMembership> memberships,
        ImmutableArray<ScenarioArmTerminal> terminals,
        bool reverseConstruction)
    {
        if (!reverseConstruction)
        {
            return new ScenarioTopology(decisions, arms, memberships, terminals);
        }

        return new ScenarioTopology(
            decisions.Reverse().ToImmutableArray(),
            arms.Reverse().ToImmutableArray(),
            memberships.Reverse().ToImmutableArray(),
            terminals.Reverse().ToImmutableArray());
    }

    private static ScenarioNode Node(
        string id,
        ScenarioNodeKind kind,
        string key,
        string detail,
        string artifact,
        int sequenceOrdinal = 0,
        ScenarioNodePresentation? presentation = null)
        => new(
            new ScenarioNodeId(id),
            kind,
            key,
            null,
            null,
            detail,
            [ScenarioGraphTestFactory.SourceEvidence(artifact)],
            CertaintyLevel.Exact,
            sequenceOrdinal,
            presentation);

    private static ScenarioEdge Edge(
        string id,
        ScenarioNode source,
        ScenarioNode target,
        ScenarioEdgeKind kind,
        string artifact,
        int sequenceOrdinal = 0,
        string? detail = null)
        => new(
            new ScenarioEdgeId(id),
            source.Id,
            target.Id,
            kind,
            detail ?? artifact,
            [ScenarioGraphTestFactory.SourceEvidence(artifact)],
            CertaintyLevel.Exact,
            sequenceOrdinal);

    private static ScenarioDecision Decision(string id, OperationId condition, string controllingFlowNode, ScenarioPredicateWordingRole? predicateRole = null, string predicatePartition = "null/member", string predicateEvidenceArtifact = "predicate")
        => new(
            new ScenarioDecisionId(id),
            ActionMethod,
            new FlowNodeId(controllingFlowNode),
            condition,
            [ScenarioGraphTestFactory.SourceEvidence("decision")],
            CertaintyLevel.Exact,
            predicateRole is null ? null : new ScenarioPredicateWording(
                new SemanticFactId("semantic-fact:v1:predicate:wording"),
                PredicateWordingTestFactory.Create(predicatePartition),
                predicateRole.Value,
                [ScenarioGraphTestFactory.SourceEvidence(predicateEvidenceArtifact)],
                CertaintyLevel.Exact));

    private static ScenarioArm Arm(string id, ScenarioDecisionId decision, bool IsTrue)
        => new(
            new ScenarioArmId(id),
            decision,
            IsTrue,
            [ScenarioGraphTestFactory.SourceEvidence("arm")],
            CertaintyLevel.Exact);

    private static ScenarioMembership Membership(
        string id,
        ScenarioArmId arm,
        ScenarioNode node,
        CertaintyLevel certainty = CertaintyLevel.Exact,
        string evidenceArtifact = "membership")
        => new(
            new ScenarioMembershipId(id),
            arm,
            node.Id,
            [ScenarioGraphTestFactory.SourceEvidence(evidenceArtifact, certainty)],
            certainty);

    private static ScenarioArmTerminal Terminal(ScenarioArmId arm, ScenarioTerminalKind kind)
        => new(
            arm,
            kind,
            [ScenarioGraphTestFactory.SourceEvidence("terminal")],
            CertaintyLevel.Exact);
}
