using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Persistence;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Persistence.Sqlite;
using SeqDoc.Persistence.Sqlite.Serialization;
using SeqDoc.Persistence.Sqlite.Testing;
using Xunit;

namespace SeqDoc.Persistence.Tests;

public sealed class SqliteProgramIndexStoreTests
{
    [Fact]
    public async Task NonEmptyFullShapeSnapshotSurvivesFreshProcessAndRoundTripsExactly()
    {
        using var database = new TemporaryDatabase();
        var snapshot = CreateSnapshot("net10.0", 'a');
        var activation = await new SqliteProgramIndexStore(database.Path).ActivateAsync(
            new ProgramIndexPersistenceRequest([snapshot]),
            CancellationToken.None);
        Assert.Equal(ApplicationOutcome.Succeeded, activation.Outcome);

        var active = await new SqliteProgramIndexStore(database.Path).ReadActiveAsync(
            snapshot.Profile.Id,
            CancellationToken.None);

        Assert.True(active.Value!.Found);
        Assert.Equal(
            ProgramIndexJsonCodec.Serialize(snapshot),
            ProgramIndexJsonCodec.Serialize(active.Value.ActiveIndex!.Snapshot));
        Assert.Equal(activation.Value!.Runs[0].RunId, active.Value.ActiveIndex.RunId);
        Assert.Single(active.Value.ActiveIndex.Snapshot.Projects[0].ProjectReferences);
        Assert.Single(active.Value.ActiveIndex.Snapshot.Types[0].Interfaces);
        Assert.Single(active.Value.ActiveIndex.Snapshot.Methods[0].Parameters);
        Assert.Single(active.Value.ActiveIndex.Snapshot.Attributes[0].Arguments);
        Assert.Single(active.Value.ActiveIndex.Snapshot.Diagnostics);

        var processResult = await ReadInFreshProcessAsync(database.Path, snapshot.Profile.Id);
        Assert.Equal($"{snapshot.IndexFingerprint}|2|1|1|1", processResult);
    }

    [Fact]
    public async Task OrphanAndInvalidSnapshotsAreRejectedBeforeStaging()
    {
        using var database = new TemporaryDatabase();
        var snapshot = CreateSnapshot("net10.0", 'b');
        var orphan = snapshot with
        {
            Documents = [snapshot.Documents[0] with { Project = new ProjectId("missing-project") }],
            IndexFingerprint = string.Empty,
        };
        orphan = orphan with { IndexFingerprint = ProgramIndexFingerprint.Compute(orphan) };

        var orphanResult = await new SqliteProgramIndexStore(database.Path).ActivateAsync(
            new ProgramIndexPersistenceRequest([orphan]),
            CancellationToken.None);
        var invalidResult = await new SqliteProgramIndexStore(database.Path).ActivateAsync(
            new ProgramIndexPersistenceRequest([snapshot with { IndexFingerprint = new string('0', 64) }]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.ValidationFailure, orphanResult.Outcome);
        Assert.Equal(ApplicationOutcome.ValidationFailure, invalidResult.Outcome);
        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public async Task ConcurrentReadersNeverObserveStagedSnapshot()
    {
        using var database = new TemporaryDatabase();
        var original = CreateSnapshot("net10.0", 'c');
        var replacement = CreateSnapshot("net10.0", 'd');
        Assert.True((await new SqliteProgramIndexStore(database.Path).ActivateAsync(
            new ProgramIndexPersistenceRequest([original]), CancellationToken.None)).IsSuccess);

        var observer = new BlockingObserver();
        var activation = new SqliteProgramIndexStore(database.Path, observer).ActivateAsync(
            new ProgramIndexPersistenceRequest([replacement]), CancellationToken.None);
        await observer.StagingReached;

        var reads = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            new SqliteProgramIndexStore(database.Path).ReadActiveAsync(original.Profile.Id, CancellationToken.None)));
        Assert.All(reads, result => Assert.Equal(original.IndexFingerprint, result.Value!.ActiveIndex!.Snapshot.IndexFingerprint));

        observer.Release();
        Assert.True((await activation).IsSuccess);
        var active = await new SqliteProgramIndexStore(database.Path).ReadActiveAsync(original.Profile.Id, CancellationToken.None);
        Assert.Equal(replacement.IndexFingerprint, active.Value!.ActiveIndex!.Snapshot.IndexFingerprint);
    }

