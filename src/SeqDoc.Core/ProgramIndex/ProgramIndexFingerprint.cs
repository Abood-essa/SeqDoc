using System.Security.Cryptography;
using System.Text.Json;

namespace SeqDoc.Core.ProgramIndex;

public static class ProgramIndexFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public static string Compute(ProgramIndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var payload = new FingerprintPayload(
            snapshot.SchemaVersion,
            snapshot.ProducerVersion,
            snapshot.Profile.CanonicalJson,
            snapshot.Projects,
            snapshot.Documents,
            snapshot.Namespaces,
            snapshot.Types,
            snapshot.Members,
            snapshot.Methods,
            snapshot.Attributes,
            snapshot.References,
            snapshot.Invocations,
            snapshot.InventoryMarkers,
            snapshot.InputManifestHash);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed record FingerprintPayload(
        int SchemaVersion,
        string ProducerVersion,
        string ProfileCanonicalJson,
        System.Collections.Immutable.ImmutableArray<ProgramProject> Projects,
        System.Collections.Immutable.ImmutableArray<ProgramDocument> Documents,
        System.Collections.Immutable.ImmutableArray<ProgramNamespace> Namespaces,
        System.Collections.Immutable.ImmutableArray<ProgramType> Types,
        System.Collections.Immutable.ImmutableArray<ProgramMember> Members,
        System.Collections.Immutable.ImmutableArray<ProgramMethod> Methods,
        System.Collections.Immutable.ImmutableArray<ProgramAttributeApplication> Attributes,
        System.Collections.Immutable.ImmutableArray<ProgramReference> References,
        System.Collections.Immutable.ImmutableArray<ProgramInvocation> Invocations,
        System.Collections.Immutable.ImmutableArray<ProgramInventoryMarker> InventoryMarkers,
        string InputManifestHash);
}
