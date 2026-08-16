using System.Text.RegularExpressions;
using SeqDoc.Core.Identity;

namespace SeqDoc.Rendering.Markdown;

/// <summary>
/// Deterministic output-file naming for one documentation entry. The entry key is derived from the
/// canonical operation key plus a stable suffix of the entry-point identity, so names are readable,
/// path-free, timestamp-free, and unique across routes that sanitize to the same slug. This helper
/// is renderer-neutral and never inspects scenario graphs.
/// </summary>
public static partial class DocumentationFileNaming
{
    public static string EntryKey(EntryPointId entryPoint, string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint.Value, nameof(entryPoint));

        string slug = NonAlphanumericRegex().Replace(operationKey.ToLowerInvariant(), "-");
        slug = DuplicateDashRegex().Replace(slug, "-").Trim('-');
        string suffix = entryPoint.Value.Length >= 8 ? entryPoint.Value[^8..] : entryPoint.Value;
        return $"{slug}-{suffix}";
    }

    public static string CanonicalRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string normalized = relativePath.Replace('\\', '/');
        if (normalized.Length == 0
            || normalized.StartsWith('/')
            || normalized.Contains("://", StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException(
                $"Output path '{relativePath}' must be a canonical relative path without parent traversal.",
                nameof(relativePath));
        }

        return normalized;
    }

    [GeneratedRegex("[^a-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateDashRegex();
}
