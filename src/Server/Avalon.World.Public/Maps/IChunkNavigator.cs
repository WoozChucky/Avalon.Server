using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Maps;

public interface IChunkNavigator
{
    Task LoadAsync(string meshFilename);
    List<Vector3> FindPath(Vector3 start, Vector3 end);
    bool HasVisibility(Vector3 start, Vector3 end);
    
    object? Mesh { get; }
}
