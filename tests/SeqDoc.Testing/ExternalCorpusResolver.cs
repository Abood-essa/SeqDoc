using Xunit;
using Xunit.Sdk;

namespace SeqDoc.Testing;

public enum ExternalCorpusGroup
{
    Provided,
    OpenSource,
}

public sealed record ExternalCorpusResolverInput(
    string AppContextBaseDirectory,
    string? EnvironmentOverride,
    string? CurrentDirectory);

public sealed class ExternalCorpusResolutionException : Exception
{
    public ExternalCorpusResolutionException(string expectedPath, string message)
        : base(message) => ExpectedPath = expectedPath;

    public string ExpectedPath { get; }
}

public sealed class ExternalCorpusResolver
{
    private const string RepositoryMarker = "SeqDoc.slnx";
    private readonly string _root;

    private ExternalCorpusResolver(string root) => _root = root;

    public static ExternalCorpusResolver Current => Resolve();

    public static ExternalCorpusResolver Resolve()
    {
        string repositoryRoot = DiscoverRepositoryRoot(AppContext.BaseDirectory);
        return Resolve(new ExternalCorpusResolverInput(
            repositoryRoot,
            Environment.GetEnvironmentVariable("SEQDOC_TEST_PROJECTS_ROOT"),
            Environment.CurrentDirectory));
    }

    public static string DiscoverRepositoryRoot(string baseDirectory, string marker = RepositoryMarker)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(baseDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, marker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not discover the SeqDoc repository root from '{baseDirectory}'.");
    }

    public static ExternalCorpusResolver Resolve(ExternalCorpusResolverInput input)
    {
        string repositoryRoot = Path.GetFullPath(input.AppContextBaseDirectory);
        string corpusRoot = string.IsNullOrWhiteSpace(input.EnvironmentOverride)
            ? Path.Combine(repositoryRoot, "..", "SeqDoc-TestProjects")
            : Path.IsPathRooted(input.EnvironmentOverride!)
                ? input.EnvironmentOverride!
                : Path.Combine(repositoryRoot, input.EnvironmentOverride!);
        return new ExternalCorpusResolver(Path.GetFullPath(corpusRoot));
    }

    public string Root => _root;

    public ExternalCorpusGroupDirectory RequireGroup(ExternalCorpusGroup group)
    {
        RequireInstalledRoot();
        string path = Path.Combine(_root, group.ToString());
        if (!Directory.Exists(path))
        {
            throw Missing(path, $"External corpus group '{group}' is missing.");
        }

        return new ExternalCorpusGroupDirectory(path);
    }

    public void RequireInstalledRoot()
    {
        if (!Directory.Exists(_root))
        {
            throw SkipException.ForSkip("External test-project corpus is not installed.");
        }
    }

    private static ExternalCorpusResolutionException Missing(string path, string description)
        => new(path, description);
}

public sealed class ExternalCorpusGroupDirectory
{
    internal ExternalCorpusGroupDirectory(string root) => Root = Path.GetFullPath(root);
    public string Root { get; }

    public string RequireProject(string relativePath) => RequireFile(relativePath);

    public string RequireFile(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)
            || relativePath.Split(['/', '\\'], StringSplitOptions.None)
                .Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("The corpus file path must be a rooted-free path without traversal.", nameof(relativePath));
        }

        string path = Path.GetFullPath(Root + Path.DirectorySeparatorChar + relativePath);
        string rootWithSeparator = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The corpus file path must remain within its selected group.", nameof(relativePath));
        }

        if (!File.Exists(path))
        {
            throw new ExternalCorpusResolutionException(
                path,
                "External corpus file is missing.");
        }

        return path;
    }
}
