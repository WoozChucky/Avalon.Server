// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL.Engine.Rendering;

public enum GPUBufferType
{
    Vertex,
    Index,
    Texture
}

public interface IGPUBuffer : IDisposable
{
    uint Elements { get; }
    uint ElementSize { get; }
    uint Size => Elements * ElementSize;
    GPUBufferType Type { get; }
    IntPtr GetData();

    void Bind(IntPtr renderPassPtr);
}

public sealed unsafe class SDLGPUBuffer : IGPUBuffer
{
    private readonly SDL_GPUDevice* _device;
    private readonly SDL_GPUBuffer* _native;

    private SDLGPUBuffer(GPUBufferType type, uint elements, uint elementSize, SDL_GPUBuffer* buffer,
        SDL_GPUDevice* device)
    {
        Type = type;
        Elements = elements;
        ElementSize = elementSize;
        _native = buffer;
        _device = device;
    }

    public IntPtr GetData() => (IntPtr)_native;

    public void Bind(IntPtr renderPassPtr)
    {
        SDL_GPURenderPass* renderPass = (SDL_GPURenderPass*)renderPassPtr;

        SDL_GPUBufferBinding bufferBinding;
        bufferBinding.buffer = (SDL_GPUBuffer*)GetData();
        bufferBinding.offset = 0;

        switch (Type)
        {
            case GPUBufferType.Vertex:
                SDL_BindGPUVertexBuffers(renderPass, 0, &bufferBinding, 1);
                break;
            case GPUBufferType.Index:
                SDL_GPUIndexElementSize elementSize = Elements > ushort.MaxValue
                    ? SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT
                    : SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_16BIT;
                SDL_BindGPUIndexBuffer(renderPass, &bufferBinding, elementSize);
                break;
            case GPUBufferType.Texture:
            default:
                throw new NotImplementedException("Binding of this buffer type is not implemented.");
        }
    }

    public GPUBufferType Type { get; }
    public uint Elements { get; }
    public uint ElementSize { get; }

    public void Dispose() => SDL_ReleaseGPUBuffer(_device, _native);

    public static SDLGPUBuffer CreateVertexBuffer<TVertex>(SDL_GPUDevice* device, TVertex[] vertices)
        where TVertex : unmanaged
    {
        SDL_GPUBufferCreateInfo vertexBufferCreateInfo = default;
        vertexBufferCreateInfo.usage = SDL_GPU_BUFFERUSAGE_VERTEX;
        vertexBufferCreateInfo.size = (uint)(sizeof(TVertex) * vertices.Length);
        SDL_GPUBuffer* buffer = SDL_CreateGPUBuffer(device, &vertexBufferCreateInfo);
        BufferBuilder.UploadDataToBuffer(device, buffer, vertices);
        return new SDLGPUBuffer(GPUBufferType.Vertex, (uint)vertices.Length, (uint)sizeof(TVertex), buffer, device);
    }

    public static SDLGPUBuffer CreateIndexBuffer<TIndex>(SDL_GPUDevice* device, TIndex[] indices)
        where TIndex : unmanaged
    {
        SDL_GPUBufferCreateInfo indexBufferCreateInfo = default;
        indexBufferCreateInfo.usage = SDL_GPU_BUFFERUSAGE_INDEX;
        indexBufferCreateInfo.size = (uint)(sizeof(TIndex) * indices.Length);
        SDL_GPUBuffer* buffer = SDL_CreateGPUBuffer(device, &indexBufferCreateInfo);
        BufferBuilder.UploadDataToBuffer(device, buffer, indices);
        return new SDLGPUBuffer(GPUBufferType.Index, (uint)indices.Length, (uint)sizeof(TIndex), buffer, device);
    }
}
