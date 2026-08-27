using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Wording;

namespace SeqDoc.Rendering.Markdown;

/// <summary>One planned documentation entry with its deterministic output file base name.</summary>
public sealed record DocumentSetEntry(string FileName, WordingDocument Wording, DiagramPlan Diagram);

/// <summary>Reports the in-memory documentation set build result.</summary>
public sealed record DocumentationSetBuildResult(
    bool Succeeded,
    ImmutableArray<RenderedOutputFile> Files,
    ImmutableArray<string> Errors)
{
    /// <summary>All diagnostics retained by the final plans used for this build, including truncation diagnostics.</summary>
    public ImmutableArray<DiagramPlanDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Builds the complete in-memory documentation set (per-Get Markdown, Mermaid, and the profile
/// index) before any output-root activation. The builder validates every Mermaid diagram
/// structurally and returns a failure with explicit errors instead of emitting invalid output;
/// nothing touches the filesystem in this step.
/// </summary>
public static class DocumentationSetBuilder
{
    public static DocumentationSetBuildResult Build(
        string profileId,
        string programIndexFingerprint,
        IReadOnlyList<DocumentSetEntry> documents,
        DiagramBudget? diagramBudget = null,
        DiagramDecompositionOptions? decomposition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint);
        ArgumentNullException.ThrowIfNull(documents);

        var files = new List<RenderedOutputFile>();
        var indexEntries = new List<(string OperationKey, string FileName)>();
        var childDocumentsByFile = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var errors = new List<string>();
        var diagnostics = new List<DiagramPlanDiagnostic>();
        foreach (var document in documents.OrderBy(item => item.FileName, StringComparer.Ordinal))
        {
            // Decomposition is opt-in only: without the flag (or when the plan fits its Mermaid
            // budget or lacks splittable structure) the legacy path below runs byte-identically.
            if (decomposition is { Enabled: true }
                && diagramBudget is not null
                && MermaidRenderer.Render(document.Diagram).Length > diagramBudget.MaxMermaidCharacters
                && DocumentationViewDecomposer.TryDecompose(document.Diagram, diagramBudget.MaxMermaidCharacters) is { } views)
            {
                AppendDecomposedDocument(
                    document,
                    views,
                    diagramBudget.MaxMermaidCharacters,
                    files,
                    indexEntries,
                    childDocumentsByFile,
                    errors,
                    diagnostics);
                continue;
            }

            DiagramPlan diagram = diagramBudget is null
                ? document.Diagram
                : FitMermaid(document.Diagram, diagramBudget.MaxMermaidCharacters);
            if (diagramBudget is not null && diagramBudget.MaxMermaidCharacters < 15)
            {
                errors.Add($"document {document.FileName}: MaxMermaidCharacters must be at least 15 for valid Mermaid output.");
                continue;
            }
            diagnostics.AddRange(diagram.Diagnostics);
            string markdown = MarkdownRenderer.RenderDocument(document.Wording, diagram);
            string mermaid = MermaidRenderer.Render(diagram);
            ImmutableArray<string> validationErrors = MermaidValidator.Validate(mermaid);
            if (validationErrors.Length > 0)
            {
                errors.Add($"document {document.FileName}: {string.Join("; ", validationErrors)}");
                continue;
            }

            string markdownName = $"{document.FileName}.md";
            string mermaidName = $"{document.FileName}.mmd";
            files.Add(new RenderedOutputFile(markdownName, Encoding.UTF8.GetBytes(markdown)));
            files.Add(new RenderedOutputFile(mermaidName, Encoding.UTF8.GetBytes(mermaid)));
            indexEntries.Add((document.Wording.OperationKey, markdownName));
        }

        if (errors.Count > 0)
        {
            return new DocumentationSetBuildResult(false, [], errors.ToImmutableArray()) { Diagnostics = diagnostics.ToImmutableArray() };
        }

        string index = MarkdownRenderer.RenderIndex(
            profileId,
            programIndexFingerprint,
            indexEntries,
            childDocumentsByFile.Count == 0 ? null : childDocumentsByFile);
        files.Add(new RenderedOutputFile("index.md", Encoding.UTF8.GetBytes(index)));
        return new DocumentationSetBuildResult(
            true,
            files.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToImmutableArray(),
            [])
        { Diagnostics = diagnostics.ToImmutableArray() };
    }

    /// <summary>
    /// Renders one decomposed document: the overview first (keeping the base file names so index
    /// links stay stable), then chronological children named {base}.part-{NNN}. Every view passes
    /// through the existing character fitter as a safety net and its own structural validation.
    /// Behavior text, original diagnostics, and the decomposition diagnostic live only on the
    /// overview; children carry a title/part/parent-link Markdown body with no behavior or
    /// diagnostics sections. Navigation lives in Markdown only, never inside Mermaid fences.
    /// </summary>
    private static void AppendDecomposedDocument(
        DocumentSetEntry document,
        ImmutableArray<DiagramPlan> views,
        int maxMermaidCharacters,
        List<RenderedOutputFile> files,
        List<(string OperationKey, string FileName)> indexEntries,
        Dictionary<string, IReadOnlyList<string>> childDocumentsByFile,
        List<string> errors,
        List<DiagramPlanDiagnostic> diagnostics)
    {
        var fittedViews = new List<DiagramPlan>();
        var mermaidTexts = new List<string>();
        for (int index = 0; index < views.Length; index++)
        {
            DiagramPlan fitted = FitMermaid(views[index], maxMermaidCharacters, preserveTopLevelFragments: true);
            diagnostics.AddRange(fitted.Diagnostics);
            string mermaid = MermaidRenderer.Render(fitted);
            ImmutableArray<string> validationErrors = MermaidValidator.Validate(mermaid);
            if (validationErrors.Length > 0)
            {
                errors.Add($"document {document.FileName}: {string.Join("; ", validationErrors)}");
                return;
            }

            fittedViews.Add(fitted);
            mermaidTexts.Add(mermaid);
        }

        int partCount = views.Length - 1;
        var continuationNames = new List<string>();
        for (int index = 1; index <= partCount; index++)
        {
            continuationNames.Add($"{document.FileName}.part-{index.ToString("000", CultureInfo.InvariantCulture)}");
        }

        for (int index = 0; index < fittedViews.Count; index++)
        {
            bool overview = index == 0;
            string ordinal = overview ? string.Empty : $".part-{index.ToString("000", CultureInfo.InvariantCulture)}";
            string markdownName = $"{document.FileName}{ordinal}.md";
            string mermaidName = $"{document.FileName}{ordinal}.mmd";
            string markdown = overview
                ? MarkdownRenderer.RenderDocument(document.Wording, fittedViews[index], continuationNames)
                : MarkdownRenderer.RenderDecomposedPart(
                    document.Wording.Title,
                    document.FileName,
                    index,
                    partCount,
                    fittedViews[index]);
            files.Add(new RenderedOutputFile(markdownName, Encoding.UTF8.GetBytes(markdown)));
            files.Add(new RenderedOutputFile(mermaidName, Encoding.UTF8.GetBytes(mermaidTexts[index])));
        }

        indexEntries.Add((document.Wording.OperationKey, $"{document.FileName}.md"));
        childDocumentsByFile[$"{document.FileName}.md"] = continuationNames.ToImmutableArray();
    }

    private static DiagramPlan FitMermaid(
        DiagramPlan original,
        int limit,
        bool preserveTopLevelFragments = false)
    {
        if (MermaidRenderer.Render(original).Length <= limit) { return original; }
        for (int count = original.Messages.Length - 1; count >= 0; count--)
        {
            var kept = original.Messages.Take(count).ToImmutableArray();
            var ids = kept.Select(message => message.Id).ToHashSet();
            var sequence = TrimSequence(original.Sequence, ids, preserveTopLevelFragments);
            if (!original.Sequence.Elements.IsEmpty && sequence.Elements.IsEmpty) { continue; }
            if (!original.Sequence.Elements.IsEmpty)
            {
                kept = kept.Where(message => SequenceContains(sequence, message.Id)).ToImmutableArray();
            }
            var participants = original.Participants
                .Where(participant => kept.Any(message => message.Source == participant.Key || message.Target == participant.Key))
                .ToImmutableArray();
            var keptKeys = kept.Select(message => message.Key).ToHashSet(StringComparer.Ordinal);
            var branches = original.Branches
                .Where(branch => branch.MessageKeys.Any(keptKeys.Contains))
                .Select(branch => new DiagramBranch(branch.Id, branch.Key, branch.Label, branch.Kind,
                    branch.MessageKeys.Where(keptKeys.Contains).ToImmutableArray(), branch.Evidence, branch.Certainty))
                .ToImmutableArray();
            var diagnostics = original.Diagnostics.Add(new DiagramPlanDiagnostic(
                    StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                        "DP-MERMAID-TRUNCATED", AnalysisStage.CommandLine, original.Profile,
                        $"{original.EntryPoint.Value}:mermaid", 0)),
                    "DP-MERMAID-TRUNCATED", "The diagram was truncated to its Mermaid character budget.",
                    $"maximum characters={limit}"));
            var candidate = new DiagramPlan(original.EntryPoint, original.Profile, original.OperationKey,
                participants, kept, branches, DebugProjection(participants, kept, sequence, branches, diagnostics), sequence,
                diagnostics);
            if (MermaidRenderer.Render(candidate).Length <= limit) { return candidate; }
        }
        var emptyDiagnostics = original.Diagnostics.Add(new DiagramPlanDiagnostic(
                StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                    "DP-MERMAID-TRUNCATED", AnalysisStage.CommandLine, original.Profile,
                    $"{original.EntryPoint.Value}:mermaid", 0)),
                "DP-MERMAID-TRUNCATED", "The diagram was truncated to its Mermaid character budget.",
                $"maximum characters={limit}"));
        return new DiagramPlan(original.EntryPoint, original.Profile, original.OperationKey, [], [], [],
            DebugProjection([], [], DiagramSequence.Empty, [], emptyDiagnostics), DiagramSequence.Empty,
            emptyDiagnostics);
    }

    private static DiagramSequence TrimSequence(
        DiagramSequence sequence,
        HashSet<DiagramPlanElementId> ids,
        bool preserveTopLevelFragments = false)
        => new(sequence.Elements.Select(element => element.IsMessageRef
            ? ids.Contains(element.MessageRefId!.Value) ? element : null
            : preserveTopLevelFragments
                ? ContainsAllFragmentMessages(element.NestedFragment!, ids) ? element : null
                : TrimFragment(element.NestedFragment!, ids)).Where(item => item is not null).Select(item => item!).ToImmutableArray());

    private static bool ContainsAllFragmentMessages(DiagramFragment fragment, HashSet<DiagramPlanElementId> ids)
        => fragment.MessageRefs.All(ids.Contains)
            && fragment.Fragments.All(nested => ContainsAllFragmentMessages(nested, ids))
            && fragment.Arms.All(arm => arm.MessageRefs.All(ids.Contains)
                && arm.Fragments.All(nested => ContainsAllFragmentMessages(nested, ids)));

    private static DiagramSequenceElement? TrimFragment(DiagramFragment fragment, HashSet<DiagramPlanElementId> ids)
    {
        var messages = fragment.MessageRefs.Where(ids.Contains).ToImmutableArray();
        var nested = fragment.Fragments.Select(item => TrimFragment(item, ids)).Where(item => item is not null).Select(item => item!.NestedFragment!).ToImmutableArray();
        var arms = fragment.Arms.Select(arm => new DiagramAltArm(arm.Id, arm.Key, arm.Label, arm.IsElse,
                arm.MessageRefs.Where(ids.Contains).ToImmutableArray(), arm.Fragments.Select(item => TrimFragment(item, ids))
                    .Where(item => item is not null).Select(item => item!.NestedFragment!).ToImmutableArray(), arm.Evidence, arm.Certainty))
            .Where(arm => arm.MessageRefs.Length > 0 || arm.Fragments.Length > 0).ToImmutableArray();
        if (messages.Length == 0 && nested.Length == 0 && arms.Length == 0) { return null; }
        if (arms.Length == 1)
        {
            var arm = arms[0];
            return DiagramSequenceElement.Fragment(new DiagramFragment(fragment.Id, fragment.Key, fragment.Label,
                DiagramFragmentKind.Opt, [], arm.MessageRefs, arm.Fragments, fragment.Evidence, fragment.Certainty));
        }
        return DiagramSequenceElement.Fragment(new DiagramFragment(fragment.Id, fragment.Key, fragment.Label, fragment.Kind,
            arms, messages, nested, fragment.Evidence, fragment.Certainty));
    }

    private static bool SequenceContains(DiagramSequence sequence, DiagramPlanElementId id)
        => sequence.Elements.Any(element => element.IsMessageRef ? element.MessageRefId == id : FragmentContains(element.NestedFragment!, id));

    private static bool FragmentContains(DiagramFragment fragment, DiagramPlanElementId id)
        => fragment.MessageRefs.Contains(id) || fragment.Arms.Any(arm => arm.MessageRefs.Contains(id)
                || arm.Fragments.Any(item => FragmentContains(item, id)))
            || fragment.Fragments.Any(item => FragmentContains(item, id));

    /// <summary>Canonical newline-only debug projection shared with the view decomposer.</summary>
    internal static string DebugProjection(IEnumerable<DiagramParticipant> participants,
        IEnumerable<DiagramMessage> messages, DiagramSequence sequence, IEnumerable<DiagramBranch> branches,
        IEnumerable<DiagramPlanDiagnostic> diagnostics)
    {
        var builder = new StringBuilder();
        foreach (var participant in participants)
        {
            builder.Append("participant ").Append(participant.Id.Value).Append(" key=").Append(participant.Key)
                .Append(" kind=").Append(participant.Kind).Append(" certainty=").Append(participant.Certainty)
                .Append(" label=").Append(participant.Label).Append(" canonical=").Append(participant.Evidence[0].Id.Value).Append('\n');
        }
        foreach (var message in messages)
        {
            builder.Append("message ").Append(message.Id.Value).Append(" key=").Append(message.Key)
                .Append(" source=").Append(message.Source).Append(" target=").Append(message.Target)
                .Append(" kind=").Append(message.Kind).Append(" certainty=").Append(message.Certainty)
                .Append(" label=").Append(message.Label).Append('\n');
        }
        AppendSequence(builder, sequence);
        foreach (var branch in branches)
        {
            builder.Append("branch ").Append(branch.Id.Value).Append(" key=").Append(branch.Key)
                .Append(" kind=").Append(branch.Kind).Append(" certainty=").Append(branch.Certainty)
                .Append(" label=").Append(branch.Label).Append(" messages=").Append(string.Join(',', branch.MessageKeys)).Append('\n');
        }
        foreach (var diagnostic in diagnostics)
        {
            builder.Append("diagnostic ").Append(diagnostic.Id.Value).Append(" code=").Append(diagnostic.Code)
                .Append(" summary=").Append(diagnostic.Summary).Append(" detail=").Append(diagnostic.Detail).Append('\n');
        }
        return builder.ToString().TrimEnd('\n');
    }

    private static void AppendSequence(StringBuilder builder, DiagramSequence sequence)
    {
        for (int position = 0; position < sequence.Elements.Length; position++)
        {
            var element = sequence.Elements[position];
            if (element.IsMessageRef)
            {
                builder.Append("element ").Append(element.MessageRefId!.Value).Append(" kind=message position=").Append(position).Append('\n');
            }
            else
            {
                builder.Append("element ").Append(element.NestedFragment!.Id.Value).Append(" kind=fragment position=").Append(position).Append('\n');
                AppendFragment(builder, element.NestedFragment!);
            }
        }
    }

    private static void AppendFragment(StringBuilder builder, DiagramFragment fragment)
    {
        builder.Append("fragment ").Append(fragment.Id.Value).Append(" key=").Append(fragment.Key)
            .Append(" kind=").Append(fragment.Kind).Append(" certainty=").Append(fragment.Certainty)
            .Append(" label=").Append(fragment.Label).Append('\n');
        foreach (var arm in fragment.Arms)
        {
            builder.Append("arm ").Append(arm.Id.Value).Append(" key=").Append(arm.Key)
                .Append(" isElse=").Append(arm.IsElse).Append(" certainty=").Append(arm.Certainty)
                .Append(" label=").Append(arm.Label).Append('\n');
            foreach (var nested in arm.Fragments)
            {
                AppendFragment(builder, nested);
            }
        }
        foreach (var nested in fragment.Fragments)
        {
            AppendFragment(builder, nested);
        }
    }
}
