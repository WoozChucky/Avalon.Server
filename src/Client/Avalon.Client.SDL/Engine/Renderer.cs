// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.


namespace Avalon.Client.SDL.Engine;

public interface IRenderer : IDisposable
{
    bool Setup(ArenaNativeAllocator allocator);

    bool WaitForIdle();

    bool BeginFrame();
    void EndFrame();

    void ToggleDebugMode();
}

internal unsafe struct RendererContext
{
    public SDL_GPUCommandBuffer* CommandBuffer { get; set; }
    public SDL_GPURenderPass* ShadowPass { get; set; }
    public SDL_GPURenderPass* GeometryPass { get; set; }
    public SDL_GPURenderPass* PostProcessPass { get; set; }
}

public sealed unsafe class Renderer : IRenderer
{
    private readonly SDL_GPUDevice* Device;
    private readonly IWindow Window;
    private SDL_GPUGraphicsPipeline* ChunkPipeline;
    private RendererContext Context;
    private SDL_GPUTexture* DepthStencilTexture;
    private SDL_GPUTextureFormat SwapchainTextureFormat;

    public Renderer(IWindow window, bool debugMode = false)
    {
        Device = SDL_CreateGPUDevice(SDL_GPU_SHADERFORMAT_SPIRV, debugMode, null);
        if (Device == null)
        {
            throw new InvalidOperationException("SDL_CreateGPUDevice failed: " + SDL_GetError());
        }

        Log.Information("GPU Device created: {Device}", SDL_GetGPUDeviceDriver(Device));

        Window = window;

        SDL_Window* windowHandle = (SDL_Window*)Window.GetNativeHandle();

        if (!SDL_ClaimWindowForGPUDevice(Device, windowHandle))
        {
            throw new InvalidOperationException("SDL_ClaimWindowForGPUDevice failed: " + SDL_GetError());
        }

        if (SDL_WindowSupportsGPUPresentMode(Device, windowHandle, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_MAILBOX))
        {
            Log.Information("Mailbox present mode supported");
        }

        if (SDL_WindowSupportsGPUPresentMode(Device, windowHandle, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_IMMEDIATE))
        {
            Log.Information("Immediate present mode supported");
        }

        if (SDL_WindowSupportsGPUPresentMode(Device, windowHandle, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC))
        {
            Log.Information("VSync present mode supported");
        }

        if (SDL_WindowSupportsGPUSwapchainComposition(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR))
        {
            Log.Information("SDR composite swapchain composition supported");
        }

        if (SDL_WindowSupportsGPUSwapchainComposition(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR_LINEAR))
        {
            Log.Information("SDR Linear composite swapchain composition supported");
        }

        if (SDL_WindowSupportsGPUSwapchainComposition(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_HDR_EXTENDED_LINEAR))
        {
            Log.Information("SDR Linear composite swapchain composition supported");
        }

        if (SDL_WindowSupportsGPUSwapchainComposition(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_HDR10_ST2084))
        {
            Log.Information("SDR Linear composite swapchain composition supported");
        }

        if (SDL_SetGPUSwapchainParameters(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR,
                SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_MAILBOX))
        {
            Log.Information("Swapchain parameters set");
        }
    }


    public void Dispose() => Cleanup();

    public bool Setup(ArenaNativeAllocator allocator)
    {
        SwapchainTextureFormat = SDL_GetGPUSwapchainTextureFormat(Device, (SDL_Window*)Window.GetNativeHandle());
        int width, height;

        if (!SDL_GetWindowSizeInPixels((SDL_Window*)Window.GetNativeHandle(), &width, &height))
        {
            Log.Error("GetWindowSizeInPixels failed: {Error}", SDL_GetError());
            return false;
        }

        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format =
            SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT; // or SDL_GPU_TEXTUREFORMAT_D32_FLOAT
        textureCreateInfo.width = (uint)width;
        textureCreateInfo.height = (uint)height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET;
        textureCreateInfo.props = 0;
        DepthStencilTexture = SDL_CreateGPUTexture(Device, &textureCreateInfo);

        ChunkPipeline = PipelineBuilder.CreateChunkPipeline(allocator, Device, (SDL_Window*)Window.GetNativeHandle());
        if (ChunkPipeline == null)
        {
            Log.Error("Failed to create default pipeline");
            return false;
        }

        return true;
    }

