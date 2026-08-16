using System.Text.Json;
using System.Text.Json.Serialization;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.Persistence.Sqlite.Serialization;

internal static class ProgramIndexJsonCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new CompilationProfileConverter() },
    };

    public static string Serialize(ProgramIndexSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static string SerializeValue<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static ProgramIndexSnapshot Deserialize(string payload) =>
        JsonSerializer.Deserialize<ProgramIndexSnapshot>(payload, Options)
        ?? throw new InvalidDataException("The stored Program Index payload is empty.");

    public static CompilationProfile DeserializeProfile(string payload)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(payload));
        reader.Read();
        return new CompilationProfileConverter().Read(ref reader, typeof(CompilationProfile), Options);
    }

    private sealed class CompilationProfileConverter : JsonConverter<CompilationProfile>
    {
        public override CompilationProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var msBuild = root.GetProperty("msBuildProperties").EnumerateObject()
                .Select(property => KeyValuePair.Create(property.Name, property.Value.GetString()!));
            var analysis = root.GetProperty("analysisProperties").EnumerateObject()
                .Select(property => KeyValuePair.Create(property.Name, property.Value.GetString()!));
            return CompilationProfile.Create(
                root.GetProperty("repositoryRelativeTargetPath").GetString()!,
                root.GetProperty("configuration").GetString()!,
                root.GetProperty("targetFramework").GetString()!,
                root.GetProperty("runtimeIdentifier").ValueKind == JsonValueKind.Null
                    ? null
                    : root.GetProperty("runtimeIdentifier").GetString(),
                msBuild,
                analysis);
        }

        public override void Write(Utf8JsonWriter writer, CompilationProfile value, JsonSerializerOptions options)
        {
            using var document = JsonDocument.Parse(value.CanonicalJson);
            document.RootElement.WriteTo(writer);
        }
    }
}
