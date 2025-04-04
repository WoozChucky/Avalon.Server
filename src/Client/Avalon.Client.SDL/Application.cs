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

        int usedBytes = GlobalInitializeAllocator.Value.Used;
        int usedKb = usedBytes / 1024;

        Log.Information("Application initialized with {UsedBytes}/{CapacityBytes} bytes of memory", usedBytes,
            GlobalInitializeAllocator.Value.Capacity);
    }

    public void Dispose() => Cleanup();

    public void Run()
    {
        _state.IsRunning = true;
        _state.CurrentTime = SDL_GetPerformanceCounter();
        _state.LastTime = SDL_GetPerformanceCounter();

        /*
        PositionTextureCoordinateVertex[] fullScreenQuadVertices =
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
        ushort[] fullScreenQuadIndices = [0, 1, 2, 2, 1, 3];

        SDL_GPUDevice* device = (SDL_GPUDevice*)_renderer.GetDeviceNativeHandle();

        SDL_GPUBuffer* vertexBuffer = BufferBuilder.CreateVertexBuffer<PositionTextureCoordinateVertex>(
            device,
            fullScreenQuadVertices.Length
        );

        SDL_GPUBuffer* indexBuffer = BufferBuilder.CreateIndexBuffer<ushort>(
            device,
            fullScreenQuadIndices.Length
        );

        {
            SDL_GPUTransferBuffer* transferBuffer = BufferBuilder.CreateTransferBuffer<PositionTextureCoordinateVertex>(
                device,
                fullScreenQuadVertices.Length,
                data => fullScreenQuadVertices.AsSpan().CopyTo(data)
            );

            SDL_GPUCommandBuffer* uploadCommandBuffer = SDL_AcquireGPUCommandBuffer(device);
            SDL_GPUCopyPass* copyPass = SDL_BeginGPUCopyPass(uploadCommandBuffer);

            SDL_GPUTransferBufferLocation bufferSource = default;
            bufferSource.transfer_buffer = transferBuffer;
            bufferSource.offset = 0;
            SDL_GPUBufferRegion bufferDestination = default;
            bufferDestination.buffer = vertexBuffer;
            bufferDestination.offset = 0;
            bufferDestination.size = (uint)(sizeof(PositionTextureCoordinateVertex) * fullScreenQuadVertices.Length);
            SDL_UploadToGPUBuffer(copyPass, &bufferSource, &bufferDestination, false);

            SDL_EndGPUCopyPass(copyPass);
            SDL_SubmitGPUCommandBuffer(uploadCommandBuffer);
            SDL_ReleaseGPUTransferBuffer(device, transferBuffer);
        }
        {
            SDL_GPUTransferBuffer* transferBuffer = BufferBuilder.CreateTransferBuffer<ushort>(
                device,
                fullScreenQuadIndices.Length,
                data => fullScreenQuadIndices.AsSpan().CopyTo(data)
            );

            SDL_GPUCommandBuffer* uploadCommandBuffer = SDL_AcquireGPUCommandBuffer(device);
            SDL_GPUCopyPass* copyPass = SDL_BeginGPUCopyPass(uploadCommandBuffer);

            SDL_GPUTransferBufferLocation bufferSource = default;
            bufferSource.transfer_buffer = transferBuffer;
            bufferSource.offset = 0;
            SDL_GPUBufferRegion bufferDestination = default;
            bufferDestination.buffer = indexBuffer;
            bufferDestination.offset = 0;
            bufferDestination.size = (uint)(sizeof(ushort) * fullScreenQuadIndices.Length);
            SDL_UploadToGPUBuffer(copyPass, &bufferSource, &bufferDestination, false);

            SDL_EndGPUCopyPass(copyPass);
            SDL_SubmitGPUCommandBuffer(uploadCommandBuffer);
            SDL_ReleaseGPUTransferBuffer(device, transferBuffer);
        }
        */

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
            {
                if (_renderer.BeginShadowPass())
                {
                    // Scene Render Shadow
                    _renderer.EndShadowPass();
                }

                if (_renderer.BeginGeometryPass())
                {
                    // Scene Render Geometry
                    _renderer.EndGeometryPass();
                }

                if (_renderer.BeginSsaoPass())
                {
                    // Scene Render SSAO
                    _renderer.EndSsaoPass();
                }

                if (_renderer.BeginLightningPass())
                {
                    // Scene Render Lightning
                    _renderer.EndLightningPass();
                }

                if (_renderer.BeginPostProcessPass())
                {
                    // Scene Render Post Process
                    _renderer.EndPostProcessPass();
                }

                if (_renderer.BeginPresentPass())
                {
                    // Scene Render Present
                    _renderer.EndPresentPass();
                }
            }
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
