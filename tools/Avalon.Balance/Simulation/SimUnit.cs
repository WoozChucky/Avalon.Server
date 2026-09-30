using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Combat;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.Combat;

namespace Avalon.Balance.Simulation;

/// <summary>One ability a unit holds: its row, its runtime metadata (seconds), its cooldown.</summary>
public sealed class SimAbility(AbilityTemplate template)
{
    public AbilityTemplate Template { get; } = template;

    public AbilityMetadata Metadata { get; } = AbilityMetadataMapper.From(template);

    public uint Id => Template.Id.Value;

    public string Name => Template.Name;

    /// <summary>Seconds; counted down every tick while above 0, as the server's ability containers do.</summary>
    public float CooldownLeft { get; set; }

    public bool Ready => CooldownLeft <= 0f;
}

/// <summary>A cast-time cast waiting to fire.</summary>
public sealed class PendingCast(SimUnit caster, SimAbility ability, float timeLeft, double started)
{
    public SimUnit Caster { get; } = caster;

    public SimAbility Ability { get; } = ability;

    public float TimeLeft { get; set; } = timeLeft;

    public double Started { get; } = started;
}

/// <summary>
/// A fighter. Implements IUnit only so the server's AbilityCost rule can be asked about it; nothing else of the
/// world object is used.
/// </summary>
public abstract class SimUnit : IUnit
{
    public required string Name { get; init; }

    public AttackerCombat Attack { get; protected init; }

    public DefenderCombat Defence { get; protected init; }

    /// <summary>
    /// Percentage points: a character's effective haste (already capped), a creature's raw haste, which
    /// CombatRules.EffectiveHaste caps as the server does. Init-only; public so a test can build a creature past its cap.
    /// </summary>
    public float HastePct { get; init; }

    public List<SimAbility> Abilities { get; } = [];

    public PendingCast? Casting { get; set; }

    public bool IsDead => CurrentHealth == 0;

    public ObjectGuid Guid { get; set; } = new();
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public Vector3 Orientation { get; set; }
    public ushort Level { get; set; }
    public uint Health { get; set; }
    public uint CurrentHealth { get; set; }
    public PowerType PowerType { get; set; }
    public uint? Power { get; set; }
    public uint? CurrentPower { get; set; }
    public MoveState MoveState { get; set; }
    public DateTime LastCastStartTime { get; set; }
    public float BodyRadius => 0.5f;

    public GameEntityFields ConsumeDirtyFields() => GameEntityFields.None;

    public void OnHit(IUnit attacker, uint damage)
    {
    }
}
