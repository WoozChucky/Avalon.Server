using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>
/// Abilities through the real cast handler, cast system and instance: the direct amount when Effects has it, then the
/// aura; a dodged hit applies none, with or without damage (#1164), unless the ability is Unavoidable; hostility decides
/// who receives it. Bleed and
/// Renew run the recording script, so these tests share its static log with every other test that reads it.
/// </summary>
[Collection(nameof(RecordingAuraScript))]
public class AbilityAuraShould
{
    public AbilityAuraShould() => RecordingAuraScript.Heard.Clear();

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

    /// <summary>Ignite's shape and aura, flagged Unavoidable (#1164).</summary>
    private static AbilityTemplate UnavoidableIgnite()
    {
        AbilityTemplate ignite = Ignite();
        ignite.Id = new AbilityId(219);
        ignite.Flags = AbilityFlags.Unavoidable;
        return ignite;
    }

    /// <summary>Ignite's shape with a stun for its aura and no damage (#1164).</summary>
    private static AbilityTemplate StunningIgnite()
    {
        AbilityTemplate ignite = Ignite();
        ignite.Id = new AbilityId(218);
        ignite.AuraId = new AuraId(920);
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

    private static AbilityTemplate Volley()
    {
        AbilityTemplate volley = AbilityTestData.Projectile(222, reach: 20f, speed: 20f);
        volley.AuraId = new AuraId(901);
        return volley;
    }

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    private async Task<(MapInstance Instance, CastAbilityHandler Handler)> InstanceAsync(ICombatRandom? random = null)
    {
        IScriptManager scripts = Substitute.For<IScriptManager>();
        scripts.GetAuraScript(Arg.Any<string>()).Returns(call => AuraHarness.TestScript(call.Arg<string>()));
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            scripts: scripts,
            abilities: () => [Rend(), Ignite(), UnavoidableIgnite(), StunningIgnite(), Renew(), Volley()],
            auras: () =>
            [
                AuraTestData.Scripted(AuraTestData.Bleed(), nameof(RecordingAuraScript)), AuraTestData.Burn(),
                AuraTestData.Scripted(AuraTestData.Renew(), nameof(RecordingAuraScript)), AuraTestData.Stunned(),
            ]));
        var auraScripts = new AuraScripts(scripts, new ServiceCollection().BuildServiceProvider(), _clock,
            NullLogger<AuraScripts>.Instance);
        MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, world: NewWorld(data),
            random: random ?? ScriptedCombatRandom.Plain(), time: _clock, auraScripts: auraScripts);
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

    private static Creature Boar(MapInstance instance, uint id, uint health = 100, uint evasion = 0,
        float stunResistance = 100f)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Vector3(0f, 0f, 2f),
            Level = 1,
            Health = health,
            CurrentHealth = health,
            Evasion = evasion,
            ControlResistance = ControlResistance.Full with { Stun = stunResistance },
        };
        creature.Script = new CombatResolutionShould.CountingWoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    private static readonly Vector3Dto s_atTheBoar = new() { X = 0f, Y = 0f, Z = 2f };

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
        Creature boar = Boar(instance, 912_902, evasion: 10000);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });

        Assert.Equal(100u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
    }

    /// <summary>
    /// An ability that deals no damage rolls whether it lands, as a damage hit does (#1164): the one evasion draw (a scripted
    /// roll under the boar's capped 30 % is an evasion), then only its aura. Evaded, it applies none and is sent as a dodge; landed,
    /// it sends no damage and the boar's script is never hit. Unavoidable, it draws nothing at that chance (the
    /// random throws on a draw it was not given) and lands. Landed on a boar that resists stuns whole, its stun is told
    /// as Immune and placed nothing. Combat is entered every time, by the hit itself, whether or not its aura sticks.
    /// </summary>
    [Theory]
    [InlineData(213u, 10000u, new[] { 0.0 }, 100f, 0u, HitResult.Dodged)]
    [InlineData(213u, 10000u, new[] { 0.99 }, 100f, 902u, HitResult.None)]
    [InlineData(219u, 10000u, new double[0], 100f, 902u, HitResult.None)]
    [InlineData(218u, 10000u, new[] { 0.99 }, 0f, 0u, HitResult.Immune)]
    public async Task Roll_whether_an_ability_without_damage_lands_before_applying_its_aura(uint abilityId, uint evasion,
        double[] rolls, float stunResistance, uint aura, HitResult sent)
    {
        var random = new ScriptedCombatRandom(rolls);
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync(random);
        MapInstanceClient wizard = Caster(instance, 912_103, abilityId switch
        {
            213 => Ignite(),
            218 => StunningIgnite(),
            _ => UnavoidableIgnite(),
        });
        Creature boar = Boar(instance, 912_903, evasion: evasion, stunResistance: stunResistance);

        handler.Execute(wizard.Connection, new CCastAbilityPacket { AbilityId = abilityId, GroundPos = s_atTheBoar });

        Assert.Equal((0, 100u, 0), (random.DoublesLeft, boar.CurrentHealth,
            ((CombatResolutionShould.CountingWoundScript)boar.Script!).Hits));
        if (aura != 0)
            ActiveAuraIs(boar, aura, wizard.Character.Guid);
        else
            Assert.Equal(0, boar.Auras.Count);
        (uint, HitResult)[] expected = sent == HitResult.None ? [] : [(0u, sent)];
        Assert.Equal(expected, wizard.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED)
            .Select(p => (p.Damage, p.Result)));
        Assert.NotNull(instance.CombatService.GetEncounterFor(boar));
        Assert.True(wizard.Character.IsInCombat, "the caster is not in combat");
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

        handler.Execute(wizard.Connection, new CCastAbilityPacket { AbilityId = 213, GroundPos = s_atTheBoar });

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

        warrior.Character.LastCastStartTime = _clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
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
        Assert.Equal(["Renew:remove:Death"], RecordingAuraScript.Heard.Where(h => h.Contains(":remove:")));
    }

    [Fact]
    public async Task Apply_a_projectiles_aura_to_the_unit_it_hits()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient hunter = Caster(instance, 912_111, Volley());
        Creature boar = Boar(instance, 912_907);

        handler.Execute(hunter.Connection, new CCastAbilityPacket { AbilityId = 222, GroundPos = s_atTheBoar });
        for (int i = 0; i < 30 && boar.Auras.Count == 0; i++)
            instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(90u, boar.CurrentHealth);   // the projectile's own 10
        ActiveAuraIs(boar, 901, hunter.Character.Guid);
    }

    /// <summary>Everyone leaves, a tick runs with nobody here, the clock moves on by <paramref name="away" />, and they return.</summary>
    private void LeaveAndReturn(MapInstance instance, MapInstanceClient client, TimeSpan away)
    {
        instance.RemoveCharacter(client.Connection);
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        _clock.Advance(away);
        instance.AddCharacter(client.Connection);
    }

    [Fact]
    public async Task End_an_aura_whose_time_ran_out_while_the_instance_was_empty_without_paying_its_ticks()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient warrior = Caster(instance, 912_112, Rend());
        Creature boar = Boar(instance, 912_908, health: 13);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });
        Assert.Equal(3u, boar.CurrentHealth);   // four owed ticks of 3 would kill it

        LeaveAndReturn(instance, warrior, TimeSpan.FromSeconds(13));
        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(3u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
        Assert.DoesNotContain("Bleed:tick", RecordingAuraScript.Heard);
        Assert.Equal(["Bleed:remove:Expired"], RecordingAuraScript.Heard.Where(h => h.Contains(":remove:")));
    }

    [Fact]
    public async Task Skip_the_ticks_owed_while_the_instance_was_empty_and_tick_on_from_its_return()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient warrior = Caster(instance, 912_113, Rend());
        Creature boar = Boar(instance, 912_909);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });

        LeaveAndReturn(instance, warrior, TimeSpan.FromSeconds(4.5));   // the tick at 3 s came due while empty
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        Assert.Equal(90u, boar.CurrentHealth);

        _clock.Advance(TimeSpan.FromSeconds(1.5));   // 6 s: the next tick
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        Assert.Equal(87u, boar.CurrentHealth);

        _clock.Advance(TimeSpan.FromSeconds(6));   // 12 s: the last two, then it ends
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        Assert.Equal(81u, boar.CurrentHealth);
        Assert.Equal(0, boar.Auras.Count);
    }

    [Fact]
    public async Task Pay_every_tick_owed_after_a_stall_while_players_are_here()
    {
        (MapInstance instance, CastAbilityHandler handler) = await InstanceAsync();
        MapInstanceClient warrior = Caster(instance, 912_114, Rend());
        Creature boar = Boar(instance, 912_910);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 203 });
        _clock.Advance(TimeSpan.FromSeconds(5));
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        Assert.Equal(87u, boar.CurrentHealth);

        _clock.Advance(TimeSpan.FromSeconds(4));   // a stall: 9 s, so the tick at 6 s and the one at 9 s are owed
        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(81u, boar.CurrentHealth);
    }

    private static void ActiveAuraIs(Avalon.World.Public.Units.IUnit unit, uint auraId, ObjectGuid caster)
    {
        Avalon.World.Auras.ActiveAura aura = Assert.Single(Avalon.World.Auras.AuraHolders.Of(unit)!.All);
        Assert.Equal((auraId, caster), (aura.Id.Value, aura.CasterGuid));
    }
}
