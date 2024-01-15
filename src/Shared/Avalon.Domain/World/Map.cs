using Avalon.Domain.Attributes;

namespace Avalon.Domain.World;

public class Map
{
    [Column("id")]
    public int Id { get; set; }
    
    [Column("map_name")]
    public string Name { get; set; }
    
    [Column("map_description")]
    public string Description { get; set; }
    
    [Column("atlas")]
    public string Atlas { get; set; }
    
    [Column("directory")]
    public string Directory { get; set; }
    
    [Column("instance_type")]
    public MapInstanceType InstanceType { get; set; }
    
    [Column("pvp")]
    public bool PvP { get; set; }
    
    [Column("min_level")]
    public int MinLevel { get; set; }
    
    [Column("max_level")]
    public int MaxLevel { get; set; }
    
    [Column("area_table_id")]
    public int AreaTableId { get; set; }
    
    [Column("loading_screen_id")]
    public int LoadingScreenId { get; set; }
    
    [Column("corpse_x")]
    public float CorpseX { get; set; }
    
    [Column("corpse_y")]
    public float CorpseY { get; set; }
    
    [Column("max_players")]
    public int MaxPlayers { get; set; }
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
