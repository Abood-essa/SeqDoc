using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SeqDoc.Rendering.Markdown;

/// <summary>Reports the outcome of one output-set activation.</summary>
public sealed record OutputActivationReport(
    bool Succeeded,
    string JournalState,
    ImmutableArray<string> WrittenFiles,
    ImmutableArray<string> RemovedFiles,
    string? FailureMessage);

/// <summary>
/// Owns the staged recoverable output-set policy. Activation renders and validates fully in memory,
/// stages a complete profile output, backs up every file the swap will touch, installs the staged
/// files, records ownership through a deterministic manifest, and journals each step so an
/// interrupted swap can roll back or recover on the next activation. Unowned files are never
/// touched; stale cleanup removes only files the prior manifest owned. A failed activation restores
/// the previous owned output and writes an explicit stale marker so the directory never pretends it
/// matches the active analysis. The journal and manifest are timestamp-free and path-free.
/// </summary>
public static partial class OutputSetActivator
{
    private const string ManifestFileName = "seqdoc.manifest.json";
    private const string StaleMarkerFileName = "seqdoc.stale";
    private const string MachineryDirectoryName = ".seqdoc";
    private const string JournalFileName = "journal.json";
    private const string StageDirectoryName = "stage";
    private const string BackupDirectoryName = "backup";

    private static readonly JsonSerializerOptions JournalJsonOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private const string StaleMarkerText = """
        SeqDoc documentation is stale.
        The most recent documentation generation did not complete successfully, so the files in this directory may not match the active analysis.
        Run 'seqdoc analyze <target> --output <path>' again to regenerate.
        """;

