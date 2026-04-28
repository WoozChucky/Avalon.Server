using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Creatures;

public interface ICreatureMetadata
{
    public CreatureTemplateId Id { get; set; }
    public float SpeedWalk { get; set; }
    public float SpeedRun { get; set; }
    public float SpeedSwim { get; set; }
    Vector3 StartPosition { get; set; }

    /// <summary>Experience awarded to the killer when this creature dies.</summary>
    uint Experience { get; set; }

    /// <summary>How long before this creature re-spawns after death.</summary>
    TimeSpan RespawnTimer { get; set; }

    /// <summary>How long before this creature's corpse is removed from the world.</summary>
    TimeSpan BodyRemoveTimer { get; set; }

    /// <summary>Aggro radius. Creatures with 0 fall back to a script-defined default.</summary>
    float DetectionRange { get; set; }
}
