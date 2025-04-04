// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL.Engine;

public static unsafe class BufferBuilder
{
    public static SDL_GPUBuffer* CreateVertexBuffer<TVertex>(SDL_GPUDevice* device, int elementCount)
        where TVertex : unmanaged
    {
        SDL_GPUBufferCreateInfo vertexBufferCreateInfo = default;
        vertexBufferCreateInfo.usage = SDL_GPU_BUFFERUSAGE_VERTEX;
        vertexBufferCreateInfo.size = (uint)(sizeof(TVertex) * elementCount);
        SDL_GPUBuffer* vertexBuffer = SDL_CreateGPUBuffer(device, &vertexBufferCreateInfo);
        return vertexBuffer;
    }

    public static SDL_GPUBuffer* CreateIndexBuffer<TVertex>(SDL_GPUDevice* device, int elementCount)
        where TVertex : unmanaged
    {
        SDL_GPUBufferCreateInfo vertexBufferCreateInfo = default;
        vertexBufferCreateInfo.usage = SDL_GPU_BUFFERUSAGE_INDEX;
        vertexBufferCreateInfo.size = (uint)(sizeof(TVertex) * elementCount);
        SDL_GPUBuffer* vertexBuffer = SDL_CreateGPUBuffer(device, &vertexBufferCreateInfo);
        return vertexBuffer;
    }

    public static SDL_GPUTransferBuffer* CreateTransferBuffer<TElement>(SDL_GPUDevice* device, int elementCount,
        Action<Span<TElement>> map)
        where TElement : unmanaged
    {
        SDL_GPUTransferBufferCreateInfo transferBufferCreateInfo = default;
        transferBufferCreateInfo.usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD;
        transferBufferCreateInfo.size = (uint)(sizeof(TElement) * elementCount);
        SDL_GPUTransferBuffer* transferBuffer = SDL_CreateGPUTransferBuffer(device, &transferBufferCreateInfo);

        TElement* dataPointer = (TElement*)SDL_MapGPUTransferBuffer(device, transferBuffer, false);
        Span<TElement> data = new(dataPointer, elementCount);
        map(data);
        SDL_UnmapGPUTransferBuffer(device, transferBuffer);

        return transferBuffer;
    }

    public static void UploadDataToBuffer<T>(SDL_GPUDevice* device, SDL_GPUBuffer* destinationBuffer, T[] data)
        where T : unmanaged
    {
        int elementCount = data.Length;

        // Create a transfer buffer and copy the managed data into it.
        SDL_GPUTransferBuffer* transferBuffer = CreateTransferBuffer<T>(
            device,
            elementCount,
            span => data.AsSpan().CopyTo(span)
        );

        // Acquire a command buffer to record the GPU copy commands.
        SDL_GPUCommandBuffer* uploadCommandBuffer = SDL_AcquireGPUCommandBuffer(device);
        SDL_GPUCopyPass* copyPass = SDL_BeginGPUCopyPass(uploadCommandBuffer);

        // Set up the source transfer buffer location.
        SDL_GPUTransferBufferLocation bufferSource = default;
        bufferSource.transfer_buffer = transferBuffer;
        bufferSource.offset = 0;

        // Configure the destination region on the GPU buffer.
        SDL_GPUBufferRegion bufferDestination = default;
        bufferDestination.buffer = destinationBuffer;
        bufferDestination.offset = 0;
        bufferDestination.size = (uint)(sizeof(T) * elementCount);

        // Record the buffer copy command.
        SDL_UploadToGPUBuffer(copyPass, &bufferSource, &bufferDestination, false);

        // End the copy pass and submit the command buffer for execution.
        SDL_EndGPUCopyPass(copyPass);
        SDL_SubmitGPUCommandBuffer(uploadCommandBuffer);

        // Release the transfer buffer now that the data has been uploaded.
        SDL_ReleaseGPUTransferBuffer(device, transferBuffer);
    }
}
