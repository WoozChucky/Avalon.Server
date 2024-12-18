// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Vulkan.Engine;

public class GlobalUbo
{
    private readonly Vector4 ambientColor; // 16
    private Vector4 frontVec; // 16
    private int numLights; // 4
    private readonly int padding1; // 4
    private readonly int padding2; // 4
    private readonly int padding3; // 4
    private readonly PointLight[] pointLights = null!; // 10 * 32 * 320
    private Matrix4x4 projection; // 64

    private Matrix4x4 view; // 64
    // total size = 496

    public GlobalUbo()
    {
        projection = Matrix4x4.Identity;
        view = Matrix4x4.Identity;
        frontVec = Vector4.UnitZ;
        ambientColor = new Vector4(1f, 1f, 1f, 0.02f);
        numLights = 0;
        padding1 = 0;
        padding2 = 0;
        padding3 = 0;
        pointLights = new PointLight[10];
    }

    public GlobalUbo(byte[] bytes)
    {
        int offset = 0;
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                projection[row, col] = BitConverter.ToSingle(bytes[offset..(offset + 4)]);
                offset += 4;
            }
        }

        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                view[row, col] = BitConverter.ToSingle(bytes[offset..(offset + 4)]);
                offset += 4;
            }
        }

        for (int col = 0; col < 4; col++)
        {
            frontVec[col] = BitConverter.ToSingle(bytes[offset..(offset + 4)]);
            offset += 4;
        }

        for (int col = 0; col < 4; col++)
        {
            ambientColor[col] = BitConverter.ToSingle(bytes[offset..(offset + 4)]);
            offset += 4;
        }

        numLights = BitConverter.ToInt32(bytes[offset..(offset + 4)]);
        offset += 4;
        padding1 = BitConverter.ToInt32(bytes[offset..(offset + 4)]);
        offset += 4;
        padding2 = BitConverter.ToInt32(bytes[offset..(offset + 4)]);
        offset += 4;
        padding3 = BitConverter.ToInt32(bytes[offset..(offset + 4)]);
        offset += 4;

        for (int pt = 0; pt < 10; pt++)
        {
            Vector4 ptpos = Vector4.Zero;
            for (int col = 0; col < 4; col++)
            {
                ptpos[col] = BitConverter.ToSingle(bytes[offset..(offset + 4)]);
                offset += 4;
            }

            Vector4 ptcol = Vector4.One;
            for (int col = 0; col < 4; col++)
            {
                ptcol[col] = BitConverter.ToSingle(bytes[offset..(offset + 4)]);
                offset += 4;
            }

            pointLights[pt] = new PointLight(ptpos, ptcol);
        }
    }

    public void Update(Matrix4x4 projection, Matrix4x4 view, Vector4 frontVec)
    {
        this.projection = projection;
        this.view = view;
        this.frontVec = frontVec;
    }


    public void SetNumLights(int numLights) => this.numLights = numLights;

    public void SetPointLightTranslation(int lightIndex, Vector3 translation) =>
        pointLights[lightIndex].SetPosition(translation);

    public void SetPointLightColor(int lightIndex, Vector4 color, float intensity) =>
        pointLights[lightIndex].SetColor(color, intensity);

    public byte[] AsBytes()
    {
        uint offset = 0;
        uint fsize = sizeof(float);
        uint vsize = fsize * 4;
        uint msize = vsize * 4;
        byte[] bytes = new byte[SizeOf()];

        projection.AsBytes().CopyTo(bytes, offset);
        offset += msize;
        view.AsBytes().CopyTo(bytes, offset);
        offset += msize;

        frontVec.AsBytes().CopyTo(bytes, offset);
        offset += vsize;
        ambientColor.AsBytes().CopyTo(bytes, offset);
        offset += vsize;

        numLights.AsBytes().CopyTo(bytes, offset);
        offset += fsize;
        padding1.AsBytes().CopyTo(bytes, offset);
        offset += fsize;
        padding2.AsBytes().CopyTo(bytes, offset);
        offset += fsize;
        padding3.AsBytes().CopyTo(bytes, offset);
        offset += fsize;

        byte[]? pbytes = pointLights.AsBytes();
        pbytes.CopyTo(bytes, offset);

        return bytes;
    }

    public static uint SizeOf() => 496; // (uint)Unsafe.SizeOf<GlobalUbo2>();
}
