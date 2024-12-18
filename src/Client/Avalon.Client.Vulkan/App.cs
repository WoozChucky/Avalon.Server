// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Camera;
using Avalon.Client.Vulkan.Controls;
using Avalon.Client.Vulkan.Engine;
using Avalon.Client.Vulkan.Systems.ImGui;
using Avalon.Client.Vulkan.Systems.MeshTest;
using Avalon.Client.Vulkan.Systems.PointLight;
using Avalon.Client.Vulkan.Systems.Simple;
using Avalon.Client.Vulkan.Utils;

namespace Avalon.Client.Vulkan;

public partial class App : IDisposable
{
// set to true to force FIFO swapping
    private const bool USE_FIFO = false;

    private readonly LveDevice device = null!;
    private readonly long fpsUpdateInterval = 5 * 10_000;

    private readonly Dictionary<uint, LveGameObject> gameObjects = new();
    private readonly LveDescriptorPool globalPool = null!;
    private readonly int height = 1200;

    // ImGui
    private readonly ImGuiController imGuiController = null!;
    private readonly LveRenderer lveRenderer = null!;
    private readonly Dictionary<uint, LveMeshObject> meshObjects = new();

    // Vk api
    private readonly Vk vk = null!;
    private readonly int width = 1800;
    private readonly string windowName = "Vulkan Tut";

    private ICamera camera = null!;

    private CameraController cameraController = null!;
    private long fpsLastUpdate;
    private DescriptorSet[] globalDescriptorSets = null!;

    private LveDescriptorSetLayout globalSetLayout = null!;

    private readonly IInputContext input = null!;
    private KeyboardController keyboardController = null!;
    private Mesh2Renderer mesh2Renderer = null!;

    // mouse stuff
    private MouseState mouseLast;
    private PointLightRenderSystem pointLightRenderSystem = null!;
    private SboMeshTest sboMeshTest = new();

    private LveBuffer sboMeshTestBuffer = null!;

    private SimpleRenderSystem simpleRenderSystem = null!;
    private LveBuffer[] uboBuffers = null!;


    private GlobalUbo[] ubos = null!;

    // Window stuff
    private IView window = null!;

    public App()
    {
        log.RestartTimer();
        log.d("startup", "starting up");

        vk = Vk.GetApi();
        log.d("startup", "got vk");

        initWindow();
        log.d("startup", "got window");

        device = new LveDevice(vk, window);
        log.d("startup", "got device");

        lveRenderer = new LveRenderer(vk, window, device, USE_FIFO);
        log.d("startup", "got renderer");

        globalPool = new LveDescriptorPool.Builder(vk, device)
            .SetMaxSets((uint)LveSwapChain.MAX_FRAMES_IN_FLIGHT)
            .AddPoolSize(DescriptorType.UniformBuffer, (uint)LveSwapChain.MAX_FRAMES_IN_FLIGHT)
            .AddPoolSize(DescriptorType.StorageBuffer, 10)
            .Build();
        log.d("startup", "global descriptor pool created");

        loadGameObjects();
        log.d("startup", "objects loaded");

        input = window.CreateInput();

        imGuiController = new ImGuiController(
            vk,
            window,
            input,
            device.VkPhysicalDevice,
            device.GraphicsFamilyIndex,
            LveSwapChain.MAX_FRAMES_IN_FLIGHT,
            lveRenderer.SwapChainImageFormat,
            lveRenderer.SwapChainDepthFormat,
            device.GetMsaaSamples()
        );
        log.d("startup", "imgui loaded");
    }

    public void Dispose()
    {
        window.Dispose();
        lveRenderer.Dispose();
        simpleRenderSystem.Dispose();
        pointLightRenderSystem.Dispose();
        imGuiController.Dispose();
        device.Dispose();

        GC.SuppressFinalize(this);
    }

