using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
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
