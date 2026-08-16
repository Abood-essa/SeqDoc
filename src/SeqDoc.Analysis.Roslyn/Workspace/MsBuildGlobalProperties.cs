using System.Collections.Immutable;
using SeqDoc.Application.Analysis;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Roslyn.Workspace;

internal static class MsBuildGlobalProperties
{
    private static readonly string[] ReservedNames =
    [
        "Configuration",
        "TargetFramework",
        "TargetFrameworks",
        "RuntimeIdentifier",
    ];

    public static Dictionary<string, string> CreateForDiscovery(CompilationProfileResolutionRequest request) =>
        Create(
            request.Configuration,
            null,
            request.RuntimeIdentifier,
            request.MsBuildProperties ?? ImmutableSortedDictionary<string, string>.Empty);

    public static Dictionary<string, string> CreateForWorkspace(CompilationProfile profile) =>
        Create(
            profile.Configuration,
            profile.TargetFramework,
            profile.RuntimeIdentifier,
            profile.MsBuildProperties);

    public static string? FindReservedProperty(IEnumerable<KeyValuePair<string, string>> properties) =>
        properties.Select(property => property.Key)
            .FirstOrDefault(key => ReservedNames.Contains(key, StringComparer.OrdinalIgnoreCase));

    private static Dictionary<string, string> Create(
        string configuration,
        string? targetFramework,
        string? runtimeIdentifier,
        IEnumerable<KeyValuePair<string, string>> customProperties)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration,
        };
        if (targetFramework is not null)
        {
            properties.Add("TargetFramework", targetFramework);
        }

        if (runtimeIdentifier is not null)
        {
            properties.Add("RuntimeIdentifier", runtimeIdentifier);
        }

        foreach (var property in customProperties)
        {
            properties.Add(property.Key, property.Value);
        }

        return properties;
    }
}
