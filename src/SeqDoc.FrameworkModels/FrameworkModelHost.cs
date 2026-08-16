using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels;

/// <summary>
/// Deterministic host for versioned framework models. It rejects duplicate registrations, invokes
/// applicable models in descriptor order, validates evidence and producer provenance, and aggregates
/// artifacts into canonical order so registration order and input encounter order never change
/// results.
/// </summary>
public sealed class FrameworkModelHost
{
    private readonly ImmutableArray<IFrameworkBehaviorModel> _models;

    public FrameworkModelHost(IEnumerable<IFrameworkBehaviorModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        _models = SortAndValidate(models);
        Descriptors = _models.Select(model => model.Descriptor).ToImmutableArray();
    }

    public ImmutableArray<FrameworkModelDescriptor> Descriptors { get; }

    public async ValueTask<FrameworkAnalysisResult> AnalyzeAsync(
        FrameworkAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DetectionContext);
        ArgumentNullException.ThrowIfNull(request.AnalysisContext);
        if (request.Operations.IsDefault || request.Symbols.IsDefault)
        {
            throw new ArgumentException("Analysis inputs must be initialized immutable arrays.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var applicable = _models
            .Where(model => model.IsApplicable(request.DetectionContext))
            .ToImmutableArray();

        // Invoke inputs in full canonical identity order so caller-provided request order never
        // influences model invocation or the aggregate.
        var orderedOperations = request.Operations
            .OrderBy(operation => operation.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedSymbols = request.Symbols
            .OrderBy(symbol => symbol.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        var executions = new List<ModelExecution>();
        foreach (var model in applicable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var operation in orderedOperations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                executions.Add(new ModelExecution(
                    model,
                    await model
                        .AnalyzeOperationAsync(operation, request.AnalysisContext, cancellationToken)
                        .ConfigureAwait(false)));
            }

            foreach (var symbol in orderedSymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                executions.Add(new ModelExecution(
                    model,
                    await model
                        .AnalyzeSymbolAsync(symbol, request.AnalysisContext, cancellationToken)
                        .ConfigureAwait(false)));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Aggregate(request.AnalysisContext.Profile.Id, applicable, executions);
    }

    private static FrameworkAnalysisResult Aggregate(
        CompilationProfileId profileId,
        ImmutableArray<IFrameworkBehaviorModel> applicable,
        List<ModelExecution> executions)
    {
        var facts = new List<BehaviorFact>();
        var resolutionHints = new List<CallResolutionHint>();
        var suppressionHints = new List<SuppressionHint>();
        var summaryRules = new List<MethodSummaryRule>();
        var modelDiagnostics = new List<AnalysisDiagnostic>();
        var hostDiagnostics = new List<AnalysisDiagnostic>();
        var retainedFacts = new Dictionary<string, BehaviorFact>(StringComparer.Ordinal);
        var conflictedFactIds = new HashSet<string>(StringComparer.Ordinal);
        var recognized = false;

        foreach (var execution in executions)
        {
            var producer = execution.Model.Descriptor;
            var result = execution.Result;
            if (result is null)
            {
                throw new InvalidOperationException("A framework model returned a null result.");
            }

            recognized |= result.Recognized;

            foreach (var rawFact in result.Facts)
            {
                if (rawFact is null)
                {
                    throw new InvalidOperationException("A framework model returned a null behavior fact.");
                }

                var fact = CanonicalizeFact(rawFact);
                if (!HasEvidence(fact.Evidence) || string.IsNullOrWhiteSpace(fact.Id.Value))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.FactWithoutEvidence(
                        profileId,
                        string.IsNullOrWhiteSpace(fact.Id.Value)
                            ? $"{producer.ModelId}:{producer.Version}:fact-without-id"
                            : fact.Id.Value));
                    continue;
                }

                if (!EvidenceMatchesProducer(fact.Evidence, producer))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.ArtifactProducerMismatch(
                        profileId,
                        $"{producer.ModelId}:{producer.Version}:fact:{fact.Id.Value}"));
                    continue;
                }

                var factId = fact.Id.Value;
                if (conflictedFactIds.Contains(factId))
                {
                    continue;
                }

                if (!retainedFacts.TryGetValue(factId, out var retained))
                {
                    retainedFacts.Add(factId, fact);
                    facts.Add(fact);
                    continue;
                }

                if (SemanticallyEqualFact(retained, fact))
                {
                    // A separately constructed fact with identical ID, payload, certainty, and
                    // evidence values is the same fact; deduplicate without a diagnostic.
                    continue;
                }

                // Same identity with a genuinely different payload is ambiguous. Never keep whichever
                // payload arrived first; exclude every occurrence of this identity and report it once.
                facts.Remove(retained);
                retainedFacts.Remove(factId);
                conflictedFactIds.Add(factId);
                hostDiagnostics.Add(FrameworkModelDiagnostics.ConflictingFact(profileId, factId));
            }

            foreach (var hint in result.ResolutionHints)
            {
                if (hint is null)
                {
                    throw new InvalidOperationException("A framework model returned a null resolution hint.");
                }

                if (!HasEvidence(hint.Evidence))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.ArtifactWithoutEvidence(profileId, ArtifactSubject(producer, hint)));
                    continue;
                }

                if (hint.Ordinal < 0
                    || string.IsNullOrWhiteSpace(hint.SourceOperation.Value)
                    || (hint.TargetMethod is null && hint.TargetType is null)
                    || string.IsNullOrWhiteSpace(hint.Reason)
                    || HasBlankTarget(hint))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.InvalidArtifactValue(profileId, ArtifactSubject(producer, hint)));
                    continue;
                }

                if (!EvidenceMatchesProducer(hint.Evidence, producer))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.ArtifactProducerMismatch(profileId, ArtifactSubject(producer, hint)));
                    continue;
                }

                resolutionHints.Add(hint with { Evidence = CanonicalizeEvidence(hint.Evidence) });
            }

            foreach (var hint in result.SuppressionHints)
            {
                if (hint is null)
                {
                    throw new InvalidOperationException("A framework model returned a null suppression hint.");
                }

                if (!HasEvidence(hint.Evidence))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.ArtifactWithoutEvidence(profileId, ArtifactSubject(producer, hint)));
                    continue;
                }

                if (hint.Ordinal < 0
                    || string.IsNullOrWhiteSpace(hint.Scope)
                    || string.IsNullOrWhiteSpace(hint.Reason))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.InvalidArtifactValue(profileId, ArtifactSubject(producer, hint)));
                    continue;
                }

                if (!EvidenceMatchesProducer(hint.Evidence, producer))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.ArtifactProducerMismatch(profileId, ArtifactSubject(producer, hint)));
                    continue;
                }

                suppressionHints.Add(hint with { Evidence = CanonicalizeEvidence(hint.Evidence) });
            }

            foreach (var rule in result.SummaryRules)
            {
                if (rule is null)
                {
                    throw new InvalidOperationException("A framework model returned a null summary rule.");
                }

                if (!HasEvidence(rule.Evidence))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.ArtifactWithoutEvidence(profileId, ArtifactSubject(producer, rule)));
                    continue;
                }

                if (rule.Ordinal < 0
                    || string.IsNullOrWhiteSpace(rule.Scope)
                    || string.IsNullOrWhiteSpace(rule.Reason))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.InvalidArtifactValue(profileId, ArtifactSubject(producer, rule)));
                    continue;
                }

                if (!EvidenceMatchesProducer(rule.Evidence, producer))
                {
                    hostDiagnostics.Add(FrameworkModelDiagnostics.ArtifactProducerMismatch(profileId, ArtifactSubject(producer, rule)));
                    continue;
                }

                summaryRules.Add(rule with { Evidence = CanonicalizeEvidence(rule.Evidence) });
            }

            modelDiagnostics.AddRange(result.Diagnostics);
        }

        return new FrameworkAnalysisResult(
            recognized,
            facts.OrderBy(fact => fact.Id.Value, StringComparer.Ordinal).ToImmutableArray(),
            resolutionHints
                .OrderBy(hint => hint.Ordinal)
                .ThenBy(hint => hint.SourceOperation.Value, StringComparer.Ordinal)
                .ThenBy(hint => hint.TargetMethod?.Value ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(hint => hint.TargetType?.Value ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(hint => hint.Reason, StringComparer.Ordinal)
                .ThenBy(hint => hint.Certainty)
                .ThenBy(hint => EvidenceKey(hint.Evidence), StringComparer.Ordinal)
                .ToImmutableArray(),
            suppressionHints
                .OrderBy(hint => hint.Ordinal)
                .ThenBy(hint => hint.Scope, StringComparer.Ordinal)
                .ThenBy(hint => hint.Reason, StringComparer.Ordinal)
                .ThenBy(hint => hint.Certainty)
                .ThenBy(hint => EvidenceKey(hint.Evidence), StringComparer.Ordinal)
                .ToImmutableArray(),
            summaryRules
                .OrderBy(rule => rule.Ordinal)
                .ThenBy(rule => rule.Scope, StringComparer.Ordinal)
                .ThenBy(rule => rule.Reason, StringComparer.Ordinal)
                .ThenBy(rule => rule.Certainty)
                .ThenBy(rule => EvidenceKey(rule.Evidence), StringComparer.Ordinal)
                .ToImmutableArray(),
            modelDiagnostics
                .Concat(hostDiagnostics)
                .DistinctBy(diagnostic => diagnostic.Id.Value)
                .OrderBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray(),
            applicable.Select(model => model.Descriptor).ToImmutableArray());
    }

    private static bool HasEvidence(ImmutableArray<EvidenceRef> evidence) => !evidence.IsDefaultOrEmpty;

    private static string EvidenceKey(ImmutableArray<EvidenceRef> evidence)
        => string.Join('\u001f', evidence.Select(item => item.Id.Value));

    /// <summary>
    /// Orders evidence by full EvidenceId ordinal so reversed but semantically equal evidence input
    /// canonicalizes identically before comparison, storage, and sort-key computation.
    /// </summary>
    private static ImmutableArray<EvidenceRef> CanonicalizeEvidence(ImmutableArray<EvidenceRef> evidence)
    {
        if (evidence.IsDefaultOrEmpty || evidence.Length == 1)
        {
            return evidence;
        }

        return evidence.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    private static BehaviorFact CanonicalizeFact(BehaviorFact fact)
    {
        if (fact is not GeneralBehaviorFact general)
        {
            return fact;
        }

        return general with { Evidence = CanonicalizeEvidence(general.Evidence) };
    }

    private static bool HasBlankTarget(CallResolutionHint hint)
        => (hint.TargetMethod is not null && string.IsNullOrWhiteSpace(hint.TargetMethod.Value.Value))
            || (hint.TargetType is not null && string.IsNullOrWhiteSpace(hint.TargetType.Value.Value));

    /// <summary>
    /// Requires every evidence entry of a model-derived artifact to be FrameworkModel evidence naming
    /// the actual producing descriptor and retaining direct source or generated-source provenance.
    /// EvidenceRef construction already enforces its own shape; this check enforces the producer chain.
    /// </summary>
    private static bool EvidenceMatchesProducer(ImmutableArray<EvidenceRef> evidence, FrameworkModelDescriptor producer)
    {
        if (evidence.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var item in evidence)
        {
            if (item.Kind != EvidenceKind.FrameworkModel)
            {
                return false;
            }

            if (!string.Equals(item.ProducerId, producer.ModelId, StringComparison.Ordinal)
                || !string.Equals(item.ProducerVersion, producer.Version, StringComparison.Ordinal))
            {
                return false;
            }

            if (item.UnderlyingEvidence.IsDefaultOrEmpty
                || !item.UnderlyingEvidence.Any(
                    underlying => underlying.Kind is EvidenceKind.Source or EvidenceKind.GeneratedSource))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two facts by canonical payload values, never by record equality, because
    /// ImmutableArray equality is reference-based and not semantic. Unknown derived shapes cannot be
    /// compared conservatively, so they never deduplicate.
    /// </summary>
    private static bool SemanticallyEqualFact(BehaviorFact first, BehaviorFact second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first is not GeneralBehaviorFact firstGeneral || second is not GeneralBehaviorFact secondGeneral)
        {
            return false;
        }

        return string.Equals(firstGeneral.Id.Value, secondGeneral.Id.Value, StringComparison.Ordinal)
            && string.Equals(firstGeneral.Kind, secondGeneral.Kind, StringComparison.Ordinal)
            && string.Equals(firstGeneral.Detail, secondGeneral.Detail, StringComparison.Ordinal)
            && firstGeneral.Certainty == secondGeneral.Certainty
            && EvidenceSemanticallyEqual(firstGeneral.Evidence, secondGeneral.Evidence);
    }

    private static bool EvidenceSemanticallyEqual(ImmutableArray<EvidenceRef> first, ImmutableArray<EvidenceRef> second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (var index = 0; index < first.Length; index++)
        {
            if (!EvidenceRefSemanticallyEqual(first[index], second[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvidenceRefSemanticallyEqual(EvidenceRef first, EvidenceRef second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        return string.Equals(first.Id.Value, second.Id.Value, StringComparison.Ordinal)
            && first.Kind == second.Kind
            && string.Equals(first.Artifact, second.Artifact, StringComparison.Ordinal)
            && SourceRangeSemanticallyEqual(first.Range, second.Range)
            && string.Equals(first.Symbol, second.Symbol, StringComparison.Ordinal)
            && string.Equals(first.Detail, second.Detail, StringComparison.Ordinal)
            && first.Certainty == second.Certainty
            && string.Equals(first.ProducerId, second.ProducerId, StringComparison.Ordinal)
            && string.Equals(first.ProducerVersion, second.ProducerVersion, StringComparison.Ordinal)
            && EvidenceSemanticallyEqual(first.UnderlyingEvidence, second.UnderlyingEvidence);
    }

    private static bool SourceRangeSemanticallyEqual(SourceRange? first, SourceRange? second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        return string.Equals(first.Document.Value, second.Document.Value, StringComparison.Ordinal)
            && first.Start.Line == second.Start.Line
            && first.Start.Column == second.Start.Column
            && first.End.Line == second.End.Line
            && first.End.Column == second.End.Column;
    }

    /// <summary>
    /// Builds a stable diagnostic subject from the artifact's own semantic fields plus the producing
    /// model, certainty, and the complete canonical evidence-ID sequence, so identical defects always
    /// share one diagnostic identity regardless of encounter order and distinct invalid payloads are
    /// never collapsed solely because their primary fields tie.
    /// </summary>
    private static string ArtifactSubject(FrameworkModelDescriptor producer, CallResolutionHint hint)
        => string.Join('\u001f', producer.ModelId, producer.Version, "resolution", hint.SourceOperation.Value, hint.Ordinal, hint.TargetMethod?.Value ?? string.Empty, hint.TargetType?.Value ?? string.Empty, hint.Reason, hint.Certainty, EvidenceKey(CanonicalizeEvidence(hint.Evidence)));

    private static string ArtifactSubject(FrameworkModelDescriptor producer, SuppressionHint hint)
        => string.Join('\u001f', producer.ModelId, producer.Version, "suppression", hint.Ordinal, hint.Scope, hint.Reason, hint.Certainty, EvidenceKey(CanonicalizeEvidence(hint.Evidence)));

    private static string ArtifactSubject(FrameworkModelDescriptor producer, MethodSummaryRule rule)
        => string.Join('\u001f', producer.ModelId, producer.Version, "summary", rule.Ordinal, rule.Scope, rule.Reason, rule.Certainty, EvidenceKey(CanonicalizeEvidence(rule.Evidence)));

    private static ImmutableArray<IFrameworkBehaviorModel> SortAndValidate(
        IEnumerable<IFrameworkBehaviorModel> models)
    {
        var seen = new HashSet<(string ModelId, string Version)>();
        var ordered = new List<IFrameworkBehaviorModel>();
        foreach (var model in models)
        {
            if (model is null)
            {
                throw new ArgumentException("Framework model registrations cannot contain null.", nameof(models));
            }

            var descriptor = model.Descriptor;
            if (descriptor is null)
            {
                throw new ArgumentException("A framework model must expose a descriptor.", nameof(models));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ModelId, nameof(models));
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Version, nameof(models));
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.DisplayName, nameof(models));
            ArgumentOutOfRangeException.ThrowIfNegative(descriptor.Order, nameof(models));

            if (!seen.Add((descriptor.ModelId, descriptor.Version)))
            {
                throw new ArgumentException(
                    $"A framework model with id '{descriptor.ModelId}' and version '{descriptor.Version}' is already registered.",
                    nameof(models));
            }

            ordered.Add(model);
        }

        return ordered
            .OrderBy(model => model.Descriptor.Order)
            .ThenBy(model => model.Descriptor.ModelId, StringComparer.Ordinal)
            .ThenBy(model => model.Descriptor.Version, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private sealed record ModelExecution(IFrameworkBehaviorModel Model, ModelResult Result);
}
