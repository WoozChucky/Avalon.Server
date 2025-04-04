// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL.Engine.Rendering.Passes;

public unsafe struct PresentPassContext
{
    public SDL_GPURenderPass* RenderPass { get; set; }
    public SDL_GPUGraphicsPipeline* Pipeline { get; set; }

    public bool Initialize(INativeAllocator allocator, SDL_GPUDevice* device)
    {
        Pipeline = PipelineBuilder.CreatePresentPassPipeline(allocator, device);
        if (Pipeline == null)
        {
            Log.Fatal("Failed to create present pass pipeline");
            return false;
        }

        Log.Information("PresentPass context initialized");
        return true;
    }
}
