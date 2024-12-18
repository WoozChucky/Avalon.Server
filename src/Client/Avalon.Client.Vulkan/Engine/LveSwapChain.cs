// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Utils;

namespace Avalon.Client.Vulkan.Engine;

public class LveSwapChain : IDisposable
{
    public static int MAX_FRAMES_IN_FLIGHT = 2;
    private readonly LveDevice device = null!;


    private readonly LveSwapChain? oldSwapChain = null!;

    private readonly Vk vk = null!;
    private readonly Device vkDevice;

    private readonly Extent2D windowExtent;
    private DeviceMemory[] colorImageMemorys = null!;

    private Image[] colorImages = null!;
    private ImageView[] colorImageViews = null!;
    private int currentFrame;
    private DeviceMemory[] depthImageMemorys = null!;

    // save this for later
    //private SampleCountFlags msaaSamples = SampleCountFlags.Count1Bit;

    private Image[] depthImages = null!;
    private ImageView[] depthImageViews = null!;

    private Semaphore[] imageAvailableSemaphores = null!;

    private Fence[] imagesInFlight = null!;
    private Fence[] inFlightFences = null!;

    private KhrSwapchain khrSwapChain = null!;
    private Semaphore[] renderFinishedSemaphores = null!;

    private RenderPass renderPass;
    private SwapchainKHR swapChain;

    private Extent2D swapChainExtent;


    private Framebuffer[] swapChainFramebuffers = null!;

    private Image[] swapChainImages = null!;
    private ImageView[] swapChainImageViews = null!;

    public LveSwapChain(Vk vk, LveDevice device, Extent2D extent, bool useFifo)
    {
        this.vk = vk;
        this.device = device;
        UseFifo = useFifo;
        vkDevice = device.VkDevice;
        windowExtent = extent;
        init();
    }

    public int MaxFramesInFlight => MAX_FRAMES_IN_FLIGHT;
    public SwapchainKHR VkSwapChain => swapChain;
    public Format SwapChainImageFormat { get; private set; }

    public Format SwapChainDepthFormat { get; private set; }

    public bool UseFifo { get; set; }

    public uint Width => swapChainExtent.Width;
    public uint Height => swapChainExtent.Height;

    public unsafe void Dispose()
    {
        foreach (Framebuffer framebuffer in swapChainFramebuffers)
        {
            vk.DestroyFramebuffer(device.VkDevice, framebuffer, null);
        }

        foreach (ImageView imageView in swapChainImageViews)
        {
            vk.DestroyImageView(device.VkDevice, imageView, null);
        }

        Array.Clear(swapChainImageViews);


        for (int i = 0; i < depthImages.Length; i++)
        {
            vk.DestroyImageView(device.VkDevice, depthImageViews[i], null);
            vk.DestroyImage(device.VkDevice, depthImages[i], null);
            vk.FreeMemory(device.VkDevice, depthImageMemorys[i], null);
        }

        for (int i = 0; i < colorImages.Length; i++)
        {
            vk.DestroyImageView(device.VkDevice, colorImageViews[i], null);
            vk.DestroyImage(device.VkDevice, colorImages[i], null);
            vk.FreeMemory(device.VkDevice, colorImageMemorys[i], null);
        }

        vk.DestroyRenderPass(device.VkDevice, renderPass, null);

        // cleanup synchronization objects
        for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
        {
            vk.DestroySemaphore(device.VkDevice, renderFinishedSemaphores[i], null);
            vk.DestroySemaphore(device.VkDevice, imageAvailableSemaphores[i], null);
            vk.DestroyFence(device.VkDevice, inFlightFences[i], null);
        }

        khrSwapChain!.DestroySwapchain(device.VkDevice, swapChain, null);
        swapChain = default;

        GC.SuppressFinalize(this);
    }

    public ImageView[] GetSwapChainImageViews() => swapChainImageViews;
    public Framebuffer GetFrameBufferAt(uint i) => swapChainFramebuffers[i];
    public Framebuffer[] GetFrameBuffers() => swapChainFramebuffers;

    public uint GetFrameBufferCount() => (uint)swapChainFramebuffers.Length;

    public Extent2D GetSwapChainExtent() => swapChainExtent;

    // need the floats below?
    public float GetAspectRatio() => swapChainExtent.Width / (float)swapChainExtent.Height;
    public RenderPass GetRenderPass() => renderPass;

    public uint ImageCount() => (uint)swapChainImageViews.Length;

