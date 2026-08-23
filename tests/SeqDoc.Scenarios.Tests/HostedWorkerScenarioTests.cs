using System.Collections.Immutable;
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
            },
        };
    }
}
