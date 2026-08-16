using System.Collections.Immutable;
using SeqDoc.Application.Persistence;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.ScenarioGraph;

namespace SeqDoc.Application.Analysis;

public enum CatalogKind
{
    All,
    Project,
    Document,
    Type,
    Method,
    Reference,
    Invocation,
}

public sealed record PassAAnalysisSummary(
    string ToolchainVersion,
    ImmutableArray<string> AvailableTargetFrameworks,
    ImmutableArray<ActivatedProfileRun> Runs,
    ImmutableArray<ProgramIndexCounts> Counts,
    ImmutableArray<CompanionInspection> CompanionInspections = default);

/// <summary>
/// Deterministic memory-only inspection of one profile's companion facts. Returned only after the
/// accepted snapshot activates successfully; a failed analysis or activation never yields a
/// companion success result. The actual memory-only scenario-graph set is exposed beside the debug
/// projections so command handlers can plan documentation without re-running analysis or persisting
/// any companion fact.
/// </summary>
public sealed record CompanionInspection(
    CompilationProfileId ProfileId,
    string SemanticDebugProjection,
    string FrameworkDebugProjection,
    string DependencyInjectionDebugProjection,
    string StructuralResultDebugProjection,
    string ScenarioDebugProjection,
    ScenarioGraphSet ScenarioGraphs);

public sealed record ProgramIndexCounts(
    CompilationProfileId ProfileId,
    int Projects,
    int Documents,
    int Types,
    int Methods,
    int References,
    int Invocations,
    int Diagnostics);

public sealed record CatalogQuery(
    CompilationProfileResolutionRequest ProfileRequest,
    CatalogKind Kind = CatalogKind.All,
    string? Text = null,
    string? IdPrefix = null);

public sealed record CatalogItem(
    string Kind,
    string Id,
    string Name,
    string? Context,
    string Detail,
    CompilationProfileId ProfileId);

public sealed record CatalogResult(
    ImmutableArray<CatalogItem> Items,
    ImmutableArray<ActiveProgramIndex> ActiveIndexes);

public sealed record SolutionInspection(
    string TargetPath,
    string ToolchainVersion,
    ImmutableArray<string> AvailableTargetFrameworks,
    ImmutableArray<InspectedProfile> Profiles);

