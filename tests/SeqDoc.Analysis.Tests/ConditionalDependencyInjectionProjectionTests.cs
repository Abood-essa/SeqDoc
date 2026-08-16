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
/// accepted contract risk-based tests for the new memory-only <see cref="ConditionalDependencyInjectionFactSet"/>.
/// The fixture mirrors the pinned CustomerManagement shape on unrelated vocabulary: an exact
/// top-level <c>ConfigurationBinder.GetValue&lt;bool&gt;</c> local feeding one if/else that registers
/// two unrelated <c>IStorageService</c> implementations whose controller/service call resolution
/// includes both implementations. Claims 1-3 prove exact true/false arm membership and the single
/// complete same-condition opposite-arm group; claim 4 consolidates the exclusivity negatives
/// (independent ifs, missing else, same polarity, overlap, unguarded extra); claim 5 proves
/// unsupported DI shapes never project arms or groups; claim 6 proves top-level-only authority (an
/// extracted method's admitted read/if/else never creates companion arms, and an unresolved
/// condition never projects arms); claim 10 proves both arms remain possible without a profile-known
/// value while a checked-in true stays Conservative/MayBeOverridden and never selects; claim 11
/// proves a profile-known value retains both arms and provenance without promoting certainty; and
/// claim 12 proves deterministic identity/debug output across repeated construction and two relocated
/// git-free roots.
/// </summary>
[Collection(MsBuildIntegrationGroup.Name)]
public sealed class ConditionalDependencyInjectionProjectionTests
{
    private const string FixtureRelativePath = "tests/fixtures/AdvancedAnalysis/ConditionalDependencyInjection/ConditionalDependencyInjection.csproj";

    private const string StorageServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IStorageService";
    private const string MemoryStorageImplementation = "AdvancedAnalysis.ConditionalDependencyInjection.Services.MemoryStorageService";
    private const string FileStorageImplementation = "AdvancedAnalysis.ConditionalDependencyInjection.Services.FileStorageService";
    private const string StorageToggleKey = "Storage:UseMemoryStorage";

    private const string AuditServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IAuditService";
    private const string CacheServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.ICacheService";
    private const string SmsServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.ISmsService";
    private const string BackupServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IBackupService";
    private const string NotificationServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.INotificationService";

    private const string KeyedServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IKeyedService";
    private const string TryServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.ITryService";
    private const string WidgetServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IWidgetService";

    private const string SinkServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.ISinkService";
    private const string PolicyServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IPolicyService";

    private const string FallbackServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IFallbackService";
    private const string LoopServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.ILoopService";
    private const string ReassignedServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IReassignedService";
    private const string RefEscapedServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IRefEscapedService";
    private const string OutEscapedServiceType = "AdvancedAnalysis.ConditionalDependencyInjection.Services.IOutEscapedService";

    /// <summary>
    /// accepted contract explicit owned configuration inventory for the ConditionalDependencyInjection fixture:
    /// exactly the repository-controlled appsettings file that may contribute checked-in
    /// observations. Runtime-created ambient files are never listed.
    /// </summary>
    private static readonly ImmutableArray<string> FixtureOwnedFiles =
    [
        "tests/fixtures/AdvancedAnalysis/ConditionalDependencyInjection/appsettings.json",
    ];

    /// <summary>
    /// accepted contract owned-file inventory for a relocated git-free checkout: the copied appsettings file is
    /// repository-root-relative only.
    /// </summary>
    private static readonly ImmutableArray<string> RelocatedOwnedFiles =
    [
        "appsettings.json",
    ];

    /// <summary>
    /// Claim 1: the exact true-arm registration membership projects with the admitted condition and
    /// read operations, the canonical key, the Scoped lifetime, Exact certainty, and non-empty
    /// evidence. The arm's condition/read operations are anchored to the accepted contract
    /// ConfigurationConditionSemanticFact.
    /// </summary>
    [Fact]
    public async Task ExactTrueArmRegistrationProjectsWithConditionReadKeyAndLifetime()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConditionalDependencyInjectionFacts;

