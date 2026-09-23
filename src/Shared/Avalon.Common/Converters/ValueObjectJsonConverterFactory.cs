// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.Common.Converters;

/// <summary>
/// Serialises a <see cref="ValueObject{TValue}" /> as the primitive it wraps, so an id crosses
/// JSON as <c>42</c> rather than <c>{"value":42}</c>.
/// </summary>
public class ValueObjectJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => UnderlyingValueType(typeToConvert) != null;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type valueType = UnderlyingValueType(typeToConvert)
            ?? throw new InvalidOperationException($"{typeToConvert} does not derive from ValueObject<T>.");

        // Closed over the concrete type, not over ValueObject<TValue>: the serializer asks about
        // CharacterId, so the converter it gets back should be a converter for CharacterId.
        Type converterType = typeof(ValueObjectJsonConverter<,>).MakeGenericType(typeToConvert, valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    /// <summary>
    /// Walks the inheritance chain to <see cref="ValueObject{TValue}" /> and returns its TValue,
    /// or null when the type is not a value object.
    /// </summary>
    /// <remarks>
    /// The chain has to be walked rather than matched. Every value object in this codebase is a
    /// concrete subclass — <c>CharacterId</c>, <c>AccountId</c>, <c>ItemTemplateId</c> — and none
    /// of them is itself generic, so a factory that tested
    /// <c>typeToConvert.GetGenericTypeDefinition() == typeof(ValueObject&lt;&gt;)</c> matched
    /// nothing anyone serialises. This one did exactly that, silently, for as long as it existed.
    /// </remarks>
    private static Type? UnderlyingValueType(Type? type)
    {
        for (Type? cursor = type; cursor != null; cursor = cursor.BaseType)
        {
            if (cursor.IsGenericType && cursor.GetGenericTypeDefinition() == typeof(ValueObject<>))
            {
                return cursor.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
