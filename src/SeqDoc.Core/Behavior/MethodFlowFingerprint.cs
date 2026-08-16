using System.Security.Cryptography;
using System.Text.Json;

namespace SeqDoc.Core.Behavior;

/// <summary>
/// Computes a full-content fingerprint over one normalized method flow so that any change to a node,
/// edge, region, outcome, value fact, control dependence, or summary fact changes the fingerprint.
/// </summary>
public static class MethodFlowFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public static string Compute(MethodFlowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot with { FlowFingerprint = string.Empty }, SerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
