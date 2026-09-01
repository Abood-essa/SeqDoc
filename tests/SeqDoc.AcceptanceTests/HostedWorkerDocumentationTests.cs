using System.Text;
using System.Runtime.ExceptionServices;
using Microsoft.Data.Sqlite;
using SeqDoc.Cli;
using Xunit;

namespace SeqDoc.AcceptanceTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostedWorkerDocumentationGroup
{
    public const string Name = "P32-R1 HostedWorker documentation";
}

[Collection(HostedWorkerDocumentationGroup.Name)]
public sealed class HostedWorkerDocumentationTests
{
    [Fact]
    public async Task HostedWorkerCliProducesEvidenceBackedDeterministicDocumentation()
    {
        string root = FindRepositoryRoot();
        string project = Path.Combine(root, "tests", "fixtures", "PassC", "HostedWorkers", "HostedWorkers.csproj");
        string firstOutput = Path.Combine(Path.GetTempPath(), $"seqdoc-p32-worker-output-{Guid.NewGuid():N}");
        string firstCache = Path.Combine(Path.GetTempPath(), $"seqdoc-p32-worker-cache-{Guid.NewGuid():N}.db");
        string secondOutput = Path.Combine(Path.GetTempPath(), $"seqdoc-p32-worker-output-{Guid.NewGuid():N}");
        string secondCache = Path.Combine(Path.GetTempPath(), $"seqdoc-p32-worker-cache-{Guid.NewGuid():N}.db");

        Exception? testFailure = null;
        try
        {
            var first = await RunAsync(project, root, firstOutput, firstCache);
            var second = await RunAsync(project, root, secondOutput, secondCache);
            Assert.True(first.ExitCode == 0, first.Error);
            Assert.True(second.ExitCode == 0, second.Error);

            var firstFiles = ReadOwnedFiles(firstOutput);
            var secondFiles = ReadOwnedFiles(secondOutput);
            Assert.NotEmpty(firstFiles);
            Assert.Equal(firstFiles.Select(item => item.Key), secondFiles.Select(item => item.Key));
            Assert.True(firstFiles.Zip(secondFiles).All(pair => pair.First.Value.SequenceEqual(pair.Second.Value)));

            var direct = WorkerFiles(firstFiles, "DirectSemaphoreWorker");
            var proof = WorkerFiles(firstFiles, "SemaphoreProofWorker");
            var retry = WorkerFiles(firstFiles, "RetryWorker");
            var terminal = WorkerFiles(firstFiles, "TerminalWorker");
            var background = WorkerFiles(firstFiles, "BackgroundWorker");
            string markdown = string.Join("\n", firstFiles.Where(item => item.Key.EndsWith(".md", StringComparison.Ordinal)).Select(item => Encoding.UTF8.GetString(item.Value)));
            string mermaid = string.Join("\n", firstFiles.Where(item => item.Key.EndsWith(".mmd", StringComparison.Ordinal)).Select(item => Encoding.UTF8.GetString(item.Value)));
            Assert.Contains("awaited repeating loop", direct.Markdown, StringComparison.Ordinal);
            Assert.Equal(1, Count(direct.Markdown, "semaphore synchronization boundary"));
            Assert.Equal(1, Count(direct.Mermaid, "semaphore synchronization boundary"));
            Assert.Contains("awaited repeating loop", proof.Markdown, StringComparison.Ordinal);
            Assert.Equal(1, Count(proof.Markdown, "semaphore synchronization boundary"));
            Assert.Equal(1, Count(proof.Mermaid, "semaphore synchronization boundary"));
            Assert.Contains("cancellation check", proof.Mermaid, StringComparison.Ordinal);
            Assert.Contains("awaited repeating loop", retry.Markdown, StringComparison.Ordinal);
            Assert.Contains("catch-to-loop continuation boundary", retry.Markdown, StringComparison.Ordinal);
            Assert.Contains("throw boundary", retry.Markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("return boundary", retry.Markdown, StringComparison.Ordinal);
            Assert.Contains("return boundary", terminal.Markdown, StringComparison.Ordinal);
            Assert.Contains("throw boundary", terminal.Markdown, StringComparison.Ordinal);
            Assert.Contains("cancellation check", proof.Markdown, StringComparison.Ordinal);
            Assert.Contains("awaited repeating loop", background.Markdown, StringComparison.Ordinal);
            Assert.Contains("enumeration loop", background.Markdown, StringComparison.Ordinal);
            AssertNestedLoopStructure(background.Mermaid);
            Assert.DoesNotContain("Condition", mermaid, StringComparison.Ordinal);
            Assert.DoesNotContain("Continue evaluating condition", mermaid, StringComparison.Ordinal);
            foreach (string worker in new[]
            {
                "SemaphoreNegativeShapesWorker", "SemaphoreUnawaitedWorker", "SemaphoreLoopMismatchWorker",
                "SemaphoreBranchWorker", "SemaphoreConsumptionWorker", "SemaphoreReceiverWorker",
                "SemaphoreLookalikeWorker", "SemaphoreDynamicWorker", "SemaphoreExtensionWorker",
                "DerivedSemaphoreWorker", "SemaphoreNestedLoopWorker"
            })
            {
                var negative = WorkerFiles(firstFiles, worker);
                Assert.DoesNotContain("semaphore synchronization boundary", negative.Markdown, StringComparison.Ordinal);
                Assert.DoesNotContain("semaphore synchronization boundary", negative.Mermaid, StringComparison.Ordinal);
            }

            foreach (string worker in new[] { "DerivedSemaphoreWorker", "SemaphoreNestedLoopWorker" })
            {
                var negative = WorkerFiles(firstFiles, worker);
                Assert.Contains("SC-WORKER-UNSUPPORTED-PLACEMENT", negative.Markdown, StringComparison.Ordinal);
            }

            var cancellationNegative = WorkerFiles(firstFiles, "CancellationNegativeWorker");
            Assert.DoesNotContain("cancellation check", cancellationNegative.Markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("cancellation check", cancellationNegative.Mermaid, StringComparison.Ordinal);
            Assert.Contains(firstFiles, item => Encoding.UTF8.GetString(item.Value)
                .Contains("SC-WORKER-UNSUPPORTED-PLACEMENT", StringComparison.Ordinal));
            foreach (string claim in new[]
            {
                "completed successfully", "eventually succeeds", "will eventually", "cancellation succeeded",
                "executions per second", "runtime throughput", "observed duration", "runtime timing",
                "execution timing", "runtime rate", "observed rate", "runtime schedule", "observed schedule"
            })
            {
                Assert.DoesNotContain(claim, markdown, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception exception)
        {
            testFailure = exception;
        }
        var cleanupFailures = new List<Exception>();
        Delete(firstOutput, firstCache, cleanupFailures);
        Delete(secondOutput, secondCache, cleanupFailures);
        if (testFailure is not null)
        {
            ExceptionDispatchInfo.Capture(testFailure).Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException("Hosted-worker acceptance cleanup failed.", cleanupFailures);
        }
    }

    private static async Task<(int ExitCode, string Error)> RunAsync(string project, string root, string output, string cache)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exitCode = await CliHost.RunAsync(
            ["analyze", project, "--repository-root", root, "--configuration", "Release", "--framework", "net10.0",
             "--cache", cache, "--output", output, "--json"], stdout, stderr);
        return (exitCode, stderr.ToString());
    }

    private static KeyValuePair<string, byte[]>[] ReadOwnedFiles(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new KeyValuePair<string, byte[]>(
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllBytes(path)))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();

    private static void Delete(string output, string cache, ICollection<Exception> failures)
    {
        TryCleanup(() =>
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }, failures);

        TryCleanup(SqliteConnection.ClearAllPools, failures);
        foreach (string path in new[] { cache, cache + "-wal", cache + "-shm" })
        {
            TryCleanup(() => DeleteFileWithRetry(path), failures);
        }
    }

    private static void TryCleanup(Action cleanup, ICollection<Exception> failures)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void DeleteFileWithRetry(string path)
    {
        const int attempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static int Count(string value, string token)
        => value.Split(token, StringSplitOptions.None).Length - 1;

    private static void AssertNestedLoopStructure(string mermaid)
    {
        string[] lines = mermaid.Split('\n');
        var openings = lines.Select((line, index) => (Line: line.TrimEnd('\r'), Index: index))
            .Where(item => item.Line.TrimStart().StartsWith("loop ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, openings.Length);
        Assert.All(openings, item => Assert.Equal("loop each iteration", item.Line.TrimStart()));
        Assert.True(openings[0].Line.Length - openings[0].Line.TrimStart().Length
            < openings[1].Line.Length - openings[1].Line.TrimStart().Length);

        var stack = new Stack<(int Index, int Indent)>();
        var closingIndexes = new List<int>();
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            string trimmed = line.TrimStart();
            int indent = line.Length - trimmed.Length;
            if (trimmed.StartsWith("loop ", StringComparison.Ordinal))
            {
                stack.Push((index, indent));
            }
            else if (trimmed == "end")
            {
                if (stack.Count > 0)
                {
                    closingIndexes.Add(index);
                    stack.Pop();
                }
            }
        }

        Assert.Empty(stack);
        Assert.Equal(2, closingIndexes.Count);
        Assert.True(openings[1].Index < closingIndexes[0]);
        Assert.True(closingIndexes[0] < closingIndexes[1]);
    }

    private static (string Markdown, string Mermaid) WorkerFiles(IEnumerable<KeyValuePair<string, byte[]>> files, string worker)
    {
        var owned = files.Where(item => !IsAggregate(item.Key)
                && (item.Key.EndsWith(".md", StringComparison.Ordinal) || item.Key.EndsWith(".mmd", StringComparison.Ordinal))
                && Encoding.UTF8.GetString(item.Value).Contains(worker, StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, owned.Length);
        Assert.Single(owned, item => item.Key.EndsWith(".md", StringComparison.Ordinal));
        Assert.Single(owned, item => item.Key.EndsWith(".mmd", StringComparison.Ordinal));
        return (
            string.Join("\n", owned.Where(item => item.Key.EndsWith(".md", StringComparison.Ordinal)).Select(item => Encoding.UTF8.GetString(item.Value))),
            string.Join("\n", owned.Where(item => item.Key.EndsWith(".mmd", StringComparison.Ordinal)).Select(item => Encoding.UTF8.GetString(item.Value))));
    }

    private static bool IsAggregate(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("index.mmd", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("aggregate", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