    public static OutputActivationReport Activate(string outputRoot, IReadOnlyList<RenderedOutputFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(files);

        RenderedOutputFile[] ordered = [];
        ImmutableArray<ManifestEntry> newEntries = [];
        ImmutableArray<string> stalePaths = [];
        bool journalPreparedByThisCall = false;
        try
        {
            Directory.CreateDirectory(outputRoot);
            ReconcileOrRollback(outputRoot);

            ordered = files
                .Select(ValidateFile)
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .ToArray();
            newEntries = ordered
                .Select(file => new ManifestEntry(file.RelativePath, Sha256(file.Content)))
                .ToImmutableArray();
            ImmutableArray<string> priorOwned = ReadManifestEntries(outputRoot).Select(entry => entry.RelativePath).ToImmutableArray();

            // A newly generated path that already exists on disk but is absent from the prior valid
            // manifest is a user-file collision. Reject before staging or journaling anything and
            // mark the output stale; the unowned file is preserved byte-for-byte.
            foreach (string path in newEntries.Select(entry => entry.RelativePath))
            {
                if (!priorOwned.Contains(path, StringComparer.Ordinal)
                    && File.Exists(Path.Combine(outputRoot, path)))
                {
                    throw new IOException(
                        $"A file already exists at generated path '{path}' but is not owned by the previous SeqDoc output; refusing to overwrite it.");
                }
            }

            stalePaths = priorOwned.Except(newEntries.Select(entry => entry.RelativePath), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray();
            var backupPaths = priorOwned
                .Concat(newEntries.Select(entry => entry.RelativePath))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray();

            var journal = new ActivationJournal(
                1,
                "prepared",
                newEntries,
                stalePaths,
                backupPaths);

            string machinery = MachineryPath(outputRoot);
            string stage = Path.Combine(machinery, StageDirectoryName);
            string backup = Path.Combine(machinery, BackupDirectoryName);

            WriteStage(stage, ordered);
            WriteJournal(machinery, journal);
            journalPreparedByThisCall = true;

            Backup(outputRoot, backup, backupPaths);
            Install(outputRoot, stage, ordered);
            WriteManifest(outputRoot, newEntries);
            WriteJournal(machinery, journal with { State = "committed" });
            DeleteDirectory(stage);
            DeleteDirectory(backup);
            DeleteFile(Path.Combine(outputRoot, StaleMarkerFileName));

            return new OutputActivationReport(
                true,
                "committed",
                newEntries.Select(entry => entry.RelativePath).ToImmutableArray(),
                stalePaths,
                null);
        }
        catch (Exception exception)
        {
            ReconcileOrRollback(outputRoot);
            string state = ReadJournalState(outputRoot);
            if (journalPreparedByThisCall && state == "committed")
            {
                // The swap itself committed; only a later cleanup or journal finalization failed.
                return new OutputActivationReport(
                    true,
                    "committed",
                    newEntries.Select(entry => entry.RelativePath).ToImmutableArray(),
                    stalePaths,
                    null);
            }

            // A committed journal here belongs to an earlier successful activation, not this call;
            // every validation, render, and staging failure still marks the documentation stale and
            // returns a failed activation while preserving the previous bytes untouched.
            MarkStale(outputRoot);
            return new OutputActivationReport(
                false,
                "rolled-back",
                [],
                [],
                exception.Message);
        }
    }

    /// <summary>
    /// Recovers an interrupted activation. If the journal says a swap was prepared but the manifest
    /// already matches the planned files, the swap is finalized; otherwise the previous owned output
    /// is restored and the directory is marked stale. A missing or committed journal is a no-op.
    /// </summary>
    public static void Recover(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        if (!Directory.Exists(outputRoot))
        {
            return;
        }

        ReconcileOrRollback(outputRoot);
    }

    /// <summary>Writes or updates the stale marker without touching any documentation file.</summary>
    public static void MarkStale(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        try
        {
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(Path.Combine(outputRoot, StaleMarkerFileName), StaleMarkerText, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // The stale marker is best-effort; the caller still returns the original generation failure.
        }
    }

    private static void ReconcileOrRollback(string outputRoot)
    {
        try
        {
            ReconcileOrRollbackCore(outputRoot);
        }
        catch (Exception)
        {
            // Recovery is best-effort and must never delete user files on an unexpected failure.
            MarkStale(outputRoot);
        }
    }

    private static void ReconcileOrRollbackCore(string outputRoot)
    {
        string machinery = MachineryPath(outputRoot);
        string journalPath = Path.Combine(machinery, JournalFileName);
        if (!File.Exists(journalPath))
        {
            return;
        }

        ActivationJournal journal;
        try
        {
            journal = ReadJournal(journalPath);
        }
        catch (Exception)
        {
            // A corrupt journal never authorizes deleting user files; clean only the machinery
            // directories and mark the documentation stale so the next run starts fresh.
            DeleteDirectory(Path.Combine(machinery, StageDirectoryName));
            DeleteDirectory(Path.Combine(machinery, BackupDirectoryName));
            MarkStale(outputRoot);
            return;
        }

        if (journal.State is "committed" or "rolled-back")
        {
            return;
        }

        string stage = Path.Combine(machinery, StageDirectoryName);
        string backup = Path.Combine(machinery, BackupDirectoryName);
        ImmutableArray<ManifestEntry> manifest = ReadManifestEntries(outputRoot);
        bool manifestMatches = manifest
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .Select(entry => (entry.RelativePath, entry.Sha256))
            .SequenceEqual(journal.NewFiles
                .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
                .Select(entry => (entry.RelativePath, entry.Sha256)));
        if (manifestMatches)
        {
            // The swap committed before the journal was finalized; finish the commit.
            WriteJournal(machinery, journal with { State = "committed" });
            DeleteDirectory(stage);
            DeleteDirectory(backup);
            DeleteFile(Path.Combine(outputRoot, StaleMarkerFileName));
            return;
        }

        Rollback(outputRoot, journal, stage, backup);
        WriteJournal(machinery, journal with { State = "rolled-back" });
        MarkStale(outputRoot);
    }

    private static string ReadJournalState(string outputRoot)
    {
        string journalPath = Path.Combine(MachineryPath(outputRoot), JournalFileName);
        if (!File.Exists(journalPath))
        {
            return "none";
        }

        try
        {
            ActivationJournal journal = ReadJournal(journalPath);
            return journal.State;
        }
        catch (Exception)
        {
            return "none";
        }
    }

    private static void Rollback(string outputRoot, ActivationJournal journal, string stage, string backup)
    {
        // Ground truth comes from the backup directory, not the journal's plan: a path has a backup
        // copy only when the crash actually moved it.
        var backedUp = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in journal.BackupPaths)
        {
            if (File.Exists(Path.Combine(backup, path)))
            {
                backedUp.Add(path);
            }
        }

        // Restore every backed-up file, overwriting any partially installed content.
        foreach (string path in backedUp)
        {
            string backupFile = Path.Combine(backup, path);
            string target = Path.Combine(outputRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Move(backupFile, target);
        }

        // Install starts only after the backup phase completes. If any prior-owned (non-new) path
        // still sits in the output root without a backup copy, the crash interrupted the backup
        // phase and no new file can have been installed yet.
        var newPaths = journal.NewFiles.Select(entry => entry.RelativePath).ToHashSet(StringComparer.Ordinal);
        bool backupIncomplete = journal.BackupPaths.Any(path =>
            !newPaths.Contains(path)
            && !backedUp.Contains(path)
            && File.Exists(Path.Combine(outputRoot, path)));

        if (!backupIncomplete)
        {
            // Delete only provably installed new files: new paths with no backup copy were written
            // by the interrupted install. Pre-existing unowned files at new paths were backed up
            // during a completed backup and were restored above.
            foreach (string path in newPaths)
            {
                if (!backedUp.Contains(path))
                {
                    DeleteFile(Path.Combine(outputRoot, path));
                }
            }
        }

        // Stale files are never deleted on rollback: backed-up stale files were restored above, and
        // stale files that were never backed up are untouched prior-owned originals.
        DeleteDirectory(stage);
        DeleteDirectory(backup);
    }

    private static void WriteStage(string stage, IEnumerable<RenderedOutputFile> files)
    {
        if (Directory.Exists(stage))
        {
            Directory.Delete(stage, recursive: true);
        }

        Directory.CreateDirectory(stage);
        foreach (var file in files)
        {
            string target = Path.Combine(stage, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, file.Content);
        }
    }

    private static void Backup(string outputRoot, string backup, IEnumerable<string> paths)
    {
        if (Directory.Exists(backup))
        {
            Directory.Delete(backup, recursive: true);
        }

        foreach (string path in paths)
        {
            string source = Path.Combine(outputRoot, path);
            if (!File.Exists(source))
            {
                continue;
            }

            string target = Path.Combine(backup, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Move(source, target);
        }
    }

    private static void Install(string outputRoot, string stage, IEnumerable<RenderedOutputFile> files)
    {
        foreach (var file in files)
        {
            string source = Path.Combine(stage, file.RelativePath);
            string target = Path.Combine(outputRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Move(source, target);
        }
    }

    private static void WriteManifest(string outputRoot, ImmutableArray<ManifestEntry> entries)
    {
        var document = new ManifestDocument(1, entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToImmutableArray());
        string path = Path.Combine(outputRoot, ManifestFileName);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            WriteCanonicalText(temporary, JsonSerializer.Serialize(document, ManifestJsonOptions));
            Move(temporary, path);
        }
        finally
        {
            DeleteFile(temporary);
        }
    }

    private static void WriteJournal(string machinery, ActivationJournal journal)
    {
        Directory.CreateDirectory(machinery);
        string path = Path.Combine(machinery, JournalFileName);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            WriteCanonicalText(temporary, JsonSerializer.Serialize(journal, JournalJsonOptions));
            Move(temporary, path);
        }
        finally
        {
            DeleteFile(temporary);
        }
    }

    /// <summary>Writes UTF-8 text with canonical newlines so generated output never varies by platform.</summary>
    private static void WriteCanonicalText(string path, string content)
    {
        string canonical = content.Replace("\r\n", "\n").Replace('\r', '\n');
        File.WriteAllText(path, canonical, new UTF8Encoding(false));
    }

    private static ActivationJournal ReadJournal(string journalPath)
    {
        string text = File.ReadAllText(journalPath);
        ActivationJournal? journal = JsonSerializer.Deserialize<ActivationJournal>(text);
        if (journal is null)
        {
            throw new InvalidDataException("The output journal could not be read.");
        }

        // Strict structural parsing before any filesystem use: the journal is trusted only after
        // schema, state, and every recorded path are proven valid. Hashes are informational in the
        // journal (used only for manifest-match reconciliation) so recovery journals may carry
        // placeholders; the manifest enforces the strict 64-hex contract.
        if (journal.SchemaVersion != 1)
        {
            throw new InvalidDataException("The output journal has an unsupported schema version.");
        }

        if (journal.State is not ("prepared" or "committed" or "rolled-back"))
        {
            throw new InvalidDataException("The output journal has an invalid state.");
        }

        if (journal.NewFiles.IsDefault || journal.StaleFiles.IsDefault || journal.BackupPaths.IsDefault)
        {
            throw new InvalidDataException("The output journal is missing required arrays.");
        }

        // Canonical, confined, non-reserved paths with within-array uniqueness. The same path may
        // legitimately appear in both StaleFiles and BackupPaths (a stale prior-owned file is
        // backed up before removal), so uniqueness is enforced per array only.
        string[] newPaths = journal.NewFiles
            .Select(entry => ValidateRecordedPath(entry.RelativePath, "journal"))
            .ToArray();
        string[] stalePaths = journal.StaleFiles
            .Select(path => ValidateRecordedPath(path, "journal"))
            .ToArray();
        string[] backupPaths = journal.BackupPaths
            .Select(path => ValidateRecordedPath(path, "journal"))
            .ToArray();
        AssertUniquePaths(newPaths, "journal new-files");
        AssertUniquePaths(stalePaths, "journal stale-files");
        AssertUniquePaths(backupPaths, "journal backup-paths");

        return journal;
    }

    private static ImmutableArray<ManifestEntry> ReadManifestEntries(string outputRoot)
    {
        string path = Path.Combine(outputRoot, ManifestFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        ManifestDocument? document = JsonSerializer.Deserialize<ManifestDocument>(File.ReadAllText(path), ManifestJsonOptions);
        if (document is null)
        {
            throw new InvalidDataException("The output ownership manifest could not be read.");
        }

        // Strict manifest parsing before any filesystem use. Invalid metadata may only mark the
        // output stale; no listed path inside or outside the output root is ever touched.
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException("The output ownership manifest has an unsupported schema version.");
        }

        if (document.Files.IsDefault)
        {
            throw new InvalidDataException("The output ownership manifest is missing its files array.");
        }

        var entries = new List<ManifestEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in document.Files)
        {
            string relativePath = ValidateRecordedPath(entry.RelativePath, "manifest");
            if (!seen.Add(relativePath))
            {
                throw new InvalidDataException($"The output ownership manifest records duplicate path '{relativePath}'.");
            }

            if (!IsLowerHex64(entry.Sha256))
            {
                throw new InvalidDataException($"The output ownership manifest records an invalid hash for '{relativePath}'.");
            }

            entries.Add(new ManifestEntry(relativePath, entry.Sha256));
        }

        return entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToImmutableArray();
    }

    /// <summary>
    /// Validates one recorded relative path from manifest or journal metadata: canonical, confined
    /// under the output root, unique, and never a reserved SeqDoc metadata path.
    /// </summary>
    private static string ValidateRecordedPath(string relativePath, string source)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException($"The {source} records an empty path.");
        }

