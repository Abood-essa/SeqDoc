using SeqDoc.Application.Documentation;
using SeqDoc.Core.DiagramPlan;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Semantics;
using SeqDoc.Rendering.Markdown;
using Xunit;

namespace SeqDoc.Wording.Tests;

/// <summary>
/// accepted contract contract coverage for typed decision/result wording. These pure tests define the contract the
/// DocumentationPlanner must implement from typed presentation facts only: result phrases and
/// diagram messages render conservative typed behavior ("Return Not Found", "Return Conflict",
/// "Return validation failure", "Return success data", "Return a failure status") instead of the
/// compiler-oriented details the builder emits today ("failure factory carries status",
/// "success factory carries data", "status result of {type}"); primary fragment/arm/Break labels
/// use exact typed terminal wording or the sentence-case technical labels "Condition"/"Continue"
/// and never expose raw operation IDs, "Terminates", or "Rejoins"; poisoned Detail strings never
/// override typed wording or element identities; and phrase/fragment evidence keeps every typed
/// support fact with certainty degraded to the weakest contributor.
///
/// The result nodes reference <c>ScenarioNodePresentation.ResultFactoryKind</c>, the typed
/// structural-result presentation input accepted contract requires but the current contract does not yet expose;
/// until that field and the planner wording exist this file cannot compile, and every failure is
/// the intentionally absent accepted contract wording contract, not test setup (all graph shapes compile against
/// the reviewed accepted contract/accepted contract topology and presentation contracts).
/// </summary>
public sealed class DecisionWordingTests
{
    /// <summary>CR-2 formatter equivalence partitions: operands, constants, precedence, and escaping.</summary>
    [Theory]
    [InlineData("null/member", "reservation is null")]
    [InlineData("enum/string/char/constants", "status == Cancelled && name == \"a\\n\\\"b\" && marker == '\\n' && count == 2")]
    [InlineData("arithmetic precedence", "requestedCount + 1 * pageSize > remainingCapacity")]
    [InlineData("logical parentheses", "ready || (enabled && count > 0)")]
    [InlineData("negation", "!(reservation is null)")]
    public void NormalizedPredicateFormatterUsesCompilerPrecedenceAndSafeEscaping(string partition, string expected)
    {
        var expression = partition switch
        {
            "logical parentheses" => new PredicateExpression(
                PredicateExpressionKind.LogicalOr,
                [
                    new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Boolean", displayName: "ready"),
                    new PredicateExpression(
                        PredicateExpressionKind.LogicalAnd,
                        [
                            new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Boolean", displayName: "enabled"),
                            new PredicateExpression(
                                PredicateExpressionKind.Comparison,
                                [
                                    new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Int32", displayName: "count"),
                                    new PredicateExpression(PredicateExpressionKind.NumericConstant, [], "System.Int32", constantValue: "0"),
                                ],
                                "System.Boolean",
                                PredicateComparisonOperatorKind.GreaterThan),
                        ],
                        "System.Boolean"),
                ],
                "System.Boolean"),
            _ => FragmentScenarioTestFactory.PredicateWordingTestFactory.Create(partition),
        };

        Assert.Equal(expected, PredicateWordingFormatter.Format(expression));
    }

    /// <summary>CR-2 polarity partitions: exact comparison complements, conservative unsupported fallback, and subordinate marker.</summary>
    [Theory]
    [InlineData("Equal", "status != Cancelled")]
    [InlineData("NotEqual", "status == Cancelled")]
    [InlineData("LessThan", "count >= 2")]
    [InlineData("LessThanOrEqual", "count > 2")]
    [InlineData("GreaterThan", "count <= 2")]
    [InlineData("GreaterThanOrEqual", "count < 2")]
    public void SafeComparisonComplementIsExact(string comparison, string expected)
    {
        var expression = comparison switch
        {
            "LessThan" or "LessThanOrEqual" or "GreaterThan" or "GreaterThanOrEqual" =>
                new PredicateExpression(
                    PredicateExpressionKind.Comparison,
                    [
                        new PredicateExpression(PredicateExpressionKind.SymbolValue, [], "System.Int32", displayName: "count"),
                        new PredicateExpression(PredicateExpressionKind.NumericConstant, [], "System.Int32", constantValue: "2"),
                    ],
                    "System.Boolean",
                    Enum.Parse<PredicateComparisonOperatorKind>(comparison)),
            _ => FragmentScenarioTestFactory.PredicateWordingTestFactory.CreateComparison(comparison),
        };

        Assert.Equal(expected, PredicateWordingFormatter.FormatComplement(expression));
    }

