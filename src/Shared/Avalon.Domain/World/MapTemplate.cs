using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class MapTemplate : IDbEntity<MapTemplateId>
{
    public MapTemplateId Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Directory { get; set; }

    /// <summary>Whether this map is a shared Town hub or a private Normal instanced map.</summary>
    public MapType MapType { get; set; }

    public bool PvP { get; set; }
    public ushort? MinLevel { get; set; }
    public ushort? MaxLevel { get; set; }
    public int AreaTableId { get; set; }
    public int LoadingScreenId { get; set; }
    public float? CorpseX { get; set; }
    public float? CorpseY { get; set; }
    public float? CorpseZ { get; set; }

    /// <summary>
    /// Maximum number of players allowed per instance.
    /// For Town maps: a new instance is created when all existing ones reach this cap.
    /// </summary>
    public ushort? MaxPlayers { get; set; }

    /// <summary>World-space X coordinate where players spawn when entering this map.</summary>
    public float DefaultSpawnX { get; set; }

    /// <summary>World-space Y coordinate where players spawn when entering this map.</summary>
    public float DefaultSpawnY { get; set; }

    /// <summary>World-space Z coordinate where players spawn when entering this map.</summary>
    public float DefaultSpawnZ { get; set; }

    /// <summary>
    /// For Normal maps: the Town map ID players respawn to on login / corpse release.
    /// Null for Town maps.
    /// </summary>
    public ushort? LogoutMapId { get; set; }
}
