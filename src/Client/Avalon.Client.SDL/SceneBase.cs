// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL;

public abstract unsafe class SceneBase
{
    // NOTE: It's important that this memory is allocated only once (per thread) or else the application can crash with exit code 137 (out of memory).
    private static readonly ThreadLocal<ArenaNativeAllocator> GlobalInitializeAllocator =
        new(() => new ArenaNativeAllocator((int)Math.Pow(1024,
            2))); // 1 KB should be plenty of space for initialization related memory such as various "createinfo" data structures

    private bool _hasQuit;

    protected SceneBase(WindowOptions? windowOptions = null)
    {
        AssetsDirectory = AppContext.BaseDirectory;
        WindowOptions = windowOptions ?? new WindowOptions {Width = 1920, Height = 1080};
    }

    public SDL_Window* Window { get; private set; }

    public string AssetsDirectory { get; set; }

    public WindowOptions WindowOptions { get; }

    public string Name { get; protected set; } = string.Empty;

    public int ScreenWidth { get; private set; }

    public int ScreenHeight { get; private set; }

    public abstract bool Initialize(INativeAllocator allocator);

    public abstract void Quit();

    public abstract void KeyboardEvent(SDL_KeyboardEvent e);

    public abstract bool Update(float deltaTime);

    public abstract bool Draw(float deltaTime);

    internal void QuitInternal()
    {
        bool hasAlreadyQuit = Interlocked.CompareExchange(ref _hasQuit, true, false);
        if (hasAlreadyQuit)
        {
            return;
        }

        Quit();

        SDL_DestroyWindow(Window);
        Window = null;
    }

    internal bool InitializeInternal()
    {
        ArenaNativeAllocator allocator = GlobalInitializeAllocator.Value!;

        CString exampleNameCString = GlobalInitializeAllocator.Value.AllocateCString(Name);
        Window = SDL_CreateWindow(
            exampleNameCString,
            WindowOptions.Width,
            WindowOptions.Height,
            0);
        if (Window == null)
        {
            Console.Error.WriteLine("CreateWindow failed: " + SDL_GetError());
            return false;
        }

        int windowWidth;
        int windowHeight;
        SDL_GetWindowSize(Window, &windowWidth, &windowHeight);
        ScreenWidth = windowWidth;
        ScreenHeight = windowHeight;

        bool isInitialized = Initialize(allocator);
        allocator.Reset();
        return isInitialized;
    }
}
