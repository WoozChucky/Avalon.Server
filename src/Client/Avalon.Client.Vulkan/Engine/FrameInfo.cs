// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Client.Vulkan.Camera;

namespace Avalon.Client.Vulkan.Engine;

public struct FrameInfo
{
    public int FrameIndex; // { get; set; }
    public float FrameTime; // { get; set; }
    public CommandBuffer CommandBuffer; // { get; init; }
    public ICamera Camera; // { get; init; } = null!;
    public DescriptorSet GlobalDescriptorSet; // { get; init; }
    public Dictionary<uint, LveGameObject> GameObjects;
    public Dictionary<uint, LveMeshObject> MeshObjects;
}