    [Fact]
    public void GroupedAndUnsupportedPolarityDegradeWithoutGenericControlLabels()
    {
        Assert.Equal("Otherwise", PredicateWordingFormatter.FormatComplement(FragmentScenarioTestFactory.PredicateWordingTestFactory.CreateGrouped()));
        string unsupported = PredicateWordingFormatter.Format(FragmentScenarioTestFactory.PredicateWordingTestFactory.CreateUnsupported());
        Assert.Equal("typed predicate unavailable", unsupported);
        Assert.DoesNotContain("Condition", unsupported, StringComparison.Ordinal);
        Assert.Equal("Otherwise", PredicateWordingFormatter.FormatSubordinate());
    }

    [Fact]
    public void RepeatedPredicateFormattingIsByteDeterministic()
    {
        var expression = FragmentScenarioTestFactory.PredicateWordingTestFactory.Create("logical parentheses");
        Assert.Equal(PredicateWordingFormatter.Format(expression), PredicateWordingFormatter.Format(expression));
    }

    [Theory]
    [InlineData("backticks in string", "a`b```c")]
    [InlineData("backticks in char", "`")]
    public void MarkdownNeutralizesBackticksInsideTheSingleMermaidFence(string partition, string _)
    {
        var graph = FragmentScenarioTestFactory.CreateBothMaterialAltGraph(
            predicateRole: SeqDoc.Core.ScenarioGraph.ScenarioPredicateWordingRole.Owner,
            predicatePartition: partition);

        string markdown = MarkdownRenderer.RenderDocument(
            DocumentationPlanner.Plan(graph).Wording,
            DocumentationPlanner.Plan(graph).Diagram);
        string[] lines = markdown.Split('\n');

        Assert.Equal(2, lines.Count(line => line.StartsWith("```", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            '`',
            string.Join('\n', lines.Where(line => !line.StartsWith("```", StringComparison.Ordinal))));
        Assert.Contains("\\u0060", markdown, StringComparison.Ordinal);
        Assert.Contains("reservation", markdown, StringComparison.Ordinal);
    }
    /// <summary>
    /// Claim 1: exact structural-result factory kinds render conservative typed wording for both the
    /// wording phrase and the diagram message, never the detail-driven factory text or generic result
    /// type. The success/data path is invariant across the theory.
    /// </summary>
    [Theory]
    [InlineData(StructuralResultFactoryKind.NotFound, "Return Not Found", HttpOutcomeHelperKind.NotFound, 404)]
    [InlineData(StructuralResultFactoryKind.Conflict, "Return Conflict", HttpOutcomeHelperKind.Conflict, 409)]
    [InlineData(StructuralResultFactoryKind.ValidationError, "Return validation failure", HttpOutcomeHelperKind.BadRequest, 400)]
    public void TypedStructuralResultFactoryKindWordingTheory(
        StructuralResultFactoryKind failureFactoryKind,
        string expectedFailureWording,
        HttpOutcomeHelperKind failureHelperKind,
        int failureStatusCode)
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateTypedResultDecisionGraph(
                failureFactoryKind: failureFactoryKind,
                failureHelperKind: failureHelperKind,
                failureStatusCode: failureStatusCode)));