        var arm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && candidate.IsTrueArm);
        Assert.Equal(MemoryStorageImplementation, arm.ImplementationType);
        Assert.Equal(StorageToggleKey, arm.Key);
        Assert.Equal(DependencyInjectionLifetime.Scoped, arm.Lifetime);
        Assert.True(arm.IsTrueArm);
        Assert.Equal(CertaintyLevel.Exact, arm.Certainty);
        AssertEvidence(arm.Evidence, arm.Certainty);
        Assert.NotEmpty(arm.RegistrationOperation.Value);
        Assert.NotEmpty(arm.ConditionOperation.Value);
        Assert.NotEmpty(arm.ReadOperation.Value);

        // The arm anchors to the exact accepted contract condition rather than to source text or guesses.
        var condition = Assert.Single(
            extraction.ConfigurationSemanticFacts.Conditions,
            candidate => candidate.ReadOperation == arm.ReadOperation);
        Assert.Equal(arm.ConditionOperation, condition.ConditionOperation);
        Assert.True(condition.TrueWhenReadTrue);

        // The arm is bound to the exact DI registration fact, never to a name match.
        var registration = Assert.Single(
            extraction.DependencyInjectionFacts.Registrations,
            candidate => candidate.Id == arm.RegistrationId);
        Assert.Equal(MemoryStorageImplementation, registration.ImplementationType);
        Assert.Equal(DependencyInjectionLifetime.Scoped, registration.Lifetime);
    }

    /// <summary>
    /// Claim 2: the exact false-arm registration membership projects with the opposite semantic
    /// polarity under the same condition/read operation and key.
    /// </summary>
    [Fact]
    public async Task ExactFalseArmRegistrationProjectsOppositePolarityWithSameConditionAndKey()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConditionalDependencyInjectionFacts;

        var arm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && !candidate.IsTrueArm);
        Assert.Equal(FileStorageImplementation, arm.ImplementationType);
        Assert.Equal(StorageToggleKey, arm.Key);
        Assert.Equal(DependencyInjectionLifetime.Scoped, arm.Lifetime);
        Assert.False(arm.IsTrueArm);
        Assert.Equal(CertaintyLevel.Exact, arm.Certainty);
        AssertEvidence(arm.Evidence, arm.Certainty);

        var trueArm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && candidate.IsTrueArm);
        Assert.Equal(trueArm.ConditionOperation, arm.ConditionOperation);
        Assert.Equal(trueArm.ReadOperation, arm.ReadOperation);
    }

    /// <summary>
    /// Claim 3: exactly one complete same-condition opposite-arm alternative group projects for the
    /// IStorageService pair. The group references the exact registration identities of both arms and
    /// retains both implementation types, the shared condition/read operations, the key, evidence,
    /// and certainty.
    /// </summary>
    [Fact]
    public async Task SingleCompleteSameConditionOppositeArmGroupProjectsExactlyOnce()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConditionalDependencyInjectionFacts;

        var group = Assert.Single(facts.Groups);
        Assert.Equal(StorageServiceType, group.ServiceType);
        Assert.Equal(StorageToggleKey, group.Key);
        Assert.Equal(DependencyInjectionLifetime.Scoped, group.Lifetime);
        AssertEvidence(group.Evidence, group.Certainty);

        var trueArm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && candidate.IsTrueArm);
        var falseArm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && !candidate.IsTrueArm);
        Assert.Equal(trueArm.RegistrationId, group.TrueRegistrationId);
        Assert.Equal(falseArm.RegistrationId, group.FalseRegistrationId);
        Assert.Equal(MemoryStorageImplementation, group.TrueImplementationType);
        Assert.Equal(FileStorageImplementation, group.FalseImplementationType);
        Assert.Equal(trueArm.ConditionOperation, group.ConditionOperation);
        Assert.Equal(trueArm.ReadOperation, group.ReadOperation);
    }

    /// <summary>
    /// Claim 4: independent if statements, a missing else, same-polarity registrations, overlapping
    /// registrations, and an unguarded extra registration never form an alternative group. Arm facts
    /// still project where the condition is admitted; only the group refuses to form.
    /// </summary>
    [Theory]
    [InlineData("independent-ifs", "AdvancedAnalysis.ConditionalDependencyInjection.Services.IAuditService")]
    [InlineData("missing-else", "AdvancedAnalysis.ConditionalDependencyInjection.Services.ICacheService")]
    [InlineData("same-polarity", "AdvancedAnalysis.ConditionalDependencyInjection.Services.ISmsService")]
    [InlineData("overlap", "AdvancedAnalysis.ConditionalDependencyInjection.Services.IBackupService")]
    [InlineData("unguarded-extra", "AdvancedAnalysis.ConditionalDependencyInjection.Services.INotificationService")]
    public async Task ExclusivityNegativesNeverFormAlternativeGroups(string partition, string serviceType)
    {
        Assert.NotEmpty(partition);
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConditionalDependencyInjectionFacts;

        Assert.DoesNotContain(facts.Groups, group => group.ServiceType == serviceType);
        // The admitted conditions still project their arm facts; only the group refuses to form.
        Assert.Contains(facts.RegistrationArms, arm => arm.ServiceType == serviceType);
    }

    /// <summary>
    /// Claim 5: keyed, TryAdd, and factory registrations never project DI registration facts, arm
    /// facts, or groups; the fail-closed vocabulary keeps every unsupported shape absent.
    /// </summary>
    [Theory]
    [InlineData("keyed", "AdvancedAnalysis.ConditionalDependencyInjection.Services.IKeyedService")]
    [InlineData("try-add", "AdvancedAnalysis.ConditionalDependencyInjection.Services.ITryService")]
    [InlineData("factory", "AdvancedAnalysis.ConditionalDependencyInjection.Services.IWidgetService")]
    public async Task UnsupportedDiShapesNeverProjectArmsOrGroups(string partition, string serviceType)
    {
        Assert.NotEmpty(partition);
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConditionalDependencyInjectionFacts;

        Assert.DoesNotContain(extraction.DependencyInjectionFacts.Registrations, registration => registration.ServiceType == serviceType);
        Assert.DoesNotContain(facts.RegistrationArms, arm => arm.ServiceType == serviceType);
        Assert.DoesNotContain(facts.Groups, group => group.ServiceType == serviceType);
    }

    /// <summary>
    /// Claim 6: top-level-only authority. An extracted helper method containing an admitted read and
    /// an exact if/else with exact registrations never projects companion arm facts (Method Flow is
    /// the sole control authority there), and an unresolved top-level condition never projects arms
    /// either. The exact DI registration facts still project from both methods.
    /// </summary>
    [Fact]
    public async Task ExtractedMethodAndUnresolvedConditionRegistrationsNeverProjectCompanionArms()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConditionalDependencyInjectionFacts;

        // The helper-method registrations are still admitted as exact DI facts...
        Assert.Equal(
            2,
            extraction.DependencyInjectionFacts.Registrations.Count(registration => registration.ServiceType == SinkServiceType));
        // ...but no companion arm facts project inside the extracted method.
        Assert.DoesNotContain(facts.RegistrationArms, arm => arm.ServiceType == SinkServiceType);
        Assert.DoesNotContain(facts.Groups, group => group.ServiceType == SinkServiceType);

        // The unresolved top-level condition keeps exact DI registrations but never projects arms.
        Assert.Equal(
            2,
            extraction.DependencyInjectionFacts.Registrations.Count(registration => registration.ServiceType == PolicyServiceType));
        Assert.DoesNotContain(facts.RegistrationArms, arm => arm.ServiceType == PolicyServiceType);
        Assert.DoesNotContain(facts.Groups, group => group.ServiceType == PolicyServiceType);
    }

    /// <summary>
    /// Claim 4 (review regression/F2): a registration nested inside an inner if or a loop is never
    /// directly enclosed by the outer arm, and a read local reassigned or ref/out-escaped before its
    /// condition is never a single-write direct local. Neither shape may produce arm facts or an
    /// outer complete group, even though the admitted reads themselves still project.
    /// </summary>
    [Theory]
    [InlineData("nested-if", "AdvancedAnalysis.ConditionalDependencyInjection.Services.IFallbackService", "Storage:UsePrimary")]
    [InlineData("nested-loop", "AdvancedAnalysis.ConditionalDependencyInjection.Services.ILoopService", "Storage:UseLoop")]
    [InlineData("reassigned", "AdvancedAnalysis.ConditionalDependencyInjection.Services.IReassignedService", "Storage:UseReassigned")]
    [InlineData("ref-escape", "AdvancedAnalysis.ConditionalDependencyInjection.Services.IRefEscapedService", "Storage:UseRefEscaped")]
    [InlineData("out-escape", "AdvancedAnalysis.ConditionalDependencyInjection.Services.IOutEscapedService", "Storage:UseOutEscaped")]
    public async Task NestedOrEscapedRegistrationsNeverFormAlternativeGroups(
        string partition,
        string serviceType,
        string key)
    {
        Assert.NotEmpty(partition);
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConditionalDependencyInjectionFacts;

        // The admitted read still projects; only the arm/group association is withheld.
        Assert.Contains(extraction.ConfigurationSemanticFacts.Reads, read => read.Key == key);

        // No arm fact may attribute the nested/escaped registration to the outer arm.
        Assert.DoesNotContain(facts.RegistrationArms, arm => arm.ServiceType == serviceType);
        Assert.DoesNotContain(facts.Groups, group => group.ServiceType == serviceType);
    }

    /// <summary>
    /// Claim 10 (projection side): without an explicit analysis-profile value both arms remain
    /// possible, and the checked-in true observation stays Conservative and explicitly
    /// MayBeOverridden. The checked-in value never suppresses, promotes, or selects an arm.
    /// </summary>
    [Fact]
    public async Task BothArmsProjectWithoutProfileKnownValueAndCheckedInTrueNeverSelectsAnArm()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.ConditionalDependencyInjectionFacts;

        var trueArm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && candidate.IsTrueArm);
        var falseArm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && !candidate.IsTrueArm);
        Assert.Equal(MemoryStorageImplementation, trueArm.ImplementationType);
        Assert.Equal(FileStorageImplementation, falseArm.ImplementationType);

        var checkedIn = Assert.Single(
            extraction.ConfigurationSemanticFacts.CheckedInValues,
            fact => fact.Key == StorageToggleKey);
        Assert.True(checkedIn.Value);
        Assert.Equal(CertaintyLevel.Conservative, checkedIn.Certainty);
        Assert.True(checkedIn.MayBeOverridden);
        Assert.EndsWith("appsettings.json", checkedIn.SourceFile, StringComparison.Ordinal);

        Assert.Empty(extraction.ConfigurationSemanticFacts.ProfileKnownValues);
    }

    /// <summary>
    /// Claim 11 (projection side): a matching analysis-profile boolean retains both arms, keeps their
    /// certainty unpromoted, and surfaces the accepted contract profile-known fact with its analysis-profile
    /// provenance.
    /// </summary>
    [Fact]
    public async Task ProfileKnownValueKeepsBothArmsAndProfileKnownFactCarriesProvenance()
    {
        var extraction = await ExtractSuccessfullyAsync(analysisValue: "true");
        var facts = extraction.ConditionalDependencyInjectionFacts;

        var trueArm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && candidate.IsTrueArm);
        var falseArm = Assert.Single(
            facts.RegistrationArms,
            candidate => candidate.ServiceType == StorageServiceType && !candidate.IsTrueArm);
        Assert.Equal(MemoryStorageImplementation, trueArm.ImplementationType);
        Assert.Equal(FileStorageImplementation, falseArm.ImplementationType);
        Assert.Equal(CertaintyLevel.Exact, trueArm.Certainty);
        Assert.Equal(CertaintyLevel.Exact, falseArm.Certainty);

        var profileKnown = Assert.Single(
            extraction.ConfigurationSemanticFacts.ProfileKnownValues,
            fact => fact.Key == StorageToggleKey);
        Assert.True(profileKnown.Value);
        Assert.NotEqual(CertaintyLevel.Unknown, profileKnown.Certainty);
        Assert.False(string.IsNullOrWhiteSpace(profileKnown.AnalysisProfileSource));
        AssertEvidence(profileKnown.Evidence, profileKnown.Certainty);
    }

    /// <summary>
    /// Claim 12 (projection side): fact identities and the debug projection are deterministic across
    /// repeated construction and two physically relocated checkout roots, and no absolute checkout
    /// path leaks into the projection.
    /// </summary>
    [Fact]
    public async Task ConditionalDiFactsAreDeterministicAcrossRepeatedConstructionAndRelocatedRoots()
    {
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();
        Assert.Equal(
            CollectConditionalDiFactIds(first.ConditionalDependencyInjectionFacts),
            CollectConditionalDiFactIds(second.ConditionalDependencyInjectionFacts));
        Assert.Equal(
            first.ConditionalDependencyInjectionFacts.DebugProjection,
            second.ConditionalDependencyInjectionFacts.DebugProjection);
        Assert.DoesNotContain(
            FindRepositoryRoot(),
            first.ConditionalDependencyInjectionFacts.DebugProjection,
            StringComparison.OrdinalIgnoreCase);

        var source = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "fixtures",
            "AdvancedAnalysis",
            "ConditionalDependencyInjection");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-conditional-di-relocation-{Guid.NewGuid():N}");
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
                CollectConditionalDiFactIds(relocatedFirst.ConditionalDependencyInjectionFacts),
                CollectConditionalDiFactIds(relocatedSecond.ConditionalDependencyInjectionFacts));
            Assert.Equal(
                relocatedFirst.ConditionalDependencyInjectionFacts.DebugProjection,
                relocatedSecond.ConditionalDependencyInjectionFacts.DebugProjection);
            Assert.DoesNotContain(
                firstRoot,
                relocatedFirst.ConditionalDependencyInjectionFacts.DebugProjection,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                secondRoot,
                relocatedSecond.ConditionalDependencyInjectionFacts.DebugProjection,
                StringComparison.OrdinalIgnoreCase);
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
    /// Claim 12 (review regression): arm/group identities include the exact required semantic anchors
    /// and never depend on the checkout path. Two minimal single-anchor fixture variants prove churn:
    /// changing only the toggle key (and therefore the read operation) must churn the arm identity,
    /// and changing only the false-arm registration must churn the group identity. Relocation
    /// stability itself is pinned by <see cref="ConditionalDiFactsAreDeterministicAcrossRepeatedConstructionAndRelocatedRoots"/>.
    /// </summary>
    [Fact]
    public async Task ArmAndGroupIdentitiesChurnOnReadAndRegistrationChanges()
    {
        var baseline = await ExtractSuccessfullyAsync();
        var baselineTrueArm = Assert.Single(
            baseline.ConditionalDependencyInjectionFacts.RegistrationArms,
            arm => arm.ServiceType == StorageServiceType && arm.IsTrueArm);
        var baselineGroup = Assert.Single(
            baseline.ConditionalDependencyInjectionFacts.Groups,
            group => group.ServiceType == StorageServiceType);

        var source = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "fixtures",
            "AdvancedAnalysis",
            "ConditionalDependencyInjection");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"seqdoc-conditional-di-identity-{Guid.NewGuid():N}");
        try
        {
            // Read/key variant: only the toggle key (and therefore the read operation) changes; the
            // condition operation and both registrations stay identical, so only a read-anchored
            // identity may churn.
            var readVariant = Path.Combine(temporaryDirectory, "read-variant");
            CopyFixture(source, readVariant);
            ReplaceInCopiedFiles(readVariant, "\"Storage:UseMemoryStorage\"", "\"Storage:UseAlternateStorage\"");
            await RestoreAsync(readVariant);
            var readExtraction = await ExtractRelocatedAsync(readVariant);
            var readTrueArm = Assert.Single(
                readExtraction.ConditionalDependencyInjectionFacts.RegistrationArms,
                arm => arm.ServiceType == StorageServiceType && arm.IsTrueArm);
            Assert.NotEqual(baselineTrueArm.Id, readTrueArm.Id);

            // Registration variant: only the false-arm implementation changes; the read/key/condition
            // stay identical, so only a registration-anchored group identity may churn.
            var registrationVariant = Path.Combine(temporaryDirectory, "registration-variant");
            CopyFixture(source, registrationVariant);
            ReplaceInCopiedFiles(registrationVariant, "FileStorageService", "FileStorageServiceVariant");
            await RestoreAsync(registrationVariant);
            var registrationExtraction = await ExtractRelocatedAsync(registrationVariant);
            var registrationGroup = Assert.Single(
                registrationExtraction.ConditionalDependencyInjectionFacts.Groups,
                group => group.ServiceType == StorageServiceType);
            Assert.NotEqual(baselineGroup.Id, registrationGroup.Id);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static async Task<ProfileAnalysisExtraction> ExtractSuccessfullyAsync(string? analysisValue = null)
    {
        var result = await ExtractFixtureAsync(analysisValue);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static async Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractFixtureAsync(
        string? analysisValue = null)
    {
        var root = FindRepositoryRoot();
        IEnumerable<KeyValuePair<string, string>>? analysisProperties = null;
        if (analysisValue is not null)
        {
            analysisProperties = [new KeyValuePair<string, string>(StorageToggleKey, analysisValue)];
        }

        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            CompilationProfile.Create(FixtureRelativePath, "Release", "net10.0", analysisProperties: analysisProperties),
            RepositoryOwnedConfigurationFiles: FixtureOwnedFiles);
        return await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
    }

    private static async Task<ProfileAnalysisExtraction> ExtractRelocatedAsync(string root)
    {
        var request = new CompilationAnalysisRequest(
            root,
            Path.Combine(root, "ConditionalDependencyInjection.csproj"),
            CompilationProfile.Create("ConditionalDependencyInjection.csproj", "Release", "net10.0"),
            RepositoryOwnedConfigurationFiles: RelocatedOwnedFiles);
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(request, CancellationToken.None);
        Assert.True(
            result.Outcome == ApplicationOutcome.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.TechnicalCause}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    private static string CollectConditionalDiFactIds(ConditionalDependencyInjectionFactSet facts)
        => string.Join(
            "\n",
            facts.RegistrationArms
                .Select(fact => fact.Id.Value)
                .Concat(facts.Groups.Select(fact => fact.Id.Value))
                .Order(StringComparer.Ordinal));

    private static void AssertEvidence(ImmutableArray<EvidenceRef> evidence, CertaintyLevel certainty)
    {
        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.False(string.IsNullOrWhiteSpace(item.Artifact)));
        Assert.True(certainty != CertaintyLevel.Unknown, "A projected fact must carry explicit certainty.");
        Assert.True(certainty >= evidence.Max(item => item.Certainty), "Fact certainty must never exceed its strongest evidence.");
    }

    private static void CopyFixture(string sourceDirectory, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            string[] segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(segment => segment is "bin" or "obj-custom"))
            {
                continue;
            }

            if (relative is "packages.lock.json" or "Directory.Build.props" or "Directory.Packages.props")
            {
                continue;
            }

            string destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    /// <summary>
    /// Applies one exact source-text replacement to every copied C# fixture file so a single
    /// semantic anchor (the toggle key or the implementation type) can vary while every other anchor
    /// stays identical.
    /// </summary>
    private static void ReplaceInCopiedFiles(string root, string oldText, string newText)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(file);
            if (content.Contains(oldText, StringComparison.Ordinal))
            {
                File.WriteAllText(file, content.Replace(oldText, newText, StringComparison.Ordinal));
            }
        }
    }

    private static async Task RestoreAsync(string root)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "restore ConditionalDependencyInjection.csproj --nologo",
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
