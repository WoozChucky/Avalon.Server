// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Avalon.Client.SDL.Engine.Math;

namespace Avalon.Client.SDL.Engine;

public static unsafe class PipelineBuilder
{
    private static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");

    public static SDL_GPUGraphicsPipeline* CreateChunkPipeline(INativeAllocator allocator, SDL_GPUDevice* device,
        SDL_Window* window)
    {
        SDL_GPUShader* vertexShader = CreateShader(allocator, "Chunk.vert", device, 0, 1);
        if (vertexShader == null)
        {
            Log.Error("Failed to create vertex shader!");
            return null;
        }

        SDL_GPUShader* fragmentShader = CreateShader(
            allocator, "Block.frag", device, 1);
        if (fragmentShader == null)
        {
            Log.Error("Failed to create fragment shader!");
            return null;
        }

        SDL_GPUGraphicsPipelineCreateInfo pipelineCreateInfo = default;
        pipelineCreateInfo.primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;
        pipelineCreateInfo.vertex_shader = vertexShader;
        pipelineCreateInfo.fragment_shader = fragmentShader;
        FillGraphicsPipelineSwapchainColorTarget(allocator, device, window, ref pipelineCreateInfo.target_info);
        FillGraphicsPipelineVertexAttributes<ChunkVertex>(allocator, ref pipelineCreateInfo.vertex_input_state);
        FillGraphicsPipelineVertexBuffer<ChunkVertex>(allocator, ref pipelineCreateInfo.vertex_input_state);

        SDL_GPUGraphicsPipeline* pipeline = SDL_CreateGPUGraphicsPipeline(device, &pipelineCreateInfo);
        if (pipeline == null)
        {
            Log.Error("Failed to create graphics pipeline!");
            return null;
        }

        SDL_ReleaseGPUShader(device, vertexShader);
        SDL_ReleaseGPUShader(device, fragmentShader);

        return pipeline;
    }

    private static void FillGraphicsPipelineSwapchainColorTarget(
        INativeAllocator allocator,
        SDL_GPUDevice* device,
        SDL_Window* window,
        ref SDL_GPUGraphicsPipelineTargetInfo targetInfo)
    {
        targetInfo.num_color_targets = 1;
        targetInfo.color_target_descriptions = allocator
            .AllocateArray<SDL_GPUColorTargetDescription>(1);

        ref SDL_GPUColorTargetDescription colorTargetDescription = ref targetInfo.color_target_descriptions[0];
        colorTargetDescription.format = SDL_GetGPUSwapchainTextureFormat(device, window);
    }

    private static void FillGraphicsPipelineVertexAttributes<TVertex>(
        INativeAllocator allocator,
        ref SDL_GPUVertexInputState vertexInputState)
        where TVertex : unmanaged
    {
        Type vertexType = typeof(TVertex);
        ImmutableArray<FieldInfo> vertexFields = [..vertexType.GetFields().Where(f => !f.IsStatic)];

        vertexInputState.num_vertex_attributes = (uint)vertexFields.Length;
        vertexInputState.vertex_attributes = allocator.AllocateArray<SDL_GPUVertexAttribute>(vertexFields.Length);

        for (int i = 0; i < vertexFields.Length; i++)
        {
            FieldInfo vertexField = vertexFields[i];
            ref SDL_GPUVertexAttribute vertexAttribute = ref vertexInputState.vertex_attributes[i];
            vertexAttribute.location = (uint)i;

            if (vertexField.FieldType == typeof(Vector2))
            {
                vertexAttribute.format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2;
            }
            else if (vertexField.FieldType == typeof(Vector3))
            {
                vertexAttribute.format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3;
            }
            else if (vertexField.FieldType == typeof(Rgba8U))
            {
                vertexAttribute.format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM;
            }
            else if (vertexField.FieldType == typeof(IVec2))
            {
                vertexAttribute.format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_INT2;
            }
            else if (vertexField.FieldType == typeof(IVec3))
            {
                vertexAttribute.format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_INT3;
            }
            else
            {
                throw new NotImplementedException("Unsupported vertex field type!");
            }

            vertexAttribute.offset = (uint)Marshal.OffsetOf(vertexType, vertexField.Name);
        }
    }

    private static void FillGraphicsPipelineVertexBuffer<TVertex>(
        INativeAllocator allocator,
        ref SDL_GPUVertexInputState vertexInputState)
        where TVertex : unmanaged
    {
        vertexInputState.num_vertex_buffers = 1;
        vertexInputState.vertex_buffer_descriptions = allocator
            .AllocateArray<SDL_GPUVertexBufferDescription>(1);

        ref SDL_GPUVertexBufferDescription vertexBufferDescription = ref vertexInputState.vertex_buffer_descriptions[0];
        vertexBufferDescription.slot = 0;
        vertexBufferDescription.input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX;
        vertexBufferDescription.instance_step_rate = 0;
        vertexBufferDescription.pitch = (uint)sizeof(TVertex);
    }

    private static SDL_GPUShader* CreateShader(
        INativeAllocator allocator,
        string fileName,
        SDL_GPUDevice* device,
        uint samplerCount = 0,
        uint uniformBufferCount = 0,
        uint storageBufferCount = 0,
        uint storageTextureCount = 0)
    {
        // Auto-detect the shader stage from the file name for convenience
        SDL_GPUShaderStage stage;

        if (fileName.EndsWith(".vert", StringComparison.CurrentCultureIgnoreCase))
        {
            stage = SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX;
        }
        else if (fileName.EndsWith(".frag", StringComparison.CurrentCultureIgnoreCase))
        {
            stage = SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT;
        }
        else
        {
            Console.Error.WriteLine("Invalid shader stage!");
            return null;
        }

        string filePath = Path.Combine(AssetsDirectory, "Shaders/Compiled/SPIRV", fileName + ".spv");
        uint format = SDL_GPU_SHADERFORMAT_SPIRV;
        string entryPoint = "main";

        ulong codeSize;
        CString filePathC = allocator.AllocateCString(filePath);
        void* code = SDL_LoadFile(filePathC, &codeSize);
        if (code == null)
        {
            Console.Error.WriteLine($"Failed to load shader '{filePath}' from disk!");
            return null;
        }

        SDL_GPUShaderCreateInfo shaderInfo = default;

        shaderInfo.code = (byte*)code;
        shaderInfo.code_size = codeSize;
        shaderInfo.entrypoint = allocator.AllocateCString(entryPoint);
        shaderInfo.format = format;
        shaderInfo.stage = stage;
        shaderInfo.num_samplers = samplerCount;
        shaderInfo.num_uniform_buffers = uniformBufferCount;
        shaderInfo.num_storage_buffers = storageBufferCount;
        shaderInfo.num_storage_textures = storageTextureCount;

        SDL_GPUShader* shader = SDL_CreateGPUShader(device, &shaderInfo);
        if (shader == null)
        {
            Console.Error.WriteLine("Failed to create shader!");
            SDL_free(code);
            return null;
        }

        SDL_free(code);
        return shader;
    }
}
