using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using SeqDoc.Core.Configuration;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Wording;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Rendering.Tests;

// RED tests for checkpoint I23 (optional overview/child decomposition). These tests intentionally
// compile against APIs that do not exist yet. The implementer must match EXACTLY this surface:
//
//   1. namespace SeqDoc.Rendering.Markdown:
//      public sealed record DiagramDecompositionOptions(bool Enabled);
//   2. DocumentationSetBuilder.Build overload:
//      Build(string profileId, string programIndexFingerprint,
//            IReadOnlyList<DocumentSetEntry> documents,
//            DiagramBudget? diagramBudget = null,
//            DiagramDecompositionOptions? decomposition = null)
//   3. Child file naming: "{base}.part-{NNN}" with a three-digit ordinal beginning at 001,
//      chronological (part-001 holds the earliest packed elements).
//
// Behavior contracts under test (capsule claims 1-10): default-off byte identity, opt-in split of
// oversized plans only, disjoint message cover, chronology, seam/outcome retention on the overview,
// whole-subtree fragment movement, determinism, legacy fallback to DP-MERMAID-TRUNCATED, oversized
// single-element truncation inside its own child, and the single stable DP-DIAGRAM-DECOMPOSED
// diagnostic carried only by the overview together with all original diagnostics and behavior text.
public sealed class DocumentationDecompositionTests
{
    private static readonly DiagramDecompositionOptions Enabled = new(Enabled: true);

