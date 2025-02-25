// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace Avalon.Client.SDL;

public sealed unsafe class App : IDisposable
{
    private SceneBase? _currentScene;
    private int _exampleIndex = -1;
    private int _goToExampleIndex;
    private int _scenesCount;
    private ImmutableArray<Type> _sceneTypes = [];

    public void Dispose()
    {
        _currentScene?.QuitInternal();
        _currentScene = null;
    }

    public int Run()
    {
        if (!Initialize())
        {
            return 1;
        }

        SDL_AddEventWatch(new SDL_EventFilter(&AppLifecycleWatcher), null);
        return Loop();
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

    private bool Initialize()
    {
        if (!SDL_Init(SDL_INIT_VIDEO | SDL_INIT_GAMEPAD))
        {
            Console.Error.WriteLine("Failed to initialize SDL: " + SDL_GetError());
            return false;
        }

        string? assemblyName = Assembly.GetEntryAssembly()!.GetName().Name;
        Console.WriteLine($"Welcome to the {assemblyName} example suite!");
        Console.WriteLine("Press 1/2 (or LB/RB) to move between examples!");

        _sceneTypes =
        [
            ..AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(SceneBase).IsAssignableFrom(type) && !type.IsAbstract)
        ];
        _scenesCount = _sceneTypes.Length;

        return true;
    }

    private int Loop()
    {
        bool isExiting = false;
        bool canDraw = true;
        float lastTime = 0.0f;

        while (!isExiting)
        {
            SDL_Event e;
            if (SDL_PollEvent(&e))
            {
                SDL_EventType eventType = (SDL_EventType)e.type;
                HandleEvent(eventType, ref isExiting, ref canDraw, e);
            }

            float newTime = SDL_GetTicks() / 1000.0f;
            float deltaTime = newTime - lastTime;
            lastTime = newTime;

            if (!Frame(deltaTime, canDraw))
            {
                return 1;
            }
        }

        return 0;
    }

    private void HandleEvent(
        SDL_EventType eventType,
        ref bool isExiting,
        ref bool canDraw,
        SDL_Event e)
    {
        switch (eventType)
        {
            case SDL_EventType.SDL_EVENT_QUIT:
            {
                _currentScene?.QuitInternal();
                _currentScene = null;
                isExiting = true;
                break;
            }

            case SDL_EventType.SDL_EVENT_USER:
            {
                switch (e.user.code)
                {
                    case 0:
                        canDraw = false;
                        break;
                    case 1:
                        canDraw = true;
                        break;
                }

                break;
            }

            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            {
                SDL_Scancode key = e.key.scancode;
                if (key == SDL_Scancode.SDL_SCANCODE_2)
                {
                    _goToExampleIndex = _exampleIndex + 1;
                    if (_goToExampleIndex >= _scenesCount)
                    {
                        _goToExampleIndex = 0;
                    }
                }
                else if (key == SDL_Scancode.SDL_SCANCODE_1)
                {
                    _goToExampleIndex = _exampleIndex - 1;
                    if (_goToExampleIndex < 0)
                    {
                        _goToExampleIndex = _scenesCount - 1;
                    }
                }
                else
                {
                    _currentScene?.KeyboardEvent(e.key);
                }

                break;
            }
        }
    }

    private bool Frame(float deltaTime, bool canDraw)
    {
        if (_goToExampleIndex != -1)
        {
            SceneBase? previousExample = _currentScene;
            if (previousExample != null)
            {
                _currentScene = null;
                previousExample.QuitInternal();
                // ReSharper disable once RedundantAssignment
#pragma warning disable IDE0059
                previousExample = null;
#pragma warning restore IDE0059

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long bytesEnding = Process.GetCurrentProcess().WorkingSet64;
                string bytesEndingString =
                    (bytesEnding / Math.Pow(1024, 2)).ToString("0.00 MB", CultureInfo.InvariantCulture);
                Console.WriteLine("ENDING EXAMPLE, TOTAL MEMORY SIZE AFTER QUIT: {0}", bytesEndingString);
            }

            _exampleIndex = _goToExampleIndex;
            _currentScene = (SceneBase)Activator.CreateInstance(_sceneTypes[_exampleIndex])!;
            long bytesStartingBeforeInit = Process.GetCurrentProcess().WorkingSet64;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            string bytesStartingStringBeforeInit =
                (bytesStartingBeforeInit / Math.Pow(1024, 2)).ToString("0.00 MB", CultureInfo.InvariantCulture);
            Console.WriteLine("STARTING EXAMPLE: '{0}', TOTAL MEMORY SIZE BEFORE INIT: {1}", _currentScene.Name,
                bytesStartingStringBeforeInit);

            bool isExampleInitialized = _currentScene.InitializeInternal();
            if (!isExampleInitialized)
            {
                Console.Error.WriteLine("\nInit failed!");
                return false;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long bytesStartingAfterInit = Process.GetCurrentProcess().WorkingSet64;
            string bytesStartingStringAfterInit =
                (bytesStartingAfterInit / Math.Pow(1024, 2)).ToString("0.00 MB", CultureInfo.InvariantCulture);
            Console.WriteLine("INITIALIZED EXAMPLE: '{0}', TOTAL MEMORY SIZE AFTER INIT: {1}", _currentScene.Name,
                bytesStartingStringAfterInit);

            _goToExampleIndex = -1;
        }

        if (_currentScene != null)
        {
            if (!_currentScene.Update(deltaTime))
            {
                Console.Error.WriteLine("Update failed!");
                return false;
            }

            if (canDraw)
            {
                if (!_currentScene.Draw(deltaTime))
                {
                    Console.Error.WriteLine("Draw failed!");
                    return false;
                }
            }
        }

        return true;
    }
}
