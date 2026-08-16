using Microsoft.Build.Evaluation;

namespace SeqDoc.Analysis.Roslyn.Workspace;

internal sealed class MsBuildWarningPolicy(IReadOnlyDictionary<string, string> globalProperties)
{
    private readonly Dictionary<(string ProjectPath, string? Code), bool> cache = new();
    private readonly Dictionary<string, string> properties = globalProperties
        .ToDictionary(property => property.Key, property => property.Value, StringComparer.OrdinalIgnoreCase);

    public bool IsPromoted(string projectPath, string? code)
    {
        var key = (Path.GetFullPath(projectPath), code);
        if (cache.TryGetValue(key, out bool promoted))
        {
            return promoted;
        }

        try
        {
            using var projects = new ProjectCollection(properties);
            var project = projects.LoadProject(key.Item1);
            promoted = IsTrue(project.GetPropertyValue("TreatWarningsAsErrors"))
                && !ContainsCode(project.GetPropertyValue("WarningsNotAsErrors"), code);
            promoted |= ContainsCode(project.GetPropertyValue("WarningsAsErrors"), code)
                || ContainsCode(project.GetPropertyValue("MSBuildWarningsAsErrors"), code);
        }
        catch (Exception)
        {
            // If policy cannot be evaluated, retain Roslyn's conservative failure classification.
            promoted = true;
        }

        cache.Add(key, promoted);
        return promoted;
    }

    private static bool IsTrue(string value) =>
        bool.TryParse(value, out bool result) && result;

    private static bool ContainsCode(string value, string? code) =>
        code is not null
        && value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(code, StringComparer.OrdinalIgnoreCase);
}
