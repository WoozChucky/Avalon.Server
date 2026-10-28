using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// A real hit query over units the test places, records of who was hit or healed, in what order and at what share of
/// the amount (#1181 Part D: a second hit's share is kept with it), and a navigator that lets every ray through unless
/// a test says otherwise. Its combat service is a substitute that hits and heals never reach.
/// </summary>
internal sealed class TestArena : IAbilityArena
{
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = new();
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = new();
    private uint _nextCreature = 1000;

    public TestArena(MapType mapType = MapType.Normal)
    {
        MapType = mapType;
        Hits = new UnitHitQuery(_characters, _creatures);
        Navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => ci.ArgAt<Vector3>(1));
    }

    public MapType MapType { get; }
    public IHitQuery Hits { get; }
    public int HitRewindTicks { get; set; }
    public IUnit? HitRewindExempt { get; set; }
    public AbilityFootprint? DodgeFootprint { get; set; }

    /// <summary>Never holds a hit: every hit lands as it is made.</summary>
    public bool TryDeferHit(IUnit caster, IAbility ability, IUnit target) => false;
    public ICombatService CombatService { get; } = Substitute.For<ICombatService>();
    public IMapNavigator Navigator { get; } = Substitute.For<IMapNavigator>();
    public List<(Vector3 Origin, Vector3? Direction, Vector3? Centre)> Fired { get; } = [];

    public IMapNavigator GetNavigatorForPosition(Vector3 position) => Navigator;

    /// <summary>How every hostile hit is answered; Landed unless a test says otherwise.</summary>
    public HitOutcome NextOutcome { get; set; } = HitOutcome.Landed;

    /// <summary>Every aura application asked for, in order.</summary>
    public List<(IUnit Caster, IUnit Target, IAbility Ability)> AurasApplied { get; } = [];

    /// <summary>Every hostile hit's shaping, in order (#1181 Part D).</summary>
    public List<HitShaping> Shapings { get; } = [];

    /// <summary>Every hit of an ability whose Effects has Damage, in order, with its shaping (#1181 Part D).</summary>
    public List<(IUnit Caster, IUnit Target, HitShaping Shaping)> DamageDealt { get; } = [];

    /// <summary>Every heal, in order, with its share of the amount in percent (#1181 Part D).</summary>
    public List<(IUnit Target, float SharePct)> HealsDone { get; } = [];

    /// <summary>
    /// Records the hit's shaping and, for an ability whose Effects has Damage, the damage with it, and answers
    /// <see cref="NextOutcome" />.
    /// </summary>
    public HitOutcome HitForAbility(IUnit caster, IUnit target, IAbility ability, in HitShaping shaping)
    {
        Shapings.Add(shaping);
        if (AbilityRules.HasDirectEffect(ability.Metadata.Effects, ability.Metadata.Affects))
            DamageDealt.Add((caster, target, shaping));
        return NextOutcome;
    }

    public void HealForAbility(IUnit caster, IUnit target, IAbility ability, float amountPct) =>
        HealsDone.Add((target, amountPct));

    public void ApplyAbilityAura(IUnit caster, IUnit target, IAbility ability) => AurasApplied.Add((caster, target, ability));

    /// <summary>Every fired broadcast's whole footprint and the cast id in flight then, in order (#648).</summary>
    public List<(AbilityFootprint Footprint, uint CastId)> FiredFootprints { get; } = [];

    public void BroadcastAbilityFired(IUnit caster, IAbility ability, AbilityFootprint footprint)
    {
        Fired.Add((footprint.Origin, footprint.Direction, footprint.Centre));
        FiredFootprints.Add((footprint, _castInFlight));
    }

    private uint _castInFlight;

    public uint CastInFlight { get => _castInFlight; set => _castInFlight = value; }

    public AbilityFootprint? FootprintInFlight { get; set; }

    /// <summary>The view rounds the instance has completed, moved only by the test (#1181 Part D).</summary>
    public uint ViewRound { get; set; }

    /// <summary>Every projectile impact broadcast, in order (#479).</summary>
    public List<(ProjectileImpactReason Reason, Vector3 Position, IUnit? Target, uint CastId)> Impacts { get; } = [];

    public void BroadcastProjectileImpact(ObjectGuid projectile, IUnit caster, IAbility ability, uint castId,
        Vector3 position, ProjectileImpactReason reason, IUnit? target) =>
        Impacts.Add((reason, position, target, castId));

    /// <summary>Every start broadcast, in order, with its cast id and footprint (#648).</summary>
    public List<(IUnit Caster, IAbility Ability, uint CastId, AbilityFootprint? Footprint)> Started { get; } = [];

    public void BroadcastUnitStartCast(IUnit caster, IAbility ability, uint castId, AbilityFootprint? footprint) =>
        Started.Add((caster, ability, castId, footprint));

    /// <summary>Every finish-cast broadcast, in order.</summary>
    public List<(IUnit Caster, IAbility Ability)> Finished { get; } = [];

    /// <summary>Every interrupt broadcast, in order.</summary>
    public List<(IUnit Caster, IAbility Ability)> Interrupted { get; } = [];

    /// <summary>The cast id of every finish-cast broadcast, in order (#648).</summary>
    public List<uint> FinishedIds { get; } = [];

    /// <summary>The cast id of every interrupt broadcast, in order (#648).</summary>
    public List<uint> InterruptedIds { get; } = [];

    public void BroadcastFinishCast(IUnit caster, IAbility ability, uint castId)
    {
        Finished.Add((caster, ability));
        FinishedIds.Add(castId);
    }

    /// <summary>When set, an interrupt broadcast for this unit is recorded and then throws, as a failing send would.</summary>
    public IUnit? InterruptThrowsFor { get; set; }

    public void BroadcastInterruptedCast(IUnit caster, IAbility ability, uint castId)
    {
        Interrupted.Add((caster, ability));
        InterruptedIds.Add(castId);
        if (ReferenceEquals(caster, InterruptThrowsFor))
            throw new InvalidOperationException("The send failed.");
    }

    /// <summary>The abilities whose interrupt was broadcast for <paramref name="caster" />, in order.</summary>
    public List<IAbility> InterruptsOf(IUnit caster) =>
        Interrupted.Where(i => ReferenceEquals(i.Caster, caster)).Select(i => i.Ability).ToList();

    public CharacterEntity Player(uint id, float x, float z, bool pvp = false)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Data!.PvpEnabled = pvp;
        character.Health = 100;
        character.CurrentHealth = 50;
        character.Position = new Vector3(x, 0f, z);
        _characters[character.Guid] = character;
        return character;
    }

    public ICreature Creature(float x, float z, float body = 0.5f, bool invulnerable = false, uint health = 10)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, _nextCreature++));
        creature.Position.Returns(new Vector3(x, 0f, z));
        creature.BodyRadius.Returns(body);
        creature.CurrentHealth.Returns(health);
        creature.Invulnerable.Returns(invulnerable);
        _creatures[creature.Guid] = creature;
        return creature;
    }

    /// <summary>Every unit damaged, in order, once for each hit (a second hit's unit twice).</summary>
    public List<IUnit> Damaged() => [.. DamageDealt.Select(d => d.Target)];

    /// <summary>Every unit healed, in order, once for each heal.</summary>
    public List<IUnit> Healed() => [.. HealsDone.Select(h => h.Target)];
}
