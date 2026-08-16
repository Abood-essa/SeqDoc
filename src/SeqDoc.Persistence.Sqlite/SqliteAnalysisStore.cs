using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Persistence;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Persistence.Sqlite.Diagnostics;
using SeqDoc.Persistence.Sqlite.Serialization;
using SeqDoc.Persistence.Sqlite.Testing;

namespace SeqDoc.Persistence.Sqlite;

/// <summary>
/// Persists the aggregate Program Index and behavior snapshot per profile run and activates all
/// selected profiles atomically. Existing Program Index-only caches remain readable and report
/// behavior as unavailable.
/// </summary>
public sealed class SqliteAnalysisStore : IAnalysisStore
{
    private readonly string databasePath;
    private readonly string connectionString;
    private readonly IPersistenceCheckpointObserver checkpointObserver;

    public SqliteAnalysisStore(string databasePath)
        : this(databasePath, new NoOpPersistenceCheckpointObserver())
    {
    }

    internal SqliteAnalysisStore(string databasePath, IPersistenceCheckpointObserver checkpointObserver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(checkpointObserver);
        this.databasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        this.checkpointObserver = checkpointObserver;
    }

    public async Task<ApplicationResult<AnalysisActivation>> ActivateAsync(
        AnalysisPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validationError = ValidateRequest(request.Snapshots);
        if (validationError is not null)
        {
            return ApplicationResult.Failure<AnalysisActivation>(
                ApplicationOutcome.ValidationFailure,
                [PersistenceDiagnosticFactory.Create(
                    "PD3001",
                    "The analysis snapshot is invalid.",
                    validationError,
                    "Rebuild the analysis before persistence.")]);
        }

        var snapshots = request.Snapshots.OrderBy(snapshot => snapshot.ProgramIndex.Profile.Id.Value, StringComparer.Ordinal).ToArray();
        long? invocationSequence = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            invocationSequence = await CreateStagingInvocationAsync(connection, snapshots, cancellationToken).ConfigureAwait(false);
            var runs = snapshots.Select(snapshot => new ActivatedProfileRun(
                    snapshot.ProgramIndex.Profile.Id,
                    StableIdentity.CreateAnalysisRunId(invocationSequence.Value, snapshot.ProgramIndex.Profile.Id),
                    snapshot.ProgramIndex.IndexFingerprint))
                .ToArray();
            await CreateStagingRunsAsync(connection, invocationSequence.Value, snapshots, runs, cancellationToken)
                .ConfigureAwait(false);

            for (var index = 0; index < snapshots.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SqliteSnapshotRepository.StageAsync(
                    connection,
                    runs[index].RunId,
                    snapshots[index].ProgramIndex,
                    cancellationToken).ConfigureAwait(false);
                if (snapshots[index].Behavior is { } behavior)
                {
                    await StageBehaviorAsync(connection, runs[index].RunId, behavior, cancellationToken).ConfigureAwait(false);
                }
            }

            await checkpointObserver.ReachedAsync(PersistenceCheckpoint.AfterStaging, cancellationToken).ConfigureAwait(false);
            await ValidateStagedAsync(connection, snapshots, runs, cancellationToken).ConfigureAwait(false);
            await checkpointObserver.ReachedAsync(PersistenceCheckpoint.AfterValidation, cancellationToken).ConfigureAwait(false);
            await ActivateRunsAsync(connection, invocationSequence.Value, runs, cancellationToken).ConfigureAwait(false);
            return ApplicationResult.Success(new AnalysisActivation(runs.ToImmutableArray()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryFinalizeAsync(invocationSequence, "Cancelled").ConfigureAwait(false);
            return ApplicationResult.Failure<AnalysisActivation>(ApplicationOutcome.Cancelled, []);
        }
        catch (UnsupportedSchemaException exception)
        {
            return ApplicationResult.Failure<AnalysisActivation>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "PD3002",
                    "The SQLite cache uses a newer schema.",
                    exception.Message,
                    "Use a compatible newer SeqDoc version or choose another cache path.",
                    exception)]);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            await TryFinalizeAsync(invocationSequence, "Failed").ConfigureAwait(false);
            return ApplicationResult.Failure<AnalysisActivation>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "PD3003",
                    "The analysis snapshot could not be activated.",
                    exception.Message,
                    "Check cache permissions and integrity, then retry.",
                    exception)]);
        }
    }

    public async Task<ApplicationResult<ActiveAnalysisLookup>> ReadActiveAsync(
        CompilationProfileId profileId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return ApplicationResult.Success(new ActiveAnalysisLookup(false, null));
        }

        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT r.run_id
                FROM active_profile_runs a
                JOIN profile_runs r ON r.run_id = a.run_id AND r.profile_id = a.profile_id
                JOIN analysis_invocations i ON i.invocation_sequence = r.invocation_sequence
                WHERE a.profile_id = $profile AND r.state = 'Completed' AND i.state = 'Completed';
                """;
            command.Parameters.AddWithValue("$profile", profileId.Value);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null)
            {
                return ApplicationResult.Success(new ActiveAnalysisLookup(false, null));
            }

            var runId = new AnalysisRunId(Convert.ToString(value, CultureInfo.InvariantCulture)!);
            var profile = await ReadProfileAsync(connection, runId, cancellationToken).ConfigureAwait(false);
            return ApplicationResult.Success(new ActiveAnalysisLookup(
                true,
                new ActiveAnalysisProfile(runId, profile.ProgramIndex, profile.Behavior)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApplicationResult.Failure<ActiveAnalysisLookup>(ApplicationOutcome.Cancelled, []);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception) || exception is UnsupportedSchemaException)
        {
            return ApplicationResult.Failure<ActiveAnalysisLookup>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "PD3004",
                    "The active analysis profile could not be read.",
                    exception.Message,
                    "Check the cache schema and integrity.",
                    exception)]);
        }
    }

    public async Task<ApplicationResult<ActiveAnalyses>> ReadAllActiveAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return ApplicationResult.Success(new ActiveAnalyses([]));
        }

        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT r.run_id
                FROM active_profile_runs a
                JOIN profile_runs r ON r.run_id = a.run_id AND r.profile_id = a.profile_id
                JOIN analysis_invocations i ON i.invocation_sequence = r.invocation_sequence
                WHERE r.state = 'Completed' AND i.state = 'Completed'
                ORDER BY a.profile_id;
                """;
            var runIds = new List<AnalysisRunId>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    runIds.Add(new AnalysisRunId(reader.GetString(0)));
                }
            }

            var profiles = ImmutableArray.CreateBuilder<ActiveAnalysisProfile>();
            foreach (var runId in runIds)
            {
                var profile = await ReadProfileAsync(connection, runId, cancellationToken).ConfigureAwait(false);
                profiles.Add(new ActiveAnalysisProfile(runId, profile.ProgramIndex, profile.Behavior));
            }

            return ApplicationResult.Success(new ActiveAnalyses(profiles.ToImmutable()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApplicationResult.Failure<ActiveAnalyses>(ApplicationOutcome.Cancelled, []);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception) || exception is UnsupportedSchemaException)
        {
            return ApplicationResult.Failure<ActiveAnalyses>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "PD3004",
                    "The active analysis catalog could not be read.",
                    exception.Message,
                    "Check the cache schema and integrity.",
                    exception)]);
        }
    }

    private static string? ValidateRequest(ImmutableArray<AnalysisProfileSnapshot> snapshots)
    {
        if (snapshots.IsEmpty)
        {
            return "At least one analysis snapshot is required.";
        }

        foreach (var snapshot in snapshots)
        {
            var error = ProgramIndexSnapshotValidator.Validate([snapshot.ProgramIndex]);
            if (error is not null)
            {
                return $"Program Index: {error}";
            }

            if (snapshot.Behavior is { } behavior
                && behavior.ProgramIndexFingerprint != snapshot.ProgramIndex.IndexFingerprint)
            {
                return "Behavior snapshot fingerprint does not match the Program Index fingerprint.";
            }

            if (snapshot.Behavior is { } behaviorWithFingerprint
                && behaviorWithFingerprint.BehaviorFingerprint != BehaviorFingerprint.Compute(behaviorWithFingerprint))
            {
                return "Behavior snapshot fingerprint does not match its canonical content.";
            }
        }

        return null;
    }

    private static async Task<long> CreateStagingInvocationAsync(
        SqliteConnection connection,
        AnalysisProfileSnapshot[] snapshots,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO analysis_invocations(state, expected_profile_count) VALUES('Staging', $count); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$count", snapshots.Length);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task CreateStagingRunsAsync(
        SqliteConnection connection,
        long invocationSequence,
        AnalysisProfileSnapshot[] snapshots,
        ActivatedProfileRun[] runs,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < snapshots.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = snapshots[index].ProgramIndex;
            var run = runs[index];
            await ExecuteAsync(connection, "INSERT OR IGNORE INTO compilation_profiles(profile_id, canonical_json) VALUES($id,$json);",
                cancellationToken, transaction, ("$id", snapshot.Profile.Id.Value), ("$json", snapshot.Profile.CanonicalJson)).ConfigureAwait(false);
            await ExecuteAsync(connection, """
                INSERT INTO profile_runs(
                    run_id,invocation_sequence,profile_id,state,index_fingerprint,input_manifest_hash,schema_version,producer_version)
                VALUES($run,$invocation,$profile,'Staging',$fingerprint,$manifest,$schema,$producer);
                """, cancellationToken, transaction,
                ("$run", run.RunId.Value), ("$invocation", invocationSequence), ("$profile", snapshot.Profile.Id.Value),
                ("$fingerprint", snapshot.IndexFingerprint), ("$manifest", snapshot.InputManifestHash),
                ("$schema", snapshot.SchemaVersion), ("$producer", snapshot.ProducerVersion)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task StageBehaviorAsync(
        SqliteConnection connection,
        AnalysisRunId runId,
        BehaviorSnapshot behavior,
        CancellationToken cancellationToken)
    {
        var payload = BehaviorSnapshotJsonCodec.Serialize(behavior);
        await ExecuteAsync(connection, """
            INSERT INTO behavior_snapshots(run_id,profile_id,behavior_fingerprint,schema_version,producer_version,payload_json)
            VALUES($run,$profile,$fingerprint,$schema,$producer,$payload);
            """, cancellationToken, null,
            ("$run", runId.Value), ("$profile", behavior.Profile.Id.Value),
            ("$fingerprint", behavior.BehaviorFingerprint), ("$schema", behavior.SchemaVersion),
            ("$producer", behavior.ProducerVersion), ("$payload", payload)).ConfigureAwait(false);
    }

    private static async Task ValidateStagedAsync(
        SqliteConnection connection,
        AnalysisProfileSnapshot[] expectedSnapshots,
        ActivatedProfileRun[] runs,
        CancellationToken cancellationToken)
    {
        var foreignKeyViolations = Convert.ToInt32(
            await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (foreignKeyViolations != 0)
        {
            throw new InvalidDataException("Staged aggregate runs contain foreign-key violations.");
        }

        for (var index = 0; index < runs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actual = await ReadProfileAsync(connection, runs[index].RunId, cancellationToken).ConfigureAwait(false);
            if (actual.ProgramIndex.Profile.Id != runs[index].ProfileId
                || !string.Equals(
                    ProgramIndexJsonCodec.Serialize(expectedSnapshots[index].ProgramIndex),
                    ProgramIndexJsonCodec.Serialize(actual.ProgramIndex),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Staged run '{runs[index].RunId.Value}' failed Program Index reconstruction validation.");
            }

            if (expectedSnapshots[index].Behavior is { } expectedBehavior)
            {
                if (actual.Behavior is null)
                {
                    throw new InvalidDataException(
                        $"Staged run '{runs[index].RunId.Value}' is missing its behavior snapshot.");
                }

                var reconstructed = actual.Behavior;
                if (reconstructed.BehaviorFingerprint != expectedBehavior.BehaviorFingerprint
                    || BehaviorSnapshotJsonCodec.Serialize(reconstructed) != BehaviorSnapshotJsonCodec.Serialize(expectedBehavior))
                {
                    throw new InvalidDataException(
                        $"Staged run '{runs[index].RunId.Value}' failed behavior reconstruction validation.");
                }
            }
        }
    }

    private async Task ActivateRunsAsync(
        SqliteConnection connection,
        long invocationSequence,
        ActivatedProfileRun[] runs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var invocationRows = await ExecuteAsync(connection, """
            UPDATE analysis_invocations SET state='Completed'
            WHERE invocation_sequence=$invocation AND state='Staging' AND expected_profile_count=$count;
            """, cancellationToken, transaction, ("$invocation", invocationSequence), ("$count", runs.Length)).ConfigureAwait(false);
        if (invocationRows != 1)
        {
            throw new InvalidDataException("The staging invocation could not transition to Completed.");
        }

        for (var index = 0; index < runs.Length; index++)
        {
            var run = runs[index];
            var runRows = await ExecuteAsync(connection,
                "UPDATE profile_runs SET state='Completed' WHERE run_id=$run AND state='Staging';",
                cancellationToken, transaction, ("$run", run.RunId.Value)).ConfigureAwait(false);
            if (runRows != 1)
            {
                throw new InvalidDataException($"Staging run '{run.RunId.Value}' could not transition to Completed.");
            }

            await ExecuteAsync(connection, """
                INSERT INTO active_profile_runs(profile_id,run_id) VALUES($profile,$run)
                ON CONFLICT(profile_id) DO UPDATE SET run_id=excluded.run_id;
                """, cancellationToken, transaction,
                ("$profile", run.ProfileId.Value), ("$run", run.RunId.Value)).ConfigureAwait(false);
            if (index == 0)
            {
                await checkpointObserver.ReachedAsync(PersistenceCheckpoint.AfterFirstPointerReplaced, cancellationToken).ConfigureAwait(false);
            }
        }

        await checkpointObserver.ReachedAsync(PersistenceCheckpoint.BeforeActivationCommit, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ActiveAnalysisProfile> ReadProfileAsync(
        SqliteConnection connection,
        AnalysisRunId runId,
        CancellationToken cancellationToken)
    {
        var programIndex = await SqliteSnapshotRepository.ReadAsync(connection, runId, cancellationToken).ConfigureAwait(false);
        var validationError = ProgramIndexSnapshotValidator.Validate([programIndex]);
        if (validationError is not null)
        {
            throw new InvalidDataException(validationError);
        }

        if (!string.Equals(programIndex.IndexFingerprint, ProgramIndexFingerprint.Compute(programIndex), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Stored Program Index '{runId.Value}' fingerprint does not verify.");
        }

        var behavior = await ReadBehaviorAsync(connection, runId, cancellationToken).ConfigureAwait(false);
        return new ActiveAnalysisProfile(runId, programIndex, behavior);
    }

    private static async Task<BehaviorSnapshot?> ReadBehaviorAsync(
        SqliteConnection connection,
        AnalysisRunId runId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM behavior_snapshots WHERE run_id=$run;";
        command.Parameters.AddWithValue("$run", runId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return null;
        }

        var snapshot = BehaviorSnapshotJsonCodec.Deserialize(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        if (string.IsNullOrWhiteSpace(snapshot.BehaviorFingerprint))
        {
            throw new InvalidDataException($"Stored behavior snapshot '{runId.Value}' has no fingerprint.");
        }

        if (!string.Equals(snapshot.BehaviorFingerprint, BehaviorFingerprint.Compute(snapshot), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Stored behavior snapshot '{runId.Value}' fingerprint does not verify.");
        }

        return snapshot;
    }

    private async Task TryFinalizeAsync(long? invocationSequence, string state)
    {
        if (invocationSequence is null)
        {
            return;
        }

        try
        {
            await using var connection = await OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            await ExecuteAsync(connection,
                "UPDATE profile_runs SET state=$state WHERE invocation_sequence=$invocation AND state='Staging';",
                CancellationToken.None, transaction, ("$state", state), ("$invocation", invocationSequence.Value)).ConfigureAwait(false);
            await ExecuteAsync(connection,
                "UPDATE analysis_invocations SET state=$state WHERE invocation_sequence=$invocation AND state='Staging';",
                CancellationToken.None, transaction, ("$state", state), ("$invocation", invocationSequence.Value)).ConfigureAwait(false);
            if (state is "Failed" or "Cancelled")
            {
                await DeleteStagedFactsAsync(connection, transaction, invocationSequence.Value, CancellationToken.None).ConfigureAwait(false);
            }

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception) || exception is UnsupportedSchemaException)
        {
            // Finalization is best effort. The active pointer remains authoritative and never targets staging runs.
        }
    }

    private static async Task DeleteStagedFactsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long invocationSequence,
        CancellationToken cancellationToken)
    {
        string runFilter = "WHERE run_id IN (SELECT run_id FROM profile_runs WHERE invocation_sequence=$invocation)";
        string[] factTables =
        [
            "evidence_underlying", "fact_evidence", "evidence", "attribute_arguments", "program_attributes",
            "method_parameters", "program_methods", "program_members", "type_interfaces", "program_types",
            "program_namespaces", "program_documents", "project_reference_edges", "program_projects",
            "program_invocations", "program_references", "program_inventory_markers",
            "behavior_snapshots",
        ];
        foreach (string table in factTables)
        {
            await ExecuteAsync(
                connection,
                $"DELETE FROM {table} {runFilter};",
                cancellationToken,
                transaction,
                ("$invocation", invocationSequence)).ConfigureAwait(false);
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000; PRAGMA synchronous = FULL;", cancellationToken)
                .ConfigureAwait(false);
            var version = Convert.ToInt32(
                await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (version > SqliteSchema.Version)
            {
                throw new UnsupportedSchemaException(version);
            }

            if (version == 0)
            {
                await SqliteSchema.MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var appliedMigrations = new List<(int Version, string Checksum)>();
                await using (var migrationCommand = connection.CreateCommand())
                {
                    migrationCommand.CommandText = "SELECT version, checksum_sha256 FROM schema_migrations ORDER BY version;";
                    await using var reader = await migrationCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        appliedMigrations.Add((reader.GetInt32(0), reader.GetString(1)));
                    }
                }

                foreach (var expected in SqliteSchema.Migrations)
                {
                    var applied = appliedMigrations.FirstOrDefault(migration => migration.Version == expected.Version);
                    if (applied.Version == 0
                        || !string.Equals(applied.Checksum, SqliteSchema.Checksum(expected.Sql), StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("SQLite migration history checksum does not match the executable migration.");
                    }
                }

                if (version < SqliteSchema.Version)
                {
                    await SqliteSchema.MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
                }
            }

            var journalMode = Convert.ToString(
                await ScalarAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SQLite WAL journal mode could not be enabled.");
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is SqliteException or IOException or InvalidDataException or JsonException or ArgumentException;

    private sealed class UnsupportedSchemaException(int actualVersion)
        : Exception($"Database schema {actualVersion} is newer than supported schema {SqliteSchema.Version}.");
}
