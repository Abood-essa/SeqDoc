using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.FrameworkModels;
using SeqDoc.FrameworkModels.AspNetCore;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class FrameworkCompositionProjectionTests
{
    private const string FixtureName = "DependencyInjection";
    private const string FixtureRelativePath = "tests/fixtures/BehaviorDocumentation/DependencyInjection/DependencyInjection.csproj";
    private const string ControllerMetadataName = "BehaviorDocumentation.DependencyInjection.Controllers.GadgetsController";
    private const string GadgetStoreServiceType = "BehaviorDocumentation.DependencyInjection.Services.IGadgetStore";
    private const string ClockServiceType = "BehaviorDocumentation.DependencyInjection.Services.IClock";
    private const string RepositoryServiceType = "BehaviorDocumentation.DependencyInjection.Services.IGadgetRepository";
    private static readonly char[] ProjectionLineSeparators = { '\r', '\n' };

    [Fact]
    public async Task ProductionCompositionInvokesAcceptedControllerFacts()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var framework = await ComposeAsync(extraction);

        Assert.True(framework.Recognized);
        Assert.Contains(framework.AppliedModels, model =>
            model.ModelId == AspNetCoreControllerModel.ModelIdValue
            && model.Version == AspNetCoreControllerModel.ModelVersionValue);

        var entries = framework.Facts.OfType<HttpEntryPointFact>().ToArray();
        var getEntry = Assert.Single(entries, entry =>
            entry.HttpMethod == HttpMethodKind.Get && entry.CanonicalRoute == "api/Gadgets/{id:guid}");
        Assert.Equal(CertaintyLevel.Exact, getEntry.Certainty);
        Assert.Equal("GET api/Gadgets/{id:guid}", getEntry.OperationKey);

        var outcome = Assert.Single(framework.Facts.OfType<HttpDirectOutcomeFact>());
        Assert.Equal(HttpOutcomeHelperKind.Ok, outcome.HelperKind);
        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal(CertaintyLevel.Exact, outcome.Certainty);

        var binding = Assert.Single(framework.Facts.OfType<HttpRequestBindingFact>(),
            candidate => candidate.ParameterName == "id" && candidate.BindingKind == HttpBindingKind.Route);
        Assert.Equal("id", binding.RoutePlaceholder);
    }

    [Fact]
    public async Task RepeatedAnalysisProducesIdenticalCompanionIdsAndCanonicalDebugOutput()
    {
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();

        var firstIds = CollectFactIds(first.DependencyInjectionFacts);
        var secondIds = CollectFactIds(second.DependencyInjectionFacts);
        Assert.NotEmpty(firstIds);
        Assert.Equal(firstIds, secondIds);

        Assert.Equal(first.DependencyInjectionFacts.DebugProjection, second.DependencyInjectionFacts.DebugProjection);
        Assert.DoesNotContain("\r", first.DependencyInjectionFacts.DebugProjection, StringComparison.Ordinal);
        Assert.Contains("\n", first.DependencyInjectionFacts.DebugProjection, StringComparison.Ordinal);
        Assert.DoesNotContain(FindRepositoryRoot(), first.DependencyInjectionFacts.DebugProjection, StringComparison.OrdinalIgnoreCase);

        var firstFramework = await ComposeAsync(first);
        var secondFramework = await ComposeAsync(second);
        Assert.Equal(
            string.Join("\n", firstFramework.Facts.Select(fact => fact.Id.Value).Order(StringComparer.Ordinal)),
            string.Join("\n", secondFramework.Facts.Select(fact => fact.Id.Value).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task ExactRegistrationsBindControllerConstructorParametersByCompilerIdentity()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var controllerType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == ControllerMetadataName);
        var constructor = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == ".ctor" && method.ContainingType == controllerType.Id);

        var storeBindings = extraction.DependencyInjectionFacts.Bindings
            .Where(binding => binding.ConstructorMethod == constructor.Id && binding.ParameterName == "store")
            .ToArray();
        Assert.Equal(2, storeBindings.Length);
        Assert.All(storeBindings, binding => Assert.Equal(GadgetStoreServiceType, binding.ParameterType));

        var clockBindings = extraction.DependencyInjectionFacts.Bindings
            .Where(binding => binding.ConstructorMethod == constructor.Id && binding.ParameterName == "clock")
            .ToArray();
        var clockBinding = Assert.Single(clockBindings);
        Assert.Equal(ClockServiceType, clockBinding.ParameterType);
        Assert.Equal(ClockServiceType, clockBinding.ServiceType);
        Assert.Equal("BehaviorDocumentation.DependencyInjection.Services.SystemClock", clockBinding.ImplementationType);
        Assert.Equal(DependencyInjectionLifetime.Singleton, clockBinding.Lifetime);
    }

    [Fact]
    public async Task DistinctMatchingRegistrationsRemainDistinctAndVisible()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.DependencyInjectionFacts;

        var storeRegistrations = facts.Registrations
            .Where(registration => registration.ServiceType == GadgetStoreServiceType)
            .ToArray();
        Assert.Equal(2, storeRegistrations.Length);
        Assert.NotEqual(storeRegistrations[0].Id, storeRegistrations[1].Id);
        Assert.NotEqual(storeRegistrations[0].Operation, storeRegistrations[1].Operation);

        var controllerType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == ControllerMetadataName);
        var constructor = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == ".ctor" && method.ContainingType == controllerType.Id);
        var storeBindings = facts.Bindings
            .Where(binding => binding.ConstructorMethod == constructor.Id && binding.ParameterName == "store")
            .ToArray();
        Assert.Equal(2, storeBindings.Length);
        Assert.Equal(
            storeRegistrations.Select(registration => registration.Id.Value).Order(StringComparer.Ordinal),
            storeBindings.Select(binding => binding.RegistrationId.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ScopedSingletonAndTransientLifetimesAreExact()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var registrations = extraction.DependencyInjectionFacts.Registrations;

        // Non-vacuous: the fixture admits exactly four registrations, and each lifetime maps to the
        // exact service types declared in Program.cs.
        Assert.Equal(4, registrations.Length);

        var scoped = registrations.Where(registration => registration.Lifetime == DependencyInjectionLifetime.Scoped).ToArray();
        Assert.Equal(2, scoped.Length);
        Assert.All(scoped, registration => Assert.Equal(GadgetStoreServiceType, registration.ServiceType));

        var singleton = Assert.Single(
            registrations.Where(registration => registration.Lifetime == DependencyInjectionLifetime.Singleton));
        Assert.Equal(ClockServiceType, singleton.ServiceType);
        Assert.Equal("BehaviorDocumentation.DependencyInjection.Services.SystemClock", singleton.ImplementationType);

        var transient = Assert.Single(
            registrations.Where(registration => registration.Lifetime == DependencyInjectionLifetime.Transient));
        Assert.Equal(RepositoryServiceType, transient.ServiceType);
        Assert.Equal("BehaviorDocumentation.DependencyInjection.Services.GadgetRepository", transient.ImplementationType);
    }

    [Fact]
    public async Task FactoryInstanceOpenGenericCollectionKeyedTryAddAndLookalikeFormsFailClosed()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.DependencyInjectionFacts;

        // Only the four admitted receiver-only generic registrations project; factory, instance,
        // non-generic, collection, keyed, TryAdd, and lookalike forms never appear.
        Assert.Equal(4, facts.Registrations.Length);
        Assert.Equal(2, facts.Registrations.Count(registration => registration.ServiceType == GadgetStoreServiceType));
        Assert.Equal(1, facts.Registrations.Count(registration => registration.ServiceType == ClockServiceType));
        Assert.Equal(1, facts.Registrations.Count(registration => registration.ServiceType == RepositoryServiceType));
        Assert.DoesNotContain(facts.Registrations, registration =>
            registration.ImplementationType.Contains("GadgetStoreCollection", StringComparison.Ordinal));

        // Collection-injection constructor parameters never bind to a single registration.
        var collectionType = Assert.Single(extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.DependencyInjection.Services.GadgetCollectionConsumer");
        var collectionConstructor = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == ".ctor" && method.ContainingType == collectionType.Id);
        Assert.DoesNotContain(facts.Bindings, binding => binding.ConstructorMethod == collectionConstructor.Id);
    }

    [Fact]
    public async Task FactsRetainProfileFingerprintEvidenceAndNonPromotedCertainty()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.DependencyInjectionFacts;

        Assert.Equal(extraction.ProgramIndex.Profile.Id, facts.Profile.Id);
        Assert.Equal(extraction.ProgramIndex.IndexFingerprint, facts.ProgramIndexFingerprint);
        Assert.NotEmpty(facts.Registrations);
        Assert.All(facts.Registrations, registration => AssertEvidence(registration.Evidence, registration.Certainty));
        Assert.All(facts.Bindings, binding => AssertEvidence(binding.Evidence, binding.Certainty));
    }

    [Fact]
    public async Task TopLevelProgramRegistrationsUseStableCompanionOnlyIdentity()
    {
        var first = await ExtractSuccessfullyAsync();
        var second = await ExtractSuccessfullyAsync();

        var registration = Assert.Single(
            first.DependencyInjectionFacts.Registrations,
            candidate => candidate.ServiceType == RepositoryServiceType);
        Assert.Equal("BehaviorDocumentation.DependencyInjection.Services.GadgetRepository", registration.ImplementationType);
        Assert.Equal(DependencyInjectionLifetime.Transient, registration.Lifetime);

        // Stable repeated identity: method, operation, and fact ids are deterministic across runs.
        Assert.False(string.IsNullOrWhiteSpace(registration.SourceMethod.Value));
        Assert.False(string.IsNullOrWhiteSpace(registration.Operation.Value));
        var repeated = Assert.Single(
            second.DependencyInjectionFacts.Registrations,
            candidate => candidate.ServiceType == RepositoryServiceType);
        Assert.Equal(registration.SourceMethod, repeated.SourceMethod);
        Assert.Equal(registration.Operation, repeated.Operation);
        Assert.Equal(registration.Id, repeated.Id);

        // Repository-relative source evidence anchored to the top-level Program file.
        Assert.NotEmpty(registration.Evidence);
        Assert.All(registration.Evidence, item => Assert.False(string.IsNullOrWhiteSpace(item.Artifact)));
        Assert.Contains(registration.Evidence, item => item.Artifact.Contains("Program.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(registration.Evidence, item =>
            item.Artifact.Contains(FindRepositoryRoot(), StringComparison.OrdinalIgnoreCase));

        // Companion-only: accepted behavior input never admits the implicit top-level method, and
        // repeated analysis keeps the accepted Program Index fingerprint stable.
        Assert.Equal(4, first.DependencyInjectionFacts.Registrations.Length);
        Assert.Equal(4, second.DependencyInjectionFacts.Registrations.Length);
        Assert.DoesNotContain(first.BehaviorInput.Methods, body =>
            first.ProgramIndex.Methods.FirstOrDefault(method => method.Id == body.Method)?.Name == "<Main>$");
        Assert.Equal(first.ProgramIndex.IndexFingerprint, second.ProgramIndex.IndexFingerprint);
    }

    [Fact]
    public async Task AuthoritativeCompilerSymbolsAdmitOnlyMicrosoftDiAndRejectLookalikeHelper()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.DependencyInjectionFacts;

        Assert.Equal(4, facts.Registrations.Length);
        Assert.Equal(2, facts.Registrations.Count(registration => registration.ServiceType == GadgetStoreServiceType));
        Assert.Equal(1, facts.Registrations.Count(registration => registration.ServiceType == ClockServiceType));
        Assert.Equal(1, facts.Registrations.Count(registration => registration.ServiceType == RepositoryServiceType));

        // The lookalike helper shares the simple type and method names with the Microsoft class but
        // never anchors a fact because admission compares authoritative compiler symbols.
        var lookalikeType = Assert.Single(extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.DependencyInjection.Lookalikes.ServiceCollectionServiceExtensions");
        var lookalikeMethod = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == "AddScoped" && method.ContainingType == lookalikeType.Id);
        Assert.DoesNotContain(facts.Registrations, registration => registration.SourceMethod == lookalikeMethod.Id);

        // Every admitted fact is anchored to a real Microsoft-DI source call in the fixture files.
        Assert.All(facts.Registrations, registration => Assert.Contains(registration.Evidence, item =>
            item.Artifact.Contains("ServiceRegistration.cs", StringComparison.Ordinal)
            || item.Artifact.Contains("Program.cs", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BindingsCombineAndDeduplicateRegistrationAndControllerConstructorEvidence()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var controllerType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == ControllerMetadataName);
        var constructor = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == ".ctor" && method.ContainingType == controllerType.Id);
        var bindings = extraction.DependencyInjectionFacts.Bindings
            .Where(binding => binding.ConstructorMethod == constructor.Id)
            .ToArray();
        Assert.Equal(3, bindings.Length);

        foreach (var binding in bindings)
        {
            // Both source anchors are present: the controller-constructor declaration and the
            // registration call. Evidence is deduplicated by id and canonically ordered.
            Assert.Contains(binding.Evidence, item => item.Artifact.Contains("GadgetsController.cs", StringComparison.Ordinal));
            Assert.Contains(binding.Evidence, item =>
                item.Artifact.Contains("ServiceRegistration.cs", StringComparison.Ordinal)
                || item.Artifact.Contains("Program.cs", StringComparison.Ordinal));
            Assert.Equal(binding.Evidence.Length, binding.Evidence.Select(item => item.Id).Distinct().Count());
            Assert.Equal(
                binding.Evidence.OrderBy(item => item.Id.Value, StringComparer.Ordinal).Select(item => item.Id.Value),
                binding.Evidence.Select(item => item.Id.Value));
        }
    }

    [Fact]
    public async Task ConstructorCandidatesAreLimitedToExactAdmittedControllers()
    {
        var extraction = await ExtractSuccessfullyAsync();
        var facts = extraction.DependencyInjectionFacts;

        var controllerType = Assert.Single(extraction.ProgramIndex.Types, type => type.MetadataName == ControllerMetadataName);
        var controllerConstructor = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == ".ctor" && method.ContainingType == controllerType.Id);
        Assert.Equal(3, facts.Bindings.Count(binding => binding.ConstructorMethod == controllerConstructor.Id));

        // An unrelated class with a same-service constructor is not an admitted controller and never
        // binds, even though IGadgetStore exactly matches an admitted registration.
        var reporterType = Assert.Single(extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.DependencyInjection.Services.GadgetReporter");
        var reporterConstructor = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == ".ctor" && method.ContainingType == reporterType.Id);
        Assert.DoesNotContain(facts.Bindings, binding => binding.ConstructorMethod == reporterConstructor.Id);

        // A non-controller collection-injection constructor is also never a candidate.
        var collectionType = Assert.Single(extraction.ProgramIndex.Types,
            type => type.MetadataName == "BehaviorDocumentation.DependencyInjection.Services.GadgetCollectionConsumer");
        var collectionConstructor = Assert.Single(extraction.ProgramIndex.Methods,
            method => method.Name == ".ctor" && method.ContainingType == collectionType.Id);
        Assert.DoesNotContain(facts.Bindings, binding => binding.ConstructorMethod == collectionConstructor.Id);
    }

    [Fact]
    public void RepresentativeDependencyInjectionConstructorRejectsEmptyEvidenceAndUnknownCertainty()
    {
        var id = new SemanticFactId("di-fact-test-id");
        var method = new MethodId("method-test");
        var operation = new OperationId("operation-test");

        Assert.Throws<ArgumentException>(() => new DependencyInjectionRegistrationFact(
            id,
            method,
            operation,
            "Test.IService",
            "Test.Service",
            DependencyInjectionLifetime.Scoped,
            [],
            CertaintyLevel.Exact));

        Assert.Throws<ArgumentException>(() => new DependencyInjectionRegistrationFact(
            id,
            method,
            operation,
            "Test.IService",
            "Test.Service",
            DependencyInjectionLifetime.Scoped,
            [CreateSourceEvidence(CertaintyLevel.Conservative)],
            CertaintyLevel.Exact));
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

    private static async Task<FrameworkAnalysisResult> ComposeAsync(ProfileAnalysisExtraction extraction)
    {
        var host = new FrameworkModelHost([new AspNetCoreControllerModel()]);
        return await host.AnalyzeAsync(
            new FrameworkAnalysisRequest(
                new FrameworkDetectionContext(extraction.ProgramIndex.Profile, extraction.ProgramIndex),
                new FrameworkAnalysisContext(extraction.ProgramIndex.Profile, extraction.ProgramIndex),
                extraction.Operations,
                extraction.Symbols),
            CancellationToken.None);
    }

    private static string CollectFactIds(DependencyInjectionFactSet facts) => string.Join(
        "\n",
        facts.Registrations
            .Select(registration => registration.Id.Value)
            .Concat(facts.Bindings.Select(binding => binding.Id.Value))
            .Order(StringComparer.Ordinal));

    private static EvidenceRef CreateSourceEvidence(CertaintyLevel certainty) => new(
        new EvidenceId("evidence-test-id"),
        EvidenceKind.Source,
        "test-artifact",
        new SourceRange(
            new DocumentId("document-test-id"),
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
