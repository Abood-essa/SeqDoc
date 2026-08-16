using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Behavior.Tests;

public sealed class LocalFlowAnalyzerTests
{
    private static readonly MethodId Method = new("method:v1:test");

    [Fact]
    public void AssignmentChainBuildsDefinitionBasedValueGraph()
    {
        var literal = new OperationId("behavior-operation:v1:literal");
        var local = new OperationId("behavior-operation:v1:local");
        var assignment = new OperationId("behavior-operation:v1:assignment");
        var result = new OperationId("behavior-operation:v1:result");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(literal, ExtractedOperationKind.Literal, "System.Int32", "1", null, null),
                Operation(local, ExtractedOperationKind.LocalReference, "System.Int32", null, "total", null),
                Operation(
                    assignment,
                    ExtractedOperationKind.Assignment,
                    "System.Int32",
                    null,
                    null,
                    null,
                    new ExtractedAssignmentPayload(local, literal, false)),
                Operation(result, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, "total", null)),
            ImmutableArray.Create(
                Block(0, [literal, local, assignment], 1, None),
                Block(1, [result], null, Exit)),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                1,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var (graph, dependences, summary) = LocalFlowAnalyzer.Analyze(body, MethodFlowBuilder.Build(body).Snapshot);

        Assert.Contains(graph.Nodes, node => node.Kind == ValueNodeKind.Constant);
        Assert.Contains(graph.Nodes, node => node.Kind == ValueNodeKind.LocalDefinition);
        Assert.Contains(graph.Edges, edge => edge.Kind == ValueEdgeKind.Assignment);
        Assert.Equal(64, summary.BodyFingerprint.Length);
        Assert.True(summary.IsComplete);
    }

    [Fact]
    public void ParameterFlowToReturnIsDetected()
    {
        var parameter = new OperationId("behavior-operation:v1:param");
        var returnOp = new OperationId("behavior-operation:v1:return");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            ImmutableArray.Create(new ExtractedParameter("input", "System.Int32", ParameterRefKind.None)),
            [],
            ImmutableArray.Create(
                Operation(parameter, ExtractedOperationKind.ParameterReference, "System.Int32", null, null, 0),
                Operation(
                    returnOp,
                    ExtractedOperationKind.Return,
                    "System.Int32",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    new ExtractedReturnPayload(parameter))),
            ImmutableArray.Create(
                Block(0, [parameter, returnOp], 1, None),
                Block(1, [], null, Exit)),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                1,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var (graph, _, summary) = LocalFlowAnalyzer.Analyze(body, MethodFlowBuilder.Build(body).Snapshot);

        var flow = Assert.Single(summary.ParameterFlows);
        Assert.True(flow.FlowsToReturn);
    }

    [Fact]
    public void StateReadsFromFieldReferencesAreCollected()
    {
        var field = new OperationId("behavior-operation:v1:field");
        var fieldSymbol = new SymbolId("symbol:v1:field");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(Operation(
                field,
                ExtractedOperationKind.FieldReference,
                "System.Int32",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                ImmutableArray<MethodId>.Empty,
                ImmutableArray<SymbolId>.Empty,
                ImmutableArray.Create(fieldSymbol))),
            ImmutableArray.Create(
                Block(0, [field], 1, None),
                Block(1, [], null, Exit)),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                1,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var (_, _, summary) = LocalFlowAnalyzer.Analyze(body, MethodFlowBuilder.Build(body).Snapshot);

        Assert.Contains(summary.StateReads, symbol => symbol == fieldSymbol);
    }

    [Fact]
    public void ControlDependencesAreDeterministic()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var value = new OperationId("behavior-operation:v1:value");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(value, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null)),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, Conditional, condition, [3], [0]),
                Block(2, [value], 4, None, null, [], [1]),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [2, 3])),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                4,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        Assert.NotEmpty(dependences);
        Assert.All(dependences, dependence => Assert.NotEqual(default, dependence.ControllingDecision));
    }

    [Fact]
    public void FalseBranchNodeIsMarkedControlledOnFalse()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var value = new OperationId("behavior-operation:v1:value");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(value, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null)),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, Conditional, condition, [3], [0]),
                Block(2, [value], 4, None, null, [], [1]),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [2, 3])),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                4,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        var dependence = Assert.Single(dependences);
        Assert.False(dependence.ControlledOnTrue);
        Assert.Contains(flow.Nodes.OfType<OperationFlowNode>(), node => node.Id == dependence.ControlledNode);
    }

    [Fact]
    public void PostMergeNodeHasNoControlDependence()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var trueValue = new OperationId("behavior-operation:v1:true-value");
        var mergeValue = new OperationId("behavior-operation:v1:merge-value");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(trueValue, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null),
                Operation(mergeValue, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null)),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, Conditional, condition, [3], [0]),
                Block(2, [trueValue], 4, None, null, [], [1]),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [mergeValue], 5, None, null, [], [2, 3]),
                Block(5, [], null, Exit, null, [], [4])),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                5,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        var mergeNode = Assert.Single(flow.Nodes.OfType<OperationFlowNode>(), node => node.Operation == mergeValue);
        Assert.DoesNotContain(dependences, dependence => dependence.ControlledNode == mergeNode.Id);
    }

    [Fact]
    public void BranchWithThrowSinkControlsBothSides()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var throwValue = new OperationId("behavior-operation:v1:throw-value");
        var returnValue = new OperationId("behavior-operation:v1:return-value");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(throwValue, ExtractedOperationKind.ExpressionStatement, "System.InvalidOperationException", null, null, null),
                Operation(returnValue, ExtractedOperationKind.ExpressionStatement, "System.Int32", null, null, null)),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, Conditional, condition, [3], [0]),
                Block(2, [throwValue], null, Throw, null, [], [1]),
                Block(3, [returnValue], 4, Return, null, [], [1]),
                Block(4, [], null, Exit, null, [], [3])),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                4,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        var throwNode = Assert.Single(flow.Nodes.OfType<OperationFlowNode>(), node => node.Operation == throwValue);
        var returnNode = Assert.Single(flow.Nodes.OfType<OperationFlowNode>(), node => node.Operation == returnValue);
        Assert.Contains(dependences, dependence => dependence.ControlledNode == throwNode.Id && !dependence.ControlledOnTrue);
        Assert.Contains(dependences, dependence => dependence.ControlledNode == returnNode.Id && dependence.ControlledOnTrue);
    }

    [Fact]
    public void NestedOperandInControlledBlockIsSelected()
    {
        var condition = new OperationId("behavior-operation:v1:cond");
        var nested = new OperationId("behavior-operation:v1:nested");
        var returnLike = new OperationId("behavior-operation:v1:return-like");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(
                Operation(condition, ExtractedOperationKind.Binary, "System.Boolean", null, null, null),
                Operation(nested, ExtractedOperationKind.LocalReference, "System.Int32", null, null, null),
                Operation(returnLike, ExtractedOperationKind.Return, "System.Int32", null, null, null,
                    referencedOperands: [nested])),
            ImmutableArray.Create(
                Block(0, [], 1, None),
                Block(1, [], 2, Conditional, condition, [3], [0]),
                Block(2, [returnLike], 4, None, null, [], [1]),
                Block(3, [], 4, None, null, [], [1]),
                Block(4, [], null, Exit, null, [], [2, 3])),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                4,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, dependences, _) = LocalFlowAnalyzer.Analyze(body, flow);

        var nestedNode = Assert.Single(flow.Nodes.OfType<OperationFlowNode>(), node => node.Operation == nested);
        var dependence = Assert.Single(dependences);
        Assert.Equal(nestedNode.Id, dependence.ControlledNode);
        Assert.False(dependence.ControlledOnTrue);
    }

    [Fact]
    public void SummaryIsIncompleteWhenUnsupportedOperationsPresent()
    {
        var unknown = new OperationId("behavior-operation:v1:unknown");
        var body = new ExtractedMethodBody(
            Method,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            [],
            [],
            ImmutableArray.Create(Operation(unknown, ExtractedOperationKind.Unknown, "System.Object", null, null, null)),
            ImmutableArray.Create(
                Block(0, [unknown], 1, None),
                Block(1, [], null, Exit)),
            ImmutableArray.Create(new ExtractedExceptionRegion(
                new FlowRegionId("flow-region:v1:root"),
                ExtractedRegionKind.Root,
                null,
                0,
                0,
                1,
                null,
                [],
                CertaintyLevel.Exact)),
            []);

        var flow = MethodFlowBuilder.Build(body).Snapshot;
        var (_, _, summary) = LocalFlowAnalyzer.Analyze(body, flow);

        Assert.False(summary.IsComplete);
        Assert.Equal(CertaintyLevel.Conservative, summary.Certainty);
    }

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

    private static ExtractedOperation Operation(
        OperationId id,
        ExtractedOperationKind kind,
        string type,
        string? constantValue,
        string? localName,
        int? parameterOrdinal,
        ExtractedAssignmentPayload? assignment = null,
        ExtractedInvocationPayload? invocation = null,
        ExtractedConversionPayload? conversion = null,
        ExtractedAwaitPayload? awaitPayload = null,
        ExtractedReturnPayload? returnPayload = null,
        ImmutableArray<MethodId> referencedMethods = default,
        ImmutableArray<SymbolId> referencedTypes = default,
        ImmutableArray<SymbolId> referencedMembers = default,
        ImmutableArray<OperationId> referencedOperands = default) =>
        new(
            id,
            Method,
            kind,
            null,
            referencedOperands.IsDefault ? [] : referencedOperands,
            0,
            type,
            constantValue,
            false,
            true,
            referencedMethods.IsDefault ? [] : referencedMethods,
            referencedTypes.IsDefault ? [] : referencedTypes,
            referencedMembers.IsDefault ? [] : referencedMembers,
            invocation,
            assignment,
            conversion,
            awaitPayload,
            returnPayload,
            null,
            localName,
            parameterOrdinal,
            [],
            CertaintyLevel.Exact);

    private const ExtractedBlockTerminalKind None = ExtractedBlockTerminalKind.None;
    private const ExtractedBlockTerminalKind Conditional = ExtractedBlockTerminalKind.Conditional;
    private const ExtractedBlockTerminalKind Return = ExtractedBlockTerminalKind.Return;
    private const ExtractedBlockTerminalKind Throw = ExtractedBlockTerminalKind.Throw;
    private const ExtractedBlockTerminalKind Exit = ExtractedBlockTerminalKind.Exit;
}
