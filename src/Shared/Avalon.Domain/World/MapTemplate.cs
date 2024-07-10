using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

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
