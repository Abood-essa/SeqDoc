using System.Collections.Immutable;
using System.Globalization;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Identity;

namespace SeqDoc.Rendering.Markdown;

/// <summary>
/// Opt-in options for overview/child diagram decomposition. Decomposition never changes output
/// unless explicitly enabled; the default mode stays byte-identical end to end.
/// </summary>
public sealed record DiagramDecompositionOptions(bool Enabled);

/// <summary>
/// Pure deterministic splitter for one oversized <see cref="DiagramPlan"/> (checkpoint I23). The
/// decomposer consumes typed plan facts only: no graph access, no labels, no scheduling, and no
/// timestamps influence the split. Trigger conditions: the plan's rendered Mermaid exceeds the
/// character limit AND the sequence is non-empty AND at least one top-level element is a fragment.
/// Anything else returns <c>null</c> so the caller falls back to the existing conservative
/// truncation path. Split mechanics: the overview retains the leading run of top-level message
/// elements before the first top-level fragment (root seam) plus the maximal trailing run of
/// top-level Response-kind message elements (outcomes); the middle candidate pool packs greedily in
/// chronological order into child buckets under the limit measured on the standalone rendering
/// contribution of each tentative view; an element that alone exceeds the budget becomes its own
/// bucket and is truncated internally by the caller's existing fitter. Fragment subtrees move whole
/// (arms are never separated). Every view rebuilds referenced-only participants (original order),
/// branches filtered to surviving keys (empty-key branches dropped), sequence, and debug
/// projection. Message identities, evidence, certainty, and labels are carried verbatim, and the
/// multiset union of view messages equals the original plan's messages with pairwise disjoint
/// views by construction. Original diagnostics remain only on the overview, which additionally
/// carries one conservative <c>DP-DIAGRAM-DECOMPOSED</c> diagnostic created through the existing
/// identity family (subject <c>{entryPoint}:decomposition</c>) naming the child count.
/// </summary>
internal static class DocumentationViewDecomposer
{
    /// <summary>
    /// Attempts to split an oversized plan. Returns the ordered views — the overview first, then
    /// chronologically packed children — or <c>null</c> when the plan fits, lacks splittable
    /// structure, or carries a legacy topology-empty sequence (caller falls back to truncation).
    /// </summary>
    public static ImmutableArray<DiagramPlan>? TryDecompose(DiagramPlan plan, int maxMermaidCharacters)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (maxMermaidCharacters < 15) { return null; }
        if (plan.Sequence.Elements.IsEmpty) { return null; }

        int firstFragmentIndex = -1;
        for (int index = 0; index < plan.Sequence.Elements.Length; index++)
        {
            if (plan.Sequence.Elements[index].IsFragment)
            {
                firstFragmentIndex = index;
                break;
            }
        }

        if (firstFragmentIndex < 0) { return null; }

        // Maximal trailing run of top-level Response-kind message elements; it never reaches the
        // first fragment, so the seam, the fragment, and everything between stay in the pool.
        int trailingStart = plan.Sequence.Elements.Length;
        for (int index = plan.Sequence.Elements.Length - 1; index > firstFragmentIndex; index--)
        {
            var element = plan.Sequence.Elements[index];
            if (!element.IsMessageRef)
            {
                break;
            }

            var message = FindMessage(plan, element.MessageRefId!.Value);
            if (message.Kind != DiagramMessageKind.Response)
            {
                break;
            }

            trailingStart = index;
        }

        var pool = plan.Sequence.Elements
            .Slice(firstFragmentIndex, trailingStart - firstFragmentIndex)
            .ToBuilder();

        // Greedy chronological packing: grow the current bucket while its standalone rendering
        // contribution fits; otherwise close the bucket. An element that alone exceeds the limit
        // becomes its own bucket (the caller's fitter truncates it internally afterwards).
        var buckets = new List<ImmutableArray<DiagramSequenceElement>>();
        var current = new List<DiagramSequenceElement>();
        foreach (var element in pool)
        {
            current.Add(element);
            if (current.Count == 1
                || MeasureView(plan, current) <= maxMermaidCharacters)
            {
                continue;
            }

            var last = current[^1];
            current.RemoveAt(current.Count - 1);
            buckets.Add(current.ToImmutableArray());
            current = [last];
        }

        if (current.Count > 0)
        {
            buckets.Add(current.ToImmutableArray());
        }

