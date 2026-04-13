// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.Common.Converters;

public class ValueObjectJsonConverter<TValue> : JsonConverter<ValueObject<TValue>>
    where TValue : IEquatable<TValue>
{
    public override ValueObject<TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Deserialize the underlying value type
        TValue? value = JsonSerializer.Deserialize<TValue>(ref reader, options);

        // Return an instance of the derived class
        if (value == null)
        {
            return null;
        }

        // Since the base class is abstract, you need to handle the concrete type creation here.
        ConstructorInfo? constructor = typeToConvert.GetConstructor(new[] {typeof(TValue)});
        if (constructor == null)
        {
            throw new JsonException($"No suitable constructor found for type {typeToConvert}.");
        }

        return (ValueObject<TValue>)constructor.Invoke(new object[] {value});
    }

    public override void Write(Utf8JsonWriter writer, ValueObject<TValue> valueObject, JsonSerializerOptions options) =>
        // Serialize the underlying value
        JsonSerializer.Serialize(writer, valueObject.Value, options);
}
