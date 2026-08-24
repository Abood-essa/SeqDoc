using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.Workers;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class HostedWorkerProductionProjectionTests
{
    [Fact]
    public async Task ProductionExtractionProjectsTimerAndRegistrationIntoFirstConsumerFacts()
    {
        var root = FindRepositoryRoot();
        const string relativeProject = "tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj";
        var profile = CompilationProfile.Create(relativeProject, "Release", "net10.0");
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(
                root,
                Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar)),
                profile),
            CancellationToken.None);

        Assert.True(extraction.IsSuccess, string.Join(Environment.NewLine, extraction.Diagnostics.Select(item => item.TechnicalCause)));
        var value = Assert.IsType<ProfileAnalysisExtraction>(extraction.Value);
        var timer = Assert.Single(
            value.Operations,
            operation => operation.Kind == "ObjectCreation"
                && operation.TargetIdentity?.ContainingMetadataType == "System.Threading.Timer"
                && operation.CallbackTarget?.Kind == CallbackTargetKind.MethodGroup);
        Assert.Equal("System.Threading.TimerCallback", timer.TargetIdentity!.Parameters[0].FullyQualifiedType);
        Assert.True(
            timer.CallbackTarget is not null,
            $"target={timer.TargetIdentity}; callback={timer.CallbackTarget}; operation={timer.Id.Value}");
        Assert.Equal(CallbackTargetKind.MethodGroup, timer.CallbackTarget!.Kind);
        Assert.NotNull(timer.CallbackTarget.TargetMethod);

        var registrationOperation = Assert.Single(
            value.Operations,
            operation => operation.TargetIdentity?.MethodMetadataName == "AddHostedService"
                && operation.ConstructedType?.MetadataName == "HostedWorkers.ExactWorker");
        Assert.NotNull(registrationOperation.ConstructedTypeSymbol);

        var host = new FrameworkModelHost([new HostedWorkerModel(), new SchedulerModel()]);
        var result = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, value.ProgramIndex),
                new FrameworkAnalysisContext(profile, value.ProgramIndex, value.CallbackBoundaryFacts),
                value.Operations,
                value.Symbols),
            CancellationToken.None);

        var workers = result.Facts.OfType<HostedWorkerLifecycleFact>().ToArray();
        var exact = Assert.Single(workers, worker => worker.HostedTypeName == "HostedWorkers.ExactWorker");
        Assert.Equal("StartAsync", value.ProgramIndex.Methods.Single(method => method.Id == exact.StartMethod).Name);
        Assert.Null(exact.ExecuteMethod);
        Assert.NotNull(exact.StopMethod);
        var registration = Assert.Single(
            result.Facts.OfType<HostedWorkerRegistrationFact>(),
            fact => fact.HostedType == exact.HostedType);
        Assert.Equal(exact.HostedType, registration.HostedType);

        var background = Assert.Single(workers, worker => worker.HostedTypeName == "HostedWorkers.BackgroundWorker");
        Assert.Null(background.StartMethod);
        Assert.NotNull(background.ExecuteMethod);
        Assert.Null(background.StopMethod);
        Assert.Contains(
            result.Facts.OfType<SchedulerJobFact>(),
            fact => fact.JobMethod == timer.CallbackTarget.TargetMethod);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SEQWRK001");

        var unregistered = Assert.Single(workers, worker => worker.HostedTypeName == "HostedWorkers.UnregisteredWorker");
        Assert.NotEqual(exact.HostedType, unregistered.HostedType);

        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(value.ProgramIndex, value.BehaviorInput),
            CancellationToken.None);
        Assert.True(behavior.IsSuccess, string.Join(Environment.NewLine, behavior.Diagnostics.Select(item => item.TechnicalCause)));
        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            value.ProgramIndex,
            behavior.Value!,
            result,
            value.SemanticFacts,
            value.DependencyInjectionFacts,
            value.StructuralResultFacts,
            value.NonGetSemanticFacts,
            value.ConditionalDependencyInjectionFacts,
            value.ConfigurationSemanticFacts,
            value.CallbackBoundaryFacts,
            value.PredicateSemanticFacts,
            value.MinimalApiHandlerFacts));
        var graph = Assert.Single(
            graphs.Graphs,
            item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                && item.OperationKey.Contains("ExactWorker", StringComparison.Ordinal));
        var exactLifecycle = graph.Nodes
            .Where(node => node.Presentation?.HostedWorkerLifecycleStep is not null)
            .ToArray();
        Assert.Equal("cancellationToken", exactLifecycle.Single(node => node.Presentation!.TargetMemberName == "StartAsync")
            .Presentation!.HostedWorkerCancellationParameterName);
        Assert.Null(exactLifecycle.Single(node => node.Presentation!.TargetMemberName == "StopAsync")
            .Presentation!.HostedWorkerCancellationParameterName);
        var backgroundGraph = Assert.Single(
            graphs.Graphs,
            item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                && item.OperationKey.Contains("BackgroundWorker", StringComparison.Ordinal));
        var backgroundExecute = backgroundGraph.Nodes.Single(node => node.Presentation?.TargetMemberName == "ExecuteAsync");
        Assert.Equal("stoppingToken", backgroundExecute.Presentation!.HostedWorkerCancellationParameterName);
        Assert.Contains(
            DocumentationPlanner.Plan(backgroundGraph).Wording.Phrases,
            phrase => phrase.Text.Contains("ExecuteAsync with cancellation parameter evidence: stoppingToken", StringComparison.Ordinal));
        Assert.DoesNotContain(
            graphs.Graphs,
            item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                && item.OperationKey.Contains("UnregisteredWorker", StringComparison.Ordinal));
        var lookalikeCancellationGraph = Assert.Single(
            graphs.Graphs,
            item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                && item.OperationKey.Contains("LookalikeCancellationWorker", StringComparison.Ordinal));
        Assert.DoesNotContain(
            lookalikeCancellationGraph.Nodes,
            node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CancellationCheck);
        var documentation = DocumentationPlanner.Plan(graph);
        var wording = documentation.Wording.Phrases.Select(phrase => phrase.Text).ToArray();
        Assert.Contains(wording, phrase => phrase.Contains("registers a timer callback", StringComparison.Ordinal));
        Assert.Contains(wording, phrase => phrase.Contains("registered hosted-worker lifecycle includes StartAsync", StringComparison.Ordinal));
        Assert.Contains(wording, phrase => phrase.Contains("StartAsync with cancellation parameter evidence: cancellationToken", StringComparison.Ordinal));
        Assert.Contains(wording, phrase => phrase.Contains("registered hosted-worker lifecycle includes StopAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(wording, phrase => phrase.Contains("invokes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(wording, phrase => phrase.Contains("completed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(wording, phrase => phrase.Contains("success", StringComparison.OrdinalIgnoreCase));

        var workerControlNodes = graphs.Graphs
            .Where(item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker)
            .SelectMany(item => item.Nodes)
            .Where(node => node.Presentation?.HostedWorkerControlKind is not null)
            .ToArray();
        Assert.Contains(workerControlNodes, node => node.Presentation!.HostedWorkerControlKind == HostedWorkerControlKind.AwaitedRepeatingLoop);
        Assert.Contains(workerControlNodes, node => node.Presentation!.HostedWorkerControlKind == HostedWorkerControlKind.EnumerationLoop);
        Assert.Contains(workerControlNodes, node => node.Presentation!.HostedWorkerControlKind == HostedWorkerControlKind.CatchLoopContinuation);
        Assert.Contains(workerControlNodes, node => node.Presentation!.HostedWorkerControlKind == HostedWorkerControlKind.CancellationCheck);
        Assert.Contains(workerControlNodes, node => node.Presentation!.HostedWorkerControlKind == HostedWorkerControlKind.SemaphoreBoundary);
        Assert.Contains(workerControlNodes, node => node.Presentation!.HostedWorkerControlKind == HostedWorkerControlKind.TerminalOutcome);
        var controlWording = graphs.Graphs
            .Where(item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker)
            .SelectMany(item => DocumentationPlanner.Plan(item).Wording.Phrases)
            .Select(phrase => phrase.Text)
            .ToArray();
        Assert.Contains(controlWording, phrase => phrase.Contains("repeats awaited work in a loop", StringComparison.Ordinal));
        Assert.Contains(controlWording, phrase => phrase.Contains("enumerates items in a loop", StringComparison.Ordinal));
        Assert.Contains(controlWording, phrase => phrase.Contains("catch-to-loop continuation boundary", StringComparison.Ordinal));
        Assert.Contains(controlWording, phrase => phrase.Contains("checks its cancellation token", StringComparison.Ordinal));
        Assert.Contains(controlWording, phrase => phrase.Contains("semaphore synchronization boundary", StringComparison.Ordinal));
        Assert.Contains(controlWording, phrase => phrase.Contains("terminal outcome boundary", StringComparison.Ordinal));
        Assert.DoesNotContain(controlWording, phrase => phrase.Contains("eventually", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(controlWording, phrase => phrase.Contains("succeeds", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(controlWording, phrase => phrase.Contains("completed", StringComparison.OrdinalIgnoreCase));

        var backgroundControls = backgroundGraph.Nodes
            .Where(node => node.Presentation?.HostedWorkerControlKind is not null)
            .ToArray();
        Assert.Single(backgroundControls,
            node => node.Presentation!.HostedWorkerControlKind == HostedWorkerControlKind.AwaitedRepeatingLoop);
        Assert.Single(backgroundControls,
            node => node.Presentation!.HostedWorkerControlKind == HostedWorkerControlKind.EnumerationLoop);
        Assert.DoesNotContain(DocumentationPlanner.Plan(backgroundGraph).Wording.Phrases,
            phrase => phrase.Text.Contains("polling", StringComparison.OrdinalIgnoreCase)
                || phrase.Text.Contains("batch", StringComparison.OrdinalIgnoreCase));
        var backgroundDiagram = DocumentationPlanner.Plan(backgroundGraph).Diagram;
        Assert.Equal(1, backgroundDiagram.Messages.Count(message => message.Label == "awaited repeating loop"));
        Assert.Equal(1, backgroundDiagram.Messages.Count(message => message.Label == "enumeration loop"));

        var retryGraph = Assert.Single(
            graphs.Graphs,
            item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                && item.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        Assert.Single(retryGraph.Nodes,
            node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CatchLoopContinuation);
        Assert.DoesNotContain(DocumentationPlanner.Plan(retryGraph).Wording.Phrases,
            phrase => phrase.Text.Contains("retries work", StringComparison.OrdinalIgnoreCase)
                || phrase.Text.Contains("retry policy", StringComparison.OrdinalIgnoreCase));

        foreach (var workerName in new[] { "LocalTokenWorker", "FieldTokenWorker", "SubstitutedTokenWorker" })
        {
            var graphForWorker = Assert.Single(
                graphs.Graphs,
                item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                    && item.OperationKey.Contains(workerName, StringComparison.Ordinal));
            Assert.DoesNotContain(graphForWorker.Nodes,
                node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CancellationCheck);
        }

        var semaphoreGraph = Assert.Single(
            graphs.Graphs,
            item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                && item.OperationKey.Contains("ThrottledWorker", StringComparison.Ordinal));
        Assert.Single(semaphoreGraph.Nodes,
            node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.SemaphoreBoundary);
        Assert.DoesNotContain(DocumentationPlanner.Plan(semaphoreGraph).Wording.Phrases,
            phrase => phrase.Text.Contains("throttling boundary", StringComparison.OrdinalIgnoreCase));

        var unrelatedCatchGraph = Assert.Single(
            graphs.Graphs,
            item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                && item.OperationKey.Contains("UnrelatedCatchWorker", StringComparison.Ordinal));
        Assert.Contains(unrelatedCatchGraph.Nodes,
            node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.TerminalOutcome);
        Assert.DoesNotContain(unrelatedCatchGraph.Nodes,
            node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CatchLoopContinuation);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
