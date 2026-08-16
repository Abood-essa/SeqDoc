using System.Security.Cryptography;
using System.Text.Json;

namespace SeqDoc.Core.Behavior;

/// <summary>Computes deterministic full-content fingerprints over extracted behavior inputs and method bodies.</summary>
public static class BehaviorFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public static string ComputeInput(ExtractedBehaviorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var payload = new InputPayload(
            input.Profile.CanonicalJson,
            input.ProgramIndexFingerprint,
            input.Methods,
            input.TypeHierarchy,
            input.Instantiations,
            input.InterfaceImplementations,
            input.MethodOverrides);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string ComputeBody(ExtractedMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var payload = new BodyPayload(
            body.Method.Value,
            body.Parameters,
            body.Locals,
            body.Operations,
            body.Blocks,
            body.Regions);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Produces a full-content fingerprint over the complete behavior snapshot so any change to a
    /// flow, call site, call edge, instantiation fact, or diagnostic changes the fingerprint. The
    /// fingerprint field itself is excluded so recomputation over a reconstructed snapshot matches.
    /// </summary>
    public static string Compute(BehaviorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot with { BehaviorFingerprint = string.Empty }, SerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed record InputPayload(
        string ProfileCanonicalJson,
        string ProgramIndexFingerprint,
        System.Collections.Immutable.ImmutableArray<ExtractedMethodBody> Methods,
        ExtractedTypeHierarchy TypeHierarchy,
        System.Collections.Immutable.ImmutableArray<TypeInstantiationFact> Instantiations,
        System.Collections.Immutable.ImmutableArray<InterfaceImplementationFact> InterfaceImplementations,
        System.Collections.Immutable.ImmutableArray<MethodOverrideFact> MethodOverrides);

    private sealed record BodyPayload(
        string Method,
        System.Collections.Immutable.ImmutableArray<ExtractedParameter> Parameters,
        System.Collections.Immutable.ImmutableArray<ExtractedLocal> Locals,
        System.Collections.Immutable.ImmutableArray<ExtractedOperation> Operations,
        System.Collections.Immutable.ImmutableArray<ExtractedBasicBlock> Blocks,
        System.Collections.Immutable.ImmutableArray<ExtractedExceptionRegion> Regions);
}
