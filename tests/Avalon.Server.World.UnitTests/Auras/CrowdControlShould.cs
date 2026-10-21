using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Items;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>
/// Crowd control as it is applied (#770), through a real instance over the seeded reference data: Stunned (12, 1.5 s)
/// from Shield Bash (204), Bound (13, 3 s) from Binding Shot (224), Frozen (14, 2.5 s) from Frost Bind (214), Silenced
/// (15) and Crippled (3, a 6 s slow) from Crippling Shot (223), and the seeded rarity rows. Character ids 770_xxx,
/// creature ids 770_9xx.
/// </summary>
public class CrowdControlShould
{
    private const uint Stunned = 12, Bound = 13, Silenced = 15;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    private static StaticData Data => ScenarioReferenceData.Shared.Data;

    /// <summary>
    /// A creature takes its share of a CC aura's duration: its template's own percentage where set, else its rarity's
    /// (Normal 100, Rare 50, Boss 0 for a stun and 50 for a root or a slow). 0 places nothing and tells the caster
    /// "Immune" once, as a hit of 0; so does a creature walking home, whatever its resistance.
    /// </summary>
    [Theory]
    [InlineData(204u, CreatureRarity.Normal, null, 1500u)]
    [InlineData(204u, CreatureRarity.Rare, null, 750u)]
    [InlineData(204u, CreatureRarity.Boss, null, 0u)]
    [InlineData(204u, CreatureRarity.Boss, 100f, 1500u)]
    [InlineData(214u, CreatureRarity.Boss, null, 1250u)]
    [InlineData(223u, CreatureRarity.Boss, null, 3000u)]
    [InlineData(204u, CreatureRarity.Normal, null, 0u, true)]
    public void Scale_crowd_control_on_creatures_by_rarity_and_override(uint abilityId, CreatureRarity rarity,
        float? stunOverride, uint landedMs, bool walkingHome = false)
    {
        using MapInstance instance = Instance();
        MapInstanceClient hunter = Join(instance, 770_101);
        CreatureTemplate template = Loot.LootTestData.BoarTemplate(null);
        template.Rarity = rarity;
        template.StunDurationPct = stunOverride;
        Creature boar = AddCreature(instance, 770_901, new Vector3(0f, 0f, 2f), template);
        if (walkingHome)
            TurnForHome(boar, hunter.Character);

        ((IAbilityArena)instance).ApplyAbilityAura(hunter.Character, boar, Seeded(abilityId));

        Assert.Equal(landedMs, boar.Auras.All.SingleOrDefault()?.DurationMs ?? 0u);
        Assert.Equal(landedMs == 0 ? 1 : 0, hunter.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED)
            .Count(p => p is { Result: HitResult.Immune, Damage: 0 }));
    }

    /// <summary>
    /// Stuns from characters on a character step down whoever casts them (full, half, quarter, then immune), and start
    /// over once none has landed for the 6 s window: the immune attempt does not restart it, and is told as "Immune", a
    /// hit of 0, to the target and to its caster. Each type counts on its own, and a creature's never diminishes.
    /// </summary>
    [Fact]
    public void Diminish_player_crowd_control_and_reset_after_the_window()
    {
        using MapInstance instance = Instance();
        MapInstanceClient warrior = Join(instance, 770_201);
        MapInstanceClient other = Join(instance, 770_202);
        MapInstanceClient targetClient = Join(instance, 770_203);
        CharacterEntity target = targetClient.Character;
        GameAbility bash = Seeded(204);

        DateTimeOffset start = _clock.GetUtcNow();
        Assert.Equal(1500u, LandAndExpire(instance, warrior.Character, target, bash, Stunned));
        Assert.Equal(750u, LandAndExpire(instance, other.Character, target, bash, Stunned));
        DateTimeOffset lastLanded = _clock.GetUtcNow();
        Assert.Equal(375u, LandAndExpire(instance, warrior.Character, target, bash, Stunned));
        Assert.Equal(0u, LandAndExpire(instance, other.Character, target, bash, Stunned));
        Assert.Equal(start.AddMilliseconds(1500 + 750 + 375), _clock.GetUtcNow());
        Assert.Single(targetClient.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED),
            p => p is { Result: HitResult.Immune, Damage: 0, AbilityId: 204 } && p.Attacker == other.Character.Guid.RawValue);
        Assert.Single(other.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED),
            p => p is { Result: HitResult.Immune, Damage: 0 } && p.Target == target.Guid.RawValue);

        _clock.SetUtcNow(lastLanded.AddSeconds(6));
        Assert.Equal(1500u, LandAndExpire(instance, other.Character, target, bash, Stunned));

        CharacterEntity fresh = Join(instance, 770_204).Character;
        Assert.Equal(1500u, LandAndExpire(instance, warrior.Character, fresh, bash, Stunned));
        Assert.Equal(3000u, LandAndExpire(instance, warrior.Character, fresh, Seeded(224), Bound));

        CharacterEntity bitten = Join(instance, 770_205).Character;
        Creature boar = AddCreature(instance, 770_902, new Vector3(0f, 0f, 2f), Loot.LootTestData.BoarTemplate(null));
        for (int i = 0; i < 4; i++)
            Assert.Equal(1500u, LandAndExpire(instance, boar, bitten, bash, Stunned));
    }

    /// <summary>
    /// A refresh never shortens a held CC aura: a later end replaces the held one, an earlier one keeps it, and the timer
    /// the client is sent runs from the refresh to that end.
    /// </summary>
    [Fact]
    public void Keep_the_later_expiry_when_a_refresh_is_shorter()
    {
        using MapInstance instance = Instance();
        var arena = (IAbilityArena)instance;
        MapInstanceClient warrior = Join(instance, 770_301);
        MapInstanceClient other = Join(instance, 770_302);
        CharacterEntity target = Join(instance, 770_303).Character;
        GameAbility bash = Seeded(204);
        DateTimeOffset t0 = _clock.GetUtcNow();

        arena.ApplyAbilityAura(warrior.Character, target, bash);
        ActiveAura stun = Assert.Single(target.Auras.All);
        Assert.Equal((t0.AddMilliseconds(1500), 1500u), (stun.Schedule.ExpiresAt, stun.DurationMs));

        _clock.Advance(TimeSpan.FromMilliseconds(1000));
        arena.ApplyAbilityAura(other.Character, target, bash);   // 750: ends at 1.75 s, later than 1.5 s
        Assert.Same(stun, Assert.Single(target.Auras.All));
        Assert.Equal((t0.AddMilliseconds(1750), 750u), (stun.Schedule.ExpiresAt, stun.DurationMs));

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        arena.ApplyAbilityAura(warrior.Character, target, bash);   // 375: would end at 1.475 s, so 1.75 s stays
        Assert.Same(stun, Assert.Single(target.Auras.All));
        Assert.Equal((t0.AddMilliseconds(1750), 650u), (stun.Schedule.ExpiresAt, stun.DurationMs));
        Assert.Equal(650u, target.Auras.Changes[^1].RemainingMs);
    }

    /// <summary>
    /// A stun ends only running cast bars, with no cooldown started and each interrupt sent once: a creature's wind-up,
    /// stunned by a character's cast firing in the same tick's walk of the queue, and a character's ability, item and
    /// doodad cast bars (the item's use told it was interrupted). Queued
    /// after the stunner, the wind-up is ended before the walk reaches it, and a wind-up shorter than a tick must not
    /// fire there anyway; queued before, it already ran down this tick. A hit already fired and held for the dodge wait
    /// still lands, its caster stunned or not.
    /// </summary>
    [Theory]
    [InlineData(true, 10u)]
    [InlineData(false, 2000u)]
    public void Interrupt_only_the_running_cast_bar_when_stunned(bool stunnerQueuedFirst, uint windUpMs)
    {
        var ticks = new WorldTicks(1_000);
        using MapInstance instance = Instance(ticks);
        MapInstanceClient warrior = Player(instance, 770_401, Vector3.zero);
        MapInstanceClient caster = Player(instance, 770_402, new Vector3(-20f, 0f, 0f));
        MapInstanceClient victim = Player(instance, 770_403, new Vector3(20f, 0f, 2f));
        Creature windingUp = AddCreature(instance, 770_903, new Vector3(0f, 0f, 2f), Loot.LootTestData.BoarTemplate(null));
        Creature novaCaster = AddCreature(instance, 770_904, new Vector3(20f, 0f, 0f), Loot.LootTestData.BoarTemplate(null));

        GameAbility nova = Cast(AbilityTestData.Circle(315, radius: 3f), 10);
        GameAbility windUp = Cast(AbilityTestData.Circle(316, radius: 3f), windUpMs);
        GameAbility longCast = Cast(AbilityTestData.Circle(317, radius: 3f), 2000);
        GameAbility bash = Cast(AbilityTestData.Cone(204, reach: 2.5f), 10);   // the seeded row's aura: Stunned
        var forward = new AbilityAim(new Vector3(0f, 0f, 1f), null);

        // The cast queue (a HashSet with nothing removed yet) is walked in the order the casts were queued.
        Assert.True(instance.QueueAbility(novaCaster, forward, nova));
        if (stunnerQueuedFirst)
            Assert.True(instance.QueueAbility(warrior.Character, forward, bash));
        Assert.True(instance.QueueAbility(windingUp, forward, windUp));
        if (!stunnerQueuedFirst)
            Assert.True(instance.QueueAbility(warrior.Character, forward, bash));
        Assert.True(instance.QueueAbility(caster.Character, forward, longCast));
        bool itemUseInterrupted = false;
        instance.ItemUses.Start(new PendingItemUse
        {
            Character = caster.Character,
            Item = new ItemTemplateId(14),
            StartPosition = caster.Character.Position,
            CastId = instance.ItemUses.TakeCastId(),
            CastTimeSeconds = 5f,
            CanComplete = static () => true,
            Completed = static () => { },
            Interrupted = () => itemUseInterrupted = true,
        });
        instance.DoodadCasts.Start(new PendingDoodadUse(caster.Character, Barrel(770_801), caster.Character.Position,
            instance.DoodadCasts.TakeCastId(), _clock.GetUtcNow().UtcDateTime.AddSeconds(5), 5f, caster.Connection));

        // The bash and the nova fire on the first tick: the nova's hit on the victim waits, the bash stuns the wind-up.
        Tick(instance, ticks);
        Assert.Equal((false, false), (bash.Casting, nova.Casting));
        Assert.Equal(100u, victim.Character.CurrentHealth);
        Assert.Contains(windingUp.Auras.All, a => a.Id.Value == Stunned);
        Assert.Equal((false, 0f), (windUp.Casting, windUp.CooldownTimer));

        instance.Auras.Apply(warrior.Character, caster.Character, new AuraId(Stunned), AuraSource.None);
        instance.Auras.Apply(warrior.Character, novaCaster, new AuraId(Stunned), AuraSource.None);
        Assert.Equal((false, 0f), (longCast.Casting, longCast.CooldownTimer));
        Assert.Equal((false, false, true), (instance.ItemUses.IsCasting(caster.Character.Guid),
            instance.DoodadCasts.IsCasting(caster.Character.Guid), itemUseInterrupted));

        // Past the wind-up's whole cast time: nothing it would have hit (the warrior) is hit.
        for (int i = 0; i < 150; i++)
            Tick(instance, ticks);
        Assert.True(victim.Character.CurrentHealth < 100u);
        Assert.Equal(100u, warrior.Character.CurrentHealth);
        Assert.Equal(1, Interrupts(warrior, windingUp));
        Assert.Equal((1, 1, 1), (Interrupts(caster, caster.Character), Interrupts(caster, caster.Character, item: true),
            Interrupts(caster, caster.Character, doodad: true)));
    }

    /// <summary>
    /// A silence ends a running cast bar but spares the basic attack's, and an ability cast an item started (an item
    /// use), which a stun then ends.
    /// </summary>
    [Fact]
    public void Spare_the_basic_attack_when_silenced()
    {
        using MapInstance instance = Instance();
        MapInstanceClient basicCaster = Player(instance, 770_501, Vector3.zero);
        MapInstanceClient spellCaster = Player(instance, 770_502, new Vector3(10f, 0f, 0f));
        AbilityTemplate basicRow = AbilityTestData.Circle(318, radius: 3f);
        basicRow.Flags = AbilityFlags.Basic;
        GameAbility basic = Cast(basicRow, 2000);
        GameAbility spell = Cast(AbilityTestData.Circle(319, radius: 3f), 2000);
        var forward = new AbilityAim(new Vector3(0f, 0f, 1f), null);
        Assert.True(instance.QueueAbility(basicCaster.Character, forward, basic));
        Assert.True(instance.QueueAbility(spellCaster.Character, forward, spell));

        instance.Auras.Apply(spellCaster.Character, basicCaster.Character, new AuraId(Silenced), AuraSource.None);
        instance.Auras.Apply(basicCaster.Character, spellCaster.Character, new AuraId(Silenced), AuraSource.None);

        Assert.Equal((true, false), (basic.Casting, spell.Casting));
        Assert.Equal((0, 1), (Interrupts(basicCaster, basicCaster.Character), Interrupts(spellCaster, spellCaster.Character)));

        MapInstanceClient itemUser = Player(instance, 770_503, new Vector3(-10f, 0f, 0f));
        GameAbility itemCast = Cast(AbilityTestData.Circle(320, radius: 3f), 2000);
        Assert.True(((IItemUseHost)instance).CastForItem(itemUser.Character, forward, itemCast, free: true));
        instance.Auras.Apply(spellCaster.Character, itemUser.Character, new AuraId(Silenced), AuraSource.None);
        Assert.True(itemCast.Casting);
        instance.Auras.Apply(spellCaster.Character, itemUser.Character, new AuraId(Stunned), AuraSource.None);
        Assert.Equal((false, 1), (itemCast.Casting, Interrupts(itemUser, itemUser.Character)));
    }

    private MapInstance Instance(WorldTicks? ticks = null) =>
        TestMapInstances.BuildCasting(out _, world: NewWorld(Data), random: ScriptedCombatRandom.Plain(), time: _clock,
            worldTicks: ticks);

    /// <summary>
    /// Applies <paramref name="ability" />'s aura from <paramref name="from" /> and answers how long the copy of
    /// <paramref name="aura" /> it left lasts, 0 for none; then runs the clock to its end and the aura pass, so it is gone.
    /// </summary>
    private uint LandAndExpire(MapInstance instance, IUnit from, CharacterEntity on, GameAbility ability, uint aura)
    {
        ((IAbilityArena)instance).ApplyAbilityAura(from, on, ability);
        if (on.Auras.All.FirstOrDefault(a => a.Id.Value == aura) is not { } held)
            return 0u;

        _clock.Advance(TimeSpan.FromMilliseconds(held.DurationMs));
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        Assert.DoesNotContain(held, on.Auras.All);
        return held.DurationMs;
    }

    private static GameAbility Seeded(uint id)
    {
        Assert.True(Data.LoadedAbilities!.TryGet(new AbilityId(id), out AbilityTemplate? row));
        return AbilityTestData.Game(row);
    }

    private static GameAbility Cast(AbilityTemplate row, uint castTimeMs)
    {
        row.CastTime = castTimeMs;
        return AbilityTestData.Game(row);
    }

    /// <summary>The interrupts of <paramref name="caster" />'s ability casts the client saw, or of its item or doodad bars.</summary>
    private static int Interrupts(MapInstanceClient client, IUnit caster, bool item = false, bool doodad = false) =>
        client.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST)
            .Count(p => p.Caster == caster.Guid.RawValue && p.ItemTemplateId != 0 == item && p.DoodadTemplateId != 0 == doodad);

    /// <summary>A plain clicked barrel: what a doodad cast bar is started on.</summary>
    private static Doodad Barrel(uint id) => new(new ObjectGuid(ObjectType.Doodad, id), new DoodadTemplateView(
            new DoodadTemplateId(1), "Barrel", "props-kit", "Barrel", null, null, null, null,
            DoodadInteraction.Click, 0, 5f, 0f, DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, null, null,
            null, 0, 0, 0, DoodadCollision.None, 0f, 0f, 0f, null, null, [], 0, ScriptCanSee: false),
        new Vector3(-20f, 0f, 1f), 0f);

    private static void Tick(MapInstance instance, WorldTicks ticks)
    {
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        ticks.Advance();
    }

    /// <summary>A player facing +Z at full health whose connection reports a 120 ms round trip: a 4-tick dodge wait.</summary>
    private static MapInstanceClient Player(MapInstance instance, uint id, Vector3 position)
    {
        MapInstanceClient player = Join(instance, id);
        player.Character.Position = position;
        player.Character.Orientation = Vector3.zero;
        player.Character.Health = 100;
        player.Character.CurrentHealth = 100;
        player.Connection.RoundTripTime.Returns(TimeSpan.FromMilliseconds(120).Ticks);
        return player;
    }

    /// <summary>
    /// Turns <paramref name="creature" /> for home through its combat script's own calls: it takes
    /// <paramref name="target" /> on, is drawn 5 m from where that began, and loses it.
    /// </summary>
    private static void TurnForHome(Creature creature, ICharacter target)
    {
        ISimulationContext ctx = Substitute.For<ISimulationContext>();
        ctx.MeleeSlots.Returns(Substitute.For<IMeleeSlots>());
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, ctx);
        creature.Script = script;
        script.OnEnteredRange(target);
        creature.Position = new Vector3(5f, 0f, 0f);
        script.OnCharacterLeft(target);
        Assert.Equal(CreatureCombatScript.CombatState.Returning, script.State);
    }

    /// <summary>A creature as the spawner fixes it: its resistance from its template and the seeded rarity rows.</summary>
    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position, CreatureTemplate template)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = template,
            Position = position,
            Level = 1,
            Health = 100,
            CurrentHealth = 100,
            ControlResistance = ControlResistance.Of(template, Data.CreatureRarityModifiers),
        };
        creature.Script = new CombatResolutionShould.CountingWoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }
}
