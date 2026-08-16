using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Analysis.Roslyn.Semantics;

/// <summary>
/// Accumulates semantic companion fact drafts during one Roslyn compilation/extraction session and
/// builds the Roslyn-neutral, memory-only <see cref="SemanticFactSet"/>. The collector itself is
/// Roslyn-neutral: every draft carries stable SeqDoc identities and evidence resolved during the
/// same operation traversal that produced the accepted behavior input.
/// </summary>
internal sealed class RoslynSemanticFactCollector
{
    private const string ProducerVersion = "0.1.0-alpha";

    private readonly List<ComparisonDraft> _comparisons = [];
    private readonly List<ArgumentBindingDraft> _argumentBindings = [];
    private readonly List<ReturnProvenanceDraft> _returnProvenances = [];

    public void AddComparison(
        MethodId method,
        OperationId operation,
        ComparisonOperatorKind operatorKind,
        OperationId leftOperation,
        OperationId rightOperation,
        ImmutableArray<EvidenceRef> evidence) =>
        _comparisons.Add(new ComparisonDraft(method, operation, operatorKind, leftOperation, rightOperation, evidence));

    public void AddArgumentBinding(
        MethodId method,
        MethodId targetMethod,
        int parameterOrdinal,
        OperationId argumentOperation,
        ImmutableArray<EvidenceRef> evidence) =>
        _argumentBindings.Add(new ArgumentBindingDraft(
            method,
            targetMethod,
            parameterOrdinal,
            argumentOperation,
            evidence));

    public void AddReturnProvenance(
        MethodId method,
        OperationId valueOperation,
        ImmutableArray<EvidenceRef> evidence) =>
        _returnProvenances.Add(new ReturnProvenanceDraft(method, valueOperation, evidence));

