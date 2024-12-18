// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Engine;

public static class TypeExtensions
{
    public static byte[] AsBytes(this int i)
    {
        byte[] bytes = new byte[4];
        BitConverter.GetBytes(i).CopyTo(bytes, 0);
        return bytes;
    }

    public static byte[] AsBytes(this float f)
    {
        byte[] bytes = new byte[4];
        BitConverter.GetBytes(f).CopyTo(bytes, 0);
        return bytes;
    }

    public static byte[] AsBytes(this Vector4 vec)
    {
        uint offset = 0;
        uint fsize = 4;
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(vec.X).CopyTo(bytes, offset);
        BitConverter.GetBytes(vec.Y).CopyTo(bytes, offset += fsize);
        BitConverter.GetBytes(vec.Z).CopyTo(bytes, offset += fsize);
        BitConverter.GetBytes(vec.W).CopyTo(bytes, offset += fsize);

        return bytes;
    }

    public static byte[] AsBytes(this Matrix4x4 mat)
    {
        uint offset = 0;
        uint fsize = 4;
        byte[] bytes = new byte[64];
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                BitConverter.GetBytes(mat[row, col]).CopyTo(bytes, offset);
                offset += fsize;
            }
        }

        return bytes;
    }


    public static byte[] AsBytes(this PointLight[] pts)
    {
        uint offset = 0;
        //uint psize = 32;
        byte[] bytes = new byte[320];
        for (uint i = 0; i < 10; i++)
        {
            pts[i].AsBytes().CopyTo(bytes, offset);
            offset += 32;
        }

        return bytes;
    }
}
