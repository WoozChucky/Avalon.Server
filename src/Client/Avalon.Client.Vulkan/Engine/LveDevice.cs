// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Utils;

namespace Avalon.Client.Vulkan.Engine;

public unsafe class LveDevice : IDisposable
{
    //private List<string> instanceExtensions = new() { ExtDebugUtils.ExtensionName };
    //private List<string> deviceExtensions = new() { KhrSwapchain.ExtensionName };

    private readonly string[] deviceExtensions = new[]
    {
        KhrSwapchain.ExtensionName, KhrSynchronization2.ExtensionName, "VK_EXT_mesh_shader"
        //"VK_KHR_spirv_1_4",
        //"VK_KHR_shader_float_controls",
    };

    private readonly bool enableValidationLayers = true;
    private readonly string[] validationLayers = {"VK_LAYER_KHRONOS_validation"};

    private readonly Vk vk = null!;
    private readonly IView window;

    private CommandPool commandPool;
    private DebugUtilsMessengerEXT debugMessenger;

    private ExtDebugUtils debugUtils = null!;
    private Device device;

    private Queue graphicsQueue;

    private Instance instance;

    private KhrSurface khrSurface = null!;

    private SampleCountFlags msaaSamples = SampleCountFlags.Count1Bit;

    private PhysicalDevice physicalDevice;

    private Queue presentQueue;

    public LveDevice(Vk vk, IView window)
    {
        this.vk = vk;
        this.window = window;
        createInstance();
        setupDebugMessenger();
        createSurface();
        pickPhysicalDevice();
        createLogicalDevice();
        createCommandPool();
    }

    public Device VkDevice => device;
    public Instance Instance => instance;
    public SurfaceKHR Surface { get; private set; }

    public PhysicalDevice VkPhysicalDevice => physicalDevice;
    public string DeviceName { get; private set; } = "unknown";

    public uint GraphicsFamilyIndex { get; private set; }

    public Queue GraphicsQueue => graphicsQueue;
    public Queue PresentQueue => presentQueue;

    public void Dispose()
    {
        vk.DestroyCommandPool(device, commandPool, null);
        vk.DestroyDevice(device, null);
        GC.SuppressFinalize(this);
    }

