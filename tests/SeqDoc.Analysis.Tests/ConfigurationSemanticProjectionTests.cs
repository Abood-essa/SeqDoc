using System.Collections.Immutable;
using System.Diagnostics;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Analysis.Tests;

/// <summary>
/// accepted contract risk-based tests for the new memory-only <see cref="ConfigurationSemanticFactSet"/>. Claims 1
/// through 10 prove the exact compiler-resolved Microsoft <c>ConfigurationBinder.GetValue&lt;bool&gt;</c>
/// read, direct local-to-<c>if</c> condition association, fail-closed lookalike/unsupported shapes,
/// Conservative provider precedence, Conservative checked-in observations, profile-known provenance,
/// secret safety, deterministic identity/debug output, and the pinned CustomerManagement SC001
/// boundary. Review-hardening claims 11 through 13 consolidate the independent regressions: (11)
/// explicit file ownership, link/reparse containment, and per-project checked-in attribution in a
/// multi-project solution; (12) compiler-bound named/static argument resolution, actual-if/ref-out
/// boundaries, and separator-sensitive key rejection; and (13) public impossible-state invariants,
/// fact-set version/profile/array/debug validation, cancellation, and fail-closed appsettings I/O.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class ConfigurationSemanticProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/AdvancedAnalysis/ConfigurationReads/ConfigurationReads.csproj";
    private const string MultiProjectRelativePath = "tests/fixtures/AdvancedAnalysis/ConfigurationMultiProject/ConfigurationMultiProject.slnx";

    /// <summary>
    /// accepted contract F2 ownership inventory for the ConfigurationReads fixture: exactly the repository-
    /// controlled appsettings files that may contribute checked-in observations. The malformed file is
    /// owned too so the fail-closed I/O partition stays meaningful; runtime-created ambient files are
    /// never listed.
    /// </summary>
    private static readonly ImmutableArray<string> ConfigurationReadsOwnedFiles =
    [
        "tests/fixtures/AdvancedAnalysis/ConfigurationReads/appsettings.json",
        "tests/fixtures/AdvancedAnalysis/ConfigurationReads/appsettings.Development.json",
        "tests/fixtures/AdvancedAnalysis/ConfigurationReads/appsettings.Malformed.json",
    ];

    /// <summary>
    /// accepted contract F2 ownership inventory for a relocated git-free checkout: the copied appsettings files are
    /// repository-root-relative file names only. Runtime-created Untracked and Linked files are never
    /// listed, which keeps the ownership negative test meaningful.
    /// </summary>
    private static readonly ImmutableArray<string> RelocatedOwnedConfigurationFiles =
    [
        "appsettings.json",
        "appsettings.Development.json",
        "appsettings.Malformed.json",
    ];

    /// <summary>
    /// Claim 1: exact <c>ConfigurationBinder.GetValue&lt;bool&gt;</c> reads project on both the
    /// <c>IConfiguration</c> and <c>ConfigurationManager</c> receivers with the canonical key, no
    /// invented default, Exact certainty, and non-empty evidence.
    /// </summary>
    [Fact]
    public async Task ExactConfigurationBinderBooleanReadsProjectOnIConfigurationAndConfigurationManager()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConfigurationSemanticFacts;

        var useSqlDatabase = FindMethod(extraction, "UseSqlDatabase");
        var sqlRead = Assert.Single(facts.Reads, fact => fact.Method == useSqlDatabase);
        Assert.Equal("FeatureToggles:UseSqlDatabase", sqlRead.Key);
        Assert.Null(sqlRead.DefaultValue);
        Assert.Equal(CertaintyLevel.Exact, sqlRead.Certainty);
        AssertEvidence(sqlRead.Evidence, sqlRead.Certainty);

        var useAudit = FindMethod(extraction, "UseAudit");
        var auditRead = Assert.Single(facts.Reads, fact => fact.Method == useAudit);
        Assert.Equal("FeatureToggles:UseAudit", auditRead.Key);
        Assert.Equal(CertaintyLevel.Exact, auditRead.Certainty);
        AssertEvidence(auditRead.Evidence, auditRead.Certainty);
    }

    /// <summary>
    /// Claim 2: the canonical constant key and the optional compiler-proven boolean default project
    /// exactly; the no-default shape carries a null default and the explicit-default shape carries the
    /// literal default.
    /// </summary>
    [Theory]
    [InlineData("UseSqlDatabase", "FeatureToggles:UseSqlDatabase", null)]
    [InlineData("UseCacheWithDefault", "FeatureToggles:UseCache", true)]
    public async Task ConstantKeyTypeAndCompilerDefaultProjection(
        string methodName,
        string expectedKey,
        bool? expectedDefault)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var method = FindMethod(extraction, methodName);

        var read = Assert.Single(extraction.ConfigurationSemanticFacts.Reads, fact => fact.Method == method);
        Assert.Equal(expectedKey, read.Key);
        Assert.Equal(expectedDefault, read.DefaultValue);
        Assert.Equal(CertaintyLevel.Exact, read.Certainty);
    }

    /// <summary>
    /// Claim 3: an admitted read that flows through exactly one compiler-bound local directly into an
    /// <c>if</c> boolean condition produces one condition fact anchored to the read operation and to a
    /// real source-backed condition operation in the behavior input, with the explicit true/false
    /// relationship (branch is taken when the read is true).
    /// </summary>
    [Fact]
    public async Task AdmittedReadFlowingThroughSingleLocalToIfConditionAssociatesExactly()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConfigurationSemanticFacts;
        var method = FindMethod(extraction, "UseSqlDatabase");
        var read = Assert.Single(facts.Reads, fact => fact.Method == method);

        var condition = Assert.Single(facts.Conditions, fact => fact.Method == method);
        Assert.Equal(read.Operation, condition.ReadOperation);
        Assert.True(condition.TrueWhenReadTrue);
        Assert.Equal(CertaintyLevel.Exact, condition.Certainty);
        AssertEvidence(condition.Evidence, condition.Certainty);

        // The condition fact anchors to a real compiler operation in the accepted behavior input;
        // it is never synthesized from textual or dataflow guesses.
        var anchored = Assert.Single(
            Assert.Single(extraction.BehaviorInput.Methods, body => body.Method == method).Operations,
            operation => operation.Id == condition.ConditionOperation);
        Assert.True(anchored.IsSourceBacked);
    }

    /// <summary>
    /// Claim 4: same-simple-name lookalike helpers, dynamic/non-constant keys, unsupported
    /// string/int generics, GetSection calls, and custom non-IConfiguration receivers fail closed and
    /// never project a configuration read fact.
    /// </summary>
    [Theory]
    [InlineData("LookalikeHelper")]
    [InlineData("DynamicKey")]
    [InlineData("UnsupportedString")]
    [InlineData("UnsupportedInt")]
    [InlineData("UnsupportedSection")]
    [InlineData("UnsupportedCustomReceiver")]
    public async Task LookalikeAndUnsupportedConfigurationShapesFailClosed(string methodName)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var method = FindMethod(extraction, methodName);

        Assert.DoesNotContain(
            extraction.ConfigurationSemanticFacts.Reads,
            fact => fact.Method == method);
    }

    /// <summary>
    /// Claim 5: the exact read itself is admitted, but a local reassigned before the <c>if</c> or a
    /// local that flows through a helper call never associates a condition fact.
    /// </summary>
    [Theory]
    [InlineData("ReassignedLocal")]
    [InlineData("AmbiguousLocal")]
    public async Task ReassignedOrAmbiguousLocalsKeepTheReadButNeverAssociateACondition(string methodName)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConfigurationSemanticFacts;
        var method = FindMethod(extraction, methodName);

        var read = Assert.Single(facts.Reads, fact => fact.Method == method);
        Assert.Equal("FeatureToggles:UseSqlDatabase", read.Key);
        Assert.Equal(CertaintyLevel.Exact, read.Certainty);
        Assert.DoesNotContain(facts.Conditions, fact => fact.Method == method);
    }

    /// <summary>
    /// Claim 6: the exact WebApplication.CreateBuilder default configuration precedence is projected
    /// only as five ordered provider observations (base JSON, environment JSON, development user
    /// secrets, environment variables, command line). Each stays Conservative; the facts describe
    /// possible later override and never claim a provider is present or an effective value.
    /// </summary>
    [Fact]
    public async Task CreateBuilderStandardProviderPrecedenceIsConservativeObservationNotRuntimeClaim()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var observations = extraction.ConfigurationSemanticFacts.ProviderObservations;

        Assert.Equal(
            [
                StandardConfigurationProviderKind.BaseJson,
                StandardConfigurationProviderKind.EnvironmentJson,
                StandardConfigurationProviderKind.DevelopmentUserSecrets,
                StandardConfigurationProviderKind.EnvironmentVariables,
                StandardConfigurationProviderKind.CommandLine,
            ],
            observations
                .OrderBy(fact => fact.PrecedenceOrdinal)
                .Select(fact => fact.ProviderKind));

        Assert.All(observations, fact => Assert.Equal(CertaintyLevel.Conservative, fact.Certainty));
        Assert.All(observations, fact => AssertEvidence(fact.Evidence, fact.Certainty));
    }

    /// <summary>
    /// Claim 7: only matching boolean keys in the owned appsettings.json and
    /// appsettings.Development.json are observed; every observation is Conservative and explicitly
    /// MayBeOverridden. A checked-in value is never runtime truth.
    /// </summary>
    [Fact]
    public async Task CheckedInBaseAndEnvironmentJsonObservationsStayConservativeAndMayBeOverridden()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var checkedIn = extraction.ConfigurationSemanticFacts.CheckedInValues;

        var baseObservation = Assert.Single(
            checkedIn,
            fact => fact.Key == "FeatureToggles:UseSqlDatabase"
                && fact.SourceFile.EndsWith("appsettings.json", StringComparison.Ordinal));
        var developmentObservation = Assert.Single(
            checkedIn,
            fact => fact.Key == "FeatureToggles:UseSqlDatabase"
                && fact.SourceFile.EndsWith("appsettings.Development.json", StringComparison.Ordinal));
        var cacheObservation = Assert.Single(
            checkedIn,
            fact => fact.Key == "FeatureToggles:UseCache");

        Assert.True(baseObservation.Value);
        Assert.True(developmentObservation.Value);
        Assert.False(cacheObservation.Value);
        Assert.Equal(CertaintyLevel.Conservative, baseObservation.Certainty);
        Assert.Equal(CertaintyLevel.Conservative, developmentObservation.Certainty);
        Assert.Equal(CertaintyLevel.Conservative, cacheObservation.Certainty);
        Assert.True(baseObservation.MayBeOverridden);
        Assert.True(developmentObservation.MayBeOverridden);
        Assert.True(cacheObservation.MayBeOverridden);
        Assert.All(
            checkedIn,
            fact =>
            {
                AssertEvidence(fact.Evidence, fact.Certainty);
                Assert.True(fact.MayBeOverridden);
            });
    }

    /// <summary>
    /// Claim 8: a matching CompilationProfile.AnalysisProperties value that parses exactly as a
    /// boolean becomes an explicit profile-known fact with profile provenance; an unsupported value
    /// fails closed and never projects.
    /// </summary>
    [Theory]
    [InlineData(ProfileValuePartition.ValidBoolean)]
    [InlineData(ProfileValuePartition.NotABoolean)]
    public async Task ProfileKnownBooleanProvenanceWithUnsupportedValuesFailingClosed(ProfileValuePartition partition)
    {
        var (analysisValue, expectFact) = partition switch
        {
            ProfileValuePartition.ValidBoolean => ("true", true),
            ProfileValuePartition.NotABoolean => ("not-a-bool", false),
            _ => throw new ArgumentOutOfRangeException(nameof(partition)),
        };
        var extraction = await ExtractSuccessfullyAsync(analysisValue);
        var matching = extraction.ConfigurationSemanticFacts.ProfileKnownValues
            .Where(fact => fact.Key == "FeatureToggles:UseSqlDatabase")
            .ToArray();

        if (expectFact)
        {
            var fact = Assert.Single(matching);
            Assert.True(fact.Value);
            Assert.NotEqual(CertaintyLevel.Unknown, fact.Certainty);
            AssertEvidence(fact.Evidence, fact.Certainty);
        }
        else
        {
            Assert.Empty(matching);
        }
    }

    /// <summary>
    /// Claim 9: sensitive connection-string/API-key keys and values, and every non-boolean value,
    /// never enter fact payloads or the deterministic debug projection.
    /// </summary>
    [Fact]
    public async Task SensitiveAndNonBooleanValuesNeverEnterFactsOrDebugProjection()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConfigurationSemanticFacts;
        string projection = facts.DebugProjection;
        Assert.NotEmpty(projection);

        string[] forbiddenTokens =
        [
            "Server=localhost",
            "SuperSecret",
            "s3cr3t",
            "ApiKey",
            "DefaultConnection",
            "MaxRetries",
            "Greeting",
        ];
        foreach (string token in forbiddenTokens)
        {
            Assert.DoesNotContain(token, projection, StringComparison.OrdinalIgnoreCase);
        }

        Assert.All(facts.Reads, fact => Assert.DoesNotContain("ConnectionStrings", fact.Key, StringComparison.Ordinal));
        Assert.All(facts.CheckedInValues, fact => Assert.DoesNotContain("ConnectionStrings", fact.Key, StringComparison.Ordinal));
        Assert.All(facts.CheckedInValues, fact => Assert.DoesNotContain("DefaultConnection", fact.SourceFile, StringComparison.Ordinal));
        Assert.All(facts.ProfileKnownValues, fact => Assert.DoesNotContain("ApiKey", fact.Key, StringComparison.Ordinal));

        // Only the matching boolean keys are observed at all; no unrelated raw string/number/object
        // configuration ever becomes a checked-in fact.
        Assert.All(
            facts.CheckedInValues,
            fact => Assert.True(
                fact.Key is "FeatureToggles:UseSqlDatabase" or "FeatureToggles:UseCache",
                $"Unexpected checked-in key '{fact.Key}' leaked into facts."));
    }

    /// <summary>
    /// Claim 10: fact identity and the debug projection are deterministic across repeated construction
    /// and two physically relocated checkout roots, and no absolute checkout path leaks into the
    /// projection.
    /// </summary>
    [Fact]
    public async Task ConfigurationFactsAreDeterministicAcrossRepeatedConstructionAndRelocatedRoots()
    {
        // Repeated construction from the repository fixture must be identical.
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();
        Assert.Equal(
            CollectConfigurationFactIds(first.ConfigurationSemanticFacts),
            CollectConfigurationFactIds(second.ConfigurationSemanticFacts));
        Assert.Equal(first.ConfigurationSemanticFacts.DebugProjection, second.ConfigurationSemanticFacts.DebugProjection);
        Assert.DoesNotContain(FindRepositoryRoot(), first.ConfigurationSemanticFacts.DebugProjection, StringComparison.OrdinalIgnoreCase);

        // Two independent relocated git-free roots analyzed with the identical relative profile must
        // produce identical fact identities and debug bytes, with no root path leakage.
        var source = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "AdvancedAnalysis", "ConfigurationReads");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-config-relocation-{Guid.NewGuid():N}");
        var firstRoot = Path.Combine(temporaryDirectory, "first");
        var secondRoot = Path.Combine(temporaryDirectory, "second");
        try
        {
            CopyFixture(source, firstRoot);
            CopyFixture(source, secondRoot);
            await RestoreAsync(firstRoot);
            await RestoreAsync(secondRoot);

            var relocatedFirst = await ExtractRelocatedAsync(firstRoot);
            var relocatedSecond = await ExtractRelocatedAsync(secondRoot);

            Assert.Equal(
                CollectConfigurationFactIds(relocatedFirst.ConfigurationSemanticFacts),
                CollectConfigurationFactIds(relocatedSecond.ConfigurationSemanticFacts));
            Assert.Equal(relocatedFirst.ConfigurationSemanticFacts.DebugProjection, relocatedSecond.ConfigurationSemanticFacts.DebugProjection);
            Assert.DoesNotContain(firstRoot, relocatedFirst.ConfigurationSemanticFacts.DebugProjection, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secondRoot, relocatedSecond.ConfigurationSemanticFacts.DebugProjection, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Claim 11 (review regression): a multi-project solution reading the same safe boolean key with
    /// opposite owned appsettings values attributes each checked-in observation to its read-owning
    /// project. The first-project fallback must never replace the second project's observation.
    /// </summary>
    [Fact]
    public async Task MultiProjectCheckedInObservationsAreAttributedPerReadOwningProject()
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, MultiProjectRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(MultiProjectRelativePath, "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles:
            [
                "tests/fixtures/AdvancedAnalysis/ConfigurationMultiProject/Alpha/appsettings.json",
                "tests/fixtures/AdvancedAnalysis/ConfigurationMultiProject/Beta/appsettings.json",
            ]);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);
        var facts = extraction.ConfigurationSemanticFacts;

        // Both projects' exact reads are admitted with the shared canonical key.
        var alphaMethod = FindMethod(extraction, "ReadSharedToggleAlpha");
        var betaMethod = FindMethod(extraction, "ReadSharedToggleBeta");
        var alphaRead = Assert.Single(facts.Reads, fact => fact.Method == alphaMethod);
        var betaRead = Assert.Single(facts.Reads, fact => fact.Method == betaMethod);
        Assert.Equal("FeatureToggles:UseSharedToggle", alphaRead.Key);
        Assert.Equal("FeatureToggles:UseSharedToggle", betaRead.Key);

        // Per-project checked-in attribution: Alpha owns true and Beta owns false, and the two
        // observations come from two distinct project-owned files. Ownership matches the project
        // directory as a path segment so the shared fixture prefix (AdvancedAnalysis) can never alias one
        // project's attribution to the other.
        var alphaObservation = Assert.Single(
            facts.CheckedInValues,
            fact => fact.Key == "FeatureToggles:UseSharedToggle"
                && IsProjectOwnedSource(fact.SourceFile, "Alpha"));
        var betaObservation = Assert.Single(
            facts.CheckedInValues,
            fact => fact.Key == "FeatureToggles:UseSharedToggle"
                && IsProjectOwnedSource(fact.SourceFile, "Beta"));
        Assert.True(alphaObservation.Value);
        Assert.False(betaObservation.Value);
        Assert.NotEqual(alphaObservation.SourceFile, betaObservation.SourceFile);
        Assert.Equal(CertaintyLevel.Conservative, alphaObservation.Certainty);
        Assert.Equal(CertaintyLevel.Conservative, betaObservation.Certainty);
    }

    /// <summary>
    /// Claim 11 (review regression): only explicit owned, contained regular files contribute checked-in
    /// observations. An untracked ambient appsettings file in a temporary checkout and (when the
    /// environment permits creating one) a reparse-point link to a file outside the repository never
    /// become facts, and no external value or path leaks into the debug projection.
    /// </summary>
    [Fact]
    public async Task UntrackedAndLinkedAppSettingsFilesNeverBecomeCheckedInFacts()
    {
        var source = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "AdvancedAnalysis", "ConfigurationReads");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-config-ownership-{Guid.NewGuid():N}");
        var root = Path.Combine(temporaryDirectory, "checkout");
        var outsideDirectory = Path.Combine(temporaryDirectory, "outside");
        try
        {
            CopyFixture(source, root);
            Directory.CreateDirectory(outsideDirectory);

            // Untracked ambient file in the project directory with a matching boolean key whose value
            // contradicts the owned appsettings.json (owned true, ambient false).
            File.WriteAllText(
                Path.Combine(root, "appsettings.Untracked.json"),
                """{ "FeatureToggles": { "UseSqlDatabase": false } }""");

            // Reparse-point link to a matching file outside the repository/project directory. When
            // the environment lacks symlink privilege the partition is skipped deterministically, but
            // the untracked-file expectation above is never weakened.
            string outsideFile = Path.Combine(outsideDirectory, "external-appsettings.json");
            File.WriteAllText(
                outsideFile,
                """{ "FeatureToggles": { "UseSqlDatabase": false } }""");
            string linkPath = Path.Combine(root, "appsettings.Linked.json");
            bool linkCreated = TryCreateFileLink(linkPath, outsideFile);

            await RestoreAsync(root);
            var extraction = await ExtractRelocatedAsync(root);
            var checkedIn = extraction.ConfigurationSemanticFacts.CheckedInValues;

            // The owned base appsettings.json observation remains and stays Conservative.
            var owned = Assert.Single(
                checkedIn,
                fact => fact.Key == "FeatureToggles:UseSqlDatabase"
                    && fact.SourceFile.EndsWith("appsettings.json", StringComparison.Ordinal));
            Assert.True(owned.Value);
            Assert.Equal(CertaintyLevel.Conservative, owned.Certainty);
            Assert.True(owned.MayBeOverridden);

            // Untracked ambient files never contribute observations and never leak into debug output.
            Assert.DoesNotContain(checkedIn, fact => fact.SourceFile.Contains("Untracked", StringComparison.Ordinal));
            Assert.DoesNotContain(
                extraction.ConfigurationSemanticFacts.DebugProjection,
                "Untracked",
                StringComparison.OrdinalIgnoreCase);

            if (linkCreated)
            {
                Assert.DoesNotContain(checkedIn, fact => fact.SourceFile.Contains("Linked", StringComparison.Ordinal));
                Assert.DoesNotContain(
                    extraction.ConfigurationSemanticFacts.DebugProjection,
                    "Linked",
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    extraction.ConfigurationSemanticFacts.DebugProjection,
                    outsideFile,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Claim 11 (regression missing-inventory partition): when the explicit owned-file inventory is
    /// empty, the exact reads still project, but checked-in observations fail closed to an empty set
    /// instead of falling back to ambient filesystem enumeration.
    /// </summary>
    [Fact]
    public async Task ReadsStillProjectWhenOwnedFileInventoryIsEmptyButCheckedInValuesFailClosed()
    {
        var extraction = await ExtractSuccessfullyAsync(ownedConfigurationFiles: []);
        Assert.NotEmpty(extraction.ConfigurationSemanticFacts.Reads);
        Assert.Empty(extraction.ConfigurationSemanticFacts.CheckedInValues);
    }

    /// <summary>
    /// Claim 12 (review regression): reordered named instance-syntax and static-syntax
    /// <c>ConfigurationBinder.GetValue&lt;bool&gt;</c> arguments resolve by the compiler-bound
    /// parameter ordinal, never by source position, so the canonical key and boolean default project
    /// exactly.
    /// </summary>
    [Theory]
    [InlineData("UseNamedReordered", "FeatureToggles:UseNamedReordered", true)]
    [InlineData("UseStaticNamedReordered", "FeatureToggles:UseStaticNamed", false)]
    public async Task NamedInstanceAndStaticGetValueArgumentsResolveByCompilerBoundParameterOrdinal(
        string methodName,
        string expectedKey,
        bool expectedDefault)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var method = FindMethod(extraction, methodName);

        var read = Assert.Single(extraction.ConfigurationSemanticFacts.Reads, fact => fact.Method == method);
        Assert.Equal(expectedKey, read.Key);
        Assert.Equal(expectedDefault, read.DefaultValue);
        Assert.Equal(CertaintyLevel.Exact, read.Certainty);
    }

    /// <summary>
    /// Claim 12 (review regression): condition admission requires an actual <c>if</c> boolean condition
    /// with the direct single-write local shape. A <c>while</c> condition and a ref/out escape before
    /// the <c>if</c> keep the exact read but never associate a condition fact.
    /// </summary>
    [Theory]
    [InlineData("WhileLocal")]
    [InlineData("RefEscapeLocal")]
    [InlineData("OutEscapeLocal")]
    public async Task WhileAndRefOutEscapesNeverAssociateACondition(string methodName)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConfigurationSemanticFacts;
        var method = FindMethod(extraction, methodName);

        var read = Assert.Single(facts.Reads, fact => fact.Method == method);
        Assert.Equal("FeatureToggles:UseSqlDatabase", read.Key);
        Assert.Equal(CertaintyLevel.Exact, read.Certainty);
        Assert.DoesNotContain(facts.Conditions, fact => fact.Method == method);
    }

    /// <summary>
    /// Claim 12 (review regression): sensitive keys spelled with hierarchical separators are canonicalized
    /// before matching; <c>Api:Key</c>, <c>Private.Key</c>, <c>Access/Key</c>, and <c>Pass:Word</c>
    /// never admit read, checked-in, profile-known, or debug-projection facts.
    /// </summary>
    [Theory]
    [InlineData("SensitiveApiColonKey", "Api:Key")]
    [InlineData("SensitivePrivateDotKey", "Private.Key")]
    [InlineData("SensitiveAccessSlashKey", "Access/Key")]
    [InlineData("SensitivePasswordColonKey", "Pass:Word")]
    public async Task SensitiveSeparatorSpellingsNeverAdmitReadProfileOrDebugFacts(string methodName, string sensitiveKey)
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConfigurationSemanticFacts;
        var method = FindMethod(extraction, methodName);

        Assert.DoesNotContain(facts.Reads, fact => fact.Key == sensitiveKey);
        Assert.DoesNotContain(facts.CheckedInValues, fact => fact.Key == sensitiveKey);
        Assert.DoesNotContain(facts.DebugProjection, sensitiveKey, StringComparison.OrdinalIgnoreCase);

        // Profile-known partition: a matching analysis-profile boolean for a separator-spelled
        // sensitive key never becomes a fact either.
        var profileExtraction = await ExtractWithSensitiveProfileAsync();
        Assert.DoesNotContain(
            profileExtraction.ConfigurationSemanticFacts.ProfileKnownValues,
            fact => fact.Key == sensitiveKey);
    }

    /// <summary>
    /// Claim 13 (review regression): the public fact contracts enforce their documented impossible-state
    /// invariants at construction: non-empty evidence, explicit certainty, no certainty promotion, a
    /// Conservative checked-in value with the explicit MayBeOverridden marker, Conservative provider
    /// observations, and a well-formed fact set (schema version, producer, profile, fingerprint,
    /// initialized arrays, and debug text).
    /// </summary>
    [Fact]
    public void ConfigurationFactConstructorsEnforceImpossibleStatesAndFactSetInvariants()
    {
        var id = new SemanticFactId("configuration-fact-test-id");
        var method = new MethodId("method:AdvancedAnalysis.ConfigurationReads.ConfigurationReadShapes.UseSqlDatabase");
        var operation = new OperationId("operation:AdvancedAnalysis.ConfigurationReads.ConfigurationReadShapes.UseSqlDatabase:read:1");
        var conditionOperation = new OperationId("operation:AdvancedAnalysis.ConfigurationReads.ConfigurationReadShapes.UseSqlDatabase:condition:1");
        var profile = CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0");

        // (a) Empty evidence must be rejected.
        Assert.Throws<ArgumentException>(() => new ConfigurationReadSemanticFact(
            id,
            method,
            operation,
            "FeatureToggles:UseSqlDatabase",
            defaultValue: null,
            [],
            CertaintyLevel.Exact));

        // (b) Unknown certainty must be rejected even with valid source evidence.
        Assert.Throws<ArgumentException>(() => new ConfigurationReadSemanticFact(
            id,
            method,
            operation,
            "FeatureToggles:UseSqlDatabase",
            defaultValue: null,
            [CreateSourceEvidence(CertaintyLevel.Exact)],
            CertaintyLevel.Unknown));

        // (c) Exact fact certainty must never be promoted from Conservative evidence.
        Assert.Throws<ArgumentException>(() => new ConfigurationReadSemanticFact(
            id,
            method,
            operation,
            "FeatureToggles:UseSqlDatabase",
            defaultValue: null,
            [CreateSourceEvidence(CertaintyLevel.Conservative)],
            CertaintyLevel.Exact));

        // (d) A checked-in observation must never claim Exact certainty (weakest-certainty partition).
        Assert.Throws<ArgumentException>(() => new CheckedInConfigurationValueFact(
            id,
            "FeatureToggles:UseSqlDatabase",
            true,
            "tests/fixtures/AdvancedAnalysis/ConfigurationReads/appsettings.json",
            [CreateSourceEvidence(CertaintyLevel.Exact)],
            CertaintyLevel.Exact));

        // (e) The weakest-certainty positive: a Conservative checked-in observation with Conservative
        // evidence and the explicit MayBeOverridden marker is accepted.
        var accepted = new CheckedInConfigurationValueFact(
            id,
            "FeatureToggles:UseSqlDatabase",
            true,
            "tests/fixtures/AdvancedAnalysis/ConfigurationReads/appsettings.json",
            [CreateSourceEvidence(CertaintyLevel.Conservative)],
            CertaintyLevel.Conservative,
            mayBeOverridden: true);
        Assert.True(accepted.MayBeOverridden);

        // (f) A checked-in observation without the explicit MayBeOverridden marker is impossible.
        Assert.Throws<ArgumentException>(() => new CheckedInConfigurationValueFact(
            id,
            "FeatureToggles:UseSqlDatabase",
            true,
            "tests/fixtures/AdvancedAnalysis/ConfigurationReads/appsettings.json",
            [CreateSourceEvidence(CertaintyLevel.Conservative)],
            CertaintyLevel.Conservative,
            mayBeOverridden: false));

        // (g) A provider observation must remain Conservative and can never claim Exact certainty.
        Assert.Throws<ArgumentException>(() => new StandardProviderObservationFact(
            id,
            method,
            operation,
            StandardConfigurationProviderKind.CommandLine,
            precedenceOrdinal: 4,
            [CreateSourceEvidence(CertaintyLevel.Exact)],
            CertaintyLevel.Exact));

        // (h) The fact set rejects malformed versions, producers, profiles, fingerprints, uninitialized
        // arrays, and empty debug text while accepting a well-formed empty set.
        Assert.Throws<ArgumentException>(() => new ConfigurationSemanticFactSet(
            SchemaVersion: 0,
            "producer",
            profile,
            "fingerprint",
            [],
            [],
            [],
            [],
            [],
            [],
            "configuration-facts:v1"));
        Assert.Throws<ArgumentException>(() => new ConfigurationSemanticFactSet(
            SchemaVersion: 1,
            " ",
            profile,
            "fingerprint",
            [],
            [],
            [],
            [],
            [],
            [],
            "configuration-facts:v1"));
        Assert.Throws<ArgumentException>(() => new ConfigurationSemanticFactSet(
            SchemaVersion: 1,
            "producer",
            null!,
            "fingerprint",
            [],
            [],
            [],
            [],
            [],
            [],
            "configuration-facts:v1"));
        Assert.Throws<ArgumentException>(() => new ConfigurationSemanticFactSet(
            SchemaVersion: 1,
            "producer",
            profile,
            " ",
            [],
            [],
            [],
            [],
            [],
            [],
            "configuration-facts:v1"));
        Assert.Throws<ArgumentException>(() => new ConfigurationSemanticFactSet(
            SchemaVersion: 1,
            "producer",
            profile,
            "fingerprint",
            default,
            default,
            default,
            default,
            default,
            default,
            "configuration-facts:v1"));
        Assert.Throws<ArgumentException>(() => new ConfigurationSemanticFactSet(
            SchemaVersion: 1,
            "producer",
            profile,
            "fingerprint",
            [],
            [],
            [],
            [],
            [],
            [],
            " "));
        var acceptedSet = new ConfigurationSemanticFactSet(
            SchemaVersion: 1,
            "producer",
            profile,
            "fingerprint",
            [],
            [],
            [],
            [],
            [],
            [],
            "configuration-facts:v1");
        Assert.NotNull(acceptedSet);
    }

    /// <summary>
    /// Claim 13 (review regression): an already-cancelled extraction reports <see cref="ApplicationOutcome.Cancelled"/>
    /// with no value, and a malformed owned appsettings file fails closed: it contributes no checked-in
    /// observation, never fails the analysis, and never leaks into the debug projection.
    /// </summary>
    [Fact]
    public async Task CancelledExtractionAndMalformedAppSettingsFailClosedWithoutInventingFacts()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = await new RoslynProfileAnalysisExtractor()
            .ExtractAsync(CreateFixtureRequest(), cts.Token);
        Assert.Equal(ApplicationOutcome.Cancelled, cancelled.Outcome);
        Assert.Null(cancelled.Value);

        var extraction = await ExtractSuccessfullyAsync();
        Assert.DoesNotContain(
            extraction.ConfigurationSemanticFacts.CheckedInValues,
            fact => fact.SourceFile.Contains("Malformed", StringComparison.Ordinal));
        Assert.DoesNotContain(
            extraction.ConfigurationSemanticFacts.DebugProjection,
            "Malformed",
            StringComparison.OrdinalIgnoreCase);
    }

    public enum ProfileValuePartition
    {
        ValidBoolean,
        NotABoolean,
    }

    private static async Task<ProfileAnalysisExtraction> ExtractSuccessfullyAsync(
        string? analysisValue = null,
        ImmutableArray<string>? ownedConfigurationFiles = null)
    {
        var result = await ExtractFixtureAsync(analysisValue, ownedConfigurationFiles);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractFixtureAsync(
        string? analysisValue = null,
        ImmutableArray<string>? ownedConfigurationFiles = null)
        => await new RoslynProfileAnalysisExtractor()
            .ExtractAsync(CreateFixtureRequest(analysisValue, ownedConfigurationFiles), CancellationToken.None);

    private static async Task<ProfileAnalysisExtraction> ExtractWithSensitiveProfileAsync()
    {
        var root = FindRepositoryRoot();
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(
                FixtureRelativePath,
                "Release",
                "net10.0",
                analysisProperties:
                [
                    new KeyValuePair<string, string>("Api:Key", "true"),
                    new KeyValuePair<string, string>("Private.Key", "true"),
                    new KeyValuePair<string, string>("Access/Key", "true"),
                    new KeyValuePair<string, string>("Pass:Word", "true"),
                ]),
            RepositoryOwnedConfigurationFiles: ConfigurationReadsOwnedFiles);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static CompilationAnalysisRequest CreateFixtureRequest(
        string? analysisValue = null,
        ImmutableArray<string>? ownedConfigurationFiles = null)
    {
        var root = FindRepositoryRoot();
        IEnumerable<KeyValuePair<string, string>>? analysisProperties = null;
        if (analysisValue is not null)
        {
            analysisProperties = [new KeyValuePair<string, string>("FeatureToggles:UseSqlDatabase", analysisValue)];
        }

        return new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0", analysisProperties: analysisProperties),
            RepositoryOwnedConfigurationFiles: ownedConfigurationFiles ?? ConfigurationReadsOwnedFiles);
    }

    private static async Task<ProfileAnalysisExtraction> ExtractRelocatedAsync(string root)
    {
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, "ConfigurationReads.csproj"),
            CompilationProfile.Create("ConfigurationReads.csproj", "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles: RelocatedOwnedConfigurationFiles);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static MethodId FindMethod(ProfileAnalysisExtraction extraction, string name)
        => Assert.Single(extraction.ProgramIndex.Methods, method => method.Name == name).Id;

    private static bool IsProjectOwnedSource(string sourceFile, string projectName)
        => sourceFile
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Contains(projectName, StringComparer.Ordinal);

    private static string CollectConfigurationFactIds(ConfigurationSemanticFactSet facts) => string.Join(
        "\n",
        facts.Reads
            .Select(fact => fact.Id.Value)
            .Concat(facts.Conditions.Select(fact => fact.Id.Value))
            .Concat(facts.ProviderObservations.Select(fact => fact.Id.Value))
            .Concat(facts.CheckedInValues.Select(fact => fact.Id.Value))
            .Concat(facts.ProfileKnownValues.Select(fact => fact.Id.Value))
            .Order(StringComparer.Ordinal));

    private static EvidenceRef CreateSourceEvidence(CertaintyLevel certainty) => new(
        new EvidenceId("evidence-configuration-test-id"),
        EvidenceKind.Source,
        "test-artifact",
        new SourceRange(
            new DocumentId("document-configuration-test-id"),
            new SourcePosition(1, 1),
            new SourcePosition(1, 5)),
        "test-symbol",
        null,
        certainty);

    private static void AssertEvidence(ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.False(string.IsNullOrWhiteSpace(item.Artifact)));
        Assert.True(certainty != CertaintyLevel.Unknown, "A projected fact must carry explicit certainty.");
        Assert.True(certainty >= evidence.Max(item => item.Certainty), "Fact certainty must never exceed its strongest evidence.");
    }

    private static bool TryCreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void CopyFixture(string sourceDirectory, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string name = Path.GetFileName(file);
            if (name is "packages.lock.json" or "Directory.Build.props" or "Directory.Packages.props")
            {
                continue;
            }

            File.Copy(file, Path.Combine(destinationRoot, name));
        }
    }

    private static async Task RestoreAsync(string root)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "restore ConfigurationReads.csproj --nologo",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{await output}\n{await error}");
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
