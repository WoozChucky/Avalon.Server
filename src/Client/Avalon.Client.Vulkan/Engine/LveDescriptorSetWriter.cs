// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Engine;

public unsafe class LveDescriptorSetWriter
{
    private readonly LveDevice device = null!;
    private readonly LveDescriptorSetLayout setLayout = null!;
    private readonly Vk vk = null!;

    private WriteDescriptorSet[] writes = Array.Empty<WriteDescriptorSet>();

    public LveDescriptorSetWriter(Vk vk, LveDevice device, LveDescriptorSetLayout setLayout)
    {
        this.vk = vk;
        this.device = device;
        this.setLayout = setLayout;
    }

    public LveDescriptorSetWriter WriteBuffer(uint binding, DescriptorBufferInfo bufferInfo)
    {
        if (!setLayout.Bindings.ContainsKey(binding))
        {
            throw new ApplicationException($"Layout does not contain the specified binding at {binding}");
        }

        DescriptorSetLayoutBinding bindingDescription = setLayout.Bindings[binding];

        if (bindingDescription.DescriptorCount > 1)
        {
            throw new ApplicationException("Binding single descriptor info, but binding expects multiple");
        }

        WriteDescriptorSet write = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DescriptorType = bindingDescription.DescriptorType,
            DstBinding = binding,
            PBufferInfo = &bufferInfo,
            DescriptorCount = 1
        };

        int writesLen = writes.Length;
        Array.Resize(ref writes, writesLen + 1);
        writes[writesLen] = write;
        return this;
    }

    public LveDescriptorSetWriter WriteImage(uint binding, DescriptorImageInfo imageInfo)
    {
        if (!setLayout.Bindings.ContainsKey(binding))
        {
            throw new ApplicationException($"Layout does not contain the specified binding at {binding}");
        }

        DescriptorSetLayoutBinding bindingDescription = setLayout.Bindings[binding];

        if (bindingDescription.DescriptorCount > 1)
        {
            throw new ApplicationException("Binding single descriptor info, but binding expects multiple");
        }

        WriteDescriptorSet write = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DescriptorType = bindingDescription.DescriptorType,
            DstBinding = binding,
            PImageInfo = &imageInfo,
            DescriptorCount = 1
        };

        int writesLen = writes.Length;
        Array.Resize(ref writes, writesLen + 1);
        writes[writesLen] = write;
        return this;
    }

    public bool Build(LveDescriptorPool pool, DescriptorSetLayout layout, ref DescriptorSet set)
    {
        bool success = pool.AllocateDescriptorSet(setLayout.GetDescriptorSetLayout(), ref set);
        if (!success)
        {
            return false;
        }

        Overwrite(ref set);
        return true;
    }


    private void Overwrite(ref DescriptorSet set)
    {
        for (int i = 0; i < writes.Length; i++)
        {
            writes[i].DstSet = set;
        }

        fixed (WriteDescriptorSet* writesPtr = writes)
        {
            vk.UpdateDescriptorSets(device.VkDevice, (uint)writes.Length, writesPtr, 0, null);
        }
    }
}
