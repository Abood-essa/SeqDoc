using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using Xunit;

namespace SeqDoc.Scenarios.Tests;

public sealed class HostedWorkerScenarioTests
{
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
        Assert.Equal("cancellationToken", lifecycle[1].Presentation!.HostedWorkerCancellationParameterName);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimerRegistrationRequiresAnUnconditionalFlowAnchor(bool guarded)
    {
        var request = CreateSchedulerRequest(guarded);
        var graph = Assert.Single(
            ScenarioGraphBuilder.Build(request).Graphs,
            item => item.RootKind == ScenarioRootKind.HostedWorker);

        Assert.Equal(
            !guarded,
            graph.Nodes.Any(node => node.Presentation?.HostedWorkerSchedulerRegistration == true));
    }

    private static ScenarioAnalysisRequest CreateRequest()
    {
        var baseRequest = ScenarioTestFactory.CreateGetRequest();
        var workerType = new SymbolId("symbol:v1:HostedWorkers.SampleWorker");
        var workerMethods = new[] { "StartAsync", "ExecuteAsync", "StopAsync" }
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

    private static ScenarioAnalysisRequest CreateSchedulerRequest(bool guarded)
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
        var flow = new MethodFlowSnapshot(
            registrationMethod,
            "hosted-worker-timer-body",
            guarded ? [decision, anchor] : [anchor],
            [],
            [],
            [],
            new LocalValueGraph([], []),
            guarded ? [new ControlDependence(decision.Id, anchor.Id, true, [evidence], SeqDoc.Core.Evidence.CertaintyLevel.Exact)] : [],
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
            Behavior = request.Behavior with { MethodFlows = request.Behavior.MethodFlows.Add(flow) },
            FrameworkFacts = request.FrameworkFacts with { Facts = request.FrameworkFacts.Facts.Add(scheduler) },
        };
    }
}
