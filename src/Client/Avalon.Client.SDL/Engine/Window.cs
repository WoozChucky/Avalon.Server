// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.SDL.Engine;

public interface IWindow : IDisposable
{
    bool Setup(ArenaNativeAllocator allocator);

    IntPtr GetNativeHandle();
}

public sealed unsafe class Window(string title, uint width, uint height) : IWindow
{
    private SDL_Window* _native;

    public bool Setup(ArenaNativeAllocator allocator)
    {
        if (!SDL_Init(SDL_INIT_AUDIO | SDL_INIT_VIDEO))
        {
            Log.Error("SDL_Init failed: {Error}", SDL_GetError());
            return false;
        }

        CString cTitle = allocator.AllocateCString(title);

        _native = SDL_CreateWindow(cTitle, (int)width, (int)height, SDL_WINDOW_RESIZABLE | SDL_WINDOW_VULKAN);
        if (_native == null)
        {
            Log.Error("SDL_CreateWindow failed: {Error}", SDL_GetError());
            return false;
        }

        SDL_AddEventWatch(new SDL_EventFilter(&AppLifecycleWatcher), null);

        return true;
    }

    public IntPtr GetNativeHandle() => (IntPtr)_native;

    public void Dispose()
    {
        if (_native != null)
        {
            SDL_RemoveEventWatch(new SDL_EventFilter(&AppLifecycleWatcher), null);
            SDL_DestroyWindow(_native);
        }

        SDL_Quit();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static CBool AppLifecycleWatcher(void* userData, SDL_Event* e)
    {
        // This callback may be on a different thread, so let's
        // push these events as USER events so they appear
        // in the main thread's event loop.
        // That allows us to cancel drawing before/after we finish
        // drawing a frame, rather than mid-draw (which can crash!).

        SDL_EventType eventType = (SDL_EventType)e->type;
        switch (eventType)
        {
            case SDL_EventType.SDL_EVENT_DID_ENTER_BACKGROUND:
            {
                SDL_Event newEvent;
                newEvent.type = (uint)SDL_EventType.SDL_EVENT_USER;
                newEvent.user.code = 0;
                SDL_PushEvent(&newEvent);
                break;
            }

            case SDL_EventType.SDL_EVENT_WILL_ENTER_FOREGROUND:
            {
                SDL_Event newEvent;
                newEvent.type = (uint)SDL_EventType.SDL_EVENT_USER;
                newEvent.user.code = 1;
                SDL_PushEvent(&newEvent);
                break;
            }
        }

        return false;
    }
}
