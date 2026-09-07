using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Lettermint.Models;

public abstract class ApiModel
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this, GetType(), LettermintJson.Options);
}

public static class LettermintJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            var converter = new ApiDictionaryConverterFactory();
            foreach (var property in info.Properties)
                if (!property.IsExtensionData && converter.CanConvert(property.PropertyType))
                    property.CustomConverter = converter.CreateConverter(property.PropertyType, options);
        });
        options.TypeInfoResolver = resolver;
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

public sealed class WireEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly Dictionary<T, string> Names = Enum.GetValues<T>().ToDictionary(
        value => value,
        value => typeof(T).GetField(value.ToString())!.GetCustomAttribute<EnumMemberAttribute>()!.Value!);
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        foreach (var pair in Names)
            if (pair.Value == text) return pair.Key;
        throw new JsonException("Unknown API enum value.");
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(Names[value]);
}

// Preserve page fields when the API returns a page. Map a bare array to Data.
public sealed class CollectionResponseConverter<T> : JsonConverter<T> where T : ApiModel, new()
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var result = new T();
        var fields = typeof(T).GetProperties().Where(p => p.GetCustomAttribute<JsonPropertyNameAttribute>() != null)
            .ToDictionary(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name);
        if (root.ValueKind == JsonValueKind.Array)
        {
            var data = fields["data"];
            data.SetValue(result, root.Deserialize(data.PropertyType, options));
            return result;
        }
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a page or an array.");
        foreach (var item in root.EnumerateObject())
        {
            if (fields.TryGetValue(item.Name, out var field))
                field.SetValue(result, item.Value.Deserialize(field.PropertyType, options));
            else
                (result.AdditionalProperties ??= new())[item.Name] = item.Value.Clone();
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var field in typeof(T).GetProperties())
        {
            var name = field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            var data = field.GetValue(value);
            if (name == null || data == null) continue;
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, data, field.PropertyType, options);
        }
        if (value.AdditionalProperties != null)
            foreach (var field in value.AdditionalProperties)
            {
                writer.WritePropertyName(field.Key);
                field.Value.WriteTo(writer);
            }
        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(JsonUnionConverterFactory))]
public sealed class JsonUnion<TObject, TArray>
{
    public TObject? Object { get; }
    public TArray? Array { get; }
    public bool IsArray { get; }
    public JsonUnion(TObject value) => Object = value;
    public JsonUnion(TArray value) { Array = value; IsArray = true; }
}

public sealed class JsonUnionConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(JsonUnion<,>);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(Converter<,>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    private sealed class Converter<TObject, TArray> : JsonConverter<JsonUnion<TObject, TArray>>
    {
        public override JsonUnion<TObject, TArray> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
                return new(JsonSerializer.Deserialize<TArray>(ref reader, options)!);
            if (reader.TokenType == JsonTokenType.StartObject)
                return new(JsonSerializer.Deserialize<TObject>(ref reader, options)!);
            throw new JsonException("Expected an object or an array.");
        }
        public override void Write(Utf8JsonWriter writer, JsonUnion<TObject, TArray> value, JsonSerializerOptions options)
        {
            if (value.IsArray) JsonSerializer.Serialize(writer, value.Array, options);
            else JsonSerializer.Serialize(writer, value.Object, options);
        }
    }
}


// PHP encodes an empty associative array as []. Accept only an empty array here.
public sealed class ApiDictionaryConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        && typeToConvert.GetGenericArguments()[0] == typeof(string);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()[1]))!;
    private sealed class Converter<TValue> : JsonConverter<Dictionary<string, TValue>>
    {
        public override Dictionary<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0) return new();
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a JSON object or an empty PHP array.");
            return root.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Deserialize<TValue>(options)!);
        }
        public override void Write(Utf8JsonWriter writer, Dictionary<string, TValue> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var pair in value)
            {
                writer.WritePropertyName(pair.Key);
                JsonSerializer.Serialize(writer, pair.Value, options);
            }
            writer.WriteEndObject();
        }
    }
}
