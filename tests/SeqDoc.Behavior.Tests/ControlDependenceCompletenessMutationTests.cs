using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Behavior.Tests;

/// <summary>
/// accepted contract deterministic mutation gate for the control-dependence completeness repair. The first
/// assertion proves the fingerprint machinery is sensitive to omitting the second-node or terminal
/// dependence (the exact mutant architecture decision authorizes regenerating). The second assertion proves the
/// current candidate fingerprint is RED: the extractor still records only the first node and never the
/// represented terminal, so the fingerprint over the complete required dependence set differs from the
/// candidate until the repair lands. This file deliberately does not edit the preserved existing
/// MutationGateTests.
/// </summary>
public sealed class ControlDependenceCompletenessMutationTests
{
    private static readonly MethodId Method = new("method:v1:test");

    [Fact]
    public void SecondNodeAndTerminalDependenceOmissionChangesFingerprintAndCurrentFingerprintIsIncomplete()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var first = new OperationId("behavior-operation:v1:first");
        var second = new OperationId("behavior-operation:v1:second");
        var body = CreateControlledTerminalBody(condition, first, second);

        var actual = MethodFlowBuilder.Build(body).Snapshot;
        var decision = Assert.Single(actual.Nodes.OfType<DecisionFlowNode>());
        var firstNode = Assert.Single(actual.Nodes.OfType<OperationFlowNode>(), node => node.Operation == first);
        var secondNode = Assert.Single(actual.Nodes.OfType<OperationFlowNode>(), node => node.Operation == second);
        var terminal = Assert.Single(actual.Nodes.OfType<ReturnFlowNode>());

        // The required post-repair dependence set: first node, second node, and the represented
        // terminal of the same controlled block, all on the true arm, in canonical order.
        var complete = ImmutableArray.Create(
                new ControlDependence(decision.Id, firstNode.Id, ControlledOnTrue: true, decision.Evidence, CertaintyLevel.Exact),
                new ControlDependence(decision.Id, secondNode.Id, ControlledOnTrue: true, decision.Evidence, CertaintyLevel.Exact),
                new ControlDependence(decision.Id, terminal.Id, ControlledOnTrue: true, decision.Evidence, CertaintyLevel.Exact))
            .OrderBy(dependence => dependence.ControllingDecision.Value, StringComparer.Ordinal)
            .ThenBy(dependence => dependence.ControlledNode.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var omittedSecond = complete.Where(dependence => dependence.ControlledNode != secondNode.Id).ToImmutableArray();
        var omittedTerminal = complete.Where(dependence => dependence.ControlledNode != terminal.Id).ToImmutableArray();

        // Mutation gate: dropping the second-node or terminal dependence changes the fingerprint, so a
        // first-node-only extractor mutant is observable in the affected Method Flow fingerprint.
        Assert.NotEqual(
            MethodFlowFingerprint.Compute(actual with { ControlDependences = complete }),
            MethodFlowFingerprint.Compute(actual with { ControlDependences = omittedSecond }));
        Assert.NotEqual(
            MethodFlowFingerprint.Compute(actual with { ControlDependences = complete }),
            MethodFlowFingerprint.Compute(actual with { ControlDependences = omittedTerminal }));

        // RED: the candidate fingerprint equals the fingerprint over the extractor's current
        // (incomplete) dependence set, which differs from the complete required set. After the repair
        // the extractor emits the complete set and this equality holds.
        Assert.Equal(
            MethodFlowFingerprint.Compute(actual with { ControlDependences = complete }),
            actual.FlowFingerprint);

        // Direct RED observations of the missing second-node and terminal dependences.
        Assert.Contains(actual.ControlDependences, dependence => dependence.ControlledNode == secondNode.Id);
        Assert.Contains(actual.ControlDependences, dependence => dependence.ControlledNode == terminal.Id);
    }

    private static ExtractedMethodBody CreateControlledTerminalBody(
        OperationId condition,
        OperationId first,
        OperationId second)
    {
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(first, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null),
                Operation(second, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null)),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 3, Conditional, condition, [2], [0]),
                Block(2, [first, second], null, Return, null, [], [1]),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [3])),
            RootRegion(4),
            []);
        return body;
    }

    private static ImmutableArray<ExtractedExceptionRegion> RootRegion(int lastBlock) =>
        ImmutableArray.Create(new ExtractedExceptionRegion(
            new FlowRegionId("flow-region:v1:root"),
            ExtractedRegionKind.Root,
            null,
            0,
            0,
            lastBlock,
            null,
            [],
            CertaintyLevel.Exact));

    private static ExtractedBasicBlock Block(
        int ordinal,
        ImmutableArray<OperationId> operations,
        int? fallThrough,
        ExtractedBlockTerminalKind terminal,
        OperationId? branchCondition = null,
        ImmutableArray<int> conditionals = default,
        ImmutableArray<int> predecessors = default) =>
        new(
            ordinal,
            operations,
            branchCondition,
            fallThrough,
            conditionals.IsDefault ? [] : conditionals,
            predecessors.IsDefault ? [] : predecessors,
            terminal,
            false,
            [],
            [],
            [],
            CertaintyLevel.Exact);

    private static ExtractedOperation Operation(OperationId id, ExtractedOperationKind kind, string type, string? constantValue, string? localName, int? parameterOrdinal) =>
        new(
            id,
            Method,
            kind,
            null,
            [],
            0,
            type,
            constantValue,
            false,
            true,
            [],
            [],
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            localName,
            parameterOrdinal,
            [],
            CertaintyLevel.Exact);

    private const ExtractedBlockTerminalKind None = ExtractedBlockTerminalKind.None;
    private const ExtractedBlockTerminalKind Conditional = ExtractedBlockTerminalKind.Conditional;
    private const ExtractedBlockTerminalKind Return = ExtractedBlockTerminalKind.Return;
    private const ExtractedBlockTerminalKind Exit = ExtractedBlockTerminalKind.Exit;
}
