using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Behavior;

/// <summary>
/// Builds a definition-based local value graph, direct control dependence, and a structural method
/// summary from an extracted method body.
/// </summary>
public static class LocalFlowAnalyzer
{
    public static (LocalValueGraph Graph, ImmutableArray<ControlDependence> ControlDependences, MethodSummary Summary)
        Analyze(ExtractedMethodBody body, MethodFlowSnapshot flow)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(flow);

        var nodes = ImmutableArray.CreateBuilder<ValueNode>();
        var edges = ImmutableArray.CreateBuilder<ValueEdge>();
        var edgeOrdinal = 0;
        var operationsById = body.Operations.ToDictionary(operation => operation.Id);
        var blocksByOrdinal = body.Blocks.ToDictionary(block => block.Ordinal);
        var valueByOperation = new Dictionary<OperationId, ValueNodeId>();
        var currentDefinitions = new Dictionary<string, ValueNodeId>(StringComparer.Ordinal);
        var stateReads = new List<SymbolId>();
        var stateWrites = new List<SymbolId>();
        var stateWriteValueNodes = new List<ValueNodeId>();
        var returnedValues = new List<ValueNodeId>();

        var blockByOperation = body.Blocks
            .SelectMany(block => block.Operations.Select(operationId => (operationId, block.Ordinal)))
            .GroupBy(pair => pair.operationId)
            .ToDictionary(group => group.Key, group => group.Min(pair => pair.Ordinal));

        foreach (var operation in body.Operations.OrderBy(operation => operation.EvaluationOrdinal))
        {
            if (!blockByOperation.TryGetValue(operation.Id, out var blockOrdinal))
            {
                blockOrdinal = 0;
            }

            CreateValueNodeFor(
                body,
                operation,
                blockOrdinal,
                valueByOperation,
                nodes,
                stateReads);
        }

        foreach (var operation in body.Operations.OrderBy(operation => operation.EvaluationOrdinal))
        {
            if (!blockByOperation.TryGetValue(operation.Id, out var blockOrdinal))
            {
                blockOrdinal = 0;
            }

            WireValueEdges(
                body,
                operation,
                blockOrdinal,
                operationsById,
                valueByOperation,
                currentDefinitions,
                edges,
                stateWrites,
                stateWriteValueNodes,
                returnedValues,
                ref edgeOrdinal);
        }

        foreach (var returnNode in flow.Nodes.OfType<ReturnFlowNode>())
        {
            if (returnNode.Value is { } returnValue && valueByOperation.TryGetValue(returnValue, out var returnedValueNode))
            {
                returnedValues.Add(returnedValueNode);
            }
        }

