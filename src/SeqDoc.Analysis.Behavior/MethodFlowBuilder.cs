using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Behavior;
/// <summary>
/// Normalizes one extracted method body into a method flow graph with nodes, edges, regions, loops,
/// and reconciled structural outcomes.
/// </summary>
public static class MethodFlowBuilder
{
    public static MethodFlowBuildResult Build(ExtractedMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var nodes = ImmutableArray.CreateBuilder<FlowNode>();
        var edges = ImmutableArray.CreateBuilder<FlowEdge>();
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        var edgeOrdinal = 0;

        var operationsById = body.Operations.ToDictionary(operation => operation.Id);
        var blocksByOrdinal = body.Blocks.ToDictionary(block => block.Ordinal);
        var exitBlock = body.Blocks.FirstOrDefault(block => block.Terminal == ExtractedBlockTerminalKind.Exit);

        var entryId = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
            body.Method, "Entry", 0, 0, "entry"));
        var exitId = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
            body.Method, "Exit", int.MaxValue, int.MaxValue, "exit"));
        nodes.Add(new EntryFlowNode(entryId, body.Method, body.Evidence, CertaintyLevel.Exact));
        nodes.Add(new ExitFlowNode(exitId, body.Method, body.Evidence, CertaintyLevel.Exact));

        var blockHeads = new Dictionary<int, FlowNodeId>();
        var blockTails = new Dictionary<int, FlowNodeId>();

        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            if (exitBlock is not null && block.Ordinal == exitBlock.Ordinal)
            {
                blockHeads[block.Ordinal] = exitId;
                blockTails[block.Ordinal] = exitId;
                continue;
            }

            FlowNodeId? firstNodeId = null;
            FlowNodeId? lastNodeId = null;
            var blockOperationIds = CollectBlockOperations(block, operationsById);
            foreach (var operationId in blockOperationIds)
            {
                if (!operationsById.TryGetValue(operationId, out var operation))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "BD2001",
                        "A block references an operation outside its own body.",
                        body.Method.Value,
                        block.Ordinal));
                    continue;
                }

                var node = CreateOperationNode(body.Method, operation, block.Ordinal);
                nodes.Add(node);
                firstNodeId ??= node.Id;
                if (lastNodeId is { } previous)
                {
                    edges.Add(CreateEdge(body.Method, previous, node.Id, FlowEdgeKind.Normal, null, ref edgeOrdinal));
                }

                lastNodeId = node.Id;
            }

            if (block.BranchCondition is { } conditionId)
            {
                var decisionId = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    body.Method, "Decision", block.Ordinal, 0, "decision"));
                nodes.Add(new DecisionFlowNode(
                    decisionId,
                    body.Method,
                    conditionId,
                    block.Evidence,
                    CertaintyLevel.Exact));
                if (lastNodeId is { } prior)
                {
                    edges.Add(CreateEdge(body.Method, prior, decisionId, FlowEdgeKind.Normal, null, ref edgeOrdinal));
                }

                firstNodeId ??= decisionId;
                lastNodeId = decisionId;
            }

            var terminalNode = CreateTerminalNode(body.Method, block, operationsById);
            if (terminalNode is not null)
            {
                nodes.Add(terminalNode);
                if (lastNodeId is { } prior)
                {
                    var kind = terminalNode.Kind == FlowNodeKind.Return
                        ? FlowEdgeKind.Return
                        : FlowEdgeKind.Throw;
                    edges.Add(CreateEdge(body.Method, prior, terminalNode.Id, kind, null, ref edgeOrdinal));
                }

                firstNodeId ??= terminalNode.Id;
                lastNodeId = terminalNode.Id;
            }

            if (block.Ordinal == 0 && firstNodeId is null)
            {
                firstNodeId = entryId;
            }

            blockHeads[block.Ordinal] = firstNodeId ?? lastNodeId ?? entryId;
            blockTails[block.Ordinal] = lastNodeId ?? firstNodeId ?? entryId;
        }

        var dominators = ComputeDominators(body, blocksByOrdinal);
        AddBlockEdges(
            body,
            blocksByOrdinal,
            blockHeads,
            blockTails,
            entryId,
            exitId,
            dominators,
            edges,
            ref edgeOrdinal,
            diagnostics);

        var regions = ImmutableArray.CreateBuilder<FlowRegion>();
        foreach (var region in body.Regions.OrderBy(region => region.Ordinal))
        {
            AddFlowRegion(body, region, blocksByOrdinal, blockHeads, blockTails, regions);
        }

        var loopRegions = DetectLoops(
            body,
            blocksByOrdinal,
            blockHeads,
            blockTails,
            nodes,
            regions,
            body.Evidence,
            ref edgeOrdinal);
        if (exitBlock is null)
        {
            diagnostics.Add(CreateDiagnostic(
                "BD2004",
                "The method flow has no exit block.",
                body.Method.Value,
                -1));
        }

        var outcomes = ReconcileTerminals(body, blocksByOrdinal, blockTails, exitId, edges, regions, diagnostics);

        var preliminary = new MethodFlowSnapshot(
            body.Method,
            body.BodyFingerprint,
            nodes.OrderBy(node => node.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            edges.OrderBy(edge => edge.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            regions.OrderBy(region => region.Ordinal).ToImmutableArray(),
            outcomes.OrderBy(outcome => outcome.BlockOrdinal ?? -1).ToImmutableArray(),
            new LocalValueGraph([], []),
            [],
            null,
            diagnostics.ToImmutable(),
            string.Empty);
        var (graph, dependences, summary) = LocalFlowAnalyzer.Analyze(body, preliminary);
        var snapshot = preliminary with
        {
            ValueGraph = graph,
            ControlDependences = dependences,
            Summary = summary,
        };
        return new MethodFlowBuildResult(
            snapshot with { FlowFingerprint = MethodFlowFingerprint.Compute(snapshot) },
            snapshot.Diagnostics);
    }

    private static FlowNode CreateOperationNode(MethodId method, ExtractedOperation operation, int blockOrdinal)
    {
        var id = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
            method,
            operation.Kind.ToString(),
            blockOrdinal,
            operation.EvaluationOrdinal,
            "operation"));
        return operation.Kind switch
        {
            ExtractedOperationKind.Invocation or ExtractedOperationKind.DynamicInvocation or ExtractedOperationKind.EventAssignment => new InvocationFlowNode(
                id,
                method,
                operation.Id,
                operation.Invocation?.Target,
                operation.Invocation?.IsDispatchable ?? false,
                operation.Invocation?.IsDelegateOrEventInvoke ?? false,
                 operation.Invocation?.IsStatic ?? false,
                 operation.Invocation?.IsConstructor ?? false,
                 operation.Invocation?.IsDynamic ?? false,
                 operation.Evidence,
                 operation.Certainty,
                 operation.Invocation?.TargetContainingTypeName,
                  operation.Invocation?.TargetMethodName,
                  operation.Invocation?.IsInsideNestedFunction ?? false,
                   operation.IsSourceBacked,
                   operation.Invocation?.IsLoadedProjectTarget ?? false,
                   blockOrdinal,
                   operation.EvaluationOrdinal,
                   operation.Invocation?.TargetAssemblyName,
                   operation.Invocation?.IsPlatformTarget ?? false),
            ExtractedOperationKind.ObjectCreation => new InvocationFlowNode(
                id,
                method,
                operation.Id,
                operation.ReferencedMethods.FirstOrDefault(),
                IsDispatchable: false,
                IsDelegateOrEventInvoke: false,
                IsStatic: false,
                 IsConstructor: true,
                 IsDynamic: false,
                 operation.Evidence,
                 operation.Certainty,
                 IsSourceBacked: operation.IsSourceBacked),
            ExtractedOperationKind.Await => new AwaitFlowNode(
                id,
                method,
                operation.Await?.Operand ?? operation.Id,
                operation.Evidence,
                operation.Certainty),
            ExtractedOperationKind.Return => new ReturnFlowNode(
                id,
                method,
                operation.Return?.Value,
                operation.Evidence,
                operation.Certainty),
            ExtractedOperationKind.Throw => new ThrowFlowNode(
                id,
                method,
                operation.Throw?.Exception,
                operation.Throw?.IsRethrow ?? false,
                operation.Evidence,
                operation.Certainty),
            ExtractedOperationKind.Unknown => new UnknownOperationFlowNode(
                id,
                method,
                operation.Id,
                operation.Evidence,
                operation.Certainty),
            _ => new OperationFlowNode(
                id,
                method,
                operation.Id,
                operation.Kind,
                operation.Evidence,
                operation.Certainty),
        };
    }

    private static OperationId[] CollectBlockOperations(
        ExtractedBasicBlock block,
        Dictionary<OperationId, ExtractedOperation> operationsById)
    {
        var collected = new List<OperationId>();
        var visited = new HashSet<OperationId>();
        var pending = new Stack<OperationId>();
        foreach (var operationId in block.Operations.Reverse())
        {
            pending.Push(operationId);
        }

        while (pending.TryPop(out var operationId))
        {
            if (!visited.Add(operationId))
            {
                continue;
            }

            collected.Add(operationId);
            if (operationsById.TryGetValue(operationId, out var operation))
            {
                foreach (var operand in operation.Operands)
                {
                    pending.Push(operand);
                }
            }
        }

        return collected
            .OrderBy(operationId => operationsById[operationId].EvaluationOrdinal)
            .ToArray();
    }

    private static FlowNode? CreateTerminalNode(
        MethodId method,
        ExtractedBasicBlock block,
        Dictionary<OperationId, ExtractedOperation> operationsById)
    {
        return block.Terminal switch
        {
            ExtractedBlockTerminalKind.Return => new ReturnFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    method, "Return", block.Ordinal, 0, "terminal")),
                method,
                FindTerminalValue(block, operationsById),
                block.Evidence,
                CertaintyLevel.Exact),
            ExtractedBlockTerminalKind.Throw => new ThrowFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    method, "Throw", block.Ordinal, 0, "terminal")),
                method,
                FindTerminalValue(block, operationsById),
                IsRethrow: false,
                block.Evidence,
                CertaintyLevel.Exact),
            ExtractedBlockTerminalKind.Rethrow => new ThrowFlowNode(
                StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    method, "Rethrow", block.Ordinal, 0, "terminal")),
                method,
                null,
                IsRethrow: true,
                block.Evidence,
                CertaintyLevel.Exact),
            _ => null,
        };
    }

    private static OperationId? FindTerminalValue(
        ExtractedBasicBlock block,
        Dictionary<OperationId, ExtractedOperation> operationsById)
    {
        foreach (var operationId in block.Operations)
        {
            if (operationsById.TryGetValue(operationId, out var operation)
                && operation.Kind is not (ExtractedOperationKind.Return or ExtractedOperationKind.Throw))
            {
                return operationId;
            }
        }

        return null;
    }

    private static void AddBlockEdges(
        ExtractedMethodBody body,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<int, FlowNodeId> blockHeads,
        Dictionary<int, FlowNodeId> blockTails,
        FlowNodeId entryId,
        FlowNodeId exitId,
        Dictionary<int, HashSet<int>> dominators,
        ImmutableArray<FlowEdge>.Builder edges,
        ref int edgeOrdinal,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics)
    {
        if (blockHeads.TryGetValue(0, out var entryHead) && entryHead != entryId)
        {
            edges.Add(CreateEdge(body.Method, entryId, entryHead, FlowEdgeKind.Normal, null, ref edgeOrdinal));
        }

        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            var tail = blockTails[block.Ordinal];
            var targets = new List<(FlowNodeId Target, FlowEdgeKind Kind, OperationId? Guard)>();
            if (block.Terminal == ExtractedBlockTerminalKind.Conditional)
            {
                foreach (var successor in block.ConditionalSuccessors)
                {
                    if (TryResolveTarget(successor, blocksByOrdinal, blockHeads, exitId, out var target))
                    {
                        targets.Add((target, IsBackEdge(block.Ordinal, successor, dominators) ? FlowEdgeKind.LoopBack : FlowEdgeKind.True, block.BranchCondition));
                    }
                    else
                    {
                        diagnostics.Add(CreateDiagnostic(
                            "BD2002",
                            "A conditional successor references an unknown block.",
                            body.Method.Value,
                            block.Ordinal));
                    }
                }

                if (block.FallThroughSuccessor is { } fallThrough
                    && TryResolveTarget(fallThrough, blocksByOrdinal, blockHeads, exitId, out var fallTarget))
                {
                    targets.Add((fallTarget, IsBackEdge(block.Ordinal, fallThrough, dominators) ? FlowEdgeKind.LoopBack : FlowEdgeKind.False, block.BranchCondition));
                }
            }
            else if (block.FallThroughSuccessor is { } fallThrough)
            {
                if (TryResolveTarget(fallThrough, blocksByOrdinal, blockHeads, exitId, out var fallTarget))
                {
                    var kind = block.Terminal switch
                    {
                        ExtractedBlockTerminalKind.Return => FlowEdgeKind.Return,
                        ExtractedBlockTerminalKind.Throw => FlowEdgeKind.Throw,
                        ExtractedBlockTerminalKind.Rethrow => FlowEdgeKind.Rethrow,
                        _ => IsBackEdge(block.Ordinal, fallThrough, dominators) ? FlowEdgeKind.LoopBack : FlowEdgeKind.Normal,
                    };
                    targets.Add((fallTarget, kind, null));
                }
                else
                {
                    diagnostics.Add(CreateDiagnostic(
                        "BD2003",
                        "A fall-through successor references an unknown block.",
                        body.Method.Value,
                        block.Ordinal));
                }
            }
            else if (block.Terminal is ExtractedBlockTerminalKind.Throw or ExtractedBlockTerminalKind.Rethrow)
            {
                var kind = block.Terminal == ExtractedBlockTerminalKind.Rethrow ? FlowEdgeKind.Rethrow : FlowEdgeKind.Throw;
                targets.Add((exitId, kind, null));
            }

            foreach (var target in targets)
            {
                edges.Add(CreateEdge(body.Method, tail, target.Target, target.Kind, target.Guard, ref edgeOrdinal));
            }
        }
    }

    private static bool IsBackEdge(
        int sourceOrdinal,
        int targetOrdinal,
        Dictionary<int, HashSet<int>> dominators)
    {
        if (targetOrdinal > sourceOrdinal
            || !dominators.TryGetValue(sourceOrdinal, out var sourceDominators))
        {
            return false;
        }

        return sourceDominators.Contains(targetOrdinal);
    }

    private static bool TryResolveTarget(
        int successor,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<int, FlowNodeId> blockHeads,
        FlowNodeId exitId,
        out FlowNodeId target)
    {
        if (blocksByOrdinal.ContainsKey(successor))
        {
            target = blockHeads[successor];
            return true;
        }

        target = default;
        return false;
    }

    private static void AddFlowRegion(
        ExtractedMethodBody body,
        ExtractedExceptionRegion region,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<int, FlowNodeId> blockHeads,
        Dictionary<int, FlowNodeId> blockTails,
        ImmutableArray<FlowRegion>.Builder regions)
    {
        var kind = ToFlowRegionKind(region.Kind);
        if (kind == FlowRegionKind.Unknown)
        {
            return;
        }

        var regionNodes = new List<FlowNodeId>();
        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            if (block.Ordinal < region.StartBlockOrdinal || block.Ordinal > region.EndBlockOrdinal)
            {
                continue;
            }

            if (blockHeads.TryGetValue(block.Ordinal, out var head) && !regionNodes.Contains(head))
            {
                regionNodes.Add(head);
            }

            if (blockTails.TryGetValue(block.Ordinal, out var tail) && !regionNodes.Contains(tail))
            {
                regionNodes.Add(tail);
            }
        }

        regions.Add(new FlowRegion(
            region.Id,
            body.Method,
            kind,
            region.Parent,
            region.Ordinal,
            regionNodes.OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray(),
            region.ExceptionType,
            region.Evidence,
            region.Certainty));
    }

    private static FlowRegionKind ToFlowRegionKind(ExtractedRegionKind kind) => kind switch
    {
        ExtractedRegionKind.Root => FlowRegionKind.Root,
        ExtractedRegionKind.Try => FlowRegionKind.Try,
        ExtractedRegionKind.Catch => FlowRegionKind.Catch,
        ExtractedRegionKind.Filter => FlowRegionKind.Filter,
        ExtractedRegionKind.Finally => FlowRegionKind.Finally,
        _ => FlowRegionKind.Unknown,
    };

    private static ImmutableArray<FlowRegion> DetectLoops(
        ExtractedMethodBody body,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<int, FlowNodeId> blockHeads,
        Dictionary<int, FlowNodeId> blockTails,
        ImmutableArray<FlowNode>.Builder nodes,
        ImmutableArray<FlowRegion>.Builder regions,
        ImmutableArray<EvidenceRef> evidence,
        ref int edgeOrdinal)
    {
        var dominators = ComputeDominators(body, blocksByOrdinal);
        var loopOrdinal = regions.Count;
        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            var backTargets = new List<int>();
            if (block.FallThroughSuccessor is { } fallThrough && blocksByOrdinal.ContainsKey(fallThrough))
            {
                backTargets.Add(fallThrough);
            }

            backTargets.AddRange(block.ConditionalSuccessors.Where(blocksByOrdinal.ContainsKey));
            foreach (var headerOrdinal in backTargets.Distinct().Order())
            {
                if (headerOrdinal > block.Ordinal
                    || !dominators.TryGetValue(block.Ordinal, out var latchDominators)
                    || !latchDominators.Contains(headerOrdinal))
                {
                    continue;
                }

                var loopMembers = ComputeNaturalLoop(headerOrdinal, block.Ordinal, blocksByOrdinal);
                if (loopMembers.Count < 2 && headerOrdinal != block.Ordinal)
                {
                    continue;
                }

                var loopId = StableIdentity.CreateFlowRegionId(new FlowRegionIdentityDescriptor(
                    body.Method, "NaturalLoop", loopOrdinal));
                var loopNodeId = StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(
                    body.Method, "Loop", headerOrdinal, 0, "loop"));
                var bodyNodes = loopMembers
                    .Where(ordinal => ordinal != headerOrdinal)
                    .Order()
                    .Select(ordinal => blockTails.TryGetValue(ordinal, out var tail) ? tail : blockHeads[ordinal])
                    .OrderBy(id => id.Value, StringComparer.Ordinal)
                    .ToImmutableArray();
                var bodyBlockOrdinals = loopMembers
                    .Where(ordinal => ordinal != headerOrdinal)
                    .Order()
                    .ToImmutableArray();
                var exits = blocksByOrdinal
                    .Where(pair => !loopMembers.Contains(pair.Key))
                    .Select(pair => pair.Value)
                    .Where(candidate => candidate.Predecessors.Any(loopMembers.Contains))
                    .Select(candidate => blockHeads.TryGetValue(candidate.Ordinal, out var head) ? head : blockTails[candidate.Ordinal])
                    .OrderBy(id => id.Value, StringComparer.Ordinal)
                    .ToImmutableArray();

                nodes.Add(new LoopNode(
                    loopNodeId,
                    body.Method,
                    loopId,
                    blockHeads[headerOrdinal],
                    bodyNodes,
                    exits,
                    evidence,
                    CertaintyLevel.Exact,
                    bodyBlockOrdinals));
                regions.Add(new FlowRegion(
                    loopId,
                    body.Method,
                    FlowRegionKind.NaturalLoop,
                    null,
                    loopOrdinal,
                    bodyNodes,
                    null,
                    evidence,
                    CertaintyLevel.Exact));
                loopOrdinal++;
            }
        }

        return regions.ToImmutable();
    }

    private static Dictionary<int, HashSet<int>> ComputeDominators(
        ExtractedMethodBody body,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal)
    {
        var ordinals = body.Blocks.Select(block => block.Ordinal).Order().ToArray();
        var all = new HashSet<int>(ordinals);
        var dominators = ordinals.ToDictionary(
            ordinal => ordinal,
            ordinal => ordinal == 0 ? new HashSet<int> { 0 } : new HashSet<int>(all));

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var ordinal in ordinals.Where(ordinal => ordinal != 0))
            {
                if (!blocksByOrdinal.TryGetValue(ordinal, out var block))
                {
                    continue;
                }

                var predecessors = block.Predecessors
                    .Where(predecessor => dominators.ContainsKey(predecessor))
                    .ToArray();
                if (predecessors.Length == 0)
                {
                    continue;
                }

                var intersection = new HashSet<int>(dominators[predecessors[0]]);
                foreach (var predecessor in predecessors.Skip(1))
                {
                    intersection.IntersectWith(dominators[predecessor]);
                }

                intersection.Add(ordinal);
                if (!intersection.SetEquals(dominators[ordinal]))
                {
                    dominators[ordinal] = intersection;
                    changed = true;
                }
            }
        }

        return dominators;
    }

    private static HashSet<int> ComputeNaturalLoop(
        int header,
        int latch,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal)
    {
        if (header == latch)
        {
            return new HashSet<int> { header };
        }

        var loop = new HashSet<int> { header, latch };
        var pending = new Stack<int>();
        pending.Push(latch);
        while (pending.TryPop(out var ordinal))
        {
            if (!blocksByOrdinal.TryGetValue(ordinal, out var block))
            {
                continue;
            }

            foreach (var predecessor in block.Predecessors)
            {
                if (predecessor != header && loop.Add(predecessor))
                {
                    pending.Push(predecessor);
                }
            }
        }

        return loop;
    }

    private static ImmutableArray<FlowOutcome> ReconcileTerminals(
        ExtractedMethodBody body,
        Dictionary<int, ExtractedBasicBlock> blocksByOrdinal,
        Dictionary<int, FlowNodeId> blockTails,
        FlowNodeId exitId,
        ImmutableArray<FlowEdge>.Builder edges,
        ImmutableArray<FlowRegion>.Builder regions,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics)
    {
        var outcomes = ImmutableArray.CreateBuilder<FlowOutcome>();
        var normalExitReachable = false;
        foreach (var block in body.Blocks.OrderBy(block => block.Ordinal))
        {
            if (block.Terminal == ExtractedBlockTerminalKind.Exit)
            {
                continue;
            }

            switch (block.Terminal)
            {
                case ExtractedBlockTerminalKind.Return:
                    outcomes.Add(new FlowOutcome(
                        FlowOutcomeKind.ExplicitReturn,
                        block.Ordinal,
                        block.BranchCondition,
                        block.Evidence,
                        CertaintyLevel.Exact));
                    break;
                case ExtractedBlockTerminalKind.Throw:
                    if (!block.EscapingThrow)
                    {
                        continue;
                    }

                    outcomes.Add(new FlowOutcome(
                        FlowOutcomeKind.EscapingThrow,
                        block.Ordinal,
                        block.BranchCondition,
                        block.Evidence,
                        CertaintyLevel.Exact));
                    break;
                case ExtractedBlockTerminalKind.Rethrow:
                    if (!block.EscapingThrow)
                    {
                        continue;
                    }

                    outcomes.Add(new FlowOutcome(
                        FlowOutcomeKind.EscapingThrow,
                        block.Ordinal,
                        block.BranchCondition,
                        block.Evidence,
                        CertaintyLevel.Exact));
                    break;
                case ExtractedBlockTerminalKind.Unknown:
                    outcomes.Add(new FlowOutcome(
                        FlowOutcomeKind.Unknown,
                        block.Ordinal,
                        block.BranchCondition,
                        block.Evidence,
                        CertaintyLevel.Unknown));
                    break;
            }

            if (block.FallThroughSuccessor is { } successor
                && blocksByOrdinal.TryGetValue(successor, out var successorBlock)
                && successorBlock.Terminal == ExtractedBlockTerminalKind.Exit)
            {
                normalExitReachable = true;
            }
        }

        if (edges.Any(edge => edge.Target == exitId && edge.Kind == FlowEdgeKind.Normal))
        {
            normalExitReachable = true;
        }

        if (normalExitReachable)
        {
            outcomes.Add(new FlowOutcome(
                FlowOutcomeKind.NormalCompletion,
                null,
                null,
                [],
                CertaintyLevel.Exact));
        }

        if (outcomes.All(outcome => outcome.Kind
                is not (FlowOutcomeKind.NormalCompletion or FlowOutcomeKind.ExplicitReturn)))
        {
            outcomes.Add(new FlowOutcome(
                FlowOutcomeKind.NoNormalExit,
                null,
                null,
                [],
                CertaintyLevel.Conservative));
        }

        return outcomes.ToImmutable();
    }

    private static FlowEdge CreateEdge(
        MethodId method,
        FlowNodeId source,
        FlowNodeId target,
        FlowEdgeKind kind,
        OperationId? guard,
        ref int edgeOrdinal)
    {
        var id = StableIdentity.CreateFlowEdgeId(new FlowEdgeIdentityDescriptor(
            method,
            source.Value,
            target.Value,
            kind.ToString(),
            edgeOrdinal));
        edgeOrdinal++;
        return new FlowEdge(id, method, source, target, kind, guard, [], CertaintyLevel.Exact);
    }

    private static AnalysisDiagnostic CreateDiagnostic(
        string code,
        string summary,
        string subjectId,
        int ordinal)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.BaselineIndex,
            null,
            subjectId,
            Math.Max(0, ordinal)));
        return new AnalysisDiagnostic(
            id,
            code,
            DiagnosticSeverity.Warning,
            AnalysisStage.BaselineIndex,
            summary,
            new DiagnosticLocation("method flow", symbol: new SymbolId(subjectId)),
            $"The method flow violates invariant '{code}'.",
            "The method flow is not trustworthy for this method.",
            "Reanalyze the target; if the problem persists, report the affected method identity.",
            CertaintyLevel.Exact,
            internalDetail: $"{code} at ordinal {ordinal}");
    }
}

/// <summary>Carries one normalized method flow and its build diagnostics.</summary>
public sealed record MethodFlowBuildResult(MethodFlowSnapshot Snapshot, ImmutableArray<AnalysisDiagnostic> Diagnostics);
