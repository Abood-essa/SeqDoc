using SeqDoc.Analysis.Behavior;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Scenarios;
using SeqDoc.Application.Analysis;
using SeqDoc.Application.Documentation;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ScenarioGraph;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.Wording;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using SeqDoc.FrameworkModels.EntityFramework;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.AcceptanceTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BehaviorDocumentationM1WordingGroup
{
    public const string Name = "Translation alpha M1 wording";
}

/// <summary>
/// accepted contract M1 acceptance for the real TicketReservation flows through the complete typed pipeline. The
/// suite proves execution-accurate decision placement (Reserve capacity guards and Update
/// remove/clear/add/save guards: data interactions appear only in continuing nested arms, never in a
/// terminating arm after its Break), typed terminal/arm wording instead of compiler-oriented
/// factory/status phrases in Markdown and Mermaid, and the Get/Cancel/CreatedAtAction regressions.
/// CustomerManagement claim 10 is intentionally NOT duplicated here: the existing
/// <see cref="BehaviorDocumentationLevel2Tests"/> lane already pins the exact SC001 sparse output and
/// repeated/relocated determinism, and the test harness includes that lane in the M1 verification gate.
/// Like the FourFlow acceptance suite, the external TicketReservation lane is skipped when the
/// supplied checkout is absent.
/// </summary>
[Collection(BehaviorDocumentationM1WordingGroup.Name)]
public sealed class BehaviorDocumentationM1WordingTests
{
    private const string ExternalTicketReservationRoot = "samples/Provided/TicketReservation-Solution";
    private const string ExternalTicketReservationTarget = "TicketReservation.Api/TicketReservation.Api.csproj";

