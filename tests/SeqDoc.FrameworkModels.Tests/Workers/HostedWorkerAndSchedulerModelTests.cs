using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.FrameworkModels.Workers;
using Xunit;

namespace SeqDoc.FrameworkModels.Tests.Workers;

public sealed class HostedWorkerAndSchedulerModelTests
{
    private static readonly CompilationProfile Profile =
        CompilationProfile.Create("tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj", "Release", "net10.0");
    private static readonly string[] WorkerMethodNames = ["StartAsync", "ExecuteAsync", "StopAsync", "RunJob"];

    [Fact]
    public async Task HostedWorkerRequiresExactContractAndEmitsChronologyAndCancellation()
    {
        var index = WorkerIndex(withHostedContract: true);
        var model = new HostedWorkerModel();
        var context = new FrameworkAnalysisContext(Profile, index);

        Assert.True(model.IsApplicable(new FrameworkDetectionContext(Profile, index)));
        var result = await model.AnalyzeSymbolAsync(Symbol("StartAsync", withHostedContract: true), context, CancellationToken.None);

        var fact = Assert.Single(result.Facts.OfType<HostedWorkerLifecycleFact>());
        Assert.Equal(Method("StartAsync"), fact.StartMethod);
        Assert.Null(fact.ExecuteMethod);
        Assert.Equal(Method("StopAsync"), fact.StopMethod);
        Assert.Equal(Method("StartAsync"), fact.RootMethod);
        Assert.Equal("cancellationToken", fact.CancellationParameterName);
        Assert.False(fact.IsBackgroundService);
        Assert.Equal(CertaintyLevel.Exact, fact.Certainty);
        Assert.All(fact.Evidence, evidence => Assert.Equal(EvidenceKind.FrameworkModel, evidence.Kind));
    }

    [Fact]
    public async Task HostedWorkerRejectsLookalikeInterfaceFromAnotherAssembly()
    {
        var index = WorkerIndex(withHostedContract: false);
        var model = new HostedWorkerModel();
        var result = await model.AnalyzeSymbolAsync(
            Symbol("StartAsync", withHostedContract: false),
            new FrameworkAnalysisContext(Profile, index),
            CancellationToken.None);

        Assert.False(result.Recognized);
        Assert.Empty(result.Facts);
    }

    [Fact]
    public async Task TimerModelLinksExactRegistrationToMethodGroupJob()
    {
        var index = WorkerIndex(withHostedContract: true);
        var model = new SchedulerModel();
        var operation = new OperationDescriptor(
            new OperationId("operation:timer-registration"),
            Method("ExecuteAsync"),
            "ObjectCreation",
            DocumentId,
            80,
            30,
            [SourceEvidence("timer")],
            CertaintyLevel.Exact,
            TimerConstructor,
            CallbackTarget: new CallbackTargetDescriptor(Method("RunJob"), null));

        var result = await model.AnalyzeOperationAsync(
            operation,
            new FrameworkAnalysisContext(Profile, index),
            CancellationToken.None);

        var fact = Assert.Single(result.Facts.OfType<SchedulerJobFact>());
        Assert.Equal(SchedulerKind.Timer, fact.Scheduler);
        Assert.Equal(Method("ExecuteAsync"), fact.RegistrationMethod);
        Assert.Equal(Method("RunJob"), fact.JobMethod);
        Assert.Equal("System.Threading.TimerCallback", fact.CallbackTypeName);
        Assert.Equal(80, fact.SourceStart);
    }

