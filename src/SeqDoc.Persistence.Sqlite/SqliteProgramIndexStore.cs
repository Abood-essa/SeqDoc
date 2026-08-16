using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Persistence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Persistence.Sqlite.Diagnostics;
using SeqDoc.Persistence.Sqlite.Serialization;
using SeqDoc.Persistence.Sqlite.Testing;

namespace SeqDoc.Persistence.Sqlite;

public sealed class SqliteProgramIndexStore : IProgramIndexStore
{
    private readonly string databasePath;
    private readonly string connectionString;
    private readonly IPersistenceCheckpointObserver checkpointObserver;

    public SqliteProgramIndexStore(string databasePath)
        : this(databasePath, new NoOpPersistenceCheckpointObserver())
    {
    }

    internal SqliteProgramIndexStore(string databasePath, IPersistenceCheckpointObserver checkpointObserver)
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

    public async Task<ApplicationResult<ProgramIndexActivation>> ActivateAsync(
        ProgramIndexPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validationError = ProgramIndexSnapshotValidator.Validate(request.Snapshots);
        if (validationError is not null)
        {
            return ApplicationResult.Failure<ProgramIndexActivation>(
                ApplicationOutcome.ValidationFailure,
                [PersistenceDiagnosticFactory.Create(
                    "SD2001",
                    "The Program Index snapshot is invalid.",
                    validationError,
                    "Rebuild the Program Index before persistence.")]);
        }

        var snapshots = request.Snapshots.OrderBy(snapshot => snapshot.Profile.Id.Value, StringComparer.Ordinal).ToArray();
        long? invocationSequence = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            invocationSequence = await CreateStagingInvocationAsync(connection, snapshots, cancellationToken).ConfigureAwait(false);
            var runs = snapshots.Select(snapshot => new ActivatedProfileRun(
                    snapshot.Profile.Id,
                    StableIdentity.CreateAnalysisRunId(invocationSequence.Value, snapshot.Profile.Id),
                    snapshot.IndexFingerprint))
                .ToArray();
            await CreateStagingRunsAsync(connection, invocationSequence.Value, snapshots, runs, cancellationToken)
                .ConfigureAwait(false);

            for (var index = 0; index < snapshots.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SqliteSnapshotRepository.StageAsync(connection, runs[index].RunId, snapshots[index], cancellationToken)
                    .ConfigureAwait(false);
            }

            await checkpointObserver.ReachedAsync(PersistenceCheckpoint.AfterStaging, cancellationToken).ConfigureAwait(false);
            await ValidateStagedAsync(connection, snapshots, runs, cancellationToken).ConfigureAwait(false);
            await checkpointObserver.ReachedAsync(PersistenceCheckpoint.AfterValidation, cancellationToken).ConfigureAwait(false);
            await ActivateRunsAsync(connection, invocationSequence.Value, runs, cancellationToken).ConfigureAwait(false);
            return ApplicationResult.Success(new ProgramIndexActivation(runs.ToImmutableArray()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryFinalizeAsync(invocationSequence, "Cancelled").ConfigureAwait(false);
            return ApplicationResult.Failure<ProgramIndexActivation>(ApplicationOutcome.Cancelled, []);
        }
        catch (UnsupportedSchemaException exception)
        {
            return ApplicationResult.Failure<ProgramIndexActivation>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "SD2002",
                    "The SQLite cache uses a newer schema.",
                    exception.Message,
                    "Use a compatible newer SeqDoc version or choose another cache path.",
                    exception)]);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            await TryFinalizeAsync(invocationSequence, "Failed").ConfigureAwait(false);
            return ApplicationResult.Failure<ProgramIndexActivation>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "SD2003",
                    "The Program Index could not be activated.",
                    exception.Message,
                    "Check cache permissions and integrity, then retry.",
                    exception)]);
        }
    }

    public async Task<ApplicationResult<ActiveProgramIndexLookup>> ReadActiveAsync(
        CompilationProfileId profileId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return ApplicationResult.Success(new ActiveProgramIndexLookup(false, null));
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
                return ApplicationResult.Success(new ActiveProgramIndexLookup(false, null));
            }

            var runId = new AnalysisRunId(Convert.ToString(value, CultureInfo.InvariantCulture)!);
            var snapshot = await SqliteSnapshotRepository.ReadAsync(connection, runId, cancellationToken).ConfigureAwait(false);
            var validationError = ProgramIndexSnapshotValidator.Validate(snapshot);
            if (validationError is not null || snapshot.Profile.Id != profileId)
            {
                throw new InvalidDataException(validationError ?? "The active run belongs to another profile.");
            }

            return ApplicationResult.Success(new ActiveProgramIndexLookup(
                true,
                new ActiveProgramIndex(runId, snapshot)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApplicationResult.Failure<ActiveProgramIndexLookup>(ApplicationOutcome.Cancelled, []);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception) || exception is UnsupportedSchemaException)
        {
            return ApplicationResult.Failure<ActiveProgramIndexLookup>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "SD2004",
                    "The active Program Index could not be read.",
                    exception.Message,
                    "Check the cache schema and integrity.",
                    exception)]);
        }
    }

    public async Task<ApplicationResult<ActiveProgramIndexes>> ReadAllActiveAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return ApplicationResult.Success(new ActiveProgramIndexes([]));
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

            var indexes = ImmutableArray.CreateBuilder<ActiveProgramIndex>();
            foreach (var runId in runIds)
            {
                var snapshot = await SqliteSnapshotRepository.ReadAsync(connection, runId, cancellationToken).ConfigureAwait(false);
                var validationError = ProgramIndexSnapshotValidator.Validate(snapshot);
                if (validationError is not null)
                {
                    throw new InvalidDataException(validationError);
                }

                indexes.Add(new ActiveProgramIndex(runId, snapshot));
            }

            return ApplicationResult.Success(new ActiveProgramIndexes(indexes.ToImmutable()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApplicationResult.Failure<ActiveProgramIndexes>(ApplicationOutcome.Cancelled, []);
        }
        catch (UnsupportedSchemaException exception)
        {
            return ApplicationResult.Failure<ActiveProgramIndexes>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "SD2002",
                    "The SQLite cache uses a newer schema.",
                    exception.Message,
                    "Use a compatible newer SeqDoc version or choose another cache path.",
                    exception)]);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            return ApplicationResult.Failure<ActiveProgramIndexes>(
                ApplicationOutcome.PersistenceFailure,
                [PersistenceDiagnosticFactory.Create(
                    "SD2004",
                    "The active Program Index catalog could not be read.",
                    exception.Message,
                    "Check cache permissions and integrity, then retry.",
                    exception)]);
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

    private static async Task<long> CreateStagingInvocationAsync(
        SqliteConnection connection,
        ProgramIndexSnapshot[] snapshots,
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
        ProgramIndexSnapshot[] snapshots,
        ActivatedProfileRun[] runs,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < snapshots.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = snapshots[index];
            var run = runs[index];
            await ExecuteAsync(connection, "INSERT OR IGNORE INTO compilation_profiles(profile_id, canonical_json) VALUES($id,$json);",
                cancellationToken, transaction, ("$id", snapshot.Profile.Id.Value), ("$json", snapshot.Profile.CanonicalJson)).ConfigureAwait(false);
            var storedProfile = Convert.ToString(await ScalarAsync(
                connection, "SELECT canonical_json FROM compilation_profiles WHERE profile_id=$id;", cancellationToken,
                transaction, ("$id", snapshot.Profile.Id.Value)).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (!string.Equals(storedProfile, snapshot.Profile.CanonicalJson, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Compilation profile '{snapshot.Profile.Id.Value}' conflicts with its stored descriptor.");
            }

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

    private static async Task ValidateStagedAsync(
        SqliteConnection connection,
        ProgramIndexSnapshot[] expectedSnapshots,
        ActivatedProfileRun[] runs,
        CancellationToken cancellationToken)
    {
        var foreignKeyViolations = Convert.ToInt32(
            await ScalarAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;", cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (foreignKeyViolations != 0)
        {
            throw new InvalidDataException("Staged runs contain foreign-key violations.");
        }

        var invalidEvidenceOwners = Convert.ToInt32(await ScalarAsync(connection, """
            SELECT COUNT(*) FROM fact_evidence f
            WHERE NOT (
                (f.owner_kind='project' AND EXISTS(SELECT 1 FROM program_projects x WHERE x.run_id=f.run_id AND x.project_id=f.owner_id)) OR
                (f.owner_kind='document' AND EXISTS(SELECT 1 FROM program_documents x WHERE x.run_id=f.run_id AND x.document_id=f.owner_id)) OR
                (f.owner_kind='namespace' AND EXISTS(SELECT 1 FROM program_namespaces x WHERE x.run_id=f.run_id AND x.symbol_id=f.owner_id)) OR
                (f.owner_kind='type' AND EXISTS(SELECT 1 FROM program_types x WHERE x.run_id=f.run_id AND x.symbol_id=f.owner_id)) OR
                (f.owner_kind='member' AND EXISTS(SELECT 1 FROM program_members x WHERE x.run_id=f.run_id AND x.symbol_id=f.owner_id)) OR
                (f.owner_kind='method' AND EXISTS(SELECT 1 FROM program_methods x WHERE x.run_id=f.run_id AND x.method_id=f.owner_id)) OR
                (f.owner_kind='attribute' AND EXISTS(SELECT 1 FROM program_attributes x WHERE x.run_id=f.run_id AND x.attribute_id=f.owner_id)) OR
                (f.owner_kind='reference' AND EXISTS(SELECT 1 FROM program_references x WHERE x.run_id=f.run_id AND x.reference_id=f.owner_id)) OR
                (f.owner_kind='invocation' AND EXISTS(SELECT 1 FROM program_invocations x WHERE x.run_id=f.run_id AND x.operation_id=f.owner_id)) OR
                (f.owner_kind='marker' AND EXISTS(SELECT 1 FROM program_inventory_markers x WHERE x.run_id=f.run_id AND x.marker_id=f.owner_id)) OR
                (f.owner_kind='diagnostic' AND EXISTS(SELECT 1 FROM analysis_diagnostics x WHERE x.run_id=f.run_id AND x.diagnostic_id=f.owner_id))
            );
            """, cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (invalidEvidenceOwners != 0)
        {
            throw new InvalidDataException("Staged runs contain evidence links with missing owners.");
        }

        for (var index = 0; index < runs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reconstructed = await SqliteSnapshotRepository.ReadAsync(connection, runs[index].RunId, cancellationToken)
                .ConfigureAwait(false);
            var validationError = ProgramIndexSnapshotValidator.Validate(reconstructed);
            if (validationError is not null
                || reconstructed.Profile.Id != runs[index].ProfileId
                || !string.Equals(
                    ProgramIndexJsonCodec.Serialize(expectedSnapshots[index]),
                    ProgramIndexJsonCodec.Serialize(reconstructed),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Staged run '{runs[index].RunId.Value}' failed normalized reconstruction validation: {validationError ?? "canonical content differs"}.");
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
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception) || exception is UnsupportedSchemaException)
        {
            // Finalization is best effort. The active pointer remains authoritative and never targets staging runs.
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

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is SqliteException or IOException or InvalidDataException or JsonException or ArgumentException;

    private sealed class UnsupportedSchemaException(int actualVersion)
        : Exception($"Database schema {actualVersion} is newer than supported schema {SqliteSchema.Version}.");
}