public sealed record InspectedProfile(
    CompilationProfileId ProfileId,
    AnalysisRunId RunId,
    string TargetFramework,
    string Configuration,
    int SchemaVersion,
    string ProducerVersion,
    string InputManifestHash,
    string IndexFingerprint,
    ProgramIndexCounts Counts,
    BehaviorInspectionCounts? Behavior,
    ImmutableArray<InspectedProject> Projects,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

public sealed record InspectedProject(ProjectId Id, string Name, string Path, string Status);

/// <summary>Reports behavior snapshot availability and counts for one profile run.</summary>
public sealed record BehaviorInspectionCounts(
    bool Available,
    string? BehaviorFingerprint,
    int MethodFlows,
    int CallSites,
    int CallEdges);

/// <summary>Composes the verified analysis ports without leaking adapter types into command handlers.</summary>
public sealed class PassAWorkflow(
    ICompilationProfileResolver profileResolver,
    IProgramIndexBuilder indexBuilder,
    IProgramIndexStore indexStore,
    IAnalysisStore? analysisStore = null,
    IAnalysisBuilder? analysisBuilder = null)
{
    public async Task<ApplicationResult<PassAAnalysisSummary>> AnalyzeAsync(
        CompilationProfileResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var resolution = await profileResolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        if (!resolution.IsSuccess)
        {
            return ApplicationResult.Failure<PassAAnalysisSummary>(resolution.Outcome, resolution.Diagnostics);
        }

        var resolved = resolution.Value!;
        var profiles = resolved.Profiles.OrderBy(profile => profile.Id.Value, StringComparer.Ordinal).ToArray();
        if (analysisStore is not null && analysisBuilder is not null)
        {
            return await AnalyzeAggregateAsync(
                request,
                resolved,
                profiles,
                analysisBuilder,
                analysisStore,
                cancellationToken).ConfigureAwait(false);
        }

        var buildResults = new ApplicationResult<ProgramIndexSnapshot>[profiles.Length];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, profiles.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = request.MaxParallelism,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                buildResults[index] = await indexBuilder.BuildAsync(
                    new CompilationAnalysisRequest(request.RepositoryRoot, request.TargetPath, profiles[index]),
                    token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var snapshots = ImmutableArray.CreateBuilder<ProgramIndexSnapshot>(profiles.Length);
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        ApplicationOutcome? failureOutcome = null;
        foreach (var result in buildResults)
        {
            diagnostics.AddRange(result.Diagnostics);
            if (!result.IsSuccess)
            {
                failureOutcome ??= result.Outcome;
                continue;
            }

            snapshots.Add(result.Value!);
        }

        if (failureOutcome is not null)
        {
            return ApplicationResult.Failure<PassAAnalysisSummary>(failureOutcome.Value, diagnostics.ToImmutable());
        }

        var activation = await indexStore.ActivateAsync(
            new ProgramIndexPersistenceRequest(snapshots.ToImmutable()),
            cancellationToken).ConfigureAwait(false);
        diagnostics.AddRange(activation.Diagnostics);
        if (!activation.IsSuccess)
        {
            return ApplicationResult.Failure<PassAAnalysisSummary>(activation.Outcome, diagnostics.ToImmutable());
        }

        return ApplicationResult.Success(
            new PassAAnalysisSummary(
                resolved.ToolchainVersion,
                resolved.AvailableTargetFrameworks,
                activation.Value!.Runs,
                snapshots.Select(CreateCounts).ToImmutableArray()),
            diagnostics.ToImmutable());
    }

    private static async Task<ApplicationResult<PassAAnalysisSummary>> AnalyzeAggregateAsync(
        CompilationProfileResolutionRequest request,
        ResolvedCompilationProfiles resolved,
        CompilationProfile[] profiles,
        IAnalysisBuilder analysisBuilder,
        IAnalysisStore analysisStore,
        CancellationToken cancellationToken)
    {
        var buildResults = new ApplicationResult<AnalysisProfileCandidate>[profiles.Length];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, profiles.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = request.MaxParallelism,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                buildResults[index] = await analysisBuilder.BuildAsync(
                    new CompilationAnalysisRequest(request.RepositoryRoot, request.TargetPath, profiles[index]),
                    token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var candidates = ImmutableArray.CreateBuilder<AnalysisProfileCandidate>(profiles.Length);
        var snapshots = ImmutableArray.CreateBuilder<AnalysisProfileSnapshot>(profiles.Length);
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        ApplicationOutcome? failureOutcome = null;
        foreach (var result in buildResults)
        {
            diagnostics.AddRange(result.Diagnostics);
            if (!result.IsSuccess)
            {
                failureOutcome ??= result.Outcome;
                continue;
            }

            // Persistence receives only the unchanged accepted snapshot; the memory-only companions
            // never cross the persistence boundary.
            candidates.Add(result.Value!);
            snapshots.Add(result.Value!.Snapshot);
        }

        if (failureOutcome is not null)
        {
            return ApplicationResult.Failure<PassAAnalysisSummary>(failureOutcome.Value, diagnostics.ToImmutable());
        }

        var activation = await analysisStore.ActivateAsync(
            new AnalysisPersistenceRequest(snapshots.ToImmutable()),
            cancellationToken).ConfigureAwait(false);
        diagnostics.AddRange(activation.Diagnostics);
        if (!activation.IsSuccess)
        {
            // A failed activation returns no companion success result.
            return ApplicationResult.Failure<PassAAnalysisSummary>(activation.Outcome, diagnostics.ToImmutable());
        }

        return ApplicationResult.Success(
            new PassAAnalysisSummary(
                resolved.ToolchainVersion,
                resolved.AvailableTargetFrameworks,
                activation.Value!.Runs,
                snapshots.Select(snapshot => CreateCounts(snapshot.ProgramIndex)).ToImmutableArray(),
                candidates.Select(BuildCompanionInspection).ToImmutableArray()),
            diagnostics.ToImmutable());
    }

    private static CompanionInspection BuildCompanionInspection(AnalysisProfileCandidate candidate) => new(
        candidate.Snapshot.ProgramIndex.Profile.Id,
        candidate.SemanticFacts.DebugProjection,
        BuildFrameworkDebugProjection(candidate.FrameworkFacts),
        candidate.DependencyInjectionFacts.DebugProjection,
        candidate.StructuralResultFacts.DebugProjection,
        candidate.ScenarioGraphs.DebugProjection,
        candidate.ScenarioGraphs);

    private static string BuildFrameworkDebugProjection(FrameworkAnalysisResult result)
    {
        var lines = result.AppliedModels
            .Select(model => $"applied-model {model.ModelId} {model.Version}")
            .Concat(result.Facts.Select(fact => $"fact {fact.Id.Value} type={fact.GetType().Name} certainty={fact.Certainty}"))
            .Concat(result.Facts
                .OfType<EntityFrameworkQueryFact>()
                .Select(fact => $"ef-query {fact.Id.Value} method={fact.Method.Value} entity={fact.EntityType} chain={string.Join(",", fact.Chain.Select(item => item.OperatorKind.ToString()))} predicate={fact.PredicateOperation?.Value ?? "none"}"))
            .Concat(result.ResolutionHints.Select(hint => $"resolution-hint {hint.SourceOperation.Value} target={hint.TargetMethod?.Value ?? hint.TargetType?.Value ?? string.Empty} reason={hint.Reason}"))
            .Concat(result.SuppressionHints.Select(hint => $"suppression-hint {hint.Ordinal} scope={hint.Scope}"))
            .Concat(result.SummaryRules.Select(rule => $"summary-rule {rule.Ordinal} scope={rule.Scope}"))
            .Concat(result.Diagnostics.Select(diagnostic => $"diagnostic {diagnostic.Code} {diagnostic.Id.Value}"));
        return string.Join('\n', lines.Order(StringComparer.Ordinal));
    }

    public async Task<ApplicationResult<CatalogResult>> CatalogAsync(
        CatalogQuery query,
        CancellationToken cancellationToken)
    {
        var activeResult = await FindActiveAsync(query.ProfileRequest, cancellationToken).ConfigureAwait(false);
        if (!activeResult.IsSuccess)
        {
            return ApplicationResult.Failure<CatalogResult>(activeResult.Outcome, activeResult.Diagnostics);
        }

        var indexes = activeResult.Value!.Indexes;

        var items = indexes
            .SelectMany(index => Project(index.Snapshot, query.Kind))
            .Where(item => query.Text is null || MatchesText(item, query.Text))
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToImmutableArray();

        if (query.IdPrefix is not null)
        {
            var matches = items.Where(item => item.Id.StartsWith(query.IdPrefix, StringComparison.Ordinal)).ToImmutableArray();
            if (matches.Length != 1)
            {
                return ApplicationResult.Failure<CatalogResult>(
                    ApplicationOutcome.InvalidInput,
                    [CreateCommandDiagnostic(
                        matches.IsEmpty ? "SD4002" : "SD4003",
                        matches.IsEmpty ? "No catalog ID matches the supplied prefix." : "The catalog ID prefix is ambiguous.",
                        query.IdPrefix,
                        matches.IsEmpty
                            ? "The prefix did not match an active catalog item."
                            : $"The prefix matched {matches.Length} active catalog items.",
                        "Provide a longer unique prefix or a complete catalog ID.")]);
            }

            items = matches;
        }

        return ApplicationResult.Success(new CatalogResult(items, indexes));
    }

    public async Task<ApplicationResult<SolutionInspection>> InspectAsync(
        CompilationProfileResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var activeResult = await FindActiveAsync(request, cancellationToken).ConfigureAwait(false);
        if (!activeResult.IsSuccess)
        {
            return ApplicationResult.Failure<SolutionInspection>(activeResult.Outcome, activeResult.Diagnostics);
        }

        var indexes = activeResult.Value!.Indexes;
        var behaviorByProfile = analysisStore is null
            ? ImmutableDictionary<CompilationProfileId, BehaviorInspectionCounts>.Empty
            : await ReadBehaviorCountsAsync(indexes.Select(index => index.Snapshot.Profile.Id), cancellationToken)
                .ConfigureAwait(false);
        var profiles = indexes.Select(active =>
        {
            var snapshot = active.Snapshot;
            return new InspectedProfile(
                snapshot.Profile.Id,
                active.RunId,
                snapshot.Profile.TargetFramework,
                snapshot.Profile.Configuration,
                snapshot.SchemaVersion,
                snapshot.ProducerVersion,
                snapshot.InputManifestHash,
                snapshot.IndexFingerprint,
                CreateCounts(snapshot),
                behaviorByProfile.GetValueOrDefault(snapshot.Profile.Id),
                snapshot.Projects.Select(project => new InspectedProject(
                        project.Id,
                        project.Name,
                        project.RepositoryRelativePath,
                        "Indexed"))
                    .OrderBy(project => project.Id.Value, StringComparer.Ordinal)
                    .ToImmutableArray(),
                snapshot.Diagnostics);
        }).ToImmutableArray();

        return ApplicationResult.Success(new SolutionInspection(
            request.TargetPath,
            string.Join(", ", indexes.Select(index => index.Snapshot.Profile.AnalysisProperties.TryGetValue(
                    "seqdoc.toolchainVersion",
                    out string? version)
                    ? version
                    : "unknown (reanalyze to record)")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)),
            indexes.Select(index => index.Snapshot.Profile.TargetFramework).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            profiles));
    }

    private async Task<ApplicationResult<ActiveProgramIndexes>> FindActiveAsync(
        CompilationProfileResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var allActive = await indexStore.ReadAllActiveAsync(cancellationToken).ConfigureAwait(false);
        if (!allActive.IsSuccess)
        {
            return allActive;
        }

        var requestedPath = RepositoryRelativePath.Normalize(Path.GetRelativePath(request.RepositoryRoot, request.TargetPath));
        var indexes = allActive.Value!.Indexes.Where(active =>
                string.Equals(active.Snapshot.Profile.RepositoryRelativeTargetPath, requestedPath, StringComparison.Ordinal)
                && string.Equals(active.Snapshot.Profile.Configuration, request.Configuration, StringComparison.Ordinal)
                && string.Equals(active.Snapshot.Profile.RuntimeIdentifier, request.RuntimeIdentifier, StringComparison.Ordinal)
                && MapsEqual(active.Snapshot.Profile.MsBuildProperties, request.MsBuildProperties, ignoreToolchain: false)
                && MapsEqual(active.Snapshot.Profile.AnalysisProperties, request.AnalysisProperties, ignoreToolchain: true)
                && (request.TargetFramework is null
                    || string.Equals(active.Snapshot.Profile.TargetFramework, request.TargetFramework, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(active => active.Snapshot.Profile.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        if (!request.AllTargetFrameworks && request.TargetFramework is null && indexes.Length > 1)
        {
            return ApplicationResult.Failure<ActiveProgramIndexes>(
                ApplicationOutcome.InvalidInput,
                [CreateCommandDiagnostic(
                    "SD4005",
                    "Several active target-framework profiles match the request.",
                    requestedPath,
                    $"Active frameworks: {string.Join(", ", indexes.Select(item => item.Snapshot.Profile.TargetFramework))}.",
                    "Select one framework explicitly or request all frameworks separately.")]);
        }

        return indexes.IsEmpty
            ? ApplicationResult.Failure<ActiveProgramIndexes>(
                ApplicationOutcome.InvalidInput,
                [CreateCommandDiagnostic(
                    "SD4001",
                    "No active Program Index exists for the selected profile.",
                    requestedPath,
                    "The cache has no completed active run for the requested target and profile settings.",
                    "Run 'seqdoc analyze' with the same target and profile options first.")])
            : ApplicationResult.Success(new ActiveProgramIndexes(indexes));
    }

    private static IEnumerable<CatalogItem> Project(ProgramIndexSnapshot snapshot, CatalogKind kind)
    {
        if (kind is CatalogKind.All or CatalogKind.Project)
        {
            foreach (var item in snapshot.Projects)
            {
                yield return new CatalogItem("project", item.Id.Value, item.Name, item.RepositoryRelativePath, item.Kind.ToString(), snapshot.Profile.Id);
            }
        }

        if (kind is CatalogKind.All or CatalogKind.Document)
        {
            foreach (var item in snapshot.Documents)
            {
                yield return new CatalogItem("document", item.Id.Value, item.LogicalPath, item.Project.Value, item.Origin.ToString(), snapshot.Profile.Id);
            }
        }

        if (kind is CatalogKind.All or CatalogKind.Type)
        {
            foreach (var item in snapshot.Types)
            {
                yield return new CatalogItem("type", item.Id.Value, item.MetadataName, item.Project.Value, item.Kind.ToString(), snapshot.Profile.Id);
            }
        }

        if (kind is CatalogKind.All or CatalogKind.Method)
        {
            foreach (var item in snapshot.Methods)
            {
                yield return new CatalogItem("method", item.Id.Value, item.Name, item.ContainingType.Value, item.DisplaySignature, snapshot.Profile.Id);
            }
        }

        if (kind is CatalogKind.All or CatalogKind.Reference)
        {
            foreach (var item in snapshot.References)
            {
                yield return new CatalogItem("reference", item.Id, item.Identity, item.Project.Value, item.Kind.ToString(), snapshot.Profile.Id);
            }
        }

        if (kind is CatalogKind.All or CatalogKind.Invocation)
        {
            foreach (var item in snapshot.Invocations)
            {
                yield return new CatalogItem("invocation", item.Id.Value, item.DisplayTarget, item.ContainingMethod.Value, item.Certainty.ToString(), snapshot.Profile.Id);
            }
        }
    }

    private static bool MatchesText(CatalogItem item, string text) =>
        item.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
        || item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
        || (item.Context?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
        || item.Detail.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool MapsEqual(
        ImmutableSortedDictionary<string, string> left,
        ImmutableSortedDictionary<string, string>? right,
        bool ignoreToolchain)
    {
        var expected = right ?? ImmutableSortedDictionary<string, string>.Empty;
        var actual = ignoreToolchain ? left.Remove("seqdoc.toolchainVersion") : left;
        return actual.Count == expected.Count
            && expected.All(pair => actual.TryGetValue(pair.Key, out string? value)
                && string.Equals(value, pair.Value, StringComparison.Ordinal));
    }

    private async Task<ImmutableDictionary<CompilationProfileId, BehaviorInspectionCounts>> ReadBehaviorCountsAsync(
        IEnumerable<CompilationProfileId> profileIds,
        CancellationToken cancellationToken)
    {
        var result = ImmutableDictionary.CreateBuilder<CompilationProfileId, BehaviorInspectionCounts>();
        if (analysisStore is null)
        {
            return result.ToImmutable();
        }

        foreach (var profileId in profileIds.Distinct())
        {
            var active = await analysisStore.ReadActiveAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (!active.IsSuccess || active.Value is not { Found: true } || active.Value.ActiveProfile?.Behavior is not { } behavior)
            {
                result[profileId] = new BehaviorInspectionCounts(false, null, 0, 0, 0);
                continue;
            }

            result[profileId] = new BehaviorInspectionCounts(
                true,
                behavior.BehaviorFingerprint,
                behavior.MethodFlows.Length,
                behavior.CallGraph.CallSites.Length,
                behavior.CallGraph.Edges.Length);
        }

        return result.ToImmutable();
    }

    private static ProgramIndexCounts CreateCounts(ProgramIndexSnapshot snapshot) => new(
        snapshot.Profile.Id,
        snapshot.Projects.Length,
        snapshot.Documents.Length,
        snapshot.Types.Length,
        snapshot.Methods.Length,
        snapshot.References.Length,
        snapshot.Invocations.Length,
        snapshot.Diagnostics.Length);

    private static AnalysisDiagnostic CreateCommandDiagnostic(
        string code,
        string summary,
        string location,
        string cause,
        string nextAction)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.CommandLine,
            null,
            location,
            0));
        return new AnalysisDiagnostic(
            id,
            code,
            DiagnosticSeverity.Error,
            AnalysisStage.CommandLine,
            summary,
            new DiagnosticLocation(location),
            cause,
            "No cached Program Index facts were returned or changed.",
            nextAction,
            CertaintyLevel.Exact);
    }

}