        var failurePhrase = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "result-failure");
        Assert.Contains(expectedFailureWording, failurePhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("failure result with status", failurePhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("GadgetResult", failurePhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme.Models.Gadget", failurePhrase.Text, StringComparison.Ordinal);

        var successPhrase = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "result-success");
        Assert.Contains("Return success data", successPhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("success result with data", successPhrase.Text, StringComparison.Ordinal);

        // Diagram messages are typed labels too; the compiler-oriented edge details never leak.
        Assert.Contains(plan.Diagram.Messages, message => message.Label.Contains(expectedFailureWording, StringComparison.Ordinal));
        Assert.Contains(plan.Diagram.Messages, message => message.Label.Contains("Return success data", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("failure factory carries status", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("success factory carries data", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("GadgetResult", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("status result", StringComparison.Ordinal));
    }

    /// <summary>
    /// Claim 2: an unknown/custom factory kind (the closed-vocabulary value the compiler collector
    /// returns for unrecognized factory names) falls back conservatively: "Return a failure status"
    /// never invents NotFound/Conflict/ValidationError meaning, while the success side keeps the
    /// neutral "Return success data".
    /// </summary>
    [Fact]
    public void UnknownCustomResultKindsFallBackConservatively()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateUnknownResultDecisionGraph()));

        var failurePhrase = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "result-failure");
        Assert.Contains("Return a failure status", failurePhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("NotFound", failurePhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Conflict", failurePhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("failure result with status", failurePhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("GadgetResult", failurePhrase.Text, StringComparison.Ordinal);

        var successPhrase = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "result-success");
        Assert.Contains("Return success data", successPhrase.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("success result with data", successPhrase.Text, StringComparison.Ordinal);

        Assert.Contains(plan.Diagram.Messages, message => message.Label.Contains("Return a failure status", StringComparison.Ordinal));
        Assert.Contains(plan.Diagram.Messages, message => message.Label.Contains("Return success data", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("failure factory carries status", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("success factory carries data", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Diagram.Messages, message => message.Label.Contains("status result", StringComparison.Ordinal));
    }

    /// <summary>
    /// Claim 3a: a terminating arm with one unique typed terminal result uses the exact typed
    /// factory wording on the arm and on its Break; the rejoining arm uses "Continue" and the
    /// fragment label is the sentence-case technical "Condition".
    /// </summary>
    [Fact]
    public void TerminatingArmAndBreakUseExactTypedTerminalWording()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateTypedResultDecisionGraph())).Diagram;

        var alt = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, alt.Kind);
        Assert.Equal("reservation is null", alt.Label, StringComparer.Ordinal);

        var terminatingArm = alt.Arms[0];
        var continuingArm = alt.Arms[1];
        Assert.Equal("Return Not Found", terminatingArm.Label, StringComparer.Ordinal);
        Assert.Equal("reservation != null", continuingArm.Label, StringComparer.Ordinal);

        var breakFragment = Assert.Single(terminatingArm.Fragments);
        Assert.Equal(DiagramFragmentKind.Break, breakFragment.Kind);
        Assert.Equal("Return Not Found", breakFragment.Label, StringComparer.Ordinal);
    }

    /// <summary>
    /// Review F1: a TERMINATING success arm must use the typed success wording on the arm and on its
    /// Break. The shared typed-result fixture marks the success arm as rejoining, which hides the
    /// defect; this partition flips the terminals so the success arm is the unique Terminates arm and
    /// every structural result flows through the terminal label. Sending Success through the failure
    /// label would render the contradictory "Return a failure status" here.
    /// </summary>
    [Fact]
    public void TerminatingSuccessArmAndBreakUseReturnSuccessDataWording()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateTypedResultDecisionGraph(successArmTerminates: true))).Diagram;

        var alt = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal(DiagramFragmentKind.Alt, alt.Kind);
        Assert.Equal("reservation is null", alt.Label, StringComparer.Ordinal);

        var successArm = Assert.Single(alt.Arms, arm => arm.Label == "Return success data");
        var breakFragment = Assert.Single(successArm.Fragments);
        Assert.Equal(DiagramFragmentKind.Break, breakFragment.Kind);
        Assert.Equal("Return success data", breakFragment.Label, StringComparer.Ordinal);

        // The success arm is terminating (visual order places it first) and the failure arm rejoins
        // with the exact predicate wording; neither arm ever renders the failure vocabulary.
        Assert.Equal("Return success data", alt.Arms[0].Label, StringComparer.Ordinal);
        Assert.Equal("reservation is null", alt.Arms[1].Label, StringComparer.Ordinal);
        Assert.DoesNotContain(alt.Arms, arm => arm.Label.Contains("Return a failure status", StringComparison.Ordinal));
        Assert.DoesNotContain(alt.Arms, arm => arm.Label.Contains("failure", StringComparison.Ordinal));
    }

    /// <summary>
    /// Claim 3b: when the arm's unique typed terminal is an HTTP outcome rather than a factory-kind
    /// result, the arm/Break label renders the exact status ("Return HTTP 404") while the "status
    /// result" compiler phrase disappears.
    /// </summary>
    [Fact]
    public void OutcomeOnlyTerminatingArmUsesReturnHttpStatusWording()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateStatusSwitchTopologyGraph())).Diagram;

        var alt = Assert.Single(plan.Sequence.Fragments);
        Assert.Equal("reservation is null", alt.Label, StringComparer.Ordinal);
        Assert.Equal("Return HTTP 404", alt.Arms[0].Label, StringComparer.Ordinal);
        Assert.Equal("Return HTTP 404", Assert.Single(alt.Arms[0].Fragments).Label, StringComparer.Ordinal);
        Assert.Equal("reservation != null", alt.Arms[1].Label, StringComparer.Ordinal);
    }

    /// <summary>
    /// Claim 4: primary fragment, arm, and Break labels never expose raw operation IDs, "Terminates",
    /// or "Rejoins" — for both typed-terminal graphs and the accepted contract technical-label graphs (whose arms
    /// carry no typed terminal, so the terminating arm/Break use the sentence-case "Condition").
    /// </summary>
    [Fact]
    public void PrimaryFragmentArmBreakLabelsNeverExposeRawOperationOrTerminalVocabulary()
    {
        var graphs = new[]
        {
            FragmentScenarioTestFactory.WithExactOwnerWording(FragmentScenarioTestFactory.CreateTypedResultDecisionGraph()),
            FragmentScenarioTestFactory.WithExactOwnerWording(FragmentScenarioTestFactory.CreateStatusSwitchTopologyGraph()),
            FragmentScenarioTestFactory.WithExactOwnerWording(FragmentScenarioTestFactory.CreateNestedAbsentLockedGraph()),
        };

        foreach (var graph in graphs)
        {
            var plan = DocumentationPlanner.Plan(graph).Diagram;
            Assert.Equal("reservation is null", Assert.Single(plan.Sequence.Fragments).Label, StringComparer.Ordinal);
            foreach (var fragment in AllFragments(plan))
            {
                Assert.DoesNotContain("operation:v1", fragment.Label, StringComparison.Ordinal);
                Assert.DoesNotContain("Terminates", fragment.Label, StringComparison.Ordinal);
                Assert.DoesNotContain("Rejoins", fragment.Label, StringComparison.Ordinal);
                foreach (var arm in fragment.Arms)
                {
                    Assert.DoesNotContain("operation:v1", arm.Label, StringComparison.Ordinal);
                    Assert.DoesNotContain("Terminates", arm.Label, StringComparison.Ordinal);
                    Assert.DoesNotContain("Rejoins", arm.Label, StringComparison.Ordinal);
                    foreach (var nested in arm.Fragments)
                    {
                        Assert.DoesNotContain("operation:v1", nested.Label, StringComparison.Ordinal);
                        Assert.DoesNotContain("Terminates", nested.Label, StringComparison.Ordinal);
                        Assert.DoesNotContain("Rejoins", nested.Label, StringComparison.Ordinal);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Claim 5: the renderer serializes phrase texts and message labels verbatim, so banning the
    /// compiler-oriented phrases and generic result type leakage at the plan layer guarantees they
    /// are absent from Markdown and Mermaid. The status-switch partition additionally bans the
    /// "status result" phrase and its enum type name.
    /// </summary>
    [Fact]
    public void PhraseTextAndMessageLabelsBanCompilerFactoryStatusAndResultTypeLeakage()
    {
        var typed = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateTypedResultDecisionGraph());
        foreach (string banned in new[]
        {
            "failure factory carries status",
            "success factory carries data",
            "failure result with status",
            "success result with data",
            "GadgetResult",
            "Acme.Models.Gadget",
        })
        {
            Assert.DoesNotContain(typed.Wording.Phrases, phrase => phrase.Text.Contains(banned, StringComparison.Ordinal));
            Assert.DoesNotContain(typed.Diagram.Messages, message => message.Label.Contains(banned, StringComparison.Ordinal));
        }

        var statusSwitch = DocumentationPlanner.Plan(FragmentScenarioTestFactory.CreateStatusSwitchTopologyGraph());
        Assert.DoesNotContain(statusSwitch.Wording.Phrases, phrase => phrase.Text.Contains("status result", StringComparison.Ordinal));
        Assert.DoesNotContain(statusSwitch.Diagram.Messages, message => message.Label.Contains("status result", StringComparison.Ordinal));
        Assert.DoesNotContain(statusSwitch.Wording.Phrases, phrase => phrase.Text.Contains("ServiceResultStatus", StringComparison.Ordinal));
        Assert.DoesNotContain(statusSwitch.Diagram.Messages, message => message.Label.Contains("ServiceResultStatus", StringComparison.Ordinal));
    }

    /// <summary>
    /// Claim 6: poisoned Detail strings never change typed wording (result phrases, message labels,
    /// and the typed outcome label) and never change phrase, message, fragment, arm, or Break
    /// identities. Replanning the same topology with conflicting details must be byte-identical in
    /// identities and typed text.
    /// </summary>
    [Fact]
    public void PoisonedDetailCannotChangeTypedWordingOrElementIdentities()
    {
        var clean = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateTypedResultDecisionGraph()));
        var poisoned = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateTypedResultDecisionGraph(poisoned: true)));

        var cleanFailure = Assert.Single(clean.Wording.Phrases, phrase => phrase.Key == "result-failure");
        var poisonedFailure = Assert.Single(poisoned.Wording.Phrases, phrase => phrase.Key == "result-failure");
        Assert.Equal(cleanFailure.Text, poisonedFailure.Text, StringComparer.Ordinal);
        Assert.Contains("Return Not Found", poisonedFailure.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP 999", poisonedFailure.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("api/Evil", poisonedFailure.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("status result", poisonedFailure.Text, StringComparison.Ordinal);

        // The typed outcome label still comes from the presentation facts, never the poisoned detail.
        Assert.DoesNotContain(poisoned.Diagram.Messages, message => message.Label.Contains("HTTP 999", StringComparison.Ordinal));
        Assert.DoesNotContain(poisoned.Diagram.Messages, message => message.Label.Contains("success factory carries data", StringComparison.Ordinal));
        Assert.DoesNotContain(poisoned.Diagram.Messages, message => message.Label.Contains("status result", StringComparison.Ordinal));
        Assert.Contains(poisoned.Diagram.Messages, message => message.Label.Contains("NotFound -> HTTP 404", StringComparison.Ordinal));

        // Identities are stable semantic anchors, never detail or label text.
        Assert.Equal(
            clean.Wording.Phrases.Select(phrase => phrase.Id.Value),
            poisoned.Wording.Phrases.Select(phrase => phrase.Id.Value));
        Assert.Equal(
            clean.Diagram.Messages.Select(message => message.Id.Value),
            poisoned.Diagram.Messages.Select(message => message.Id.Value));
        Assert.Equal(FragmentIdLines(clean.Diagram), FragmentIdLines(poisoned.Diagram));
    }

    /// <summary>
    /// Claim 7: phrase and fragment evidence keep every typed fact that supports them (the
    /// structural-result fact and the IsSuccess decision for a result phrase; decision, arm,
    /// membership, and terminal support for a fragment), certainty degrades to the weakest
    /// contributor, and typed wording never depends on certainty.
    /// </summary>
    [Fact]
    public void PhraseAndFragmentEvidenceIncludeTypedSupportAndDegradeToWeakest()
    {
        var plan = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateTypedResultDecisionGraph()));
        var failurePhrase = Assert.Single(plan.Wording.Phrases, phrase => phrase.Key == "result-failure");
        Assert.Contains(failurePhrase.Evidence, evidence => evidence.Artifact == "structural-result");
        Assert.Contains(failurePhrase.Evidence, evidence => evidence.Artifact == "decision");
        Assert.Equal(CertaintyLevel.Exact, failurePhrase.Certainty);

        var conservative = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateTypedResultDecisionGraph(
                failureMembershipCertainty: CertaintyLevel.Conservative))).Diagram;
        var alt = Assert.Single(conservative.Sequence.Fragments);
        Assert.Contains(alt.Evidence, evidence => evidence.Artifact == "membership");
        Assert.Equal(CertaintyLevel.Conservative, alt.Certainty);
        Assert.Equal(CertaintyLevel.Conservative, alt.Arms[0].Certainty);
        Assert.Equal(CertaintyLevel.Conservative, Assert.Single(alt.Arms[0].Fragments).Certainty);
        Assert.Equal(CertaintyLevel.Exact, alt.Arms[1].Certainty);

        var degraded = DocumentationPlanner.Plan(FragmentScenarioTestFactory.WithExactOwnerWording(
            FragmentScenarioTestFactory.CreateTypedResultDecisionGraph(
                failureMembershipCertainty: CertaintyLevel.Conservative)));
        var degradedFailure = Assert.Single(degraded.Wording.Phrases, phrase => phrase.Key == "result-failure");
        Assert.Contains("Return Not Found", degradedFailure.Text, StringComparison.Ordinal);
        Assert.Equal(CertaintyLevel.Exact, degradedFailure.Certainty);
    }

    private static IEnumerable<DiagramFragment> AllFragments(DiagramPlan plan)
    {
        foreach (var element in plan.Sequence.Elements)
        {
            if (element.IsFragment)
            {
                foreach (var fragment in AllFragments(element.NestedFragment!))
                {
                    yield return fragment;
                }
            }
        }
    }

    private static IEnumerable<DiagramFragment> AllFragments(DiagramFragment fragment)
    {
        yield return fragment;
        foreach (var arm in fragment.Arms)
        {
            foreach (var nested in arm.Fragments)
            {
                foreach (var item in AllFragments(nested))
                {
                    yield return item;
                }
            }
        }

        foreach (var nested in fragment.Fragments)
        {
            foreach (var item in AllFragments(nested))
            {
                yield return item;
            }
        }
    }

    /// <summary>Canonical depth-first fragment/arm/break ID lines for one plan tree.</summary>
    private static string[] FragmentIdLines(DiagramPlan plan)
    {
        var lines = new List<string>();
        foreach (var element in plan.Sequence.Elements)
        {
            if (element.IsFragment)
            {
                AppendFragmentIdLines(lines, element.NestedFragment!);
            }
        }

        return lines.ToArray();
    }

    private static void AppendFragmentIdLines(List<string> lines, DiagramFragment fragment)
    {
        lines.Add($"fragment {fragment.Id.Value}");
        foreach (var arm in fragment.Arms)
        {
            lines.Add($"arm {arm.Id.Value}");
            foreach (var nested in arm.Fragments)
            {
                AppendFragmentIdLines(lines, nested);
            }
        }

        foreach (var nested in fragment.Fragments)
        {
            AppendFragmentIdLines(lines, nested);
        }
    }
}
