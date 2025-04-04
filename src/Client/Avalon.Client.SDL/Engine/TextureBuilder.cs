// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;

namespace Avalon.Client.SDL.Engine;

public static unsafe class TextureBuilder
{
    public static SDL_GPUTexture* CreateShadowMapTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT;
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage =
            SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "ShadowMapTexture"u8);
        return texture;
    }

    public static SDL_GPUTexture* CreateAlbedoTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM;
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "AlbedoTexture"u8);
        return texture;
    }

    public static SDL_GPUTexture* CreateNormalsTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT;
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "NormalsTexture"u8);
        return texture;
    }

    public static SDL_GPUTexture* CreateDepthTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT;
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "HardwareDepthTexture"u8);
        return texture;
    }

    public static SDL_GPUTexture* CreateOutDepthTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R32_FLOAT;
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "OutDepthTexture"u8);
        return texture;
    }

    public static SDL_GPUTexture* CreateSpecularTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM;
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "SpecularTexture"u8);
        return texture;
    }

    public static SDL_GPUTexture* CreateSSAOTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8_UNORM; // Grayscale SSAO texture
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "SSAOTexture"u8);
        return texture;
    }

    public static SDL_GPUTexture* CreateLightningTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM;
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "LightningTexture"u8);
        return texture;
    }

    public static SDL_GPUTexture* GenerateNoiseTexture(SDL_GPUDevice* device, uint width, uint height)
    {
        Random random = new();
        Vector4[] noise = new Vector4[16];

        for (int i = 0; i < 16; i++)
        {
            // random X, Y, Z
            float x = (float)random.NextDouble() * 2.0f - 1.0f;
            float y = (float)random.NextDouble() * 2.0f - 1.0f;
            const float z = 0.0f;
            const float w = 0.0f;
            Vector3 normal = Vector3.Normalize(new Vector3(x, y, z));
            noise[i] = new Vector4(normal, w);
        }

        // Upload noise to a small 4x4 texture
        SDL_GPUTextureCreateInfo textureCreateInfo = default;
        textureCreateInfo.type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D;
        textureCreateInfo.format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R32G32B32A32_FLOAT;
        textureCreateInfo.width = width;
        textureCreateInfo.height = height;
        textureCreateInfo.layer_count_or_depth = 1;
        textureCreateInfo.num_levels = 1;
        textureCreateInfo.sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1;
        textureCreateInfo.usage = SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPU_TEXTUREUSAGE_SAMPLER;
        textureCreateInfo.props = 0;
        SDL_GPUTexture* texture = SDL_CreateGPUTexture(device, &textureCreateInfo);
        SDL_SetGPUTextureName(device, texture, "SSAONoiseTexture"u8);

        uint bytes = (uint)sizeof(Vector4) * (uint)noise.Length;

        fixed (void* data = &noise[0])
        {
            if (!UploadTextureData(device, texture, width, height, data, bytes))
            {
                SDL_ReleaseGPUTexture(device, texture);
                return null;
            }
        }

        return texture;
    }

    private static bool UploadTextureData(SDL_GPUDevice* device, SDL_GPUTexture* texture, uint width, uint height,
        void* data, uint size)
    {
        SDL_GPUTransferBufferCreateInfo transferBufferCreateInfo = default;
        transferBufferCreateInfo.usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD;
        transferBufferCreateInfo.size = size;

        SDL_GPUTransferBuffer* transferBuffer = SDL_CreateGPUTransferBuffer(device, &transferBufferCreateInfo);

        void* dataTexture = SDL_MapGPUTransferBuffer(device, transferBuffer, false);
        if (dataTexture == null)
        {
            Log.Error("MapGPUTransferBuffer failed: {Error}", CString.ToString(SDL_GetError()));
            return false;
        }

        NativeMemory.Copy(data, dataTexture, size);

        SDL_UnmapGPUTransferBuffer(device, transferBuffer);

        // Begin copy command
        SDL_GPUCommandBuffer* uploadCommandBuffer = SDL_AcquireGPUCommandBuffer(device);
        SDL_GPUCopyPass* copyPass = SDL_BeginGPUCopyPass(uploadCommandBuffer);

        SDL_GPUTextureTransferInfo bufferSourceTexture = default;
        bufferSourceTexture.transfer_buffer = transferBuffer;
        bufferSourceTexture.offset = 0;

        SDL_GPUTextureRegion bufferDestinationTexture = default;
        bufferDestinationTexture.texture = texture;
        bufferDestinationTexture.w = width;
        bufferDestinationTexture.h = height;
        bufferDestinationTexture.d = 1;

        SDL_UploadToGPUTexture(copyPass, &bufferSourceTexture, &bufferDestinationTexture, false);

        // Finalize and submit copy pass
        SDL_EndGPUCopyPass(copyPass);
        SDL_SubmitGPUCommandBuffer(uploadCommandBuffer);

        // Cleanup transfer buffer
        SDL_ReleaseGPUTransferBuffer(device, transferBuffer);

        return true;
    }
}
