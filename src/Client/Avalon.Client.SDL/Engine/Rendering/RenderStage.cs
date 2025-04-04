// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL.Engine.Rendering;

public enum RenderStage : byte
{
    Invalid,
    Begin,
    Shadow,
    Geometry,
    SSAO,
    Lightning,
    PostProcess,
    Present,
    End
}
