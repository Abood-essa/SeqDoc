using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.Workers;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class HostedWorkerProductionProjectionTests
{
    [Fact]
    public async Task ProductionExtractionPreservesAllSemaphoreInvocationsAndMeasuresTheAcceptedIdentity()
    {
        var value = await ExtractHostedWorkersAsync();
        var extractedOperations = value.BehaviorInput.Methods.SelectMany(method => method.Operations)
            .ToDictionary(operation => operation.Id);
        var semaphore = value.Operations
            .Where(operation => operation.TargetIdentity?.ContainingMetadataType == "System.Threading.SemaphoreSlim")
            .ToArray();

        var acquires = semaphore.Where(operation => operation.TargetIdentity!.MethodMetadataName == "WaitAsync"
            && operation.TargetIdentity.Parameters.Length == 1
            && operation.TargetIdentity.Parameters[0].FullyQualifiedType == "System.Threading.CancellationToken").ToArray();
        var releases = semaphore.Where(operation => operation.TargetIdentity!.MethodMetadataName == "Release"
            && operation.TargetIdentity.Parameters.Length == 0).ToArray();
        Assert.NotEmpty(acquires);
        Assert.NotEmpty(releases);
        Assert.Equal("System.Threading", acquires[0].TargetIdentity!.AssemblyIdentity);
        Assert.Equal("10.0.0.0", acquires[0].TargetIdentity!.AssemblyVersion);
        var firstAcquireIdentity = acquires[0].TargetIdentity
            ?? throw new Xunit.Sdk.XunitException("Accepted WaitAsync operation has no target identity.");
        Assert.Equal("System.Threading, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            extractedOperations[acquires[0].Id].Invocation!.TargetAssemblyFullIdentity);
        var acceptedIdentities = acquires.Concat(releases).Select(operation => operation.TargetIdentity!).ToArray();
        Assert.All(acceptedIdentities, identity =>
        {
            Assert.Equal("System.Threading.SemaphoreSlim", identity.ContainingMetadataType);
            Assert.Equal(0, identity.GenericArity);
            Assert.NotEmpty(identity.AssemblyIdentity);
            Assert.Equal("System.Threading", identity.AssemblyIdentity);
            Assert.Equal("10.0.0.0", identity.AssemblyVersion);
        });
        Assert.All(acquires, operation =>
        {
            Assert.Equal("WaitAsync", operation.TargetIdentity!.MethodMetadataName);
            Assert.Equal("System.Threading.Tasks.Task", operation.TargetIdentity.ReturnType);
            var parameter = Assert.Single(operation.TargetIdentity.Parameters);
            Assert.Equal(ParameterRefKind.None, parameter.RefKind);
            Assert.Equal("System.Threading.CancellationToken", parameter.FullyQualifiedType);
            var extracted = extractedOperations[operation.Id];
            Assert.NotNull(extracted.Invocation);
            Assert.False(string.IsNullOrWhiteSpace(extracted.Invocation!.ReceiverIdentity));
        });
        var derivedMethod = value.ProgramIndex.Methods.Single(method => method.ContainingType == value.ProgramIndex.Types
            .Single(type => type.MetadataName == "HostedWorkers.DerivedSemaphoreWorker").Id
            && method.Name == "ExecuteAsync").Id;
        var derivedInvocations = value.BehaviorInput.Methods
            .Single(method => method.Method == derivedMethod)
            .Operations
            .Where(operation => operation.Invocation?.TargetIdentity?.ContainingMetadataType == "System.Threading.SemaphoreSlim")
            .ToArray();
        Assert.Contains(derivedInvocations, operation => operation.Invocation!.TargetIdentity!.MethodMetadataName == "WaitAsync"
            && operation.Invocation.ReceiverOriginalTypeIdentity == new FrameworkTypeIdentity("HostedWorkers", "1.0.0.0", "HostedWorkers.DerivedSemaphore"));
        Assert.Contains(derivedInvocations, operation => operation.Invocation!.TargetIdentity!.MethodMetadataName == "Release"
            && operation.Invocation.ReceiverOriginalTypeIdentity == new FrameworkTypeIdentity("HostedWorkers", "1.0.0.0", "HostedWorkers.DerivedSemaphore"));
        Assert.All(releases, operation =>
        {
            Assert.Equal("Release", operation.TargetIdentity!.MethodMetadataName);
            Assert.Equal("System.Int32", operation.TargetIdentity.ReturnType);
            Assert.Empty(operation.TargetIdentity.Parameters);
        });
        Assert.All(acceptedIdentities, identity => Assert.Equal(acquires[0].TargetIdentity!.AssemblyIdentity, identity.AssemblyIdentity));
        // The producer is an evidence-preserving boundary: unsupported invocations remain available
        // for the Scenario admission decision rather than disappearing during extraction.
        Assert.Equal(acquires[0].TargetIdentity!.AssemblyIdentity, releases[0].TargetIdentity!.AssemblyIdentity);
        Assert.Contains(semaphore, operation => operation.TargetIdentity!.MethodMetadataName == "Wait");
        Assert.Contains(semaphore, operation => operation.TargetIdentity!.MethodMetadataName == "Release"
            && operation.TargetIdentity.Parameters.Length == 1);
        Assert.Contains(semaphore, operation => operation.TargetIdentity!.MethodMetadataName == "WaitAsync"
            && operation.TargetIdentity.Parameters.Length != 1);
        Assert.Contains(value.BehaviorInput.Methods.SelectMany(method => method.Operations), operation => operation.Invocation?.TargetContainingTypeName == "HostedWorkers.FakeSemaphore");
        Assert.Contains(value.BehaviorInput.Methods.SelectMany(method => method.Operations), operation => operation.Invocation?.TargetContainingTypeName == "HostedWorkers.SemaphoreExtensions");
        Assert.Contains(value.BehaviorInput.Methods.SelectMany(method => method.Operations), operation => operation.Kind == ExtractedOperationKind.DynamicInvocation);
        var cancellationChecks = value.BehaviorInput.Methods.SelectMany(method => method.Operations).Where(operation =>
            operation.Invocation?.TargetIdentity?.ContainingMetadataType == "System.Threading.CancellationToken"
            && operation.Invocation.TargetIdentity.MethodMetadataName == "ThrowIfCancellationRequested"
            && operation.Invocation.TargetAssemblyFullIdentity == "System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").ToArray();
        Assert.NotEmpty(cancellationChecks);
        var cancellationCheck = cancellationChecks.First();
        Assert.Equal("System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            cancellationCheck.Invocation!.TargetAssemblyFullIdentity);

        // The accepted WaitAsync is compiler-owned by an await expression, not merely a call with a
        // promising name.  The receiver and operand joins are retained in the producer output.
        var behaviorOperations = value.BehaviorInput.Methods.SelectMany(method => method.Operations).ToArray();
        var awaitedWaits = behaviorOperations
            .Where(operation => operation.Invocation?.TargetIdentity?.ContainingMetadataType == "System.Threading.SemaphoreSlim"
                && operation.Invocation.TargetIdentity?.MethodMetadataName == "WaitAsync"
                && operation.Invocation.TargetIdentity?.Parameters.Length == 1
                && operation.Invocation.ReceiverIdentity is not null)
            .ToArray();
        Assert.NotEmpty(awaitedWaits);
        Assert.Contains(awaitedWaits, wait => behaviorOperations.Any(candidate =>
            candidate.Kind == ExtractedOperationKind.Await && candidate.Await?.Operand.Value == wait.Id.Value));
        Assert.Contains(behaviorOperations,
            wait => wait.Invocation?.TargetIdentity?.ContainingMetadataType == "System.Threading.SemaphoreSlim"
                && wait.Invocation.TargetIdentity.MethodMetadataName == "WaitAsync"
                && wait.Invocation.TargetIdentity.Parameters.Length == 1
                && !behaviorOperations.Any(candidate => candidate.Kind == ExtractedOperationKind.Await
                    && candidate.Await?.Operand.Value == wait.Id.Value));
    }

    [Fact]
    public async Task ScenarioProjectionPairsOnlyAwaitedSameReceiverOnOneReachablePath()
    {
        var value = await ExtractHostedWorkersAsync();
        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(value.ProgramIndex, value.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, string.Join(Environment.NewLine, behavior.Diagnostics.Select(item => item.TechnicalCause)));
        var retryType = value.ProgramIndex.Types.Single(type => type.MetadataName == "HostedWorkers.RetryWorker").Id;
        var retryMethod = value.ProgramIndex.Methods.First(method => method.Name == "ExecuteAsync"
            && method.ContainingType == retryType).Id;
        var retryFlow = behavior.Value!.MethodFlows.Single(flow => flow.Method == retryMethod);
        Assert.False(retryFlow.CatchContinuations.IsDefaultOrEmpty);
        Assert.DoesNotContain(retryFlow.Diagnostics, diagnostic => diagnostic.Code == "BD2020");
        Assert.DoesNotContain(retryFlow.Nodes, node => node is ReturnFlowNode);
        var profile = CompilationProfile.Create("tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj", "Release", "net10.0");
        var frameworks = await new FrameworkModelHost([new HostedWorkerModel(), new SchedulerModel()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(new FrameworkDetectionContext(profile, value.ProgramIndex),
                new FrameworkAnalysisContext(profile, value.ProgramIndex, value.CallbackBoundaryFacts), value.Operations, value.Symbols), CancellationToken.None);
        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(profile, value.ProgramIndex, behavior.Value!, frameworks,
            value.SemanticFacts, value.DependencyInjectionFacts, value.StructuralResultFacts, value.NonGetSemanticFacts,
            value.ConditionalDependencyInjectionFacts, value.ConfigurationSemanticFacts, value.CallbackBoundaryFacts,
            value.PredicateSemanticFacts, value.MinimalApiHandlerFacts));

        var valid = Assert.Single(graphs.Graphs, graph => graph.OperationKey.Contains("SemaphoreProofWorker", StringComparison.Ordinal));
        var semaphoreNode = Assert.Single(valid.Nodes, node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.SemaphoreBoundary);
        Assert.NotNull(semaphoreNode.Presentation!.HostedWorkerFlowRegion);
        Assert.NotNull(semaphoreNode.Presentation.HostedWorkerHeader);
        var direct = Assert.Single(graphs.Graphs, graph => graph.OperationKey.Contains("DirectSemaphoreWorker", StringComparison.Ordinal));
        Assert.Single(direct.Nodes, node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.SemaphoreBoundary);
        var derived = Assert.Single(graphs.Graphs, graph => graph.OperationKey.Contains("DerivedSemaphoreWorker", StringComparison.Ordinal));
        Assert.DoesNotContain(derived.Nodes, node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.SemaphoreBoundary);
        Assert.Contains(derived.Diagnostics, diagnostic => diagnostic.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT");

        var nested = Assert.Single(graphs.Graphs, graph => graph.OperationKey.Contains("SemaphoreNestedLoopWorker", StringComparison.Ordinal));
        Assert.DoesNotContain(nested.Nodes, node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.SemaphoreBoundary);
        Assert.Contains(nested.Diagnostics, diagnostic => diagnostic.Code == "SC-WORKER-UNSUPPORTED-PLACEMENT");

        foreach (var name in new[] { "SemaphoreNegativeShapesWorker", "SemaphoreUnawaitedWorker", "SemaphoreLoopMismatchWorker", "SemaphoreBranchWorker", "SemaphoreConsumptionWorker", "SemaphoreReceiverWorker", "SemaphoreLookalikeWorker", "SemaphoreDynamicWorker", "SemaphoreExtensionWorker" })
        {
            var graph = Assert.Single(graphs.Graphs, item => item.OperationKey.Contains(name, StringComparison.Ordinal));
            Assert.DoesNotContain(graph.Nodes, node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.SemaphoreBoundary);
        }

        var proof = Assert.Single(graphs.Graphs, graph => graph.OperationKey.Contains("SemaphoreProofWorker", StringComparison.Ordinal));
        Assert.Single(proof.Nodes, node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CancellationCheck);
        var cancellationNegative = Assert.Single(graphs.Graphs, graph => graph.OperationKey.Contains("CancellationNegativeWorker", StringComparison.Ordinal));
        Assert.DoesNotContain(cancellationNegative.Nodes,
            node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CancellationCheck);
    }

    [Fact]
    public async Task ReversedProductionInputsReachOneDeterministicDiagramTree()
    {
        var value = await ExtractHostedWorkersAsync();
        var behavior = await new BehaviorAnalyzer().AnalyzeAsync(new BehaviorAnalysisRequest(value.ProgramIndex, value.BehaviorInput), CancellationToken.None);
        Assert.True(behavior.IsSuccess, string.Join(Environment.NewLine, behavior.Diagnostics.Select(item => item.TechnicalCause)));
        var terminalType = value.ProgramIndex.Types.Single(type => type.MetadataName == "HostedWorkers.TerminalWorker").Id;
        var terminalMethod = value.ProgramIndex.Methods.First(method => method.Name == "StartAsync"
            && method.ContainingType == terminalType).Id;
        var terminalFlow = behavior.Value!.MethodFlows.Single(flow => flow.Method == terminalMethod);
        Assert.Single(terminalFlow.Nodes.OfType<ReturnFlowNode>());
        Assert.Single(terminalFlow.Nodes.OfType<ThrowFlowNode>());
        Assert.Single(terminalFlow.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.ExplicitReturn);
        Assert.Single(terminalFlow.Outcomes, outcome => outcome.Kind == FlowOutcomeKind.EscapingThrow);
        var profile = CompilationProfile.Create("tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj", "Release", "net10.0");
        var frameworks = await new FrameworkModelHost([new HostedWorkerModel(), new SchedulerModel()]).AnalyzeAsync(
            new FrameworkAnalysisRequest(new FrameworkDetectionContext(profile, value.ProgramIndex),
                new FrameworkAnalysisContext(profile, value.ProgramIndex, value.CallbackBoundaryFacts), value.Operations, value.Symbols), CancellationToken.None);
        var request = new ScenarioAnalysisRequest(profile, value.ProgramIndex, behavior.Value!, frameworks,
            value.SemanticFacts, value.DependencyInjectionFacts, value.StructuralResultFacts, value.NonGetSemanticFacts,
            value.ConditionalDependencyInjectionFacts, value.ConfigurationSemanticFacts, value.CallbackBoundaryFacts,
            value.PredicateSemanticFacts, value.MinimalApiHandlerFacts);
        var forward = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs,
            graph => graph.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));
        var reversed = Assert.Single(ScenarioGraphBuilder.Build(request with
        {
            Behavior = request.Behavior with { MethodFlows = request.Behavior.MethodFlows.Reverse().ToImmutableArray() },
            FrameworkFacts = request.FrameworkFacts with { Facts = request.FrameworkFacts.Facts.Reverse().ToImmutableArray() },
        }).Graphs, graph => graph.OperationKey.Contains("RetryWorker", StringComparison.Ordinal));

        var first = DocumentationPlanner.Plan(forward).Diagram;
        var second = DocumentationPlanner.Plan(reversed).Diagram;
        Assert.Equal(first.DebugProjection, second.DebugProjection);
        Assert.Equal(first.Messages.Select(message => message.Label).Order(StringComparer.Ordinal),
            second.Messages.Select(message => message.Label).Order(StringComparer.Ordinal));
        Assert.Equal(1, first.Messages.Count(message => message.Label == "catch-to-loop continuation boundary"));
        Assert.Equal(1, first.Messages.Count(message => message.Label == "awaited repeating loop"));
        Assert.DoesNotContain(first.Messages, message => message.Label == "unconditional catch continuation");
        Assert.Equal(1, forward.Nodes.Count(node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.CatchLoopContinuation));
        Assert.Equal(1, forward.Nodes.Count(node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.ThrowBoundary));
        Assert.Contains(first.Messages, message => message.Label == "throw boundary");

        var terminal = Assert.Single(ScenarioGraphBuilder.Build(request).Graphs,
            graph => graph.OperationKey.Contains("TerminalWorker", StringComparison.Ordinal));
        Assert.Equal(1, terminal.Nodes.Count(node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.ReturnBoundary));
        Assert.Equal(1, terminal.Nodes.Count(node => node.Presentation?.HostedWorkerControlKind == HostedWorkerControlKind.ThrowBoundary));
        var terminalPlan = DocumentationPlanner.Plan(terminal).Diagram;
        Assert.Contains(terminalPlan.Messages, message => message.Label == "return boundary");
        Assert.Contains(terminalPlan.Messages, message => message.Label == "throw boundary");
    }

    private static async Task<ProfileAnalysisExtraction> ExtractHostedWorkersAsync()
    {
        var root = FindRepositoryRoot();
        const string relativeProject = "tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj";
        var profile = CompilationProfile.Create(relativeProject, "Release", "net10.0");
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(new CompilationAnalysisRequest(root,
            Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar)), profile), CancellationToken.None);
        Assert.True(extraction.IsSuccess, string.Join(Environment.NewLine, extraction.Diagnostics.Select(item => item.TechnicalCause)));
        return Assert.IsType<ProfileAnalysisExtraction>(extraction.Value);
    }

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
        var backgroundPlan = DocumentationPlanner.Plan(backgroundGraph);
        Assert.Contains(
            backgroundPlan.Wording.Phrases,
            phrase => phrase.Text.Contains("ExecuteAsync with cancellation parameter evidence: stoppingToken", StringComparison.Ordinal));
        var outerLoop = Assert.Single(backgroundPlan.Diagram.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Loop, outerLoop.Kind);
        var innerLoop = Assert.Single(outerLoop.Fragments);
        Assert.Equal(DiagramFragmentKind.Loop, innerLoop.Kind);
        Assert.DoesNotContain(backgroundPlan.Diagram.Diagnostics, diagnostic => diagnostic.Code == "DP-WORKER-INVALID-TOPOLOGY");
        var references = backgroundPlan.Diagram.Sequence.MessageRefs
            .Concat(backgroundPlan.Diagram.Sequence.Fragments.SelectMany(LoopReferences))
            .ToArray();
        Assert.Equal(references.Length, references.Distinct().Count());
        Assert.Equal(backgroundPlan.Diagram.Messages.Select(message => message.Id).OrderBy(id => id.Value),
            references.OrderBy(id => id.Value));
        Assert.DoesNotContain(
            graphs.Graphs,
            item => item.RootKind == SeqDoc.Core.ScenarioGraph.ScenarioRootKind.HostedWorker
                && item.OperationKey.Contains("UnregisteredWorker", StringComparison.Ordinal));
        var documentation = DocumentationPlanner.Plan(graph);
        var wording = documentation.Wording.Phrases.Select(phrase => phrase.Text).ToArray();
        Assert.Contains(wording, phrase => phrase.Contains("registers a timer callback", StringComparison.Ordinal));
        Assert.Contains(wording, phrase => phrase.Contains("registered hosted-worker lifecycle includes StartAsync", StringComparison.Ordinal));
        Assert.Contains(wording, phrase => phrase.Contains("StartAsync with cancellation parameter evidence: cancellationToken", StringComparison.Ordinal));
        Assert.Contains(wording, phrase => phrase.Contains("registered hosted-worker lifecycle includes StopAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(wording, phrase => phrase.Contains("invokes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(wording, phrase => phrase.Contains("completed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(wording, phrase => phrase.Contains("success", StringComparison.OrdinalIgnoreCase));
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

    private static IEnumerable<DiagramPlanElementId> LoopReferences(DiagramFragment fragment)
        => fragment.MessageRefs.Concat(fragment.Fragments.SelectMany(LoopReferences));
}
