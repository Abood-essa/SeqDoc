using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Persistence;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.Persistence.Sqlite;
using SeqDoc.Persistence.Sqlite.Serialization;
using SeqDoc.Persistence.Sqlite.Testing;
using Xunit;

namespace SeqDoc.Persistence.Tests;

public sealed class SqliteAnalysisStoreTests
{
    [Fact]
    public async Task AggregateSnapshotRoundTripsThroughFreshStore()
    {
        using var database = new TemporaryDatabase();
        var index = CreateSnapshot("net10.0", 'a');
        var behavior = CreateBehavior(index);
        var activation = await new SqliteAnalysisStore(database.Path).ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(index, behavior)]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.Succeeded, activation.Outcome);
        var active = await new SqliteAnalysisStore(database.Path).ReadActiveAsync(index.Profile.Id, CancellationToken.None);
        Assert.True(active.Value!.Found);
        Assert.Equal(index.IndexFingerprint, active.Value.ActiveProfile!.ProgramIndex.IndexFingerprint);
        Assert.NotNull(active.Value.ActiveProfile.Behavior);
        Assert.Equal(behavior.BehaviorFingerprint, active.Value.ActiveProfile.Behavior!.BehaviorFingerprint);
        Assert.Equal(
            BehaviorSnapshotJsonCodec.Serialize(behavior),
            BehaviorSnapshotJsonCodec.Serialize(active.Value.ActiveProfile.Behavior));
    }

    [Fact]
    public async Task LoopNodeSnapshotActivatesWithCanonicalShape()
    {
        using var database = new TemporaryDatabase();
        var index = CreateSnapshot("net10.0", 'l');
        var behavior = CreateBehavior(index, includeLoop: true);

        var activation = await new SqliteAnalysisStore(database.Path).ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(index, behavior)]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.Succeeded, activation.Outcome);
        var active = await new SqliteAnalysisStore(database.Path).ReadActiveAsync(index.Profile.Id, CancellationToken.None);
        var activeBehavior = active.Value!.ActiveProfile!.Behavior!;
        Assert.IsType<LoopNode>(Assert.Single(activeBehavior.MethodFlows[0].Nodes.Where(node => node is LoopNode)));
        Assert.Equal(BehaviorSnapshotJsonCodec.Serialize(behavior), BehaviorSnapshotJsonCodec.Serialize(activeBehavior));
    }

    [Fact]
    public async Task ProgramIndexOnlySnapshotReportsBehaviorUnavailable()
    {
        using var database = new TemporaryDatabase();
        var index = CreateSnapshot("net10.0", 'b');
        var activation = await new SqliteAnalysisStore(database.Path).ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(index, null)]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.Succeeded, activation.Outcome);
        var active = await new SqliteAnalysisStore(database.Path).ReadActiveAsync(index.Profile.Id, CancellationToken.None);
        Assert.True(active.Value!.Found);
        Assert.Null(active.Value.ActiveProfile!.Behavior);
    }

    [Fact]
    public async Task BehaviorFingerprintMismatchIsRejectedBeforeStaging()
    {
        using var database = new TemporaryDatabase();
        var index = CreateSnapshot("net10.0", 'c');
        var behavior = CreateBehavior(index) with { BehaviorFingerprint = new string('0', 64) };
        var result = await new SqliteAnalysisStore(database.Path).ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(index, behavior)]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.ValidationFailure, result.Outcome);
        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public async Task FailedAggregateActivationPreservesPreviousAggregate()
    {
        using var database = new TemporaryDatabase();
        var original = CreateSnapshot("net10.0", 'd');
        var originalBehavior = CreateBehavior(original);
        var baseline = new SqliteAnalysisStore(database.Path);
        Assert.True((await baseline.ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(original, originalBehavior)]),
            CancellationToken.None)).IsSuccess);

        var failed = await new SqliteAnalysisStore(database.Path, new ThrowingObserver()).ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(CreateSnapshot("net10.0", 'e'), null)]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.PersistenceFailure, failed.Outcome);
        var after = await new SqliteAnalysisStore(database.Path).ReadActiveAsync(original.Profile.Id, CancellationToken.None);
        Assert.Equal(original.IndexFingerprint, after.Value!.ActiveProfile!.ProgramIndex.IndexFingerprint);
        Assert.Equal(originalBehavior.BehaviorFingerprint, after.Value.ActiveProfile.Behavior!.BehaviorFingerprint);
        Assert.Equal("Failed", await ScalarAsync(database.Path, "SELECT state FROM analysis_invocations ORDER BY invocation_sequence DESC LIMIT 1;"));
    }

    [Fact]
    public async Task CancelledAggregateActivationPreservesPreviousAggregate()
    {
        using var database = new TemporaryDatabase();
        var original = CreateSnapshot("net10.0", 'f');
        var originalBehavior = CreateBehavior(original);
        var baseline = new SqliteAnalysisStore(database.Path);
        Assert.True((await baseline.ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(original, originalBehavior)]),
            CancellationToken.None)).IsSuccess);

        using var cancellation = new CancellationTokenSource();
        var cancelled = await new SqliteAnalysisStore(database.Path, new CancellingObserver(cancellation)).ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(CreateSnapshot("net10.0", 'g'), null)]),
            cancellation.Token);

        Assert.Equal(ApplicationOutcome.Cancelled, cancelled.Outcome);
        var after = await new SqliteAnalysisStore(database.Path).ReadActiveAsync(original.Profile.Id, CancellationToken.None);
        Assert.Equal(original.IndexFingerprint, after.Value!.ActiveProfile!.ProgramIndex.IndexFingerprint);
        Assert.Equal(originalBehavior.BehaviorFingerprint, after.Value.ActiveProfile.Behavior!.BehaviorFingerprint);
        Assert.Equal("Cancelled", await ScalarAsync(database.Path, "SELECT state FROM analysis_invocations ORDER BY invocation_sequence DESC LIMIT 1;"));
    }

    [Fact]
    public async Task MultiProfileAggregateActivationIsAtomic()
    {
        using var database = new TemporaryDatabase();
        var first = CreateSnapshot("net10.0", '1');
        var second = CreateSnapshot("net10.0-windows", '2');
        var firstBehavior = CreateBehavior(first);
        var secondBehavior = CreateBehavior(second);
        var baseline = new SqliteAnalysisStore(database.Path);
        Assert.True((await baseline.ActivateAsync(
            new AnalysisPersistenceRequest([
                new AnalysisProfileSnapshot(first, firstBehavior),
                new AnalysisProfileSnapshot(second, secondBehavior),
            ]),
            CancellationToken.None)).IsSuccess);

        var failed = await new SqliteAnalysisStore(database.Path, new ThrowingObserver()).ActivateAsync(
            new AnalysisPersistenceRequest([
                new AnalysisProfileSnapshot(CreateSnapshot("net10.0", '3'), null),
                new AnalysisProfileSnapshot(CreateSnapshot("net10.0-windows", '4'), null),
            ]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.PersistenceFailure, failed.Outcome);
        var restarted = new SqliteAnalysisStore(database.Path);
        var firstActive = await restarted.ReadActiveAsync(first.Profile.Id, CancellationToken.None);
        var secondActive = await restarted.ReadActiveAsync(second.Profile.Id, CancellationToken.None);
        Assert.Equal(first.IndexFingerprint, firstActive.Value!.ActiveProfile!.ProgramIndex.IndexFingerprint);
        Assert.Equal(second.IndexFingerprint, secondActive.Value!.ActiveProfile!.ProgramIndex.IndexFingerprint);
        Assert.Equal(firstBehavior.BehaviorFingerprint, firstActive.Value.ActiveProfile.Behavior!.BehaviorFingerprint);
        Assert.Equal(secondBehavior.BehaviorFingerprint, secondActive.Value.ActiveProfile.Behavior!.BehaviorFingerprint);
    }

    [Fact]
    public async Task ConcurrentReadersNeverObserveStagedAggregate()
    {
        using var database = new TemporaryDatabase();
        var original = CreateSnapshot("net10.0", 'h');
        var originalBehavior = CreateBehavior(original);
        var baseline = new SqliteAnalysisStore(database.Path);
        Assert.True((await baseline.ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(original, originalBehavior)]),
            CancellationToken.None)).IsSuccess);

        var replacement = CreateSnapshot("net10.0", 'i');
        var observer = new BlockingObserver();
        var activation = new SqliteAnalysisStore(database.Path, observer).ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(replacement, null)]),
            CancellationToken.None);
        await observer.StagingReached;

        var reads = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            new SqliteAnalysisStore(database.Path).ReadActiveAsync(original.Profile.Id, CancellationToken.None)));
        Assert.All(reads, result => Assert.Equal(original.IndexFingerprint, result.Value!.ActiveProfile!.ProgramIndex.IndexFingerprint));

        observer.Release();
        Assert.True((await activation).IsSuccess);
        var active = await new SqliteAnalysisStore(database.Path).ReadActiveAsync(original.Profile.Id, CancellationToken.None);
        Assert.Equal(replacement.IndexFingerprint, active.Value!.ActiveProfile!.ProgramIndex.IndexFingerprint);
    }

    [Fact]
    public async Task OldProgramIndexCacheRemainsReadableAsAggregate()
    {
        using var database = new TemporaryDatabase();
        var index = CreateSnapshot("net10.0", 'j');
        var programIndexActivation = await new SqliteProgramIndexStore(database.Path).ActivateAsync(
            new ProgramIndexPersistenceRequest([index]),
            CancellationToken.None);
        Assert.True(
            programIndexActivation.IsSuccess,
            string.Join(Environment.NewLine, programIndexActivation.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var active = await new SqliteAnalysisStore(database.Path).ReadActiveAsync(index.Profile.Id, CancellationToken.None);
        Assert.True(active.Value!.Found);
        Assert.Equal(index.IndexFingerprint, active.Value.ActiveProfile!.ProgramIndex.IndexFingerprint);
        Assert.Null(active.Value.ActiveProfile.Behavior);
    }

    private static BehaviorSnapshot CreateBehavior(ProgramIndexSnapshot index, bool includeLoop = false)
    {
        var methodId = new MethodId("method:v1:test");
        var entry = new EntryFlowNode(
            StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(methodId, "Entry", 0, 0, "entry")),
            methodId,
            [],
            CertaintyLevel.Exact);
        var exit = new ExitFlowNode(
            StableIdentity.CreateFlowNodeId(new FlowNodeIdentityDescriptor(methodId, "Exit", int.MaxValue, int.MaxValue, "exit")),
            methodId,
            [],
            CertaintyLevel.Exact);
        var nodes = includeLoop
            ? ImmutableArray.Create<FlowNode>(
                entry,
                exit,
                new LoopNode(
                    new("flow-node:test-loop"),
                    methodId,
                    new("flow-region:test-loop"),
                    entry.Id,
                    [entry.Id],
                    [exit.Id],
                    [],
                    CertaintyLevel.Exact,
                    [7, 3, 7, 2]))
            : [entry, exit];
        var flow = new MethodFlowSnapshot(
            methodId,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            nodes,
            [],
            [],
            [],
            new LocalValueGraph([], []),
            [],
            null,
            [],
            string.Empty);
        var completedFlow = flow with { FlowFingerprint = MethodFlowFingerprint.Compute(flow) };
        var callGraph = new CallGraph([], []);
        var rtaFoundation = new RtaFoundation([], HasExplicitRoots: false);
        var snapshot = new BehaviorSnapshot(
            1,
            "test",
            index.Profile,
            index.IndexFingerprint,
            [completedFlow],
            callGraph,
            rtaFoundation,
            [],
            [],
            string.Empty);
        return snapshot with { BehaviorFingerprint = BehaviorFingerprint.Compute(snapshot) };
    }

    private static ProgramIndexSnapshot CreateSnapshot(string framework, char hashCharacter)
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"fixture:{framework}:{hashCharacter}")));
        var profile = CompilationProfile.Create("src/App/App.csproj", "Release", framework);
        var projectId = StableIdentity.CreateProjectId(profile.Id, "src/App/App.csproj");
        var documentId = StableIdentity.CreateDocumentId(new DocumentIdentityDescriptor(
            projectId, DocumentIdentityKind.Source, "src/App/Program.cs"));
        var namespaceId = new SymbolId($"namespace:{framework}");
        var typeId = new SymbolId($"type:{framework}");
        var methodId = new MethodId($"method:{framework}");
        var methodSymbolId = new SymbolId($"method-symbol:{framework}");
        var evidenceId = StableIdentity.CreateEvidenceId(new EvidenceIdentityDescriptor(
            EvidenceKind.Source, "src/App/Program.cs", documentId, 0, 10, "App.Program.Main", CertaintyLevel.Exact));
        var evidence = new EvidenceRef(
            evidenceId, EvidenceKind.Source, "src/App/Program.cs", new SourceRange(documentId, new SourcePosition(0, 0), new SourcePosition(0, 10)), "App.Program.Main", null, CertaintyLevel.Exact);

        var snapshot = new ProgramIndexSnapshot(
            1,
            "test",
            profile,
            [new ProgramProject(projectId, "App", "src/App/App.csproj", profile.Id, framework, ProjectKind.Executable, hash, [], [evidence])],
            [new ProgramDocument(documentId, projectId, "src/App/Program.cs", DocumentOrigin.Source, hash, null, [evidence])],
            [new ProgramNamespace(namespaceId, projectId, "App", [evidence])],
            [new ProgramType(typeId, projectId, namespaceId, "App.Program", ProgramTypeKind.Class, null, [], hash, [evidence])],
            [],
            [new ProgramMethod(methodId, methodSymbolId, typeId, "Main", "void App.Program.Main()", [], "System.Void", hash, hash, [evidence])],
            [],
            [],
            [],
            [],
            [],
            hash,
            string.Empty);
        return snapshot with { IndexFingerprint = ProgramIndexFingerprint.Compute(snapshot) };
    }

    [Fact]
    public async Task FailingBehaviorAnalysisDoesNotActivateAndPreservesPreviousAggregate()
    {
        using var database = new TemporaryDatabase();
        var original = CreateSnapshot("net10.0", 'w');
        var originalBehavior = CreateBehavior(original);
        var store = new SqliteAnalysisStore(database.Path);
        Assert.True((await store.ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(original, originalBehavior)]),
            CancellationToken.None)).IsSuccess);

        var profile = original.Profile;
        var resolver = new FixedProfileResolver(new ResolvedCompilationProfiles(
            ["net10.0"],
            ImmutableArray.Create(profile),
            "test"));
        var failingBuilder = new FailingAnalysisBuilder();
        var workflow = new PassAWorkflow(
            resolver,
            new NeverUsedIndexBuilder(),
            new SqliteProgramIndexStore(database.Path),
            store,
            failingBuilder);

        var result = await workflow.AnalyzeAsync(
            new CompilationProfileResolutionRequest("root", "src/App/App.csproj", "Release"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationOutcome.AnalysisFailure, result.Outcome);
        var after = await store.ReadActiveAsync(profile.Id, CancellationToken.None);
        Assert.Equal(original.IndexFingerprint, after.Value!.ActiveProfile!.ProgramIndex.IndexFingerprint);
        Assert.Equal(originalBehavior.BehaviorFingerprint, after.Value.ActiveProfile.Behavior!.BehaviorFingerprint);
    }

    [Fact]
    public async Task TamperedStoredBehaviorFingerprintFailsOnRead()
    {
        using var database = new TemporaryDatabase();
        var index = CreateSnapshot("net10.0", 't');
        var behavior = CreateBehavior(index);
        var store = new SqliteAnalysisStore(database.Path);
        Assert.True((await store.ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(index, behavior)]),
            CancellationToken.None)).IsSuccess);

        await using (var connection = new SqliteConnection($"Data Source={database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE behavior_snapshots SET payload_json = REPLACE(payload_json, '\"SchemaVersion\":1', '\"SchemaVersion\":9');";
            await command.ExecuteNonQueryAsync();
        }

        var read = await new SqliteAnalysisStore(database.Path).ReadActiveAsync(index.Profile.Id, CancellationToken.None);

        Assert.False(read.IsSuccess);
        Assert.Equal(ApplicationOutcome.PersistenceFailure, read.Outcome);
    }

    [Fact]
    public async Task FailedActivationRemovesStagedBehaviorFacts()
    {
        using var database = new TemporaryDatabase();
        var original = CreateSnapshot("net10.0", 'u');
        var originalBehavior = CreateBehavior(original);
        var baseline = new SqliteAnalysisStore(database.Path);
        Assert.True((await baseline.ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(original, originalBehavior)]),
            CancellationToken.None)).IsSuccess);

        var failed = await new SqliteAnalysisStore(database.Path, new ThrowingObserver()).ActivateAsync(
            new AnalysisPersistenceRequest([new AnalysisProfileSnapshot(CreateSnapshot("net10.0", 'v'), CreateBehavior(CreateSnapshot("net10.0", 'v')))]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.PersistenceFailure, failed.Outcome);
        Assert.Equal("Failed", await ScalarAsync(database.Path, "SELECT state FROM analysis_invocations ORDER BY invocation_sequence DESC LIMIT 1;"));
        var failedRun = Convert.ToString(
            await ScalarAsync(database.Path, "SELECT run_id FROM profile_runs WHERE state='Failed' ORDER BY rowid DESC LIMIT 1;"),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotNull(failedRun);
        var behaviorCount = await ScalarAsync(database.Path,
            $"SELECT COUNT(*) FROM behavior_snapshots WHERE run_id = '{failedRun}';");
        Assert.Equal(0L, Convert.ToInt64(behaviorCount, System.Globalization.CultureInfo.InvariantCulture));
        var projectCount = await ScalarAsync(database.Path,
            $"SELECT COUNT(*) FROM program_projects WHERE run_id = '{failedRun}';");
        Assert.Equal(0L, Convert.ToInt64(projectCount, System.Globalization.CultureInfo.InvariantCulture));
        var lifecycleCount = await ScalarAsync(database.Path,
            "SELECT COUNT(*) FROM profile_runs WHERE state='Failed';");
        Assert.True(Convert.ToInt64(lifecycleCount, System.Globalization.CultureInfo.InvariantCulture) >= 1,
            "Lifecycle rows must be preserved after cleanup.");
    }

    private static async Task<object?> ScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private sealed class FixedProfileResolver(ResolvedCompilationProfiles profiles) : ICompilationProfileResolver
    {
        public Task<ApplicationResult<ResolvedCompilationProfiles>> ResolveAsync(
            CompilationProfileResolutionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ApplicationResult.Success(profiles));
    }

    private sealed class NeverUsedIndexBuilder : IProgramIndexBuilder
    {
        public Task<ApplicationResult<ProgramIndexSnapshot>> BuildAsync(
            CompilationAnalysisRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The aggregate path must not build the program index separately.");
    }

    private sealed class FailingAnalysisBuilder : IAnalysisBuilder
    {
        public Task<ApplicationResult<AnalysisProfileCandidate>> BuildAsync(
            CompilationAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            var diagnostic = new AnalysisDiagnostic(
                StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                    "BD1009",
                    AnalysisStage.BaselineIndex,
                    null,
                    "method:v1:malformed",
                    0)),
                "BD1009",
                DiagnosticSeverity.Warning,
                AnalysisStage.BaselineIndex,
                "A block fall-through successor references an unknown block.",
                new DiagnosticLocation("behavior extraction", symbol: new SymbolId("method:v1:malformed")),
                "A block fall-through successor references an unknown block.",
                "The extracted behavior facts are not trustworthy.",
                "Reanalyze the target.",
                CertaintyLevel.Exact);
            return Task.FromResult(ApplicationResult.Failure<AnalysisProfileCandidate>(
                ApplicationOutcome.AnalysisFailure,
                ImmutableArray.Create(diagnostic)));
        }
    }

    /// <summary>
    /// Returns a successful candidate whose memory-only companions are distinct from the persisted
    /// snapshot so tests can prove the workflow persists only the snapshot and returns companion
    /// inspection only after activation succeeds.
    /// </summary>
    private sealed class CompanionAnalysisBuilder(AnalysisProfileSnapshot snapshot) : IAnalysisBuilder
    {
        public Task<ApplicationResult<AnalysisProfileCandidate>> BuildAsync(
            CompilationAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            var index = snapshot.ProgramIndex;
            var candidate = new AnalysisProfileCandidate(
                snapshot,
                new SemanticFactSet(
                    1,
                    "test",
                    index.Profile,
                    index.IndexFingerprint,
                    [],
                    [],
                    [],
                    [],
                    "semantic-companion-test"),
                new FrameworkAnalysisResult(
                    true,
                    [],
                    [],
                    [],
                    [],
                    [],
                    [new FrameworkModelDescriptor("test.framework.model", "1.0.0", "Test Framework Model", 1)]),
                new DependencyInjectionFactSet(
                    1,
                    "test",
                    index.Profile,
                    index.IndexFingerprint,
                    [],
                    [],
                    [],
                    "dependency-injection-companion-test"),
                new StructuralResultFactSet(
                    1,
                    "test",
                    index.Profile,
                    index.IndexFingerprint,
                    [],
                    [],
                    [],
                    "structural-result-companion-test"),
                new ScenarioGraphSet(
                    1,
                    "test",
                    index.Profile,
                    index.IndexFingerprint,
                    [],
                    [],
                    "scenario-graphs-companion-test"));
            return Task.FromResult(ApplicationResult.Success(candidate));
        }
    }

    [Fact]
    public async Task DependencyInjectionWorkflowPersistsOnlySnapshotAndReturnsCompanionsAfterActivation()
    {
        using var database = new TemporaryDatabase();
        var index = CreateSnapshot("net10.0", 'x');
        var behavior = CreateBehavior(index);
        var snapshot = new AnalysisProfileSnapshot(index, behavior);
        var profile = index.Profile;
        var resolver = new FixedProfileResolver(new ResolvedCompilationProfiles(
            ["net10.0"],
            ImmutableArray.Create(profile),
            "test"));
        var store = new SqliteAnalysisStore(database.Path);
        var workflow = new PassAWorkflow(
            resolver,
            new NeverUsedIndexBuilder(),
            new SqliteProgramIndexStore(database.Path),
            store,
            new CompanionAnalysisBuilder(snapshot));

        var result = await workflow.AnalyzeAsync(
            new CompilationProfileResolutionRequest("root", "src/App/App.csproj", "Release"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var active = await store.ReadActiveAsync(profile.Id, CancellationToken.None);
        Assert.Equal(index.IndexFingerprint, active.Value!.ActiveProfile!.ProgramIndex.IndexFingerprint);
        Assert.Equal(behavior.BehaviorFingerprint, active.Value.ActiveProfile.Behavior!.BehaviorFingerprint);

        var companion = Assert.Single(result.Value!.CompanionInspections);
        Assert.Equal(profile.Id, companion.ProfileId);
        Assert.Equal("semantic-companion-test", companion.SemanticDebugProjection);
        Assert.Contains("applied-model test.framework.model 1.0.0", companion.FrameworkDebugProjection, StringComparison.Ordinal);
        Assert.Equal("dependency-injection-companion-test", companion.DependencyInjectionDebugProjection);
    }

    [Fact]
    public async Task DependencyInjectionFailedActivationReturnsNoCompanionSuccessResult()
    {
        using var database = new TemporaryDatabase();
        var index = CreateSnapshot("net10.0", 'y');
        var behavior = CreateBehavior(index);
        var profile = index.Profile;
        var resolver = new FixedProfileResolver(new ResolvedCompilationProfiles(
            ["net10.0"],
            ImmutableArray.Create(profile),
            "test"));
        var store = new SqliteAnalysisStore(database.Path, new ThrowingObserver());
        var workflow = new PassAWorkflow(
            resolver,
            new NeverUsedIndexBuilder(),
            new SqliteProgramIndexStore(database.Path),
            store,
            new CompanionAnalysisBuilder(new AnalysisProfileSnapshot(index, behavior)));

        var result = await workflow.AnalyzeAsync(
            new CompilationProfileResolutionRequest("root", "src/App/App.csproj", "Release"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationOutcome.PersistenceFailure, result.Outcome);
        Assert.Null(result.Value);
    }

    private sealed class BlockingObserver : IPersistenceCheckpointObserver
    {
        private readonly TaskCompletionSource stagingReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StagingReached => stagingReached.Task;

        public async ValueTask ReachedAsync(PersistenceCheckpoint stage, CancellationToken cancellationToken)
        {
            if (stage == PersistenceCheckpoint.AfterStaging)
            {
                stagingReached.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        }

        public void Release() => release.SetResult();
    }

    private sealed class ThrowingObserver : IPersistenceCheckpointObserver
    {
        public ValueTask ReachedAsync(PersistenceCheckpoint stage, CancellationToken cancellationToken)
        {
            if (stage == PersistenceCheckpoint.AfterFirstPointerReplaced)
            {
                throw new IOException("Injected activation failure.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellingObserver(CancellationTokenSource source) : IPersistenceCheckpointObserver
    {
        public ValueTask ReachedAsync(PersistenceCheckpoint stage, CancellationToken cancellationToken)
        {
            if (stage == PersistenceCheckpoint.BeforeActivationCommit)
            {
                source.Cancel();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public TemporaryDatabase() => Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"seqdoc-analysis-{Guid.NewGuid():N}.db");

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = Path + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
