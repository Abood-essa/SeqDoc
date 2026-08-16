using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Persistence.Sqlite.Serialization;

namespace SeqDoc.Persistence.Sqlite;

internal static class ProgramIndexSnapshotValidator
{
    public static string? Validate(ImmutableArray<ProgramIndexSnapshot> snapshots)
    {
        if (snapshots.IsDefaultOrEmpty)
        {
            return "At least one snapshot is required.";
        }

        if (snapshots.Any(snapshot => snapshot is null))
        {
            return "Snapshots cannot contain null values.";
        }

        if (snapshots.Select(snapshot => snapshot.Profile.Id).Distinct().Count() != snapshots.Length)
        {
            return "A persistence invocation cannot contain duplicate profile IDs.";
        }

        foreach (var snapshot in snapshots)
        {
            var error = Validate(snapshot);
            if (error is not null)
            {
                return $"Snapshot '{snapshot.Profile.Id.Value}' is invalid: {error}";
            }
        }

        return null;
    }

    public static string? Validate(ProgramIndexSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1)
        {
            return $"Program Index schema version {snapshot.SchemaVersion} is unsupported.";
        }

        if (string.IsNullOrWhiteSpace(snapshot.ProducerVersion)
            || string.IsNullOrWhiteSpace(snapshot.Profile.Id.Value)
            || string.IsNullOrWhiteSpace(snapshot.Profile.CanonicalJson))
        {
            return "Producer and compilation-profile identity are required.";
        }

        if (!AreInitialized(snapshot))
        {
            return "Every Program Index collection must be initialized.";
        }

        if (!IsSha256(snapshot.InputManifestHash) || !IsSha256(snapshot.IndexFingerprint))
        {
            return "Manifest and index fingerprints must be lowercase SHA-256 values.";
        }

        if (!Unique(snapshot.Projects, item => item.Id.Value)
            || !Unique(snapshot.Documents, item => item.Id.Value)
            || !Unique(snapshot.Namespaces, item => item.Id.Value)
            || !Unique(snapshot.Types, item => item.Id.Value)
            || !Unique(snapshot.Members, item => item.Id.Value)
            || !Unique(snapshot.Methods, item => item.Id.Value)
            || !Unique(snapshot.Methods, item => item.Symbol.Value)
            || !Unique(snapshot.Attributes, item => item.Id)
            || !Unique(snapshot.References, item => item.Id)
            || !Unique(snapshot.Invocations, item => item.Id.Value)
            || !Unique(snapshot.InventoryMarkers, item => item.Id)
            || !Unique(snapshot.Diagnostics, item => item.Id.Value))
        {
            return "Program Index record IDs must be unique within each record family.";
        }

        var projects = snapshot.Projects.ToDictionary(item => item.Id);
        var documents = snapshot.Documents.ToDictionary(item => item.Id);
        var namespaces = snapshot.Namespaces.ToDictionary(item => item.Id);
        var types = snapshot.Types.ToDictionary(item => item.Id);
        var members = snapshot.Members.ToDictionary(item => item.Id);
        var methods = snapshot.Methods.ToDictionary(item => item.Id);
        var symbols = types.Keys.Concat(members.Keys).Concat(methods.Values.Select(item => item.Symbol)).ToHashSet();

        foreach (var project in snapshot.Projects)
        {
            if (project.Profile != snapshot.Profile.Id
                || StableIdentity.CreateProjectId(snapshot.Profile.Id, project.RepositoryRelativePath) != project.Id
                || project.ProjectReferences.Any(reference => !projects.ContainsKey(reference))
                || !IsSha256(project.BuildFingerprint))
            {
                return $"Project '{project.Id.Value}' has invalid identity, ownership, references, or fingerprint.";
            }
        }

        foreach (var document in snapshot.Documents)
        {
            if (!projects.ContainsKey(document.Project)
                || !IsSha256(document.ContentFingerprint)
                || (document.SemanticFingerprint is not null && !IsSha256(document.SemanticFingerprint)))
            {
                return $"Document '{document.Id.Value}' has invalid ownership or fingerprints.";
            }
        }

        foreach (var item in snapshot.Namespaces)
        {
            if (!projects.ContainsKey(item.Project))
            {
                return $"Namespace '{item.Id.Value}' references an unknown project.";
            }
        }

        foreach (var type in snapshot.Types)
        {
            if (!projects.ContainsKey(type.Project)
                || !namespaces.TryGetValue(type.Namespace, out var @namespace)
                || @namespace.Project != type.Project
                || !IsSha256(type.SignatureFingerprint))
            {
                return $"Type '{type.Id.Value}' has invalid ownership or fingerprint.";
            }
        }

        foreach (var member in snapshot.Members)
        {
            if (!projects.ContainsKey(member.Project)
                || !types.TryGetValue(member.ContainingType, out var containingType)
                || containingType.Project != member.Project
                || !IsSha256(member.SignatureFingerprint))
            {
                return $"Member '{member.Id.Value}' has invalid ownership or fingerprint.";
            }
        }

        foreach (var method in snapshot.Methods)
        {
            if (!types.ContainsKey(method.ContainingType)
                || !IsSha256(method.SignatureFingerprint)
                || (method.BodyFingerprint is not null && !IsSha256(method.BodyFingerprint)))
            {
                return $"Method '{method.Id.Value}' has invalid ownership or fingerprint.";
            }
        }

        if (snapshot.Attributes.Any(item => !symbols.Contains(item.Target))
            || snapshot.References.Any(item => !projects.ContainsKey(item.Project))
            || snapshot.Invocations.Any(item => !methods.ContainsKey(item.ContainingMethod))
            || snapshot.InventoryMarkers.Any(item => !projects.ContainsKey(item.Project)
                || (item.Symbol is not null && !symbols.Contains(item.Symbol.Value))))
        {
            return "An attribute, reference, invocation, or inventory marker contains an orphaned owner.";
        }

