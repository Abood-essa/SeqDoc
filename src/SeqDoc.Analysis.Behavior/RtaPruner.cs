using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Analysis.Behavior;

/// <summary>
/// Provides a Rapid Type Analysis (RTA) foundation. Production Pass B results must not use RTA to
/// remove CHA candidates; this pruner requires an explicit root set and a documented completeness
/// policy and is intended for algorithm verification with synthetic roots.
/// </summary>
public static class RtaPruner
{
    /// <summary>Prunes CHA candidates to those whose containing type is reachable from the explicit roots.</summary>
    public static ImmutableArray<MethodId> Prune(
        ProgramIndexSnapshot index,
        ImmutableArray<MethodId> chaCandidates,
        ImmutableArray<SymbolId> explicitRootTypes,
        bool assumeClosedWorld)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (explicitRootTypes.IsEmpty)
        {
            throw new ArgumentException("RTA pruning requires an explicit root set.", nameof(explicitRootTypes));
        }

        if (!assumeClosedWorld)
        {
            return chaCandidates
                .OrderBy(methodId => methodId.Value, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        var reachable = CollectReachableTypes(index, explicitRootTypes);
        return chaCandidates
            .Where(methodId =>
            {
                var method = index.Methods.First(candidate => candidate.Id == methodId);
                return reachable.Contains(method.ContainingType);
            })
            .OrderBy(methodId => methodId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static HashSet<SymbolId> CollectReachableTypes(
        ProgramIndexSnapshot index,
        ImmutableArray<SymbolId> roots)
    {
        var typesById = index.Types.ToDictionary(type => type.Id);
        var reachable = new HashSet<SymbolId>(roots);
        var pending = new Stack<SymbolId>(roots);
        while (pending.TryPop(out var current))
        {
            if (!typesById.TryGetValue(current, out var type))
            {
                continue;
            }

            if (type.BaseType is { } baseType && reachable.Add(baseType))
            {
                pending.Push(baseType);
            }

            foreach (var iface in type.Interfaces)
            {
                if (reachable.Add(iface))
                {
                    pending.Push(iface);
                }
            }
        }

        return reachable;
    }
}
