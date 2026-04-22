using Avalon.World.Public.Enums;

namespace Avalon.Api.Contract;

public sealed class MapTemplateDto
{
    public ushort Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Directory { get; set; } = "";

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

    /// <summary>Maximum number of players allowed per instance.</summary>
    public ushort? MaxPlayers { get; set; }

    public float DefaultSpawnX { get; set; }
    public float DefaultSpawnY { get; set; }
    public float DefaultSpawnZ { get; set; }

    /// <summary>For Normal maps: the Town map ID players are returned to when they log out.</summary>
    public ushort? ReturnMapId { get; set; }
}
