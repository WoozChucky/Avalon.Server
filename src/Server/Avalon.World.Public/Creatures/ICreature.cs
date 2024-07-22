using Avalon.Common.Mathematics;
using Avalon.World.Public.Scripts;

namespace Avalon.World.Public.Creatures;

public delegate void CreatureKilledDelegate(ICreature creature, IUnit killer);

public interface ICreature : IUnit
{
    ICreatureMetadata Metadata { get; set; }
    string Name { get; set; }
    float Speed { get; set; }
    string ScriptName { get; set; }
    AiScript? Script { get; set; }

    void LookAt(Vector3 target);
    bool IsLookingAt(Vector3 target, float threshold = 0.1f);
    void Died(IUnit killer);
}