    public bool WaitForIdle() => SDL_WaitForGPUIdle(Device);

    public bool BeginGeometryPass()
    {
        return true;
    }

    public bool BeginShadowPass()
    {
        return true;
    }

    public bool BeginPostProcessPass()
    {
        return true;
    }

    public bool BeginFrame()
    {
        Context.CommandBuffer = SDL_AcquireGPUCommandBuffer(Device);
        if (Context.CommandBuffer == null)
        {
            Log.Error("AcquireGPUCommandBuffer failed: {Error}", SDL_GetError());
            return false;
        }

        SDL_GPUTexture* textureSwapchain;
        if (!SDL_WaitAndAcquireGPUSwapchainTexture(
                Context.CommandBuffer,
                (SDL_Window*)Window.GetNativeHandle(),
                &textureSwapchain,
                null,
                null))
        {
            Log.Error("WaitAndAcquireGPUSwapchainTexture failed: {Error}", SDL_GetError());
            return false;
        }

        if (textureSwapchain == null)
        {
            SDL_CancelGPUCommandBuffer(Context.CommandBuffer);
            Log.Warning("No texture swapchain available");
            return false;
        }

        SDL_GPUColorTargetInfo colorTargetInfo = default;
        colorTargetInfo.texture = textureSwapchain;
        colorTargetInfo.clear_color = Rgba32F.CornflowerBlue;
        colorTargetInfo.load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR;
        colorTargetInfo.store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE;
        colorTargetInfo.cycle = true;

        SDL_GPUDepthStencilTargetInfo depthStencilTargetInfo = default;
        depthStencilTargetInfo.clear_depth = 1.0f;
        depthStencilTargetInfo.load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR;
        depthStencilTargetInfo.store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE;
        depthStencilTargetInfo.stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE;
        depthStencilTargetInfo.stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE;
        depthStencilTargetInfo.texture = DepthStencilTexture;
        depthStencilTargetInfo.cycle = true;

        //TODO: Maybe render passes should be more than 1?
        Context.RenderPass =
            SDL_BeginGPURenderPass(Context.CommandBuffer, &colorTargetInfo, 1, &depthStencilTargetInfo);
        if (Context.RenderPass == null)
        {
            Log.Error("BeginGPURenderPass failed: {Error}", SDL_GetError());
            return false;
        }

        return true;
    }

    public void EndFrame()
    {
        if (Context.CommandBuffer == null)
        {
            Log.Fatal("EndFrame called without a valid command buffer");
            return;
        }

        if (Context.RenderPass == null)
        {
            Log.Fatal("EndFrame called without a valid render pass");
            return;
        }

        SDL_EndGPURenderPass(Context.RenderPass);
        SDL_SubmitGPUCommandBuffer(Context.CommandBuffer);
    }

    public void ToggleDebugMode() => throw new NotImplementedException();

    private void Cleanup()
    {
        if (Device != null)
        {
            // Release Buffers (Transfer, Instance)

            SDL_ReleaseWindowFromGPUDevice(Device, (SDL_Window*)Window.GetNativeHandle());

            // Release Graphics Pipelines
            if (ChunkPipeline != null)
            {
                SDL_ReleaseGPUGraphicsPipeline(Device, ChunkPipeline);
            }

            // Release Textures
            if (DepthStencilTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, DepthStencilTexture);
            }

            // Release Samplers

            SDL_DestroyGPUDevice(Device);
        }
    }
}
