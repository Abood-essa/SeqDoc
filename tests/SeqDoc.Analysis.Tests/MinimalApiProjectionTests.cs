using System.Collections.Immutable;
using SeqDoc.Analysis.Roslyn;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using Xunit;

namespace SeqDoc.Analysis.Tests;

[Collection(MsBuildIntegrationGroup.Name)]
public sealed class MinimalApiProjectionTests
{
    [Fact]
    public async Task CompilerProjectionCarriesLiteralRouteAndCallbackBodyAnchor()
    {
        var root = FindRoot();
        var path = "tests/fixtures/CorpusRoadmap/MinimalApis/MinimalApis.csproj";
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)),
                CompilationProfile.Create(path, "Release", "net10.0")), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.TechnicalCause}: {diagnostic.Summary}")));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);
        var maps = extraction.Operations.Where(operation => operation.TargetIdentity?.MethodMetadataName is "MapGet" or "MapPost" or "MapPut" or "MapDelete").ToArray();
        var item = Assert.Single(maps, operation => operation.TargetIdentity?.MethodMetadataName == "MapGet"
            && operation.ConstantArguments.Any(argument => argument.Value == "/items"));
        Assert.NotNull(item.CallbackTarget);
        Assert.Equal("/api", Assert.Single(item.RouteGroup?.Prefixes ?? ImmutableArray<string>.Empty));

        var telecom = Assert.Single(maps, operation => operation.ConstantArguments.Any(argument => argument.Value == "/telecom"));
        Assert.NotNull(telecom.Document);
        Assert.True(telecom.SourceStart > 0);
        Assert.True(telecom.SourceLength > 0);
    }

    [Fact]
    public async Task TopLevelCallbacksCarryBoundaryIdentityAndExactKind()
    {
        var extraction = await ExtractFixture();
        var maps = extraction.Operations.Where(operation => operation.TargetIdentity?.MethodMetadataName is "MapGet" or "MapPost" or "MapPut" or "MapDelete");

        var anonymous = Assert.Single(maps, operation => operation.ConstantArguments.Any(argument => argument.Value == "/anonymous"));
        Assert.NotNull(anonymous.CallbackTarget);
        Assert.Equal(CallbackTargetKind.AnonymousFunction, anonymous.CallbackTarget!.Kind);
        Assert.NotNull(anonymous.CallbackTarget.CallbackBoundaryId);

        var local = Assert.Single(maps, operation => operation.ConstantArguments.Any(argument => argument.Value == "/local"));
        Assert.NotNull(local.CallbackTarget);
        Assert.Equal(CallbackTargetKind.LocalFunction, local.CallbackTarget!.Kind);
        Assert.NotNull(local.CallbackTarget.TargetMethod);
        Assert.NotNull(local.CallbackTarget.CallbackBoundaryId);
    }

    [Fact]
    public async Task AnonymousTelecomHandlerProjectsTypedBodyPredicatesDelayAndArmOrderWithoutMethodFlow()
    {
        // The fixture intentionally exercises both the pinned object and generic Results overloads.
        var extraction = await ExtractFixture();
        var facts = extraction.MinimalApiHandlerFacts;
        Assert.NotNull(facts);
        Assert.NotEmpty(facts!.Facts);
        var fact = Assert.Single(facts.Facts.Where(item =>
            item.Parameters.Any(parameter => parameter.TypeName == "SmsRequest")
            && item.Outcomes.Length == 3));

        Assert.Equal("SmsRequest", Assert.Single(fact.Parameters, parameter => parameter.TypeName == "SmsRequest").TypeName);
        Assert.Equal(HttpBindingKind.CancellationToken,
            Assert.Single(fact.Parameters, parameter => parameter.Name == "cancellationToken").BindingKind);
        Assert.Equal([30, 50], fact.Predicates.Select(predicate => predicate.Constant).ToArray());
        Assert.Equal(11000, Assert.Single(fact.Operations.Where(operation => operation.Kind == MinimalApiHandlerOperationKind.Delay)).DelayMilliseconds);
        Assert.Equal([500, 200, 200], fact.Outcomes.Select(outcome => outcome.StatusCode));
        // Source ordinals cover every admitted handler operation: the delay occupies ordinal 1,
        // so outcome ordinals must remain [0, 2, 3] rather than being renumbered as outcomes only.
        Assert.Equal([0, 2, 3], fact.Outcomes.Select(outcome => outcome.Arm.SourceOrdinal));
        Assert.Contains(fact.Outcomes, outcome => outcome.FactoryIdentity.StartsWith("Microsoft.AspNetCore.Http.Results.Ok<", StringComparison.Ordinal));
        Assert.Contains(fact.Outcomes, outcome => outcome.FactoryIdentity.Contains("Ok(object", StringComparison.Ordinal));
        Assert.DoesNotContain(fact.Outcomes, outcome => outcome.StatusCode is null);
        Assert.DoesNotContain(extraction.ProgramIndex.Methods, method => method.Id == fact.HandlerRoot);
    }

    [Fact]
    public async Task GroupedHandlerProjectsRouteCancellationAndUnsupportedBinderPartitions()
    {
        var extraction = await ExtractFixture();
        var facts = extraction.MinimalApiHandlerFacts;
        Assert.NotNull(facts);
        var fact = Assert.Single(facts!.Facts, item => item.Parameters.Any(parameter => parameter.Name == "id")
            && item.Parameters.Any(parameter => parameter.Name == "cancellationToken")
            && item.Parameters.Any(parameter => parameter.Name == "custom"));

        Assert.Equal(HttpBindingKind.Route, Assert.Single(fact.Parameters, parameter => parameter.Name == "id").BindingKind);
        Assert.Equal(HttpBindingKind.CancellationToken, Assert.Single(fact.Parameters, parameter => parameter.Name == "cancellationToken").BindingKind);
        Assert.Equal(HttpBindingKind.Unknown, Assert.Single(fact.Parameters, parameter => parameter.Name == "custom").BindingKind);
    }

    [Fact]
    public async Task LookalikeRegistrationIsNotAHandlerAndComplexServiceLikeParameterStaysUnknown()
    {
        var extraction = await ExtractFixture();
        var facts = Assert.IsType<MinimalApiHandlerFactSet>(extraction.MinimalApiHandlerFacts).Facts;

        var lookalike = Assert.Single(extraction.Operations,
            operation => operation.ConstantArguments.Any(argument => argument.Value == "/lookalike-lambda"));
        Assert.DoesNotContain(facts, fact => fact.BoundaryId == lookalike.CallbackTarget?.CallbackBoundaryId);

        var serviceRoute = Assert.Single(extraction.Operations,
            operation => operation.ConstantArguments.Any(argument => argument.Value == "/service-like"));
        var serviceFact = Assert.Single(facts, fact => fact.BoundaryId == serviceRoute.CallbackTarget?.CallbackBoundaryId);
        Assert.Equal(HttpBindingKind.Unknown, Assert.Single(serviceFact.Parameters).BindingKind);

        var telecom = Assert.Single(facts, fact => fact.Parameters.Any(parameter => parameter.TypeName == "SmsRequest"));
        Assert.Equal(HttpBindingKind.Body, Assert.Single(telecom.Parameters, parameter => parameter.TypeName == "SmsRequest").BindingKind);
    }

    [Fact]
    public async Task NonconstantProblemStatusIsWithheldWithStableDiagnostic()
    {
        var extraction = await ExtractFixture();
        var facts = Assert.IsType<MinimalApiHandlerFactSet>(extraction.MinimalApiHandlerFacts).Facts;
        var route = Assert.Single(extraction.Operations,
            operation => operation.ConstantArguments.Any(argument => argument.Value == "/dynamic-problem"));
        var fact = Assert.Single(facts, item => item.BoundaryId == route.CallbackTarget?.CallbackBoundaryId);

        Assert.Empty(fact.Outcomes);
        Assert.Contains(extraction.MinimalApiHandlerFacts.Diagnostics, diagnostic => diagnostic.Code == "MA002");
    }

    [Fact]
    public async Task NonterminatingFirstGuardDoesNotNarrowTheFollowingRelationalPattern()
    {
        var extraction = await ExtractFixture();
        var facts = Assert.IsType<MinimalApiHandlerFactSet>(extraction.MinimalApiHandlerFacts).Facts;
        var route = Assert.Single(extraction.Operations,
            operation => operation.ConstantArguments.Any(argument => argument.Value == "/nonterminating-pattern"));
        var fact = Assert.Single(facts, item => item.BoundaryId == route.CallbackTarget?.CallbackBoundaryId);

        Assert.All(fact.Predicates, predicate => Assert.Null(predicate.TrueArm.DecisionOrdinal));
        Assert.All(fact.Operations, operation => Assert.DoesNotContain("IsAllowed", operation.TargetIdentity ?? "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsupportedFirstGuardWithholdsItsArmAndPreservesStableDiagnosticAndOrdinal()
    {
        var extraction = await ExtractFixture();
        var facts = Assert.IsType<MinimalApiHandlerFactSet>(extraction.MinimalApiHandlerFacts).Facts;
        var route = Assert.Single(extraction.Operations,
            operation => operation.ConstantArguments.Any(argument => argument.Value == "/unsupported-then-supported"));
        var fact = Assert.Single(facts, item => item.BoundaryId == route.CallbackTarget?.CallbackBoundaryId);

        Assert.Equal(["x is at most 5"], fact.Predicates.Select(predicate => predicate.PredicateText));
        Assert.DoesNotContain(fact.Operations, operation => operation.TargetIdentity?.Contains("IsAllowed", StringComparison.Ordinal) == true);
        Assert.Contains(extraction.MinimalApiHandlerFacts.Diagnostics, diagnostic => diagnostic.Code == "MA003");
    }

    [Fact]
    public async Task ReassignedGroupReceiverFailsClosedWithoutRouteGroup()
    {
        var root = FindRoot();
        var path = "tests/fixtures/CorpusRoadmap/MinimalApis/MinimalApis.csproj";
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)),
                CompilationProfile.Create(path, "Release", "net10.0")), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.TechnicalCause}: {diagnostic.Summary}")));
        var extraction = Assert.IsType<ProfileAnalysisExtraction>(result.Value);
        var reassigned = Assert.Single(extraction.Operations,
            operation => operation.ConstantArguments.Any(argument => argument.Value == "/reassigned"));

        Assert.Null(reassigned.RouteGroup);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SeqDoc.slnx")))
        {
            directory = directory.Parent;
        }
        return directory!.FullName;
    }

    private static async Task<ProfileAnalysisExtraction> ExtractFixture()
    {
        var root = FindRoot();
        var path = "tests/fixtures/CorpusRoadmap/MinimalApis/MinimalApis.csproj";
        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(root, Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)),
                CompilationProfile.Create(path, "Release", "net10.0")), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.TechnicalCause}: {diagnostic.Summary}")));
        return Assert.IsType<ProfileAnalysisExtraction>(result.Value);
    }

    [Fact]
    public async Task PreCancelledExtractionReturnsCancelledOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new RoslynProfileAnalysisExtractor().ExtractAsync(
            new CompilationAnalysisRequest(
                FindRoot(),
                Path.Combine(FindRoot(), "tests/fixtures/CorpusRoadmap/MinimalApis/MinimalApis.csproj"),
                CompilationProfile.Create("tests/fixtures/CorpusRoadmap/MinimalApis/MinimalApis.csproj", "Release", "net10.0")),
            cancellation.Token);

        Assert.Equal(ApplicationOutcome.Cancelled, result.Outcome);
    }
}
