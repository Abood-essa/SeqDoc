using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;

namespace SeqDoc.Application.Analysis;

/// <summary>Classifies the stable process outcome used by CLI exit-code mapping.</summary>
public enum ApplicationOutcome
{
    Succeeded,
    InvalidInput,
    BuildFailure,
    AnalysisFailure,
    DocumentationGenerationFailure,
    ValidationFailure,
    UnsupportedRequiredFeature,
    PersistenceFailure,
    Cancelled,
}

/// <summary>Returns a typed use-case value and its structured diagnostics without infrastructure exceptions.</summary>
public sealed record ApplicationResult<T>
{
    internal ApplicationResult(
        ApplicationOutcome outcome,
        T? value,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        Outcome = outcome;
        Value = value;
        Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
    }

    public ApplicationOutcome Outcome { get; }

    public T? Value { get; }

    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    public bool IsSuccess => Outcome == ApplicationOutcome.Succeeded;

}

public static class ApplicationResult
{
    public static ApplicationResult<T> Success<T>(
        T value,
        ImmutableArray<AnalysisDiagnostic> diagnostics = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ApplicationResult<T>(ApplicationOutcome.Succeeded, value, diagnostics);
    }

    public static ApplicationResult<T> Failure<T>(
        ApplicationOutcome outcome,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        if (outcome == ApplicationOutcome.Succeeded)
        {
            throw new ArgumentException("A failure result cannot use the succeeded outcome.", nameof(outcome));
        }

        var normalizedDiagnostics = diagnostics.IsDefault ? [] : diagnostics;
        if (normalizedDiagnostics.IsEmpty && outcome != ApplicationOutcome.Cancelled)
        {
            throw new ArgumentException("A non-cancellation failure requires at least one diagnostic.", nameof(diagnostics));
        }

        return new ApplicationResult<T>(outcome, default, normalizedDiagnostics);
    }
}