        var views = ImmutableArray.CreateBuilder<DiagramPlan>();
        var overviewElements = plan.Sequence.Elements
            .RemoveRange(firstFragmentIndex, trailingStart - firstFragmentIndex);
        views.Add(BuildView(plan, overviewElements, plan.Diagnostics.Add(CreateDecomposedDiagnostic(plan, buckets.Count))));
        foreach (var bucket in buckets)
        {
            views.Add(BuildView(plan, bucket, []));
        }

        return views.ToImmutable();
    }

    /// <summary>The conservative decomposition diagnostic, stable across reruns.</summary>
    private static DiagramPlanDiagnostic CreateDecomposedDiagnostic(DiagramPlan plan, int childCount)
        => new(
            StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                "DP-DIAGRAM-DECOMPOSED",
                AnalysisStage.CommandLine,
                plan.Profile,
                $"{plan.EntryPoint.Value}:decomposition",
                0)),
            "DP-DIAGRAM-DECOMPOSED",
            "The diagram was decomposed into an overview and chronological child documents.",
            $"child documents={childCount.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Renders a tentative standalone view to measure its character contribution.</summary>
    private static int MeasureView(DiagramPlan plan, List<DiagramSequenceElement> elements)
        => MermaidRenderer.Render(BuildView(plan, elements.ToImmutableArray(), [])).Length;

    /// <summary>
    /// Builds one self-contained view plan: referenced-only participants in original order, the
    /// closed message set referenced through the sequence tree (satisfying exact sequence coverage),
    /// branches filtered to surviving keys with empties dropped, and a rebuilt debug projection.
    /// </summary>
    private static DiagramPlan BuildView(
        DiagramPlan original,
        ImmutableArray<DiagramSequenceElement> elements,
        ImmutableArray<DiagramPlanDiagnostic> diagnostics)
    {
        var referenced = new HashSet<DiagramPlanElementId>();
        foreach (var element in elements)
        {
            CollectReferencedIds(element, referenced);
        }

        var messages = original.Messages
            .Where(message => referenced.Contains(message.Id))
            .ToImmutableArray();
        var participants = original.Participants
            .Where(participant => messages.Any(message => message.Source == participant.Key || message.Target == participant.Key))
            .ToImmutableArray();
        var messageKeys = messages.Select(message => message.Key).ToHashSet(StringComparer.Ordinal);
        var branches = original.Branches
            .Select(branch => (Branch: branch, Keys: branch.MessageKeys.Where(messageKeys.Contains).ToImmutableArray()))
            .Where(item => !item.Keys.IsDefaultOrEmpty)
            .Select(item => new DiagramBranch(item.Branch.Id, item.Branch.Key, item.Branch.Label, item.Branch.Kind,
                item.Keys, item.Branch.Evidence, item.Branch.Certainty))
            .ToImmutableArray();
        return new DiagramPlan(
            original.EntryPoint,
            original.Profile,
            original.OperationKey,
            participants,
            messages,
            branches,
            DocumentationSetBuilder.DebugProjection(participants, messages, new DiagramSequence(elements), branches, diagnostics),
            new DiagramSequence(elements),
            diagnostics);
    }

    /// <summary>Closes message references through nested fragments and alt arms recursively.</summary>
    private static void CollectReferencedIds(DiagramSequenceElement element, HashSet<DiagramPlanElementId> referenced)
    {
        if (element.IsMessageRef)
        {
            referenced.Add(element.MessageRefId!.Value);
            return;
        }

        CollectFragmentIds(element.NestedFragment!, referenced);
    }

    private static void CollectFragmentIds(DiagramFragment fragment, HashSet<DiagramPlanElementId> referenced)
    {
        foreach (var reference in fragment.MessageRefs)
        {
            referenced.Add(reference);
        }

        foreach (var nested in fragment.Fragments)
        {
            CollectFragmentIds(nested, referenced);
        }

        foreach (var arm in fragment.Arms)
        {
            foreach (var reference in arm.MessageRefs)
            {
                referenced.Add(reference);
            }

            foreach (var nested in arm.Fragments)
            {
                CollectFragmentIds(nested, referenced);
            }
        }
    }

    /// <summary>Fails closed on an unresolved reference; the renderer rejects it anyway.</summary>
    private static DiagramMessage FindMessage(DiagramPlan plan, DiagramPlanElementId id)
    {
        foreach (var message in plan.Messages)
        {
            if (message.Id == id)
            {
                return message;
            }
        }

        throw new InvalidOperationException(
            $"A sequence element references diagram message '{id.Value}' that is not planned; decomposition fails closed.");
    }
}
