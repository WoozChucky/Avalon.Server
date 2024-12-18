// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Engine;

namespace Avalon.Client.Vulkan.Systems.MeshTest;

public class Mesh2Renderer : IDisposable
{
    private readonly LveDevice device = null!;
    private readonly Vk vk = null!;

    private LveMeshPipeline pipeline = null!;
    private PipelineLayout pipelineLayout;

    public Mesh2Renderer(Vk vk, LveDevice device, RenderPass renderPass, DescriptorSetLayout globalSetLayout)
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
        DescriptorSetLayout[] descriptorSetLayouts = new[] {globalSetLayout};
        PushConstantRange pushConstantRange = new()
        {
            StageFlags = ShaderStageFlags.FragmentBit | ShaderStageFlags.MeshBitNV | ShaderStageFlags.TaskBitNV,
            Offset = 0,
            Size = LineSegMeshPushConstantData.SizeOf()
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
        LveMeshPipeline.DefaultPipelineConfigInfo(ref pipelineConfig);
        LveMeshPipeline.EnableAlphaBlending(ref pipelineConfig);
        LveMeshPipeline.EnableMultiSampling(ref pipelineConfig, device.GetMsaaSamples());
        //LveMeshPipeline.SetDrawPoints(ref pipelineConfig);
        pipelineConfig.InputAssemblyInfo.Topology = PrimitiveTopology.TriangleStrip;
        //pipelineConfig.BindingDescriptions = LineSegMeshVertex.GetBindingDescriptions();
        //pipelineConfig.AttributeDescriptions = LineSegMeshVertex.GetAttributeDescriptions();

        //pipelineConfig.BindingDescriptions =

        pipelineConfig.RenderPass = renderPass;
        pipelineConfig.PipelineLayout = pipelineLayout;
        pipeline = new LveMeshPipeline(
            vk,
            device,
            "testMesh.task.spv",
            "testMesh.mesh.spv",
            "testMesh.frag.spv",
            pipelineConfig,
            "LveMesh2Renderer"
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


        foreach ((uint id, LveMeshObject g) in frameInfo.MeshObjects)
        {
            LineSegMeshPushConstantData push = new() {ModelMatrix = g.TransformationMatrix()};
            vk.CmdPushConstants(
                frameInfo.CommandBuffer,
                pipelineLayout,
                ShaderStageFlags.FragmentBit | ShaderStageFlags.MeshBitNV | ShaderStageFlags.TaskBitNV,
                0,
                LineSegMeshPushConstantData.SizeOf(),
                ref push
            );
            //g.Bind(frameInfo.CommandBuffer);
            g.Draw(frameInfo.CommandBuffer);
        }
    }
}

public struct LineSegMeshPushConstantData
{
    public Matrix4x4 ModelMatrix;
    public Matrix4x4 NormalMatrix;
    //public Vector4 Color;

    public LineSegMeshPushConstantData()
    {
        ModelMatrix = Matrix4x4.Identity;
        NormalMatrix = Matrix4x4.Identity;
    }

    public static uint SizeOf() => (uint)Unsafe.SizeOf<LineSegMeshPushConstantData>();
}
