using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SeqDoc.Persistence.Sqlite;

internal static class SqliteSchema
{
    public const int Version = 2;
    public const string Migration1Name = "run-versioned-program-index";
    public const string Migration2Name = "run-versioned-behavior-aggregate";

    public static readonly (int Version, string Name, string Sql)[] Migrations =
    [
        (1, Migration1Name, MigrationV1Sql),
        (2, Migration2Name, MigrationV2Sql),
    ];

    public static string Checksum(string sql) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

    public static string ChecksumFor(int version) => Checksum(Migrations.Single(migration => migration.Version == version).Sql);

    public const string MigrationV1Sql = """
        CREATE TABLE schema_migrations(
            version INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            checksum_sha256 TEXT NOT NULL CHECK(length(checksum_sha256) = 64));
        CREATE TABLE analysis_invocations(
            invocation_sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            state TEXT NOT NULL CHECK(state IN ('Staging', 'Completed', 'Failed', 'Cancelled')),
            expected_profile_count INTEGER NOT NULL CHECK(expected_profile_count > 0));
        CREATE TABLE compilation_profiles(
            profile_id TEXT PRIMARY KEY,
            canonical_json TEXT NOT NULL);
        CREATE TABLE profile_runs(
            run_id TEXT PRIMARY KEY,
            invocation_sequence INTEGER NOT NULL REFERENCES analysis_invocations(invocation_sequence),
            profile_id TEXT NOT NULL REFERENCES compilation_profiles(profile_id),
            state TEXT NOT NULL CHECK(state IN ('Staging', 'Completed', 'Failed', 'Cancelled')),
            index_fingerprint TEXT NOT NULL CHECK(length(index_fingerprint) = 64),
            input_manifest_hash TEXT NOT NULL CHECK(length(input_manifest_hash) = 64),
            schema_version INTEGER NOT NULL,
            producer_version TEXT NOT NULL,
            UNIQUE(invocation_sequence, profile_id),
            UNIQUE(run_id, profile_id));
        CREATE TABLE program_projects(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            project_id TEXT NOT NULL,
            name TEXT NOT NULL,
            repository_relative_path TEXT NOT NULL,
            target_framework TEXT NOT NULL,
            kind INTEGER NOT NULL,
            build_fingerprint TEXT NOT NULL,
            PRIMARY KEY(run_id, project_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id));
        CREATE TABLE project_reference_edges(
            run_id TEXT NOT NULL,
            project_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            referenced_project_id TEXT NOT NULL,
            PRIMARY KEY(run_id, project_id, ordinal),
            UNIQUE(run_id, project_id, referenced_project_id),
            FOREIGN KEY(run_id, project_id) REFERENCES program_projects(run_id, project_id),
            FOREIGN KEY(run_id, referenced_project_id) REFERENCES program_projects(run_id, project_id));
        CREATE TABLE program_documents(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            document_id TEXT NOT NULL,
            project_id TEXT NOT NULL,
            logical_path TEXT NOT NULL,
            origin INTEGER NOT NULL,
            content_fingerprint TEXT NOT NULL,
            semantic_fingerprint TEXT,
            PRIMARY KEY(run_id, document_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, project_id) REFERENCES program_projects(run_id, project_id));
        CREATE TABLE program_namespaces(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            symbol_id TEXT NOT NULL,
            project_id TEXT NOT NULL,
            name TEXT NOT NULL,
            PRIMARY KEY(run_id, symbol_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, project_id) REFERENCES program_projects(run_id, project_id));
        CREATE TABLE program_types(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            symbol_id TEXT NOT NULL,
            project_id TEXT NOT NULL,
            namespace_id TEXT NOT NULL,
            metadata_name TEXT NOT NULL,
            kind INTEGER NOT NULL,
            base_type_id TEXT,
            signature_fingerprint TEXT NOT NULL,
            PRIMARY KEY(run_id, symbol_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, project_id) REFERENCES program_projects(run_id, project_id),
            FOREIGN KEY(run_id, namespace_id) REFERENCES program_namespaces(run_id, symbol_id));
        CREATE TABLE type_interfaces(
            run_id TEXT NOT NULL,
            type_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            interface_id TEXT NOT NULL,
            PRIMARY KEY(run_id, type_id, ordinal),
            UNIQUE(run_id, type_id, interface_id),
            FOREIGN KEY(run_id, type_id) REFERENCES program_types(run_id, symbol_id));
        CREATE TABLE program_members(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            symbol_id TEXT NOT NULL,
            project_id TEXT NOT NULL,
            containing_type_id TEXT NOT NULL,
            kind INTEGER NOT NULL,
            name TEXT NOT NULL,
            fully_qualified_type TEXT NOT NULL,
            signature_fingerprint TEXT NOT NULL,
            PRIMARY KEY(run_id, symbol_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, project_id) REFERENCES program_projects(run_id, project_id),
            FOREIGN KEY(run_id, containing_type_id) REFERENCES program_types(run_id, symbol_id));
        CREATE TABLE program_methods(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            method_id TEXT NOT NULL,
            symbol_id TEXT NOT NULL,
            containing_type_id TEXT NOT NULL,
            name TEXT NOT NULL,
            display_signature TEXT NOT NULL,
            return_type TEXT NOT NULL,
            signature_fingerprint TEXT NOT NULL,
            body_fingerprint TEXT,
            PRIMARY KEY(run_id, method_id),
            UNIQUE(run_id, symbol_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, containing_type_id) REFERENCES program_types(run_id, symbol_id));
        CREATE TABLE method_parameters(
            run_id TEXT NOT NULL,
            method_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            name TEXT NOT NULL,
            fully_qualified_type TEXT NOT NULL,
            ref_kind INTEGER NOT NULL,
            PRIMARY KEY(run_id, method_id, ordinal),
            FOREIGN KEY(run_id, method_id) REFERENCES program_methods(run_id, method_id));
        CREATE TABLE program_attributes(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            attribute_id TEXT NOT NULL,
            target_symbol_id TEXT NOT NULL,
            attribute_type TEXT NOT NULL,
            constructor TEXT NOT NULL,
            PRIMARY KEY(run_id, attribute_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id));
        CREATE TABLE attribute_arguments(
            run_id TEXT NOT NULL,
            attribute_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            value TEXT NOT NULL,
            PRIMARY KEY(run_id, attribute_id, ordinal),
            FOREIGN KEY(run_id, attribute_id) REFERENCES program_attributes(run_id, attribute_id));
        CREATE TABLE program_references(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            reference_id TEXT NOT NULL,
            project_id TEXT NOT NULL,
            kind INTEGER NOT NULL,
            identity TEXT NOT NULL,
            version TEXT,
            PRIMARY KEY(run_id, reference_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, project_id) REFERENCES program_projects(run_id, project_id));
        CREATE TABLE program_invocations(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            operation_id TEXT NOT NULL,
            containing_method_id TEXT NOT NULL,
            bound_target_id TEXT,
            display_target TEXT NOT NULL,
            certainty INTEGER NOT NULL,
            PRIMARY KEY(run_id, operation_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, containing_method_id) REFERENCES program_methods(run_id, method_id));
        CREATE TABLE program_inventory_markers(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            marker_id TEXT NOT NULL,
            project_id TEXT NOT NULL,
            kind INTEGER NOT NULL,
            symbol_id TEXT,
            PRIMARY KEY(run_id, marker_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, project_id) REFERENCES program_projects(run_id, project_id));
        CREATE TABLE evidence(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            evidence_id TEXT NOT NULL,
            kind INTEGER NOT NULL,
            artifact TEXT NOT NULL,
            document_id TEXT,
            start_line INTEGER,
            start_column INTEGER,
            end_line INTEGER,
            end_column INTEGER,
            symbol TEXT,
            detail TEXT,
            certainty INTEGER NOT NULL,
            producer_id TEXT,
            producer_version TEXT,
            PRIMARY KEY(run_id, evidence_id),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, document_id) REFERENCES program_documents(run_id, document_id),
            CHECK((document_id IS NULL AND start_line IS NULL AND start_column IS NULL AND end_line IS NULL AND end_column IS NULL)
               OR (document_id IS NOT NULL AND start_line IS NOT NULL AND start_column IS NOT NULL AND end_line IS NOT NULL AND end_column IS NOT NULL)));
        CREATE TABLE evidence_underlying(
            run_id TEXT NOT NULL,
            evidence_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            underlying_evidence_id TEXT NOT NULL,
            PRIMARY KEY(run_id, evidence_id, ordinal),
            UNIQUE(run_id, evidence_id, underlying_evidence_id),
            FOREIGN KEY(run_id, evidence_id) REFERENCES evidence(run_id, evidence_id),
            FOREIGN KEY(run_id, underlying_evidence_id) REFERENCES evidence(run_id, evidence_id));
        CREATE TABLE fact_evidence(
            run_id TEXT NOT NULL,
            owner_kind TEXT NOT NULL,
            owner_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            evidence_id TEXT NOT NULL,
            PRIMARY KEY(run_id, owner_kind, owner_id, ordinal),
            UNIQUE(run_id, owner_kind, owner_id, evidence_id),
            FOREIGN KEY(run_id, evidence_id) REFERENCES evidence(run_id, evidence_id));
        CREATE TABLE analysis_diagnostics(
            run_id TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            diagnostic_id TEXT NOT NULL,
            code TEXT NOT NULL,
            severity INTEGER NOT NULL,
            stage INTEGER NOT NULL,
            summary TEXT NOT NULL,
            location_description TEXT NOT NULL,
            location_profile_id TEXT,
            location_project_id TEXT,
            location_symbol_id TEXT,
            location_document_id TEXT,
            start_line INTEGER,
            start_column INTEGER,
            end_line INTEGER,
            end_column INTEGER,
            technical_cause TEXT NOT NULL,
            user_impact TEXT NOT NULL,
            next_action TEXT NOT NULL,
            certainty INTEGER NOT NULL,
            internal_detail TEXT,
            PRIMARY KEY(run_id, diagnostic_id),
            UNIQUE(run_id, ordinal),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id),
            FOREIGN KEY(run_id, location_project_id) REFERENCES program_projects(run_id, project_id),
            FOREIGN KEY(run_id, location_document_id) REFERENCES program_documents(run_id, document_id));
        CREATE TABLE active_profile_runs(
            profile_id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL UNIQUE,
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id));
        CREATE TRIGGER active_run_must_be_completed
        BEFORE INSERT ON active_profile_runs
        WHEN NOT EXISTS(
            SELECT 1 FROM profile_runs r
            JOIN analysis_invocations i ON i.invocation_sequence = r.invocation_sequence
            WHERE r.run_id = NEW.run_id AND r.profile_id = NEW.profile_id
              AND r.state = 'Completed' AND i.state = 'Completed')
        BEGIN SELECT RAISE(ABORT, 'active run and invocation must be completed'); END;
        CREATE TRIGGER updated_active_run_must_be_completed
        BEFORE UPDATE ON active_profile_runs
        WHEN NOT EXISTS(
            SELECT 1 FROM profile_runs r
            JOIN analysis_invocations i ON i.invocation_sequence = r.invocation_sequence
            WHERE r.run_id = NEW.run_id AND r.profile_id = NEW.profile_id
              AND r.state = 'Completed' AND i.state = 'Completed')
        BEGIN SELECT RAISE(ABORT, 'active run and invocation must be completed'); END;
        CREATE TRIGGER active_profile_run_cannot_be_downgraded
        BEFORE UPDATE OF state ON profile_runs
        WHEN NEW.state <> 'Completed' AND EXISTS(
            SELECT 1 FROM active_profile_runs a WHERE a.run_id = OLD.run_id)
        BEGIN SELECT RAISE(ABORT, 'active profile run must remain completed'); END;
        CREATE TRIGGER active_invocation_cannot_be_downgraded
        BEFORE UPDATE OF state ON analysis_invocations
        WHEN NEW.state <> 'Completed' AND EXISTS(
            SELECT 1 FROM profile_runs r
            JOIN active_profile_runs a ON a.run_id = r.run_id
            WHERE r.invocation_sequence = OLD.invocation_sequence)
        BEGIN SELECT RAISE(ABORT, 'active invocation must remain completed'); END;
        """;

