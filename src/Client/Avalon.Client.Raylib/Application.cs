// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Scenes;
using Avalon.Client.Scenes.Voxel;
using rlImGui_cs;

namespace Avalon.Client;

public class Application(int width, int height)
{
    private IScene? _scene;

    public void Setup()
    {
        SetConfigFlags(
            ConfigFlags.VSyncHint |
            ConfigFlags.HighDpiWindow |
            ConfigFlags.ResizableWindow |
            ConfigFlags.AlwaysRunWindow |
            ConfigFlags.MaximizedWindow |
            ConfigFlags.Msaa4xHint |
            ConfigFlags.BorderlessWindowMode
        );

        SetTraceLogLevel(TraceLogLevel.All);
        InitWindow(width, height, "Avalon");
        SetExitKey(KeyboardKey.Null);
        SetTargetFPS(240);
        DisableCursor();

        rlImGui.Setup(true, true);

        _scene = new RegniaScene();
        _scene = new VoxelScene();

        _scene.Setup();
    }

    public void Run()
    {
        while (!WindowShouldClose())
        {
            _scene?.Update();
            _scene?.Render();
        }

        Shutdown();
    }

    private void Shutdown()
    {
        _scene?.Unload();
        rlImGui.Shutdown();
        CloseWindow();
    }
}