        var graph = new LocalValueGraph(
            nodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            edges.OrderBy(edge => edge.Id.Value, StringComparer.Ordinal).ToImmutableArray());
        var controlDependences = ComputeControlDependences(body, flow);
        var summary = BuildSummary(body, flow, graph, stateReads, stateWrites, stateWriteValueNodes, returnedValues);
        return (graph, controlDependences, summary);
    }

    private static void CreateValueNodeFor(
        ExtractedMethodBody body,
        ExtractedOperation operation,
        int blockOrdinal,
        Dictionary<OperationId, ValueNodeId> valueByOperation,
        ImmutableArray<ValueNode>.Builder nodes,
        List<SymbolId> stateReads)
    {
        ValueNode? node = operation.Kind switch
        {
            ExtractedOperationKind.ParameterReference when operation.ParameterOrdinal is { } parameterOrdinal =>
                CreateValueNode(
                    body.Method,
                    ValueNodeKind.Parameter,
                    blockOrdinal,
                    operation.EvaluationOrdinal,
                    "parameter",
                    operation.TypeDescriptor,
                    null,
                    parameterOrdinal,
                    null,
                    operation.Evidence),
            ExtractedOperationKind.Literal => CreateValueNode(
                body.Method,
                ValueNodeKind.Constant,
                blockOrdinal,
                operation.EvaluationOrdinal,
                "constant",
                operation.TypeDescriptor,
                null,
                null,
                operation.ConstantValue,
                operation.Evidence),
            ExtractedOperationKind.LocalReference when operation.LocalName is { } localName => CreateValueNode(
                body.Method,
                ValueNodeKind.OperationResult,
                blockOrdinal,
                operation.EvaluationOrdinal,
                $"local:{localName}",
                operation.TypeDescriptor,
                localName,
                null,
                null,
                operation.Evidence),
            ExtractedOperationKind.Assignment or ExtractedOperationKind.CompoundAssignment
                when operation.Assignment is { } assignment
                && operation.Assignment is not null => CreateValueNode(
                body.Method,
                ValueNodeKind.LocalDefinition,
                blockOrdinal,
                operation.EvaluationOrdinal,
                "definition",
                operation.TypeDescriptor,
                null,
                null,
                null,
                operation.Evidence),
            ExtractedOperationKind.Invocation => CreateValueNode(
                body.Method,
                ValueNodeKind.InvocationResult,
                blockOrdinal,
                operation.EvaluationOrdinal,
                "invocation",
                operation.TypeDescriptor,
                null,
                null,
                null,
                operation.Evidence),
            ExtractedOperationKind.FieldReference or ExtractedOperationKind.PropertyReference
                when operation.ReferencedMembers.Length > 0 => CreateValueNode(
                body.Method,
                ValueNodeKind.MemberRead,
                blockOrdinal,
                operation.EvaluationOrdinal,
                "member-read",
                operation.TypeDescriptor,
                null,
                null,
                null,
                operation.Evidence),
            ExtractedOperationKind.Conversion => CreateValueNode(
                body.Method,
                ValueNodeKind.OperationResult,
                blockOrdinal,
                operation.EvaluationOrdinal,
                "conversion",
                operation.Conversion?.ToType ?? operation.TypeDescriptor,
                null,
                null,
                null,
                operation.Evidence),
            _ when operation.Operands.Length > 0
                    && operation.Kind is not (ExtractedOperationKind.ExpressionStatement
                        or ExtractedOperationKind.Return
                        or ExtractedOperationKind.Throw) => CreateValueNode(
                body.Method,
                ValueNodeKind.OperationResult,
                blockOrdinal,
                operation.EvaluationOrdinal,
                "result",
                operation.TypeDescriptor,
                null,
                null,
                null,
                operation.Evidence),
            _ => null,
        };

        if (node is not null)
        {
            nodes.Add(node);
            valueByOperation[operation.Id] = node.Id;
            if (operation.Kind is ExtractedOperationKind.FieldReference or ExtractedOperationKind.PropertyReference
                && operation.ReferencedMembers.Length > 0)
            {
                stateReads.Add(operation.ReferencedMembers[0]);
            }
        }
    }

    private static void WireValueEdges(
        ExtractedMethodBody body,
        ExtractedOperation operation,
        int blockOrdinal,
        Dictionary<OperationId, ExtractedOperation> operationsById,
        Dictionary<OperationId, ValueNodeId> valueByOperation,
        Dictionary<string, ValueNodeId> currentDefinitions,
        ImmutableArray<ValueEdge>.Builder edges,
        List<SymbolId> stateWrites,
        List<ValueNodeId> stateWriteValueNodes,
        List<ValueNodeId> returnedValues,
        ref int edgeOrdinal)
    {
        switch (operation.Kind)
        {
            case ExtractedOperationKind.LocalReference:
                if (operation.LocalName is { } localName
                    && valueByOperation.TryGetValue(operation.Id, out var localUse)
                    && currentDefinitions.TryGetValue(localName, out var definition))
                {
                    edges.Add(CreateValueEdge(
                        body.Method,
                        definition,
                        localUse,
                        ValueEdgeKind.CaptureCollapse,
                        operation.Id,
                        operation.Evidence,
                        ref edgeOrdinal));
                }

                break;
            case ExtractedOperationKind.Assignment:
            case ExtractedOperationKind.CompoundAssignment:
                if (operation.Assignment is { } assignment
                    && operationsById.TryGetValue(assignment.Target, out var targetOperation)
                    && valueByOperation.TryGetValue(operation.Id, out var definitionNode))
                {
                    if (targetOperation.LocalName is { } targetName
                        && valueByOperation.TryGetValue(assignment.Value, out var sourceValue))
                    {
                        edges.Add(CreateValueEdge(
                            body.Method,
                            sourceValue,
                            definitionNode,
                            ValueEdgeKind.Assignment,
                            operation.Id,
                            operation.Evidence,
                            ref edgeOrdinal));
                        currentDefinitions[targetName] = definitionNode;
                    }

                    if (targetOperation.Kind is ExtractedOperationKind.FieldReference
                            or ExtractedOperationKind.PropertyReference
                        && targetOperation.ReferencedMembers.Length > 0
                        && valueByOperation.TryGetValue(assignment.Value, out var writtenValue))
                    {
                        stateWrites.Add(targetOperation.ReferencedMembers[0]);
                        stateWriteValueNodes.Add(writtenValue);
                    }
                }

                break;
            case ExtractedOperationKind.Invocation:
                if (valueByOperation.TryGetValue(operation.Id, out var invocationNode)
                    && operation.Invocation is { } invocationPayload)
                {
                    foreach (var argument in invocationPayload.Arguments)
                    {
                        if (valueByOperation.TryGetValue(argument, out var argumentValue))
                        {
                            edges.Add(CreateValueEdge(
                                body.Method,
                                argumentValue,
                                invocationNode,
                                ValueEdgeKind.Argument,
                                operation.Id,
                                operation.Evidence,
                                ref edgeOrdinal));
                        }
                    }
                }

                break;
            case ExtractedOperationKind.Conversion:
                if (valueByOperation.TryGetValue(operation.Id, out var conversionNode)
                    && operation.Operands.Length == 1
                    && valueByOperation.TryGetValue(operation.Operands[0], out var sourceConversion))
                {
                    edges.Add(CreateValueEdge(
                        body.Method,
                        sourceConversion,
                        conversionNode,
                        ValueEdgeKind.Conversion,
                        operation.Id,
                        operation.Evidence,
                        ref edgeOrdinal));
                }

                break;
            case ExtractedOperationKind.Return:
                if (operation.Return is { } returnPayload && returnPayload.Value is { } returnValue
                    && valueByOperation.TryGetValue(returnValue, out var returnedValue))
                {
                    returnedValues.Add(returnedValue);
                }

                break;
            default:
                if (valueByOperation.TryGetValue(operation.Id, out var resultNode)
                    && operation.Kind is not (ExtractedOperationKind.ExpressionStatement
                        or ExtractedOperationKind.Return
                        or ExtractedOperationKind.Throw))
                {
                    foreach (var operand in operation.Operands)
                    {
                        if (valueByOperation.TryGetValue(operand, out var operandValue))
                        {
                            edges.Add(CreateValueEdge(
                                body.Method,
                                operandValue,
                                resultNode,
                                ValueEdgeKind.Operand,
                                operation.Id,
                                operation.Evidence,
                                ref edgeOrdinal));
                        }
                    }
                }

                break;
        }
    }

    private static ImmutableArray<ControlDependence> ComputeControlDependences(
        ExtractedMethodBody body,
        MethodFlowSnapshot flow)
    {
        var postDominators = ComputePostDominators(body);
        var blockByOperation = BuildBlockByOperation(body);
        var representedTerminals = BuildRepresentedTerminals(body);
        var dependences = ImmutableArray.CreateBuilder<ControlDependence>();
        var decisions = flow.Nodes
            .OfType<DecisionFlowNode>()
            .OrderBy(node => node.Id.Value, StringComparer.Ordinal)
            .ToArray();

        foreach (var decision in decisions)
        {
            var decisionBlock = body.Blocks.FirstOrDefault(block => block.BranchCondition == decision.Condition);
            if (decisionBlock is null)
            {
                continue;
            }

            var trueSuccessors = decisionBlock.ConditionalSuccessors
                .Where(postDominators.ContainsKey)
                .ToArray();
            var falseSuccessors = decisionBlock.FallThroughSuccessor is { } fallThrough
                && postDominators.ContainsKey(fallThrough)
                ? new[] { fallThrough }
                : [];

            foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
            {
                if (block.Ordinal == decisionBlock.Ordinal)
                {
                    continue;
                }

                bool postTrue = trueSuccessors.Any(successor => postDominators[successor].Contains(block.Ordinal));
                bool postFalse = falseSuccessors.Any(successor => postDominators[successor].Contains(block.Ordinal));
                bool postDecision = postDominators[decisionBlock.Ordinal].Contains(block.Ordinal);
                if (postDecision || postTrue == postFalse)
                {
                    continue;
                }

                // Every eligible represented node of the controlled block receives its own direct
                // dependence (architecture decision): operation/invocation/await nodes, the represented Return or
                // Throw terminal of the block, and a nested DecisionFlowNode. Entry, exit, loop, and
                // unknown-operation nodes never become targets, and an operation-derived duplicate
                // return/throw node is never the represented terminal.
                foreach (var controlledNode in EligibleControlledNodes(flow, block, blockByOperation, representedTerminals))
                {
                    dependences.Add(new ControlDependence(
                        decision.Id,
                        controlledNode.Id,
                        postTrue,
                        decision.Evidence,
                        CertaintyLevel.Exact));
                }
            }
        }

        return dependences.OrderBy(dependence => dependence.ControllingDecision.Value, StringComparer.Ordinal)
            .ThenBy(dependence => dependence.ControlledNode.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    /// Enumerates every eligible represented flow node that belongs to one controlled basic block.
    /// Operation, invocation, and await nodes match through their exact operation anchor; the
    /// represented Return/Throw terminal matches the terminal node identity the method-flow builder
    /// created for that block; a nested decision matches the block's branch condition. Nodes are
    /// returned in canonical identity order.
    /// </summary>
    private static IEnumerable<FlowNode> EligibleControlledNodes(
        MethodFlowSnapshot flow,
        ExtractedBasicBlock block,
        Dictionary<OperationId, int> blockByOperation,
        Dictionary<int, FlowNodeId> representedTerminals)
    {
        foreach (var node in flow.Nodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal))
        {
            switch (node)
            {
                case OperationFlowNode or InvocationFlowNode or AwaitFlowNode
                    when MatchesBlock(node, block.Ordinal, blockByOperation):
                    yield return node;
                    break;
                case ReturnFlowNode or ThrowFlowNode
                    when representedTerminals.TryGetValue(block.Ordinal, out var terminalId) && node.Id == terminalId:
                    yield return node;
                    break;
                case DecisionFlowNode decisionNode when decisionNode.Condition == block.BranchCondition:
                    yield return node;
                    break;
            }
        }
    }

    /// <summary>
    /// Maps every block ordinal to the flow-node identity of its represented Return/Throw terminal.
    /// The identity reuses the exact descriptor the method-flow builder used for terminal nodes
    /// (<c>kind</c> of "Return", "Throw", or "Rethrow" with role "terminal") so an operation-derived
    /// duplicate return/throw node can never be mistaken for the block terminal.
    /// </summary>
    private static Dictionary<int, FlowNodeId> BuildRepresentedTerminals(ExtractedMethodBody body)
    {
        var result = new Dictionary<int, FlowNodeId>();
        foreach (var block in body.Blocks)
        {
            var kind = block.Terminal switch
            {
                ExtractedBlockTerminalKind.Return => "Return",
                ExtractedBlockTerminalKind.Throw => "Throw",
                ExtractedBlockTerminalKind.Rethrow => "Rethrow",
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }

            result[block.Ordinal] = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                body.Method,
                kind,
                block.Ordinal,
                0,
                "terminal"));
        }

        return result;
    }

    private static Dictionary<OperationId, int> BuildBlockByOperation(ExtractedMethodBody body)
    {
        var operationsById = body.Operations.ToDictionary(operation => operation.Id);
        var blockByOperation = new Dictionary<OperationId, int>();
        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            var pending = new Stack<OperationId>();
            foreach (var operationId in block.Operations.Reverse())
            {
                pending.Push(operationId);
            }

            while (pending.TryPop(out var operationId))
            {
                if (!blockByOperation.TryAdd(operationId, block.Ordinal)
                    || !operationsById.TryGetValue(operationId, out var operation))
                {
                    continue;
                }

                foreach (var operand in operation.Operands)
                {
                    pending.Push(operand);
                }
            }
        }

        return blockByOperation;
    }

    private static bool MatchesBlock(
        FlowNode node,
        int blockOrdinal,
        Dictionary<OperationId, int> blockByOperation)
    {
        var operationId = node switch
        {
            OperationFlowNode operationNode => operationNode.Operation,
            InvocationFlowNode invocationNode => invocationNode.Operation,
            AwaitFlowNode awaitNode => awaitNode.Operand,
            _ => default(OperationId?),
        };
        return operationId is { } operation
            && blockByOperation.TryGetValue(operation, out var containingBlock)
            && containingBlock == blockOrdinal;
    }

    private static Dictionary<int, HashSet<int>> ComputePostDominators(ExtractedMethodBody body)
    {
        var ordinals = body.Blocks.Select(block => block.Ordinal).Order().ToArray();
        var exitOrdinal = body.Blocks.FirstOrDefault(block => block.Terminal == ExtractedBlockTerminalKind.Exit)?.Ordinal
            ?? ordinals[^1];
        var all = new HashSet<int>(ordinals);
        var postDominators = ordinals.ToDictionary(
            ordinal => ordinal,
            ordinal =>
            {
                if (ordinal == exitOrdinal)
                {
                    return new HashSet<int> { exitOrdinal };
                }

                var block = body.Blocks.First(candidate => candidate.Ordinal == ordinal);
                bool isTerminalSink = block.FallThroughSuccessor is null && block.ConditionalSuccessors.Length == 0;
                return isTerminalSink ? new HashSet<int> { ordinal } : new HashSet<int>(all);
            });

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var ordinal in ordinals.Where(ordinal => ordinal != exitOrdinal))
            {
                var block = body.Blocks.First(candidate => candidate.Ordinal == ordinal);
                var successors = new List<int>();
                if (block.FallThroughSuccessor is { } fallThrough)
                {
                    successors.Add(fallThrough);
                }

                successors.AddRange(block.ConditionalSuccessors);
                successors = successors.Where(successor => postDominators.ContainsKey(successor)).Distinct().ToList();
                if (successors.Count == 0)
                {
                    continue;
                }

                var intersection = new HashSet<int>(postDominators[successors[0]]);
                foreach (var successor in successors.Skip(1))
                {
                    intersection.IntersectWith(postDominators[successor]);
                }

                intersection.Add(ordinal);
                if (!intersection.SetEquals(postDominators[ordinal]))
                {
                    postDominators[ordinal] = intersection;
                    changed = true;
                }
            }
        }

        return postDominators;
    }

    private static MethodSummary BuildSummary(
        ExtractedMethodBody body,
        MethodFlowSnapshot flow,
        LocalValueGraph graph,
        List<SymbolId> stateReads,
        List<SymbolId> stateWrites,
        List<ValueNodeId> stateWriteValueNodes,
        List<ValueNodeId> returnedValues)
    {
        var parameterFlows = ImmutableArray.CreateBuilder<ParameterFlow>();
        for (var index = 0; index < body.Parameters.Length; index++)
        {
            var parameter = body.Parameters[index];
            var flowsToReturn = parameterFlowsToReturn(body, graph, index, returnedValues);
            var influencesStateWrite = parameterInfluencesStateWrite(graph, index, stateWriteValueNodes);
            parameterFlows.Add(new ParameterFlow(
                index,
                parameter.Name,
                flowsToReturn,
                influencesStateWrite,
                body.Evidence,
                flowsToReturn || influencesStateWrite ? CertaintyLevel.Conservative : CertaintyLevel.Exact));
        }

        bool hasUnsupportedOperations = flow.Nodes.Any(node => node.Kind == FlowNodeKind.UnknownOperation)
            || flow.Outcomes.Any(outcome => outcome.Kind == FlowOutcomeKind.Unknown);
        return new MethodSummary(
            body.Method,
            body.BodyFingerprint,
            parameterFlows.ToImmutable(),
            stateReads.Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray(),
            stateWrites.Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray(),
            flow.Outcomes,
            IsComplete: !hasUnsupportedOperations,
            flow.Outcomes.Any(outcome => outcome.Kind == FlowOutcomeKind.Unknown)
                ? CertaintyLevel.Unknown
                : hasUnsupportedOperations
                    ? CertaintyLevel.Conservative
                    : CertaintyLevel.Exact,
            body.Evidence);
    }

    private static bool parameterFlowsToReturn(
        ExtractedMethodBody body,
        LocalValueGraph graph,
        int parameterOrdinal,
        List<ValueNodeId> returnedValues)
    {
        var parameterValues = graph.Nodes
            .Where(node => node.Kind == ValueNodeKind.Parameter && node.ParameterOrdinal == parameterOrdinal)
            .Select(node => node.Id)
            .ToArray();
        if (parameterValues.Length == 0 || returnedValues.Count == 0)
        {
            return false;
        }

        return parameterValues.Any(parameterValue => IsReachable(graph, parameterValue, returnedValues));
    }

    private static bool parameterInfluencesStateWrite(
        LocalValueGraph graph,
        int parameterOrdinal,
        List<ValueNodeId> stateWriteValueNodes)
    {
        var parameterValues = graph.Nodes
            .Where(node => node.Kind == ValueNodeKind.Parameter && node.ParameterOrdinal == parameterOrdinal)
            .Select(node => node.Id)
            .ToArray();
        if (parameterValues.Length == 0 || stateWriteValueNodes.Count == 0)
        {
            return false;
        }

        return parameterValues.Any(parameterValue => stateWriteValueNodes.Any(writeValue =>
            IsReachable(graph, parameterValue, [writeValue])));
    }

    private static bool IsReachable(
        LocalValueGraph graph,
        ValueNodeId source,
        List<ValueNodeId> targets)
    {
        var visited = new HashSet<ValueNodeId>();
        var pending = new Stack<ValueNodeId>();
        pending.Push(source);
        while (pending.TryPop(out var current))
        {
            if (targets.Contains(current))
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var target in graph.Edges
                         .Where(edge => edge.Source == current)
                         .Select(edge => edge.Target))
            {
                pending.Push(target);
            }
        }

        return false;
    }

    private static ValueNode CreateValueNode(
        MethodId method,
        ValueNodeKind kind,
        int blockOrdinal,
        int evaluationOrdinal,
        string role,
        string type,
        string? name,
        int? parameterOrdinal,
        string? constantValue,
        ImmutableArray<EvidenceRef> evidence)
    {
        var id = StableIdentity.CreateValueNodeId(new ValueNodeIdentityDescriptor(
            method,
            kind.ToString(),
            blockOrdinal,
            evaluationOrdinal,
            role));
        return new ValueNode(
            id,
            method,
            kind,
            type,
            name,
            DefiningOperation: null,
            parameterOrdinal,
            constantValue,
            evidence,
            CertaintyLevel.Exact);
    }

    private static ValueEdge CreateValueEdge(
        MethodId method,
        ValueNodeId source,
        ValueNodeId target,
        ValueEdgeKind kind,
        OperationId? guard,
        ImmutableArray<EvidenceRef> evidence,
        ref int edgeOrdinal)
    {
        var id = StableIdentity.CreateValueEdgeId(new ValueEdgeIdentityDescriptor(
            method,
            source.Value,
            target.Value,
            kind.ToString(),
            edgeOrdinal));
        edgeOrdinal++;
        return new ValueEdge(id, method, source, target, kind, guard, evidence, CertaintyLevel.Exact);
    }
}
