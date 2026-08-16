using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Persistence.Sqlite.Serialization;

namespace SeqDoc.Persistence.Sqlite;

internal static class SqliteSnapshotRepository
{
    private const int CommandsPerTransaction = 256;

    public static async Task StageAsync(
        SqliteConnection connection,
        AnalysisRunId runId,
        ProgramIndexSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var writer = new BoundedWriter(connection, CommandsPerTransaction, cancellationToken);
        var run = runId.Value;
        var profile = snapshot.Profile.Id.Value;

        for (var index = 0; index < snapshot.Projects.Length; index++)
        {
            var item = snapshot.Projects[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_projects VALUES($run,$profile,$ordinal,$id,$name,$path,$tfm,$kind,$fingerprint);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id.Value),
                ("$name", item.Name), ("$path", item.RepositoryRelativePath), ("$tfm", item.TargetFramework),
                ("$kind", (int)item.Kind), ("$fingerprint", item.BuildFingerprint)).ConfigureAwait(false);
        }

        foreach (var item in snapshot.Projects)
        {
            for (var index = 0; index < item.ProjectReferences.Length; index++)
            {
                await writer.ExecuteAsync(
                    "INSERT INTO project_reference_edges VALUES($run,$owner,$ordinal,$target);",
                    ("$run", run), ("$owner", item.Id.Value), ("$ordinal", index),
                    ("$target", item.ProjectReferences[index].Value)).ConfigureAwait(false);
            }
        }

