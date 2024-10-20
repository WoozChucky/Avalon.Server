// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

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
