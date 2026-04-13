// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System.Text.Json;

namespace Avalon.Common.Converters;

public static class JsonSerializerOptionsFactory
{
    public static JsonSerializerOptions Settings { get; } = Create();

    public static JsonSerializerOptions Create()
    {
        JsonSerializerOptions jsonOptions = new();
        jsonOptions.Converters.Add(new ValueObjectJsonConverterFactory());
        return jsonOptions;
    }
}
