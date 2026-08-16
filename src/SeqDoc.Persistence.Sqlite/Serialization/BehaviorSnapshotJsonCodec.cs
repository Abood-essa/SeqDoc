using System.Text.Json;
using System.Text.Json.Serialization;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Persistence.Sqlite.Serialization;

internal static class BehaviorSnapshotJsonCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new CompilationProfileConverter(),
            new SourcePositionConverter(),
            new SourceRangeConverter(),
        },
        PropertyNamingPolicy = null,
    };

    public static string Serialize(BehaviorSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static BehaviorSnapshot Deserialize(string payload) =>
        JsonSerializer.Deserialize<BehaviorSnapshot>(payload, Options)
        ?? throw new InvalidDataException("The stored behavior snapshot payload is empty.");

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

    private sealed class SourcePositionConverter : JsonConverter<SourcePosition>
    {
        public override SourcePosition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return new SourcePosition(
                document.RootElement.GetProperty("Line").GetInt32(),
                document.RootElement.GetProperty("Column").GetInt32());
        }

        public override void Write(Utf8JsonWriter writer, SourcePosition value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Line", value.Line);
            writer.WriteNumber("Column", value.Column);
            writer.WriteEndObject();
        }
    }

    private sealed class SourceRangeConverter : JsonConverter<SourceRange>
    {
        public override SourceRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var documentId = JsonSerializer.Deserialize<DocumentId>(document.RootElement.GetProperty("Document").GetRawText(), options);
            var start = JsonSerializer.Deserialize<SourcePosition>(document.RootElement.GetProperty("Start").GetRawText(), options);
            var end = JsonSerializer.Deserialize<SourcePosition>(document.RootElement.GetProperty("End").GetRawText(), options);
            return new SourceRange(documentId, start, end);
        }

        public override void Write(Utf8JsonWriter writer, SourceRange value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Document");
            JsonSerializer.Serialize(writer, value.Document, options);
            writer.WritePropertyName("Start");
            JsonSerializer.Serialize(writer, value.Start, options);
            writer.WritePropertyName("End");
            JsonSerializer.Serialize(writer, value.End, options);
            writer.WriteEndObject();
        }
    }
}
