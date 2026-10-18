using System.Globalization;
using System.Text;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// A solid doodad's box as obj lines, appended to the combined obj the navmesh bake reads after every chunk. A client
/// that bakes its own navmesh writes the same vertices and faces in the same order, or the two meshes differ around the
/// doodad: this is the mirror contract.
/// </summary>
public static class DoodadObstacleGeometry
{
    /// <summary>
    /// Appends <paramref name="box" />: 8 vertices, (MinX, BaseY, MinZ), (MaxX, BaseY, MinZ), (MaxX, BaseY, MaxZ),
    /// (MinX, BaseY, MaxZ), then the same four at TopY; and 10 faces, no bottom: the top (5 6 7) (5 7 8), then the sides
    /// (1 2 6) (1 6 5), (2 3 7) (2 7 6), (3 4 8) (3 8 7), (4 1 5) (4 5 8). Indices are 1-based plus
    /// <paramref name="vOffset" />, the vertices already written; numbers are written with the invariant culture, as a
    /// chunk's transformed vertices are.
    /// </summary>
    /// <returns>The vertices written: 8.</returns>
    public static int Append(StringBuilder sb, in DoodadObstacle box, int vOffset)
    {
        Vertex(sb, box.MinX, box.BaseY, box.MinZ);
        Vertex(sb, box.MaxX, box.BaseY, box.MinZ);
        Vertex(sb, box.MaxX, box.BaseY, box.MaxZ);
        Vertex(sb, box.MinX, box.BaseY, box.MaxZ);
        Vertex(sb, box.MinX, box.TopY, box.MinZ);
        Vertex(sb, box.MaxX, box.TopY, box.MinZ);
        Vertex(sb, box.MaxX, box.TopY, box.MaxZ);
        Vertex(sb, box.MinX, box.TopY, box.MaxZ);

        Face(sb, vOffset, 5, 6, 7);
        Face(sb, vOffset, 5, 7, 8);
        Face(sb, vOffset, 1, 2, 6);
        Face(sb, vOffset, 1, 6, 5);
        Face(sb, vOffset, 2, 3, 7);
        Face(sb, vOffset, 2, 7, 6);
        Face(sb, vOffset, 3, 4, 8);
        Face(sb, vOffset, 3, 8, 7);
        Face(sb, vOffset, 4, 1, 5);
        Face(sb, vOffset, 4, 5, 8);
        return 8;
    }

    private static void Vertex(StringBuilder sb, float x, float y, float z) =>
        sb.Append("v ")
          .Append(x.ToString(CultureInfo.InvariantCulture)).Append(' ')
          .Append(y.ToString(CultureInfo.InvariantCulture)).Append(' ')
          .Append(z.ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void Face(StringBuilder sb, int vOffset, int a, int b, int c) =>
        sb.Append("f ").Append(a + vOffset).Append(' ').Append(b + vOffset).Append(' ').Append(c + vOffset).Append('\n');
}
