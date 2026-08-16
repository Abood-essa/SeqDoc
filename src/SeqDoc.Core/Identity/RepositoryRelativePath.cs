namespace SeqDoc.Core.Identity;

/// <summary>Normalizes logical repository paths without consulting the host file system.</summary>
public static class RepositoryRelativePath
{
    /// <summary>
    /// Returns a normalized, slash-separated relative path suitable for persisted identity input.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the path is empty, rooted, or escapes the repository through a parent segment.
    /// </exception>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedInput = path.Replace('\\', '/');
        if (IsRooted(normalizedInput))
        {
            throw new ArgumentException("Repository identity paths must be relative.", nameof(path));
        }

        var segments = new List<string>();
        foreach (var segment in normalizedInput.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new ArgumentException("Repository identity paths cannot escape the repository root.", nameof(path));
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0
            ? throw new ArgumentException("Repository identity paths must name an artifact.", nameof(path))
            : string.Join('/', segments);
    }

    private static bool IsRooted(string path)
    {
        return path[0] == '/'
            || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
    }
}
