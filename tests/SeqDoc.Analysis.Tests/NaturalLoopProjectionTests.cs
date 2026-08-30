using System.Collections.Immutable;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class NaturalLoopProjectionTests
{
    [Fact]
    public async Task RoslynDistinguishesTopAndBottomTestedWhileOperations()
    {
        var extraction = await ExtractAsync();
        var bodies = extraction.BehaviorInput.Methods
            .Where(body => MethodName(extraction, body.Method) is "DoWhileShape" or "WhileLoopShape" or "ForLoopShape" or "ForEachShape")
            .ToDictionary(body => MethodName(extraction, body.Method));

        Assert.DoesNotContain(bodies["DoWhileShape"].Operations, operation => operation.Kind == ExtractedOperationKind.DoWhileLoop);
        Assert.DoesNotContain(bodies["WhileLoopShape"].Operations, operation => operation.Kind == ExtractedOperationKind.WhileLoop);
        Assert.Equal(ExtractedLoopKind.DoWhileLoop, bodies["DoWhileShape"].NaturalLoops.Single().Kind);
        Assert.Equal(ExtractedLoopKind.WhileLoop, bodies["WhileLoopShape"].NaturalLoops.Single().Kind);
        Assert.Equal(ExtractedLoopKind.ForLoop, bodies["ForLoopShape"].NaturalLoops.Single().Kind);
        Assert.Equal(ExtractedLoopKind.ForEachLoop, bodies["ForEachShape"].NaturalLoops.Single().Kind);
        var nestedForeach = extraction.BehaviorInput.Methods.Single(body => MethodName(extraction, body.Method) == "NestedForEachShape");
        Assert.Equal(2, nestedForeach.NaturalLoops.Length);
        Assert.All(nestedForeach.NaturalLoops, loop => Assert.Equal(ExtractedLoopKind.ForEachLoop, loop.Kind));
    }

    [Fact]
    public async Task LoopDescriptorsUseExactOperationsAndCanonicalEdges()
    {
        var extraction = await ExtractAsync();
        foreach (var body in extraction.BehaviorInput.Methods.Where(body => body.NaturalLoops.Length > 0))
        {
            foreach (var loop in body.NaturalLoops)
            {
                Assert.Contains(body.LoopAnchors, anchor => anchor.Operation == loop.LoopOperation && anchor.Kind == loop.Kind);
                Assert.NotEmpty(loop.LatchBlockOrdinals);
                Assert.Equal(loop.LatchBlockOrdinals, loop.LatchBlockOrdinals.Distinct().Order());
                Assert.Equal(loop.BodyBlockOrdinals, loop.BodyBlockOrdinals.Distinct().Order());
                Assert.Equal(loop.ExitBlockOrdinals, loop.ExitBlockOrdinals.Distinct().Order());
                Assert.All(loop.BackEdges, edge =>
                {
                    Assert.Equal(loop.HeaderBlockOrdinal, edge.DestinationBlockOrdinal);
                    Assert.Contains(edge.SourceBlockOrdinal, loop.LatchBlockOrdinals);
                });
            }
        }
    }

    [Fact]
    public async Task LoopDescriptorsDoNotPerturbValueOperationsOrEdges()
    {
        var (extraction, snapshot) = await AnalyzeAsync();
        var body = extraction.BehaviorInput.Methods.Single(candidate => MethodName(extraction, candidate.Method) == "WhileLoopShape");
        var loopIds = body.NaturalLoops.Select(loop => loop.LoopOperation).ToHashSet();
        var blockIds = body.Blocks.SelectMany(block => block.Operations).ToHashSet();

        Assert.NotEmpty(loopIds);
        Assert.Empty(loopIds.Intersect(blockIds));
        Assert.Equal(body.Operations.Length, body.Operations.Select(operation => operation.Id).Distinct().Count());
        var flow = FlowNamed(extraction, snapshot, "WhileLoopShape");
        Assert.NotEmpty(flow.ValueGraph.Nodes);
        Assert.DoesNotContain(flow.ValueGraph.Nodes, node => node.DefiningOperation is { } id && loopIds.Contains(id));
    }

    [Fact]
    public async Task UnreachableControlFlowIslandProducesNoNaturalLoop()
    {
        var (extraction, snapshot) = await AnalyzeAsync();
        var flow = FlowNamed(extraction, snapshot, "UnreachableLoopShape");
        Assert.Empty(flow.Nodes.OfType<LoopNode>());
        Assert.DoesNotContain(flow.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
    }

    [Fact]
    public async Task EachCompilerLoopProjectsOneLoopNodeAndNaturalLoopRegion()
    {
        var (extraction, snapshot) = await AnalyzeAsync();
        foreach (var name in new[] { "DoWhileShape", "WhileLoopShape", "ForLoopShape", "ForEachShape" })
        {
            var flow = FlowNamed(extraction, snapshot, name);
            Assert.Single(flow.Nodes.OfType<LoopNode>());
            Assert.Single(flow.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
        }
    }

    [Fact]
    public async Task SequentialLoopsKeepDistinctHeadersAndIdentities()
    {
        var (extraction, snapshot) = await AnalyzeAsync();
        var flow = FlowNamed(extraction, snapshot, "SequentialLoopShape");
        var loops = flow.Nodes.OfType<LoopNode>().ToArray();

        Assert.Equal(2, loops.Length);
        Assert.Equal(2, loops.Select(loop => loop.Header).Distinct().Count());
        Assert.Equal(2, loops.Select(loop => loop.Region).Distinct().Count());
    }

    [Fact]
    public async Task NestedLoopsRemainDistinctAndContainCanonicalBodies()
    {
        var (extraction, snapshot) = await AnalyzeAsync();
        var flow = FlowNamed(extraction, snapshot, "NestedLoopShape");
        var loops = flow.Nodes.OfType<LoopNode>().ToArray();

        Assert.Equal(2, loops.Length);
        Assert.All(loops, loop => Assert.NotEmpty(loop.BodyBlockOrdinals));
        Assert.NotEqual(loops[0].Header, loops[1].Header);
    }

    [Fact]
    public async Task CompatibleLatchesAreGroupedWithoutDuplicateLoopRegions()
    {
        var (extraction, snapshot) = await AnalyzeAsync();
        var flow = FlowNamed(extraction, snapshot, "MultipleLatchLoopShape");
        var loops = flow.Nodes.OfType<LoopNode>().ToArray();

        Assert.Single(loops);
        Assert.Single(flow.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
        Assert.Equal(loops[0].BodyBlockOrdinals.Distinct().Order(), loops[0].BodyBlockOrdinals);
        Assert.Equal(loops[0].Exits.Distinct().Order(), loops[0].Exits);
    }

    [Fact]
    public async Task FinallyBoundaryDoesNotBecomeAnOrdinaryLoop()
    {
        var (extraction, snapshot) = await AnalyzeAsync();
        var flow = FlowNamed(extraction, snapshot, "FinallyBoundaryShape");

        Assert.Empty(flow.Nodes.OfType<LoopNode>());
        Assert.DoesNotContain(flow.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
    }

    [Fact]
    public async Task LoopProjectionRetainsEvidenceAndWeakestCertainty()
    {
        var (extraction, snapshot) = await AnalyzeAsync();
        var flow = FlowNamed(extraction, snapshot, "WhileLoopShape");
        var loop = Assert.Single(flow.Nodes.OfType<LoopNode>());
        var region = Assert.Single(flow.Regions, candidate => candidate.Kind == FlowRegionKind.NaturalLoop);

        Assert.NotEmpty(loop.Evidence);
        Assert.NotEmpty(region.Evidence);
        Assert.Equal(loop.Certainty, region.Certainty);
        Assert.NotEqual(CertaintyLevel.Unknown, loop.Certainty);
        Assert.All(loop.Evidence, evidence => Assert.False(string.IsNullOrWhiteSpace(evidence.Symbol)));
        Assert.Equal(loop.Evidence, loop.Evidence.Distinct());
    }

    [Fact]
    public async Task ReversedConstructionIsDeterministicAndDefaultLoopsAreSafe()
    {
        var (firstExtraction, firstSnapshot) = await AnalyzeAsync();
        var body = firstExtraction.BehaviorInput.Methods.Single(candidate => MethodName(firstExtraction, candidate.Method) == "NestedLoopShape");
        var reversed = body with
        {
            Blocks = body.Blocks.Reverse().ToImmutableArray(),
            Operations = body.Operations.Reverse().ToImmutableArray(),
            NaturalLoops = body.NaturalLoops.Reverse().ToImmutableArray(),
        };
        var reversedFlow = MethodFlowBuilder.Build(reversed).Snapshot;
        var first = FlowNamed(firstExtraction, firstSnapshot, "NestedLoopShape");

        Assert.Equal(first.FlowFingerprint, reversedFlow.FlowFingerprint);
        Assert.Equal(
            first.Nodes.OfType<LoopNode>().Select(loop => loop.Id),
            reversedFlow.Nodes.OfType<LoopNode>().Select(loop => loop.Id));
        Assert.Equal(
            first.Nodes.OfType<LoopNode>().Select(loop => loop.BodyBlockOrdinals),
            reversedFlow.Nodes.OfType<LoopNode>().Select(loop => loop.BodyBlockOrdinals));

        var legacy = MethodFlowBuilder.Build(body with { NaturalLoops = default });
        Assert.Empty(legacy.Snapshot.Nodes.OfType<LoopNode>());
        Assert.DoesNotContain(legacy.Snapshot.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
    }

    [Fact]
    public void MalformedNaturalLoopProducesOneConservativeDiagnostic()
    {
        var loop = new ExtractedNaturalLoop(
            new OperationId("behavior-operation:v1:missing-loop"), ExtractedLoopKind.WhileLoop, 1,
            [2], [2], [3], [new ExtractedOrdinaryBranch(2, 1, [], [])], [], CertaintyLevel.Exact);
        var result = MethodFlowBuilder.Build(CreateHandBuiltBody([loop]));

        Assert.DoesNotContain(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Loop);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "BD2011");
    }

    [Fact]
    public void DisconnectedNaturalLoopProducesOneConservativeDiagnostic()
    {
        var operation = new OperationId("behavior-operation:v1:disconnected-loop");
        var evidence = new EvidenceRef(new EvidenceId("evidence:v1:disconnected-loop"), EvidenceKind.Source, "loop.cs", null, "while", "test", CertaintyLevel.Exact);
        var loop = new ExtractedNaturalLoop(operation, ExtractedLoopKind.WhileLoop, 1, [2], [2], [3],
            [new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], evidence.Certainty)], [evidence], evidence.Certainty);
        var connected = CreateHandBuiltBody([loop], operation, evidence);
        var body = connected with
        {
            Blocks = connected.Blocks.Select(block => block.Ordinal switch
            {
                0 => block with { FallThroughSuccessor = null },
                1 => block with { Predecessors = [2] },
                _ => block,
            }).ToImmutableArray(),
            OrdinaryBranches = connected.OrdinaryBranches
                .Where(branch => branch.SourceBlockOrdinal != 0).ToImmutableArray(),
        };

        var result = MethodFlowBuilder.Build(body);

        Assert.DoesNotContain(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Loop);
        Assert.DoesNotContain(result.Snapshot.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "BD2011");
    }

    [Fact]
    public void ValidHandBuiltLoopPreservesMappingEvidenceAndCertainty()
    {
        var operation = new OperationId("behavior-operation:v1:valid-loop");
        var evidence = new EvidenceRef(new EvidenceId("evidence:v1:loop"), EvidenceKind.Source, "loop.cs", null, "while", "test", CertaintyLevel.Conservative);
        var loop = new ExtractedNaturalLoop(operation, ExtractedLoopKind.DoWhileLoop, 1, [2], [2], [3], [new ExtractedOrdinaryBranch(2, 1, [], [], [evidence], CertaintyLevel.Conservative)], [evidence], CertaintyLevel.Conservative);
        var result = MethodFlowBuilder.Build(CreateHandBuiltBody([loop], operation, evidence));
        var projected = Assert.Single(result.Snapshot.Nodes.OfType<LoopNode>());

        Assert.True(projected.BodyBlockOrdinals.SequenceEqual([2]));
        Assert.NotEmpty(projected.Exits);
        Assert.Equal(CertaintyLevel.Conservative, projected.Certainty);
        Assert.Equal([evidence.Id], projected.Evidence.Select(item => item.Id));
    }

    [Fact]
    public void FabricatedBackedgeOrRegionIsWithheld()
    {
        var operation = new OperationId("behavior-operation:v1:fabricated-loop");
        var evidence = new EvidenceRef(new EvidenceId("evidence:v1:fabricated-loop"), EvidenceKind.Source, "loop.cs", null, "while", "test", CertaintyLevel.Exact);
        var loop = new ExtractedNaturalLoop(operation, ExtractedLoopKind.WhileLoop, 1, [2], [2], [3],
            [new ExtractedOrdinaryBranch(2, 0, [new FlowRegionId("foreign")], [], [evidence], CertaintyLevel.Exact)], [evidence], CertaintyLevel.Exact);

        var result = MethodFlowBuilder.Build(CreateHandBuiltBody([loop], operation, evidence));

        Assert.DoesNotContain(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Loop);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "BD2011");
    }

    [Fact]
    public async Task BodyFingerprintIncludesCanonicalLoopTopologyAndNormalizesConstructionOrder()
    {
        var extraction = await ExtractAsync();
        var body = extraction.BehaviorInput.Methods.Single(candidate => MethodName(extraction, candidate.Method) == "NestedLoopShape");
        var loop = body.NaturalLoops[0];
        var anchor = body.LoopAnchors[0];
        var branch = body.OrdinaryBranches[0];

        Assert.NotEqual(body.BodyFingerprint, BehaviorFingerprint.ComputeBody(body with
        {
            NaturalLoops = body.NaturalLoops.SetItem(0, loop with { ExitBlockOrdinals = loop.ExitBlockOrdinals.SetItem(0, loop.ExitBlockOrdinals[0] + 1) })
        }));
        Assert.NotEqual(body.BodyFingerprint, BehaviorFingerprint.ComputeBody(body with
        {
            LoopAnchors = body.LoopAnchors.SetItem(0, anchor with { Kind = anchor.Kind == ExtractedLoopKind.DoWhileLoop ? ExtractedLoopKind.WhileLoop : ExtractedLoopKind.DoWhileLoop })
        }));
        Assert.NotEqual(body.BodyFingerprint, BehaviorFingerprint.ComputeBody(body with
        {
            OrdinaryBranches = body.OrdinaryBranches.SetItem(0, branch with { DestinationBlockOrdinal = branch.DestinationBlockOrdinal + 1 })
        }));

        Assert.Equal(body.BodyFingerprint, BehaviorFingerprint.ComputeBody(body with
        {
            NaturalLoops = body.NaturalLoops.Reverse().Select(item => item with
            {
                BodyBlockOrdinals = item.BodyBlockOrdinals.Reverse().ToImmutableArray(),
                LatchBlockOrdinals = item.LatchBlockOrdinals.Reverse().ToImmutableArray(),
                ExitBlockOrdinals = item.ExitBlockOrdinals.Reverse().ToImmutableArray(),
                BackEdges = item.BackEdges.Reverse().Select(edge => edge with
                {
                    EnteringRegions = edge.EnteringRegions.Reverse().ToImmutableArray(),
                    LeavingRegions = edge.LeavingRegions.Reverse().ToImmutableArray(),
                    Evidence = edge.Evidence.Reverse().ToImmutableArray(),
                }).ToImmutableArray(),
                Evidence = item.Evidence.Reverse().ToImmutableArray(),
            }).ToImmutableArray(),
            LoopAnchors = body.LoopAnchors.Reverse().Select(item => item with { Evidence = item.Evidence.Reverse().ToImmutableArray() }).ToImmutableArray(),
            OrdinaryBranches = body.OrdinaryBranches.Reverse().Select(item => item with
            {
                EnteringRegions = item.EnteringRegions.Reverse().ToImmutableArray(),
                LeavingRegions = item.LeavingRegions.Reverse().ToImmutableArray(),
                Evidence = item.Evidence.Reverse().ToImmutableArray(),
            }).ToImmutableArray()
        }));

        var noTopology = extraction.BehaviorInput.Methods.Single(candidate => MethodName(extraction, candidate.Method) == "StaticShape");
        Assert.Equal(
            BehaviorFingerprint.ComputeBody(noTopology with { NaturalLoops = [], LoopAnchors = [], OrdinaryBranches = [] }),
            BehaviorFingerprint.ComputeBody(noTopology with { NaturalLoops = default, LoopAnchors = default, OrdinaryBranches = default }));
    }

    [Fact]
    public async Task NestedFunctionLoopsStayConfinedToTheirOwningMethods()
    {
        var extraction = await ExtractAsync();
        foreach (var name in new[] { "LocalFunctionNestedLoopShape", "AnonymousFunctionNestedLoopShape" })
        {
            var body = extraction.BehaviorInput.Methods.Single(candidate => MethodName(extraction, candidate.Method) == name);
            Assert.Empty(body.NaturalLoops);
            Assert.Empty(body.LoopAnchors);
            Assert.DoesNotContain(extraction.BehaviorInput.Diagnostics, diagnostic =>
                diagnostic.Code == "BE2010" && diagnostic.Location.Description.Contains(body.Method.Value, StringComparison.Ordinal));
        }

    }

    [Theory]
    [InlineData("non-successor backedge")]
    [InlineData("unreachable body")]
    [InlineData("incorrect exit")]
    [InlineData("impossible region transition")]
    public void HostileValidIdLoopDescriptorsAreWithheld(string hostileShape)
    {
        var result = MethodFlowBuilder.Build(CreateHostileBody(hostileShape));

        Assert.DoesNotContain(result.Snapshot.Nodes, node => node.Kind == FlowNodeKind.Loop);
        Assert.DoesNotContain(result.Snapshot.Regions, region => region.Kind == FlowRegionKind.NaturalLoop);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "BD2011");
    }

    [Fact]
    public async Task CompilerCatchBoundariesDoNotFabricateLatchesAndRemainDeterministic()
    {
        var extraction = await ExtractAsync();
        var (_, snapshot) = await AnalyzeAsync();
        foreach (var name in new[] { "CatchToLoopShape", "NestedTryCatchLoopShape" })
        {
            var body = extraction.BehaviorInput.Methods.Single(candidate => MethodName(extraction, candidate.Method) == name);
            var flow = FlowNamed(extraction, snapshot, name);
            var projectedLoops = flow.Nodes.OfType<LoopNode>().ToArray();
            Assert.Equal(body.NaturalLoops.Length, projectedLoops.Length);
            Assert.Contains(body.Regions, region => region.Kind is ExtractedRegionKind.Catch or ExtractedRegionKind.Filter or ExtractedRegionKind.Finally);
            var catchRegions = body.Regions.Where(region => region.Kind == ExtractedRegionKind.Catch).ToArray();
            var loopHeaders = body.NaturalLoops.Select(loop => loop.HeaderBlockOrdinal).ToHashSet();
            var catchContinuationBranches = body.OrdinaryBranches.Where(branch =>
                catchRegions.Any(region => branch.SourceBlockOrdinal >= region.StartBlockOrdinal
                    && branch.SourceBlockOrdinal <= region.EndBlockOrdinal)
                && loopHeaders.Contains(branch.DestinationBlockOrdinal)).ToArray();
            Assert.NotEmpty(catchContinuationBranches);
            Assert.All(catchContinuationBranches, branch =>
                Assert.DoesNotContain(body.NaturalLoops, loop =>
                    loop.LatchBlockOrdinals.Contains(branch.SourceBlockOrdinal)
                    || loop.BackEdges.Any(edge => edge.SourceBlockOrdinal == branch.SourceBlockOrdinal
                        && edge.DestinationBlockOrdinal == branch.DestinationBlockOrdinal)));
            foreach (var descriptor in body.NaturalLoops)
            {
                Assert.Contains(projectedLoops, loop => loop.BodyBlockOrdinals.SequenceEqual(descriptor.BodyBlockOrdinals));
                Assert.All(descriptor.LatchBlockOrdinals, latch =>
                    Assert.DoesNotContain(body.Regions, region =>
                        (region.Kind is ExtractedRegionKind.Catch or ExtractedRegionKind.Filter or ExtractedRegionKind.Finally)
                        && latch >= region.StartBlockOrdinal
                        && latch <= region.EndBlockOrdinal));
                Assert.All(descriptor.BackEdges, edge => Assert.Equal(descriptor.HeaderBlockOrdinal, edge.DestinationBlockOrdinal));
            }

            var reversed = body with
            {
                Blocks = body.Blocks.Reverse().ToImmutableArray(),
                Operations = body.Operations.Reverse().ToImmutableArray(),
                Regions = body.Regions.Reverse().ToImmutableArray(),
                NaturalLoops = body.NaturalLoops.Reverse().ToImmutableArray(),
                LoopAnchors = body.LoopAnchors.Reverse().ToImmutableArray(),
                OrdinaryBranches = body.OrdinaryBranches.Reverse().ToImmutableArray()
            };
            var originalResult = MethodFlowBuilder.Build(body);
            var reversedResult = MethodFlowBuilder.Build(reversed);
            Assert.Equal(originalResult.Snapshot.FlowFingerprint, reversedResult.Snapshot.FlowFingerprint);
            Assert.Equal(originalResult.Diagnostics.Select(DiagnosticSignature), reversedResult.Diagnostics.Select(DiagnosticSignature));
            Assert.Equal(originalResult.Snapshot.Nodes.OfType<LoopNode>().Select(node => node.Id), reversedResult.Snapshot.Nodes.OfType<LoopNode>().Select(node => node.Id));
        }
    }

    private static string DiagnosticSignature(AnalysisDiagnostic diagnostic)
        => $"{diagnostic.Code}|{diagnostic.InternalDetail}|{diagnostic.Location.Description}";

    private static ExtractedMethodBody CreateHandBuiltBody(
        ImmutableArray<ExtractedNaturalLoop> loops,
        OperationId? operationId = null,
        EvidenceRef? evidence = null)
    {
        var operation = operationId ?? new OperationId("behavior-operation:v1:hand-built-loop");
        return new ExtractedMethodBody(
            new MethodId("method:v1:hand-built-loop"), "body", [], [],
            [new ExtractedOperation(operation, new MethodId("method:v1:hand-built-loop"), ExtractedOperationKind.WhileLoop, null, [], 0, "System.Void", null, false, true, [], [], [], null, null, null, null, null, null, null, null, evidence is null ? [] : [evidence], evidence?.Certainty ?? CertaintyLevel.Exact)],
            [
                new ExtractedBasicBlock(0, [], null, 1, [], [], ExtractedBlockTerminalKind.None, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(1, [], null, 2, [3], [0, 2], ExtractedBlockTerminalKind.None, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(2, [], null, 1, [], [1], ExtractedBlockTerminalKind.None, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(3, [], null, null, [], [1], ExtractedBlockTerminalKind.Exit, false, [], [], [], CertaintyLevel.Exact),
            ],
            [new ExtractedExceptionRegion(new FlowRegionId("flow-region:v1:root"), ExtractedRegionKind.Root, null, 0, 0, 3, null, [], CertaintyLevel.Exact)],
            evidence is null ? [] : [evidence], loops,
            operationId is null ? [] : [new ExtractedLoopAnchor(operation, loops[0].Kind, [evidence!], evidence!.Certainty)],
            operationId is null ? [] : [
                new ExtractedOrdinaryBranch(0, 1, [], [], [evidence!], evidence!.Certainty),
                new ExtractedOrdinaryBranch(1, 2, [], [], [evidence!], evidence!.Certainty),
                new ExtractedOrdinaryBranch(1, 3, [], [], [evidence!], evidence!.Certainty),
                new ExtractedOrdinaryBranch(2, 1, [], [], [evidence!], evidence!.Certainty)]);
    }

    private static ExtractedMethodBody CreateHostileBody(string shape)
    {
        var operation = new OperationId("behavior-operation:v1:hostile-loop");
        var evidence = new EvidenceRef(new EvidenceId("evidence:v1:hostile-loop"), EvidenceKind.Source, "loop.cs", null, "while", "test", CertaintyLevel.Exact);
        var branch = new ExtractedOrdinaryBranch(2, 1,
            shape == "impossible region transition" ? [new FlowRegionId("flow-region:v1:root")] : [], [], [evidence], CertaintyLevel.Exact);
        var loop = new ExtractedNaturalLoop(operation, ExtractedLoopKind.WhileLoop, 1, [2],
            [2], shape == "incorrect exit" ? [0] : [3], [branch], [evidence], CertaintyLevel.Exact);
        var body = CreateValidHostileBody(operation, evidence, loop, branch);
        if (shape == "non-successor backedge")
        {
            body = body with { Blocks = body.Blocks.Select(block => block.Ordinal == 2 ? block with { FallThroughSuccessor = 3 } : block).ToImmutableArray() };
        }
        else if (shape == "unreachable body")
        {
            body = body with { Blocks = body.Blocks.Select(block => block.Ordinal == 1 ? block with { FallThroughSuccessor = 3 } : block).ToImmutableArray() };
        }
        return body;
    }

    private static ExtractedMethodBody CreateValidHostileBody(
        OperationId operation,
        EvidenceRef evidence,
        ExtractedNaturalLoop loop,
        ExtractedOrdinaryBranch backEdge)
    {
        var exitBranch = new ExtractedOrdinaryBranch(1, 3, [], [], [evidence], CertaintyLevel.Exact);
        return new ExtractedMethodBody(
            new MethodId("method:v1:hostile-loop"), "body", [], [],
            [new ExtractedOperation(operation, new MethodId("method:v1:hostile-loop"), ExtractedOperationKind.WhileLoop, null, [], 0, "System.Void", null, false, true, [], [], [], null, null, null, null, null, null, null, null, [evidence], evidence.Certainty)],
            [
                new ExtractedBasicBlock(0, [], null, 1, [], [], ExtractedBlockTerminalKind.None, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(1, [], null, 2, [3], [0, 2], ExtractedBlockTerminalKind.None, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(2, [], null, 1, [], [1], ExtractedBlockTerminalKind.None, false, [], [], [], CertaintyLevel.Exact),
                new ExtractedBasicBlock(3, [], null, null, [], [1], ExtractedBlockTerminalKind.Exit, false, [], [], [], CertaintyLevel.Exact),
            ],
            [new ExtractedExceptionRegion(new FlowRegionId("flow-region:v1:root"), ExtractedRegionKind.Root, null, 0, 0, 3, null, [], CertaintyLevel.Exact)],
            [evidence], [loop],
            [new ExtractedLoopAnchor(operation, loop.Kind, [evidence], evidence.Certainty)],
            [
                new ExtractedOrdinaryBranch(0, 1, [], [], [evidence], CertaintyLevel.Exact),
                new ExtractedOrdinaryBranch(1, 2, [], [], [evidence], CertaintyLevel.Exact),
                exitBranch,
                backEdge]);
    }

    private static async Task<ProfileAnalysisExtraction> ExtractAsync()
    {
        var root = FindRepositoryRoot();
        var relativePath = "tests/fixtures/PassB/DispatchAndValues/DispatchAndValues.csproj";
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)), CompilationProfile.Create(relativePath, "Release", "net10.0")),
            CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return result.Value!;
    }

    private static async Task<(ProfileAnalysisExtraction Extraction, BehaviorSnapshot Snapshot)> AnalyzeAsync()
    {
        var extraction = await ExtractAsync();
        var result = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.ProgramIndex, extraction.BehaviorInput), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}"))
            + Environment.NewLine + string.Join(Environment.NewLine, extraction.BehaviorInput.Methods.Where(body => !body.NaturalLoops.IsDefaultOrEmpty).Select(body => $"{MethodName(extraction, body.Method)}:{string.Join(';', body.NaturalLoops.Select(loop => $"h{loop.HeaderBlockOrdinal}/b{string.Join(',', loop.BodyBlockOrdinals)}/l{string.Join(',', loop.LatchBlockOrdinals)}/e{string.Join(',', loop.ExitBlockOrdinals)}"))}")));
        return (extraction, result.Value!);
    }

    private static MethodFlowSnapshot FlowNamed(ProfileAnalysisExtraction extraction, BehaviorSnapshot snapshot, string name)
    {
        var method = extraction.ProgramIndex.Methods.Single(candidate => candidate.Name == name).Id;
        return snapshot.MethodFlows.Single(flow => flow.Method == method);
    }

    private static string MethodName(ProfileAnalysisExtraction extraction, MethodId method)
        => extraction.ProgramIndex.Methods.Single(candidate => candidate.Id == method).Name;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
