// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Engine;

public unsafe class LveDescriptorPool : IDisposable
{
    private readonly LveDevice device = null!;
    private readonly Vk vk = null!;

    private DescriptorPool descriptorPool;
    private uint maxSets;
    private DescriptorPoolCreateFlags poolFlags; // = DescriptorPoolCreateFlags.None;

    private DescriptorPoolSize[] poolSizes = null!; // = new();

    public LveDescriptorPool(Vk vk, LveDevice device, uint maxSets, DescriptorPoolCreateFlags poolFlags,
        DescriptorPoolSize[] poolSizes)
    {
        this.vk = vk;
        this.device = device;
        this.poolSizes = poolSizes;
        this.maxSets = maxSets;
        this.poolFlags = poolFlags;

        fixed (DescriptorPool* descriptorPoolPtr = &descriptorPool)
        fixed (DescriptorPoolSize* poolSizesPtr = poolSizes)
        {
            DescriptorPoolCreateInfo descriptorPoolInfo = new()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)poolSizes.Length,
                PPoolSizes = poolSizesPtr,
                MaxSets = maxSets,
                Flags = poolFlags
            };

            if (vk.CreateDescriptorPool(device.VkDevice, &descriptorPoolInfo, null, descriptorPoolPtr) !=
                Result.Success)
            {
                throw new ApplicationException("Failed to create descriptor pool");
            }
        }
    }

    public LveDevice LveDevice => device;

    public void Dispose()
    {
        vk.DestroyDescriptorPool(device.VkDevice, descriptorPool, null);
        GC.SuppressFinalize(this);
    }

    public DescriptorPool GetDescriptorPool() => descriptorPool;

    public bool AllocateDescriptorSet(DescriptorSetLayout descriptorSetLayout, ref DescriptorSet descriptorSet)
    {
        DescriptorSetAllocateInfo allocInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            PSetLayouts = &descriptorSetLayout,
            DescriptorSetCount = 1
        };
        Result result = vk.AllocateDescriptorSets(device.VkDevice, allocInfo, out descriptorSet);
        if (result != Result.Success)
        {
            return false;
        }

        return true;
    }

    private void freeDescriptors(ref DescriptorSet[] descriptors) =>
        vk.FreeDescriptorSets(device.VkDevice, descriptorPool, descriptors);

    private void resetPool() => vk.ResetDescriptorPool(device.VkDevice, descriptorPool, 0);


    // helper builder class for chaining calls
    public class Builder
    {
        private readonly LveDevice device = null!;
        private readonly Vk vk = null!;
        private uint maxSets;
        private DescriptorPoolCreateFlags poolFlags;

        private readonly List<DescriptorPoolSize> poolSizes = new();

        public Builder(Vk vk, LveDevice device)
        {
            this.vk = vk;
            this.device = device;
        }


        public Builder AddPoolSize(DescriptorType descriptorType, uint count)
        {
            poolSizes.Add(new DescriptorPoolSize(descriptorType, count));
            return this;
        }

        public Builder SetPoolFlags(DescriptorPoolCreateFlags flags)
        {
            poolFlags = flags;
            return this;
        }

        public Builder SetMaxSets(uint count)
        {
            maxSets = count;
            return this;
        }

        public LveDescriptorPool Build() => new(vk, device, maxSets, poolFlags, poolSizes.ToArray());
    }
}