    //public LveSwapChain(Vk vk, LveDevice device, Extent2D extent, bool useFifo, LveSwapChain previous)
    //{
    //    this.vk = vk;
    //    this.device = device;
    //    UseFifo = useFifo;
    //    vkDevice = device.VkDevice;
    //    windowExtent = extent;
    //    oldSwapChain = previous;
    //    init();

    //    oldSwapChain = null;
    //}

    private void init()
    {
        createSwapChain();
        createImageViews();
        createRenderPass();
        createColorResources();
        createDepthResources();
        createFrameBuffers();
        createSyncObjects();
    }

    //public bool CompareSwapFormats(LveSwapChain swapChainToCompare)
    //{
    //    return swapChainToCompare.SwapChainDepthFormat == swapChainDepthFormat &&
    //           swapChainToCompare.SwapChainImageFormat == swapChainImageFormat;
    //}
    public Result AcquireNextImage(ref uint imageIndex)
    {
        //var fence = inFlightFences[currentFrame];
        //vk.WaitForFences(device.VkDevice, 1, in fence, Vk.True, ulong.MaxValue);
        vk.WaitForFences(device.VkDevice, 1, inFlightFences[currentFrame], true, ulong.MaxValue);

        Result result = khrSwapChain.AcquireNextImage
        (device.VkDevice, swapChain, ulong.MaxValue, imageAvailableSemaphores[currentFrame], default,
            ref imageIndex);

        return result;
    }


