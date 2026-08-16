using System.Collections.Immutable;
using System.Text;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Wording;

namespace SeqDoc.Rendering.Markdown;

/// <summary>One planned documentation entry with its deterministic output file base name.</summary>
public sealed record DocumentSetEntry(string FileName, WordingDocument Wording, DiagramPlan Diagram);

/// <summary>Reports the in-memory documentation set build result.</summary>
public sealed record DocumentationSetBuildResult(
    bool Succeeded,
    ImmutableArray<RenderedOutputFile> Files,
    ImmutableArray<string> Errors);

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
        IReadOnlyList<DocumentSetEntry> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint);
        ArgumentNullException.ThrowIfNull(documents);

        var files = new List<RenderedOutputFile>();
        var indexEntries = new List<(string OperationKey, string FileName)>();
        var errors = new List<string>();
        foreach (var document in documents.OrderBy(item => item.FileName, StringComparer.Ordinal))
        {
            string markdown = MarkdownRenderer.RenderDocument(document.Wording, document.Diagram);
            string mermaid = MermaidRenderer.Render(document.Diagram);
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
            return new DocumentationSetBuildResult(false, [], errors.ToImmutableArray());
        }

        string index = MarkdownRenderer.RenderIndex(profileId, programIndexFingerprint, indexEntries);
        files.Add(new RenderedOutputFile("index.md", Encoding.UTF8.GetBytes(index)));
        return new DocumentationSetBuildResult(
            true,
            files.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToImmutableArray(),
            []);
    }
}