    // --------------------------------------------------------------------------------------------
    // Claim 1: disabled-by-default (and explicit-disabled) output stays byte-identical with the
    // existing four-argument build, even for a representative oversized fixture.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void DisabledDecompositionIsByteIdenticalWithLegacyBuildForOversizedFixture()
    {
        var plan = DecompositionTestPlans.CreateDecomposablePlan();
        var budget = DecompositionTestPlans.SplittingBudget(plan);
        Assert.True(
            MermaidRenderer.Render(plan).Length > budget.MaxMermaidCharacters,
            "Fixture precondition: the representative plan must exceed the Mermaid budget.");
        var entries = new[]
        {
            new DocumentSetEntry(DecompositionTestPlans.BaseFileName, DecompositionTestPlans.CreateWording(), plan),
        };

        var legacy = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", entries, budget);
        var disabled = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", entries, budget, new DiagramDecompositionOptions(Enabled: false));

        Assert.True(legacy.Succeeded, string.Join("; ", legacy.Errors));
        Assert.Equal(
            legacy.Files.Select(file => (file.RelativePath, file.Content)),
            disabled.Files.Select(file => (file.RelativePath, file.Content)));
        Assert.Equal(
            legacy.Diagnostics.Select(item => (item.Id.Value, item.Code)),
            disabled.Diagnostics.Select(item => (item.Id.Value, item.Code)));
        Assert.DoesNotContain(disabled.Diagnostics, item => item.Code == "DP-DIAGRAM-DECOMPOSED");
        Assert.Single(disabled.Files.Where(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)));
    }

    // --------------------------------------------------------------------------------------------
    // Claim 2: enabled + oversized splits into one overview plus at least two children; enabled +
    // an already-fitting plan stays a single undecomposed diagram.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void EnabledDecompositionSplitsOversizedPlanOnly()
    {
        var plan = DecompositionTestPlans.CreateDecomposablePlan();
        var budget = DecompositionTestPlans.SplittingBudget(plan);
        var split = BuildDecomposed(plan, budget);

        Assert.True(split.Succeeded, string.Join("; ", split.Errors));
        var partNames = PartMermaidNames(split);
        Assert.Equal($"{DecompositionTestPlans.BaseFileName}.mmd", OverviewMermaidName(split));
        Assert.True(partNames.Count >= 2, $"Expected at least two part files but found: {string.Join(", ", partNames)}");

        var fitting = BuildDecomposed(DecompositionTestPlans.CreateDecomposablePlan(), DiagramBudget.Default);
        Assert.True(fitting.Succeeded, string.Join("; ", fitting.Errors));
        Assert.Single(fitting.Files, file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal));
        Assert.DoesNotContain(fitting.Diagnostics, item => item.Code == "DP-DIAGRAM-DECOMPOSED");
        Assert.DoesNotContain(fitting.Files, file => Regex.IsMatch(file.RelativePath, @"^get-api-test\.part-\d{3}\.mmd$"));
    }

    // --------------------------------------------------------------------------------------------
    // Claim 3: multiset union of view messages equals the original messages and views are pairwise
    // disjoint (each unique message label appears exactly once across all emitted Mermaid files);
    // behavior text is asserted once and never duplicated into children.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void ViewMessagesFormDisjointExactCoverAndBehaviorTextIsNotDuplicated()
    {
        const int middleCount = 12;
        var plan = DecompositionTestPlans.CreateDecomposablePlan(middleCount);
        var built = BuildDecomposed(plan, DecompositionTestPlans.SplittingBudget(plan));

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        string allMermaid = string.Join('\n', MermaidTexts(built));
        foreach (string label in DecompositionTestPlans.AllLabels(middleCount))
        {
            Assert.Equal(1, CountOccurrences(allMermaid, label));
        }

        string allMarkdown = string.Join('\n', built.Files
            .Where(file => file.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            .Select(file => Encoding.UTF8.GetString(file.Content)));
        Assert.Equal(1, CountOccurrences(allMarkdown, DecompositionTestPlans.BehaviorPhraseText));
    }

    // --------------------------------------------------------------------------------------------
    // Claim 4: chronology preserved within views and part files emitted in chronological order
    // (concatenated part contents reproduce the candidate-pool order exactly).
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void ChildPartsPreserveChronologyWithinViewsAndAcrossPartNumbers()
    {
        const int middleCount = 8;
        var plan = DecompositionTestPlans.CreateDecomposablePlan(middleCount);
        var built = BuildDecomposed(plan, DecompositionTestPlans.SplittingBudget(plan));

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        var textsByPath = TextsByRelativePath(built);
        var partPaths = PartMermaidPaths(textsByPath.Keys).ToArray();
        var poolOrder = DecompositionTestPlans.CandidatePoolLabels(middleCount).ToArray();

        var pooledLabels = partPaths.SelectMany(path => MessageLabels(textsByPath[path])).ToArray();
        Assert.Equal(poolOrder, pooledLabels);

        var overviewLabels = MessageLabels(textsByPath[$"{DecompositionTestPlans.BaseFileName}.mmd"]);
        Assert.Equal(DecompositionTestPlans.OverviewLabels, overviewLabels);

        for (int index = 1; index < partPaths.Length; index++)
        {
            var previous = MessageLabels(textsByPath[partPaths[index - 1]]);
            var current = MessageLabels(textsByPath[partPaths[index]]);
            Assert.NotEqual(previous[^1], current[0]);
            Assert.True(
                Array.IndexOf(poolOrder, previous[^1]) < Array.IndexOf(poolOrder, current[0]),
                "Part numbering must follow chronological packing order.");
        }
    }

    // --------------------------------------------------------------------------------------------
    // Claim 5: the overview retains the root seam (leading messages before the first top-level
    // fragment) plus the maximal trailing run of top-level Response-kind outcomes, and nothing
    // from the candidate pool.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void OverviewRetainsRootSeamAndTrailingResponseOutcomes()
    {
        var plan = DecompositionTestPlans.CreateDecomposablePlan();
        var built = BuildDecomposed(plan, DecompositionTestPlans.SplittingBudget(plan));

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        string overviewText = TextsByRelativePath(built)[$"{DecompositionTestPlans.BaseFileName}.mmd"];
        Assert.Contains(DecompositionTestPlans.EntryLabel, overviewText, StringComparison.Ordinal);
        Assert.Contains(DecompositionTestPlans.OutcomeLabel, overviewText, StringComparison.Ordinal);
        Assert.Contains(DecompositionTestPlans.AuditLabel, overviewText, StringComparison.Ordinal);
        foreach (string pooled in DecompositionTestPlans.CandidatePoolLabels(12))
        {
            Assert.DoesNotContain(pooled, overviewText, StringComparison.Ordinal);
        }
    }

    // --------------------------------------------------------------------------------------------
    // Claim 6: a top-level alt fragment moves WHOLE with both arms intact into exactly one child
    // view; no view ever receives one arm without the other (guarded-topology regression guard).
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void TopLevelAltFragmentMovesWholeWithBothArmsIntoOneChild()
    {
        var plan = DecompositionTestPlans.CreateDecomposablePlan();
        var built = BuildDecomposed(plan, DecompositionTestPlans.SplittingBudget(plan));

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        var texts = MermaidTexts(built).ToArray();

        var holders = texts.Where(text =>
            text.Contains(DecompositionTestPlans.ArmTrueLabel, StringComparison.Ordinal)).ToArray();
        var elseHolders = texts.Where(text =>
            text.Contains(DecompositionTestPlans.ArmElseLabel, StringComparison.Ordinal)).ToArray();

        Assert.Single(holders);
        Assert.Single(elseHolders);
        Assert.Equal(holders, elseHolders);
        string fragmentView = holders[0];
        Assert.Contains(
            fragmentView.Split('\n'),
            line => line.TrimStart().StartsWith("alt ", StringComparison.Ordinal));
        Assert.Contains(
            fragmentView.Split('\n'),
            line => line.TrimStart().StartsWith("else ", StringComparison.Ordinal));

        string overview = TextsByRelativePath(built)[$"{DecompositionTestPlans.BaseFileName}.mmd"];
        Assert.DoesNotContain(DecompositionTestPlans.ArmTrueLabel, overview, StringComparison.Ordinal);
        Assert.DoesNotContain(DecompositionTestPlans.ArmElseLabel, overview, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------------------------------------
    // Claim 7: repeated builds are byte-identical, and the decomposition of a document is a pure
    // function of that document — reversing sibling document order changes nothing per document.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void DecompositionIsDeterministicAndIndependentOfSiblingDocumentOrder()
    {
        var plan = DecompositionTestPlans.CreateDecomposablePlan();
        var budget = DecompositionTestPlans.SplittingBudget(plan);
        var oversized = new DocumentSetEntry(
            DecompositionTestPlans.BaseFileName, DecompositionTestPlans.CreateWording(), plan);
        var simple = new DocumentSetEntry(
            "zz-simple-flow", PlanTestFactory.CreateWordingDocument(), PlanTestFactory.CreateDiagramPlan());

        var first = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [oversized, simple], budget, Enabled);
        var second = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [oversized, simple], budget, Enabled);
        var reversed = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [simple, oversized], budget, Enabled);

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.True(reversed.Succeeded, string.Join("; ", reversed.Errors));
        Assert.Equal(TextsByRelativePath(first), TextsByRelativePath(second));
        Assert.Equal(TextsByRelativePath(first), TextsByRelativePath(reversed));
        Assert.True(PartMermaidPaths(TextsByRelativePath(first).Keys).Count() >= 2);
    }

    // --------------------------------------------------------------------------------------------
    // Claim 8: legacy topology-empty plans (empty sequence) fall back to the existing conservative
    // truncation path when decomposition is enabled — success, DP-MERMAID-TRUNCATED, no crash and
    // no decomposition diagnostic.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void LegacyEmptySequencePlanFallsBackToTruncationInsteadOfDecomposition()
    {
        var plan = PlanTestFactory.CreateDiagramPlan();
        int renderedLength = MermaidRenderer.Render(plan).Length;
        var budget = new DiagramBudget(1024, 4096, 1024, 256, renderedLength - 5);
        var entry = new DocumentSetEntry(
            DecompositionTestPlans.BaseFileName, PlanTestFactory.CreateWordingDocument(), plan);

        var built = DocumentationSetBuilder.Build("profile:v1:test", "fingerprint", [entry], budget, Enabled);

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        Assert.Contains(built.Diagnostics, item => item.Code == "DP-MERMAID-TRUNCATED");
        Assert.DoesNotContain(built.Diagnostics, item => item.Code == "DP-DIAGRAM-DECOMPOSED");
        var mermaid = built.Files.Where(file => file.RelativePath.EndsWith(".mmd", StringComparison.Ordinal)).ToArray();
        Assert.Single(mermaid);
        Assert.True(Encoding.UTF8.GetString(mermaid[0].Content).Length <= budget.MaxMermaidCharacters);
    }

    // --------------------------------------------------------------------------------------------
    // Claim 9: an element whose standalone contribution alone exceeds the budget becomes its own
    // child bucket and is truncated internally by the existing fitter; the overall build succeeds.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void OversizedSingleElementBecomesItsOwnTruncatedChildAndBuildStillSucceeds()
    {
        var plan = DecompositionTestPlans.CreateSingleOversizedFragmentPlan();
        var budget = DecompositionTestPlans.SplittingBudget(plan);
        Assert.True(
            MermaidRenderer.Render(plan).Length > budget.MaxMermaidCharacters,
            "Fixture precondition: the oversized-element plan must exceed the Mermaid budget.");

        var built = BuildDecomposed(plan, budget);

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        Assert.Contains(built.Diagnostics, item => item.Code == "DP-DIAGRAM-DECOMPOSED");
        Assert.Contains(built.Diagnostics, item => item.Code == "DP-MERMAID-TRUNCATED");
        Assert.True(PartMermaidNames(built).Count >= 1);
        Assert.All(MermaidTexts(built), text => Assert.True(text.Length <= budget.MaxMermaidCharacters));
        Assert.All(MermaidTexts(built), text => Assert.Empty(MermaidValidator.Validate(text)));
    }

    // --------------------------------------------------------------------------------------------
    // Claim 10: DP-DIAGRAM-DECOMPOSED appears exactly once, lives on the overview only, keeps a
    // stable identity across reruns, and the overview additionally carries all original
    // diagnostics plus a Continuations section linking every existing child file.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void DecomposedDiagnosticAppearsExactlyOnceWithStableIdentityAndNavigation()
    {
        var plan = DecompositionTestPlans.CreateDecomposablePlan();
        var built = BuildDecomposed(plan, DecompositionTestPlans.SplittingBudget(plan));
        var rebuilt = BuildDecomposed(plan, DecompositionTestPlans.SplittingBudget(plan));

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        var decomposed = built.Diagnostics.Where(item => item.Code == "DP-DIAGRAM-DECOMPOSED").ToArray();
        Assert.Single(decomposed);
        Assert.Equal(
            decomposed[0].Id.Value,
            rebuilt.Diagnostics.Single(item => item.Code == "DP-DIAGRAM-DECOMPOSED").Id.Value);

        string overviewMarkdown = TextsByRelativePath(built)[$"{DecompositionTestPlans.BaseFileName}.md"];
        Assert.Contains("DP-DIAGRAM-DECOMPOSED", overviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("Continuations", overviewMarkdown, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(overviewMarkdown, "DP-TEST-ORIGINAL"));

        var partMarkdownPaths = TextsByRelativePath(built).Keys
            .Where(path => Regex.IsMatch(path, @"^get-api-test\.part-\d{3}\.md$"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(partMarkdownPaths.Length >= 2);
        foreach (string partPath in partMarkdownPaths)
        {
            string partMarkdown = TextsByRelativePath(built)[partPath];
            Assert.DoesNotContain("## Diagram diagnostics", partMarkdown, StringComparison.Ordinal);
            Assert.DoesNotContain("DP-DIAGRAM-DECOMPOSED", partMarkdown, StringComparison.Ordinal);
            Assert.Contains(partPath[..^3], overviewMarkdown, StringComparison.Ordinal);
        }

        Assert.Equal(1, built.Diagnostics.Count(item => item.Code == "DP-TEST-ORIGINAL"));
    }

    // --------------------------------------------------------------------------------------------
    // Review finding I23-F2: an overview whose retained content alone (a long root-seam run plus
    // trailing Response outcomes) exceeds the budget falls back to the existing conservative
    // truncation path — the build succeeds with DP-MERMAID-TRUNCATED while decomposition still
    // reports DP-DIAGRAM-DECOMPOSED exactly once.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void OversizedOverviewFallsBackToConservativeTruncationDiagnostic()
    {
        var plan = DecompositionTestPlans.CreateOversizedOverviewPlan();
        int fullLength = MermaidRenderer.Render(plan).Length;

        // The limit sits 300 characters below the full rendering: the fragment child's standalone
        // contribution (~200 characters plus header) stays under it, so only the retained overview
        // is oversized and must be truncated by the fitter safety net.
        var budget = new DiagramBudget(1024, 4096, 1024, 256, fullLength - 300);
        Assert.True(
            MermaidRenderer.Render(plan).Length > budget.MaxMermaidCharacters,
            "Fixture precondition: the plan must exceed the Mermaid budget.");

        var built = BuildDecomposed(plan, budget);

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        Assert.Contains(built.Diagnostics, item => item.Code == "DP-MERMAID-TRUNCATED");
        Assert.Equal(1, built.Diagnostics.Count(item => item.Code == "DP-DIAGRAM-DECOMPOSED"));
        var textsByPath = TextsByRelativePath(built);
        string overviewMermaid = textsByPath[$"{DecompositionTestPlans.BaseFileName}.mmd"];
        Assert.True(
            overviewMermaid.Length <= budget.MaxMermaidCharacters,
            $"Overview mermaid was {overviewMermaid.Length} characters against a {budget.MaxMermaidCharacters} budget.");
        Assert.Single(PartMermaidNames(built));
    }

    // --------------------------------------------------------------------------------------------
    // Review finding I23-F3: a sequence that begins directly with a fragment and carries no
    // trailing Response run yields a structurally valid empty overview while the parts still carry
    // every original message exactly once, with Continuations navigation intact.
    // --------------------------------------------------------------------------------------------
    [Fact]
    public void FragmentOnlySequenceYieldsValidEmptyOverviewAndCompleteParts()
    {
        var plan = DecompositionTestPlans.CreateFragmentFirstPlan();
        int fullLength = MermaidRenderer.Render(plan).Length;

        // The limit sits just below the full rendering: the leading fragment view fits, so the
        // trailing Request message packs into its own chronological child without any truncation.
        var budget = new DiagramBudget(1024, 4096, 1024, 256, fullLength - 20);
        Assert.True(
            MermaidRenderer.Render(plan).Length > budget.MaxMermaidCharacters,
            "Fixture precondition: the plan must exceed the Mermaid budget.");

        var built = BuildDecomposed(plan, budget);

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        var textsByPath = TextsByRelativePath(built);
        Assert.All(
            textsByPath.Where(pair => pair.Key.EndsWith(".mmd", StringComparison.Ordinal)),
            pair => Assert.Empty(MermaidValidator.Validate(pair.Value)));

        string overviewMarkdown = textsByPath[$"{DecompositionTestPlans.BaseFileName}.md"];
        Assert.Contains(DecompositionTestPlans.CreateWording().Title, overviewMarkdown, StringComparison.Ordinal);
        Assert.Contains("Continuations", overviewMarkdown, StringComparison.Ordinal);

        Assert.True(PartMermaidPaths(textsByPath.Keys).Any(), "Expected at least one child part.");
        string allMermaid = string.Join('\n', MermaidTexts(built));
        foreach (string label in DecompositionTestPlans.FragmentFirstLabels)
        {
            Assert.Equal(1, CountOccurrences(allMermaid, label));
        }
    }

    [Fact]
    public void OversizedTopLevelAltRemainsAtomicWhenAChildIsFitted()
    {
        var plan = DecompositionTestPlans.CreateDecomposablePlan(middleCount: 0);
        var budget = new DiagramBudget(1024, 4096, 1024, 256, 220);
        Assert.True(MermaidRenderer.Render(plan).Length > budget.MaxMermaidCharacters);

        var built = BuildDecomposed(plan, budget);

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        var guardedViews = MermaidTexts(built)
            .Where(text => text.Contains(DecompositionTestPlans.ArmTrueLabel, StringComparison.Ordinal)
                || text.Contains(DecompositionTestPlans.ArmElseLabel, StringComparison.Ordinal))
            .ToArray();
        Assert.InRange(guardedViews.Length, 0, 1);
        Assert.All(MermaidTexts(built), text => Assert.True(text.Length <= budget.MaxMermaidCharacters));
        if (guardedViews.Length == 1)
        {
            Assert.Contains(DecompositionTestPlans.ArmTrueLabel, guardedViews[0], StringComparison.Ordinal);
            Assert.Contains(DecompositionTestPlans.ArmElseLabel, guardedViews[0], StringComparison.Ordinal);
            Assert.Contains(guardedViews[0].Split('\n'), line => line.TrimStart().StartsWith("alt ", StringComparison.Ordinal));
            Assert.DoesNotContain(guardedViews[0].Split('\n'), line => line.TrimStart().StartsWith("opt ", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(built.Diagnostics, item => item.Code == "DP-MERMAID-TRUNCATED");
        }
    }

    [Fact]
    public void DuplicateLabelsStillHaveDistinctMessagesInTheDecomposedCover()
    {
        var original = DecompositionTestPlans.CreateDecomposablePlan(8);
        const string duplicate = "Repeated call text with distinct identities";
        var messages = original.Messages.Select((message, index) => index switch
        {
            3 => new DiagramMessage(message.Id, message.Key, "client", "data", duplicate, message.Kind,
                message.Evidence, message.Certainty)
            ,
            4 => new DiagramMessage(message.Id, message.Key, "data", "client", duplicate, message.Kind,
                message.Evidence, message.Certainty),
            _ => message,
        }).ToImmutableArray();
        var plan = new DiagramPlan(original.EntryPoint, original.Profile, original.OperationKey, original.Participants,
            messages, original.Branches, original.DebugProjection, original.Sequence, original.Diagnostics);

        var built = BuildDecomposed(plan, DecompositionTestPlans.SplittingBudget(plan));

        Assert.True(built.Succeeded, string.Join("; ", built.Errors));
        string allMermaid = string.Join('\n', MermaidTexts(built));
        var duplicateMessages = plan.Messages.Where(message => message.Label == duplicate).ToArray();
        Assert.Equal(2, duplicateMessages.Length);
        var duplicateLines = duplicateMessages.Select(message =>
            $"{message.Source}{(message.Kind == DiagramMessageKind.Request ? "->>" : "-->>")}{message.Target}: {message.Label}").ToArray();
        Assert.Equal(2, duplicateLines.Distinct(StringComparer.Ordinal).Count());
        Assert.All(duplicateLines, line => Assert.Equal(1, allMermaid.Split('\n').Count(rendered => rendered.Trim() == line)));
        Assert.Equal(2, allMermaid.Split('\n').Count(line => line.Contains(duplicate, StringComparison.Ordinal)));
    }

    // --------------------------------------------------------------------------------------------

    private static DocumentationSetBuildResult BuildDecomposed(DiagramPlan plan, DiagramBudget budget)
        => DocumentationSetBuilder.Build(
            "profile:v1:test",
            "fingerprint",
            [new DocumentSetEntry(DecompositionTestPlans.BaseFileName, DecompositionTestPlans.CreateWording(), plan)],
            budget,
            Enabled);

    private static Dictionary<string, string> TextsByRelativePath(DocumentationSetBuildResult result)
        => result.Files.ToDictionary(
            file => file.RelativePath,
            file => Encoding.UTF8.GetString(file.Content),
            StringComparer.Ordinal);

    private static IEnumerable<string> MermaidTexts(DocumentationSetBuildResult result)
        => TextsByRelativePath(result)
            .Where(pair => pair.Key.EndsWith(".mmd", StringComparison.Ordinal))
            .Select(pair => pair.Value);

    private static string OverviewMermaidName(DocumentationSetBuildResult result)
        => TextsByRelativePath(result).Keys.Single(path =>
            path.EndsWith(".mmd", StringComparison.Ordinal)
            && !path.Contains(".part-", StringComparison.Ordinal));

    private static List<string> PartMermaidNames(DocumentationSetBuildResult result)
        => PartMermaidPaths(TextsByRelativePath(result).Keys).ToList();

    private static IEnumerable<string> PartMermaidPaths(IEnumerable<string> paths)
        => paths
            .Where(path => Regex.IsMatch(path, @"^get-api-test\.part-\d{3}\.mmd$"))
            .Order(StringComparer.Ordinal);

    private static string[] MessageLabels(string mermaid)
        => mermaid.Split('\n')
            .Where(line => line.Contains("->>", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(": ", StringComparison.Ordinal) + 2)..])
            .ToArray();

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int offset = 0;
        while ((offset = haystack.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }
}

/// <summary>
/// Hand-authored deterministic fixtures for decomposition tests (PlanTestFactory style; kept here
/// so the shared factory file stays untouched). Three participants (client, service, data), a
/// root-seam entry message, one top-level alt fragment with two arms, a chronological pool of
/// middle messages, and two trailing top-level Response outcomes.
/// </summary>
internal static class DecompositionTestPlans
{
    internal const string BaseFileName = "get-api-test";

    internal const string EntryLabel = "Entry request GET api-orders";
    internal const string ArmTrueLabel = "Arm true loads reserved row";
    internal const string ArmElseLabel = "Arm else loads archived row";
    internal const string OutcomeLabel = "Outcome response HTTP 200 ok";
    internal const string AuditLabel = "Audit response recorded finally";
    internal const string BehaviorPhraseText = "The documented orders boundary behaves exactly once.";

    internal static string MiddleLabel(int index) => $"Middle step {index:D2} walks the bounded pipeline";

    internal static IReadOnlyList<string> OverviewLabels => [EntryLabel, OutcomeLabel, AuditLabel];

    /// <summary>Candidate-pool labels in exact chronological (sequence) order.</summary>
    internal static IReadOnlyList<string> CandidatePoolLabels(int middleCount)
        => [ArmTrueLabel, ArmElseLabel, .. Enumerable.Range(1, middleCount).Select(MiddleLabel)];

    internal static IReadOnlyList<string> AllLabels(int middleCount)
        => [EntryLabel, .. CandidatePoolLabels(middleCount), OutcomeLabel, AuditLabel];

    internal static WordingDocument CreateWording()
        => new(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            "Decomposition target flow",
            [
                new WordingPhrase(
                    new WordingPhraseId("wording-phrase:v1:test:decomposition-behavior"),
                    "behavior",
                    WordingPhraseKind.Statement,
                    BehaviorPhraseText,
                    [SourceEvidence("decomposition-behavior")],
                    CertaintyLevel.Exact),
            ],
            "wording-document:v1:test:decomposition");

    internal static DiagramPlan CreateDecomposablePlan(int middleCount = 12)
    {
        ImmutableArray<EvidenceRef> evidence = [SourceEvidence("decomposition")];

        var client = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:client"),
            "client", "Client", DiagramParticipantKind.Client, evidence, CertaintyLevel.Exact);
        var service = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:service"),
            "service", "OrderService", DiagramParticipantKind.Service, evidence, CertaintyLevel.Exact);
        var data = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:data"),
            "data", "ReservationStore", DiagramParticipantKind.Data, evidence, CertaintyLevel.Exact);

        var entry = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:entry"), "m:entry",
            "client", "service", EntryLabel, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var armTrue = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:arm-true"), "m:arm-true",
            "service", "data", ArmTrueLabel, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var armElse = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:arm-else"), "m:arm-else",
            "service", "data", ArmElseLabel, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var middles = Enumerable.Range(1, middleCount)
            .Select(index => new DiagramMessage(
                new DiagramPlanElementId($"diagram-element:v1:message:mid-{index:D2}"), $"m:mid-{index:D2}",
                "service", "data", MiddleLabel(index), DiagramMessageKind.Request, evidence, CertaintyLevel.Exact))
            .ToArray();
        var outcome = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:outcome"), "m:outcome",
            "service", "client", OutcomeLabel, DiagramMessageKind.Response, evidence, CertaintyLevel.Exact);
        var audit = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:audit"), "m:audit",
            "service", "client", AuditLabel, DiagramMessageKind.Response, evidence, CertaintyLevel.Exact);

        var trueArm = new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:true"), "decision:true",
            "when the reservation exists", isElse: false,
            [armTrue.Id], [], evidence, CertaintyLevel.Exact);
        var elseArm = new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:false"), "decision:false",
            "otherwise", isElse: true,
            [armElse.Id], [], evidence, CertaintyLevel.Exact);
        var decision = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:decision"), "decision:reservation-row",
            "reservation row decision", DiagramFragmentKind.Alt,
            [trueArm, elseArm], [], [], evidence, CertaintyLevel.Exact);

        var elements = ImmutableList.CreateBuilder<DiagramSequenceElement>();
        elements.Add(DiagramSequenceElement.MessageRef(entry.Id));
        elements.Add(DiagramSequenceElement.Fragment(decision));
        foreach (var middle in middles)
        {
            elements.Add(DiagramSequenceElement.MessageRef(middle.Id));
        }

        elements.Add(DiagramSequenceElement.MessageRef(outcome.Id));
        elements.Add(DiagramSequenceElement.MessageRef(audit.Id));

        var branchEvidence = new[] { SourceEvidence("branch-decomposition") }.ToImmutableArray();
        var branches = ImmutableArray.Create(
            new DiagramBranch(
                new DiagramPlanElementId("diagram-element:v1:branch:success"), "success",
                "success path", DiagramBranchKind.Success,
                ["m:outcome"], branchEvidence, CertaintyLevel.Exact),
            new DiagramBranch(
                new DiagramPlanElementId("diagram-element:v1:branch:failure"), "failure",
                "failure path", DiagramBranchKind.Failure,
                ["m:mid-01"], branchEvidence, CertaintyLevel.Exact));

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            [client, service, data],
            [entry, armTrue, armElse, .. middles, outcome, audit],
            branches,
            "decomposition-debug-projection",
            new DiagramSequence(elements.ToImmutable().ToImmutableArray()),
            ImmutableArray.Create(new DiagramPlanDiagnostic(
                new SeqDoc.Core.Identity.DiagnosticId("diagnostic:v1:test:decomposition-original"),
                "DP-TEST-ORIGINAL",
                "Original planner boundary retained verbatim.",
                "fixture-detail")));
    }

    /// <summary>
    /// A plan whose single guarded element (an opt fragment wrapping one very long message) cannot
    /// fit any child bucket: it becomes its own child and is truncated internally by the fitter.
    /// </summary>
    internal static DiagramPlan CreateSingleOversizedFragmentPlan()
    {
        ImmutableArray<EvidenceRef> evidence = [SourceEvidence("decomposition-oversized")];

        var client = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:client"),
            "client", "Client", DiagramParticipantKind.Client, evidence, CertaintyLevel.Exact);
        var service = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:service"),
            "service", "OrderService", DiagramParticipantKind.Service, evidence, CertaintyLevel.Exact);
        var data = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:data"),
            "data", "ReservationStore", DiagramParticipantKind.Data, evidence, CertaintyLevel.Exact);

        string hugePayload = "Huge guarded payload " + new string('x', 600);
        var entry = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:entry"), "m:entry",
            "client", "service", "Entry request GET api-orders", DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var huge = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:huge"), "m:huge",
            "service", "data", hugePayload, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var outcome = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:outcome"), "m:outcome",
            "service", "client", "Outcome response HTTP 200 ok", DiagramMessageKind.Response, evidence, CertaintyLevel.Exact);

        var guard = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:huge-guard"), "guard:huge",
            "huge guarded lookup", DiagramFragmentKind.Opt,
            [], [huge.Id], [], evidence, CertaintyLevel.Exact);

        var elements = ImmutableArray.Create(
            DiagramSequenceElement.MessageRef(entry.Id),
            DiagramSequenceElement.Fragment(guard),
            DiagramSequenceElement.MessageRef(outcome.Id));

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            [client, service, data],
            [entry, huge, outcome],
            [],
            "decomposition-oversized-debug-projection",
            new DiagramSequence(elements),
            []);
    }

    /// <summary>
    /// A decomposable plan whose retained overview content alone (four long root-seam messages plus
    /// the trailing Response outcomes) exceeds the configured Mermaid budget while a splittable
    /// top-level fragment remains, forcing the fitter safety net to truncate the overview itself.
    /// </summary>
    internal static DiagramPlan CreateOversizedOverviewPlan()
    {
        ImmutableArray<EvidenceRef> evidence = [SourceEvidence("decomposition-seam")];

        var client = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:client"),
            "client", "Client", DiagramParticipantKind.Client, evidence, CertaintyLevel.Exact);
        var service = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:service"),
            "service", "OrderService", DiagramParticipantKind.Service, evidence, CertaintyLevel.Exact);
        var data = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:data"),
            "data", "ReservationStore", DiagramParticipantKind.Data, evidence, CertaintyLevel.Exact);

        var seams = Enumerable.Range(1, 4)
            .Select(index => new DiagramMessage(
                new DiagramPlanElementId($"diagram-element:v1:message:seam-{index:D2}"), $"m:seam-{index:D2}",
                "client", "service",
                $"Opening step {index:D2} " + new string('s', 140),
                DiagramMessageKind.Request, evidence, CertaintyLevel.Exact))
            .ToArray();
        var armTrue = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:seam-arm-true"), "m:seam-arm-true",
            "service", "data", ArmTrueLabel, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var armElse = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:seam-arm-else"), "m:seam-arm-else",
            "service", "data", ArmElseLabel, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var outcome = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:outcome"), "m:outcome",
            "service", "client", OutcomeLabel, DiagramMessageKind.Response, evidence, CertaintyLevel.Exact);
        var audit = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:audit"), "m:audit",
            "service", "client", AuditLabel, DiagramMessageKind.Response, evidence, CertaintyLevel.Exact);

        var trueArm = new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:true"), "decision:true",
            "when the reservation exists", isElse: false,
            [armTrue.Id], [], evidence, CertaintyLevel.Exact);
        var elseArm = new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:false"), "decision:false",
            "otherwise", isElse: true,
            [armElse.Id], [], evidence, CertaintyLevel.Exact);
        var decision = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:decision"), "decision:reservation-row",
            "reservation row decision", DiagramFragmentKind.Alt,
            [trueArm, elseArm], [], [], evidence, CertaintyLevel.Exact);

        var elements = ImmutableList.CreateBuilder<DiagramSequenceElement>();
        foreach (var seam in seams)
        {
            elements.Add(DiagramSequenceElement.MessageRef(seam.Id));
        }

        elements.Add(DiagramSequenceElement.Fragment(decision));
        elements.Add(DiagramSequenceElement.MessageRef(outcome.Id));
        elements.Add(DiagramSequenceElement.MessageRef(audit.Id));

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            [client, service, data],
            [.. seams, armTrue, armElse, outcome, audit],
            [],
            "decomposition-seam-debug-projection",
            new DiagramSequence(elements.ToImmutable().ToImmutableArray()),
            []);
    }

    internal const string LeadTrueLabel = "Lead true scans reserved row";
    internal const string LeadElseLabel = "Lead else scans archived row";
    internal const string FragmentFirstMiddleLabel = "Middle request walks onward";

    internal static IReadOnlyList<string> FragmentFirstLabels
        => [LeadTrueLabel, LeadElseLabel, FragmentFirstMiddleLabel];

    /// <summary>
    /// A plan whose top-level sequence begins directly with a fragment (first fragment at index 0)
    /// and carries no trailing Response run after it — only a trailing Request message — so the
    /// overview view retains zero sequence elements while the parts must still carry every message.
    /// </summary>
    internal static DiagramPlan CreateFragmentFirstPlan()
    {
        ImmutableArray<EvidenceRef> evidence = [SourceEvidence("decomposition-fragment-first")];

        var client = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:client"),
            "client", "Client", DiagramParticipantKind.Client, evidence, CertaintyLevel.Exact);
        var service = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:service"),
            "service", "OrderService", DiagramParticipantKind.Service, evidence, CertaintyLevel.Exact);
        var data = new DiagramParticipant(
            new DiagramPlanElementId("diagram-element:v1:participant:data"),
            "data", "ReservationStore", DiagramParticipantKind.Data, evidence, CertaintyLevel.Exact);

        var leadTrue = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:lead-true"), "m:lead-true",
            "service", "data", LeadTrueLabel, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var leadElse = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:lead-else"), "m:lead-else",
            "service", "data", LeadElseLabel, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);
        var middle = new DiagramMessage(
            new DiagramPlanElementId("diagram-element:v1:message:ff-mid"), "m:ff-mid",
            "service", "data", FragmentFirstMiddleLabel, DiagramMessageKind.Request, evidence, CertaintyLevel.Exact);

        var trueArm = new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:true"), "decision:true",
            "when the row exists", isElse: false,
            [leadTrue.Id], [], evidence, CertaintyLevel.Exact);
        var elseArm = new DiagramAltArm(
            new DiagramPlanElementId("diagram-element:v1:arm:false"), "decision:false",
            "otherwise", isElse: true,
            [leadElse.Id], [], evidence, CertaintyLevel.Exact);
        var decision = new DiagramFragment(
            new DiagramPlanElementId("diagram-element:v1:fragment:lead-decision"), "decision:lead-row",
            "lead row decision", DiagramFragmentKind.Alt,
            [trueArm, elseArm], [], [], evidence, CertaintyLevel.Exact);

        var elements = ImmutableArray.Create(
            DiagramSequenceElement.Fragment(decision),
            DiagramSequenceElement.MessageRef(middle.Id));

        var branchEvidence = new[] { SourceEvidence("branch-fragment-first") }.ToImmutableArray();
        var branches = ImmutableArray.Create(
            new DiagramBranch(
                new DiagramPlanElementId("diagram-element:v1:branch:progress"), "progress",
                "progress path", DiagramBranchKind.Success,
                ["m:ff-mid"], branchEvidence, CertaintyLevel.Exact));

        return new DiagramPlan(
            PlanTestFactory.EntryPoint,
            PlanTestFactory.Profile,
            "GET api/Test",
            [client, service, data],
            [leadTrue, leadElse, middle],
            branches,
            "decomposition-fragment-first-debug-projection",
            new DiagramSequence(elements),
            []);
    }

    /// <summary>
    /// Derives a budget that provably forces a multi-view split for the given plan: the limit sits
    /// one quarter of the way into the message contributions above the fixed header/participant
    /// overhead, so no bucket can hold the whole pool while every single element still fits.
    /// </summary>
    internal static DiagramBudget SplittingBudget(DiagramPlan plan)
    {
        int fullLength = MermaidRenderer.Render(plan).Length;
        var headerOnly = new DiagramPlan(
            plan.EntryPoint, plan.Profile, plan.OperationKey, plan.Participants, [], [], "header-only");
        int overhead = MermaidRenderer.Render(headerOnly).Length;
        int limit = overhead + Math.Max(120, (fullLength - overhead) / 4);
        return new DiagramBudget(1024, 4096, 1024, 256, limit);
    }

    internal static EvidenceRef SourceEvidence(string artifact)
        => new(
            new EvidenceId($"evidence:v1:{artifact}"),
            EvidenceKind.Source,
            artifact,
            new SourceRange(
                new DocumentId("document:v1:test"),
                new SourcePosition(1, 0),
                new SourcePosition(1, 10)),
            "test-symbol",
            null,
            CertaintyLevel.Exact);
}
