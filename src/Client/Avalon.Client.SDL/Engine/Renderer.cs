// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.


using System.Numerics;
using Avalon.Client.SDL.Engine.Rendering;
using Avalon.Client.SDL.Engine.Rendering.Assets;
using Avalon.Client.SDL.Engine.Rendering.Passes;

namespace Avalon.Client.SDL.Engine;

public interface IRenderer : IDisposable
{
    bool Setup(ArenaNativeAllocator allocator);

    bool WaitForIdle();

    bool BeginFrame();
    void EndFrame();

    void ToggleDebugMode();

    IntPtr GetDeviceNativeHandle();
}

public sealed unsafe class Renderer : IRenderer
{
    private readonly SDL_GPUDevice* Device;
    private readonly IWindow Window;
    private SDL_GPUGraphicsPipeline* ChunkPipeline;
    private RendererContext Context;

    private SDL_GPUTextureFormat SwapchainTextureFormat;

    public Renderer(IWindow window, bool debugMode = false)
    {
        Device = SDL_CreateGPUDevice(SDL_GPU_SHADERFORMAT_SPIRV, debugMode, null);
        if (Device == null)
        {
            throw new InvalidOperationException("SDL_CreateGPUDevice failed: " + CString.ToString(SDL_GetError()));
        }

        Log.Information("GPU Device obtained: {Device}", CString.ToString(SDL_GetGPUDeviceDriver(Device)));

        Window = window;

        SDL_Window* windowHandle = (SDL_Window*)Window.GetNativeHandle();

        if (!SDL_ClaimWindowForGPUDevice(Device, windowHandle))
        {
            throw new InvalidOperationException("SDL_ClaimWindowForGPUDevice failed: " +
                                                CString.ToString(SDL_GetError()));
        }

        if (!SDL_WindowSupportsGPUPresentMode(Device, windowHandle, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_MAILBOX))
        {
            Log.Warning("Mailbox present mode not supported");
        }

        if (!SDL_WindowSupportsGPUPresentMode(Device, windowHandle, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_IMMEDIATE))
        {
            Log.Warning("Immediate present mode not supported");
        }

        if (!SDL_WindowSupportsGPUPresentMode(Device, windowHandle, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC))
        {
            Log.Warning("VSync present mode not supported");
        }

        if (!SDL_WindowSupportsGPUSwapchainComposition(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR))
        {
            Log.Warning("SDR composite swapchain composition not supported");
        }

        if (!SDL_WindowSupportsGPUSwapchainComposition(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR_LINEAR))
        {
            Log.Warning("SDR Linear composite swapchain composition not supported");
        }

        if (!SDL_WindowSupportsGPUSwapchainComposition(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_HDR_EXTENDED_LINEAR))
        {
            Log.Warning("HDR Extended Linear composite swapchain composition not supported");
        }

        if (!SDL_WindowSupportsGPUSwapchainComposition(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_HDR10_ST2084))
        {
            Log.Warning("HDR10 composite swapchain composition not supported");
        }

        if (SDL_SetGPUSwapchainParameters(Device, windowHandle,
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR,
                SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_MAILBOX))
        {
            Log.Debug("Swapchain parameters set with {Composition} and {PresentMode}",
                SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR,
                SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_MAILBOX);
        }

        Context = new RendererContext
        {
            CommandBuffer = null,
            Stage = RenderStage.Invalid,
            Shadow = new ShadowPassContext(),
            Geometry = new GeometryPassContext(),
            SSAO = new AmbientOcclusionContextPass(),
            Lightning = new LightningContextPass(),
            PostProcess = new PostProcessPassContext(),
            Present = new PresentPassContext(),
            FullScreenQuad = MeshAsset.GenerateFullScreenQuad(Device)
        };
    }


    public void Dispose() => Cleanup();

