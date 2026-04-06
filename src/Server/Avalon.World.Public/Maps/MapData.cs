using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Maps;

[Serializable]
public class MapRegion
{
    public List<CreatureInfo> Creatures; // Creatures
    public string GeometryFile;
    public string MeshFile;
    public string Name;
    public Vector3 Position;
    public Vector3 Size;
    public List<TreeInfo> Trees; // Possibly collidable objects
}

[Serializable]
public class TreeInfo
{
    public Vector3 Position;
    public int PrototypeIndex;
    public Vector3 Size;
}

[Serializable]
public class CreatureInfo
{
    public Vector3 Position;
    public ulong PrototypeIndex;
}

[Serializable]
public class NavMeshInfo
{
    public int[] Areas;
    public int[] Indices;
    public Vector3[] Vertices;
}
