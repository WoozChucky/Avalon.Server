using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.World;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Abstractions;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Auras;

public class ArrivalProtectionShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refuse_an_arrival_when_its_template_is_not_loaded(bool invalid)
    {
        var h = new AuraHarness();
        CharacterEntity character = h.Player(1);
        if (invalid)
        {
            AuraTemplate row = AuraTestData.ArrivalProtection();
            row.DurationMs = 0;
            h.Use(row);
        }

        Assert.Equal(AuraApplyResult.Refused, ArrivalProtection.Start(character, h.Auras));

        Assert.Empty(character.Auras.All);
    }

    [Fact]
    public void Use_the_loaded_definition_and_follow_forward_only_reload()
    {
        var h = new AuraHarness();
        CharacterEntity character = TestCharacters.New(time: h.Time);
        character.Health = character.CurrentHealth = 100;
        h.Enter(character);
        AuraTemplate first = AuraTestData.ArrivalProtection();
        first.DurationMs = 12000;
        first.Name = "Loaded protection";
        h.Use(first);

        ArrivalProtection.Start(character, h.Auras);

        ActiveAura held = Assert.Single(character.Auras.All);
        Assert.Same(first, held.Template);
        Assert.Equal(h.Time.GetUtcNow().AddSeconds(12), held.Schedule.ExpiresAt);
        AuraTemplate replacement = AuraTestData.ArrivalProtection();
        replacement.DurationMs = 20000;
        h.Use(replacement);
        h.Auras.Update();
        Assert.Same(first, held.Template);
        ArrivalProtection.Start(character, h.Auras);
        Assert.Same(replacement, Assert.Single(character.Auras.All).Template);
        Assert.Equal(h.Time.GetUtcNow().AddSeconds(20), held.Schedule.ExpiresAt);

        h.Use();
        h.Auras.Update();
        Assert.Empty(character.Auras.All);
        Assert.False(ArrivalProtection.IsProtected(character));
    }

    [Fact]
    public async Task Grant_a_fresh_minute_when_spawning()
    {
        var h = new AuraHarness();
        FakeTimeProvider clock = h.Time;
        CharacterEntity character = TestCharacters.New(time: clock);
        character.Health = character.CurrentHealth = 100;
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            Substitute.For<IScriptHotReloader>(), intervalSeconds: 60);
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        h.Use(AuraTestData.ArrivalProtection());
        IMapInstance instance = Substitute.For<IMapInstance, IAuraHost>();
        ((IAuraHost)instance).Auras.Returns(h.Auras);

        world.SpawnInInstance(connection, instance);

        ActiveAura aura = Assert.Single(character.Auras.All);
        Assert.Equal(11u, aura.Id.Value);
        Assert.Equal(clock.GetUtcNow().AddSeconds(60), aura.Schedule.ExpiresAt);
        clock.Advance(TimeSpan.FromSeconds(20));
        world.SpawnInInstance(connection, instance);
        ActiveAura next = Assert.Single(character.Auras.All);
        Assert.Equal(aura.Key, next.Key);
        Assert.Equal(clock.GetUtcNow().AddSeconds(60), next.Schedule.ExpiresAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void End_on_the_deadline_or_the_players_cancel_without_saving(bool cancel)
    {
        var h = new AuraHarness();
        h.Use(AuraTestData.ArrivalProtection());
        CharacterEntity character = TestCharacters.New(time: h.Time);
        character.Health = character.CurrentHealth = 100;
        h.Enter(character);
        ArrivalProtection.Start(character, h.Auras);
        ActiveAura aura = Assert.Single(character.Auras.All);
        Assert.True(ArrivalProtection.IsProtected(character));
        Assert.Empty(CharacterSaveSnapshot.Take(character).Batch.Auras!.Rows);

        h.Advance(TimeSpan.FromSeconds(59));
        h.Auras.Update();
        Assert.True(ArrivalProtection.IsProtected(character));
        if (cancel)
        {
            Assert.Equal(AuraCancelResult.Ok, h.Auras.Cancel(character, new AuraId(11), aura.Key));
        }
        else
        {
            h.Advance(TimeSpan.FromSeconds(1));
            Assert.False(ArrivalProtection.IsProtected(character)); // exact deadline, before the aura pass
            h.Auras.Update();
        }

        Assert.False(ArrivalProtection.IsProtected(character));
        Assert.Empty(character.Auras.All);
        Assert.Equal(AuraChangeKind.Removed, character.Auras.Changes[^1].Kind);
        Assert.Empty(CharacterSaveSnapshot.Take(character).Batch.Auras!.Rows);
    }

    [Fact]
    public void Reserve_arrival_space_without_displacing_buffs()
    {
        var h = new AuraHarness(maxPerUnit: 1);
        CharacterEntity character = TestCharacters.New(time: h.Time);
        character.Health = character.CurrentHealth = 100;
        h.Enter(character);
        AuraTemplate buff = AuraTestData.Renew();
        h.Use(buff, AuraTestData.ArrivalProtection());
        Assert.Equal(AuraApplyResult.Applied, h.Auras.Apply(null, character, buff, AuraSource.None));
        ArrivalProtection.Start(character, h.Auras);
        Assert.Equal(2, character.Auras.Count);
        Assert.True(ArrivalProtection.IsProtected(character));
        Assert.Equal(AuraApplyResult.Refreshed,
            h.Auras.Apply(character, character, new AuraId(11), AuraSource.None));
        Assert.Equal(AuraApplyResult.Refused,
            h.Auras.Apply(null, character, AuraTestData.Fortified(), AuraSource.None));
        Assert.Equal(AuraCancelResult.Ok, h.Auras.Cancel(character, buff.Id));
        Assert.Equal(AuraApplyResult.Applied, h.Auras.Apply(null, character, buff, AuraSource.None));
        Assert.Equal(2, character.Auras.Count);
    }

    [Fact]
    public void Reserve_only_one_slot_and_cancel_every_independent_arrival_copy_on_movement()
    {
        var h = new AuraHarness(maxPerUnit: 2);
        CharacterEntity character = TestCharacters.New(time: h.Time);
        character.Health = character.CurrentHealth = 100;
        h.Enter(character);
        AuraTemplate arrival = AuraTestData.ArrivalProtection();
        arrival.Stacking = AuraStacking.Independent;
        AuraTemplate ordinary = AuraTestData.Fortified();
        h.Use(arrival, ordinary);
        CharacterEntity second = h.Player(2);
        CharacterEntity third = h.Player(3);
        h.Auras.Apply(character, character, ordinary.Id, AuraSource.None);
        Assert.Equal(AuraApplyResult.Applied, h.Auras.Apply(character, character, arrival.Id, AuraSource.None));
        h.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(AuraApplyResult.Applied, h.Auras.Apply(second, character, arrival.Id, AuraSource.None));
        Assert.Equal(AuraApplyResult.Refused, h.Auras.Apply(third, character, arrival.Id, AuraSource.None));

        AuraTemplate shorter = AuraTestData.ArrivalProtection();
        shorter.Stacking = AuraStacking.Independent;
        shorter.DurationMs = 5000;
        h.Use(shorter, ordinary);
        h.Auras.Apply(second, character, shorter.Id, AuraSource.None);
        h.Advance(TimeSpan.FromSeconds(6));
        Assert.True(ArrivalProtection.IsProtected(character)); // the refreshed copy expired; the first is still live

        Assert.Equal(AuraCancelResult.Ok, h.Auras.Cancel(character, arrival.Id, character.Auras.All[^1].Key));
        Assert.True(ArrivalProtection.IsProtected(character));
        h.Auras.Apply(second, character, arrival.Id, AuraSource.None);

        character.Position = new Vector3(1, 0, 0);

        Assert.False(ArrivalProtection.IsProtected(character));
        Assert.Equal(ordinary.Id, Assert.Single(character.Auras.All).Id);
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("ability")]
    [InlineData("periodic")]
    public void Ignore_hits_and_fights_without_rolls_power_combat_or_reports_until_it_expires(string source)
    {
        var time = new FakeTimeProvider();
        var encounters = new EncounterRegistry(new CombatConfig(), time);
        var random = ScriptedCombatRandom.Plain();
        ICombatOutcomes outcomes = Substitute.For<ICombatOutcomes>();
        var combat = new CombatService(new CombatConfig(), encounters, time: time, random: random, outcomes: outcomes);
        CharacterEntity attacker = Fighter(1, time);
        CharacterEntity target = Fighter(2, time);
        attacker.CurrentPower = 0;
        target.CurrentPower = 0;
        ArrivalProtectionTestData.Apply(target);
        IAbility ability = AbilityTestData.Game(AbilityTestData.Circle(1));
        var tick = new PeriodicHit(attacker, target, new AuraId(901), 10, new AuraSnapshot(10, 0, 1), AuraSource.None);
        double carry = 0.25;

        combat.EnterCombat(new Creature { Guid = new ObjectGuid(ObjectType.Creature, 10) }, target);
        Hit();

        Assert.Equal(100u, target.CurrentHealth);
        Assert.Equal(0u, target.CurrentPower);
        Assert.Equal(0u, attacker.CurrentPower);
        Assert.False(target.IsInCombat);
        Assert.False(attacker.IsInCombat);
        Assert.Empty(encounters.Active);
        Assert.Equal(0, random.DoublesDrawn);
        Assert.Equal(0.25, carry);
        Assert.Empty(outcomes.ReceivedCalls());

        time.Advance(TimeSpan.FromSeconds(60));
        Hit();

        Assert.True(target.CurrentHealth < 100);
        Assert.True(target.IsInCombat);
        Assert.Single(encounters.Active);
        Assert.True(random.DoublesDrawn > 0);
        return;

        void Hit()
        {
            switch (source)
            {
                case "raw": combat.ApplyDamage(attacker, target, 10); break;
                case "ability": combat.ApplyDamage(attacker, target, ability); break;
                case "periodic": combat.ApplyPeriodicDamage(tick, ref carry, lastTick: false); break;
            }
        }
    }

    [Fact]
    public void Keep_creatures_from_detecting_or_engaging_a_protected_character_until_it_expires()
    {
        var clock = new FakeTimeProvider();
        CharacterEntity character = Fighter(1, clock);
        var creature = new Creature { Metadata = Substitute.For<ICreatureMetadata>() };
        ISimulationContext context = Substitute.For<ISimulationContext>();
        context.Characters.Returns(new Dictionary<ObjectGuid, ICharacter> { [character.Guid] = character });
        context.GetNavigatorForPosition(Arg.Any<Vector3>())
            .HasVisibility(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(true);
        var detector = new CreatureRangeDetectorScript(creature, context, 10);
        var combat = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context, clock);
        detector.CharacterDetected += combat.OnEnteredRange;
        ArrivalProtectionTestData.Apply(character);

        detector.Update(TimeSpan.FromSeconds(1));
        combat.OnEnteredRange(character);

        Assert.Equal(CreatureRangeDetectorScript.RangeDetectionState.Searching, detector.State);
        Assert.Equal(CreatureCombatScript.CombatState.None, combat.State);

        clock.Advance(TimeSpan.FromSeconds(60));
        detector.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(CreatureRangeDetectorScript.RangeDetectionState.Detected, detector.State);
        Assert.Equal(CreatureCombatScript.CombatState.Combat, combat.State);
    }

    [Fact]
    public void Skip_a_protected_taunter_and_top_threat_then_release_the_last_protected_target()
    {
        var clock = new FakeTimeProvider();
        CharacterEntity arrival = Fighter(1, clock);
        CharacterEntity fighter = Fighter(2, clock);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 3),
            Metadata = Substitute.For<ICreatureMetadata>(),
        };
        ISimulationContext context = Substitute.For<ISimulationContext>();
        var encounter = new Encounter(new CombatConfig(), clock);
        encounter.AddHostile(creature);
        encounter.AddPlayer(fighter);
        encounter.AddPlayer(arrival);
        encounter.AddThreat(creature, arrival, 100);
        context.CombatService.GetEncounterFor(creature).Returns(encounter);
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context, clock);
        script.OnEnteredRange(arrival);
        creature.TauntedBy = arrival;
        creature.TauntExpiresAt = clock.GetUtcNow().UtcDateTime.AddSeconds(10);
        ArrivalProtectionTestData.Apply(arrival);

        Assert.Same(fighter, script.PickTarget());
        script.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(CreatureCombatScript.CombatState.Combat, script.State);

        ArrivalProtectionTestData.Apply(fighter);
        Assert.Null(script.PickTarget());
        script.Update(TimeSpan.FromMilliseconds(16));
        Assert.Equal(CreatureCombatScript.CombatState.Returning, script.State);
    }

    private static CharacterEntity Fighter(uint id, TimeProvider time)
    {
        CharacterEntity character = TestCharacters.New(id, time: time);
        character.Health = 100;
        character.CurrentHealth = 100;
        character.Power = 100;
        character.PowerType = PowerType.Mana;
        return character;
    }
}
