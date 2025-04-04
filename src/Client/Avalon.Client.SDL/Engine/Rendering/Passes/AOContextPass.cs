// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Common.Mathematics;
using Vector3 = System.Numerics.Vector3;

namespace Avalon.Client.SDL.Engine.Rendering.Passes;

internal unsafe struct AmbientOcclusionContextPass
{
    public SDL_GPURenderPass* RenderPass { get; set; }
    public SDL_GPUGraphicsPipeline* Pipeline { get; set; }
    public SDL_GPUTexture* Texture { get; set; }
    public SDL_GPUTexture* NoiseTexture { get; set; }
    public SSAOSettings Settings;
    public SDL_GPUSampler* DepthSampler { get; set; }
    public SDL_GPUSampler* NormalSampler { get; set; }
    public SDL_GPUSampler* NoiseSampler { get; set; }
    public SDL_GPUSampler* AOSampler { get; set; }
    public SDL_GPUTextureSamplerBinding[] GeometryBindings;
    public SDL_GPUTextureSamplerBinding[] NoiseBindings;
    public SDL_GPUColorTargetInfo ColorTarget;

    public bool Initialize(INativeAllocator allocator, SDL_GPUDevice* device, uint width, uint height,
        SDL_GPUTexture* depthTexture, SDL_GPUTexture* normalsTexture)
    {
        Settings = new SSAOSettings {samples = GenerateSSAOKernel(), kernelSize = 64, radius = 0.5f, bias = 0.005f};
        Texture = TextureBuilder.CreateSSAOTexture(device, width, height);
        if (Texture == null)
        {
            Log.Error("Failed to create SSAO texture");
            return false;
        }

        NoiseTexture = TextureBuilder.GenerateNoiseTexture(device, 4, 4);
        if (NoiseTexture == null)
        {
            Log.Error("Failed to create noise texture");
            return false;
        }

        SDL_GPUSamplerCreateInfo samplerCreateInfo;
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
        DepthSampler = SDL_CreateGPUSampler(device, &samplerCreateInfo);
        if (DepthSampler == null)
        {
            Log.Error("Failed to create depth sampler");
            return false;
        }

        NormalSampler = SDL_CreateGPUSampler(device, &samplerCreateInfo);
        if (NormalSampler == null)
        {
            Log.Error("Failed to create normal sampler");
            return false;
        }

        samplerCreateInfo.min_filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST;
        samplerCreateInfo.mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST;
        samplerCreateInfo.mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST;
        samplerCreateInfo.address_mode_u = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT;
        samplerCreateInfo.address_mode_v = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT;
        samplerCreateInfo.address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT;
        samplerCreateInfo.mip_lod_bias = 0;
        samplerCreateInfo.max_anisotropy = 0;
        samplerCreateInfo.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_INVALID;
        samplerCreateInfo.min_lod = 0;
        samplerCreateInfo.max_lod = 0;
        samplerCreateInfo.enable_anisotropy = false;
        samplerCreateInfo.enable_compare = false;
        NoiseSampler = SDL_CreateGPUSampler(device, &samplerCreateInfo);
        if (NoiseSampler == null)
        {
            Log.Error("Failed to create noise sampler");
            return false;
        }

        GeometryBindings =
        [
            new SDL_GPUTextureSamplerBinding {texture = depthTexture, sampler = DepthSampler},
            new SDL_GPUTextureSamplerBinding {texture = normalsTexture, sampler = NormalSampler}
        ];
        NoiseBindings =
        [
            new SDL_GPUTextureSamplerBinding {texture = NoiseTexture, sampler = NoiseSampler}
        ];
        SDL_GPUColorTargetInfo colorTarget = default;
        colorTarget.texture = Texture;
        colorTarget.clear_color = Rgba32F.White; // Default AO value (fully lit)
        colorTarget.load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR;
        colorTarget.store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE;
        colorTarget.cycle = true;
        ColorTarget = colorTarget;
        SDL_GPUSamplerCreateInfo aoSamplerInfo = default;
        // If we want the raw data (no smoothing), we could do SDL_GPU_FILTER_NEAREST for min_filter and mag_filter.
        aoSamplerInfo.min_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
        aoSamplerInfo.mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
        aoSamplerInfo.mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR;
        aoSamplerInfo.address_mode_u = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        aoSamplerInfo.address_mode_v = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        aoSamplerInfo.address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        aoSamplerInfo.mip_lod_bias = 0;
        aoSamplerInfo.max_anisotropy = 0;
        aoSamplerInfo.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_INVALID;
        aoSamplerInfo.min_lod = 0;
        aoSamplerInfo.max_lod = 0;
        aoSamplerInfo.enable_anisotropy = false;
        aoSamplerInfo.enable_compare = false;
        AOSampler = SDL_CreateGPUSampler(device, &aoSamplerInfo);
        if (AOSampler == null)
        {
            Log.Error("Failed to create AO sampler");
            return false;
        }

        Pipeline = PipelineBuilder.CreateSSAOPipeline(allocator, device);
        if (Pipeline == null)
        {
            Log.Error("Failed to create SSAO pipeline");
            return false;
        }

        Log.Information("SSAOPass context initialized");
        return true;
    }

    private static Vector3[] GenerateSSAOKernel()
    {
        Random random = new();
        List<Vector3> samples = new();

        for (int i = 0; i < 64; i++)
        {
            Vector3 sample = new(
                (float)random.NextDouble() * 2.0f - 1.0f,
                (float)random.NextDouble() * 2.0f - 1.0f,
                (float)random.NextDouble() // Z always positive to sample upwards
            );
            sample = Vector3.Normalize(sample);
            sample *= (float)random.NextDouble(); // Scale randomly within hemisphere

            float scale = i / 64.0f;
            scale = Mathf.Lerp(0.1f, 1.0f, scale * scale); // Lerp between 0.1 and 1.0
            sample *= scale;

            samples.Add(sample);
        }

        return samples.ToArray();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SSAOSettings : IEquatable<SSAOSettings>
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
    public Vector3[] samples;

    public int kernelSize;
    public float radius;
    public float bias;

    public bool Equals(SSAOSettings other) => samples.Equals(other.samples) && kernelSize == other.kernelSize &&
                                              radius.Equals(other.radius) && bias.Equals(other.bias);

    public override bool Equals(object? obj) => obj is SSAOSettings other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(samples, kernelSize, radius, bias);

    public static bool operator ==(SSAOSettings left, SSAOSettings right) => left.Equals(right);

    public static bool operator !=(SSAOSettings left, SSAOSettings right) => !left.Equals(right);
}
