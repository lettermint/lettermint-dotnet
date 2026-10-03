using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lettermint.Models;

[JsonConverter(typeof(OptionalNullableConverterFactory))]
public readonly struct OptionalNullable<T> where T : class
{
    public bool IsSet { get; }
    public T? Value { get; }
    private OptionalNullable(T? value) { IsSet = true; Value = value; }
    public static OptionalNullable<T> Null => new(null);
    public static implicit operator OptionalNullable<T>(T value) => new(value);
}

public sealed class OptionalNullableConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(OptionalNullable<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;

    public sealed class Converter<T> : JsonConverter<OptionalNullable<T>> where T : class
    {
        public override OptionalNullable<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? OptionalNullable<T>.Null : JsonSerializer.Deserialize<T>(ref reader, options)!;

        public override void Write(Utf8JsonWriter writer, OptionalNullable<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
