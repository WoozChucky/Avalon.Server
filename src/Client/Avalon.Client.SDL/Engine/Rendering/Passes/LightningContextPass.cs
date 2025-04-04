// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL.Engine.Rendering.Passes;

public unsafe struct LightningContextPass
{
    public SDL_GPURenderPass* RenderPass { get; set; }
    public SDL_GPUGraphicsPipeline* Pipeline { get; set; }
    public SDL_GPUTexture* LightningOutTexture { get; set; }
    public SDL_GPUColorTargetInfo ColorTarget;

    public bool Initialize(INativeAllocator allocator, SDL_GPUDevice* device, uint width, uint height)
    {
        LightningOutTexture = TextureBuilder.CreateLightningTexture(device, width, height);
        if (LightningOutTexture == null)
        {
            Log.Fatal("Failed to create lightning texture");
            return false;
        }

        SDL_GPUColorTargetInfo colorTarget = default;
        colorTarget.texture = LightningOutTexture;
        colorTarget.clear_color = Rgba32F.Black;
        colorTarget.load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR;
        colorTarget.store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE;
        colorTarget.cycle = true;
        ColorTarget = colorTarget;
        Pipeline = PipelineBuilder.CreateLightingPassPipeline(allocator, device);
        if (Pipeline == null)
        {
            Log.Fatal("Failed to create lightning pass pipeline");
            return false;
        }

        Log.Information("LightningPass context initialized");
        return true;
    }
}
