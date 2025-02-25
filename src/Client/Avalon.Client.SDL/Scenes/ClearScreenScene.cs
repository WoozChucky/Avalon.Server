// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using JetBrains.Annotations;

namespace Avalon.Client.SDL.Scenes;

[UsedImplicitly]
public sealed unsafe class ClearScreenScene : CommonScene
{
    public static readonly ThreadLocal<ArenaNativeAllocator> Allocator =
        new(() => new ArenaNativeAllocator(1024 * 1024));

    public override bool Initialize(INativeAllocator allocator)
    {
        if (!base.Initialize(allocator))
        {
            return false;
        }

        return true;
    }

    public override void KeyboardEvent(SDL_KeyboardEvent e)
    {
        if (e.scancode == SDL_Scancode.SDL_SCANCODE_ESCAPE)
        {
            QuitInternal();
        }
    }

    public override bool Update(float deltaTime) => true;

    public override bool Draw(float deltaTime)
    {
        SDL_GPUCommandBuffer* commandBuffer = SDL_AcquireGPUCommandBuffer(Device);
        if (commandBuffer == null)
        {
            Console.Error.WriteLine("AcquireGPUCommandBuffer failed: " + SDL_GetError());
            return false;
        }

        SDL_GPUTexture* textureSwapchain;
        if (!SDL_WaitAndAcquireGPUSwapchainTexture(
                commandBuffer,
                Window,
                &textureSwapchain,
                null,
                null))
        {
            Console.Error.WriteLine("WaitAndAcquireGPUSwapchainTexture failed: " + SDL_GetError());
            return false;
        }

        if (textureSwapchain != null)
        {
            SDL_GPUColorTargetInfo colorTargetInfo = default;
            colorTargetInfo.texture = textureSwapchain;
            colorTargetInfo.clear_color = Rgba32F.CornflowerBlue;
            colorTargetInfo.load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR;
            colorTargetInfo.store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE;

            SDL_GPURenderPass* renderPass = SDL_BeginGPURenderPass(
                commandBuffer, &colorTargetInfo, 1, null);
            // No rendering in this example!
            SDL_EndGPURenderPass(renderPass);
        }

        SDL_SubmitGPUCommandBuffer(commandBuffer);
        return true;
    }
}
