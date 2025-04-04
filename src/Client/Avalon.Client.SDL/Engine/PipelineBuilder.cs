// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Avalon.Client.SDL.Engine.Math;
using Avalon.Client.SDL.Engine.Vertices;

namespace Avalon.Client.SDL.Engine;

public static unsafe class PipelineBuilder
{
    private static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");

    public static SDL_GPUGraphicsPipeline* CreateShadowPassPipeline(INativeAllocator allocator, SDL_GPUDevice* device)
    {
        SDL_GPUShader* vertexShader = CreateShader(allocator, "Shadow.vert", device, 0, 1);
        if (vertexShader == null)
        {
            Log.Error("Failed to create vertex shader!");
            return null;
        }

        SDL_GPUShader* fragmentShader = CreateShader(
            allocator, "Shadow.frag", device);
        if (fragmentShader == null)
        {
            Log.Error("Failed to create fragment shader!");
            return null;
        }

        SDL_GPUGraphicsPipelineCreateInfo pipelineCreateInfo = default;
        pipelineCreateInfo.primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;
        pipelineCreateInfo.vertex_shader = vertexShader;
        pipelineCreateInfo.fragment_shader = fragmentShader;
        pipelineCreateInfo.target_info.num_color_targets = 0;
        pipelineCreateInfo.target_info.has_depth_stencil_target = true;
        pipelineCreateInfo.target_info.depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT;
        pipelineCreateInfo.depth_stencil_state.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS;
        pipelineCreateInfo.depth_stencil_state.enable_depth_test = true;
        pipelineCreateInfo.depth_stencil_state.enable_depth_write = true;
        pipelineCreateInfo.depth_stencil_state.enable_stencil_test = false;

        FillGraphicsPipelineVertexAttributes<PositionVertex>(allocator, ref pipelineCreateInfo.vertex_input_state);
        FillGraphicsPipelineVertexBuffer<PositionVertex>(allocator, ref pipelineCreateInfo.vertex_input_state);

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

    public static SDL_GPUGraphicsPipeline* CreateGeometryPassPipeline(
        INativeAllocator allocator,
        SDL_GPUDevice* device
    )
    {
        SDL_GPUShader* vertexShader = CreateShader(allocator, "GBuffer.vert", device, 0, 2);
        if (vertexShader == null)
        {
            Log.Error("Failed to create vertex shader!");
            return null;
        }

        SDL_GPUShader* fragmentShader = CreateShader(
            allocator, "GBuffer.frag", device, 2);
        if (fragmentShader == null)
        {
            Log.Error("Failed to create fragment shader!");
            return null;
        }

        // 1) Create the pipeline info
        SDL_GPUGraphicsPipelineCreateInfo pipelineCreateInfo = default;

        // 2) We are writing 4 outputs (Albedo, Normals, Specular, Depth as color attachments)
        //    plus we have a hardware depth buffer
        pipelineCreateInfo.target_info.num_color_targets = 4;

        // Typically each color target has a known format.
        // For example:
        //   Albedo = B8G8R8A8_UNORM
        //   Normals = R16G16B16A16_SFLOAT
        //   Specular = R8G8B8A8_UNORM
        //   Depth = R32_FLOAT   (storing a float depth in a color channel)
        // Or you can do them all as R8G8B8A8 if you're sure about your usage.
        // Let's do something like below for demonstration:

        SDL_GPUColorTargetDescription[] colorDescs = new SDL_GPUColorTargetDescription[4];

        // Albedo
        colorDescs[0] = new SDL_GPUColorTargetDescription
        {
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM,
            blend_state = new SDL_GPUColorTargetBlendState
            {
                enable_blend = false, color_write_mask = 0xF // write all RGBA channels
            }
        };

        // Normals
        colorDescs[1] = new SDL_GPUColorTargetDescription
        {
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT,
            blend_state = new SDL_GPUColorTargetBlendState {enable_blend = false, color_write_mask = 0xF}
        };

        // Specular
        colorDescs[2] = new SDL_GPUColorTargetDescription
        {
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM,
            blend_state = new SDL_GPUColorTargetBlendState {enable_blend = false, color_write_mask = 0xF}
        };

        // outDepth (color-coded depth)
        colorDescs[3] = new SDL_GPUColorTargetDescription
        {
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R32_FLOAT,
            blend_state = new SDL_GPUColorTargetBlendState {enable_blend = false, color_write_mask = 0xF}
        };

        // Now set them in the pipeline create info
        fixed (SDL_GPUColorTargetDescription* cDescPtr = colorDescs)
        {
            pipelineCreateInfo.target_info.color_target_descriptions = cDescPtr;
        }

        pipelineCreateInfo.target_info.has_depth_stencil_target = true;
        // We'll do a real hardware depth for occlusion: e.g. D32_FLOAT
        pipelineCreateInfo.target_info.depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT;

        // 3) Depth-stencil state
        pipelineCreateInfo.depth_stencil_state.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS;
        pipelineCreateInfo.depth_stencil_state.enable_depth_test = true;
        pipelineCreateInfo.depth_stencil_state.enable_depth_write = true;
        pipelineCreateInfo.depth_stencil_state.enable_stencil_test = false;

        // 4) Vertex Input (matching geometry pass vertex shader):
        //    inPosition => location=0 => float3
        //    inNormal => location=1 => float3
        //    inTexCoord => location=2 => float2
        // define 3 attributes, 1 vertex buffer.
        // {vec3 pos; vec3 normal; vec2 uv;} => total size = 8 floats

        FillGraphicsPipelineVertexAttributes<PositionNormalTexCoordVertex>(allocator,
            ref pipelineCreateInfo.vertex_input_state);
        FillGraphicsPipelineVertexBuffer<PositionNormalTexCoordVertex>(allocator,
            ref pipelineCreateInfo.vertex_input_state);

        // 5) Primitive type
        pipelineCreateInfo.primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;

        // 6) Shaders
        pipelineCreateInfo.vertex_shader = vertexShader;
        pipelineCreateInfo.fragment_shader = fragmentShader;

        // 7) Finally, create the pipeline
        SDL_GPUGraphicsPipeline* pipeline = SDL_CreateGPUGraphicsPipeline(device, &pipelineCreateInfo);
        if (pipeline == null)
        {
            Log.Error("Failed to create geometry pipeline!");
            return null;
        }

        SDL_ReleaseGPUShader(device, vertexShader);
        SDL_ReleaseGPUShader(device, fragmentShader);

        // Return pipeline
        return pipeline;
    }

    public static SDL_GPUGraphicsPipeline* CreateSSAOPipeline(
        INativeAllocator allocator,
        SDL_GPUDevice* device
    )
    {
        SDL_GPUShader* vertexShader = CreateShader(allocator, "SSAO.vert", device);
        if (vertexShader == null)
        {
            Log.Error("Failed to create vertex shader!");
            return null;
        }

        SDL_GPUShader* fragmentShader = CreateShader(
            allocator, "SSAO.frag", device, 3, 2);
        if (fragmentShader == null)
        {
            Log.Error("Failed to create fragment shader!");
            return null;
        }

        // 2) Prepare the pipeline info
        SDL_GPUGraphicsPipelineCreateInfo pipelineCreateInfo = default;

        // We'll write 1 color output: the occlusion factor
        pipelineCreateInfo.target_info.num_color_targets = 1;

        // The pipeline needs to know the expected color format. If your AO texture is R32_FLOAT, do:
        SDL_GPUColorTargetDescription[] colorDescs = new SDL_GPUColorTargetDescription[1];
        colorDescs[0] = new SDL_GPUColorTargetDescription
        {
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8_UNORM,
            blend_state = new SDL_GPUColorTargetBlendState
            {
                enable_blend = false,
                color_write_mask = 0xF // Write R, G, B, A. The alpha channel might be unused, but 0xF = RGBA
            }
        };

        fixed (SDL_GPUColorTargetDescription* cDescPtr = colorDescs)
        {
            pipelineCreateInfo.target_info.color_target_descriptions = cDescPtr;
        }

        // We do not need hardware depth/stencil
        pipelineCreateInfo.target_info.has_depth_stencil_target = false;
        pipelineCreateInfo.target_info.depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID;

        // 3) Depth-stencil state is irrelevant here but let's fill safe defaults
        pipelineCreateInfo.depth_stencil_state.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS;
        pipelineCreateInfo.depth_stencil_state.enable_depth_test = false;
        pipelineCreateInfo.depth_stencil_state.enable_depth_write = false;
        pipelineCreateInfo.depth_stencil_state.enable_stencil_test = false;

        // 4) Vertex Input
        //   The SSAO vertex shader has:
        //   layout(location=0) in vec3 inPosition;
        //   layout(location=1) in vec2 inTexCoord;
        // So we have 2 attributes in a single vertex buffer
        // Typically, you'd do something like {float3 pos, float2 uv} => total 5 floats
        FillGraphicsPipelineVertexAttributes<PositionTextureCoordinateVertex>(
            allocator,
            ref pipelineCreateInfo.vertex_input_state
        );
        FillGraphicsPipelineVertexBuffer<PositionTextureCoordinateVertex>(
            allocator,
            ref pipelineCreateInfo.vertex_input_state
        );

        // 5) It's a fullscreen pass => triangle list
        pipelineCreateInfo.primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;

        // 6) Assign your compiled shaders
        pipelineCreateInfo.vertex_shader = vertexShader;
        pipelineCreateInfo.fragment_shader = fragmentShader;

        // 7) Create the pipeline
        SDL_GPUGraphicsPipeline* pipeline = SDL_CreateGPUGraphicsPipeline(device, &pipelineCreateInfo);
        if (pipeline == null)
        {
            Log.Error("Failed to create SSAO pipeline!");
            return null;
        }

        // Optionally, release the shader objects if you no longer need them
        SDL_ReleaseGPUShader(device, vertexShader);
        SDL_ReleaseGPUShader(device, fragmentShader);

        return pipeline;
    }

    public static SDL_GPUGraphicsPipeline* CreateLightingPassPipeline(
        INativeAllocator allocator,
        SDL_GPUDevice* device
    )
    {
        SDL_GPUShader* vertexShader = CreateShader(allocator, "Lightning.vert", device);
        if (vertexShader == null)
        {
            Log.Error("Failed to create vertex shader!");
            return null;
        }

        SDL_GPUShader* fragmentShader = CreateShader(
            allocator, "Lightning.frag", device, 6, 3);
        if (fragmentShader == null)
        {
            Log.Error("Failed to create fragment shader!");
            return null;
        }

        SDL_GPUGraphicsPipelineCreateInfo pipelineCreateInfo = default;

        // 1) We only output 1 color => final lit color
        pipelineCreateInfo.target_info.num_color_targets = 1;

        // The pipeline needs the color format of that 1 output. If your
        // LightingOutTexture is B8G8R8A8_UNORM, do:
        SDL_GPUColorTargetDescription[] colorDescs = new SDL_GPUColorTargetDescription[1];
        colorDescs[0] = new SDL_GPUColorTargetDescription
        {
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM,
            blend_state = new SDL_GPUColorTargetBlendState {enable_blend = false, color_write_mask = 0xF}
        };

        fixed (SDL_GPUColorTargetDescription* cDescPtr = colorDescs)
        {
            pipelineCreateInfo.target_info.color_target_descriptions = cDescPtr;
        }

        // 2) We do not need hardware depth for this pass => no occlusion test
        pipelineCreateInfo.target_info.has_depth_stencil_target = false;
        pipelineCreateInfo.target_info.depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID;

        pipelineCreateInfo.depth_stencil_state.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS;
        pipelineCreateInfo.depth_stencil_state.enable_depth_test = false;
        pipelineCreateInfo.depth_stencil_state.enable_depth_write = false;
        pipelineCreateInfo.depth_stencil_state.enable_stencil_test = false;

        // 3) Vertex Input. We'll typically have a float3 pos + float2 uv
        // Just like SSAO pass. E.g.:
        FillGraphicsPipelineVertexAttributes<PositionTextureCoordinateVertex>(
            allocator,
            ref pipelineCreateInfo.vertex_input_state
        );
        FillGraphicsPipelineVertexBuffer<PositionTextureCoordinateVertex>(
            allocator,
            ref pipelineCreateInfo.vertex_input_state
        );

        // 4) Triangles
        pipelineCreateInfo.primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;

        // 5) Assign Shaders
        pipelineCreateInfo.vertex_shader = vertexShader; // pass-through vertex
        pipelineCreateInfo.fragment_shader = fragmentShader; // your lighting.frag

        // 6) Create pipeline
        SDL_GPUGraphicsPipeline* pipeline = SDL_CreateGPUGraphicsPipeline(device, &pipelineCreateInfo);
        if (pipeline == null)
        {
            Log.Error("Failed to create lighting pipeline!");
            return null;
        }

        SDL_ReleaseGPUShader(device, vertexShader);
        SDL_ReleaseGPUShader(device, fragmentShader);
        return pipeline;
    }

    public static SDL_GPUGraphicsPipeline* CreatePresentPassPipeline(
        INativeAllocator allocator,
        SDL_GPUDevice* device
    )
    {
        SDL_GPUShader* vertexShader = CreateShader(allocator, "Present.vert", device);
        if (vertexShader == null)
        {
            Log.Error("Failed to create vertex shader!");
            return null;
        }

        SDL_GPUShader* fragmentShader = CreateShader(
            allocator, "Present.frag", device, 6, 3);
        if (fragmentShader == null)
        {
            Log.Error("Failed to create fragment shader!");
            return null;
        }

        // We'll create a pipeline that outputs exactly 1 color,
        // no depth/stencil needed, no blending

        SDL_GPUGraphicsPipelineCreateInfo pipelineCreateInfo = default;

        // 1) Single color target
        pipelineCreateInfo.target_info.num_color_targets = 1;

        // Typically for a swapchain, you might have a format like B8G8R8A8_UNORM.
        // If you are rendering to an offscreen texture for post-processing,
        // match that texture's format.
        SDL_GPUColorTargetDescription[] colorDescs = new SDL_GPUColorTargetDescription[1];
        colorDescs[0] = new SDL_GPUColorTargetDescription
        {
            // If your final pass is going straight to the swapchain (which is typically B8G8R8A8),
            // or a final color texture, match that format:
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM,
            blend_state = new SDL_GPUColorTargetBlendState
            {
                enable_blend = false, color_write_mask = 0xF // Write RGBA
            }
        };

        fixed (SDL_GPUColorTargetDescription* cDescPtr = colorDescs)
        {
            pipelineCreateInfo.target_info.color_target_descriptions = cDescPtr;
        }

        // 2) No depth/stencil
        pipelineCreateInfo.target_info.has_depth_stencil_target = false;
        pipelineCreateInfo.target_info.depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID;

        pipelineCreateInfo.depth_stencil_state.compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS;
        pipelineCreateInfo.depth_stencil_state.enable_depth_test = false;
        pipelineCreateInfo.depth_stencil_state.enable_depth_write = false;
        pipelineCreateInfo.depth_stencil_state.enable_stencil_test = false;

        // 3) Vertex Input: 2 attributes => location=0 (float3 inPos), location=1 (float2 inUV)
        // Typically a struct of 5 floats: (pos.x, pos.y, pos.z, uv.x, uv.y)
        FillGraphicsPipelineVertexAttributes<PositionTextureCoordinateVertex>(
            allocator,
            ref pipelineCreateInfo.vertex_input_state
        );
        FillGraphicsPipelineVertexBuffer<PositionTextureCoordinateVertex>(
            allocator,
            ref pipelineCreateInfo.vertex_input_state
        );

        // 4) We draw full screen triangles
        pipelineCreateInfo.primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;

        // 5) Shaders
        pipelineCreateInfo.vertex_shader = vertexShader; // The pass-through vertex
        pipelineCreateInfo.fragment_shader = fragmentShader; // The final copy fragment

        // 6) Create pipeline
        SDL_GPUGraphicsPipeline* pipeline = SDL_CreateGPUGraphicsPipeline(device, &pipelineCreateInfo);
        if (pipeline == null)
        {
            Log.Error("Failed to create Present pipeline!");
            return null;
        }

        SDL_ReleaseGPUShader(device, vertexShader);
        SDL_ReleaseGPUShader(device, fragmentShader);

        return pipeline;
    }

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
