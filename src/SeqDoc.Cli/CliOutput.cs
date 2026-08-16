using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Diagnostics;

namespace SeqDoc.Cli;

internal sealed record CliDocument(
    int SchemaVersion,
    string Command,
    string Outcome,
    object? Data,
    ImmutableArray<CliDiagnostic> Diagnostics,
    CliDiagnosticOutput DiagnosticOutput);

internal sealed record CliDiagnosticOutput(
    int TotalCount,
    int DisplayedCount,
    int OmittedCount,
    int ErrorCount,
    int WarningCount,
    int InfoCount,
    string? ArtifactPath,
    string? ArtifactSha256);

internal sealed record CliDiagnosticProjection(
    ImmutableArray<AnalysisDiagnostic> Displayed,
    CliDiagnosticOutput Output);

internal sealed record CliDiagnostic(
    string Id,
    string Code,
    string Severity,
    string Stage,
    string Summary,
    string Location,
    string TechnicalCause,
    string UserImpact,
    string NextAction,
    string Certainty);

internal static class CliOutput
{
    private const int DiagnosticDisplayLimit = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void WriteJson(
        TextWriter output,
        string command,
        ApplicationOutcome outcome,
        object? data,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        string? artifactPath = null,
        string? artifactSha256 = null)
    {
        var projection = CreateProjection(diagnostics, artifactPath, artifactSha256);
        var document = new CliDocument(
            1,
            command,
            outcome.ToString(),
            data,
            projection.Displayed.Select(ToCliDiagnostic).ToImmutableArray(),
            projection.Output);
        output.WriteLine(JsonSerializer.Serialize(document, JsonOptions));
    }

    public static void WriteDiagnostics(
        TextWriter error,
        IEnumerable<AnalysisDiagnostic> diagnostics,
        string? artifactPath = null,
        string? artifactSha256 = null)
        => WriteDiagnostics(error, CreateProjection(diagnostics, artifactPath, artifactSha256));

    public static void WriteDiagnostics(
        TextWriter error,
        CliDiagnosticProjection projection,
        bool reportUnavailableArtifact = true)
    {
        foreach (var diagnostic in projection.Displayed)
        {
            error.WriteLine($"{diagnostic.Code}: {diagnostic.Summary}");
            error.WriteLine($"Location: {diagnostic.Location.Description}");
            error.WriteLine($"Cause: {diagnostic.TechnicalCause}");
            error.WriteLine($"Impact: {diagnostic.UserImpact}");
            error.WriteLine($"Next action: {diagnostic.NextAction}");
        }

        if (projection.Output.TotalCount > 0)
        {
            error.WriteLine(
                $"Diagnostics: {projection.Output.TotalCount} total "
                + $"({projection.Output.ErrorCount} error(s), {projection.Output.WarningCount} warning(s), {projection.Output.InfoCount} info); "
                + $"{projection.Output.DisplayedCount} shown, {projection.Output.OmittedCount} omitted.");
        }

        if (projection.Output.ArtifactPath is not null)
        {
            error.WriteLine($"Complete diagnostics: {projection.Output.ArtifactPath}");
            error.WriteLine($"Diagnostic artifact SHA-256: {projection.Output.ArtifactSha256}");
        }
        else if (reportUnavailableArtifact && projection.Output.OmittedCount > 0)
        {
            error.WriteLine("The complete diagnostic artifact could not be written; check the cache directory permissions.");
        }
    }

    public static ImmutableArray<AnalysisDiagnostic> OrderDiagnostics(IEnumerable<AnalysisDiagnostic> diagnostics) =>
        diagnostics
            .OrderBy(diagnostic => SeverityRank(diagnostic.Severity))
            .ThenBy(diagnostic => diagnostic.Stage)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Description, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    public static int ExitCode(ApplicationOutcome outcome) => outcome switch
    {
        ApplicationOutcome.Succeeded => 0,
        ApplicationOutcome.InvalidInput => 2,
        ApplicationOutcome.BuildFailure => 3,
        ApplicationOutcome.AnalysisFailure => 4,
        ApplicationOutcome.DocumentationGenerationFailure => 8,
        ApplicationOutcome.ValidationFailure => 5,
        ApplicationOutcome.UnsupportedRequiredFeature => 6,
        ApplicationOutcome.PersistenceFailure => 7,
        ApplicationOutcome.Cancelled => 130,
        _ => 4,
    };

    public static CliDiagnostic ToCliDiagnostic(AnalysisDiagnostic diagnostic) => new(
        diagnostic.Id.Value,
        diagnostic.Code,
        diagnostic.Severity.ToString(),
        diagnostic.Stage.ToString(),
        diagnostic.Summary,
        diagnostic.Location.Description,
        diagnostic.TechnicalCause,
        diagnostic.UserImpact,
        diagnostic.NextAction,
        diagnostic.Certainty.ToString());

    internal static CliDiagnosticProjection CreateProjection(
        IEnumerable<AnalysisDiagnostic> diagnostics,
        string? artifactPath,
        string? artifactSha256)
    {
        var ordered = OrderDiagnostics(diagnostics);
        var displayed = ordered
            .DistinctBy(diagnostic => new
            {
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Stage,
                diagnostic.Summary,
                Location = diagnostic.Location.Description,
                diagnostic.TechnicalCause,
                diagnostic.UserImpact,
                diagnostic.NextAction,
            })
            .Take(DiagnosticDisplayLimit)
            .ToImmutableArray();
        return new CliDiagnosticProjection(displayed, new CliDiagnosticOutput(
            ordered.Length,
            displayed.Length,
            ordered.Length - displayed.Length,
            ordered.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            ordered.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning),
            ordered.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Info),
            artifactPath,
            artifactSha256));
    }

    private static int SeverityRank(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => 0,
        DiagnosticSeverity.Warning => 1,
        _ => 2,
    };
}
