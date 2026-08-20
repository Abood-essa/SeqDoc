using Xunit;
using Xunit.Sdk;

namespace SeqDoc.Analysis.Tests;

// Contract tests for the test-only corpus resolver.  The resolver deliberately accepts
// all ambient values as arguments so these tests do not mutate process environment or
// current-directory state while xUnit is running in parallel.
public sealed class ExternalCorpusResolverTests
{
    [Fact]
    public void RepositoryRootIsDiscoveredFromAppContextBaseDirectoryAndMarker()
    {
        using var tree = TemporaryTree.Create();
        string baseDirectory = tree.CreateDirectory("artifacts/testhost");
        tree.CreateFile("AGENTS.md");

        string root = SeqDoc.Testing.ExternalCorpusResolver.DiscoverRepositoryRoot(
            baseDirectory,
            "AGENTS.md");

        Assert.Equal(tree.Root, root);
    }

    [Fact]
    public void ExplicitOverrideWinsOverSiblingDefault()
    {
        using var tree = TemporaryTree.Create();
        string overrideRoot = tree.CreateDirectory("custom-corpus");
        string siblingRoot = tree.CreateDirectory("SeqDoc-TestProjects");

        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(
                tree.Root,
                overrideRoot,
                tree.CreateDirectory("unrelated-working-directory")));

        Assert.Equal(Path.GetFullPath(overrideRoot), resolved.Root);
        Assert.NotEqual(Path.GetFullPath(siblingRoot), resolved.Root);
    }

    [Fact]
    public void RelativeOverrideIsResolvedFromRepositoryRootNotCurrentDirectory()
    {
        using var tree = TemporaryTree.Create();
        string repositoryRoot = tree.CreateDirectory("workspace/SeqDoc");
        string overrideRoot = tree.CreateDirectory("workspace/custom-corpus");
        string misleadingWorkingDirectory = tree.CreateDirectory("elsewhere");

        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(
                repositoryRoot,
                "../custom-corpus",
                misleadingWorkingDirectory));

        Assert.Equal(Path.GetFullPath(overrideRoot), resolved.Root);
    }

    [Fact]
    public void DefaultIsTheRepositorySiblingAndDoesNotUseCurrentDirectory()
    {
        using var tree = TemporaryTree.Create();
        string repositoryRoot = tree.CreateDirectory("workspace/SeqDoc");
        string siblingRoot = tree.CreateDirectory("workspace/SeqDoc-TestProjects");
        string misleadingWorkingDirectory = tree.CreateDirectory("elsewhere/SeqDoc-TestProjects");

        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(
                repositoryRoot,
                null,
                misleadingWorkingDirectory));

        Assert.Equal(Path.GetFullPath(siblingRoot), resolved.Root);
    }

    [Fact]
    public void ProvidedAndOpenSourceAreExplicitIndependentGroups()
    {
        using var tree = TemporaryTree.Create();
        string corpus = tree.CreateDirectory("corpus");
        tree.CreateDirectory("corpus/Provided");
        tree.CreateDirectory("corpus/OpenSource");

        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(tree.Root, corpus, tree.Root));

        Assert.Equal(
            Path.GetFullPath(Path.Combine(corpus, "Provided")),
            resolved.RequireGroup(SeqDoc.Testing.ExternalCorpusGroup.Provided).Root);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(corpus, "OpenSource")),
            resolved.RequireGroup(SeqDoc.Testing.ExternalCorpusGroup.OpenSource).Root);
    }

    [Fact]
    public void MissingWholeCorpusIsAnExplicitXunitSkip()
    {
        using var tree = TemporaryTree.Create();
        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(
                tree.CreateDirectory("SeqDoc"),
                tree.Combine("not-installed"),
                tree.Root));

        Assert.Throws<SkipException>(() => resolved.RequireInstalledRoot());
    }

    [Fact]
    public void MissingInstalledGroupFailsWithTheExactExpectedPath()
    {
        using var tree = TemporaryTree.Create();
        string corpus = tree.CreateDirectory("corpus");
        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(tree.Root, corpus, tree.Root));

        var error = Assert.Throws<SeqDoc.Testing.ExternalCorpusResolutionException>(
            () => resolved.RequireGroup(SeqDoc.Testing.ExternalCorpusGroup.Provided));

        Assert.Equal(Path.Combine(corpus, "Provided"), error.ExpectedPath);
    }

    [Fact]
    public void MissingExpectedProjectFailsWithTheExactExpectedPath()
    {
        using var tree = TemporaryTree.Create();
        string group = tree.CreateDirectory("corpus/Provided");
        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(tree.Root, tree.Combine("corpus"), tree.Root));

        var error = Assert.Throws<SeqDoc.Testing.ExternalCorpusResolutionException>(() =>
            resolved.RequireGroup(SeqDoc.Testing.ExternalCorpusGroup.Provided).RequireProject("Demo/Demo.csproj"));

        Assert.Equal(Path.Combine(group, "Demo", "Demo.csproj"), error.ExpectedPath);
    }

    [Fact]
    public void GroupRelativePathsRejectRootedTraversalAndOutsidePaths()
    {
        using var tree = TemporaryTree.Create();
        string corpus = tree.CreateDirectory("corpus");
        string groupRoot = tree.CreateDirectory("corpus/Provided");
        string outside = tree.CreateDirectory("corpus/Provided2");
        var group = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(tree.Root, corpus, tree.Root))
            .RequireGroup(SeqDoc.Testing.ExternalCorpusGroup.Provided);

        Assert.Throws<ArgumentException>(() => group.RequireFile(Path.Combine(groupRoot, "file.txt")));
        Assert.Throws<ArgumentException>(() => group.RequireFile("nested/./file.txt"));
        Assert.Throws<ArgumentException>(() => group.RequireFile("nested/../file.txt"));
        Assert.Throws<ArgumentException>(() => group.RequireFile(Path.GetRelativePath(groupRoot, outside)));
    }

    [Fact]
    public void ResolvedPathsAreCanonicalAbsolutePaths()
    {
        using var tree = TemporaryTree.Create();
        string corpus = tree.CreateDirectory("corpus");
        string provided = tree.CreateDirectory("corpus/Provided");

        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(tree.Root, Path.Combine(corpus, "."), tree.Root));

        Assert.Equal(Path.GetFullPath(corpus), resolved.Root);
        Assert.Equal(Path.GetFullPath(provided), resolved.RequireGroup(SeqDoc.Testing.ExternalCorpusGroup.Provided).Root);
    }

    [Fact]
    public void DiagnosticsUseStableRelativeContextInsteadOfMachinePaths()
    {
        using var tree = TemporaryTree.Create();
        string corpus = tree.CreateDirectory("corpus");
        var resolved = SeqDoc.Testing.ExternalCorpusResolver.Resolve(
            new SeqDoc.Testing.ExternalCorpusResolverInput(tree.Root, corpus, tree.Root));

        var error = Assert.Throws<SeqDoc.Testing.ExternalCorpusResolutionException>(
            () => resolved.RequireGroup(SeqDoc.Testing.ExternalCorpusGroup.OpenSource));

        Assert.DoesNotContain(tree.Root, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenSource", error.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryTree : IDisposable
    {
        private TemporaryTree(string root) => Root = root;
        public string Root { get; }

        public static TemporaryTree Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "seqdoc-resolver-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporaryTree(root);
        }

        public string CreateDirectory(string relative)
        {
            string path = Combine(relative);
            Directory.CreateDirectory(path);
            return path;
        }

        public void CreateFile(string relative) => File.WriteAllText(Combine(relative), "marker");
        public string Combine(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
