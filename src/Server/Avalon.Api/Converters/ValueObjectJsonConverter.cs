using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Common;

namespace Avalon.Api.Converters;

public class ValueObjectJsonConverter<TValue> : JsonConverter<ValueObject<TValue>>
    where TValue : IEquatable<TValue>
{
    public override ValueObject<TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
        return (ValueObject<TValue>)Activator.CreateInstance(typeToConvert, value)!;
    }

    public override void Write(Utf8JsonWriter writer, ValueObject<TValue> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Value, options);
    }
}
