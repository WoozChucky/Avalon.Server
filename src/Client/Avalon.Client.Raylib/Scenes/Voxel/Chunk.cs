// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.


using Avalon.Common.Mathematics;

namespace Avalon.Client.Scenes.Voxel;

public class Chunk
{
    public const int ChunkSize = 16;
    public Voxel[,,] Voxels = new Voxel[ChunkSize, ChunkSize, ChunkSize];
    public Mesh Mesh;
    public bool NeedsMeshUpdate = true;

    public Chunk()
    {
        for (int x = 0; x < ChunkSize; x++)
        for (int y = 0; y < ChunkSize; y++)
        for (int z = 0; z < ChunkSize; z++)
        {
            Voxels[x, y, z] = new Voxel {IsActive = false, Type = VoxelType.Grass, Color = Color.Green};
        }
    }

    public Mesh GenerateChunkMesh(Chunk chunk, Vector3 chunkPosition)
    {
        // Count the number of visible faces
        int visibleFaces = 0;

        for (int x = 0; x < Chunk.ChunkSize; x++)
        for (int y = 0; y < Chunk.ChunkSize; y++)
        for (int z = 0; z < Chunk.ChunkSize; z++)
        {
            if (!chunk.Voxels[x, y, z].IsActive) continue;

            // Count visible faces
            if (IsFaceExposed(chunk, x, y, z, -1, 0, 0)) visibleFaces++; // Left
            if (IsFaceExposed(chunk, x, y, z, 1, 0, 0)) visibleFaces++;  // Right
            if (IsFaceExposed(chunk, x, y, z, 0, -1, 0)) visibleFaces++; // Bottom
            if (IsFaceExposed(chunk, x, y, z, 0, 1, 0)) visibleFaces++;  // Top
            if (IsFaceExposed(chunk, x, y, z, 0, 0, -1)) visibleFaces++; // Back
            if (IsFaceExposed(chunk, x, y, z, 0, 0, 1)) visibleFaces++;  // Front
        }

        // Create the mesh
        int vertexCount = visibleFaces * 4;  // 4 vertices per face
        int triangleCount = visibleFaces * 2; // 2 triangles per face
        Mesh mesh = new(vertexCount, triangleCount);

        mesh.AllocVertices();
        mesh.AllocNormals();
        mesh.AllocColors();
        mesh.AllocIndices();

        // Access spans to write data
        Span<float> vertices = mesh.VerticesAs<float>();
        Span<float> normals = mesh.NormalsAs<float>();
        Span<byte> colors = mesh.ColorsAs<byte>();
        Span<ushort> indices = mesh.IndicesAs<ushort>();

        int vertexOffset = 0;
        int indexOffset = 0;

        for (int x = 0; x < Chunk.ChunkSize; x++)
        for (int y = 0; y < Chunk.ChunkSize; y++)
        for (int z = 0; z < Chunk.ChunkSize; z++)
        {
            Voxel voxel = chunk.Voxels[x, y, z];
            if (!voxel.IsActive) continue;

            Vector3 position = new Vector3(x, y, z);

            if (IsFaceExposed(chunk, x, y, z, -1, 0, 0)) // Left
                AddFace(position, chunkPosition, voxel.Color, Vector3.left, ref vertices, ref normals, ref colors, ref indices, ref vertexOffset, ref indexOffset);
            if (IsFaceExposed(chunk, x, y, z, 1, 0, 0)) // Right
                AddFace(position, chunkPosition, voxel.Color, Vector3.right, ref vertices, ref normals, ref colors, ref indices, ref vertexOffset, ref indexOffset);
            if (IsFaceExposed(chunk, x, y, z, 0, -1, 0)) // Bottom
                AddFace(position, chunkPosition, voxel.Color, Vector3.down, ref vertices, ref normals, ref colors, ref indices, ref vertexOffset, ref indexOffset);
            if (IsFaceExposed(chunk, x, y, z, 0, 1, 0)) // Top
                AddFace(position, chunkPosition, voxel.Color, Vector3.up, ref vertices, ref normals, ref colors, ref indices, ref vertexOffset, ref indexOffset);
            if (IsFaceExposed(chunk, x, y, z, 0, 0, -1)) // Back
                AddFace(position, chunkPosition, voxel.Color, Vector3.back, ref vertices, ref normals, ref colors, ref indices, ref vertexOffset, ref indexOffset);
            if (IsFaceExposed(chunk, x, y, z, 0, 0, 1)) // Front
                AddFace(position, chunkPosition, voxel.Color, Vector3.forward, ref vertices, ref normals, ref colors, ref indices, ref vertexOffset, ref indexOffset);
        }

        // Upload mesh to GPU
        UploadMesh(ref mesh, false);

        return mesh;
    }




    private void AddFace(Vector3 position, Vector3 chunkPosition, Color color, Vector3 normal,
        ref Span<float> vertices, ref Span<float> normals, ref Span<byte> colors, ref Span<ushort> indices,
        ref int vertexOffset, ref int indexOffset)
    {
        // Define quad vertices relative to chunkPosition
        Vector3[] faceVertices = new[]
        {
            chunkPosition + position + new Vector3(-0.5f, -0.5f, 0.5f),
            chunkPosition + position + new Vector3(0.5f, -0.5f, 0.5f),
            chunkPosition + position + new Vector3(0.5f, 0.5f, 0.5f),
            chunkPosition + position + new Vector3(-0.5f, 0.5f, 0.5f)
        };

        // Write vertices
        foreach (var v in faceVertices)
        {
            vertices[vertexOffset++] = v.x;
            vertices[vertexOffset++] = v.y;
            vertices[vertexOffset++] = v.z;
        }

        // Write normals
        for (int i = 0; i < 4; i++)
        {
            normals[vertexOffset - 12 + i * 3] = normal.x;
            normals[vertexOffset - 12 + i * 3 + 1] = normal.y;
            normals[vertexOffset - 12 + i * 3 + 2] = normal.z;
        }

        // Write colors
        for (int i = 0; i < 4; i++)
        {
            colors[(vertexOffset / 3 - 4 + i) * 4 + 0] = color.R;
            colors[(vertexOffset / 3 - 4 + i) * 4 + 1] = color.G;
            colors[(vertexOffset / 3 - 4 + i) * 4 + 2] = color.B;
            colors[(vertexOffset / 3 - 4 + i) * 4 + 3] = color.A;
        }

        // Write indices
        indices[indexOffset++] = (ushort)(vertexOffset / 3 - 4);
        indices[indexOffset++] = (ushort)(vertexOffset / 3 - 3);
        indices[indexOffset++] = (ushort)(vertexOffset / 3 - 2);
        indices[indexOffset++] = (ushort)(vertexOffset / 3 - 4);
        indices[indexOffset++] = (ushort)(vertexOffset / 3 - 2);
        indices[indexOffset++] = (ushort)(vertexOffset / 3 - 1);
    }


    private bool IsFaceExposed(Chunk chunk, int x, int y, int z, int dx, int dy, int dz)
    {
        int nx = x + dx, ny = y + dy, nz = z + dz;

        // Check boundaries
        if (nx < 0 || ny < 0 || nz < 0 ||
            nx >= Chunk.ChunkSize || ny >= Chunk.ChunkSize || nz >= Chunk.ChunkSize)
            return true; // Outside the chunk is exposed

        return !chunk.Voxels[nx, ny, nz].IsActive; // Exposed if neighbor is inactive
    }


}