    public const string MigrationV2Sql = """
        CREATE TABLE behavior_snapshots(
            run_id TEXT PRIMARY KEY,
            profile_id TEXT NOT NULL,
            behavior_fingerprint TEXT NOT NULL CHECK(length(behavior_fingerprint) = 64),
            schema_version INTEGER NOT NULL,
            producer_version TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            UNIQUE(run_id, profile_id),
            FOREIGN KEY(run_id, profile_id) REFERENCES profile_runs(run_id, profile_id));
        CREATE TRIGGER behavior_requires_completed_run
        BEFORE INSERT ON behavior_snapshots
        WHEN NOT EXISTS(
            SELECT 1 FROM profile_runs r WHERE r.run_id = NEW.run_id AND r.profile_id = NEW.profile_id AND r.state = 'Staging')
        BEGIN SELECT RAISE(ABORT, 'behavior snapshot requires a staging run'); END;
        """;

    public static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var current = Convert.ToInt32(
            await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var migration in Migrations.Where(migration => migration.Version > current).OrderBy(migration => migration.Version))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            command.CommandText = "INSERT INTO schema_migrations(version, name, checksum_sha256) VALUES($version, $name, $checksum);";
            command.Parameters.AddWithValue("$version", migration.Version);
            command.Parameters.AddWithValue("$name", migration.Name);
            command.Parameters.AddWithValue("$checksum", Checksum(migration.Sql));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            command.Parameters.Clear();
            command.CommandText = $"PRAGMA user_version = {migration.Version};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
}
