// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;
using Avalon.Client.SDL.Engine.Vertices;

namespace Avalon.Client.SDL.Engine.Rendering.Assets;

public unsafe struct MeshAsset : IDisposable
{
    public IGPUBuffer Vertex { get; set; }
    public IGPUBuffer Index { get; set; }

    public static MeshAsset GenerateFullScreenQuad(SDL_GPUDevice* device)
    {
        PositionTextureCoordinateVertex[] vertices =
        [
            // Top-left: clip-space (-1, 1) with texture coordinate (0, 0)
            new() {Position = new Vector3(-1f, 1f, 0f), TextureCoordinate = new Vector2(0f, 0f)},
            // Bottom-left: clip-space (-1, -1) with texture coordinate (0, 1)
            new() {Position = new Vector3(-1f, -1f, 0f), TextureCoordinate = new Vector2(0f, 1f)},
            // Top-right: clip-space (1, 1) with texture coordinate (1, 0)
            new() {Position = new Vector3(1f, 1f, 0f), TextureCoordinate = new Vector2(1f, 0f)},
            // Bottom-right: clip-space (1, -1) with texture coordinate (1, 1)
            new() {Position = new Vector3(1f, -1f, 0f), TextureCoordinate = new Vector2(1f, 1f)}
        ];

        // Index buffer for the full screen quad.
        // This index order creates two triangles:
        // Triangle 1: vertices 0, 1, 2 (top-left, bottom-left, top-right)
        // Triangle 2: vertices 2, 1, 3 (top-right, bottom-left, bottom-right)
        ushort[] indices = [0, 1, 2, 2, 1, 3];

        return new MeshAsset
        {
            Vertex = SDLGPUBuffer.CreateVertexBuffer(device, vertices),
            Index = SDLGPUBuffer.CreateIndexBuffer(device, indices)
        };
    }

    public void Dispose()
    {
        Vertex.Dispose();
        Index.Dispose();
    }
}
