using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.Balance.Core;

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
    /// <summary>The default regeneration settings, as the server's appsettings carry them: mana rates and the ward's recharge.</summary>
    public static readonly RegenConfiguration Regen = new();

    // Its ward (#1178, B-R6), in front of its health, over the world's WardPool: filled at create, its share kept when a
    // stats refresh or an aura fold moves its maximum, emptied at death. Advanced only when a hit meets it, as the
    // recharge is a function of the last damage alone.
    private WardPool _ward;

    public required string Name { get; init; }

    /// <summary>What its ward holds now.</summary>
    public uint Ward => _ward.Current;

    /// <summary>What a full ward holds.</summary>
    public uint MaxWard => _ward.Max;

    /// <summary>Whether its ward takes every hit and tick, blight and bleeds included, and recharges after twice the delay (Unbroken).</summary>
    public virtual bool WardTakesAll => false;

    /// <summary>Fills the ward at <paramref name="max" />: a character entering the world, a creature's spawn.</summary>
    public void ResetWard(uint max) => _ward.Reset(max);

    /// <summary>A new maximum from a stats refresh or an aura fold: the ward keeps its share (WardPool.SetMax).</summary>
    public void SetWardMax(uint max, DateTime now) => _ward.SetMax(max, now);

    /// <summary>
    /// The ward takes what it can of <paramref name="absorbable" />, after recharging to <paramref name="now" />, and pauses
    /// its recharge, as CharacterEntity.AbsorbWithWard and Creature.AbsorbWithWard; answers what it took. Nothing when the
    /// unit is dead, has no ward or the hit lets the ward absorb nothing.
    /// </summary>
    public uint AbsorbWithWard(uint absorbable, DateTime now)
    {
        if (IsDead || _ward.Max == 0 || absorbable == 0)
            return 0;

        _ward.Advance(now, Regen, slowRecharge: WardTakesAll);
        return _ward.Absorb(absorbable, now);
    }

    /// <summary>Damage taken, whether the ward met it or not: the recharge pauses from <paramref name="now" />.</summary>
    public void WardDamaged(DateTime now)
    {
        if (IsDead || _ward.Max == 0)
            return;

        _ward.Advance(now, Regen, slowRecharge: WardTakesAll);
        _ward.Damaged(now);
    }

    /// <summary>Death: the ward is empty, its clock starting from <paramref name="now" />.</summary>
    public void EmptyWard(DateTime now) => _ward.Empty(now);

    /// <summary>What it attacks with, its auras folded in (set again whenever an aura with modifiers changes).</summary>
    public AttackerCombat Attack { get; protected set; }

    /// <summary>What it defends with, its auras folded in.</summary>
    public DefenderCombat Defence { get; protected set; }

    public List<SimAbility> Abilities { get; } = [];

    /// <summary>The auras this unit holds, in the order applied, as the server's UnitAuras.</summary>
    public List<SimAura> Auras { get; } = [];

    /// <summary>What its auras add to its stats, as UnitAuras.StatTotals.</summary>
    public AuraStatTotals AuraTotals() =>
        Auras.Count == 0
            ? AuraStatTotals.Empty
            : AuraStatTotals.Of(Auras, static a => (a.Template.Modifiers, a.Stacks));

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
