using Avalon.Server.World.UnitTests.Auras;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Combat;

public class ArrivalCombatShould
{
    private readonly FakeTimeProvider _time = new();
    private readonly EncounterRegistry _encounters;
    private readonly ScriptedCombatRandom _random = ScriptedCombatRandom.Plain();
    private readonly ICombatOutcomes _outcomes = Substitute.For<ICombatOutcomes>();
    private readonly CombatService _combat;

    public ArrivalCombatShould()
    {
        _encounters = new EncounterRegistry(new CombatConfig(), _time);
        _combat = new CombatService(new CombatConfig(), _encounters, time: _time, random: _random, outcomes: _outcomes);
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("ability")]
    [InlineData("periodic")]
    public void Ignore_damage_without_rolls_power_combat_or_reports_until_arrival_protection_expires(string source)
    {
        CharacterEntity attacker = Player(1);
        CharacterEntity target = Player(2);
        attacker.CurrentPower = 0;
        target.CurrentPower = 0;
        ArrivalTestData.Apply(target);
        AbilityTemplate row = AbilityTestData.Circle(1);
        row.PowerGainPerHit = 10;
        IAbility ability = AbilityTestData.Game(row);
        double carry = 0.25;

        Hit();

        Assert.Equal(100u, target.CurrentHealth);
        Assert.Equal(0u, target.CurrentPower);
        Assert.Equal(0u, attacker.CurrentPower);
        Assert.False(target.IsInCombat);
        Assert.False(attacker.IsInCombat);
        Assert.Empty(_encounters.Active);
        Assert.Equal(0, _random.DoublesDrawn);
        Assert.Equal(0.25, carry);
        Assert.Empty(_outcomes.ReceivedCalls());

        _time.Advance(TimeSpan.FromSeconds(60));
        Hit();

        Assert.True(target.CurrentHealth < 100);
        Assert.True(target.IsInCombat);
        Assert.Single(_encounters.Active);
        Assert.True(_random.DoublesDrawn > 0);
        return;

        void Hit()
        {
            switch (source)
            {
                case "raw": _combat.ApplyDamage(attacker, target, 10); break;
                case "ability": _combat.ApplyDamage(attacker, target, 10, ability); break;
                case "periodic":
                    _combat.ApplyPeriodicDamage(Tick(attacker, target), ref carry, lastTick: false);
                    break;
            }
        }
    }

    [Fact]
    public void Refuse_range_combat_and_taunts_from_a_protected_arrival()
    {
        CharacterEntity arrival = Player(1);
        CharacterEntity fighter = Player(2);
        Creature creature = Creature();
        ArrivalTestData.Apply(arrival);

        _combat.EnterCombat(creature, arrival);
        Assert.Empty(_encounters.Active);

        _combat.EnterCombat(creature, fighter);
        _combat.ApplyTaunt(arrival, creature, 5000);

        IEncounter encounter = Assert.Single(_encounters.Active);
        Assert.DoesNotContain(arrival, encounter.Players);
        Assert.False(encounter.GetThreatList(creature).ContainsKey(arrival));
        Assert.Null(creature.TauntedBy);
    }

    [Fact]
    public void Heal_without_pulling_a_protected_healer_into_an_existing_fight()
    {
        CharacterEntity healer = Player(1);
        CharacterEntity fighter = Player(2);
        Creature creature = Creature();
        _combat.EnterCombat(creature, fighter);
        fighter.CurrentHealth = 50;
        ArrivalTestData.Apply(healer);

        _combat.ApplyHeal(healer, fighter, 10, AbilityTestData.Game(AbilityTestData.HealCircle(1)));

        Assert.Equal(60u, fighter.CurrentHealth);
        IEncounter encounter = Assert.Single(_encounters.Active);
        Assert.DoesNotContain(healer, encounter.Players);
        Assert.False(encounter.GetThreatList(creature).ContainsKey(healer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Let_damage_land_without_aggroing_its_protected_caster(bool periodic)
    {
        CharacterEntity caster = Player(1);
        Creature creature = Creature();
        ISimulationContext context = Substitute.For<ISimulationContext>();
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context, _time);
        creature.Script = script;
        ArrivalTestData.Apply(caster);

        if (periodic)
            Assert.Equal(10u, _combat.ApplyPeriodicDamage(Tick(caster, creature)));
        else
            _combat.ApplyDamage(caster, creature, 10, AbilityTestData.Game(AbilityTestData.Circle(1)));

        Assert.Equal(90u, creature.CurrentHealth);
        Assert.Empty(_encounters.Active);
        Assert.False(caster.IsInCombat);
        Assert.Equal(CreatureCombatScript.CombatState.None, script.State);
    }

    private CharacterEntity Player(uint id)
    {
        CharacterEntity player = TestCharacters.New(id, time: _time);
        player.Health = 100;
        player.CurrentHealth = 100;
        player.Power = 100;
        player.PowerType = PowerType.Fury;
        return player;
    }

    private static Creature Creature() => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, 10),
        Health = 100,
        CurrentHealth = 100,
        Metadata = Substitute.For<ICreatureMetadata>(),
    };

    private static PeriodicHit Tick(CharacterEntity caster, Avalon.World.Public.Units.IUnit target) =>
        new(caster, target, new AuraId(901), 10, new AuraSnapshot(10, 0, 1),
            AuraSource.None with { PowerGainPerHit = 10 });
}
