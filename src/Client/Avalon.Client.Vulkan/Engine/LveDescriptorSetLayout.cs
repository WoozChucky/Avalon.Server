// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Engine;

public unsafe class LveDescriptorSetLayout : IDisposable
{
    private readonly LveDevice device = null!;
    private readonly Vk vk = null!;

    private readonly DescriptorSetLayout descriptorSetLayout;

    public LveDescriptorSetLayout(Vk vk, LveDevice device, Dictionary<uint, DescriptorSetLayoutBinding> bindings)
    {
        this.vk = vk;
        this.device = device;
        this.Bindings = bindings;

        fixed (DescriptorSetLayoutBinding* setLayoutPtr = this.Bindings.Values.ToArray())
        {
            DescriptorSetLayoutCreateInfo descriptorSetLayoutInfo = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)this.Bindings.Count,
                PBindings = setLayoutPtr
            };

            if (vk.CreateDescriptorSetLayout(device.VkDevice, &descriptorSetLayoutInfo, null,
                    out descriptorSetLayout) != Result.Success)
            {
                throw new ApplicationException("Failed to create descriptor set layout");
            }
        }
    }

    public Dictionary<uint, DescriptorSetLayoutBinding> Bindings { get; } = null!;

    public void Dispose()
    {
        vk.DestroyDescriptorSetLayout(device.VkDevice, descriptorSetLayout, null);
        GC.SuppressFinalize(this);
    }

    public DescriptorSetLayout GetDescriptorSetLayout() => descriptorSetLayout;


    // builder class...
    public class Builder
    {
        private readonly LveDevice device = null!;
        private readonly Vk vk = null!;

        private readonly Dictionary<uint, DescriptorSetLayoutBinding> bindings = new();

        public Builder(Vk vk, LveDevice device)
        {
            this.vk = vk;
            this.device = device;
        }

        public Builder AddBinding(uint binding, DescriptorType descriptorType, ShaderStageFlags stageFlags,
            uint count = 1)
        {
            //Debug.Assert(bindings.Count(binding) == 0 && "Binding already in use");
            if (bindings.ContainsKey(binding))
            {
                throw new ApplicationException($"Binding {binding} is already in use, can't add");
            }

            DescriptorSetLayoutBinding layoutBinding = new()
            {
                Binding = binding, DescriptorType = descriptorType, DescriptorCount = count, StageFlags = stageFlags
            };
            bindings[binding] = layoutBinding;
            return this;
        }

        // might use for textures later?
        public Builder AddBinding(uint binding, DescriptorType descriptorType, ShaderStageFlags stageFlags,
            Sampler sampler, uint count = 1)
        {
            //Debug.Assert(bindings.Count(binding) == 0 && "Binding already in use");
            if (bindings.ContainsKey(binding))
            {
                throw new ApplicationException($"Binding {binding} is already in use, can't add");
            }

            DescriptorSetLayoutBinding layoutBinding = new()
            {
                Binding = binding,
                DescriptorType = descriptorType,
                DescriptorCount = count,
                StageFlags = stageFlags,
                PImmutableSamplers = (Sampler*)Unsafe.AsPointer(ref sampler)
            };
            bindings[binding] = layoutBinding;
            return this;
        }

        public LveDescriptorSetLayout Build() => new(vk, device, bindings);
    }
}