    public unsafe Result SubmitCommandBuffers(CommandBuffer commandBuffer, uint imageIndex)
    {
        if (imagesInFlight![imageIndex].Handle != default)
        {
            vk!.WaitForFences(device.VkDevice, 1, imagesInFlight[imageIndex], true, ulong.MaxValue);
        }

        imagesInFlight[imageIndex] = inFlightFences[currentFrame];

        SubmitInfo submitInfo = new() {SType = StructureType.SubmitInfo};

        Semaphore* waitSemaphores = stackalloc[] {imageAvailableSemaphores[currentFrame]};
        PipelineStageFlags* waitStages = stackalloc[] {PipelineStageFlags.ColorAttachmentOutputBit};

        //var buffer = commandBuffers![imageIndex];

        submitInfo = submitInfo with
        {
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        Semaphore* signalSemaphores = stackalloc[] {renderFinishedSemaphores![currentFrame]};
        submitInfo = submitInfo with {SignalSemaphoreCount = 1, PSignalSemaphores = signalSemaphores};

        vk!.ResetFences(device.VkDevice, 1, inFlightFences[currentFrame]);

        if (vk!.QueueSubmit(device.GraphicsQueue, 1, submitInfo, inFlightFences[currentFrame]) != Result.Success)
        {
            throw new Exception("failed to submit draw command buffer!");
        }

        SwapchainKHR* swapChains = stackalloc[] {swapChain};
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = signalSemaphores,
            SwapchainCount = 1,
            PSwapchains = swapChains,
            PImageIndices = &imageIndex
        };

        Result result = khrSwapChain.QueuePresent(device.PresentQueue, presentInfo);

        currentFrame = (currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;

        return result;
    }

    private unsafe void createSwapChain()
    {
        LveDevice.SwapChainSupportDetails swapChainSupport = device.QuerySwapChainSupport();

        SurfaceFormatKHR surfaceFormat = ChooseSwapSurfaceFormat(swapChainSupport.Formats);
        PresentModeKHR presentMode = ChoosePresentMode(swapChainSupport.PresentModes);
        Extent2D extent = ChooseSwapExtent(swapChainSupport.Capabilities);

        uint imageCount = swapChainSupport.Capabilities.MinImageCount + 1;
        if (swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount)
        {
            imageCount = swapChainSupport.Capabilities.MaxImageCount;
        }

        SwapchainCreateInfoKHR creatInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = device.Surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit
        };

        LveDevice.QueueFamilyIndices indices = device.FindQueueFamilies();
        uint* queueFamilyIndices = stackalloc[] {indices.GraphicsFamily!.Value, indices.PresentFamily!.Value};

        if (indices.GraphicsFamily != indices.PresentFamily)
        {
            creatInfo = creatInfo with
            {
                ImageSharingMode = SharingMode.Concurrent,
                QueueFamilyIndexCount = 2,
                PQueueFamilyIndices = queueFamilyIndices
            };
        }
        else
        {
            creatInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        creatInfo = creatInfo with
        {
            PreTransform = swapChainSupport.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true
        };

        if (khrSwapChain is null)
        {
            if (!vk.TryGetDeviceExtension(device.Instance, vkDevice, out khrSwapChain))
            {
                throw new NotSupportedException("VK_KHR_swapchain extension not found.");
            }
        }

        creatInfo.OldSwapchain = oldSwapChain == default ? default : oldSwapChain.VkSwapChain;

        //var res = khrSwapChain.CreateSwapchain(vkDevice, creatInfo, null, out swapChain);
        if (khrSwapChain.CreateSwapchain(vkDevice, creatInfo, null, out swapChain) != Result.Success)
        {
            throw new Exception("failed to create swap chain!");
        }

        khrSwapChain.GetSwapchainImages(vkDevice, swapChain, ref imageCount, null);
        swapChainImages = new Image[imageCount];
        fixed (Image* swapChainImagesPtr = swapChainImages)
        {
            khrSwapChain.GetSwapchainImages(vkDevice, swapChain, ref imageCount, swapChainImagesPtr);
        }

        SwapChainImageFormat = surfaceFormat.Format;
        swapChainExtent = extent;
    }


    private unsafe void createImageViews()
    {
        //Array.Resize(ref swapChainImageViews, swapChainImages.Length);
        swapChainImageViews = new ImageView[swapChainImages.Length];

        for (int i = 0; i < swapChainImages.Length; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = swapChainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = SwapChainImageFormat,
                SubresourceRange =
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            if (vk.CreateImageView(vkDevice, createInfo, null, out swapChainImageViews[i]) != Result.Success)
            {
                throw new Exception("failed to create image view!");
            }
        }
    }


    private unsafe void createRenderPass()
    {
        AttachmentDescription depthAttachment = new()
        {
            Format = device.FindDepthFormat(),
            //Samples = SampleCountFlags.Count1Bit,
            Samples = device.GetMsaaSamples(),
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        AttachmentReference depthAttachmentRef = new()
        {
            Attachment = 1, Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        AttachmentDescription colorAttachment = new()
        {
            Format = SwapChainImageFormat,
            //Samples = SampleCountFlags.Count1Bit,
            Samples = device.GetMsaaSamples(),
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            //FinalLayout = ImageLayout.PresentSrcKhr,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        AttachmentReference colorAttachmentRef = new() {Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal};


        AttachmentDescription colorAttachmentResolve = new()
        {
            Format = SwapChainImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        AttachmentReference colorAttachmentResolveRef = new()
        {
            Attachment = 2, Layout = ImageLayout.AttachmentOptimalKhr
        };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef,
            PDepthStencilAttachment = &depthAttachmentRef,
            PResolveAttachments = &colorAttachmentResolveRef
        };

        SubpassDependency dependency = new()
        {
            DstSubpass = 0,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcSubpass = Vk.SubpassExternal,
            SrcAccessMask = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit
        };

        //MemoryBarrier2 memoryBarrier = new()
        //{
        //    SType = StructureType.MemoryBarrier2,
        //    PNext = default,
        //    SrcStageMask = PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
        //    DstStageMask = PipelineStageFlags2.FragmentShaderBit,
        //    SrcAccessMask = AccessFlags2.DepthStencilAttachmentWriteBit,
        //    DstAccessMask = AccessFlags2.InputAttachmentReadBit
        //};

        //SubpassDependency2 dependency = new()
        //{
        //    SType = StructureType.SubpassDependency2,
        //    PNext = &memoryBarrier,
        //    SrcSubpass = 0,
        //    DstSubpass = 1,
        //    DependencyFlags = DependencyFlags.ByRegionBit
        //};

        AttachmentDescription[] attachments = new[] {colorAttachment, depthAttachment, colorAttachmentResolve};

        fixed (AttachmentDescription* attachmentsPtr = attachments)
        {
            RenderPassCreateInfo renderPassInfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint)attachments.Length,
                PAttachments = attachmentsPtr,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = 1,
                PDependencies = &dependency
            };

            if (vk.CreateRenderPass(vkDevice, renderPassInfo, null, out renderPass) != Result.Success)
            {
                throw new Exception("failed to create render pass!");
            }
        }
    }


    private unsafe void createFrameBuffers()
    {
        //Array.Resize(ref swapChainFramebuffers, swapChainImageViews.Length);
        swapChainFramebuffers = new Framebuffer[swapChainImageViews.Length];


        for (int i = 0; i < swapChainImageViews.Length; i++)
        {
            ImageView[] attachments = new[] {colorImageViews[i], depthImageViews[i], swapChainImageViews[i]};

            fixed (ImageView* attachmentsPtr = attachments)
            {
                FramebufferCreateInfo framebufferInfo = new()
                {
                    SType = StructureType.FramebufferCreateInfo,
                    RenderPass = renderPass,
                    AttachmentCount = (uint)attachments.Length,
                    PAttachments = attachmentsPtr,
                    Width = swapChainExtent.Width,
                    Height = swapChainExtent.Height,
                    Layers = 1
                };

                if (vk!.CreateFramebuffer(device.VkDevice, framebufferInfo, null, out swapChainFramebuffers[i]) !=
                    Result.Success)
                {
                    throw new Exception("failed to create framebuffer!");
                }
            }
        }
    }


    private unsafe void createColorResources()
    {
        Format colorFormat = SwapChainImageFormat;

        uint imageCount = ImageCount();
        colorImages = new Image[imageCount];
        colorImageMemorys = new DeviceMemory[imageCount];
        colorImageViews = new ImageView[imageCount];

        for (int i = 0; i < imageCount; i++)
        {
            ImageCreateInfo imageInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Extent = {Width = swapChainExtent.Width, Height = swapChainExtent.Height, Depth = 1},
                MipLevels = 1,
                ArrayLayers = 1,
                Format = colorFormat,
                Tiling = ImageTiling.Optimal,
                InitialLayout = ImageLayout.Undefined,
                Usage = ImageUsageFlags.TransientAttachmentBit | ImageUsageFlags.ColorAttachmentBit,
                //Samples = SampleCountFlags.Count1Bit,
                Samples = device.GetMsaaSamples(),
                SharingMode = SharingMode.Exclusive,
                Flags = 0
            };

            fixed (Image* imagePtr = &colorImages[i])
            {
                if (vk.CreateImage(vkDevice, imageInfo, null, imagePtr) != Result.Success)
                {
                    throw new Exception("failed to create color image!");
                }
            }

            MemoryRequirements memRequirements;
            vk.GetImageMemoryRequirements(vkDevice, colorImages[i], out memRequirements);

            MemoryAllocateInfo allocInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memRequirements.Size,
                MemoryTypeIndex =
                    device.FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
            };

            fixed (DeviceMemory* imageMemoryPtr = &colorImageMemorys[i])
            {
                if (vk.AllocateMemory(vkDevice, allocInfo, null, imageMemoryPtr) != Result.Success)
                {
                    throw new Exception("failed to allocate color image memory!");
                }
            }

            vk.BindImageMemory(vkDevice, colorImages[i], colorImageMemorys[i], 0);


            // color image view
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = colorImages[i],
                ViewType = ImageViewType.Type2D,
                Format = colorFormat,
                //Components =
                //    {
                //        R = ComponentSwizzle.Identity,
                //        G = ComponentSwizzle.Identity,
                //        B = ComponentSwizzle.Identity,
                //        A = ComponentSwizzle.Identity,
                //    },
                SubresourceRange =
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            if (vk.CreateImageView(vkDevice, createInfo, null, out colorImageViews[i]) != Result.Success)
            {
                throw new Exception("failed to create color image views!");
            }
        }
    }

