// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Engine;

public struct PointLight
{
    private Vector4 position = Vector4.Zero;
    private Vector4 color = Vector4.One;

    public PointLight()
    {
    }

    public PointLight(Vector4 position, Vector4 color)
    {
        this.position = position;
        this.color = color;
    }

    public void SetPosition(Vector3 pos) => position = new Vector4(pos.X, pos.Y, pos.Z, 0f);

    public void SetColor(Vector4 col, float intensity) => color = new Vector4(col.X, col.Y, col.Z, intensity);

    public byte[] AsBytes()
    {
        byte[] bytes = new byte[32];
        position.AsBytes().CopyTo(bytes, 0);
        color.AsBytes().CopyTo(bytes, 16);

        return bytes;
    }

    public static uint SizeOf() => (uint)Unsafe.SizeOf<PointLight>();

    public override string ToString() => $"p:{position}, c:{color}";
}
