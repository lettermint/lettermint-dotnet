using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Lettermint.Internal;

/// <summary>The serializer options of the SDK.</summary>
internal static class LettermintJson
{
    /// <summary>
    /// Default System.Text.Json behaviour, except that properties marked
    /// <c>required</c> in the generated types are not enforced when reading:
    /// a response that leaves out a documented field still decodes.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static info =>
        {
            foreach (var property in info.Properties)
            {
                property.IsRequired = false;
            }
        });
        var options = new JsonSerializerOptions { TypeInfoResolver = resolver };
        options.MakeReadOnly();
        return options;
    }
}

/// <summary>
/// Serializes typed query objects to the API's bracket syntax, exactly like
/// the Node SDK's <c>src/query.ts</c>: nested objects use brackets, booleans are
/// <c>1</c>/<c>0</c>, arrays of scalars are comma-joined, arrays of objects are
/// indexed, and null values are left out. Keys and values are encoded as
/// <c>application/x-www-form-urlencoded</c> (as <c>URLSearchParams</c> does).
/// </summary>
internal static class QueryString
{
    /// <summary>The query object as JSON, keyed by wire names (<c>page[size]</c>), or null.</summary>
    public static JsonObject? ToNode<TQuery>(TQuery? query)
    {
        if (query is null || query is NoQuery)
        {
            return null;
        }
        return JsonSerializer.SerializeToNode(query, LettermintJson.Options) as JsonObject;
    }

    public static string Serialize(JsonObject? query)
    {
        if (query is null)
        {
            return string.Empty;
        }
        var parameters = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in query)
        {
            Append(parameters, key, value);
        }
        var builder = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }
            builder.Append(FormEncode(key)).Append('=').Append(FormEncode(value));
        }
        return builder.ToString();
    }

    private static bool IsScalar(JsonNode? node) => node is null || node is JsonValue;

    private static bool IsNull(JsonNode? node) => node is null || node.GetValueKind() == JsonValueKind.Null;

    private static string Scalar(JsonValue value) => value.GetValueKind() switch
    {
        JsonValueKind.True => "1",
        JsonValueKind.False => "0",
        JsonValueKind.String => value.GetValue<string>(),
        _ => value.ToJsonString(),
    };

    private static void Append(List<KeyValuePair<string, string>> parameters, string key, JsonNode? node)
    {
        switch (node)
        {
            case null:
                return;
            case JsonValue value:
                if (IsNull(value))
                {
                    return;
                }
                parameters.Add(new(key, Scalar(value)));
                return;
            case JsonArray array:
                if (array.All(IsScalar))
                {
                    var items = array.OfType<JsonValue>().Where(v => !IsNull(v)).Select(Scalar).ToList();
                    if (items.Count > 0)
                    {
                        parameters.Add(new(key, string.Join(",", items)));
                    }
                    return;
                }
                for (var index = 0; index < array.Count; index++)
                {
                    Append(parameters, $"{key}[{index}]", array[index]);
                }
                return;
            case JsonObject obj:
                foreach (var (name, item) in obj)
                {
                    Append(parameters, $"{key}[{name}]", item);
                }
                return;
        }
    }

    /// <summary>The application/x-www-form-urlencoded byte serializer of the WHATWG URL standard.</summary>
    private static string FormEncode(string text)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            if (b == 0x20)
            {
                builder.Append('+');
            }
            else if (b is 0x2A or 0x2D or 0x2E or 0x5F || (b >= 0x30 && b <= 0x39) || (b >= 0x41 && b <= 0x5A) || (b >= 0x61 && b <= 0x7A))
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return builder.ToString();
    }
}
