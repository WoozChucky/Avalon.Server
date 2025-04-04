// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.SDL.Engine.Rendering.Passes;

internal unsafe struct ShadowPassContext
{
    public SDL_GPURenderPass* RenderPass { get; set; }
    public SDL_GPUGraphicsPipeline* Pipeline { get; set; }
    public SDL_GPUTexture* ShadowMapTexture { get; set; }
    public SDL_GPUSampler* ShadowSampler { get; set; }
    public SDL_GPUDepthStencilTargetInfo DepthTarget;
    public Matrix4x4 LightViewProjection;

    public bool Initialize(INativeAllocator allocator, SDL_GPUDevice* device, uint width, uint height)
    {
        ShadowMapTexture = TextureBuilder.CreateShadowMapTexture(device, width, height);
        if (ShadowMapTexture == null)
        {
            Log.Fatal("Failed to create shadow map texture");
            return false;
        }

        SDL_GPUDepthStencilTargetInfo depthStencilTargetInfo = default;
        depthStencilTargetInfo.clear_depth = 1.0f;
        depthStencilTargetInfo.load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR;
        depthStencilTargetInfo.store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE;
        depthStencilTargetInfo.stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE;
        depthStencilTargetInfo.stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE;
        depthStencilTargetInfo.texture = ShadowMapTexture;
        depthStencilTargetInfo.cycle = true;
        DepthTarget = depthStencilTargetInfo;

        SDL_GPUSamplerCreateInfo shadowSamplerInfo = default;
        shadowSamplerInfo.min_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
        shadowSamplerInfo.mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
        shadowSamplerInfo.mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR;
        shadowSamplerInfo.address_mode_u = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        shadowSamplerInfo.address_mode_v = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        shadowSamplerInfo.address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        shadowSamplerInfo.mip_lod_bias = 0;
        shadowSamplerInfo.max_anisotropy = 0;
        shadowSamplerInfo.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_INVALID;
        shadowSamplerInfo.min_lod = 0;
        shadowSamplerInfo.max_lod = 0;
        shadowSamplerInfo.enable_anisotropy = false;
        shadowSamplerInfo.enable_compare = false;
        ShadowSampler = SDL_CreateGPUSampler(device, &shadowSamplerInfo);
        if (ShadowSampler == null)
        {
            Log.Fatal("Failed to create shadow sampler");
            return false;
        }

        Pipeline = PipelineBuilder.CreateShadowPassPipeline(allocator, device);
        if (Pipeline == null)
        {
            Log.Fatal("Failed to create shadow pass pipeline");
            return false;
        }

        LightViewProjection = Matrix4x4.Identity;

        Log.Information("ShadowPass context initialized");
        return true;
    }
}