        var evidenceError = ValidateEvidence(snapshot, documents, projects, symbols);
        if (evidenceError is not null)
        {
            return evidenceError;
        }

        if (!string.Equals(ProgramIndexFingerprint.Compute(snapshot), snapshot.IndexFingerprint, StringComparison.Ordinal))
        {
            return "The index fingerprint does not match the canonical snapshot.";
        }

        return null;
    }

    private static string? ValidateEvidence(
        ProgramIndexSnapshot snapshot,
        Dictionary<DocumentId, ProgramDocument> documents,
        Dictionary<ProjectId, ProgramProject> projects,
        HashSet<SymbolId> symbols)
    {
        var evidenceById = new Dictionary<EvidenceId, string>();
        var visiting = new HashSet<EvidenceId>();
        foreach (var evidence in EnumerateFactEvidence(snapshot))
        {
            var error = Visit(evidence);
            if (error is not null)
            {
                return error;
            }
        }

        foreach (var diagnostic in snapshot.Diagnostics)
        {
            if (diagnostic.Location.Profile is { } profile && profile != snapshot.Profile.Id
                || diagnostic.Location.Project is { } project && !projects.ContainsKey(project)
                || diagnostic.Location.Symbol is { } symbol && !symbols.Contains(symbol)
                || diagnostic.Location.SourceRange is { } range && !documents.ContainsKey(range.Document))
            {
                return $"Diagnostic '{diagnostic.Id.Value}' contains an orphaned or cross-profile location.";
            }
        }

        return null;

        string? Visit(EvidenceRef evidence)
        {
            if (evidence is null || string.IsNullOrWhiteSpace(evidence.Id.Value))
            {
                return "Evidence records require a stable ID.";
            }

            if (visiting.Contains(evidence.Id))
            {
                return $"Evidence ID '{evidence.Id.Value}' contains a cycle.";
            }

            var canonical = ProgramIndexJsonCodec.SerializeValue(evidence);
            if (evidenceById.TryGetValue(evidence.Id, out var existing))
            {
                return existing == canonical ? null : $"Evidence ID '{evidence.Id.Value}' has conflicting values.";
            }

            visiting.Add(evidence.Id);

            if (evidence.Range is { } range && !documents.ContainsKey(range.Document))
            {
                return $"Evidence ID '{evidence.Id.Value}' references an unknown document.";
            }

            evidenceById.Add(evidence.Id, canonical);
            foreach (var underlying in evidence.UnderlyingEvidence)
            {
                var error = Visit(underlying);
                if (error is not null)
                {
                    return error;
                }
            }

            visiting.Remove(evidence.Id);
            return null;
        }
    }

    private static IEnumerable<EvidenceRef> EnumerateFactEvidence(ProgramIndexSnapshot snapshot) =>
        snapshot.Projects.SelectMany(item => item.Evidence)
            .Concat(snapshot.Documents.SelectMany(item => item.Evidence))
            .Concat(snapshot.Namespaces.SelectMany(item => item.Evidence))
            .Concat(snapshot.Types.SelectMany(item => item.Evidence))
            .Concat(snapshot.Members.SelectMany(item => item.Evidence))
            .Concat(snapshot.Methods.SelectMany(item => item.Evidence))
            .Concat(snapshot.Attributes.SelectMany(item => item.Evidence))
            .Concat(snapshot.References.SelectMany(item => item.Evidence))
            .Concat(snapshot.Invocations.SelectMany(item => item.Evidence))
            .Concat(snapshot.InventoryMarkers.SelectMany(item => item.Evidence))
            .Concat(snapshot.Diagnostics.SelectMany(item => item.Evidence));

    private static bool AreInitialized(ProgramIndexSnapshot snapshot) =>
        !snapshot.Projects.IsDefault
        && !snapshot.Documents.IsDefault
        && !snapshot.Namespaces.IsDefault
        && !snapshot.Types.IsDefault
        && !snapshot.Members.IsDefault
        && !snapshot.Methods.IsDefault
        && !snapshot.Attributes.IsDefault
        && !snapshot.References.IsDefault
        && !snapshot.Invocations.IsDefault
        && !snapshot.InventoryMarkers.IsDefault
        && !snapshot.Diagnostics.IsDefault
        && snapshot.Projects.All(item => !item.ProjectReferences.IsDefault && !item.Evidence.IsDefault)
        && snapshot.Documents.All(item => !item.Evidence.IsDefault)
        && snapshot.Namespaces.All(item => !item.Evidence.IsDefault)
        && snapshot.Types.All(item => !item.Interfaces.IsDefault && !item.Evidence.IsDefault)
        && snapshot.Members.All(item => !item.Evidence.IsDefault)
        && snapshot.Methods.All(item => !item.Parameters.IsDefault && !item.Evidence.IsDefault)
        && snapshot.Attributes.All(item => !item.Arguments.IsDefault && !item.Evidence.IsDefault)
        && snapshot.References.All(item => !item.Evidence.IsDefault)
        && snapshot.Invocations.All(item => !item.Evidence.IsDefault)
        && snapshot.InventoryMarkers.All(item => !item.Evidence.IsDefault)
        && snapshot.Diagnostics.All(item => !item.Evidence.IsDefault);

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool Unique<T>(IEnumerable<T> values, Func<T, string> keySelector)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var key = keySelector(value);
            if (string.IsNullOrWhiteSpace(key) || !keys.Add(key))
            {
                return false;
            }
        }

        return true;
    }
}