    public SemanticFactSet Build(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));

        var comparisons = ProjectAndDeDuplicate(
            _comparisons.Select(draft => ProjectComparison(profile.Id, draft)),
            fact => fact.Id,
            ComparisonPayloadEquals,
            "comparison");
        var argumentBindings = ProjectAndDeDuplicate(
            _argumentBindings.Select(draft => ProjectArgumentBinding(profile.Id, draft)),
            fact => fact.Id,
            ArgumentBindingPayloadEquals,
            "argument-binding");
        var returnProvenances = ProjectAndDeDuplicate(
            _returnProvenances.Select(draft => ProjectReturnProvenance(profile.Id, draft)),
            fact => fact.Id,
            ReturnProvenancePayloadEquals,
            "return-provenance");
        var debugProjection = BuildDebugProjection(
            profile,
            programIndexFingerprint,
            comparisons,
            argumentBindings,
            returnProvenances,
            diagnostics.Length);

        return new SemanticFactSet(
            1,
            ProducerVersion,
            profile,
            programIndexFingerprint,
            comparisons,
            argumentBindings,
            returnProvenances,
            diagnostics,
            debugProjection);
    }

    /// <summary>
    /// Projects drafts and de-duplicates them by their final semantic fact identity before ordering.
    /// Drafts that project onto the same identity with an identical payload collapse; two different
    /// payloads under one identity are an identity/programming invariant violation and fail closed
    /// with a deterministic exception rather than silently hiding one payload.
    /// </summary>
    private static ImmutableArray<T> ProjectAndDeDuplicate<T>(
        IEnumerable<T> facts,
        Func<T, SemanticFactId> idSelector,
        Func<T, T, bool> payloadEquals,
        string kind)
    {
        var result = new List<T>();
        foreach (var group in facts.GroupBy(fact => idSelector(fact).Value, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = group.ToArray();
            result.Add(ordered[0]);
            for (var index = 1; index < ordered.Length; index++)
            {
                if (!payloadEquals(ordered[0], ordered[index]))
                {
                    var conflictingId = idSelector(ordered[index]);
                    throw new InvalidOperationException(
                        $"Conflicting semantic-fact drafts projected onto identity '{conflictingId.Value}' for kind '{kind}'.");
                }
            }
        }

        return result.ToImmutableArray();
    }

    private static bool ComparisonPayloadEquals(ComparisonSemanticFact left, ComparisonSemanticFact right) =>
        left.Method == right.Method
        && left.Operator == right.Operator
        && left.Operation == right.Operation
        && left.LeftOperation == right.LeftOperation
        && left.RightOperation == right.RightOperation;

    private static bool ArgumentBindingPayloadEquals(ArgumentBindingSemanticFact left, ArgumentBindingSemanticFact right) =>
        left.Method == right.Method
        && left.TargetMethod == right.TargetMethod
        && left.ParameterOrdinal == right.ParameterOrdinal
        && left.ArgumentOperation == right.ArgumentOperation;

    private static bool ReturnProvenancePayloadEquals(ReturnProvenanceSemanticFact left, ReturnProvenanceSemanticFact right) =>
        left.Method == right.Method
        && left.ValueOperation == right.ValueOperation;

    private static ComparisonSemanticFact ProjectComparison(CompilationProfileId profileId, ComparisonDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "comparison",
            draft.Method,
            draft.Operation,
            draft.OperatorKind.ToString()));
        return new ComparisonSemanticFact(
            id,
            draft.Method,
            draft.OperatorKind,
            draft.Operation,
            draft.LeftOperation,
            draft.RightOperation,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static ArgumentBindingSemanticFact ProjectArgumentBinding(CompilationProfileId profileId, ArgumentBindingDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "argument-binding",
            draft.Method,
            draft.ArgumentOperation,
            $"{draft.TargetMethod.Value}|{draft.ParameterOrdinal.ToString(CultureInfo.InvariantCulture)}"));
        return new ArgumentBindingSemanticFact(
            id,
            draft.Method,
            draft.TargetMethod,
            draft.ParameterOrdinal,
            draft.ArgumentOperation,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static ReturnProvenanceSemanticFact ProjectReturnProvenance(CompilationProfileId profileId, ReturnProvenanceDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "return-provenance",
            draft.Method,
            draft.ValueOperation,
            null));
        return new ReturnProvenanceSemanticFact(
            id,
            draft.Method,
            draft.ValueOperation,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static string BuildDebugProjection(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<ComparisonSemanticFact> comparisons,
        ImmutableArray<ArgumentBindingSemanticFact> argumentBindings,
        ImmutableArray<ReturnProvenanceSemanticFact> returnProvenances,
        int diagnosticCount)
    {
        var lines = new List<(string Id, string Line)>();
        foreach (var fact in comparisons)
        {
            lines.Add((fact.Id.Value, $"comparison {fact.Id.Value} method={fact.Method.Value} operator={fact.Operator.ToString()} operation={fact.Operation.Value} left={fact.LeftOperation.Value} right={fact.RightOperation.Value}"));
        }

        foreach (var fact in argumentBindings)
        {
            lines.Add((fact.Id.Value, $"argument-binding {fact.Id.Value} method={fact.Method.Value} target={fact.TargetMethod.Value} parameterOrdinal={fact.ParameterOrdinal.ToString(CultureInfo.InvariantCulture)} operation={fact.ArgumentOperation.Value}"));
        }

        foreach (var fact in returnProvenances)
        {
            lines.Add((fact.Id.Value, $"return-provenance {fact.Id.Value} method={fact.Method.Value} operation={fact.ValueOperation.Value}"));
        }

        var builder = new StringBuilder();
        builder.Append("semantic-facts:v1").Append('\n');
        builder.Append("producer=").Append(ProducerVersion).Append('\n');
        builder.Append("profile=").Append(profile.Id.Value).Append('\n');
        builder.Append("programIndexFingerprint=").Append(programIndexFingerprint).Append('\n');
        builder.Append("diagnosticCount=").Append(diagnosticCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var line in lines.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append(line.Line).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    private sealed record ComparisonDraft(
        MethodId Method,
        OperationId Operation,
        ComparisonOperatorKind OperatorKind,
        OperationId LeftOperation,
        OperationId RightOperation,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record ArgumentBindingDraft(
        MethodId Method,
        MethodId TargetMethod,
        int ParameterOrdinal,
        OperationId ArgumentOperation,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record ReturnProvenanceDraft(
        MethodId Method,
        OperationId ValueOperation,
        ImmutableArray<EvidenceRef> Evidence);
}
