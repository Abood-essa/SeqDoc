namespace SeqDoc.Rendering.Markdown;

/// <summary>
/// One fully rendered output file. The relative path is canonical (forward slashes, no absolute
/// segments, no parent traversal) and the content is a complete UTF-8 byte sequence so callers can
/// render and validate entirely in memory before touching the output root.
/// </summary>
public sealed record RenderedOutputFile(string RelativePath, byte[] Content);
