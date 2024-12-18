// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Systems.ImGui;

public readonly struct ImGuiFontConfig
{
    public ImGuiFontConfig(string fontPath, int fontSize)
    {
        if (fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        FontPath = fontPath ?? throw new ArgumentNullException(nameof(fontPath));
        FontSize = fontSize;
    }

    public string FontPath { get; }
    public int FontSize { get; }
}
