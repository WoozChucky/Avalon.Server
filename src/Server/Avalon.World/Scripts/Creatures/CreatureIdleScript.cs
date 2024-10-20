using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.State;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;

namespace Avalon.World.Scripts.Creatures;

public class CreatureIdleScript : AiScript
{
    private readonly float _idleTime;
    private float _timeElapsed = 0f;

    public CreatureIdleScript(ICreature creature, IChunk chunk, float idleTime) : base(creature, chunk)
    {
        _idleTime = idleTime;
    }

    public override object State { get; set; }
    protected override bool ShouldRun()
    {
        return State is true;
    }

    public override void Update(TimeSpan deltaTime)
    {
        Creature.MoveState = MoveState.Idle;
        Creature.Velocity = Vector3.zero;

        _timeElapsed += (float)deltaTime.TotalSeconds;

        if (_timeElapsed >= _idleTime)
        {
            State = false;
            _timeElapsed = 0f;
        }
    }
}
