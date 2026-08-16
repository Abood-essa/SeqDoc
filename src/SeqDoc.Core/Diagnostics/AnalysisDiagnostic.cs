using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Diagnostics;

/// <summary>Classifies how a diagnostic affects analysis.</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Identifies the pipeline stage that produced a diagnostic.</summary>
public enum AnalysisStage
{
    ProfileResolution,
    WorkspaceLoad,
    CompilationValidation,
    BaselineIndex,
    Persistence,
    Configuration,
    CommandLine,
    FrameworkModel,
}

/// <summary>Locates a diagnostic without requiring every stage to have a source span.</summary>
public sealed record DiagnosticLocation
{
    public DiagnosticLocation(
        string description,
        CompilationProfileId? profile = null,
        ProjectId? project = null,
        SymbolId? symbol = null,
        SourceRange? sourceRange = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
        Profile = profile;
        Project = project;
        Symbol = symbol;
        SourceRange = sourceRange;
    }

    public string Description { get; }

    public CompilationProfileId? Profile { get; }

    public ProjectId? Project { get; }

    public SymbolId? Symbol { get; }

    public SourceRange? SourceRange { get; }
}

/// <summary>
/// Carries a machine-readable failure or warning together with the user impact and next action.
/// </summary>
public sealed record AnalysisDiagnostic
{
    public AnalysisDiagnostic(
        DiagnosticId id,
        string code,
        DiagnosticSeverity severity,
        AnalysisStage stage,
        string summary,
        DiagnosticLocation location,
        string technicalCause,
        string userImpact,
        string nextAction,
        CertaintyLevel certainty,
        ImmutableArray<EvidenceRef> evidence = default,
        string? internalDetail = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("A diagnostic requires a stable ID.", nameof(id));
        }

        Id = id;
        Code = Require(code, nameof(code));
        Severity = severity;
        Stage = stage;
        Summary = Require(summary, nameof(summary));
        Location = location ?? throw new ArgumentNullException(nameof(location));
        TechnicalCause = Require(technicalCause, nameof(technicalCause));
        UserImpact = Require(userImpact, nameof(userImpact));
        NextAction = Require(nextAction, nameof(nextAction));
        Certainty = certainty;
        Evidence = evidence.IsDefault ? [] : evidence;
        InternalDetail = internalDetail;
    }

    public DiagnosticId Id { get; }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public AnalysisStage Stage { get; }

    public string Summary { get; }

    public DiagnosticLocation Location { get; }

    public string TechnicalCause { get; }

    public string UserImpact { get; }

    public string NextAction { get; }

    public CertaintyLevel Certainty { get; }

    public ImmutableArray<EvidenceRef> Evidence { get; }

    /// <summary>Gets infrastructure detail that is retained for debugging but not used as primary user prose.</summary>
    public string? InternalDetail { get; }

    private static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
