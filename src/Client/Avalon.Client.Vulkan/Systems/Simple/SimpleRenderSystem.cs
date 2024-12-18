// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Engine;

namespace Avalon.Client.Vulkan.Systems.Simple;

public class SimpleRenderSystem : IDisposable
{
    private readonly LveDevice device = null!;
    private readonly Vk vk = null!;

    private LveStdPipeline pipeline = null!;
    private PipelineLayout pipelineLayout;

    public SimpleRenderSystem(Vk vk, LveDevice device, RenderPass renderPass, DescriptorSetLayout globalSetLayout)
    {
        this.vk = vk;
        this.device = device;
        createPipelineLayout(globalSetLayout);
        createPipeline(renderPass);
    }

    public unsafe void Dispose()
    {
        pipeline.Dispose();
        vk.DestroyPipelineLayout(device.VkDevice, pipelineLayout, null);
        GC.SuppressFinalize(this);
    }

    private unsafe void createPipelineLayout(DescriptorSetLayout globalSetLayout)
    {
        DescriptorSetLayout[]? descriptorSetLayouts = new[] {globalSetLayout};
        PushConstantRange pushConstantRange = new()
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = SimplePushConstantData.SizeOf()
        };


        fixed (DescriptorSetLayout* descriptorSetLayoutPtr = descriptorSetLayouts)
        {
            PipelineLayoutCreateInfo pipelineLayoutInfo = new()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = (uint)descriptorSetLayouts.Length,
                PSetLayouts = descriptorSetLayoutPtr,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushConstantRange
            };

            if (vk.CreatePipelineLayout(device.VkDevice, pipelineLayoutInfo, null, out pipelineLayout) !=
                Result.Success)
            {
                throw new Exception("failed to create pipeline layout!");
            }
        }
    }


    private void createPipeline(RenderPass renderPass)
    {
        Debug.Assert(pipelineLayout.Handle != 0, "Cannot create pipeline before pipeline layout");

        PipelineConfigInfo pipelineConfig = new();
        LveStdPipeline.DefaultPipelineConfigInfo(ref pipelineConfig);

        LveStdPipeline.EnableMultiSampling(ref pipelineConfig, device.GetMsaaSamples());

        pipelineConfig.RenderPass = renderPass;
        pipelineConfig.PipelineLayout = pipelineLayout;
        pipeline = new LveStdPipeline(
            vk, device,
            "simpleShader.vert.spv",
            "simpleShader.frag.spv",
            pipelineConfig
        );
        //log.d("app run", " got pipeline");
    }


    public unsafe void Render(FrameInfo frameInfo)
    {
        pipeline.Bind(frameInfo.CommandBuffer);

        vk.CmdBindDescriptorSets(
            frameInfo.CommandBuffer,
            PipelineBindPoint.Graphics,
            pipelineLayout,
            0,
            1,
            frameInfo.GlobalDescriptorSet,
            0,
            null
        );


        foreach ((uint id, LveGameObject? g) in frameInfo.GameObjects)
        {
            if (g.Model is null)
            {
                continue;
            }

            SimplePushConstantData push = new()
            {
                ModelMatrix = g.Transform.Mat4(), NormalMatrix = g.Transform.NormalMatrix()
            };
            vk.CmdPushConstants(
                frameInfo.CommandBuffer,
                pipelineLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0,
                SimplePushConstantData.SizeOf(),
                ref push
            );
            g.Model.Bind(frameInfo.CommandBuffer);
            g.Model.Draw(frameInfo.CommandBuffer);
        }
    }
}

public struct SimplePushConstantData
{
    public Matrix4x4 ModelMatrix;
    public Matrix4x4 NormalMatrix;
    //public Vector4 Color;

    public SimplePushConstantData()
    {
        ModelMatrix = Matrix4x4.Identity;
        NormalMatrix = Matrix4x4.Identity;
    }

    public static uint SizeOf() => (uint)Unsafe.SizeOf<SimplePushConstantData>();
}