    public void Run()
    {
        int frames = LveSwapChain.MAX_FRAMES_IN_FLIGHT;
        ubos = new GlobalUbo[frames];
        uboBuffers = new LveBuffer[frames];
        for (int i = 0; i < frames; i++)
        {
            ubos[i] = new GlobalUbo();
            uboBuffers[i] = new LveBuffer(
                vk, device,
                GlobalUbo.SizeOf(),
                1,
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );
            uboBuffers[i].Map();
        }

        log.d("run", "initialized ubo buffers");


        // this should be a Uniform Buffer, but wanted to show how Storage Buffers can work
        // with a mesh shader, you can feed in any kind of information in any format
        // there's no direct mapping/binding to vertex info, sky's the limit!
        sboMeshTestBuffer = new LveBuffer(
            vk, device,
            SboMeshTest.SizeOf(),
            1,
            BufferUsageFlags.StorageBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
        );
        sboMeshTestBuffer.Map();
        sboMeshTestBuffer.WriteToBuffer(sboMeshTest);

        ShaderStageFlags shaderuse = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit |
                                     ShaderStageFlags.MeshBitExt | ShaderStageFlags.TaskBitExt;
        globalSetLayout = new LveDescriptorSetLayout.Builder(vk, device)
            .AddBinding(0, DescriptorType.UniformBuffer, shaderuse)
            .AddBinding(1, DescriptorType.StorageBuffer, shaderuse)
            .Build();

        globalDescriptorSets = new DescriptorSet[frames];
        for (int i = 0; i < globalDescriptorSets.Length; i++)
        {
            _ = new LveDescriptorSetWriter(vk, device, globalSetLayout)
                .WriteBuffer(0, uboBuffers[i].DescriptorInfo())
                .WriteBuffer(1, sboMeshTestBuffer.DescriptorInfo())
                .Build(
                    globalPool,
                    globalSetLayout.GetDescriptorSetLayout(), ref globalDescriptorSets[i]
                );
        }

        log.d("run", "got globalDescriptorSets");


        simpleRenderSystem = new SimpleRenderSystem(
            vk, device,
            lveRenderer.GetSwapChainRenderPass(),
            globalSetLayout.GetDescriptorSetLayout()
        );

        pointLightRenderSystem = new PointLightRenderSystem(
            vk, device,
            lveRenderer.GetSwapChainRenderPass(),
            globalSetLayout.GetDescriptorSetLayout()
        );

        mesh2Renderer = new Mesh2Renderer(
            vk, device,
            lveRenderer.GetSwapChainRenderPass(),
            globalSetLayout.GetDescriptorSetLayout()
        );
        log.d("run", "got render systems");


        camera = new OrthographicCamera(Vector3.Zero, 4f, -20f, -140f, window.FramebufferSize);
        //camera = new PerspectiveCamera(new Vector3(5,5,5), 45f, 0f, 0f, window.FramebufferSize);
        cameraController = new CameraController(camera, (IWindow)window, input);
        resize(window.FramebufferSize);
        keyboardController = new KeyboardController(input);
        keyboardController.OnKeyPressed += onKeyPressed;
        log.d("run", "got camera and controls");

        //Console.WriteLine($"GlobalUbo blittable={BlittableHelper<GlobalUbo>.IsBlittable}");
        FirstAppGuiInit();

        MainLoop();
    }

    private void onKeyPressed(Key key)
    {
        switch (key)
        {
            case Key.Space:
                pointLightRenderSystem.RotateLightsEnabled = !pointLightRenderSystem.RotateLightsEnabled;
                break;
            case Key.KeypadAdd:
                pointLightRenderSystem.RotateSpeed += 0.5f;
                break;
            case Key.KeypadSubtract:
                pointLightRenderSystem.RotateSpeed -= 0.5f;
                break;
        }
    }


    private void render(double delta)
    {
        imGuiController.Update((float)delta);

        //ImGui.ShowDemoWindow();
        FirstAppGuiUpdate();

        mouseLast = cameraController.GetMouseState();

        CommandBuffer? commandBuffer = lveRenderer.BeginFrame();
        int frameIndex = lveRenderer.GetFrameIndex();

        if (commandBuffer is not null)
        {
            FrameInfo frameInfo = new()
            {
                FrameIndex = frameIndex,
                FrameTime = (float)delta,
                CommandBuffer = commandBuffer.Value,
                Camera = camera,
                GlobalDescriptorSet = globalDescriptorSets[frameIndex],
                GameObjects = gameObjects,
                MeshObjects = meshObjects
            };

            pointLightRenderSystem.Update(frameInfo, ref ubos[frameIndex]);

            ubos[frameIndex].Update(camera.GetProjectionMatrix(), camera.GetViewMatrix(), camera.GetFrontVec4());
            uboBuffers[frameIndex].WriteBytesToBuffer(ubos[frameIndex].AsBytes());

            lveRenderer.BeginSwapChainRenderPass(commandBuffer.Value);

            // render solid objects first!
            simpleRenderSystem.Render(frameInfo);
            mesh2Renderer.Render(frameInfo);

            pointLightRenderSystem.Render(frameInfo);

            imGuiController.Render(commandBuffer.Value, lveRenderer.SwapChain.GetFrameBufferAt((uint)frameIndex),
                lveRenderer.SwapChain.GetSwapChainExtent());

            lveRenderer.EndSwapChainRenderPass(commandBuffer.Value);

            lveRenderer.EndFrame();
        }
    }

