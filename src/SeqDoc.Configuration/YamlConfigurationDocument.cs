using System.Collections.Immutable;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SeqDoc.Configuration;

internal sealed record AnalysisSettings(
    string? Configuration,
    string? TargetFramework,
    string? RuntimeIdentifier,
    int? MaxParallelism,
    string? BinaryAnalysis,
    string? SourceLink,
    ImmutableSortedSet<string> SpecifiedFields);

internal sealed record NamedProfileSettings(
    ImmutableSortedDictionary<string, string> MsBuildProperties,
    ImmutableSortedDictionary<string, string> KnownValues);

internal sealed record YamlConfigurationDocument(
    AnalysisSettings Analysis,
    ImmutableSortedDictionary<string, NamedProfileSettings> Profiles)
{
    private static readonly StringComparer KeyComparer = StringComparer.Ordinal;

    public static YamlConfigurationDocument Parse(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count != 1)
        {
            throw new ConfigurationFormatException("$", "Configuration must contain exactly one YAML document.");
        }

        var root = RequireMapping(stream.Documents[0].RootNode, "$", allowEmptyScalar: true);
        var fields = ReadFields(root, "$", [
            "schemaVersion", "analysis", "profiles", "documentation", "selection", "participants",
            "terms", "conditions", "runtimeBindings", "diagrams",
        ]);

        if (!fields.TryGetValue("schemaVersion", out var schemaNode))
        {
            throw new ConfigurationFormatException("$.schemaVersion", "Existing configuration files require schemaVersion 1.");
        }

        int schemaVersion = RequireInteger(schemaNode, "$.schemaVersion", minimum: 1);
        if (schemaVersion != 1)
        {
            throw new ConfigurationFormatException("$.schemaVersion", $"Schema version {schemaVersion} is unsupported; expected 1.");
        }

        AnalysisSettings analysis = fields.TryGetValue("analysis", out var analysisNode)
            ? ParseAnalysis(analysisNode)
            : new(null, null, null, null, null, null, ImmutableSortedSet<string>.Empty);
        var profiles = fields.TryGetValue("profiles", out var profilesNode)
            ? ParseProfiles(profilesNode)
            : EmptyProfiles();

        ValidateLaterSections(fields);
        return new YamlConfigurationDocument(analysis, profiles);
    }

    private static AnalysisSettings ParseAnalysis(YamlNode node)
    {
        var fields = ReadFields(RequireMapping(node, "$.analysis"), "$.analysis", [
            "configuration", "targetFramework", "runtimeIdentifier", "maxParallelism", "binaryAnalysis", "sourceLink",
        ]);
        return new AnalysisSettings(
            OptionalString(fields, "configuration", "$.analysis.configuration", allowNull: false),
            OptionalString(fields, "targetFramework", "$.analysis.targetFramework"),
            OptionalString(fields, "runtimeIdentifier", "$.analysis.runtimeIdentifier"),
            fields.TryGetValue("maxParallelism", out var parallelism)
                ? RequireInteger(parallelism, "$.analysis.maxParallelism", minimum: 1)
                : null,
            OptionalString(fields, "binaryAnalysis", "$.analysis.binaryAnalysis", allowNull: false),
            OptionalString(fields, "sourceLink", "$.analysis.sourceLink", allowNull: false),
            fields.Keys.ToImmutableSortedSet(KeyComparer));
    }

    private static ImmutableSortedDictionary<string, NamedProfileSettings> ParseProfiles(YamlNode node)
    {
        var mapping = RequireMapping(node, "$.profiles");
        var result = ImmutableSortedDictionary.CreateBuilder<string, NamedProfileSettings>(KeyComparer);
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string name = RequireNonEmptyKey(keyNode, "$.profiles");
            string path = $"$.profiles.{name}";
            if (result.ContainsKey(name))
            {
                throw new ConfigurationFormatException(path, "Duplicate profile names are not allowed.");
            }

            var fields = ReadFields(RequireMapping(valueNode, path), path, ["msbuildProperties", "knownValues"]);
            result.Add(name, new NamedProfileSettings(
                fields.TryGetValue("msbuildProperties", out var properties)
                    ? ParseStringMap(properties, $"{path}.msbuildProperties")
                    : EmptyStrings(),
                fields.TryGetValue("knownValues", out var knownValues)
                    ? ParseStringMap(knownValues, $"{path}.knownValues")
                    : EmptyStrings()));
        }

        return result.ToImmutable();
    }

    private static void ValidateLaterSections(Dictionary<string, YamlNode> root)
    {
        if (root.TryGetValue("documentation", out var documentation))
        {
            var fields = ReadFields(RequireMapping(documentation, "$.documentation"), "$.documentation", [
                "outputDirectory", "repositoryProfile", "coverageMode", "includeComprehensiveAppendix",
            ]);
            ValidateOptionalStrings(fields, "$.documentation", "outputDirectory", "repositoryProfile", "coverageMode");
            ValidateOptionalBoolean(fields, "includeComprehensiveAppendix", "$.documentation.includeComprehensiveAppendix");
        }

        if (root.TryGetValue("selection", out var selection))
        {
            var fields = ReadFields(RequireMapping(selection, "$.selection"), "$.selection", ["include", "exclude", "critical"]);
            var include = OptionalStringSet(fields, "include", "$.selection.include");
            var exclude = OptionalStringSet(fields, "exclude", "$.selection.exclude");
            var critical = OptionalStringSet(fields, "critical", "$.selection.critical");
            string? conflict = critical.Intersect(exclude, KeyComparer).Order(KeyComparer).FirstOrDefault();
            if (conflict is not null)
            {
                throw new ConfigurationFormatException("$.selection", $"Flow '{conflict}' cannot be both critical and excluded.");
            }

            _ = include;
        }

        if (root.TryGetValue("participants", out var participants))
        {
            ValidateObjectMap(participants, "$.participants", ["displayName"], required: ["displayName"]);
        }

        if (root.TryGetValue("terms", out var terms))
        {
            _ = ParseStringMap(terms, "$.terms");
        }

        if (root.TryGetValue("conditions", out var conditions))
        {
            ValidateObjectMap(conditions, "$.conditions", ["positive", "negative"], required: ["positive", "negative"]);
        }

        if (root.TryGetValue("runtimeBindings", out var bindings))
        {
            ValidateRuntimeBindings(bindings);
        }

        if (root.TryGetValue("diagrams", out var diagrams))
        {
            var fields = ReadFields(RequireMapping(diagrams, "$.diagrams"), "$.diagrams", [
                "maxParticipants", "maxMaterialMessages", "maxFragmentDepth", "processingColor", "successColor",
                "recoveryColor", "warningColor", "terminalFailureColor",
            ]);
            foreach (string key in new[] { "maxParticipants", "maxMaterialMessages", "maxFragmentDepth" })
            {
                if (fields.TryGetValue(key, out var value))
                {
                    _ = RequireInteger(value, $"$.diagrams.{key}", minimum: 1);
                }
            }

            ValidateOptionalStrings(fields, "$.diagrams", "processingColor", "successColor", "recoveryColor", "warningColor", "terminalFailureColor");
        }
    }

    private static void ValidateObjectMap(
        YamlNode node,
        string path,
        IEnumerable<string> allowed,
        IEnumerable<string> required)
    {
        var mapping = RequireMapping(node, path);
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string key = RequireNonEmptyKey(keyNode, path);
            string itemPath = $"{path}.{key}";
            var fields = ReadFields(RequireMapping(valueNode, itemPath), itemPath, allowed);
            foreach (string field in required)
            {
                if (!fields.TryGetValue(field, out var fieldNode))
                {
                    throw new ConfigurationFormatException($"{itemPath}.{field}", $"Required key '{field}' is missing.");
                }

                _ = RequireString(fieldNode, $"{itemPath}.{field}", allowNull: false);
            }
        }
    }

    private static void ValidateRuntimeBindings(YamlNode node)
    {
        var mapping = RequireMapping(node, "$.runtimeBindings");
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string key = RequireNonEmptyKey(keyNode, "$.runtimeBindings");
            string path = $"$.runtimeBindings.{key}";
            var fields = ReadFields(RequireMapping(valueNode, path), path, ["implementation", "profiles", "reason"]);
            foreach (string required in new[] { "implementation", "profiles", "reason" })
            {
                if (!fields.ContainsKey(required))
                {
                    throw new ConfigurationFormatException($"{path}.{required}", $"Required key '{required}' is missing.");
                }
            }

            _ = RequireString(fields["implementation"], $"{path}.implementation", allowNull: false);
            _ = RequireString(fields["reason"], $"{path}.reason", allowNull: false);
            if (RequireUniqueStringSet(fields["profiles"], $"{path}.profiles").Count == 0)
            {
                throw new ConfigurationFormatException($"{path}.profiles", "At least one profile is required.");
            }
        }
    }

    private static ImmutableSortedDictionary<string, string> ParseStringMap(YamlNode node, string path)
    {
        var mapping = RequireMapping(node, path);
        var result = ImmutableSortedDictionary.CreateBuilder<string, string>(KeyComparer);
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string key = RequireNonEmptyKey(keyNode, path);
            if (result.ContainsKey(key))
            {
                throw new ConfigurationFormatException($"{path}.{key}", "Duplicate keys are not allowed.");
            }

            result.Add(key, RequireString(valueNode, $"{path}.{key}", allowNull: false));
        }

        return result.ToImmutable();
    }

    private static Dictionary<string, YamlNode> ReadFields(YamlMappingNode mapping, string path, IEnumerable<string> allowed)
    {
        var allowedSet = allowed.ToHashSet(KeyComparer);
        var result = new Dictionary<string, YamlNode>(KeyComparer);
        foreach ((YamlNode keyNode, YamlNode valueNode) in mapping.Children)
        {
            string key = RequireNonEmptyKey(keyNode, path);
            if (!allowedSet.Contains(key))
            {
                throw new ConfigurationFormatException($"{path}.{key}", $"Unknown key '{key}'.");
            }

            if (!result.TryAdd(key, valueNode))
            {
                throw new ConfigurationFormatException($"{path}.{key}", $"Duplicate key '{key}'.");
            }
        }

        return result;
    }

    private static YamlMappingNode RequireMapping(YamlNode node, string path, bool allowEmptyScalar = false)
    {
        if (node is YamlMappingNode mapping)
        {
            return mapping;
        }

        if (allowEmptyScalar && node is YamlScalarNode { Value: null })
        {
            return new YamlMappingNode();
        }

        throw new ConfigurationFormatException(path, "Expected an object.");
    }

    private static string RequireNonEmptyKey(YamlNode node, string path)
    {
        string value = RequireString(node, path, allowNull: false);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConfigurationFormatException(path, "Keys cannot be empty.");
        }

        return value;
    }

    private static string RequireString(YamlNode node, string path, bool allowNull)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw new ConfigurationFormatException(path, "Expected a string.");
        }

        if (IsNull(scalar))
        {
            if (allowNull)
            {
                return null!;
            }

            throw new ConfigurationFormatException(path, "A non-null string is required.");
        }

        if (string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw new ConfigurationFormatException(path, "A non-empty string is required.");
        }

        return scalar.Value;
    }

    private static string? OptionalString(
        Dictionary<string, YamlNode> fields,
        string key,
        string path,
        bool allowNull = true) =>
        fields.TryGetValue(key, out var value) ? RequireString(value, path, allowNull) : null;

    private static int RequireInteger(YamlNode node, string path, int minimum)
    {
        if (node is not YamlScalarNode scalar
            || !int.TryParse(scalar.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            || value < minimum)
        {
            throw new ConfigurationFormatException(path, $"Expected an integer greater than or equal to {minimum}.");
        }

        return value;
    }

    private static void ValidateOptionalBoolean(Dictionary<string, YamlNode> fields, string key, string path)
    {
        if (fields.TryGetValue(key, out var node)
            && (node is not YamlScalarNode scalar || !bool.TryParse(scalar.Value, out _)))
        {
            throw new ConfigurationFormatException(path, "Expected true or false.");
        }
    }

    private static void ValidateOptionalStrings(Dictionary<string, YamlNode> fields, string path, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (fields.TryGetValue(key, out var node))
            {
                _ = RequireString(node, $"{path}.{key}", allowNull: false);
            }
        }
    }

    private static ImmutableSortedSet<string> OptionalStringSet(
        Dictionary<string, YamlNode> fields,
        string key,
        string path) =>
        fields.TryGetValue(key, out var node)
            ? RequireUniqueStringSet(node, path)
            : ImmutableSortedSet.Create<string>(KeyComparer);

    private static ImmutableSortedSet<string> RequireUniqueStringSet(YamlNode node, string path)
    {
        if (node is not YamlSequenceNode sequence)
        {
            throw new ConfigurationFormatException(path, "Expected a list of strings.");
        }

        var result = ImmutableSortedSet.CreateBuilder<string>(KeyComparer);
        foreach (YamlNode child in sequence.Children)
        {
            string value = RequireString(child, path, allowNull: false);
            if (!result.Add(value))
            {
                throw new ConfigurationFormatException(path, $"Duplicate value '{value}' is not allowed.");
            }
        }

        return result.ToImmutable();
    }

    private static bool IsNull(YamlScalarNode scalar) =>
        scalar.Value is null
        || string.Equals(scalar.Value, "null", StringComparison.OrdinalIgnoreCase)
        || scalar.Value == "~";

    private static ImmutableSortedDictionary<string, string> EmptyStrings() =>
        ImmutableSortedDictionary.Create<string, string>(KeyComparer);

    private static ImmutableSortedDictionary<string, NamedProfileSettings> EmptyProfiles() =>
        ImmutableSortedDictionary.Create<string, NamedProfileSettings>(KeyComparer);
}

internal sealed class ConfigurationFormatException(string path, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Path { get; } = path;
}
