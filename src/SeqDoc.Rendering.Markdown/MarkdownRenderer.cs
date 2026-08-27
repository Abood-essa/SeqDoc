using System.Globalization;
using System.Text;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Wording;

namespace SeqDoc.Rendering.Markdown;

/// <summary>
/// Serializes a wording document and its diagram plan into Markdown with an embedded, structurally
/// validated Mermaid sequence diagram. The renderer performs no semantic inference: it orders and
/// formats the plan's phrases, retains the evidence and certainty every phrase carries, and always
/// emits canonical newlines.
/// </summary>
public static class MarkdownRenderer
{
    public static string RenderDocument(WordingDocument wording, DiagramPlan diagram)
        => RenderDocument(wording, diagram, continuationDocuments: null);

    /// <summary>
    /// Additive decomposition overload: identical output to the two-argument overload plus one
    /// trailing Continuations section linking the child part documents when any exist. Links live
    /// in Markdown only; the Mermaid fence is untouched.
    /// </summary>
    public static string RenderDocument(
        WordingDocument wording,
        DiagramPlan diagram,
        IReadOnlyList<string>? continuationDocuments)
    {
        ArgumentNullException.ThrowIfNull(wording);
        ArgumentNullException.ThrowIfNull(diagram);

        var builder = new StringBuilder();
        builder.Append("# ").Append(wording.Title).Append('\n').Append('\n');
        builder
            .Append("SeqDoc generated this documentation from compiler evidence. ")
            .Append("Every statement retains supporting evidence and explicit certainty.")
            .Append('\n')
            .Append('\n');
        builder.Append("## Sequence diagram").Append('\n').Append('\n');
        builder.Append("```mermaid").Append('\n');
        builder.Append(MermaidRenderer.Render(diagram)).Append('\n');
        builder.Append("```").Append('\n');
        builder.Append("## Behavior").Append('\n').Append('\n');
        foreach (var phrase in wording.Phrases)
        {
            if (phrase.Kind == WordingPhraseKind.TechnicalFallback)
            {
                continue;
            }

            AppendPhrase(builder, phrase);
        }

        var fallbacks = wording.Phrases
            .Where(item => item.Kind == WordingPhraseKind.TechnicalFallback)
            .ToArray();
        if (fallbacks.Length > 0)
        {
            builder.Append('\n').Append("## Technical fallback").Append('\n').Append('\n');
            foreach (var phrase in fallbacks)
            {
                AppendPhrase(builder, phrase);
            }
        }

        if (diagram.Diagnostics.Length > 0)
        {
            builder.Append('\n').Append("## Diagram diagnostics").Append('\n').Append('\n');
            foreach (var diagnostic in diagram.Diagnostics.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                builder.Append("- ").Append(diagnostic.Summary)
                    .Append(" _(code: ").Append(diagnostic.Code)
                    .Append("; detail: ").Append(diagnostic.Detail).Append(")_").Append('\n');
            }
        }

        if (continuationDocuments is { Count: > 0 })
        {
            builder.Append('\n').Append("## Continuations").Append('\n').Append('\n');
            builder
                .Append("This diagram exceeded the configured Mermaid character budget, so its middle ")
                .Append("chronological segments continue in the child parts below; every message keeps ")
                .Append("its original evidence and certainty.")
                .Append('\n')
                .Append('\n');
            foreach (string name in continuationDocuments)
            {
                builder.Append("- [").Append(name).Append("](").Append(name).Append(".md)").Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Dedicated renderer path for one decomposed child part: title, part k-of-N line, parent link,
    /// and the Mermaid fence. No Behavior or diagnostics sections, so behavior text and diagnostics
    /// are never duplicated into children. Navigation stays in Markdown, never inside the fence.
    /// </summary>
    public static string RenderDecomposedPart(
        string title,
        string parentFileName,
        int partNumber,
        int partCount,
        DiagramPlan diagram)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentFileName);
        ArgumentNullException.ThrowIfNull(diagram);
        if (partNumber < 1 || partCount < 1 || partNumber > partCount)
        {
            throw new ArgumentOutOfRangeException(nameof(partNumber), "Part numbering must satisfy 1 <= part <= count.");
        }

        string ordinal = partNumber.ToString("000", CultureInfo.InvariantCulture);
        string total = partCount.ToString("000", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append("# ").Append(title).Append(" (part ").Append(ordinal).Append(" of ").Append(total).Append(')').Append('\n').Append('\n');
        builder.Append("Continues [").Append(parentFileName).Append("](").Append(parentFileName)
            .Append(".md); this part carries one chronological segment of the decomposed sequence diagram.")
            .Append('\n')
            .Append('\n');
        builder.Append("## Sequence diagram").Append('\n').Append('\n');
        builder.Append("```mermaid").Append('\n');
        builder.Append(MermaidRenderer.Render(diagram)).Append('\n');
        builder.Append("```").Append('\n');
        return builder.ToString();
    }

    public static string RenderIndex(
        string profileId,
        string programIndexFingerprint,
        IReadOnlyList<(string OperationKey, string FileName)> documents)
        => RenderIndex(profileId, programIndexFingerprint, documents, childDocumentsByFileName: null);

    /// <summary>
    /// Additive child-listing overload: identical output to the existing signature, plus indented
    /// child-part links under any decomposed document. Existing callers stay byte-stable.
    /// </summary>
    public static string RenderIndex(
        string profileId,
        string programIndexFingerprint,
        IReadOnlyList<(string OperationKey, string FileName)> documents,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? childDocumentsByFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint);
        ArgumentNullException.ThrowIfNull(documents);

        var builder = new StringBuilder();
        builder.Append("# SeqDoc Documentation Index").Append('\n').Append('\n');
        builder
            .Append("SeqDoc generated this index from the active analysis. ")
            .Append("Document links resolve to evidence-backed flows in this directory.")
            .Append('\n')
            .Append('\n');
        builder.Append("## Profile").Append('\n').Append('\n');
        builder.Append("- Profile: ").Append(profileId).Append('\n');
        builder.Append("- Program Index fingerprint: ").Append(programIndexFingerprint).Append('\n').Append('\n');
        builder.Append("## Documents").Append('\n').Append('\n');
        foreach (var document in documents.OrderBy(item => item.OperationKey, StringComparer.Ordinal))
        {
            builder.Append("- [").Append(EscapeMarkdown(document.OperationKey)).Append("](")
                .Append(document.FileName).Append(')').Append('\n');
            if (childDocumentsByFileName is not null
                && childDocumentsByFileName.TryGetValue(document.FileName, out var children))
            {
                foreach (string child in children)
                {
                    builder.Append("  - [").Append(child).Append("](").Append(child).Append(".md)").Append('\n');
                }
            }
        }

        return builder.ToString();
    }

    private static void AppendPhrase(StringBuilder builder, WordingPhrase phrase)
    {
        string evidence = string.Join(
            ", ",
            phrase.Evidence.Select(item => item.Artifact).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        builder.Append("- ").Append(phrase.Text)
            .Append(" _(certainty: ").Append(phrase.Certainty)
            .Append("; evidence: ").Append(evidence).Append(")_").Append('\n');
    }

    private static string EscapeMarkdown(string value) => value.Replace("[", "\\[").Replace("]", "\\]");
}
