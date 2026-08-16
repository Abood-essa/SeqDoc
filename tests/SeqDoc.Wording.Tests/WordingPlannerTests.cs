using SeqDoc.Application.Documentation;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Wording;
using Xunit;

namespace SeqDoc.Wording.Tests;

public sealed class WordingPlannerTests
{
    [Fact]
    public void WordingPhraseRequiresEvidenceAndExplicitCertaintyWithoutPromotion()
    {
        var evidence = ScenarioGraphTestFactory.SourceEvidence("test");

        Assert.Throws<ArgumentException>(() => new WordingPhrase(
            new("wording-phrase:v1:test"),
            "test",
            WordingPhraseKind.Statement,
            "text",
            [],
            CertaintyLevel.Exact));

        Assert.Throws<ArgumentException>(() => new WordingPhrase(
            new("wording-phrase:v1:test"),
            "test",
            WordingPhraseKind.Statement,
            "text",
            [evidence],
            CertaintyLevel.Unknown));

        // A phrase can never be promoted beyond its strongest evidence.
        Assert.Throws<ArgumentException>(() => new WordingPhrase(
            new("wording-phrase:v1:test"),
            "test",
            WordingPhraseKind.Statement,
            "text",
            [ScenarioGraphTestFactory.SourceEvidence("test", CertaintyLevel.Conservative)],
            CertaintyLevel.Exact));

        var phrase = new WordingPhrase(
            new("wording-phrase:v1:test"),
            "test",
            WordingPhraseKind.Statement,
            "text",
            [evidence],
            CertaintyLevel.Exact);
        Assert.NotEmpty(phrase.Evidence);
        Assert.Equal(CertaintyLevel.Exact, phrase.Certainty);
    }

    [Fact]
    public void PlannerRetainsEvidenceCertaintyAndVisibleTechnicalFallbackDeterministically()
    {
        var complete = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCompleteGetGraph());
        Assert.DoesNotContain(complete.Wording.Phrases, phrase => phrase.Kind == WordingPhraseKind.TechnicalFallback);
        foreach (var phrase in complete.Wording.Phrases)
        {
            Assert.NotEmpty(phrase.Evidence);
            Assert.NotEqual(CertaintyLevel.Unknown, phrase.Certainty);
            Assert.Equal(CertaintyLevel.Exact, phrase.Certainty);
        }

        Assert.Contains(complete.Wording.Phrases, phrase => phrase.Text.Contains("HTTP 200", StringComparison.Ordinal));
        Assert.Contains(complete.Wording.Phrases, phrase => phrase.Text.Contains("HTTP 404", StringComparison.Ordinal));

        // Repeated planning of unchanged graphs yields identical phrase identities and texts.
        var repeated = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateCompleteGetGraph());
        Assert.Equal(
            complete.Wording.Phrases.Select(phrase => phrase.Id.Value),
            repeated.Wording.Phrases.Select(phrase => phrase.Id.Value));
        Assert.Equal(
            complete.Wording.Phrases.Select(phrase => phrase.Text),
            repeated.Wording.Phrases.Select(phrase => phrase.Text));

        // The degraded Guid-query graph exposes a visible conservative technical fallback while every
        // phrase still retains evidence and explicit certainty.
        var degraded = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateDegradedGuidQueryGraph());
        var fallback = Assert.Single(degraded.Wording.Phrases, phrase => phrase.Kind == WordingPhraseKind.TechnicalFallback);
        Assert.Contains("conservative", fallback.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CertaintyLevel.Conservative, fallback.Certainty);
        Assert.NotEmpty(fallback.Evidence);
        foreach (var phrase in degraded.Wording.Phrases)
        {
            Assert.NotEmpty(phrase.Evidence);
            Assert.NotEqual(CertaintyLevel.Unknown, phrase.Certainty);
        }

        // Technical fallback text never contains invented business meaning.
        Assert.DoesNotContain("TicketReservation", fallback.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sc005FallbackCitesDegradedQueryEvidenceNotEntryEvidence()
    {
        // Regression: every technical fallback must be grounded in the evidence of the specific
        // degraded node (here the entity query), never in unrelated entry-point evidence.
        var degraded = DocumentationPlanner.Plan(ScenarioGraphTestFactory.CreateDegradedGuidQueryGraph());
        var fallback = Assert.Single(
            degraded.Wording.Phrases,
            phrase => phrase.Key == "fallback:SC005");

        Assert.Equal("fallback:SC005", fallback.Key, StringComparer.Ordinal);
        Assert.Contains(fallback.Evidence, evidence => evidence.Id.Value == "evidence:v1:ef-query");
        Assert.DoesNotContain(fallback.Evidence, evidence => evidence.Id.Value == "evidence:v1:entry-point");
    }
}
