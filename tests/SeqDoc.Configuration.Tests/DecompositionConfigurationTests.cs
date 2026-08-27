using SeqDoc.Application.Analysis;
using SeqDoc.Configuration;
using Xunit;

namespace SeqDoc.Configuration.Tests;

// I23 coverage for claims 12-14: the optional resolved decomposition flag.
// The resolved member is additive so existing positional constructions remain compatible:
// ResolvedPassAConfiguration (additive; existing positional constructions keep compiling):
//
//   public ResolvedConfigurationValue<bool> DecompositionEnabled { get; init; }
//     // default: new(false, ConfigurationProvenance.Default)
//
// YAML contract: `$.diagrams.decomposition` is a strict boolean (true/false); absent resolves to
// Default false; a non-boolean scalar is rejected with SD3003 at path `$.diagrams.decomposition`;
// the diagrams allowlist stays closed so unknown sibling keys are still rejected after the key is
// added.
public sealed class DecompositionConfigurationTests
{
    // Claim 12a: explicit true/false parse with ConfigurationFile provenance.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DecompositionFlagParsesWithConfigurationFileProvenance(bool flag)
    {
        var result = await ResolveYamlAsync($"schemaVersion: 1\ndiagrams:\n  maxParticipants: 8\n  decomposition: {flag.ToString().ToLowerInvariant()}\n");

        var value = Assert.IsType<ResolvedPassAConfiguration>(result.Value);
        Assert.Equal(flag, value.DecompositionEnabled.Value);
        Assert.Equal(ConfigurationProvenance.ConfigurationFile, value.DecompositionEnabled.Provenance);
    }

    // Claim 12b: absent key resolves Default false, and prior positional constructions of the
    // resolved configuration keep compiling and observe the same expected defaults.
    [Fact]
    public async Task AbsentDecompositionResolvesDefaultFalseAndPriorConstructionsKeepCompiling()
    {
        var withoutSection = await ResolveYamlAsync("schemaVersion: 1\ndiagrams:\n  maxParticipants: 8\n");
        var valueWithoutKey = Assert.IsType<ResolvedPassAConfiguration>(withoutSection.Value);
        Assert.False(valueWithoutKey.DecompositionEnabled.Value);
        Assert.Equal(ConfigurationProvenance.Default, valueWithoutKey.DecompositionEnabled.Provenance);

        var withoutDiagramsSection = await ResolveYamlAsync("schemaVersion: 1\n");
        var valueWithoutSection = Assert.IsType<ResolvedPassAConfiguration>(withoutDiagramsSection.Value);
        Assert.False(valueWithoutSection.DecompositionEnabled.Value);
        Assert.Equal(ConfigurationProvenance.Default, valueWithoutSection.DecompositionEnabled.Provenance);

        var priorShape = new ResolvedPassAConfiguration(
            new("Release", ConfigurationProvenance.Default),
            new(null, ConfigurationProvenance.Default),
            new(null, ConfigurationProvenance.Default),
            new(1, ConfigurationProvenance.Default),
            new("metadata-only", ConfigurationProvenance.Default),
            new("offline", ConfigurationProvenance.Default),
            new(System.Collections.Immutable.ImmutableSortedSet<string>.Empty, ConfigurationProvenance.Default),
            System.Collections.Immutable.ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>>.Empty,
            System.Collections.Immutable.ImmutableSortedDictionary<string, ResolvedConfigurationValue<string>>.Empty);
        Assert.False(priorShape.DecompositionEnabled.Value);
        Assert.Equal(ConfigurationProvenance.Default, priorShape.DecompositionEnabled.Provenance);
    }

    // Claim 13: non-boolean scalars are rejected at their exact YAML path.
    [Theory]
    [InlineData("maybe")]
    [InlineData("42")]
    public async Task NonBooleanDecompositionIsRejectedAtItsYamlPath(string scalar)
    {
        var result = await ResolveYamlAsync($"schemaVersion: 1\ndiagrams:\n  decomposition: {scalar}\n");

        AssertConfigurationFailure(result, "SD3003", "$.diagrams.decomposition");
    }

    // Claim 14: adding the decomposition key must not loosen the diagrams schema — an unknown
    // sibling key remains rejected (distinct from the pre-existing maxMessages alias guard).
    [Fact]
    public async Task UnknownSiblingKeyUnderDiagramsRemainsRejected()
    {
        var result = await ResolveYamlAsync("schemaVersion: 1\ndiagrams:\n  decomposition: true\n  decompose: true\n");

        AssertConfigurationFailure(result, "SD3003", "$.diagrams.decompose");
    }

    // --------------------------------------------------------------------------------------------

    private static async Task<ApplicationResult<ResolvedPassAConfiguration>> ResolveYamlAsync(string yaml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"seqdoc-decomposition-config-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(path, yaml);
        try
        {
            return await new YamlConfigurationResolver().ResolveAsync(
                new ConfigurationResolutionRequest(path), CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertConfigurationFailure(
        ApplicationResult<ResolvedPassAConfiguration> result, string code, string location)
    {
        Assert.Equal(ApplicationOutcome.InvalidInput, result.Outcome);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(location, diagnostic.Location.Description);
    }
}
