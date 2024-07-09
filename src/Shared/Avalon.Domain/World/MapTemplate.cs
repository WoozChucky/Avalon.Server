using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common;

namespace Avalon.Domain.World;

public class MapTemplate : IDbEntity<MapTemplateId>
{
    public MapTemplateId Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Directory { get; set; }
    public MapInstanceType InstanceType { get; set; }
    public bool PvP { get; set; }
    public ushort? MinLevel { get; set; }
    public ushort? MaxLevel { get; set; }
    public int AreaTableId { get; set; }
    public int LoadingScreenId { get; set; }
    public float? CorpseX { get; set; }
    public float? CorpseY { get; set; }
    public float? CorpseZ { get; set; }
    public ushort? MaxPlayers { get; set; }
}

public class MapTemplateId : ValueObject<ushort>
{
    public MapTemplateId(ushort value) : base(value) {}
    
    public static implicit operator MapTemplateId(ushort value)
    {
        return new MapTemplateId(value);
    }
}

public enum MapInstanceType
{
    OpenWorld = 0,
    DungeonInstance = 1,
    RaidInstance = 2,
    BattlegroundInstance = 3,
    ArenaInstance = 4,
}
