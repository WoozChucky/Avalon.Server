// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Numerics;
using Avalon.Client.Engine;
using ImGuiNET;
using rlImGui_cs;

namespace Avalon.Client.Scenes.Voxel;

public class VoxelScene : IScene
{
    private readonly CameraThirdPerson _camera = new();
    private Frustum _frustum = new();
    private bool _showingCursor;
    private bool _showingImGui;

    private readonly Chunk chunk = new();

    public void Setup()
    {
        _camera.Setup(45, new Vector3(-5.0f, 5.0f, -5.0f), new Vector3(7.0f, 7.0f, 7.0f));
        _camera.ViewAngles.x = 3.90f;
        _camera.ViewAngles.y = -0.45f;

        // Generate a chunk
        // Create a simple flat terrain to test the mesh
        for (int x = 0; x < Chunk.ChunkSize; x++)
        for (int y = 0; y < Chunk.ChunkSize / 2; y++) // Half the height
        for (int z = 0; z < Chunk.ChunkSize; z++)
        {
            chunk.Voxels[x, y, z].IsActive = true;
            chunk.Voxels[x, y, z].Color = Color.Green; // Assign a simple color
        }

        // Generate the mesh
        chunk.Mesh = chunk.GenerateChunkMesh(chunk, new Vector3(0, 0, 0));

        EnableCursor();
    }

    public void Update()
    {
        if (IsKeyPressed(KeyboardKey.C))
        {
            _showingCursor = !_showingCursor;
            if (_showingCursor)
            {
                EnableCursor();
            }
            else
            {
                DisableCursor();
            }
        }

        if (IsKeyPressed(KeyboardKey.F1))
        {
            _showingImGui = !_showingImGui;
        }

        _camera.Update();
        _frustum = _camera.CalculateFrustum(GetScreenWidth(), GetScreenHeight());
    }

    public void Render()
    {
        BeginDrawing();

        ClearBackground(Color.SkyBlue);

        _camera.BeginMode3D();

        DrawGrid(1000, 1.0f);

        RenderChunk(chunk, new Vector3(0, 0, 0));

        _camera.EndMode3D();

        DrawFPS(10, 10);
        DrawText($"{GetFrameTime():#,0.000} ms", 10, 30, 20, Color.Lime);
        DrawText($"{_camera.ViewAngles}", 10, 50, 20, Color.Lime);

        if (_showingImGui)
        {
            rlImGui.Begin();

            if (ImGui.Begin("Debug Statistics"))
            {
                // TODO
            }

            ImGui.End();

            rlImGui.End();
        }

        EndDrawing();
    }

    public void Unload()
    {
        UnloadMesh(chunk.Mesh);
    }

    private void RenderChunk(Chunk chunk, Vector3 chunkPosition)
    {
        // Check if the chunk has a valid mesh
        if (chunk.Mesh.VertexCount > 0 && chunk.Mesh.TriangleCount > 0)
        {
            // Create a translation matrix to position the chunk in the world
            Matrix4x4 transform = Raymath.MatrixTranslate(chunkPosition.X, chunkPosition.Y, chunkPosition.Z);

            // Draw the chunk's mesh
            DrawMesh(chunk.Mesh, LoadMaterialDefault(), transform);
        }
    }
}