    private unsafe void createDepthResources()
    {
        Format depthFormat = device.FindDepthFormat();
        SwapChainDepthFormat = depthFormat;

        uint imageCount = ImageCount();
        depthImages = new Image[imageCount];
        depthImageMemorys = new DeviceMemory[imageCount];
        depthImageViews = new ImageView[imageCount];

        for (int i = 0; i < imageCount; i++)
        {
            ImageCreateInfo imageInfo = new()
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Extent = {Width = swapChainExtent.Width, Height = swapChainExtent.Height, Depth = 1},
                MipLevels = 1,
                ArrayLayers = 1,
                Format = depthFormat,
                Tiling = ImageTiling.Optimal,
                InitialLayout = ImageLayout.Undefined,
                Usage = ImageUsageFlags.DepthStencilAttachmentBit,
                //Samples = SampleCountFlags.Count1Bit,
                Samples = device.GetMsaaSamples(),
                SharingMode = SharingMode.Exclusive,
                Flags = 0
            };

            fixed (Image* imagePtr = &depthImages[i])
            {
                if (vk.CreateImage(vkDevice, imageInfo, null, imagePtr) != Result.Success)
                {
                    throw new Exception("failed to create depth image!");
                }
            }

            MemoryRequirements memRequirements;
            vk.GetImageMemoryRequirements(vkDevice, depthImages[i], out memRequirements);

            MemoryAllocateInfo allocInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memRequirements.Size,
                MemoryTypeIndex =
                    device.FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
            };

