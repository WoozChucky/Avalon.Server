namespace Avalon.World.Procedural;

public class InvalidProceduralConfigException : Exception
{
    public InvalidProceduralConfigException(string msg) : base(msg) { }
}

public class ProceduralGenerationFailedException : Exception
{
    public ProceduralGenerationFailedException(string msg) : base(msg) { }
}

public class NavmeshBuildFailedException : Exception
{
    public NavmeshBuildFailedException(string msg) : base(msg) { }
}
