using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common;

namespace Avalon.Domain.World;

public class MapTemplate : IDbEntity<MapTemplateId>
{
    [Column("Id")]
    public MapTemplateId Id { get; set; }
    
    [Column("Name")]
    public string Name { get; set; }
    
    [Column("Description")]
    public string Description { get; set; }
    
    [Column("Atlas")]
    public string Atlas { get; set; }
    
    [Column("Directory")]
    public string Directory { get; set; }
    
    [Column("InstanceType")]
    public MapInstanceType InstanceType { get; set; }
    
    [Column("PvP")]
    public bool PvP { get; set; }
    
    [Column("MinLevel")]
    public int MinLevel { get; set; }
    
    [Column("MaxLevel")]
    public int MaxLevel { get; set; }
    
    [Column("AreaTableId")]
    public int AreaTableId { get; set; }
    
    [Column("LoadingScreenId")]
    public int LoadingScreenId { get; set; }
    
    [Column("CorpseX")]
    public float CorpseX { get; set; }
    
    [Column("CorpseY")]
    public float CorpseY { get; set; }
    
    [Column("MaxPlayers")]
    public int MaxPlayers { get; set; }
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
    Village = 0,
    VillageBuilding = 1,
    OpenWorld = 2,
    WorldInstance = 3,
    DungeonInstance = 4,
    RaidInstance = 5,
    BattlegroundInstance = 6,
    ArenaInstance = 7,
}