            fixed (DeviceMemory* imageMemoryPtr = &depthImageMemorys[i])
            {
                if (vk.AllocateMemory(vkDevice, allocInfo, null, imageMemoryPtr) != Result.Success)
                {
                    throw new Exception("failed to allocate depth image memory!");
                }
            }

            vk.BindImageMemory(vkDevice, depthImages[i], depthImageMemorys[i], 0);


            // depth image view
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = depthImages[i],
                ViewType = ImageViewType.Type2D,
                Format = depthFormat,
                //Components =
                //    {
                //        R = ComponentSwizzle.Identity,
                //        G = ComponentSwizzle.Identity,
                //        B = ComponentSwizzle.Identity,
                //        A = ComponentSwizzle.Identity,
                //    },
                SubresourceRange =
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            if (vk.CreateImageView(vkDevice, createInfo, null, out depthImageViews[i]) != Result.Success)
            {
                throw new Exception("failed to create depth image views!");
            }
        }
    }


    private unsafe void CreateImage(uint width, uint height, uint mipLevels, SampleCountFlags numSamples, Format format,
        ImageTiling tiling, ImageUsageFlags usage, MemoryPropertyFlags properties, ref Image image,
        ref DeviceMemory imageMemory)
    {
        ImageCreateInfo imageInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = {Width = width, Height = height, Depth = 1},
            MipLevels = mipLevels,
            ArrayLayers = 1,
            Format = format,
            Tiling = tiling,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            Samples = numSamples,
            SharingMode = SharingMode.Exclusive
        };

        fixed (Image* imagePtr = &image)
        {
            if (vk.CreateImage(vkDevice, imageInfo, null, imagePtr) != Result.Success)
            {
                throw new Exception("failed to create image!");
            }
        }

        MemoryRequirements memRequirements;
        vk.GetImageMemoryRequirements(vkDevice, image, out memRequirements);

        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = device.FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        fixed (DeviceMemory* imageMemoryPtr = &imageMemory)
        {
            if (vk.AllocateMemory(vkDevice, allocInfo, null, imageMemoryPtr) != Result.Success)
            {
                throw new Exception("failed to allocate image memory!");
            }
        }

        vk.BindImageMemory(vkDevice, image, imageMemory, 0);
    }

    private unsafe void createSyncObjects()
    {
        imageAvailableSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
        renderFinishedSemaphores = new Semaphore[MAX_FRAMES_IN_FLIGHT];
        inFlightFences = new Fence[MAX_FRAMES_IN_FLIGHT];
        imagesInFlight = new Fence[swapChainImages!.Length];

        SemaphoreCreateInfo semaphoreInfo = new() {SType = StructureType.SemaphoreCreateInfo};

        FenceCreateInfo fenceInfo = new() {SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit};

        for (int i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
        {
            if (vk.CreateSemaphore(vkDevice, semaphoreInfo, null, out imageAvailableSemaphores[i]) != Result.Success ||
                vk.CreateSemaphore(vkDevice, semaphoreInfo, null, out renderFinishedSemaphores[i]) != Result.Success ||
                vk.CreateFence(vkDevice, fenceInfo, null, out inFlightFences[i]) != Result.Success)
            {
                throw new Exception("failed to create synchronization objects for a frame!");
            }
        }
    }


    private SurfaceFormatKHR ChooseSwapSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> availableFormats)
    {
        foreach (SurfaceFormatKHR availableFormat in availableFormats)
        {
            if (availableFormat.Format == Format.B8G8R8A8Srgb &&
                availableFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return availableFormat;
            }
        }

        return availableFormats[0];
    }

    private PresentModeKHR ChoosePresentMode(IReadOnlyList<PresentModeKHR> availablePresentModes)
    {
        if (UseFifo)
        {
            return PresentModeKHR.FifoKhr;
        }

        foreach (PresentModeKHR availablePresentMode in availablePresentModes)
        {
            if (availablePresentMode == PresentModeKHR.MailboxKhr)
            {
                log.d("swapchain", "got present mode = Mailbox");
                return availablePresentMode;
            }
        }

        log.d("swapchain", "fallback present mode = FifoKhr");
        return PresentModeKHR.FifoKhr;
    }

    private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }

        Extent2D framebufferSize = windowExtent;

        Extent2D actualExtent = new() {Width = framebufferSize.Width, Height = framebufferSize.Height};

        actualExtent.Width = Math.Clamp(actualExtent.Width, capabilities.MinImageExtent.Width,
            capabilities.MaxImageExtent.Width);
        actualExtent.Height = Math.Clamp(actualExtent.Height, capabilities.MinImageExtent.Height,
            capabilities.MaxImageExtent.Height);

        return actualExtent;
    }
}
