namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// Which of a world's databases an API process reads (#794, design section 5.1): the World database (content), the
/// Characters database, or both. Only the strings of the parts a process reads are required under Database:Worlds.
/// </summary>
[Flags]
public enum WorldDatabaseParts
{
    None = 0,
    World = 1,
    Characters = 2,
    Both = World | Characters,
}