    public bool Setup(ArenaNativeAllocator allocator)
    {
        SwapchainTextureFormat = SDL_GetGPUSwapchainTextureFormat(Device, (SDL_Window*)Window.GetNativeHandle());
        int width, height;

        if (!SDL_GetWindowSizeInPixels((SDL_Window*)Window.GetNativeHandle(), &width, &height))
        {
            Log.Error("GetWindowSizeInPixels failed: {Error}", CString.ToString(SDL_GetError()));
            return false;
        }

        if (!Context.Shadow.Initialize(allocator, Device, (uint)width, (uint)height))
        {
            return false;
        }

        if (!Context.Geometry.Initialize(allocator, Device, (uint)width, (uint)height))
        {
            return false;
        }

        if (!Context.SSAO.Initialize(allocator, Device, (uint)width, (uint)height, Context.Geometry.DepthTexture,
                Context.Geometry.NormalsTexture))
        {
            return false;
        }

        if (!Context.Lightning.Initialize(allocator, Device, (uint)width, (uint)height))
        {
            return false;
        }

        if (!Context.Present.Initialize(allocator, Device))
        {
            return false;
        }

        ChunkPipeline = PipelineBuilder.CreateChunkPipeline(allocator, Device, (SDL_Window*)Window.GetNativeHandle());
        if (ChunkPipeline == null)
        {
            Log.Error("Failed to create default pipeline");
            return false;
        }

        return true;
    }

    public bool WaitForIdle() => SDL_WaitForGPUIdle(Device);

    public bool BeginFrame()
    {
        Context.CommandBuffer = SDL_AcquireGPUCommandBuffer(Device);
        if (Context.CommandBuffer == null)
        {
            Log.Error("AcquireGPUCommandBuffer failed: {Error}", CString.ToString(SDL_GetError()));
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
            Log.Error("WaitAndAcquireGPUSwapchainTexture failed: {Error}", CString.ToString(SDL_GetError()));
            return false;
        }

        if (textureSwapchain == null)
        {
            SDL_CancelGPUCommandBuffer(Context.CommandBuffer);
            Log.Warning("No texture swapchain available");
            return false;
        }

        Context.SwapchainTexture = textureSwapchain;

        return true;
    }

    public void EndFrame()
    {
        if (Context.CommandBuffer == null)
        {
            Log.Fatal("EndFrame called without a valid command buffer");
            return;
        }

        SDL_SubmitGPUCommandBuffer(Context.CommandBuffer);
    }

    public void ToggleDebugMode() => throw new NotImplementedException();
    public IntPtr GetDeviceNativeHandle() => (IntPtr)Device;

    private void EndPass(SDL_GPURenderPass* pass)
    {
        if (Context.CommandBuffer == null)
        {
            Log.Fatal("EndPass called without a valid command buffer");
            return;
        }

        if (pass == null)
        {
            Log.Fatal("EndPass called without a valid render pass");
            return;
        }

        SDL_EndGPURenderPass(pass);
    }

    private void Cleanup()
    {
        if (Device != null)
        {
            // Release Buffers (Transfer, Instance)

            SDL_ReleaseWindowFromGPUDevice(Device, (SDL_Window*)Window.GetNativeHandle());
            Context.FullScreenQuad.Dispose();

            // Release Graphics Pipelines
            if (ChunkPipeline != null)
            {
                SDL_ReleaseGPUGraphicsPipeline(Device, ChunkPipeline);
            }

            if (Context.Shadow.Pipeline != null)
            {
                SDL_ReleaseGPUGraphicsPipeline(Device, Context.Shadow.Pipeline);
            }

            if (Context.Geometry.Pipeline != null)
            {
                SDL_ReleaseGPUGraphicsPipeline(Device, Context.Geometry.Pipeline);
            }

            if (Context.SSAO.Pipeline != null)
            {
                SDL_ReleaseGPUGraphicsPipeline(Device, Context.SSAO.Pipeline);
            }

            if (Context.Lightning.Pipeline != null)
            {
                SDL_ReleaseGPUGraphicsPipeline(Device, Context.Lightning.Pipeline);
            }

            if (Context.Present.Pipeline != null)
            {
                SDL_ReleaseGPUGraphicsPipeline(Device, Context.Present.Pipeline);
            }

            // Release Textures
            if (Context.Shadow.ShadowMapTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.Shadow.ShadowMapTexture);
            }

            if (Context.Geometry.AlbedoTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.Geometry.AlbedoTexture);
            }