    /// <summary>
    /// Claim 8: Reserve capacity queries, the aggregation, the loop-backed Add mutation, and
    /// Save changes appear only inside continuing nested arms after the guards that permit them —
    /// never in a terminating arm that ends in a Break, and never after a Break in the same path.
    /// The renderable guarded interactions (Count Reservations, Add Reservation, Add Ticket, Save
    /// changes) are structurally nested in continuing arms — Add Ticket in the exact own-header loop
    /// body, the rest in guard arms — and none remain top-level. The Mermaid diagram additionally
    /// exposes typed terminal wording, never raw operation IDs, "Terminates"/"Rejoins", or the
    /// compiler-oriented factory/status phrases.
    /// </summary>
    [Fact]
    public async Task ReserveGuardedQueriesMutationsAndSaveNeverAppearAfterTerminalBreak()
    {
        var bundle = await BuildExternalAsyncOrNull();
        if (bundle is null)
        {
            return;
        }

        var reserve = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var plan = DocumentationPlanner.Plan(reserve);

        // The Reserve decision diagram is structured from reviewed topology, never the legacy flat
        // failure/success branch output.
        Assert.NotEmpty(plan.Diagram.Sequence.Fragments);

        // Renderable guarded interactions are structurally nested in a continuing position (their
        // ref lives in exactly one Alt arm or one loop/Opt fragment that never ends in a Break) and
        // never remain top-level.
        foreach (string label in new[] { "Count Reservations", "Add Reservation", "Add Ticket", "Save changes" })
        {
            AssertNestedInContinuingArm(plan.Diagram, label);
        }

        // The pre-guard Event lookup is an unscoped fact before the guards, so it stays visible at
        // the sequence level (correct, not a guarded mutation).
        Assert.Contains(plan.Diagram.Messages, message => message.Label == "Find at most one Event");

        // Every terminating arm ends in exactly one Break and never carries a renderable data
        // interaction; a Break is the last element of its arm, so no data interaction can follow it
        // in the same path.
        var dataRefs = plan.Diagram.Messages
            .Where(message => message.Label is "Find at most one Event" or "Count Reservations" or "Add Reservation" or "Add Ticket" or "Save changes")
            .Select(message => message.Id)
            .ToHashSet();
        Assert.NotEmpty(dataRefs);
        foreach (var arm in AllArms(plan.Diagram))
        {
            if (!arm.Fragments.Any(fragment => fragment.Kind == DiagramFragmentKind.Break))
            {
                continue;
            }

            Assert.DoesNotContain(arm.MessageRefs, reference => dataRefs.Contains(reference));
            var breaks = arm.Fragments.Where(fragment => fragment.Kind == DiagramFragmentKind.Break).ToArray();
            Assert.Single(breaks);
        }

        string mermaid = MermaidRenderer.Render(plan.Diagram);
        Assert.DoesNotContain("operation:v1", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Terminates", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Rejoins", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("failure factory carries status", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("success factory carries data", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("status result", mermaid, StringComparison.Ordinal);
        string markdown = MarkdownRenderer.RenderDocument(plan.Wording, plan.Diagram);
        Assert.DoesNotContain("failure factory carries status", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("success factory carries data", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("status result", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Claim 9: Update remove/clear/add/save are guarded the same way (continuing arms only), the
    /// loop-backed Add Ticket is structurally nested in its exact own-header loop body under the
    /// guards (never top-level), and the Get/Cancel/CreatedAtAction regressions stay valid: Get
    /// keeps exact HTTP 200/404 outcome wording, Cancel keeps 404/409/200, and Reserve's
    /// CreatedAtAction keeps its exact HTTP 201 label with the compiler-bound Get link.
    /// </summary>
    [Fact]
    public async Task UpdateGuardedMutationSavePlacementAndGetCancelCreatedAtActionRegressions()
    {
        var bundle = await BuildExternalAsyncOrNull();
        if (bundle is null)
        {
            return;
        }

        var update = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Put);
        var updatePlan = DocumentationPlanner.Plan(update);
        Assert.NotEmpty(updatePlan.Diagram.Sequence.Fragments);

        // Renderable guarded interactions (remove/clear/loop-backed add/save) are structurally
        // nested in continuing arms and never remain top-level.
        foreach (string label in new[] { "Remove Ticket range", "Clear tracked Tickets", "Add Ticket", "Save changes" })
        {
            AssertNestedInContinuingArm(updatePlan.Diagram, label);
        }

        // Every terminating arm ends in exactly one Break and never carries a renderable data
        // interaction; a Break is the last element of its arm, so no data interaction can follow it
        // in the same path.
        var updateDataRefs = updatePlan.Diagram.Messages
            .Where(message => message.Label is "Remove Ticket range" or "Clear tracked Tickets" or "Add Ticket" or "Save changes")
            .Select(message => message.Id)
            .ToHashSet();
        Assert.NotEmpty(updateDataRefs);
        foreach (var arm in AllArms(updatePlan.Diagram))
        {
            if (arm.Fragments.Any(fragment => fragment.Kind == DiagramFragmentKind.Break))
            {
                Assert.DoesNotContain(arm.MessageRefs, reference => updateDataRefs.Contains(reference));
            }
        }

        // Get regression: the accepted lookup flow keeps its exact success/failure outcome wording.
        var get = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Get);
        var getPlan = DocumentationPlanner.Plan(get);
        Assert.Contains(getPlan.Diagram.Messages, message => message.Label.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Contains(getPlan.Diagram.Messages, message => message.Label.Contains("HTTP 404", StringComparison.Ordinal));

        // Cancel regression: the three status arms (404/409/200) remain present.
        var cancel = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Delete);
        var cancelPlan = DocumentationPlanner.Plan(cancel);
        Assert.Contains(cancelPlan.Diagram.Messages, message => message.Label.Contains("HTTP 404", StringComparison.Ordinal));
        Assert.Contains(cancelPlan.Diagram.Messages, message => message.Label.Contains("HTTP 409", StringComparison.Ordinal));
        Assert.Contains(cancelPlan.Diagram.Messages, message => message.Label.Contains("HTTP 200", StringComparison.Ordinal));

        // CreatedAtAction regression: exact HTTP 201 with the compiler-bound Get route link.
        var reserve = Assert.Single(bundle.Graphs.Graphs, graph => graph.HttpMethod == HttpMethodKind.Post);
        var reservePlan = DocumentationPlanner.Plan(reserve);
        Assert.Contains(
            reservePlan.Diagram.Messages,
            message => message.Label == "CreatedAtAction -> HTTP 201 links to GET api/Reservations/{id:guid}");

        // Rendered Update/Cancel/Reserve output never exposes the compiler factory/status phrases.
        foreach (var plan in new[] { updatePlan, cancelPlan, reservePlan })
        {
            string rendered = MarkdownRenderer.RenderDocument(plan.Wording, plan.Diagram)
                + "\n" + MermaidRenderer.Render(plan.Diagram);
            Assert.DoesNotContain("failure factory carries status", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("success factory carries data", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("status result", rendered, StringComparison.Ordinal);
        }
    }

    private sealed record M1Bundle(ScenarioGraphSet Graphs, ProfileAnalysisExtraction Extraction, BehaviorSnapshot Behavior);

    private static async Task<M1Bundle?> BuildExternalAsyncOrNull()
    {
        string target = Path.Combine(ExternalTicketReservationRoot, ExternalTicketReservationTarget.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target))
        {
            return null;
        }

        var profile = CompilationProfile.Create(ExternalTicketReservationTarget, "Release", "net10.0");
        return await BuildAsync(ExternalTicketReservationRoot, target, profile);
    }

    private static async Task<M1Bundle> BuildAsync(string root, string target, CompilationProfile profile)
    {
        var extraction = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, target, profile),
            CancellationToken.None);
        Assert.True(
            extraction.IsSuccess,
            string.Join(Environment.NewLine, extraction.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var analysis = await new BehaviorAnalyzer().AnalyzeAsync(
            new BehaviorAnalysisRequest(extraction.Value!.ProgramIndex, extraction.Value.BehaviorInput),
            CancellationToken.None);
        Assert.True(
            analysis.IsSuccess,
            string.Join(Environment.NewLine, analysis.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));

        var host = new FrameworkModelHost(
        [
            new AspNetCoreControllerModel(),
            new EntityFrameworkQueryModel(),
        ]);
        var framework = await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(profile, extraction.Value.ProgramIndex),
                new FrameworkAnalysisContext(profile, extraction.Value.ProgramIndex),
                extraction.Value.Operations,
                extraction.Value.Symbols),
            CancellationToken.None);

        var graphs = ScenarioGraphBuilder.Build(new ScenarioAnalysisRequest(
            profile,
            extraction.Value.ProgramIndex,
            analysis.Value!,
            framework,
            extraction.Value.SemanticFacts,
            extraction.Value.DependencyInjectionFacts,
            extraction.Value.StructuralResultFacts,
            extraction.Value.NonGetSemanticFacts));
        return new M1Bundle(graphs, extraction.Value, analysis.Value!);
    }

    private static IEnumerable<DiagramAltArm> AllArms(DiagramPlan plan)
    {
        foreach (var element in plan.Sequence.Elements)
        {
            if (element.IsFragment)
            {
                foreach (var arm in AllArms(element.NestedFragment!))
                {
                    yield return arm;
                }
            }
        }
    }

    private static IEnumerable<DiagramAltArm> AllArms(DiagramFragment fragment)
    {
        foreach (var arm in fragment.Arms)
        {
            yield return arm;
            foreach (var nested in arm.Fragments)
            {
                foreach (var item in AllArms(nested))
                {
                    yield return item;
                }
            }
        }

        foreach (var nested in fragment.Fragments)
        {
            foreach (var item in AllArms(nested))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Proves one renderable guarded interaction is structurally nested in a continuing position:
    /// its message is planned, its reference is never a top-level sequence reference, it is claimed
    /// by exactly one fragment-tree host (a continuing Alt arm for arm-hosted messages, or a loop/Opt
    /// fragment for an exact own-header loop body), and that host is never a Break. A top-level,
    /// Break-arm, or duplicated placement fails this assertion so a false-continuation overclaim
    /// cannot pass.
    /// </summary>
    private static void AssertNestedInContinuingArm(DiagramPlan plan, string label)
    {
        var message = Assert.Single(plan.Messages, candidate => candidate.Label == label);
        Assert.DoesNotContain(plan.Sequence.MessageRefs, reference => reference == message.Id);

        var armHosts = AllArms(plan)
            .Where(arm => arm.MessageRefs.Contains(message.Id))
            .ToArray();
        var fragmentHosts = AllFragments(plan.Sequence.Fragments)
            .Where(fragment => fragment.MessageRefs.Contains(message.Id))
            .ToArray();
        Assert.True(
            armHosts.Length + fragmentHosts.Length == 1,
            $"Message '{label}' must be claimed by exactly one Alt arm or fragment, found {armHosts.Length + fragmentHosts.Length}.");

        foreach (var arm in armHosts)
        {
            Assert.DoesNotContain(arm.Fragments, fragment => fragment.Kind == DiagramFragmentKind.Break);
        }

        foreach (var fragment in fragmentHosts)
        {
            // A fragment-hosted message (loop body) must be nested inside the guarded tree, never a
            // root-level fragment rendered before the guards.
            Assert.DoesNotContain(plan.Sequence.Fragments, root => root.MessageRefs.Contains(message.Id));
            Assert.DoesNotContain(fragment.Arms, arm => arm.Fragments.Any(child => child.Kind == DiagramFragmentKind.Break));
            Assert.DoesNotContain(fragment.Fragments, child => child.Kind == DiagramFragmentKind.Break);
        }
    }

    private static IEnumerable<DiagramFragment> AllFragments(IEnumerable<DiagramFragment> fragments)
    {
        foreach (var fragment in fragments)
        {
            yield return fragment;
            foreach (var arm in fragment.Arms)
            {
                foreach (var nested in AllFragments(arm.Fragments))
                {
                    yield return nested;
                }
            }

            foreach (var nested in AllFragments(fragment.Fragments))
            {
                yield return nested;
            }
        }
    }
}
