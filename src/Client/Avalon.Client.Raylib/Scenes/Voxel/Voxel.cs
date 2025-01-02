// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Scenes.Voxel;

public class Voxel
{
    public bool IsActive; // Whether the voxel is visible
    public VoxelType Type; // Define types like dirt, stone, etc.
    public Color Color; // The color of the voxel
}

public enum VoxelType
{
    Air,
    Dirt,
    Grass,
    Stone,
    Water
}