        for (var index = 0; index < snapshot.Documents.Length; index++)
        {
            var item = snapshot.Documents[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_documents VALUES($run,$profile,$ordinal,$id,$project,$path,$origin,$content,$semantic);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id.Value),
                ("$project", item.Project.Value), ("$path", item.LogicalPath), ("$origin", (int)item.Origin),
                ("$content", item.ContentFingerprint), ("$semantic", Db(item.SemanticFingerprint))).ConfigureAwait(false);
        }

        for (var index = 0; index < snapshot.Namespaces.Length; index++)
        {
            var item = snapshot.Namespaces[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_namespaces VALUES($run,$profile,$ordinal,$id,$project,$name);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id.Value),
                ("$project", item.Project.Value), ("$name", item.Name)).ConfigureAwait(false);
        }

        for (var index = 0; index < snapshot.Types.Length; index++)
        {
            var item = snapshot.Types[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_types VALUES($run,$profile,$ordinal,$id,$project,$namespace,$name,$kind,$base,$fingerprint);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id.Value),
                ("$project", item.Project.Value), ("$namespace", item.Namespace.Value), ("$name", item.MetadataName),
                ("$kind", (int)item.Kind), ("$base", Db(item.BaseType?.Value)), ("$fingerprint", item.SignatureFingerprint)).ConfigureAwait(false);
            for (var interfaceIndex = 0; interfaceIndex < item.Interfaces.Length; interfaceIndex++)
            {
                await writer.ExecuteAsync(
                    "INSERT INTO type_interfaces VALUES($run,$type,$ordinal,$interface);",
                    ("$run", run), ("$type", item.Id.Value), ("$ordinal", interfaceIndex),
                    ("$interface", item.Interfaces[interfaceIndex].Value)).ConfigureAwait(false);
            }
        }

        for (var index = 0; index < snapshot.Members.Length; index++)
        {
            var item = snapshot.Members[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_members VALUES($run,$profile,$ordinal,$id,$project,$type,$kind,$name,$value_type,$fingerprint);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id.Value),
                ("$project", item.Project.Value), ("$type", item.ContainingType.Value), ("$kind", (int)item.Kind),
                ("$name", item.Name), ("$value_type", item.FullyQualifiedType), ("$fingerprint", item.SignatureFingerprint)).ConfigureAwait(false);
        }

        for (var index = 0; index < snapshot.Methods.Length; index++)
        {
            var item = snapshot.Methods[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_methods VALUES($run,$profile,$ordinal,$id,$symbol,$type,$name,$display,$return,$signature,$body);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id.Value),
                ("$symbol", item.Symbol.Value), ("$type", item.ContainingType.Value), ("$name", item.Name),
                ("$display", item.DisplaySignature), ("$return", item.ReturnType),
                ("$signature", item.SignatureFingerprint), ("$body", Db(item.BodyFingerprint))).ConfigureAwait(false);
            for (var parameterIndex = 0; parameterIndex < item.Parameters.Length; parameterIndex++)
            {
                var parameter = item.Parameters[parameterIndex];
                await writer.ExecuteAsync(
                    "INSERT INTO method_parameters VALUES($run,$method,$ordinal,$name,$type,$kind);",
                    ("$run", run), ("$method", item.Id.Value), ("$ordinal", parameterIndex),
                    ("$name", parameter.Name), ("$type", parameter.FullyQualifiedType),
                    ("$kind", (int)parameter.RefKind)).ConfigureAwait(false);
            }
        }

        for (var index = 0; index < snapshot.Attributes.Length; index++)
        {
            var item = snapshot.Attributes[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_attributes VALUES($run,$profile,$ordinal,$id,$target,$type,$constructor);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id),
                ("$target", item.Target.Value), ("$type", item.AttributeType), ("$constructor", item.Constructor)).ConfigureAwait(false);
            for (var argumentIndex = 0; argumentIndex < item.Arguments.Length; argumentIndex++)
            {
                await writer.ExecuteAsync(
                    "INSERT INTO attribute_arguments VALUES($run,$attribute,$ordinal,$value);",
                    ("$run", run), ("$attribute", item.Id), ("$ordinal", argumentIndex),
                    ("$value", item.Arguments[argumentIndex])).ConfigureAwait(false);
            }
        }

        for (var index = 0; index < snapshot.References.Length; index++)
        {
            var item = snapshot.References[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_references VALUES($run,$profile,$ordinal,$id,$project,$kind,$identity,$version);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id),
                ("$project", item.Project.Value), ("$kind", (int)item.Kind), ("$identity", item.Identity),
                ("$version", Db(item.Version))).ConfigureAwait(false);
        }

        for (var index = 0; index < snapshot.Invocations.Length; index++)
        {
            var item = snapshot.Invocations[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_invocations VALUES($run,$profile,$ordinal,$id,$method,$target,$display,$certainty);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id.Value),
                ("$method", item.ContainingMethod.Value), ("$target", Db(item.BoundTarget?.Value)),
                ("$display", item.DisplayTarget), ("$certainty", (int)item.Certainty)).ConfigureAwait(false);
        }

        for (var index = 0; index < snapshot.InventoryMarkers.Length; index++)
        {
            var item = snapshot.InventoryMarkers[index];
            await writer.ExecuteAsync(
                "INSERT INTO program_inventory_markers VALUES($run,$profile,$ordinal,$id,$project,$kind,$symbol);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id),
                ("$project", item.Project.Value), ("$kind", (int)item.Kind), ("$symbol", Db(item.Symbol?.Value))).ConfigureAwait(false);
        }

        var evidence = CollectEvidence(snapshot);
        foreach (var item in evidence.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            await writer.ExecuteAsync(
                "INSERT INTO evidence VALUES($run,$profile,$id,$kind,$artifact,$document,$sl,$sc,$el,$ec,$symbol,$detail,$certainty,$producer,$producer_version);",
                ("$run", run), ("$profile", profile), ("$id", item.Id.Value), ("$kind", (int)item.Kind),
                ("$artifact", item.Artifact), ("$document", Db(item.Range?.Document.Value)),
                ("$sl", Db(item.Range?.Start.Line)), ("$sc", Db(item.Range?.Start.Column)),
                ("$el", Db(item.Range?.End.Line)), ("$ec", Db(item.Range?.End.Column)),
                ("$symbol", Db(item.Symbol)), ("$detail", Db(item.Detail)), ("$certainty", (int)item.Certainty),
                ("$producer", Db(item.ProducerId)), ("$producer_version", Db(item.ProducerVersion))).ConfigureAwait(false);
        }

        foreach (var item in evidence.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            for (var index = 0; index < item.UnderlyingEvidence.Length; index++)
            {
                await writer.ExecuteAsync(
                    "INSERT INTO evidence_underlying VALUES($run,$evidence,$ordinal,$underlying);",
                    ("$run", run), ("$evidence", item.Id.Value), ("$ordinal", index),
                    ("$underlying", item.UnderlyingEvidence[index].Id.Value)).ConfigureAwait(false);
            }
        }

        foreach (var owner in EnumerateOwners(snapshot))
        {
            for (var index = 0; index < owner.Evidence.Length; index++)
            {
                await writer.ExecuteAsync(
                    "INSERT INTO fact_evidence VALUES($run,$kind,$owner,$ordinal,$evidence);",
                    ("$run", run), ("$kind", owner.Kind), ("$owner", owner.Id), ("$ordinal", index),
                    ("$evidence", owner.Evidence[index].Id.Value)).ConfigureAwait(false);
            }
        }

        for (var index = 0; index < snapshot.Diagnostics.Length; index++)
        {
            var item = snapshot.Diagnostics[index];
            var range = item.Location.SourceRange;
            await writer.ExecuteAsync(
                "INSERT INTO analysis_diagnostics VALUES($run,$profile,$ordinal,$id,$code,$severity,$stage,$summary,$description,$location_profile,$project,$symbol,$document,$sl,$sc,$el,$ec,$cause,$impact,$action,$certainty,$detail);",
                ("$run", run), ("$profile", profile), ("$ordinal", index), ("$id", item.Id.Value),
                ("$code", item.Code), ("$severity", (int)item.Severity), ("$stage", (int)item.Stage),
                ("$summary", item.Summary), ("$description", item.Location.Description),
                ("$location_profile", Db(item.Location.Profile?.Value)), ("$project", Db(item.Location.Project?.Value)),
                ("$symbol", Db(item.Location.Symbol?.Value)), ("$document", Db(range?.Document.Value)),
                ("$sl", Db(range?.Start.Line)), ("$sc", Db(range?.Start.Column)),
                ("$el", Db(range?.End.Line)), ("$ec", Db(range?.End.Column)),
                ("$cause", item.TechnicalCause), ("$impact", item.UserImpact), ("$action", item.NextAction),
                ("$certainty", (int)item.Certainty), ("$detail", Db(item.InternalDetail))).ConfigureAwait(false);
        }

        await writer.CompleteAsync().ConfigureAwait(false);
    }

    public static async Task<ProgramIndexSnapshot> ReadAsync(
        SqliteConnection connection,
        AnalysisRunId runId,
        CancellationToken cancellationToken)
    {
        var run = runId.Value;
        var header = await ReadSingleAsync(connection, """
            SELECT r.schema_version, r.producer_version, p.canonical_json,
                   r.input_manifest_hash, r.index_fingerprint
            FROM profile_runs r JOIN compilation_profiles p ON p.profile_id = r.profile_id
            WHERE r.run_id = $run;
            """, run, reader => new
        {
            Schema = reader.GetInt32(0),
            Producer = reader.GetString(1),
            Profile = ProgramIndexJsonCodec.DeserializeProfile(reader.GetString(2)),
            Manifest = reader.GetString(3),
            Fingerprint = reader.GetString(4),
        }, cancellationToken).ConfigureAwait(false);

        var evidence = await ReadEvidenceAsync(connection, run, cancellationToken).ConfigureAwait(false);
        var ownerEvidence = await ReadOwnerEvidenceAsync(connection, run, evidence, cancellationToken).ConfigureAwait(false);

        var projectReferences = await ReadLookupAsync(connection,
            "SELECT project_id, referenced_project_id FROM project_reference_edges WHERE run_id=$run ORDER BY project_id, ordinal;",
            run, reader => (reader.GetString(0), new ProjectId(reader.GetString(1))), cancellationToken).ConfigureAwait(false);
        var projects = await ReadArrayAsync(connection, """
            SELECT project_id,name,repository_relative_path,profile_id,target_framework,kind,build_fingerprint
            FROM program_projects WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramProject(
                new ProjectId(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                new CompilationProfileId(reader.GetString(3)), reader.GetString(4), (ProjectKind)reader.GetInt32(5),
                reader.GetString(6), Get(projectReferences, reader.GetString(0)), GetEvidence(ownerEvidence, "project", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var documents = await ReadArrayAsync(connection, """
            SELECT document_id,project_id,logical_path,origin,content_fingerprint,semantic_fingerprint
            FROM program_documents WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramDocument(
                new DocumentId(reader.GetString(0)), new ProjectId(reader.GetString(1)), reader.GetString(2),
                (DocumentOrigin)reader.GetInt32(3), reader.GetString(4), NullableString(reader, 5),
                GetEvidence(ownerEvidence, "document", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var namespaces = await ReadArrayAsync(connection, """
            SELECT symbol_id,project_id,name FROM program_namespaces WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramNamespace(
                new SymbolId(reader.GetString(0)), new ProjectId(reader.GetString(1)), reader.GetString(2),
                GetEvidence(ownerEvidence, "namespace", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var interfaces = await ReadLookupAsync(connection,
            "SELECT type_id,interface_id FROM type_interfaces WHERE run_id=$run ORDER BY type_id,ordinal;",
            run, reader => (reader.GetString(0), new SymbolId(reader.GetString(1))), cancellationToken).ConfigureAwait(false);
        var types = await ReadArrayAsync(connection, """
            SELECT symbol_id,project_id,namespace_id,metadata_name,kind,base_type_id,signature_fingerprint
            FROM program_types WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramType(
                new SymbolId(reader.GetString(0)), new ProjectId(reader.GetString(1)), new SymbolId(reader.GetString(2)),
                reader.GetString(3), (ProgramTypeKind)reader.GetInt32(4), NullableId(reader, 5, value => new SymbolId(value)),
                Get(interfaces, reader.GetString(0)), reader.GetString(6),
                GetEvidence(ownerEvidence, "type", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var members = await ReadArrayAsync(connection, """
            SELECT symbol_id,project_id,containing_type_id,kind,name,fully_qualified_type,signature_fingerprint
            FROM program_members WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramMember(
                new SymbolId(reader.GetString(0)), new ProjectId(reader.GetString(1)), new SymbolId(reader.GetString(2)),
                (ProgramMemberKind)reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                GetEvidence(ownerEvidence, "member", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var parameters = await ReadLookupAsync(connection, """
            SELECT method_id,name,fully_qualified_type,ref_kind FROM method_parameters
            WHERE run_id=$run ORDER BY method_id,ordinal;
            """, run, reader => (reader.GetString(0), new ParameterDescriptor(
                reader.GetString(1), reader.GetString(2), (ParameterRefKind)reader.GetInt32(3))), cancellationToken).ConfigureAwait(false);
        var methods = await ReadArrayAsync(connection, """
            SELECT method_id,symbol_id,containing_type_id,name,display_signature,return_type,signature_fingerprint,body_fingerprint
            FROM program_methods WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramMethod(
                new MethodId(reader.GetString(0)), new SymbolId(reader.GetString(1)), new SymbolId(reader.GetString(2)),
                reader.GetString(3), reader.GetString(4), Get(parameters, reader.GetString(0)), reader.GetString(5),
                reader.GetString(6), NullableString(reader, 7), GetEvidence(ownerEvidence, "method", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var arguments = await ReadLookupAsync(connection,
            "SELECT attribute_id,value FROM attribute_arguments WHERE run_id=$run ORDER BY attribute_id,ordinal;",
            run, reader => (reader.GetString(0), reader.GetString(1)), cancellationToken).ConfigureAwait(false);
        var attributes = await ReadArrayAsync(connection, """
            SELECT attribute_id,target_symbol_id,attribute_type,constructor FROM program_attributes
            WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramAttributeApplication(
                reader.GetString(0), new SymbolId(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
                Get(arguments, reader.GetString(0)), GetEvidence(ownerEvidence, "attribute", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var references = await ReadArrayAsync(connection, """
            SELECT reference_id,project_id,kind,identity,version FROM program_references WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramReference(
                reader.GetString(0), new ProjectId(reader.GetString(1)), (ProgramReferenceKind)reader.GetInt32(2),
                reader.GetString(3), NullableString(reader, 4), GetEvidence(ownerEvidence, "reference", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var invocations = await ReadArrayAsync(connection, """
            SELECT operation_id,containing_method_id,bound_target_id,display_target,certainty
            FROM program_invocations WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramInvocation(
                new OperationId(reader.GetString(0)), new MethodId(reader.GetString(1)),
                NullableId(reader, 2, value => new MethodId(value)), reader.GetString(3),
                GetEvidence(ownerEvidence, "invocation", reader.GetString(0)), (CertaintyLevel)reader.GetInt32(4)), cancellationToken).ConfigureAwait(false);

        var markers = await ReadArrayAsync(connection, """
            SELECT marker_id,project_id,kind,symbol_id FROM program_inventory_markers WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new ProgramInventoryMarker(
                reader.GetString(0), new ProjectId(reader.GetString(1)), (InventoryMarkerKind)reader.GetInt32(2),
                NullableId(reader, 3, value => new SymbolId(value)), GetEvidence(ownerEvidence, "marker", reader.GetString(0))), cancellationToken).ConfigureAwait(false);

        var diagnostics = await ReadArrayAsync(connection, """
            SELECT diagnostic_id,code,severity,stage,summary,location_description,location_profile_id,
                   location_project_id,location_symbol_id,location_document_id,start_line,start_column,end_line,end_column,
                   technical_cause,user_impact,next_action,certainty,internal_detail
            FROM analysis_diagnostics WHERE run_id=$run ORDER BY ordinal;
            """, run, reader => new AnalysisDiagnostic(
                new DiagnosticId(reader.GetString(0)), reader.GetString(1), (DiagnosticSeverity)reader.GetInt32(2),
                (AnalysisStage)reader.GetInt32(3), reader.GetString(4), new DiagnosticLocation(
                    reader.GetString(5), NullableId(reader, 6, value => new CompilationProfileId(value)),
                    NullableId(reader, 7, value => new ProjectId(value)), NullableId(reader, 8, value => new SymbolId(value)),
                    ReadRange(reader, 9)), reader.GetString(14), reader.GetString(15), reader.GetString(16),
                (CertaintyLevel)reader.GetInt32(17), GetEvidence(ownerEvidence, "diagnostic", reader.GetString(0)),
                NullableString(reader, 18)), cancellationToken).ConfigureAwait(false);

        return new ProgramIndexSnapshot(
            header.Schema, header.Producer, header.Profile, projects, documents, namespaces, types, members, methods,
            attributes, references, invocations, markers, diagnostics, header.Manifest, header.Fingerprint);
    }

    private static async Task<IReadOnlyDictionary<EvidenceId, EvidenceRef>> ReadEvidenceAsync(
        SqliteConnection connection,
        string run,
        CancellationToken cancellationToken)
    {
        var rows = await ReadArrayAsync(connection, """
            SELECT evidence_id,kind,artifact,document_id,start_line,start_column,end_line,end_column,symbol,detail,
                   certainty,producer_id,producer_version FROM evidence WHERE run_id=$run ORDER BY evidence_id;
            """, run, reader => new EvidenceRow(
                new EvidenceId(reader.GetString(0)), (EvidenceKind)reader.GetInt32(1), reader.GetString(2), ReadRange(reader, 3),
                NullableString(reader, 8), NullableString(reader, 9), (CertaintyLevel)reader.GetInt32(10),
                NullableString(reader, 11), NullableString(reader, 12)), cancellationToken).ConfigureAwait(false);
        var links = await ReadLookupAsync(connection, """
            SELECT evidence_id,underlying_evidence_id FROM evidence_underlying WHERE run_id=$run ORDER BY evidence_id,ordinal;
            """, run, reader => (reader.GetString(0), new EvidenceId(reader.GetString(1))), cancellationToken).ConfigureAwait(false);
        var rowMap = rows.ToDictionary(item => item.Id);
        var built = new Dictionary<EvidenceId, EvidenceRef>();
        var visiting = new HashSet<EvidenceId>();
        foreach (var row in rows)
        {
            Build(row.Id);
        }

        return built;

        EvidenceRef Build(EvidenceId id)
        {
            if (built.TryGetValue(id, out var existing))
            {
                return existing;
            }

            if (!visiting.Add(id) || !rowMap.TryGetValue(id, out var row))
            {
                throw new InvalidDataException($"Evidence graph contains a cycle or missing node at '{id.Value}'.");
            }

            var underlying = Get(links, id.Value).Select(Build).ToImmutableArray();
            var result = new EvidenceRef(
                row.Id, row.Kind, row.Artifact, row.Range, row.Symbol, row.Detail, row.Certainty,
                underlying, row.ProducerId, row.ProducerVersion);
            visiting.Remove(id);
            built.Add(id, result);
            return result;
        }
    }

    private static async Task<Dictionary<(string Kind, string Id), ImmutableArray<EvidenceRef>>> ReadOwnerEvidenceAsync(
        SqliteConnection connection,
        string run,
        IReadOnlyDictionary<EvidenceId, EvidenceRef> evidence,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string, string), ImmutableArray<EvidenceRef>.Builder>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT owner_kind,owner_id,evidence_id FROM fact_evidence WHERE run_id=$run ORDER BY owner_kind,owner_id,ordinal;";
        command.Parameters.AddWithValue("$run", run);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = (reader.GetString(0), reader.GetString(1));
            if (!result.TryGetValue(key, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<EvidenceRef>();
                result.Add(key, builder);
            }

            var id = new EvidenceId(reader.GetString(2));
            if (!evidence.TryGetValue(id, out var item))
            {
                throw new InvalidDataException($"Fact evidence references missing evidence '{id.Value}'.");
            }

            builder.Add(item);
        }

        return result.ToDictionary(item => item.Key, item => item.Value.ToImmutable());
    }

    private static Dictionary<EvidenceId, EvidenceRef> CollectEvidence(ProgramIndexSnapshot snapshot)
    {
        var result = new Dictionary<EvidenceId, EvidenceRef>();
        foreach (var owner in EnumerateOwners(snapshot))
        {
            foreach (var evidence in owner.Evidence)
            {
                Add(evidence);
            }
        }

        return result;

        void Add(EvidenceRef item)
        {
            if (!result.TryAdd(item.Id, item))
            {
                return;
            }

            foreach (var underlying in item.UnderlyingEvidence)
            {
                Add(underlying);
            }
        }
    }

    private static IEnumerable<EvidenceOwner> EnumerateOwners(ProgramIndexSnapshot snapshot) =>
        snapshot.Projects.Select(item => new EvidenceOwner("project", item.Id.Value, item.Evidence))
            .Concat(snapshot.Documents.Select(item => new EvidenceOwner("document", item.Id.Value, item.Evidence)))
            .Concat(snapshot.Namespaces.Select(item => new EvidenceOwner("namespace", item.Id.Value, item.Evidence)))
            .Concat(snapshot.Types.Select(item => new EvidenceOwner("type", item.Id.Value, item.Evidence)))
            .Concat(snapshot.Members.Select(item => new EvidenceOwner("member", item.Id.Value, item.Evidence)))
            .Concat(snapshot.Methods.Select(item => new EvidenceOwner("method", item.Id.Value, item.Evidence)))
            .Concat(snapshot.Attributes.Select(item => new EvidenceOwner("attribute", item.Id, item.Evidence)))
            .Concat(snapshot.References.Select(item => new EvidenceOwner("reference", item.Id, item.Evidence)))
            .Concat(snapshot.Invocations.Select(item => new EvidenceOwner("invocation", item.Id.Value, item.Evidence)))
            .Concat(snapshot.InventoryMarkers.Select(item => new EvidenceOwner("marker", item.Id, item.Evidence)))
            .Concat(snapshot.Diagnostics.Select(item => new EvidenceOwner("diagnostic", item.Id.Value, item.Evidence)));

    private static async Task<T> ReadSingleAsync<T>(
        SqliteConnection connection,
        string sql,
        string run,
        Func<SqliteDataReader, T> projector,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$run", run);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException($"Program Index run '{run}' is missing its header.");
        }

        var result = projector(reader);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException($"Program Index run '{run}' has duplicate header rows.");
        }

        return result;
    }

    private static async Task<ImmutableArray<T>> ReadArrayAsync<T>(
        SqliteConnection connection,
        string sql,
        string run,
        Func<SqliteDataReader, T> projector,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<T>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$run", run);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(projector(reader));
        }

        return builder.ToImmutable();
    }

    private static async Task<Dictionary<string, ImmutableArray<TValue>>> ReadLookupAsync<TValue>(
        SqliteConnection connection,
        string sql,
        string run,
        Func<SqliteDataReader, (string Key, TValue Value)> projector,
        CancellationToken cancellationToken)
    {
        var builders = new Dictionary<string, ImmutableArray<TValue>.Builder>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$run", run);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pair = projector(reader);
            if (!builders.TryGetValue(pair.Key, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<TValue>();
                builders.Add(pair.Key, builder);
            }

            builder.Add(pair.Value);
        }

        return builders.ToDictionary(item => item.Key, item => item.Value.ToImmutable(), StringComparer.Ordinal);
    }

    private static ImmutableArray<T> Get<T>(IReadOnlyDictionary<string, ImmutableArray<T>> lookup, string key) =>
        lookup.TryGetValue(key, out var values) ? values : [];

    private static ImmutableArray<EvidenceRef> GetEvidence(
        Dictionary<(string Kind, string Id), ImmutableArray<EvidenceRef>> lookup,
        string kind,
        string id) => lookup.TryGetValue((kind, id), out var values) ? values : [];

    private static SourceRange? ReadRange(SqliteDataReader reader, int documentOrdinal)
    {
        if (reader.IsDBNull(documentOrdinal))
        {
            return null;
        }

        return new SourceRange(
            new DocumentId(reader.GetString(documentOrdinal)),
            new SourcePosition(reader.GetInt32(documentOrdinal + 1), reader.GetInt32(documentOrdinal + 2)),
            new SourcePosition(reader.GetInt32(documentOrdinal + 3), reader.GetInt32(documentOrdinal + 4)));
    }

    private static T? NullableId<T>(SqliteDataReader reader, int ordinal, Func<string, T> factory)
        where T : struct => reader.IsDBNull(ordinal) ? null : factory(reader.GetString(ordinal));

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static object Db(object? value) => value ?? DBNull.Value;

    private sealed record EvidenceOwner(string Kind, string Id, ImmutableArray<EvidenceRef> Evidence);

    private sealed record EvidenceRow(
        EvidenceId Id,
        EvidenceKind Kind,
        string Artifact,
        SourceRange? Range,
        string? Symbol,
        string? Detail,
        CertaintyLevel Certainty,
        string? ProducerId,
        string? ProducerVersion);

    private sealed class BoundedWriter : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly int commandLimit;
        private readonly CancellationToken cancellationToken;
        private SqliteTransaction? transaction;
        private int commandCount;

        public BoundedWriter(SqliteConnection connection, int commandLimit, CancellationToken cancellationToken)
        {
            this.connection = connection;
            this.commandLimit = commandLimit;
            this.cancellationToken = cancellationToken;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction ??= (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            commandCount++;
            if (commandCount >= commandLimit)
            {
                await CommitCurrentAsync().ConfigureAwait(false);
            }
        }

        public Task CompleteAsync() => CommitCurrentAsync();

        public async ValueTask DisposeAsync()
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                transaction = null;
            }
        }

        private async Task CommitCurrentAsync()
        {
            if (transaction is null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await transaction.DisposeAsync().ConfigureAwait(false);
            transaction = null;
            commandCount = 0;
        }
    }
}
