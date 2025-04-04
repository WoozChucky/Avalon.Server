// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL.Engine.Rendering.Passes;

public unsafe struct PostProcessPassContext
{
    public SDL_GPURenderPass* RenderPass { get; set; }
    public SDL_GPUGraphicsPipeline* Pipeline { get; set; }
}
