namespace Avalon.World.ChunkLayouts;

public class InvalidProceduralConfigException : Exception
{
    public InvalidProceduralConfigException(string msg) : base(msg) { }
}

public class ProceduralGenerationFailedException : Exception
{
    public ProceduralGenerationFailedException(string msg) : base(msg) { }
}
