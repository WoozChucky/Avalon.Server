// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.Common.Converters;

public class ValueObjectJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        // Check if the type is a subclass of ValueObject<>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ValueObject<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        // Get the generic type TValue
        Type valueType = typeToConvert.GetGenericArguments()[0];

        // Create the converter dynamically
        Type converterType = typeof(ValueObjectJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
