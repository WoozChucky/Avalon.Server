using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Creatures;

public class CreatureCombatScript : AiScript
{
    public enum CombatState
    {
        None,
        Combat,
        Chase,
        Returning
    }

    // This is the position distance at which the creature will stop chasing the target if no hits were received in the meantime, and if the creature itself didn't hit the target
    private const float MaxChaseDistance = 40.0f;
    private const float AttackRange = 1.5f;
    private const float PathRecalculationThreshold = 1.5f; // Threshold to recalculate the path
    private const float AttackCooldown = 2.25f; // Cooldown between attacks
    private readonly ILogger<CreatureCombatScript> _logger;
    private float _attackCooldownTimer;

    private Queue<Vector3> _currentPath;

    private bool _dead;
    private Vector3 _initialPosition;
    private Vector3 _lastKnownTargetPosition;

    private IUnit? _target;

    public CreatureCombatScript(ILoggerFactory loggerFactory, ICreature creature, IChunk chunk) : base(creature, chunk)
    {
        _logger = loggerFactory.CreateLogger<CreatureCombatScript>();
        _currentPath = new Queue<Vector3>();
        _initialPosition = Vector3.zero;
        CharacterEntity.CharacterDisconnected += OnCharacterDisconnected;
    }

    public override object State { get; set; } = CombatState.None;

    private void OnCharacterDisconnected(ICharacter character)
    {
        if (_target == character && !_dead)
        {
            _target = null;
            State = CombatState.Returning;
            Creature.CurrentHealth = Creature.Health;
            _currentPath = GeneratePath(Creature.Position, _initialPosition);
        }
    }

    protected override bool ShouldRun() => State is CombatState.Combat or CombatState.Chase or CombatState.Returning;

    public override void OnEnteredRange(ICharacter character)
    {
        if (State is CombatState.None)
        {
            _target = character;
            _initialPosition = Creature.Position;
            _lastKnownTargetPosition = character.Position;
            State = CombatState.Combat;
        }
    }

    public override void OnHit(IUnit attacker, uint damage)
    {
        if (State is not CombatState.Returning)
        {
            Creature.CurrentHealth -= damage;
            if (Creature.CurrentHealth <= 0)
            {
                _logger.LogInformation("{Name} has died", Creature.Name);
                Creature.CurrentHealth = 0;
                _dead = true;
                Creature.Died(attacker);
                return;
            }

            _target = attacker;
            if (_initialPosition == Vector3.zero)
            {
                _initialPosition = Creature.Position;
            }

            _lastKnownTargetPosition = attacker.Position;
            State = CombatState.Combat;
            Chunk.BroadcastUnitHit(attacker, Creature, Creature.CurrentHealth, damage);
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (_dead)
        {
            return;
        }

        Vector3 currentPosition = Creature.Position;

        if (State is CombatState.Returning)
        {
            if (Vector3.Distance(currentPosition, _initialPosition) < 0.1f)
            {
                State = CombatState.None;
                _target = null;
                _initialPosition = Vector3.zero;
                _currentPath.Clear();
                Creature.Velocity = Vector3.zero;
                Creature.MoveState = MoveState.Idle;
            }
            else
            {
                Creature.MoveState = MoveState.Running;
                Creature.Speed = Creature.Metadata.SpeedRun;
                FollowPath(deltaTime);
            }

            return;
        }

        if (_target == null)
        {
            return;
        }

        Vector3 targetPosition = _target.Position;

        if (Vector3.Distance(currentPosition, _initialPosition) > MaxChaseDistance)
        {
            _target = null;
            State = CombatState.Returning;
            _currentPath = GeneratePath(currentPosition, _initialPosition);
            Creature.CurrentHealth = Creature.Health;
            return;
        }

        if (_target != null && Vector3.Distance(currentPosition, targetPosition) <= AttackRange)
        {
            Creature.Velocity = Vector3.zero;
            Creature.MoveState = MoveState.Idle;
            Creature.LookAt(targetPosition);
            AttackTarget(deltaTime);
        }
        else
        {
            if (_currentPath.Count == 0 ||
                Vector3.Distance(_lastKnownTargetPosition, targetPosition) > PathRecalculationThreshold)
            {
                _currentPath = GeneratePath(currentPosition, targetPosition);
                _lastKnownTargetPosition = targetPosition;
            }

            Creature.MoveState = MoveState.Running;
            Creature.Speed = Creature.Metadata.SpeedRun;

            FollowPath(deltaTime);
        }
    }

    private void AttackTarget(TimeSpan deltaTime)
    {
        // Logic to attack the target
        if (_attackCooldownTimer <= 0.0f)
        {
            Creature.SendAttackAnimation(null); // TODO: spells for creatures
            _target?.OnHit(Creature, 10);
            _attackCooldownTimer = AttackCooldown;
        }
        else
        {
            _attackCooldownTimer -= (float)deltaTime.TotalSeconds;
        }
    }

    private Queue<Vector3> GeneratePath(Vector3 start, Vector3 end)
    {
        List<Vector3> path = Chunk.Navigator.FindPath(start, end);
        return new Queue<Vector3>(path);
    }

    private void FollowPath(TimeSpan deltaTime)
    {
        if (_currentPath.Count == 0)
        {
            return;
        }

        Vector3 currentPosition = Creature.Position;
        Vector3 nextPosition = _currentPath.Peek();

        if (Vector3.Distance(currentPosition, nextPosition) < 0.1f)
        {
            _currentPath.Dequeue();
            if (_currentPath.Count == 0)
            {
                if (State is CombatState.Chase)
                {
                    State = CombatState.Combat; // Switch back to combat state if path is completed
                }
                else if (State is CombatState.Returning)
                {
                    State = CombatState.None; // Switch back to idle state if return path is completed
                }

                return;
            }

            nextPosition = _currentPath.Peek();
        }

        Vector3 direction = Vector3.Normalize(nextPosition - currentPosition);
        Vector3 movementDelta = direction * Creature.Speed * (float)deltaTime.TotalSeconds;

        Creature.LookAt(nextPosition);
        Creature.Velocity = direction;
        Creature.Position += movementDelta;
    }
}
