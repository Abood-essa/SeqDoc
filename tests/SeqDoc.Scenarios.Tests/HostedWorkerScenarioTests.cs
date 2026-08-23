using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Wording;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

public sealed class HostedWorkerScenarioTests
{
    private static readonly string[] LifecycleMethodNames = ["StartAsync", "ExecuteAsync", "StopAsync"];

    [Fact]
    public void HostedWorkerGraphPreservesLifecycleOrderAndStableProjection()
    {
        var request = CreateRequest();

        var first = ScenarioGraphBuilder.Build(request);
        var second = ScenarioGraphBuilder.Build(request);
        var graph = Assert.Single(first.Graphs, item => item.RootKind == ScenarioRootKind.HostedWorker);
        var lifecycle = graph.Nodes
            .Where(node => node.Presentation?.HostedWorkerLifecycleStep is not null)
            .ToArray();

        Assert.Equal(
            [HostedWorkerLifecycleStep.Start, HostedWorkerLifecycleStep.Execute, HostedWorkerLifecycleStep.Stop],
            lifecycle.Select(node => node.Presentation!.HostedWorkerLifecycleStep!.Value));
        Assert.Equal("StartAsync", lifecycle[0].Presentation!.TargetMemberName);
        Assert.Equal("ExecuteAsync", lifecycle[1].Presentation!.TargetMemberName);
        Assert.Equal("StopAsync", lifecycle[2].Presentation!.TargetMemberName);
        Assert.Null(lifecycle[0].Presentation!.HostedWorkerCancellationParameterName);
        Assert.Equal("cancellationToken", lifecycle[1].Presentation!.HostedWorkerCancellationParameterName);
        Assert.Null(lifecycle[2].Presentation!.HostedWorkerCancellationParameterName);
        Assert.Equal(
            graph.Nodes.Select(node => node.Id.Value),
            Assert.Single(second.Graphs, item => item.RootKind == ScenarioRootKind.HostedWorker).Nodes.Select(node => node.Id.Value));
    }

    [Fact]
    public void ForeignFrameworkSnapshotCannotAdmitHostedWorkerRoot()
    {
        var current = CreateRequest();
        var request = current with
        {
            FrameworkFacts = current.FrameworkFacts with
            {
                ProfileId = ScenarioTestFactory.ForeignProfile.Id,
                ProgramIndexFingerprint = "foreign-index",
            },
        };

        Assert.DoesNotContain(
            ScenarioGraphBuilder.Build(request).Graphs,
            graph => graph.RootKind == ScenarioRootKind.HostedWorker);
    }

    [Fact]
    public void MissingFrameworkSnapshotIdentityCannotAdmitHostedWorkerRoot()
    {
        var current = CreateRequest();
        var request = current with
        {
            FrameworkFacts = current.FrameworkFacts with
            {
                ProfileId = null,
                ProgramIndexFingerprint = null,
            },
        };

        Assert.DoesNotContain(
            ScenarioGraphBuilder.Build(request).Graphs,
            graph => graph.RootKind == ScenarioRootKind.HostedWorker);
    }