        string canonical;
        try
        {
            canonical = DocumentationFileNaming.CanonicalRelativePath(relativePath);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"The {source} records a non-canonical path '{relativePath}'.", exception);
        }

        if (IsReservedPath(canonical))
        {
            throw new InvalidDataException($"The {source} records reserved path '{canonical}'.");
        }

        return canonical;
    }

    private static void AssertUniquePaths(IEnumerable<string> paths, string source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (!seen.Add(path))
            {
                throw new InvalidDataException($"The {source} records duplicate path '{path}'.");
            }
        }
    }

    private static bool IsReservedPath(string canonicalRelativePath)
        => string.Equals(canonicalRelativePath, ManifestFileName, StringComparison.Ordinal)
           || string.Equals(canonicalRelativePath, StaleMarkerFileName, StringComparison.Ordinal)
           || string.Equals(canonicalRelativePath, MachineryDirectoryName, StringComparison.Ordinal)
           || canonicalRelativePath.StartsWith(MachineryDirectoryName + "/", StringComparison.Ordinal);

    private static bool IsLowerHex64(string value)
        => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static RenderedOutputFile ValidateFile(RenderedOutputFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(file.Content);
        if (file.Content.Length == 0)
        {
            throw new ArgumentException($"Output file '{file.RelativePath}' has empty content.", nameof(file));
        }

        string canonical = DocumentationFileNaming.CanonicalRelativePath(file.RelativePath);
        if (IsReservedPath(canonical))
        {
            throw new ArgumentException(
                $"Output path '{file.RelativePath}' is reserved for SeqDoc metadata and cannot be generated.",
                nameof(file));
        }

        return new RenderedOutputFile(canonical, file.Content);
    }

    private static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static string MachineryPath(string outputRoot) => Path.Combine(outputRoot, MachineryDirectoryName);

    private static void Move(string source, string target) => File.Move(source, target, overwrite: true);

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record ManifestDocument(int SchemaVersion, ImmutableArray<ManifestEntry> Files);

    private sealed record ManifestEntry(string RelativePath, string Sha256);

    private sealed record ActivationJournal(
        int SchemaVersion,
        string State,
        ImmutableArray<ManifestEntry> NewFiles,
        ImmutableArray<string> StaleFiles,
        ImmutableArray<string> BackupPaths);
}