    public PhysicalDeviceProperties GetProperties()
    {
        vk.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties properties);
        return properties;
    }

    public SampleCountFlags GetMsaaSamples() => msaaSamples;
    public CommandPool GetCommandPool() => commandPool;


    public void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size)
    {
        CommandBuffer commandBuffer = BeginSingleTimeCommands();

        BufferCopy copyRegion = new()
        {
            //SrcOffset = 0,
            //DstOffset = 0,
            Size = size
        };

        vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, copyRegion);

        EndSingleTimeCommands(commandBuffer);
    }


    public void CreateBuffer(
        ulong size,
        BufferUsageFlags usage,
        MemoryPropertyFlags properties,
        ref Buffer buffer,
        ref DeviceMemory bufferMemory
    )
    {
        BufferCreateInfo bufferInfo = new()
        {
            SType = StructureType.BufferCreateInfo, Size = size, Usage = usage, SharingMode = SharingMode.Exclusive
        };

        fixed (Buffer* bufferPtr = &buffer)
        {
            if (vk.CreateBuffer(device, bufferInfo, null, bufferPtr) != Result.Success)
            {
                throw new Exception("failed to create vertex buffer!");
            }
        }

        MemoryRequirements memRequirements = new();
        vk.GetBufferMemoryRequirements(device, buffer, out memRequirements);

        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        fixed (DeviceMemory* bufferMemoryPtr = &bufferMemory)
        {
            if (vk.AllocateMemory(device, allocateInfo, null, bufferMemoryPtr) != Result.Success)
            {
                throw new Exception("failed to allocate vertex buffer memory!");
            }
        }

        vk.BindBufferMemory(device, buffer, bufferMemory, 0);
    }


    private void createInstance()
    {
        if (enableValidationLayers && !CheckValidationLayerSupport())
        {
            throw new Exception("validation layers requested, but not available!");
        }

        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("Hello Triangle"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)Marshal.StringToHGlobalAnsi("No Engine"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version12
        };

        InstanceCreateInfo createInfo = new() {SType = StructureType.InstanceCreateInfo, PApplicationInfo = &appInfo};

        string[]? extensions = GetRequiredExtensions();
        createInfo.EnabledExtensionCount = (uint)extensions.Length;
        createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);
        ;

        if (enableValidationLayers)
        {
            createInfo.EnabledLayerCount = (uint)validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(validationLayers);

            DebugUtilsMessengerCreateInfoEXT debugCreateInfo = new();
            PopulateDebugMessengerCreateInfo(ref debugCreateInfo);
            createInfo.PNext = &debugCreateInfo;
        }
        else
        {
            createInfo.EnabledLayerCount = 0;
            createInfo.PNext = null;
        }

        if (vk.CreateInstance(createInfo, null, out instance) != Result.Success)
        {
            throw new Exception("failed to create instance!");
        }

        Marshal.FreeHGlobal((IntPtr)appInfo.PApplicationName);
        Marshal.FreeHGlobal((IntPtr)appInfo.PEngineName);
        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);

        if (enableValidationLayers)
        {
            SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
        }
    }

    private void createSurface()
    {
        if (!vk.TryGetInstanceExtension(instance, out khrSurface))
        {
            throw new NotSupportedException("KHR_surface extension not found.");
        }

        if (window.VkSurface is null)
        {
            throw new ApplicationException("window.VkSurface is null and shouldn't be!");
        }

        Surface = window.VkSurface.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
    }


    private void pickPhysicalDevice()
    {
        uint devicedCount = 0;
        vk.EnumeratePhysicalDevices(instance, ref devicedCount, null);

        if (devicedCount == 0)
        {
            throw new Exception("failed to find GPUs with Vulkan support!");
        }

        PhysicalDevice[]? devices = new PhysicalDevice[devicedCount];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            vk.EnumeratePhysicalDevices(instance, ref devicedCount, devicesPtr);
        }

        foreach (PhysicalDevice device in devices)
        {
            if (IsDeviceSuitable(device))
            {
                physicalDevice = device;
                msaaSamples = GetMaxUsableSampleCount();
                break;
            }
        }

        if (physicalDevice.Handle == 0)
        {
            throw new Exception("failed to find a suitable GPU!");
        }

        vk.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties properties);
        DeviceName = getStringFromBytePointer(properties.DeviceName, 50).Trim();

        log.d("device", $"using {DeviceName}");
    }


    private static string getStringFromBytePointer(byte* pointer, int length)
    {
        // Create a span from the byte pointer and decode the string
        Span<byte> span = new(pointer, length);
        return Encoding.UTF8.GetString(span);
    }

    private void createLogicalDevice()
    {
        QueueFamilyIndices indices = FindQueueFamilies(physicalDevice);

        uint[]? uniqueQueueFamilies = new[] {indices.GraphicsFamily!.Value, indices.PresentFamily!.Value};
        uniqueQueueFamilies = uniqueQueueFamilies.Distinct().ToArray();

        GraphicsFamilyIndex = indices.GraphicsFamily.Value;

        using GlobalMemory? mem = GlobalMemory.Allocate(uniqueQueueFamilies.Length * sizeof(DeviceQueueCreateInfo));
        DeviceQueueCreateInfo* queueCreateInfos =
            (DeviceQueueCreateInfo*)Unsafe.AsPointer(ref mem.GetPinnableReference());

        float queuePriority = 1.0f;
        for (int i = 0; i < uniqueQueueFamilies.Length; i++)
        {
            queueCreateInfos[i] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueQueueFamilies[i],
                QueueCount = 1
            };


            queueCreateInfos[i].PQueuePriorities = &queuePriority;
        }


        PhysicalDeviceFeatures deviceFeatures = new() {SamplerAnisotropy = true};


        PhysicalDeviceMeshShaderFeaturesNV meshShaderFeaturesExt = new()
        {
            SType = StructureType.PhysicalDeviceMeshShaderFeaturesExt, MeshShader = Vk.True, TaskShader = Vk.True
        };

        // Enable Synchronization 2 to eliminate a validation layer error, thanks gpt4!
        PhysicalDeviceSynchronization2FeaturesKHR sync2Features = new()
        {
            SType = StructureType.PhysicalDeviceSynchronization2FeaturesKhr, Synchronization2 = Vk.True
        };

        //PhysicalDeviceFeatures2 deviceFeatures2 = new()
        //{
        //    SType = StructureType.PhysicalDeviceFeatures2,
        //    //PNext = &sync2Features
        //};

        PhysicalDeviceFeatures2 features2 = new();
        features2.SType = StructureType.PhysicalDeviceFeatures2;
        features2.PNext = &meshShaderFeaturesExt;
        meshShaderFeaturesExt.PNext = &sync2Features;
        //features2.PNext = &sync2Features;
        //sync2Features.PNext = &meshShaderFeaturesExt;
        //features2.PNext = &deviceFeatures;
        //features2.PNext = &sync2Features;
        //features2.PNext = &meshShaderFeaturesExt;

        features2.Features = new PhysicalDeviceFeatures();
        features2.Features.SamplerAnisotropy = Vk.False;

        //deviceFeatures2.PNext = &sync2Features;
        //vk.GetPhysicalDeviceFeatures2(physicalDevice, out deviceFeatures2);
        //PhysicalDeviceFeatures* extensionPtrs = new IntPtr[deviceExtensions.Length];
        //for (int i = 0; i < deviceExtensions.Length; i++)
        //{
        //    extensionPtrs[i] = SilkMarshal.StringToPtr(deviceExtensions[i]);
        //}
        //Span<PhysicalDeviceProperties> dynamic_states = stackalloc PhysicalDeviceProperties[]
        //{
        //    sync2Features,

        //};

        //fixed (PhysicalDeviceFeatures* featuresPtr = deviceExtensions)
        //{

        //}
        DeviceCreateInfo createInfo = new()
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = (uint)uniqueQueueFamilies.Length,
            PQueueCreateInfos = queueCreateInfos,

            //PEnabledFeatures = &features2,
            //PEnabledFeatures = &deviceFeatures,
            PNext = &features2,
            EnabledExtensionCount = (uint)deviceExtensions.Length,
            PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions)
        };

        //createInfo.PNext = &sync2Features;
        //createInfo.PNext = &meshShaderFeaturesExt;

        if (enableValidationLayers)
        {
            createInfo.EnabledLayerCount = (uint)validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(validationLayers);
        }
        else
        {
            createInfo.EnabledLayerCount = 0;
        }

        if (vk.CreateDevice(physicalDevice, in createInfo, null, out device) != Result.Success)
        {
            throw new Exception("failed to create logical device!");
        }

        vk.GetDeviceQueue(device, indices.GraphicsFamily!.Value, 0, out graphicsQueue);
        vk.GetDeviceQueue(device, indices.PresentFamily!.Value, 0, out presentQueue);

        if (enableValidationLayers)
        {
            SilkMarshal.Free((nint)createInfo.PpEnabledLayerNames);
        }

        SilkMarshal.Free((nint)createInfo.PpEnabledExtensionNames);
    }


    private void createCommandPool()
    {
        QueueFamilyIndices queueFamiliyIndicies = FindQueueFamilies(physicalDevice);

        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = queueFamiliyIndicies.GraphicsFamily!.Value,
            // added flag below to eliminate a validation layer error about clearing command buffer before recording
            Flags = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit
        };

        if (vk.CreateCommandPool(device, poolInfo, null, out commandPool) != Result.Success)
        {
            throw new Exception("failed to create command pool!");
        }
    }


    // helpers

    private CommandBuffer BeginSingleTimeCommands()
    {
        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = commandPool,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer = default;
        vk.AllocateCommandBuffers(device, allocateInfo, out commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        vk.BeginCommandBuffer(commandBuffer, beginInfo);

        return commandBuffer;
    }

    private void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &commandBuffer
        };

        vk.QueueSubmit(graphicsQueue, 1, submitInfo, default);
        vk.QueueWaitIdle(graphicsQueue);

        vk.FreeCommandBuffers(device, commandPool, 1, commandBuffer);
    }

    private bool checkValidationLayerSupport()
    {
        uint propCount = 0;
        Result result = vk.EnumerateInstanceLayerProperties(ref propCount, null);
        if (propCount == 0)
        {
            return false;
        }

        bool ret = false;
        using GlobalMemory? mem = GlobalMemory.Allocate((int)propCount * sizeof(LayerProperties));
        LayerProperties* props = (LayerProperties*)Unsafe.AsPointer(ref mem.GetPinnableReference());
        vk.EnumerateInstanceLayerProperties(ref propCount, props);

        for (int i = 0; i < propCount; i++)
        {
            string? layerName = GetString(props[i].LayerName);
            if (layerName == validationLayers[0])
            {
                ret = true;
            }
            //Console.WriteLine($"{i} {layerName}");
        }

        return ret;
    }

    internal static string GetString(byte* stringStart)
    {
        int characters = 0;
        while (stringStart[characters] != 0)
        {
            characters++;
        }

        return Encoding.UTF8.GetString(stringStart, characters);
    }

    private void PopulateDebugMessengerCreateInfo(ref DebugUtilsMessengerCreateInfoEXT createInfo)
    {
        createInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
        createInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                     DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt;
        createInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                                 DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
        createInfo.PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback;
    }


    private void setupDebugMessenger()
    {
        if (!enableValidationLayers)
        {
            return;
        }

        //TryGetInstanceExtension equivilant to method CreateDebugUtilsMessengerEXT from original tutorial.
        if (!vk.TryGetInstanceExtension(instance, out debugUtils))
        {
            return;
        }

        DebugUtilsMessengerCreateInfoEXT createInfo = new();
        PopulateDebugMessengerCreateInfo(ref createInfo);

        if (debugUtils!.CreateDebugUtilsMessenger(instance, in createInfo, null, out debugMessenger) != Result.Success)
        {
            throw new Exception("failed to set up debug messenger!");
        }
    }

    private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity,
        DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        if (messageSeverity == DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt)
        {
            return Vk.False;
        }

        string? msg = Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage);

        Debug.WriteLine($"{messageSeverity} | validation layer: {msg}");

        return Vk.False;
    }

    public SwapChainSupportDetails QuerySwapChainSupport() => QuerySwapChainSupport(physicalDevice);


    private SwapChainSupportDetails QuerySwapChainSupport(PhysicalDevice physicalDevice)
    {
        SwapChainSupportDetails details = new();

        khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, Surface, out details.Capabilities);

        uint formatCount = 0;
        khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, Surface, ref formatCount, null);

        if (formatCount != 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
            {
                khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, Surface, ref formatCount, formatsPtr);
            }
        }
        else
        {
            details.Formats = Array.Empty<SurfaceFormatKHR>();
        }

        uint presentModeCount = 0;
        khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, Surface, ref presentModeCount, null);

        if (presentModeCount != 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* formatsPtr = details.PresentModes)
            {
                khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, Surface, ref presentModeCount,
                    formatsPtr);
            }
        }
        else
        {
            details.PresentModes = Array.Empty<PresentModeKHR>();
        }

        return details;
    }

    private bool IsDeviceSuitable(PhysicalDevice device)
    {
        QueueFamilyIndices indices = FindQueueFamilies(device);

        bool extensionsSupported = CheckDeviceExtensionsSupport(device);

        bool swapChainAdequate = false;
        if (extensionsSupported)
        {
            SwapChainSupportDetails swapChainSupport = QuerySwapChainSupport(device);
            swapChainAdequate = swapChainSupport.Formats.Any() && swapChainSupport.PresentModes.Any();
        }

        PhysicalDeviceFeatures supportedFeatures;
        vk.GetPhysicalDeviceFeatures(device, out supportedFeatures);

        PhysicalDeviceSynchronization2FeaturesKHR sync2Features = new()
        {
            SType = StructureType.PhysicalDeviceSynchronization2FeaturesKhr, Synchronization2 = Vk.True
        };

        PhysicalDeviceFeatures2 deviceFeatures2 = new()
        {
            SType = StructureType.PhysicalDeviceFeatures2, PNext = &sync2Features
        };

        //PhysicalDeviceFeatures2 supportedFeatures2;
        //vk.GetPhysicalDeviceFeatures2(device, out supportedFeatures2);
        vk.GetPhysicalDeviceFeatures2(device, &deviceFeatures2);

        return indices.IsComplete() && extensionsSupported && swapChainAdequate &&
               supportedFeatures.SamplerAnisotropy && sync2Features.Synchronization2;
    }

    private bool CheckDeviceExtensionsSupport(PhysicalDevice device)
    {
        uint extentionsCount = 0;
        vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extentionsCount, null);

        ExtensionProperties[]? availableExtensions = new ExtensionProperties[extentionsCount];
        fixed (ExtensionProperties* availableExtensionsPtr = availableExtensions)
        {
            vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref extentionsCount, availableExtensionsPtr);
        }

        HashSet<string>? availableExtensionNames = availableExtensions
            .Select(extension => Marshal.PtrToStringAnsi((IntPtr)extension.ExtensionName)).ToHashSet();

        return deviceExtensions.All(availableExtensionNames.Contains);
    }


    public QueueFamilyIndices FindQueueFamilies() => FindQueueFamilies(physicalDevice);

    private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
    {
        QueueFamilyIndices indices = new();

        uint queueFamilityCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, null);

        QueueFamilyProperties[]? queueFamilies = new QueueFamilyProperties[queueFamilityCount];
        fixed (QueueFamilyProperties* queueFamiliesPtr = queueFamilies)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(device, ref queueFamilityCount, queueFamiliesPtr);
        }


        uint i = 0;
        foreach (QueueFamilyProperties queueFamily in queueFamilies)
        {
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                indices.GraphicsFamily = i;
            }

            khrSurface.GetPhysicalDeviceSurfaceSupport(device, i, Surface, out Bool32 presentSupport);

            if (presentSupport)
            {
                indices.PresentFamily = i;
            }

            if (indices.IsComplete())
            {
                break;
            }

            i++;
        }

        return indices;
    }


    private string[] GetRequiredExtensions()
    {
        byte** glfwExtensions = window!.VkSurface!.GetRequiredExtensions(out uint glfwExtensionCount);
        string[]? extensions = SilkMarshal.PtrToStringArray((nint)glfwExtensions, (int)glfwExtensionCount);

        if (enableValidationLayers)
        {
            return extensions.Append(ExtDebugUtils.ExtensionName).ToArray();
        }

        return extensions;
    }


    private bool CheckValidationLayerSupport()
    {
        uint layerCount = 0;
        vk.EnumerateInstanceLayerProperties(ref layerCount, null);
        LayerProperties[]? availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* availableLayersPtr = availableLayers)
        {
            vk.EnumerateInstanceLayerProperties(ref layerCount, availableLayersPtr);
        }

        HashSet<string>? availableLayerNames =
            availableLayers.Select(layer => Marshal.PtrToStringAnsi((IntPtr)layer.LayerName)).ToHashSet();

        return validationLayers.All(availableLayerNames.Contains);
    }


    private SampleCountFlags GetMaxUsableSampleCount()
    {
        vk.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties physicalDeviceProperties);

        SampleCountFlags counts = physicalDeviceProperties.Limits.FramebufferColorSampleCounts &
                                  physicalDeviceProperties.Limits.FramebufferDepthSampleCounts;

        return counts switch
        {
            var c when (c & SampleCountFlags.Count64Bit) != 0 => SampleCountFlags.Count64Bit,
            var c when (c & SampleCountFlags.Count32Bit) != 0 => SampleCountFlags.Count32Bit,
            var c when (c & SampleCountFlags.Count16Bit) != 0 => SampleCountFlags.Count16Bit,
            var c when (c & SampleCountFlags.Count8Bit) != 0 => SampleCountFlags.Count8Bit,
            var c when (c & SampleCountFlags.Count4Bit) != 0 => SampleCountFlags.Count4Bit,
            var c when (c & SampleCountFlags.Count2Bit) != 0 => SampleCountFlags.Count2Bit,
            _ => SampleCountFlags.Count1Bit
        };
    }

    private Format FindSupportedFormat(IEnumerable<Format> candidates, ImageTiling tiling, FormatFeatureFlags features)
    {
        foreach (Format format in candidates)
        {
            vk.GetPhysicalDeviceFormatProperties(physicalDevice, format, out FormatProperties props);

            if (tiling == ImageTiling.Linear && (props.LinearTilingFeatures & features) == features)
            {
                return format;
            }

            if (tiling == ImageTiling.Optimal && (props.OptimalTilingFeatures & features) == features)
            {
                return format;
            }
        }

        throw new Exception("failed to find supported format!");
    }

    public Format FindDepthFormat() =>
        FindSupportedFormat(new[] {Format.D32Sfloat, Format.D32SfloatS8Uint, Format.D24UnormS8Uint},
            ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit);

    public uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProperties;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
            {
                return (uint)i;
            }
        }

        throw new Exception("failed to find suitable memory type!");
    }

    public struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    public struct QueueFamilyIndices
    {
        public uint? GraphicsFamily { get; set; }
        public uint? PresentFamily { get; set; }
        public bool IsComplete() => GraphicsFamily.HasValue && PresentFamily.HasValue;
    }
}
