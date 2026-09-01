using SeqDoc.Analysis.Roslyn;
using SeqDoc.Analysis.Roslyn.Semantics;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// Adversarial collector/extractor negatives for the accepted contract non-Get companion facts. Every claim is a
/// false-positive boundary: ordinary direct Get outcomes must never be projected as synthetic status
/// arms, an ambiguous switch arm must fail closed, unrelated collection clears and unsupported
/// Remove/AddRange calls must never become EF mutations, every exact status arm must carry
/// evidence spanning both the switch case label and the helper association, and a discarded outcome
/// helper outside a switch arm must never become a direct terminal outcome. The SD1301 milestone
/// claim is the complementary boundary: identical state-assignment drafts project as one fact while
/// a same-identity group with genuinely conflicting content still fails closed.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class NonGetSemanticCollectorNegativeTests
{
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/FourFlows/FourFlows.csproj";

    /// <summary>
    /// F1: ordinary direct Get outcomes stay on the accepted structural-result path. The extractor
    /// must never synthesize a reserved <c>success</c> status arm for a direct <c>Ok</c>/<c>NotFound</c>
    /// call outside a switch.
    /// </summary>
    [Fact]
    public async Task FourFlowGetDirectOutcomesEmitNoSyntheticSuccessStatusArm()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var getAction = FindMethod(extraction, "GetById");

        Assert.DoesNotContain(
            extraction.NonGetSemanticFacts.StatusSwitchArms,
            arm => arm.Method == getAction && arm.StatusMemberName == "success");
    }

    /// <summary>
    /// F2: a switch arm that reaches two admitted outcome helpers is ambiguous and must fail closed
    /// by producing no status-switch arm fact; the first helper is never selected silently.
    /// </summary>
    [Fact]
    public async Task StatusSwitchAmbiguousArmWithTwoAdmittedHelpersFailsClosed()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var probe = FindMethod(extraction, "AmbiguousSwitchProbe");

        Assert.DoesNotContain(
            extraction.NonGetSemanticFacts.StatusSwitchArms,
            arm => arm.Method == probe);
    }

    /// <summary>
    /// F3: an unrelated local collection Clear yields no EF mutation, and the unsupported DbSet
    /// Remove and AddRange calls are never mislabeled as Add.
    /// </summary>
    [Fact]
    public async Task MutationProbeUnrelatedClearAndUnsupportedRemoveAddRangeProduceNoFacts()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var probe = FindMethod(extraction, "UnsupportedAndUnrelatedProbe");

        Assert.DoesNotContain(
            extraction.NonGetSemanticFacts.EntityFrameworkMutations,
            mutation => mutation.Method == probe);
    }

    /// <summary>
    /// F4: an exact ordinary property assignment remains a generic companion fact and retains its
    /// non-entity target type; a true local-variable assignment and unsupported computed value do not
    /// become state-assignment facts.
    /// </summary>
    [Fact]
    public async Task AssignmentLookalikesDoNotProduceEntityTransitions()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var probe = FindMethod(extraction, "AssignmentLookalikeProbe");

        var assignments = extraction.NonGetSemanticFacts.StateAssignments
            .Where(assignment => assignment.Method == probe)
            .ToArray();

        foreach (var target in new[] { "WidgetDto.Status", "StatusCarrier.Status" })
        {
            var ordinary = Assert.Single(assignments, assignment => assignment.TargetMember.EndsWith(target, StringComparison.Ordinal));
            Assert.Equal("System.String", ordinary.TargetType);
            Assert.Equal(StateAssignmentValueKind.Literal, ordinary.ValueKind);
            Assert.Equal(CertaintyLevel.Exact, ordinary.Certainty);
            Assert.NotEmpty(ordinary.Evidence);
        }

        Assert.DoesNotContain(assignments, assignment => assignment.Value == "changed");
        Assert.DoesNotContain(assignments, assignment => assignment.Value == "computed");
    }

    /// <summary>
    /// F5: the accepted producer slice remains exact at the source boundary: the entity assignment
    /// and mutation facts belong to the named service methods and retain exact evidence/certainty.
    /// </summary>
    [Fact]
    public async Task FourFlowTransitionFactsRemainMethodBoundAndEvidenceBacked()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var reserve = FindServiceMethod(extraction, "ReserveAsync");
        var cancel = FindServiceMethod(extraction, "CancelAsync");

        Assert.Contains(extraction.NonGetSemanticFacts.StateAssignments,
            assignment => assignment.Method == reserve && assignment.TargetMember.EndsWith("Reservation.Status", StringComparison.Ordinal)
                && assignment.Value == "Active");
        Assert.Contains(extraction.NonGetSemanticFacts.StateAssignments,
            assignment => assignment.Method == cancel && assignment.TargetMember.EndsWith("Widget.Status", StringComparison.Ordinal)
                && assignment.Value == "Cancelled");
        var mutations = extraction.NonGetSemanticFacts.EntityFrameworkMutations
            .Where(mutation => mutation.Method is var method && (method == reserve || method == cancel)).ToArray();
        Assert.NotEmpty(mutations);
        Assert.All(mutations, mutation =>
        {
            Assert.Equal(CertaintyLevel.Exact, mutation.Certainty);
            Assert.NotEmpty(mutation.Evidence);
        });
    }

    /// <summary>
    /// F6: every exact status-switch arm carries evidence for both the switch case label (the
    /// status-to-outcome mapping) and the helper invocation that produced the outcome; a lone helper
    /// invocation evidence is insufficient.
    /// </summary>
    [Fact]
    public async Task StatusSwitchArmEvidenceSpansCaseLabelAndHelperAssociation()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var cancel = FindMethod(extraction, "Cancel");

        foreach (var arm in extraction.NonGetSemanticFacts.StatusSwitchArms
                     .Where(arm => arm.Method == cancel)
                     .OrderBy(arm => arm.StatusMemberName, StringComparer.Ordinal))
        {
            Assert.True(
                arm.Evidence.Length >= 2,
                $"Arm '{arm.StatusMemberName}' must carry at least two evidence anchors (case label plus helper association); got [{string.Join(" | ", arm.Evidence.Select(item => item.Artifact))}].");
        }
    }

    /// <summary>
    /// SF1: an admitted outcome helper outside a switch arm is a direct terminal only when
    /// compiler-proven to flow directly to a method return. The probe's admitted status switch is
    /// preceded by a discarded StatusCode(500) invocation; the unused helper must never become a
    /// user-facing outcome, so no DirectTerminalOutcome fact is emitted for the probe.
    /// </summary>
    [Fact]
    public async Task DirectTerminalUnusedHelperOutsideSwitchArmIsNoOutcome()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var probe = FindMethod(extraction, "UnusedTerminalProbe");

        Assert.DoesNotContain(
            extraction.NonGetSemanticFacts.DirectTerminalOutcomes,
            terminal => terminal.Method == probe);
    }

    /// <summary>
    /// SD1301 milestone regression: identical state-assignment drafts that resolve to the same
    /// method/operation/member/type/value/evidence semantics must project as exactly one fact that
    /// keeps the first deterministic source ordinal, while a same-identity group with genuinely
    /// conflicting projected content must still fail closed with the existing conflict exception
    /// (which the extractor surfaces as SD1301). Direct collector calls keep this small test
    /// independent of the nopCommerce repository that exposed the duplicate projection.
    /// </summary>
    [Theory]
    [InlineData(StateAssignmentDraftPartition.IdenticalDrafts)]
    [InlineData(StateAssignmentDraftPartition.ConflictingDrafts)]
    public void StateAssignmentSemanticDraftDeduplicationProjectsOneFactOrFailsClosed(
        StateAssignmentDraftPartition partition)
    {
        var collector = new RoslynNonGetSemanticFactCollector();
        var profile = CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0");
        var method = new MethodId("method:Acme.Orders.OrdersController.SetState");
        var operation = new OperationId("operation:Acme.Orders.OrdersController.SetState:state-assignment:1");
        var evidence = new EvidenceRef(
            new EvidenceId("evidence:Acme.Orders.OrdersController.SetState:state-assignment:1"),
            EvidenceKind.Source,
            "tests/fixtures/BehaviorDocumentation/FourFlows/OrdersController.cs",
            range: null,
            symbol: "Acme.Orders.OrdersController.SetState",
            detail: "Status = OrderStatus.Confirmed",
            CertaintyLevel.Exact);

        collector.AddStateAssignment(
            method,
            operation,
            targetMember: "Acme.Orders.OrdersController.Status",
            targetType: "Acme.Orders.OrderStatus",
            StateAssignmentValueKind.EnumConstant,
            "Acme.Orders.OrderStatus.Confirmed",
            [evidence]);

        if (partition == StateAssignmentDraftPartition.IdenticalDrafts)
        {
            // The nopCommerce exposure visits the same compiler-proven assignment twice under one
            // shared operation identity; every projected field is identical, so the second draft is
            // not a conflict. Exactly one fact must survive and it must keep the first source ordinal.
            collector.AddStateAssignment(
                method,
                operation,
                targetMember: "Acme.Orders.OrdersController.Status",
                targetType: "Acme.Orders.OrderStatus",
                StateAssignmentValueKind.EnumConstant,
                "Acme.Orders.OrderStatus.Confirmed",
                [evidence]);

            var set = collector.Build(profile, "fingerprint:sd1301-deduplication", []);
            var fact = Assert.Single(set.StateAssignments);
            Assert.Equal("Acme.Orders.OrderStatus.Confirmed", fact.Value);
            Assert.Equal(0, fact.SequenceOrdinal);
        }
        else
        {
            // Same identity, genuinely conflicting projected content: the second draft claims a
            // different member type for the same assignment. Deduplication must never select one
            // silently; the conflict keeps failing closed exactly as before.
            collector.AddStateAssignment(
                method,
                operation,
                targetMember: "Acme.Orders.OrdersController.Status",
                targetType: "Acme.Orders.OrderStatusV2",
                StateAssignmentValueKind.EnumConstant,
                "Acme.Orders.OrderStatus.Confirmed",
                [evidence]);

            var exception = Assert.Throws<InvalidOperationException>(
                () => collector.Build(profile, "fingerprint:sd1301-deduplication", []));
            Assert.Contains("Conflicting non-Get semantic-fact drafts", exception.Message, StringComparison.Ordinal);
        }
    }

    public enum StateAssignmentDraftPartition
    {
        IdenticalDrafts,
        ConflictingDrafts,
    }

    private static async Task<ProfileAnalysisExtraction> ExtractSuccessfullyAsync()
    {
        var result = await ExtractFixtureAsync();
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractFixtureAsync()
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0"));
        return await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
    }

    private static MethodId FindMethod(ProfileAnalysisExtraction extraction, string name)
        => Assert.Single(extraction.ProgramIndex.Methods, method => method.Name == name).Id;

    private static MethodId FindServiceMethod(ProfileAnalysisExtraction extraction, string name)
    {
        var service = Assert.Single(extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.FourFlows.Services.WidgetService");
        return Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == name && method.ContainingType == service.Id).Id;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
