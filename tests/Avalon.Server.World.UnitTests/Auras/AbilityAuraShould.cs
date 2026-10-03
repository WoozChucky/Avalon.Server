using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>
/// Abilities through the real cast handler, cast system and instance: the direct amount when Effects has it, then the
/// aura; a dodged hit applies none; an aura-only cast is never dodged; hostility decides who receives it.
/// </summary>
public class AbilityAuraShould
{
    private static AbilityTemplate Rend()
    {
        AbilityTemplate rend = AbilityTestData.Cone(203, reach: 2.5f, arc: 90f);
        rend.AuraId = new AuraId(901);
        return rend;
    }

    private static AbilityTemplate Ignite()
    {
        AbilityTemplate ignite = AbilityTestData.AimedCircle(213, reach: 18f, radius: 3f);
        ignite.Effects = SpellEffect.Debuff;
        ignite.EffectValue = 0;
        ignite.AuraId = new AuraId(902);
        return ignite;
    }

    private static AbilityTemplate Renew()
    {
        AbilityTemplate renew = AbilityTestData.HealCircle(233);
        renew.Effects = SpellEffect.Buff;
        renew.EffectValue = 0;
        renew.AuraId = new AuraId(904);
        return renew;
    }

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    private async Task<(MapInstance Instance, CastAbilityHandler Handler)> InstanceAsync(ICombatRandom? random = null)
    {
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            abilities: () => [Rend(), Ignite(), Renew()],
            auras: () => [AuraTestData.Bleed(), AuraTestData.Burn(), AuraTestData.Renew()]));
        MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, world: NewWorld(data),
            random: random ?? ScriptedCombatRandom.Plain(), time: _clock);
        return (instance, handler);
    }

    private static MapInstanceClient Caster(MapInstance instance, uint id, AbilityTemplate ability)
    {
        MapInstanceClient client = Join(instance, Inventory.TestCharacters.New(id));
        client.Character.Health = 500;
        client.Character.CurrentHealth = 400;
        client.Character.Orientation = new Vector3(0f, 0f, 0f);
        client.Character.Spells.Load([AbilityTestData.Game(ability)]);
        return client;
    }

    private static Creature Boar(MapInstance instance, uint id, uint health = 100, float dodge = 0f)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id), Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Vector3(0f, 0f, 2f), Level = 1, Health = health, CurrentHealth = health, DodgePct = dodge,
        };
        creature.Script = new CombatResolutionShould.CountingWoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    private static readonly Vector3Dto AtTheBoar = new() { X = 0f, Y = 0f, Z = 2f };

    [Fact]
    public async Task Hit_then_apply_the_abilitys_aura()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient warrior = Caster(instance, 912_101, Rend());
        Creature boar = Boar(instance, 912_901);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });

        Assert.Equal(90u, boar.CurrentHealth);   // the cone's own 10
        ActiveAuraIs(boar, 901, warrior.Character.Guid);
    }

    [Fact]
    public async Task Apply_no_aura_from_a_dodged_hit()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync(new ScriptedCombatRandom(0.0));
        MapInstanceClient warrior = Caster(instance, 912_102, Rend());
        Creature boar = Boar(instance, 912_902, dodge: 30f);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });

        Assert.Equal(100u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
    }

    /// <summary>No roll is scripted: the random throws if anything is drawn, so an aura-only cast is never dodged.</summary>
    [Fact]
    public async Task Apply_an_aura_only_ability_without_dealing_anything_or_rolling_a_dodge()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync(new ScriptedCombatRandom());
        MapInstanceClient wizard = Caster(instance, 912_103, Ignite());
        Creature boar = Boar(instance, 912_903, dodge: 30f);

        handler.Execute(wizard.Connection, new CCastAbilityPacket { AbilityId = 213, GroundPos = AtTheBoar });

        Assert.Equal(100u, boar.CurrentHealth);
        ActiveAuraIs(boar, 902, wizard.Character.Guid);
        Assert.NotNull(instance.CombatService.GetEncounterFor(boar));
    }

    [Fact]
    public async Task Put_a_helpful_aura_on_the_caster_and_its_allies_and_heal_over_time()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient healer = Caster(instance, 912_104, Renew());

        handler.Execute(healer.Connection, new CCastAbilityPacket { AbilityId = 233, GroundPos = new Vector3Dto() });
        Assert.Equal(400u, healer.Character.CurrentHealth);   // no direct heal: Effects has none
        ActiveAuraIs(healer.Character, 904, healer.Character.Guid);

        _clock.Advance(TimeSpan.FromSeconds(3));
        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(406u, healer.Character.CurrentHealth);   // 24 over 4 ticks, from a caster with no stats
    }

    [Fact]
    public async Task Keep_a_harmful_aura_off_a_player_who_is_not_hostile()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync(new ScriptedCombatRandom());
        MapInstanceClient wizard = Caster(instance, 912_105, Ignite());
        MapInstanceClient bystander = Caster(instance, 912_106, Ignite());
        bystander.Character.Position = new Vector3(0f, 0f, 2f);

        handler.Execute(wizard.Connection, new CCastAbilityPacket { AbilityId = 213, GroundPos = AtTheBoar });

        Assert.Equal(0, bystander.Character.Auras.Count);
    }

    [Fact]
    public async Task Tick_an_instances_auras_on_its_own_clock()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient warrior = Caster(instance, 912_107, Rend());
        Creature boar = Boar(instance, 912_904);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });

        _clock.Advance(TimeSpan.FromSeconds(3));
        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(87u, boar.CurrentHealth);   // 90 after the cone, then a tick of 3
    }

    [Fact]
    public async Task End_every_aura_of_a_unit_a_hit_kills()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient warrior = Caster(instance, 912_108, Rend());
        Creature boar = Boar(instance, 912_905, health: 15);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });
        Assert.Equal(1, boar.Auras.Count);

        warrior.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-1);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });

        Assert.Equal(0u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
    }

    /// <summary>
    /// The killing tick reports the death from inside the aura pass: every aura ends there, and the pass ticks nothing
    /// more on the corpse. A death reported again changes nothing.
    /// </summary>
    [Fact]
    public async Task End_every_aura_of_a_unit_its_own_tick_kills()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient warrior = Caster(instance, 912_109, Rend());
        Creature boar = Boar(instance, 912_906, health: 13);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });
        Assert.Equal(3u, boar.CurrentHealth);

        _clock.Advance(TimeSpan.FromSeconds(3));
        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(0u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);

        ((Avalon.World.Combat.ICombatOutcomes)instance).UnitDied(boar);
        Assert.Equal(0, boar.Auras.Count);
    }

    [Fact]
    public async Task End_a_dead_characters_auras_once_however_often_its_death_is_reported()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient healer = Caster(instance, 912_110, Renew());
        handler.Execute(healer.Connection, new CCastAbilityPacket { AbilityId = 233, GroundPos = new Vector3Dto() });
        Assert.Equal(1, healer.Character.Auras.Count);
        healer.Character.CurrentHealth = 0;
        healer.Character.IsDead = true;

        var outcomes = (Avalon.World.Combat.ICombatOutcomes)instance;
        outcomes.UnitDied(healer.Character);
        outcomes.UnitDied(healer.Character);

        Assert.Equal(0, healer.Character.Auras.Count);
    }

    private static void ActiveAuraIs(Avalon.World.Public.Units.IUnit unit, uint auraId, ObjectGuid caster)
    {
        Avalon.World.Auras.ActiveAura aura = Assert.Single(Avalon.World.Auras.AuraHolders.Of(unit)!.All);
        Assert.Equal((auraId, caster), (aura.Id.Value, aura.CasterGuid));
    }
}
