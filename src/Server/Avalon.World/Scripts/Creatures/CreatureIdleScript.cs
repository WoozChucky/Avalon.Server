using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Movement;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;

namespace Avalon.World.Scripts.Creatures;

public class CreatureIdleScript : AiScript
{
    private readonly float _idleTime = 5f;
    private float _timeElapsed = 0f;
    
    public CreatureIdleScript(ICreature creature, IChunk chunk) : base(creature, chunk)
    {
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
