namespace Avalon.World.ChunkLayouts;

public class NavmeshBuildFailedException : Exception
{
    public NavmeshBuildFailedException(string msg) : base(msg) { }
}
