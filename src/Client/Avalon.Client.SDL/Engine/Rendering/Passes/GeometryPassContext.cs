// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL.Engine.Rendering.Passes;

internal unsafe struct GeometryPassContext
{
    public SDL_GPURenderPass* RenderPass { get; set; }
    public SDL_GPUGraphicsPipeline* Pipeline { get; set; }
    public SDL_GPUTexture* AlbedoTexture { get; set; }
    public SDL_GPUTexture* NormalsTexture { get; set; }
    public SDL_GPUTexture* SpecularTexture { get; set; }
    public SDL_GPUTexture* HardwareDepthTexture { get; set; }
    public SDL_GPUTexture* DepthTexture { get; set; }
    public SDL_GPUSampler* DefaultSampler { get; set; }

    public SDL_GPUColorTargetInfo[] ColorTargets;
    public SDL_GPUDepthStencilTargetInfo DepthTarget;

    public bool Initialize(INativeAllocator allocator, SDL_GPUDevice* device, uint width, uint height)
    {
        AlbedoTexture = TextureBuilder.CreateAlbedoTexture(device, width, height);
        if (AlbedoTexture == null)
        {
            Log.Fatal("Failed to create albedo texture");
            return false;
        }

        NormalsTexture = TextureBuilder.CreateNormalsTexture(device, width, height);
        if (NormalsTexture == null)
        {
            Log.Fatal("Failed to create normals texture");
            return false;
        }

        HardwareDepthTexture = TextureBuilder.CreateDepthTexture(device, width, height);
        if (HardwareDepthTexture == null)
        {
            Log.Fatal("Failed to create hardware depth texture");
            return false;
        }

        DepthTexture = TextureBuilder.CreateOutDepthTexture(device, width, height);
        if (DepthTexture == null)
        {
            Log.Fatal("Failed to create depth texture");
            return false;
        }

        SpecularTexture = TextureBuilder.CreateSpecularTexture(device, width, height);
        if (SpecularTexture == null)
        {
            Log.Fatal("Failed to create specular texture");
            return false;
        }

        ColorTargets =
        [
            new SDL_GPUColorTargetInfo
            {
                texture = AlbedoTexture,
                clear_color = Rgba32F.Black,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = true
            },
            new SDL_GPUColorTargetInfo
            {
                texture = NormalsTexture,
                clear_color = Rgba32F.Black,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = true
            },
            new SDL_GPUColorTargetInfo
            {
                texture = SpecularTexture,
                clear_color = Rgba32F.Black,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = true
            },
            new SDL_GPUColorTargetInfo
            {
                texture = DepthTexture,
                clear_color = Rgba32F.Black,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = true
            }
        ];
        DepthTarget = new SDL_GPUDepthStencilTargetInfo
        {
            clear_depth = 1.0f,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            texture = HardwareDepthTexture,
            cycle = true
        };
        SDL_GPUSamplerCreateInfo samplerCreateInfo = default;
        samplerCreateInfo.min_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
        samplerCreateInfo.mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
        samplerCreateInfo.mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR;
        samplerCreateInfo.address_mode_u = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        samplerCreateInfo.address_mode_v = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        samplerCreateInfo.address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        samplerCreateInfo.mip_lod_bias = 0;
        samplerCreateInfo.max_anisotropy = 0;
        samplerCreateInfo.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_INVALID;
        samplerCreateInfo.min_lod = 0;
        samplerCreateInfo.max_lod = 0;
        samplerCreateInfo.enable_anisotropy = false;
        samplerCreateInfo.enable_compare = false;
        DefaultSampler = SDL_CreateGPUSampler(device, &samplerCreateInfo);
        if (DefaultSampler == null)
        {
            Log.Fatal("Failed to create default sampler");
            return false;
        }

        Pipeline = PipelineBuilder.CreateGeometryPassPipeline(allocator, device);

        Log.Information("GeometryPass context initialized");
        return true;
    }
}
