// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.SDL.Engine;

namespace Avalon.Client.SDL;

internal struct ApplicationContext
{
    public string Title { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public IWindow Window { get; set; }
}

internal struct ApplicationState
{
    public bool IsRunning { get; set; }
    public bool IsSuspended { get; set; }
    public bool ShouldQuit { get; set; }

    public ulong CurrentTime { get; set; }
    public ulong LastTime { get; set; }
    public float DeltaTime { get; set; }
}

public sealed unsafe class Application : IDisposable
{
    // NOTE: It's important that this memory is allocated only once (per thread) or else the application can crash with exit code 137 (out of memory).
    private static readonly ThreadLocal<ArenaNativeAllocator> GlobalInitializeAllocator =
        new(() => new ArenaNativeAllocator((int)Math.Pow(1024,
            2))); // 1 KB should be plenty of space for initialization related memory such as various "createinfo" data structures

    private readonly ApplicationContext _context;
    private readonly Renderer _renderer;
    private ApplicationState _state;

    public Application(string title, uint width, uint height)
    {
        _context = new ApplicationContext {Title = title, Width = width, Height = height};

        _state = new ApplicationState
        {
            IsRunning = false,
            IsSuspended = false,
            ShouldQuit = false,
            CurrentTime = 0,
            LastTime = 0,
            DeltaTime = 0.0f
        };

        _context.Window = new Window(title, width, height);
        if (!_context.Window.Setup(GlobalInitializeAllocator.Value!))
        {
        }

        _renderer = new Renderer(_context.Window, true);
        if (!_renderer.Setup(GlobalInitializeAllocator.Value!))
        {
        }
    }

    public void Dispose() => Cleanup();

    public void Run()
    {
        _state.IsRunning = true;
        _state.CurrentTime = SDL_GetPerformanceCounter();
        _state.LastTime = SDL_GetPerformanceCounter();

        while (!_state.ShouldQuit)
        {
            _state.DeltaTime = (float)((_state.CurrentTime - _state.LastTime) / (double)SDL_GetPerformanceFrequency());

            SDL_Event evt;
            while (SDL_PollEvent(&evt))
            {
                if (evt.type == (uint)SDL_EventType.SDL_EVENT_QUIT)
                {
                    _state.ShouldQuit = true;
                    break;
                }

                if (evt.type == (uint)SDL_EventType.SDL_EVENT_KEY_DOWN && evt.key.key == SDLK_ESCAPE)
                {
                    _state.ShouldQuit = true;
                    break;
                }

                // Scene will handle the event
            }

            if (_state.ShouldQuit)
            {
                break;
            }

            // Scene Update

            // Scene Render

            _renderer.BeginFrame();

            _renderer.EndFrame();

            _state.LastTime = _state.CurrentTime;
            _state.CurrentTime = SDL_GetPerformanceCounter();
        }
    }

    // SDL_SetWindowMouseGrab(true);

    private void Cleanup()
    {
        //TODO: Destroy asset cache, scenes, renderer, then window
        _renderer.Dispose();
        _context.Window.Dispose();
    }
}
