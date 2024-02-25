using Silk.NET.Maths;

namespace Avalon.Client.Native.Graphics;

public struct Vertex
{
    // Calculate the size in bytes for each member
    public static readonly int PositionSize = 3;
    public static readonly int ColorSize = 4;
    public static readonly int TextureCoordSize = 2;

    // Calculate the offset for each member
    public static readonly int PositionOffset = 0;
    public static readonly int ColorOffset = (PositionOffset) + (PositionSize * sizeof(float));
    public static readonly int TextureCoordOffset = ColorOffset + (ColorSize * sizeof(float));

    // Calculate the total size in bytes for the entire struct
    public static readonly int TotalSizeInBytes = (PositionSize + ColorSize + TextureCoordSize) * sizeof(float);
        
    public Vector3D<float> Position;
    public Vector4D<float> Color;
    public Vector2D<float> TextureCoord;
}