    [Fact]
    public async Task StagedValidationRejectsOrphanedNormalizedRows()
    {
        using var database = new TemporaryDatabase();
        var snapshot = CreateSnapshot("net10.0", 'd');
        var result = await new SqliteProgramIndexStore(database.Path, new CorruptingObserver(database.Path)).ActivateAsync(
            new ProgramIndexPersistenceRequest([snapshot]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.PersistenceFailure, result.Outcome);
        Assert.Equal(0L, await ScalarAsync(database.Path, "SELECT COUNT(*) FROM active_profile_runs;"));
        Assert.Equal("Failed", await ScalarAsync(database.Path, "SELECT state FROM analysis_invocations;"));
    }

    [Fact]
    public async Task FailedActivationPreservesByteEquivalentCanonicalCatalogAndFinalizesLifecycle()
    {
        using var database = new TemporaryDatabase();
        var original = CreateSnapshot("net10.0", 'e');
        var baselineStore = new SqliteProgramIndexStore(database.Path);
        Assert.True((await baselineStore.ActivateAsync(
            new ProgramIndexPersistenceRequest([original]), CancellationToken.None)).IsSuccess);
        var before = await baselineStore.ReadActiveAsync(original.Profile.Id, CancellationToken.None);
        var canonicalBefore = ProgramIndexJsonCodec.Serialize(before.Value!.ActiveIndex!.Snapshot);

        var failed = await new SqliteProgramIndexStore(database.Path, new ThrowingObserver()).ActivateAsync(
            new ProgramIndexPersistenceRequest([CreateSnapshot("net10.0", 'f')]),
            CancellationToken.None);

        Assert.Equal(ApplicationOutcome.PersistenceFailure, failed.Outcome);
        var after = await new SqliteProgramIndexStore(database.Path).ReadActiveAsync(original.Profile.Id, CancellationToken.None);
        Assert.Equal(canonicalBefore, ProgramIndexJsonCodec.Serialize(after.Value!.ActiveIndex!.Snapshot));
        Assert.Equal("Failed", await ScalarAsync(database.Path, "SELECT state FROM analysis_invocations ORDER BY invocation_sequence DESC LIMIT 1;"));
        Assert.Equal("Failed", await ScalarAsync(database.Path, "SELECT state FROM profile_runs ORDER BY rowid DESC LIMIT 1;"));
    }

    [Fact]
    public async Task FailureInsideMultiProfileActivationPreservesEveryPreviousPointer()
    {
        using var database = new TemporaryDatabase();
        var first = CreateSnapshot("net10.0", '1');
        var second = CreateSnapshot("net10.0-windows", '2');
        var baselineStore = new SqliteProgramIndexStore(database.Path);
        Assert.True((await baselineStore.ActivateAsync(
            new ProgramIndexPersistenceRequest([first, second]), CancellationToken.None)).IsSuccess);

        var failed = await new SqliteProgramIndexStore(database.Path, new ThrowingObserver()).ActivateAsync(
            new ProgramIndexPersistenceRequest([
                CreateSnapshot("net10.0", '3'),
                CreateSnapshot("net10.0-windows", '4'),
            ]),
            CancellationToken.None);
        Assert.Equal(ApplicationOutcome.PersistenceFailure, failed.Outcome);

        var restarted = new SqliteProgramIndexStore(database.Path);
        var firstActive = await restarted.ReadActiveAsync(first.Profile.Id, CancellationToken.None);
        var secondActive = await restarted.ReadActiveAsync(second.Profile.Id, CancellationToken.None);
        Assert.Equal(first.IndexFingerprint, firstActive.Value!.ActiveIndex!.Snapshot.IndexFingerprint);
        Assert.Equal(second.IndexFingerprint, secondActive.Value!.ActiveIndex!.Snapshot.IndexFingerprint);
        Assert.Equal(2L, await ScalarAsync(database.Path, "SELECT COUNT(*) FROM profile_runs WHERE state='Failed';"));
    }

    [Fact]
    public async Task CancellationBeforeCommitPreservesActiveSnapshotAndFinalizesLifecycle()
    {
        using var database = new TemporaryDatabase();
        var original = CreateSnapshot("net10.0", '5');
        var store = new SqliteProgramIndexStore(database.Path);
        Assert.True((await store.ActivateAsync(new ProgramIndexPersistenceRequest([original]), CancellationToken.None)).IsSuccess);

        using var cancellation = new CancellationTokenSource();
        var cancelled = await new SqliteProgramIndexStore(database.Path, new CancellingObserver(cancellation)).ActivateAsync(
            new ProgramIndexPersistenceRequest([CreateSnapshot("net10.0", '6')]), cancellation.Token);

        Assert.Equal(ApplicationOutcome.Cancelled, cancelled.Outcome);
        var active = await new SqliteProgramIndexStore(database.Path).ReadActiveAsync(original.Profile.Id, CancellationToken.None);
        Assert.Equal(original.IndexFingerprint, active.Value!.ActiveIndex!.Snapshot.IndexFingerprint);
        Assert.Equal("Cancelled", await ScalarAsync(database.Path, "SELECT state FROM analysis_invocations ORDER BY invocation_sequence DESC LIMIT 1;"));
        Assert.Equal("Cancelled", await ScalarAsync(database.Path, "SELECT state FROM profile_runs ORDER BY rowid DESC LIMIT 1;"));
    }

    [Fact]
    public async Task NewerSchemaAndChangedMigrationChecksumFailWithoutModification()
    {
        using var newerDatabase = new TemporaryDatabase();
        await ExecuteAsync(newerDatabase.Path, "PRAGMA user_version = 99;");
        var newer = await new SqliteProgramIndexStore(newerDatabase.Path).ActivateAsync(
            new ProgramIndexPersistenceRequest([CreateSnapshot("net10.0", '7')]), CancellationToken.None);
        Assert.Equal(ApplicationOutcome.PersistenceFailure, newer.Outcome);
        Assert.Equal(99L, await ScalarAsync(newerDatabase.Path, "PRAGMA user_version;"));

        using var checksumDatabase = new TemporaryDatabase();
        var snapshot = CreateSnapshot("net10.0", '8');
        Assert.True((await new SqliteProgramIndexStore(checksumDatabase.Path).ActivateAsync(
            new ProgramIndexPersistenceRequest([snapshot]), CancellationToken.None)).IsSuccess);
        await ExecuteAsync(checksumDatabase.Path, "UPDATE schema_migrations SET checksum_sha256=lower(hex(randomblob(32))); ");
        var checksum = await new SqliteProgramIndexStore(checksumDatabase.Path).ReadActiveAsync(
            snapshot.Profile.Id, CancellationToken.None);
        Assert.Equal(ApplicationOutcome.PersistenceFailure, checksum.Outcome);
    }

    [Fact]
    public async Task ActivePointerRejectsStagingRun()
    {
        using var database = new TemporaryDatabase();
        var snapshot = CreateSnapshot("net10.0", '9');
        Assert.True((await new SqliteProgramIndexStore(database.Path).ActivateAsync(
            new ProgramIndexPersistenceRequest([snapshot]), CancellationToken.None)).IsSuccess);

        await using var connection = new SqliteConnection($"Data Source={database.Path}");
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;");
        await ExecuteAsync(connection, "INSERT INTO analysis_invocations(state,expected_profile_count) VALUES('Staging',1);");
        var sequence = (long)(await ScalarAsync(connection, "SELECT last_insert_rowid();"))!;
        var runId = StableIdentity.CreateAnalysisRunId(sequence, snapshot.Profile.Id);
        await ExecuteAsync(connection, $"""
            INSERT INTO profile_runs VALUES(
                '{runId.Value}',{sequence},'{snapshot.Profile.Id.Value}','Staging',
                '{snapshot.IndexFingerprint}','{snapshot.InputManifestHash}',1,'test');
            """);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, $"""
            UPDATE active_profile_runs SET run_id='{runId.Value}' WHERE profile_id='{snapshot.Profile.Id.Value}';
            """));
        Assert.Contains("must be completed", exception.Message, StringComparison.Ordinal);
    }

    private static ProgramIndexSnapshot CreateSnapshot(string framework, char hashCharacter)
    {
        var hash = new string(hashCharacter, 64);
        var profile = CompilationProfile.Create("src/App/App.csproj", "Release", framework);
        var projectId = StableIdentity.CreateProjectId(profile.Id, "src/App/App.csproj");
        var referencedProjectId = StableIdentity.CreateProjectId(profile.Id, "src/Library/Library.csproj");
        var documentId = StableIdentity.CreateDocumentId(new DocumentIdentityDescriptor(
            projectId, DocumentIdentityKind.Source, "src/App/Program.cs"));
        var namespaceId = new SymbolId($"namespace:{framework}");
        var typeId = new SymbolId($"type:{framework}");
        var interfaceId = new SymbolId($"external-interface:{framework}");
        var memberId = new SymbolId($"member:{framework}");
        var methodId = new MethodId($"method:{framework}");
        var methodSymbolId = new SymbolId($"method-symbol:{framework}");
        var range = new SourceRange(documentId, new SourcePosition(0, 0), new SourcePosition(0, 10));
        var evidenceId = StableIdentity.CreateEvidenceId(new EvidenceIdentityDescriptor(
            EvidenceKind.Source, "src/App/Program.cs", documentId, 0, 10, "App.Program.Main", CertaintyLevel.Exact));
        var evidence = new EvidenceRef(
            evidenceId, EvidenceKind.Source, "src/App/Program.cs", range, "App.Program.Main", "declaration", CertaintyLevel.Exact);
        var derivedEvidenceId = StableIdentity.CreateEvidenceId(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel, "framework-model", null, null, null, "App.Program", CertaintyLevel.Exact,
            "test-model", "1.0"));
        var derivedEvidence = new EvidenceRef(
            derivedEvidenceId, EvidenceKind.FrameworkModel, "framework-model", null, "App.Program", "classification",
            CertaintyLevel.Exact, [evidence], "test-model", "1.0");
        var diagnosticId = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            "SDTEST", AnalysisStage.BaselineIndex, profile.Id, methodId.Value, 0));

        var snapshot = new ProgramIndexSnapshot(
            1,
            "test",
            profile,
            [
                new ProgramProject(projectId, "App", "src/App/App.csproj", profile.Id, framework, ProjectKind.Executable, hash, [referencedProjectId], [evidence]),
                new ProgramProject(referencedProjectId, "Library", "src/Library/Library.csproj", profile.Id, framework, ProjectKind.Library, hash, [], [evidence]),
            ],
            [new ProgramDocument(documentId, projectId, "src/App/Program.cs", DocumentOrigin.Source, hash, hash, [evidence])],
            [new ProgramNamespace(namespaceId, projectId, "App", [evidence])],
            [new ProgramType(typeId, projectId, namespaceId, "App.Program", ProgramTypeKind.Class, null, [interfaceId], hash, [evidence])],
            [new ProgramMember(memberId, projectId, typeId, ProgramMemberKind.Field, "value", "System.Int32", hash, [evidence])],
            [new ProgramMethod(methodId, methodSymbolId, typeId, "Main", "void App.Program.Main(ref int value)", [new ParameterDescriptor("value", "System.Int32", ParameterRefKind.Ref)], "System.Void", hash, hash, [evidence])],
            [new ProgramAttributeApplication($"attribute:{framework}", typeId, "System.ObsoleteAttribute", ".ctor(string)", ["legacy"], [derivedEvidence])],
            [new ProgramReference($"reference:{framework}", projectId, ProgramReferenceKind.Package, "Example.Package", "1.2.3", [evidence])],
            [new ProgramInvocation(new OperationId($"operation:{framework}"), methodId, methodId, "App.Program.Main", [evidence], CertaintyLevel.Exact)],
            [new ProgramInventoryMarker($"marker:{framework}", projectId, InventoryMarkerKind.EntryPointCandidate, methodSymbolId, [evidence])],
            [new AnalysisDiagnostic(
                diagnosticId, "SDTEST", DiagnosticSeverity.Warning, AnalysisStage.BaselineIndex, "Test warning.",
                new DiagnosticLocation("test location", profile.Id, projectId, methodSymbolId, range),
                "Test cause.", "Test impact.", "Test action.", CertaintyLevel.Exact, [evidence], "test detail")],
            hash,
            string.Empty);
        return snapshot with { IndexFingerprint = ProgramIndexFingerprint.Compute(snapshot) };
    }

    private static async Task<string> ReadInFreshProcessAsync(string databasePath, CompilationProfileId profileId)
    {
        var root = FindRepositoryRoot();
        var host = Path.Combine(root, "tools", "SeqDoc.Persistence.TestHost", "bin", "Release", "net10.0", "SeqDoc.Persistence.TestHost.dll");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { host, databasePath, profileId.Value },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static async Task<object?> ScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        return await ScalarAsync(connection, sql);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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

    private sealed class CorruptingObserver(string databasePath) : IPersistenceCheckpointObserver
    {
        public async ValueTask ReachedAsync(PersistenceCheckpoint stage, CancellationToken cancellationToken)
        {
            if (stage != PersistenceCheckpoint.AfterStaging)
            {
                return;
            }

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO fact_evidence(run_id,owner_kind,owner_id,ordinal,evidence_id)
                SELECT run_id,'project','missing-project',0,evidence_id FROM evidence LIMIT 1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
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
            $"seqdoc-persistence-{Guid.NewGuid():N}.db");

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
