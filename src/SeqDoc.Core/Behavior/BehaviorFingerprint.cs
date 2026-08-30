using System.Collections.Immutable;
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
        payload = payload with
        {
            NaturalLoops = NormalizeLoops(body.NaturalLoops),
            LoopAnchors = NormalizeAnchors(body.LoopAnchors),
            OrdinaryBranches = NormalizeBranches(body.OrdinaryBranches),
        };
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
        System.Collections.Immutable.ImmutableArray<ExtractedExceptionRegion> Regions,
        System.Collections.Immutable.ImmutableArray<ExtractedNaturalLoop> NaturalLoops = default,
        System.Collections.Immutable.ImmutableArray<ExtractedLoopAnchor> LoopAnchors = default,
        System.Collections.Immutable.ImmutableArray<ExtractedOrdinaryBranch> OrdinaryBranches = default);

    private static System.Collections.Immutable.ImmutableArray<ExtractedNaturalLoop> NormalizeLoops(
        System.Collections.Immutable.ImmutableArray<ExtractedNaturalLoop> loops) =>
        (loops.IsDefault ? [] : loops)
            .Select(loop => loop with
            {
                BodyBlockOrdinals = (loop.BodyBlockOrdinals.IsDefault ? [] : loop.BodyBlockOrdinals).Order().ToImmutableArray(),
                LatchBlockOrdinals = (loop.LatchBlockOrdinals.IsDefault ? [] : loop.LatchBlockOrdinals).Order().ToImmutableArray(),
                ExitBlockOrdinals = (loop.ExitBlockOrdinals.IsDefault ? [] : loop.ExitBlockOrdinals).Order().ToImmutableArray(),
                BackEdges = (loop.BackEdges.IsDefault ? [] : loop.BackEdges)
                    .Select(edge => edge with
                    {
                        EnteringRegions = (edge.EnteringRegions.IsDefault ? [] : edge.EnteringRegions).OrderBy(item => item.Value, StringComparer.Ordinal).ToImmutableArray(),
                        LeavingRegions = (edge.LeavingRegions.IsDefault ? [] : edge.LeavingRegions).OrderBy(item => item.Value, StringComparer.Ordinal).ToImmutableArray(),
                        Evidence = (edge.Evidence.IsDefault ? [] : edge.Evidence).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
                    })
                    .OrderBy(edge => edge.SourceBlockOrdinal).ThenBy(edge => edge.DestinationBlockOrdinal).ToImmutableArray(),
                Evidence = (loop.Evidence.IsDefault ? [] : loop.Evidence).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            })
            .OrderBy(loop => loop.HeaderBlockOrdinal)
            .ThenBy(loop => loop.LoopOperation.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    private static System.Collections.Immutable.ImmutableArray<ExtractedLoopAnchor> NormalizeAnchors(
        System.Collections.Immutable.ImmutableArray<ExtractedLoopAnchor> anchors) =>
        (anchors.IsDefault ? [] : anchors)
            .Select(anchor => anchor with { Evidence = (anchor.Evidence.IsDefault ? [] : anchor.Evidence).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray() })
            .OrderBy(anchor => anchor.Operation.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    private static System.Collections.Immutable.ImmutableArray<ExtractedOrdinaryBranch> NormalizeBranches(
        System.Collections.Immutable.ImmutableArray<ExtractedOrdinaryBranch> branches) =>
        (branches.IsDefault ? [] : branches)
            .Select(branch => branch with
            {
                EnteringRegions = (branch.EnteringRegions.IsDefault ? [] : branch.EnteringRegions).OrderBy(item => item.Value, StringComparer.Ordinal).ToImmutableArray(),
                LeavingRegions = (branch.LeavingRegions.IsDefault ? [] : branch.LeavingRegions).OrderBy(item => item.Value, StringComparer.Ordinal).ToImmutableArray(),
                Evidence = (branch.Evidence.IsDefault ? [] : branch.Evidence).OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            })
            .OrderBy(branch => branch.SourceBlockOrdinal)
            .ThenBy(branch => branch.DestinationBlockOrdinal)
            .ToImmutableArray();
}
