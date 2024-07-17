using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Creatures;

public interface ICreatureMetadata
{
    public CreatureTemplateId Id { get; set; }
    public float SpeedWalk { get; set; }
    public float SpeedRun { get; set; }
    public float SpeedSwim { get; set; }
}