    private void MainLoop()
    {
        window.Run();

        vk.DeviceWaitIdle(device.VkDevice);
    }

    private void initWindow()
    {
        WindowOptions options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(width, height), Title = windowName
        };

        window = Window.Create(options);
        window.Initialize();

        if (window.VkSurface is null)
        {
            throw new Exception("Windowing platform doesn't support Vulkan.");
        }

        fpsLastUpdate = DateTime.Now.Ticks;

        window.FramebufferResize += resize;
        window.Update += updateWindow;
        window.Render += render;
    }

    private void updateWindow(double frametime)
    {
        if (DateTime.Now.Ticks - fpsLastUpdate < fpsUpdateInterval)
        {
            return;
        }

        fpsLastUpdate = DateTime.Now.Ticks;
        if (window is IWindow w)
        {
            //w.Title = $"{windowName} | W {window.Size.X}x{window.Size.Y} | FPS {Math.Ceiling(1d / obj)} | ";
            w.Title = $"{windowName} | {mouseLast.Debug} | {1d / frametime,-8: 0,000.0} fps";
        }
    }

    private void resize(Vector2D<int> newsize)
    {
        camera.Resize(0, 0, (uint)newsize.X, (uint)newsize.Y);
        cameraController.Resize(newsize);
        FirstAppGuiResize(0, 0, (uint)newsize.X, (uint)newsize.Y, newsize);
    }


    private void loadGameObjects()
    {
        LveGameObject flatVase = LveGameObject.CreateGameObject();
        flatVase.Model = ModelUtils.LoadModelFromFile(vk, device, "Assets/flat_vase.obj");
        flatVase.Transform.Translation = new Vector3(-.5f, 0.5f, 0.0f);
        flatVase.Transform.Scale = new Vector3(3.0f, 1.5f, 3.0f);
        gameObjects.Add(flatVase.Id, flatVase);

        LveGameObject smoothVase = LveGameObject.CreateGameObject();
        smoothVase.Model = ModelUtils.LoadModelFromFile(vk, device, "Assets/smooth_vase.obj");
        smoothVase.Transform.Translation = new Vector3(.5f, 0.5f, 0.0f);
        smoothVase.Transform.Scale = new Vector3(3.0f, 1.5f, 3.0f);
        gameObjects.Add(smoothVase.Id, smoothVase);

        LveGameObject cube = LveGameObject.CreateGameObject();
        cube.Model = ModelUtils.LoadModelFromFile(vk, device, "Assets/cube.obj");
        cube.Transform.Translation = new Vector3(-1.25f, .751f, -1.25f);
        cube.Transform.Scale = new Vector3(0.25f);
        gameObjects.Add(cube.Id, cube);

        LveGameObject floor = LveGameObject.CreateGameObject();
        floor.Model = ModelUtils.LoadModelFromFile(vk, device, "Assets/quad.obj");
        floor.Transform.Translation = new Vector3(0f, 0.5f, 0f);
        floor.Transform.Scale = new Vector3(3f, 1f, 3f);
        gameObjects.Add(floor.Id, floor);


        LveMeshObject mesh = new(vk, device);
        meshObjects.Add(mesh.Id, mesh);


        Vector4[] lightColors = new Vector4[]
        {
            new(1f, .1f, .1f, 1f), new(.1f, .1f, 1f, 1f), new(.1f, 1f, .1f, 1f), new(1f, 1f, .1f, 1f),
            new(.1f, 1f, 1f, 1f), new(1f, 1f, 1f, 1f)
        };
        for (int i = 0; i < 6; i++)
        {
            LveGameObject pointLight = LveGameObject.MakePointLight(
                0.2f, 0.05f, lightColors[i]
            );
            Matrix4x4 rotateLight = Matrix4x4.CreateRotationY(i * MathF.PI / lightColors.Length * 2f);
            pointLight.Transform.Translation = Vector3.Transform(new Vector3(1.25f, 1.25f, 0f), rotateLight);
            gameObjects.Add(pointLight.Id, pointLight);
        }
    }
}