    [Fact]
    public async Task TimerModelRejectsWrongConstructorShape()
    {
        var model = new SchedulerModel();
        var wrong = TimerConstructor with
        {
            Parameters = TimerConstructor.Parameters.SetItem(
                0,
                new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Action")),
        };
        var operation = new OperationDescriptor(
            new OperationId("operation:wrong-timer"),
            Method("ExecuteAsync"),
            "ObjectCreation",
            DocumentId,
            80,
            30,
            [SourceEvidence("wrong-timer")],
            CertaintyLevel.Exact,
            wrong,
            CallbackTarget: new CallbackTargetDescriptor(Method("RunJob"), null));

        var result = await model.AnalyzeOperationAsync(
            operation,
            new FrameworkAnalysisContext(Profile, WorkerIndex(withHostedContract: true)),
            CancellationToken.None);

        Assert.True(result.Recognized);
        Assert.Empty(result.Facts);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SEQWRK001");
    }

    [Fact]
    public async Task TimerFactRetainsWeakestContributorCertainty()
    {
        var operation = new OperationDescriptor(
            new OperationId("operation:timer-conservative"),
            Method("ExecuteAsync"),
            "ObjectCreation",
            DocumentId,
            80,
            30,
            [SourceEvidenceWithCertainty("timer-conservative", CertaintyLevel.Conservative)],
            CertaintyLevel.Conservative,
            TimerConstructor,
            CallbackTarget: new CallbackTargetDescriptor(Method("RunJob"), null));

        var result = await new SchedulerModel().AnalyzeOperationAsync(
            operation,
            new FrameworkAnalysisContext(Profile, WorkerIndex(withHostedContract: true)),
            CancellationToken.None);

        Assert.Equal(CertaintyLevel.Conservative, Assert.Single(result.Facts.OfType<SchedulerJobFact>()).Certainty);
    }

    private static readonly FrameworkMethodIdentity TimerConstructor = new(
        "System.Runtime",
        "System.Threading.Timer",
        ".ctor",
        0,
        [
            new(ParameterRefKind.None, "System.Threading.TimerCallback"),
            new(ParameterRefKind.None, "System.Object"),
            new(ParameterRefKind.None, "System.TimeSpan"),
            new(ParameterRefKind.None, "System.TimeSpan"),
        ],
        "System.Void");

    private static ProgramIndexSnapshot WorkerIndex(bool withHostedContract)
    {
        var type = new ProgramType(
            WorkerType,
            ProjectId,
            Namespace,
            "HostedWorkers.SampleWorker",
            ProgramTypeKind.Class,
            null,
            withHostedContract
                ? [new SymbolId("symbol:Microsoft.Extensions.Hosting.IHostedService")]
                : [new SymbolId("symbol:Other.Hosting.IHostedService")],
            "worker-type",
            [SourceEvidence("worker-type")]);
        var methods = WorkerMethodNames
            .Select(name => new ProgramMethod(
                Method(name),
                new SymbolId($"symbol:HostedWorkers.SampleWorker.{name}"),
                WorkerType,
                name,
                $"HostedWorkers.SampleWorker.{name}(System.Threading.CancellationToken)",
                [new ParameterDescriptor("cancellationToken", "System.Threading.CancellationToken", ParameterRefKind.None)],
                "System.Threading.Tasks.Task",
                $"method:{name}",
                $"body:{name}",
                [SourceEvidence(name)]))
            .ToImmutableArray();

        return new ProgramIndexSnapshot(
            1,
            "test",
            Profile,
            [new ProgramProject(ProjectId, "HostedWorkers", "HostedWorkers.csproj", Profile.Id, "net10.0", ProjectKind.Worker, "build", [], [SourceEvidence("project")])],
            [new ProgramDocument(DocumentId, ProjectId, "Worker.cs", DocumentOrigin.Source, "content", null, [SourceEvidence("document")])],
            [],
            [type],
            [],
            methods,
            [],
            [new ProgramReference("hosting", ProjectId, ProgramReferenceKind.Assembly, "Microsoft.Extensions.Hosting.Abstractions", "10.0.0", [SourceEvidence("hosting")])],
            [],
            [],
            [],
            "input",
            "fingerprint");
    }

    private static SymbolDescriptor Symbol(string name, bool withHostedContract)
        => new(
            new SymbolId($"symbol:HostedWorkers.SampleWorker.{name}"),
            "Method",
            name,
            DocumentId,
            10,
            20,
            [SourceEvidence(name)],
            CertaintyLevel.Exact,
            new FrameworkMethodShape(
                new SymbolId($"symbol:HostedWorkers.SampleWorker.{name}"),
                WorkerType,
                true,
                true,
                false,
                false,
                0,
                new FrameworkTypeShape(
                    new FrameworkTypeIdentity("HostedWorkers", "1.0.0.0", "HostedWorkers.SampleWorker"),
                    true,
                    true,
                    false,
                    false,
                    0,
                    [],
                    withHostedContract
                        ? [new FrameworkTypeIdentity("Microsoft.Extensions.Hosting.Abstractions", "10.0.0.0", "Microsoft.Extensions.Hosting.IHostedService")]
                        : [new FrameworkTypeIdentity("Other.Hosting", "1.0.0.0", "Other.Hosting.IHostedService")])));

    private static MethodId Method(string name) => new($"method:HostedWorkers.SampleWorker.{name}");
    private static readonly SymbolId WorkerType = new("symbol:HostedWorkers.SampleWorker");
    private static readonly SymbolId Namespace = new("symbol:HostedWorkers");
    private static readonly ProjectId ProjectId = new("project:HostedWorkers");
    private static readonly DocumentId DocumentId = new("document:HostedWorkers.Worker");

    private static EvidenceRef SourceEvidence(string subject)
        => SourceEvidenceWithCertainty(subject, CertaintyLevel.Exact);

    private static EvidenceRef SourceEvidenceWithCertainty(string subject, CertaintyLevel certainty)
        => new(
            new EvidenceId($"evidence:{subject}"),
            EvidenceKind.Source,
            "Worker.cs",
            new SourceRange(DocumentId, new SourcePosition(1, 0), new SourcePosition(1, 10)),
            subject,
            null,
            certainty);
}
