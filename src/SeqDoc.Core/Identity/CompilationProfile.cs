using System.Collections.Immutable;

namespace SeqDoc.Core.Identity;

/// <summary>
/// Describes one exact compilation and declared analysis context. Different target frameworks are
/// represented by different profiles and must not share profile-scoped facts.
/// </summary>
public sealed record CompilationProfile
{
    private CompilationProfile(
        CompilationProfileId id,
        string repositoryRelativeTargetPath,
        string configuration,
        string targetFramework,
        string? runtimeIdentifier,
        ImmutableSortedDictionary<string, string> msBuildProperties,
        ImmutableSortedDictionary<string, string> analysisProperties,
        string canonicalJson)
    {
        Id = id;
        RepositoryRelativeTargetPath = repositoryRelativeTargetPath;
        Configuration = configuration;
        TargetFramework = targetFramework;
        RuntimeIdentifier = runtimeIdentifier;
        MsBuildProperties = msBuildProperties;
        AnalysisProperties = analysisProperties;
        CanonicalJson = canonicalJson;
    }

    public CompilationProfileId Id { get; }

    public string RepositoryRelativeTargetPath { get; }

    public string Configuration { get; }

    public string TargetFramework { get; }

    public string? RuntimeIdentifier { get; }

    public ImmutableSortedDictionary<string, string> MsBuildProperties { get; }

    public ImmutableSortedDictionary<string, string> AnalysisProperties { get; }

    /// <summary>Gets the exact versioned descriptor bytes represented as UTF-8 JSON text.</summary>
    public string CanonicalJson { get; }

    /// <summary>Creates a normalized profile and derives its ID from the canonical descriptor.</summary>
    public static CompilationProfile Create(
        string repositoryRelativeTargetPath,
        string configuration,
        string targetFramework,
        string? runtimeIdentifier = null,
        IEnumerable<KeyValuePair<string, string>>? msBuildProperties = null,
        IEnumerable<KeyValuePair<string, string>>? analysisProperties = null)
    {
        var normalizedPath = RepositoryRelativePath.Normalize(repositoryRelativeTargetPath);
        var normalizedConfiguration = RequireNormalized(configuration, nameof(configuration));
        var normalizedTargetFramework = RequireNormalized(targetFramework, nameof(targetFramework));
        var normalizedRuntimeIdentifier = string.IsNullOrWhiteSpace(runtimeIdentifier)
            ? null
            : runtimeIdentifier;
        var normalizedMsBuildProperties = NormalizeProperties(
            msBuildProperties,
            StringComparer.OrdinalIgnoreCase,
            normalizeKeysToUpperInvariant: true);
        var normalizedAnalysisProperties = NormalizeProperties(
            analysisProperties,
            StringComparer.Ordinal,
            normalizeKeysToUpperInvariant: false);

        var canonicalJson = CanonicalIdentityJson.WriteCompilationProfile(
            normalizedPath,
            normalizedConfiguration,
            normalizedTargetFramework,
            normalizedRuntimeIdentifier,
            normalizedMsBuildProperties,
            normalizedAnalysisProperties);
        var id = new CompilationProfileId(StableIdentity.Hash("profile:v1:", canonicalJson));

        return new CompilationProfile(
            id,
            normalizedPath,
            normalizedConfiguration,
            normalizedTargetFramework,
            normalizedRuntimeIdentifier,
            normalizedMsBuildProperties,
            normalizedAnalysisProperties,
            canonicalJson);
    }

    private static ImmutableSortedDictionary<string, string> NormalizeProperties(
        IEnumerable<KeyValuePair<string, string>>? properties,
        StringComparer comparer,
        bool normalizeKeysToUpperInvariant)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(comparer);
        foreach (var property in properties ?? [])
        {
            var key = RequireNormalized(property.Key, "property key");
            if (normalizeKeysToUpperInvariant)
            {
                key = key.ToUpperInvariant();
            }

            var value = property.Value
                ?? throw new ArgumentException($"Property '{key}' has a null value.", nameof(properties));
            builder.Add(key, value);
        }

        return builder.ToImmutable();
    }

    private static string RequireNormalized(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
