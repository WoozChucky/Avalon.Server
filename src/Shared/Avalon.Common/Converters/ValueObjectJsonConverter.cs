// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.Common.Converters;

/// <summary>
/// Reads and writes one value-object type as the primitive it wraps. Created by
/// <see cref="ValueObjectJsonConverterFactory" />, which closes it over the concrete type.
/// </summary>
/// <typeparam name="TObject">The concrete value object, e.g. <c>CharacterId</c>.</typeparam>
/// <typeparam name="TValue">The primitive it wraps, e.g. <c>uint</c>.</typeparam>
public class ValueObjectJsonConverter<TObject, TValue> : JsonConverter<TObject>
    where TObject : ValueObject<TValue>
    where TValue : IEquatable<TValue>
{
    public override TObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        TValue? value = JsonSerializer.Deserialize<TValue>(ref reader, options);

        if (value == null)
        {
            return null;
        }

        // The value object owns its invariants in its constructor, so go through it rather than
        // writing the backing field directly.
        ConstructorInfo? constructor = typeToConvert.GetConstructor(new[] {typeof(TValue)});
        if (constructor == null)
        {
            throw new JsonException($"No suitable constructor found for type {typeToConvert}.");
        }

        return (TObject)constructor.Invoke(new object[] {value});
    }

    public override void Write(Utf8JsonWriter writer, TObject value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Value, options);
}