    [Fact]
    public void MissingOrForeignBehaviorIdentityCannotPlaceTimerAndProducesStableBoundary()
    {
        var current = CreateSchedulerRequest(SchedulerPlacement.Unconditional);
        var requests = new[]
        {
            current with { Behavior = current.Behavior with { Profile = ScenarioTestFactory.ForeignProfile } },
            current with { Behavior = current.Behavior with { ProgramIndexFingerprint = "foreign-index" } },
            current with { Behavior = current.Behavior with { ProgramIndexFingerprint = string.Empty } },
            current with { Behavior = current.Behavior with { ProgramIndexFingerprint = null! } },
            current with { Behavior = current.Behavior with { Profile = null!, ProgramIndexFingerprint = null! } },
        };

        foreach (var result in requests.Select(request => ScenarioGraphBuilder.Build(request)))
        {
            var graph = Assert.Single(result.Graphs, item => item.RootKind == ScenarioRootKind.HostedWorker);
            Assert.DoesNotContain(graph.Nodes, node => node.Presentation?.HostedWorkerSchedulerRegistration == true);
            var diagnostic = AssertUnsupportedPlacement(graph, "behavior identity");
            Assert.Equal(CertaintyLevel.Conservative, diagnostic.Certainty);
            Assert.Single(graph.Diagnostics, item => item.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimerRegistrationRequiresAnUnconditionalFlowAnchor(bool guarded)
    {
        var request = CreateSchedulerRequest(guarded ? SchedulerPlacement.Guarded : SchedulerPlacement.Unconditional);
        var graph = Assert.Single(
            ScenarioGraphBuilder.Build(request).Graphs,
            item => item.RootKind == ScenarioRootKind.HostedWorker);

        Assert.Equal(!guarded, graph.Nodes.Any(node => node.Presentation?.HostedWorkerSchedulerRegistration == true));
        if (guarded)
        {
            AssertUnsupportedPlacement(graph, "direct control dependence");
        }
    }

    [Theory]
    [InlineData(SchedulerPlacement.NonRootRegion, "non-root region")]
    [InlineData(SchedulerPlacement.MissingFlow, "missing flow")]
    [InlineData(SchedulerPlacement.DuplicateFlow, "ambiguous flow")]
    [InlineData(SchedulerPlacement.MissingAnchor, "missing anchor")]
    [InlineData(SchedulerPlacement.DuplicateAnchor, "ambiguous anchor")]
    public void UnsupportedSchedulerPlacementIsDiagnosedAndWithheld(SchedulerPlacement placement, string boundary)
    {
        var graph = Assert.Single(
            ScenarioGraphBuilder.Build(CreateSchedulerRequest(placement)).Graphs,
            item => item.RootKind == ScenarioRootKind.HostedWorker);

        Assert.DoesNotContain(graph.Nodes, node => node.Presentation?.HostedWorkerSchedulerRegistration == true);
        AssertUnsupportedPlacement(graph, boundary);
    }

    private static ScenarioGraphDiagnostic AssertUnsupportedPlacement(ScenarioGraph graph, string boundary)
    {
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT");
        Assert.Contains(boundary, diagnostic.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(diagnostic.Evidence);
        Assert.Equal(SeqDoc.Core.Evidence.CertaintyLevel.Conservative, diagnostic.Certainty);
        Assert.NotEmpty(diagnostic.Id.Value);
        return diagnostic;
    }

    [Fact]
    public void UnsupportedPlacementDocumentationRetainsDiagnosticEvidence()
    {
        var graph = Assert.Single(
            ScenarioGraphBuilder.Build(CreateSchedulerRequest(SchedulerPlacement.Guarded)).Graphs,
            item => item.RootKind == ScenarioRootKind.HostedWorker);
        var diagnostic = Assert.Single(graph.Diagnostics, item => item.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT");
        var documentation = DocumentationPlanner.Plan(graph);
        var fallback = Assert.Single(documentation.Wording.Phrases,
            phrase => phrase.Key == "fallback:SC-WORKER-UNSUPPORTED-PLACEMENT");

        Assert.Equal(WordingPhraseKind.TechnicalFallback, fallback.Kind);
        Assert.Contains("unresolved finding (SC-WORKER-UNSUPPORTED-PLACEMENT)", fallback.Text, StringComparison.Ordinal);
        Assert.Contains("scheduler registration was withheld", fallback.Text, StringComparison.Ordinal);
        Assert.Equal(diagnostic.Evidence.Select(item => item.Id.Value), fallback.Evidence.Select(item => item.Id.Value));
        Assert.All(fallback.Evidence, item => Assert.Equal(CertaintyLevel.Conservative, item.Certainty));
        Assert.Equal(CertaintyLevel.Conservative, fallback.Certainty);
    }

    [Fact]
    public void MultipleUnsupportedSchedulersRemainCanonicalWhenInputsAreReversed()
    {
        var forwardRequest = CreateTwoUnsupportedSchedulerRequest();
        var reversedRequest = forwardRequest with
        {
            Behavior = forwardRequest.Behavior with
            {
                MethodFlows = forwardRequest.Behavior.MethodFlows
                    .Reverse()
                    .Select(flow => flow with
                    {
                        Nodes = flow.Nodes
                            .Reverse()
                            .Select(node => node with { Evidence = node.Evidence.Reverse().ToImmutableArray() })
                            .ToImmutableArray(),
                    })
                    .ToImmutableArray(),
            },
            FrameworkFacts = forwardRequest.FrameworkFacts with
            {
                Facts = forwardRequest.FrameworkFacts.Facts
                    .Reverse()
                    .Select(fact => fact is SchedulerJobFact scheduler
                        ? scheduler with { Evidence = scheduler.Evidence.Reverse().ToImmutableArray() }
                        : fact)
                    .ToImmutableArray(),
            },
        };

        var forwardGraph = Assert.Single(
            ScenarioGraphBuilder.Build(forwardRequest).Graphs,
            item => item.RootKind == ScenarioRootKind.HostedWorker);
        var reversedGraph = Assert.Single(
            ScenarioGraphBuilder.Build(reversedRequest).Graphs,
            item => item.RootKind == ScenarioRootKind.HostedWorker);
        var forwardDiagnostics = forwardGraph.Diagnostics
            .Where(item => item.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT")
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var reversedDiagnostics = reversedGraph.Diagnostics
            .Where(item => item.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT")
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, forwardDiagnostics.Length);
        Assert.Equal(forwardDiagnostics.Select(item => item.Id.Value), reversedDiagnostics.Select(item => item.Id.Value));
        Assert.Equal(
            forwardDiagnostics.Select(item => DiagnosticProjection(item)),
            reversedDiagnostics.Select(item => DiagnosticProjection(item)));
        Assert.Equal(forwardGraph.DebugProjection, reversedGraph.DebugProjection);

        var forwardDocumentation = DocumentationPlanner.Plan(forwardGraph);
        var reversedDocumentation = DocumentationPlanner.Plan(reversedGraph);
        Assert.Equal(forwardDocumentation.Wording.DebugProjection, reversedDocumentation.Wording.DebugProjection);
        Assert.Equal(
            forwardDocumentation.Wording.Phrases.Select(item => $"{item.Kind}|{item.Text}|{string.Join(',', item.Evidence.Select(evidence => evidence.Id.Value))}"),
            reversedDocumentation.Wording.Phrases.Select(item => $"{item.Kind}|{item.Text}|{string.Join(',', item.Evidence.Select(evidence => evidence.Id.Value))}"));

        static string DiagnosticProjection(ScenarioGraphDiagnostic diagnostic)
            => $"{diagnostic.Code}|{diagnostic.Detail}|{diagnostic.Certainty}|{string.Join(',', diagnostic.Evidence.Select(item => item.Id.Value))}";
    }

    public enum SchedulerPlacement { Unconditional, Guarded, NonRootRegion, MissingFlow, DuplicateFlow, MissingAnchor, DuplicateAnchor }

    private static ScenarioAnalysisRequest CreateRequest()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var workerType = new SymbolId("symbol:v1:HostedWorkers.SampleWorker");
        var workerMethods = LifecycleMethodNames
            .Select(name => new ProgramMethod(
                new MethodId($"method:v1:HostedWorkers.SampleWorker.{name}"),
                new SymbolId($"symbol:v1:HostedWorkers.SampleWorker.{name}"),
                workerType,
                name,
                $"HostedWorkers.SampleWorker.{name}(System.Threading.CancellationToken)",
                [new ParameterDescriptor("cancellationToken", "System.Threading.CancellationToken", ParameterRefKind.None)],
                "System.Threading.Tasks.Task",
                $"signature:{name}",
                $"body:{name}",
                [ScenarioTestFactory.SourceEvidence(name)]))
            .ToImmutableArray();
        var index = baseRequest.ProgramIndex with
        {
            Types = baseRequest.ProgramIndex.Types.Add(new ProgramType(
                workerType,
                new ProjectId("project:v1:HostedWorkers"),
                new SymbolId("symbol:v1:HostedWorkers"),
                "HostedWorkers.SampleWorker",
                ProgramTypeKind.Class,
                null,
                [],
                "worker-type",
                [ScenarioTestFactory.SourceEvidence("worker-type")])),
            Methods = baseRequest.ProgramIndex.Methods.AddRange(workerMethods),
        };
        var start = workerMethods[0].Id;
        var execute = workerMethods[1].Id;
        var stop = workerMethods[2].Id;
        var fact = new HostedWorkerLifecycleFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:hosted-worker"),
            EntryPointId = new EntryPointId("entry-point:v1:hosted-worker"),
            RootMethod = start,
            HostedType = workerType,
            HostedTypeName = "HostedWorkers.SampleWorker",
            StartMethod = start,
            ExecuteMethod = execute,
            StopMethod = stop,
            IsBackgroundService = false,
            CancellationParameterName = "cancellationToken",
            Evidence = [ScenarioTestFactory.SourceEvidence("hosted-worker")],
            Certainty = SeqDoc.Core.Evidence.CertaintyLevel.Exact,
        };
        var registration = new HostedWorkerRegistrationFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:hosted-worker-registration"),
            HostedType = workerType,
            RegistrationMethod = start,
            RegistrationOperation = new OperationId("operation:v1:hosted-worker-registration"),
            Evidence = [ScenarioTestFactory.SourceEvidence("hosted-worker-registration")],
            Certainty = SeqDoc.Core.Evidence.CertaintyLevel.Exact,
        };
        return baseRequest with
        {
            ProgramIndex = index,
            FrameworkFacts = baseRequest.FrameworkFacts with
            {
                Facts = baseRequest.FrameworkFacts.Facts.Add(fact).Add(registration),
                ProfileId = baseRequest.Profile.Id,
                ProgramIndexFingerprint = index.IndexFingerprint,
            },
        };
    }

    private static ScenarioAnalysisRequest CreateSchedulerRequest(SchedulerPlacement placement)
    {
        var request = CreateRequest();
        var worker = request.FrameworkFacts.Facts.OfType<HostedWorkerLifecycleFact>().Single();
        var registrationMethod = worker.RootMethod;
        var registrationOperation = new OperationId("operation:v1:hosted-worker-timer-registration");
        var evidence = ScenarioTestFactory.SourceEvidence("hosted-worker-timer-registration");
        var anchor = new OperationFlowNode(
            new FlowNodeId("flow-node:v1:hosted-worker-timer-registration"),
            registrationMethod,
            registrationOperation,
            ExtractedOperationKind.ObjectCreation,
            [evidence],
            SeqDoc.Core.Evidence.CertaintyLevel.Exact);
        var decision = new DecisionFlowNode(
            new FlowNodeId("flow-node:v1:hosted-worker-timer-guard"),
            registrationMethod,
            new OperationId("operation:v1:hosted-worker-timer-condition"),
            [evidence],
            SeqDoc.Core.Evidence.CertaintyLevel.Exact);
        ImmutableArray<FlowNode> nodes = placement == SchedulerPlacement.MissingAnchor
            ? []
            : placement == SchedulerPlacement.DuplicateAnchor
                ? [anchor, anchor with { Id = new FlowNodeId("flow-node:v1:hosted-worker-timer-registration-duplicate") }]
                : placement == SchedulerPlacement.Guarded ? [decision, anchor] : [anchor];
        ImmutableArray<FlowRegion> regions = placement == SchedulerPlacement.NonRootRegion
            ? [new FlowRegion(
                new FlowRegionId("flow-region:v1:hosted-worker-timer-try"), registrationMethod,
                FlowRegionKind.Try, null, 1, [anchor.Id], null, [evidence],
                SeqDoc.Core.Evidence.CertaintyLevel.Exact)]
            : [];
        var flow = new MethodFlowSnapshot(
            registrationMethod,
            "hosted-worker-timer-body",
            nodes,
            [],
            regions,
            [],
            new LocalValueGraph([], []),
            placement == SchedulerPlacement.Guarded ? [new ControlDependence(decision.Id, anchor.Id, true, [evidence], SeqDoc.Core.Evidence.CertaintyLevel.Exact)] : [],
            null,
            [],
            "hosted-worker-timer-flow");
        var scheduler = new SchedulerJobFact
        {
            Id = new BehaviorFactId("behavior-fact:v1:hosted-worker-timer"),
            Scheduler = SchedulerKind.Timer,
            RegistrationMethod = registrationMethod,
            RegistrationOperation = registrationOperation,
            JobMethod = registrationMethod,
            SourceStart = 1,
            CallbackTypeName = "System.Threading.TimerCallback",
            Evidence = [evidence],
            Certainty = SeqDoc.Core.Evidence.CertaintyLevel.Exact,
        };
        return request with
        {
            Behavior = placement == SchedulerPlacement.MissingFlow
                ? request.Behavior
                : request.Behavior with
                {
                    MethodFlows = placement == SchedulerPlacement.DuplicateFlow
                        ? request.Behavior.MethodFlows.Add(flow).Add(flow with { FlowFingerprint = "hosted-worker-timer-flow-duplicate" })
                        : request.Behavior.MethodFlows.Add(flow),
                },
            FrameworkFacts = request.FrameworkFacts with { Facts = request.FrameworkFacts.Facts.Add(scheduler) },
        };
    }

    private static ScenarioAnalysisRequest CreateTwoUnsupportedSchedulerRequest()
    {
        var request = CreateSchedulerRequest(SchedulerPlacement.Guarded);
        var flow = Assert.Single(request.Behavior.MethodFlows, item => item.Method == request.FrameworkFacts.Facts.OfType<SchedulerJobFact>().Single().RegistrationMethod);
        var secondEvidence = ScenarioTestFactory.SourceEvidence("hosted-worker-second-timer-registration");
        var secondOperation = new OperationId("operation:v1:hosted-worker-timer-registration-second");
        var secondFlow = flow with
        {
            FlowFingerprint = "hosted-worker-timer-flow-second",
            Nodes = flow.Nodes.Select(node => node with { Evidence = [secondEvidence] }).ToImmutableArray(),
        };
        var firstScheduler = request.FrameworkFacts.Facts.OfType<SchedulerJobFact>().Single();
        var secondScheduler = firstScheduler with
        {
            Id = new BehaviorFactId("behavior-fact:v1:hosted-worker-timer-second"),
            RegistrationOperation = secondOperation,
            Evidence = [secondEvidence],
            SourceStart = 2,
        };
        return request with
        {
            Behavior = request.Behavior with { MethodFlows = request.Behavior.MethodFlows.Add(secondFlow) },
            FrameworkFacts = request.FrameworkFacts with { Facts = request.FrameworkFacts.Facts.Add(secondScheduler) },
        };
    }
}