            if (Context.Geometry.NormalsTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.Geometry.NormalsTexture);
            }

            if (Context.Geometry.SpecularTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.Geometry.SpecularTexture);
            }

            if (Context.Geometry.DepthTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.Geometry.DepthTexture);
            }

            if (Context.Geometry.HardwareDepthTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.Geometry.HardwareDepthTexture);
            }

            if (Context.SSAO.Texture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.SSAO.Texture);
            }

            if (Context.SSAO.NoiseTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.SSAO.NoiseTexture);
            }

            if (Context.Lightning.LightningOutTexture != null)
            {
                SDL_ReleaseGPUTexture(Device, Context.Lightning.LightningOutTexture);
            }

            // Release Samplers
            if (Context.Shadow.ShadowSampler != null)
            {
                SDL_ReleaseGPUSampler(Device, Context.Shadow.ShadowSampler);
            }

            if (Context.Geometry.DefaultSampler != null)
            {
                SDL_ReleaseGPUSampler(Device, Context.Geometry.DefaultSampler);
            }

            if (Context.SSAO.DepthSampler != null)
            {
                SDL_ReleaseGPUSampler(Device, Context.SSAO.DepthSampler);
            }

            if (Context.SSAO.NormalSampler != null)
            {
                SDL_ReleaseGPUSampler(Device, Context.SSAO.NormalSampler);
            }

            if (Context.SSAO.NoiseSampler != null)
            {
                SDL_ReleaseGPUSampler(Device, Context.SSAO.NoiseSampler);
            }

            if (Context.SSAO.AOSampler != null)
            {
                SDL_ReleaseGPUSampler(Device, Context.SSAO.AOSampler);
            }

            SDL_DestroyGPUDevice(Device);
        }
    }

    #region 1º Shadow Pass

    public bool BeginShadowPass()
    {
        fixed (SDL_GPUDepthStencilTargetInfo* target = &Context.Shadow.DepthTarget)
        {
            Context.Shadow.RenderPass =
                SDL_BeginGPURenderPass(Context.CommandBuffer, null, 0, target);
        }

        if (Context.Shadow.RenderPass == null)
        {
            Log.Error("BeginShadowRenderPass failed: {Error}", CString.ToString(SDL_GetError()));
            return false;
        }

        SDL_BindGPUGraphicsPipeline(Context.Shadow.RenderPass, Context.Shadow.Pipeline);

        Matrix4x4 lightSpaceMatrix = Context.Shadow.LightViewProjection;

        SDL_PushGPUVertexUniformData(Context.CommandBuffer, 0, &lightSpaceMatrix, (uint)sizeof(Matrix4x4));

        return true;
    }

    public void EndShadowPass() => EndPass(Context.Shadow.RenderPass);

    #endregion

    #region 2º Geometry Pass

    public bool BeginGeometryPass()
    {
        fixed (SDL_GPUColorTargetInfo* colorTargetsPtr = &Context.Geometry.ColorTargets[0])
        fixed (SDL_GPUDepthStencilTargetInfo* depthTargetPtr = &Context.Geometry.DepthTarget)
        {
            Context.Geometry.RenderPass =
                SDL_BeginGPURenderPass(Context.CommandBuffer, colorTargetsPtr,
                    (uint)Context.Geometry.ColorTargets.Length, depthTargetPtr);
        }

        if (Context.Geometry.RenderPass == null)
        {
            Log.Error("BeginGeometryPass failed: {Error}", CString.ToString(SDL_GetError()));
            return false;
        }

        SDL_BindGPUGraphicsPipeline(Context.Geometry.RenderPass, Context.Geometry.Pipeline);

        // Push Camera UBO
        // Push Model UBO

        return true;
    }

    public void EndGeometryPass() => EndPass(Context.Geometry.RenderPass);

    #endregion

    #region 3º SSAO Pass

    public bool BeginSsaoPass()
    {
        fixed (SDL_GPUColorTargetInfo* targetPtr = &Context.SSAO.ColorTarget)
        {
            Context.SSAO.RenderPass = SDL_BeginGPURenderPass(Context.CommandBuffer, targetPtr, 1, null);
        }

        if (Context.SSAO.RenderPass == null)
        {
            Log.Error("BeginSSAOPass failed: {Error}", CString.ToString(SDL_GetError()));
            return false;
        }

        SDL_BindGPUGraphicsPipeline(Context.SSAO.RenderPass, Context.SSAO.Pipeline);

        SDL_GPUBufferBinding vertexBufferBinding;
        vertexBufferBinding.buffer = (SDL_GPUBuffer*)Context.FullScreenQuad.Vertex.GetData();
        vertexBufferBinding.offset = 0;
        SDL_GPUBufferBinding indexBufferBinding;
        indexBufferBinding.buffer = (SDL_GPUBuffer*)Context.FullScreenQuad.Index.GetData();
        indexBufferBinding.offset = 0;

        SDL_BindGPUVertexBuffers(Context.SSAO.RenderPass, 0, &vertexBufferBinding, 1);
        SDL_BindGPUIndexBuffer(Context.SSAO.RenderPass, &indexBufferBinding,
            SDL_GPUIndexElementSize
                .SDL_GPU_INDEXELEMENTSIZE_16BIT); // TODO: Specify in MeshAsset the type of index element

        fixed (SDL_GPUTextureSamplerBinding* gBufferBindingsPtr = Context.SSAO.GeometryBindings)
        {
            SDL_BindGPUFragmentSamplers(Context.SSAO.RenderPass, 0, gBufferBindingsPtr,
                2); // render pass, first slot, SDL_GPUTextureSamplerBinding ptr, num bindings
        }

        fixed (SDL_GPUTextureSamplerBinding* noiseBindingPtr = Context.SSAO.NoiseBindings)
        {
            SDL_BindGPUFragmentSamplers(Context.SSAO.RenderPass, 2, noiseBindingPtr,
                1); // render pass, slot_index, SDL_GPUTextureSamplerBinding ptr, num bindings
        }

        // Bind SSAO UBO (sample, radius, bias)
        fixed (SSAOSettings* ssaoSettingsPtr = &Context.SSAO.Settings)
        {
            uint size = (uint)sizeof(SSAOSettings);
            SDL_PushGPUFragmentUniformData(Context.CommandBuffer, 0, ssaoSettingsPtr, size);
        }

        //TODO: Pass projection to SSAO context
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2.0f, 1.0f, 0.1f, 100.0f);
        SDL_PushGPUFragmentUniformData(Context.CommandBuffer, 1, &projection, (uint)sizeof(Matrix4x4));

        SDL_DrawGPUIndexedPrimitives(Context.SSAO.RenderPass, Context.FullScreenQuad.Index.Elements, 1, 0, 0, 0);
        return true;
    }

    public void EndSsaoPass() => EndPass(Context.SSAO.RenderPass);

    #endregion

    #region 4º Lightning Pass

    public bool BeginLightningPass()
    {
        fixed (SDL_GPUColorTargetInfo* targetPtr = &Context.Lightning.ColorTarget)
        {
            Context.Lightning.RenderPass = SDL_BeginGPURenderPass(Context.CommandBuffer, targetPtr, 1, null);
        }

        if (Context.Lightning.RenderPass == null)
        {
            Log.Error("BeginLightingPass failed: {Error}", CString.ToString(SDL_GetError()));
            return false;
        }

        SDL_BindGPUGraphicsPipeline(Context.Lightning.RenderPass, Context.Lightning.Pipeline);

        Context.FullScreenQuad.Vertex.Bind((IntPtr)Context.Lightning.RenderPass);
        Context.FullScreenQuad.Index.Bind((IntPtr)Context.Lightning.RenderPass);

        // 3. Bind G-Buffer (albedo, normal, depth, specular) to set=2, binding=0..3, for example
        SDL_GPUTextureSamplerBinding[] gbuffBindings = new SDL_GPUTextureSamplerBinding[4];
        gbuffBindings[0] =
            new SDL_GPUTextureSamplerBinding
            {
                texture = Context.Geometry.AlbedoTexture, sampler = Context.Geometry.DefaultSampler
            };
        gbuffBindings[1] =
            new SDL_GPUTextureSamplerBinding
            {
                texture = Context.Geometry.NormalsTexture, sampler = Context.Geometry.DefaultSampler
            };
        gbuffBindings[2] =
            new SDL_GPUTextureSamplerBinding
            {
                texture = Context.Geometry.DepthTexture, sampler = Context.Geometry.DefaultSampler
            };
        gbuffBindings[3] =
            new SDL_GPUTextureSamplerBinding
            {
                texture = Context.Geometry.SpecularTexture, sampler = Context.Geometry.DefaultSampler
            };

        fixed (SDL_GPUTextureSamplerBinding* gbPtr = gbuffBindings)
        {
            SDL_BindGPUFragmentSamplers(Context.Lightning.RenderPass, 0, gbPtr, (uint)gbuffBindings.Length);
        }

        ////////////////////////////////////////////////
        // BIND SSAO TEXTURE  (set=2, binding=4)
        ////////////////////////////////////////////////
        SDL_GPUTextureSamplerBinding aoBinding = new()
        {
            texture = Context.SSAO.Texture, // The raw or blurred AO //TODO: Add blurred AO axis passes
            sampler = Context.SSAO.AOSampler
        };

        SDL_BindGPUFragmentSamplers(Context.Lightning.RenderPass, 4, &aoBinding, 1);

        ////////////////////////////////////////////////
        // BIND SHADOW MAP  (set=2, binding=5)
        ////////////////////////////////////////////////
        SDL_GPUTextureSamplerBinding shadowBinding = new()
        {
            texture = Context.Shadow.ShadowMapTexture, // From your shadow pass
            sampler = Context.Shadow.ShadowSampler
        };

        SDL_BindGPUFragmentSamplers(Context.Lightning.RenderPass, 5, &shadowBinding, 1);

        ////////////////////////////////////////////////
        // PUSH UNIFORM BUFFERS  (set=3, binding=0..2)
        ////////////////////////////////////////////////

        /*
        // 1) CameraData at slotIndex=0
        fixed (CameraData* camPtr = &Context.CameraUBOData)
        {
            // sizeOf(CameraData)
            uint camSize = (uint)sizeof(CameraData);
            SDL_PushGPUFragmentUniformData(Context.CommandBuffer, 0, camPtr, camSize);
        }

        // 2) LightUBO at slotIndex=1
        fixed (LightUBO* lightPtr = &Context.Light.UBOData)
        {
            // sizeOf(LightUBO)
            uint lightSize = (uint)sizeof(LightUBO);
            SDL_PushGPUFragmentUniformData(Context.CommandBuffer, 1, lightPtr, lightSize);
        }

        // 3) LightSpaceUBO at slotIndex=2 (for shadow map)
        fixed (LightSpaceUBO* spacePtr = &Context.LightSpaceData)
        {
            // sizeOf(LightSpaceUBO)
            uint spaceSize = (uint)sizeof(LightSpaceUBO);
            SDL_PushGPUFragmentUniformData(Context.CommandBuffer, 2, spacePtr, spaceSize);
        }
        */
        SDL_DrawGPUIndexedPrimitives(Context.Lightning.RenderPass, Context.FullScreenQuad.Index.Elements, 1, 0, 0, 0);

        return true;
    }

    public void EndLightningPass() => EndPass(Context.Lightning.RenderPass);

    #endregion

    #region 5º Post Process Pass

    public bool BeginPostProcessPass() => true;

    public void EndPostProcessPass()
    {
    }

    #endregion

    #region 6º Present Pass

    public bool BeginPresentPass()
    {
        SDL_GPUColorTargetInfo colorTargetInfo = default;
        colorTargetInfo.texture = Context.SwapchainTexture;
        colorTargetInfo.clear_color = Rgba32F.CornflowerBlue;
        colorTargetInfo.load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR;
        colorTargetInfo.store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE;
        colorTargetInfo.cycle = true;

        Context.Present.RenderPass = SDL_BeginGPURenderPass(Context.CommandBuffer, &colorTargetInfo, 1, null);
        if (Context.Present.RenderPass == null)
        {
            Log.Error("BeginPresentPass failed: {Error}", CString.ToString(SDL_GetError()));
            return false;
        }

        SDL_BindGPUGraphicsPipeline(Context.Present.RenderPass, Context.Present.Pipeline);

        Context.FullScreenQuad.Vertex.Bind((IntPtr)Context.Present.RenderPass);
        Context.FullScreenQuad.Index.Bind((IntPtr)Context.Present.RenderPass);

        // Bind final lit texture to set=2,binding=0
        SDL_GPUTextureSamplerBinding binding = new()
        {
            texture = Context.Lightning.LightningOutTexture, sampler = Context.Geometry.DefaultSampler
        };

        SDL_BindGPUFragmentSamplers(Context.Present.RenderPass, 0, &binding, 1);

        //SDL_DrawGPUIndexedPrimitives(Context.Present.RenderPass, Context.FullScreenQuad.Index.Elements, 1, 0, 0, 0);
        return true;
    }

    public void EndPresentPass() => EndPass(Context.Present.RenderPass);

    #endregion
}
