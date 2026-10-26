using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Items;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using Avalon.World.Reload;
using Avalon.World.Testing;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// Resonance effects firing (#1180 part A2), through a real instance over the seeded reference data: a direct hit's Hit,
/// Crit and Struck, a dodge's Evaded, a ward emptied by a hit, and a creature's Kill, each through the instance's
/// dispatcher, its cooldown, its chance and the aura system. Every combat roll is scripted, so each test also proves which
/// were drawn. Wearers wear four pieces (helmet 36, two rings 68, amulet 69), each with one T9 affix of each theme they
/// are given, none of which adds an increase to damage or healing: a theme's count is then 4. Character ids 1180_xxx,
/// creature ids 1180_9xx.
/// </summary>
public class ResonanceProcsShould
{
    // The seeded effect auras (ResonanceSeed).
    private const uint Burning1 = 16, Burning3 = 18, Rimebound = 22, Regrowth3 = 28;

    private const double Miss = 0.99;

    /// <summary>The affix that gives each theme its count: none adds an increase to damage or healing.</summary>
    private static readonly Dictionary<string, string> s_affixOf = new()
    {
        ["Ember"] = "S5 Fire T9",
        ["Rime"] = "S5 Cold T9",
        ["Storm"] = "S5 Lightning T9",
        ["Wild"] = "S4 T9",
        ["Iron"] = "P12 T9",
        ["Thorn"] = "S9 T9",
        ["Gale"] = "P14 T9",
        ["Veil"] = "P16 T9",
    };

    private static readonly (byte Slot, ulong Template)[] s_kit =
    [
        (EquipmentSlots.Head, 36), (EquipmentSlots.Finger1, 68), (EquipmentSlots.Finger2, 68), (EquipmentSlots.Neck, 69),
    ];

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly ScriptedCombatRandom _random = new();

    // Everything the instances log: a firing that threw is contained and logged, so a row where nothing fires also proves
    // that nothing failed.
    private readonly Creatures.CreatureAbilitiesShould.ListLogger _logs = new();

    // What wearers are dressed and refreshed over: the shared reference data, unless a test reloads its own.
    private StaticData _data = Data;

    private static StaticData Data => ScenarioReferenceData.Shared.Data;

    /// <summary>
    /// Each trigger fires its effect on its own event and nowhere else, on the unit the effect names: the creature hit
    /// (Hit, Crit), the killer (Kill), the creature that struck (Struck), the wearer (Evaded, WardBroken, Rootsurge's life
    /// threshold). A chance under 100 % draws once, past every other check; an effect at 100 % draws nothing. A hit that
    /// kills its creature fires no effect on the corpse, and a hit in town fires none on a flagged player, nor its Struck
    /// on the attacker: neither draws. Nothing is logged at Error on any row, so a row where nothing fires did not fail.
    /// </summary>
    [Theory]
    [InlineData("Hit: Burning III on the creature hit")]
    [InlineData("Hit: a chance draw of 0.5 misses Burning III's 30 %")]
    [InlineData("Crit: Jolt III on the creature crit; a plain hit places none")]
    [InlineData("Kill: Regrowth III and Tailwind III on the killer")]
    [InlineData("Struck: Barbs III on the creature that hit")]
    [InlineData("Struck at 36 % life: no Rootsurge")]
    [InlineData("Struck at 35 % life: Rootsurge")]
    [InlineData("Evaded: Slipstream")]
    [InlineData("WardBroken: Veiled III when the hit empties the ward; none when the ward survives")]
    [InlineData("Hit that kills its creature: no Burning drawn")]
    [InlineData("Hit in town on a flagged player: nothing drawn")]
    public void Fire_each_trigger_on_its_event_and_only_there(string @case)
    {
        using MapInstance instance = Instance(@case.Contains("town", StringComparison.Ordinal) ? MapType.Town : MapType.Normal);
        ICombatService combat = instance.CombatService;
        Dictionary<string, IUnit> cast = [];
        string[] expected;
        int draws;
        switch (@case)
        {
            case "Hit: Burning III on the creature hit":
            case "Hit: a chance draw of 0.5 misses Burning III's 30 %":
            {
                bool hits = @case.StartsWith("Hit: Burning", StringComparison.Ordinal);
                IUnit wearer = cast["wearer"] = Wearer(instance, 1180_101, "Ember");
                IUnit boar = cast["boar"] = Boar(instance, 1180_901);
                _random.Doubles(Miss, Miss, Miss, hits ? 0.1 : 0.5);
                combat.ApplyDamage(wearer, boar, 10);
                expected = hits ? ["Burning III on boar from wearer"] : [];
                draws = 4;
                break;
            }

            case "Crit: Jolt III on the creature crit; a plain hit places none":
            {
                IUnit wearer = cast["wearer"] = Wearer(instance, 1180_102, "Storm");
                IUnit boar = cast["boar"] = Boar(instance, 1180_902);
                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(wearer, boar, 10);
                Assert.Empty(Holds(cast));

                _random.Doubles(Miss, 0.0, Miss);   // the crit
                combat.ApplyDamage(wearer, boar, 10);
                expected = ["Jolt III on boar from wearer"];
                draws = 6;
                break;
            }

            case "Kill: Regrowth III and Tailwind III on the killer":
            {
                IUnit wearer = cast["wearer"] = Wearer(instance, 1180_103, "Wild", "Gale");
                IUnit boar = cast["boar"] = Boar(instance, 1180_903, health: 5);
                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(wearer, boar, 10);
                Assert.Equal(0u, boar.CurrentHealth);
                expected = ["Regrowth III on wearer from wearer", "Tailwind III on wearer from wearer"];
                draws = 3;
                break;
            }

            case "Struck: Barbs III on the creature that hit":
            {
                IUnit wearer = cast["wearer"] = Wearer(instance, 1180_104, "Thorn");
                IUnit boar = cast["boar"] = Boar(instance, 1180_904);
                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(boar, wearer, 10);
                expected = ["Barbs III on boar from wearer"];
                draws = 3;
                break;
            }

            case "Struck at 36 % life: no Rootsurge":
            case "Struck at 35 % life: Rootsurge":
            {
                // A first hit at full health empties the ward a level 1 warrior has; then a raw hit of 1 deals 1, whatever
                // its mitigation, and leaves the wearer at 36 or 35 of 100.
                bool at35 = @case.Contains("35", StringComparison.Ordinal);
                CharacterEntity wearer = Wearer(instance, 1180_105, "Wild");
                cast["wearer"] = wearer;
                IUnit boar = cast["boar"] = Boar(instance, 1180_905);
                wearer.Health = wearer.CurrentHealth = 1000;
                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(boar, wearer, 50);
                Assert.Equal(0u, wearer.Ward);

                wearer.Health = 100;
                wearer.CurrentHealth = at35 ? 36u : 37u;
                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(boar, wearer, 1);
                Assert.Equal(at35 ? 35u : 36u, wearer.CurrentHealth);
                expected = at35 ? ["Rootsurge on wearer from wearer"] : [];
                draws = 6;
                break;
            }

            case "Evaded: Slipstream":
            {
                IUnit wearer = cast["wearer"] = Wearer(instance, 1180_106, "Gale");
                IUnit boar = cast["boar"] = Boar(instance, 1180_906);
                _random.Doubles(0.0);   // the evasion its Gale affixes give it
                combat.ApplyDamage(boar, wearer, 10);
                expected = ["Slipstream on wearer from wearer"];
                draws = 1;
                break;
            }

            case "WardBroken: Veiled III when the hit empties the ward; none when the ward survives":
            {
                CharacterEntity wearer = Wearer(instance, 1180_107, "Veil");
                cast["wearer"] = wearer;
                IUnit boar = cast["boar"] = Boar(instance, 1180_907);
                wearer.Health = wearer.CurrentHealth = 10_000;
                uint full = wearer.Ward;
                Assert.True(full >= 2);

                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(boar, wearer, 1);
                Assert.Equal(full - 1, wearer.Ward);
                Assert.Empty(Holds(cast));

                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(boar, wearer, 100);
                Assert.Equal(0u, wearer.Ward);
                expected = ["Veiled III on wearer from wearer"];
                draws = 6;
                break;
            }

            case "Hit that kills its creature: no Burning drawn":
            {
                IUnit wearer = cast["wearer"] = Wearer(instance, 1180_108, "Ember");
                IUnit boar = cast["boar"] = Boar(instance, 1180_908, health: 5);
                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(wearer, boar, 10);
                Assert.Equal(0u, boar.CurrentHealth);
                expected = [];
                draws = 3;
                break;
            }

            case "Hit in town on a flagged player: nothing drawn":
            {
                // Its Thorn 4 would place Barbs on the attacker outside a town.
                CharacterEntity attacker = Wearer(instance, 1180_109, "Ember");
                CharacterEntity struck = Wearer(instance, 1180_110, "Thorn");
                attacker.Data!.PvpEnabled = struck.Data!.PvpEnabled = true;
                (cast["attacker"], cast["struck"]) = (attacker, struck);
                _random.Doubles(Miss, Miss, Miss);
                combat.ApplyDamage(attacker, struck, 10);
                expected = [];
                draws = 3;
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }

        Assert.Equal(expected, Holds(cast));
        Assert.Equal(draws, _random.DoublesDrawn);
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);
    }

    /// <summary>
    /// A cooldown is spent only when its effect fires, and is kept across gear swaps and an effect catalog reload: one
    /// entry per theme and trigger, shared by its thresholds (Burning II and III). A miss spends nothing, so the next hit
    /// draws again; within the 2 s nothing is drawn, at Ember 3, at Ember 4 again, or over a reloaded catalog; at 2 s it
    /// draws again. A resisted application spends it too: Rimebound on a creature immune to roots is told Immune, and
    /// a crit 5 s later draws nothing for it (its Chilled III, on the same crit, still draws).
    /// </summary>
    [Fact]
    public void Spend_a_cooldown_only_when_it_fires_and_keep_it_across_gear_swaps()
    {
        using MapInstance instance = Instance();
        CharacterEntity wearer = Wearer(instance, 1180_201, "Ember");
        Creature boar = Boar(instance, 1180_921, health: 100_000);
        ResonanceProcs procs = TestCharacters.ResonanceProcsOf(wearer);

        Hit(instance, wearer, boar, chance: [0.9]);
        Assert.Empty(boar.Auras.All);
        Hit(instance, wearer, boar, chance: [0.1]);
        Assert.Equal([Burning3], boar.Auras.All.Select(a => a.Id.Value));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Hit(instance, wearer, boar, chance: []);

        // At 1.5 s: Ember 3, then Ember 4 again over a reloaded effect catalog, whose rows are new objects.
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        CharacterInventoryContainer equipment = wearer.Container(InventoryType.Equipment);
        Assert.True(equipment.TryGet(EquipmentSlots.Finger2, out InventoryItem ring));
        Assert.True(equipment.Remove(EquipmentSlots.Finger2));
        Assert.True(CharacterStatsRefresh.Apply(wearer, Data, CurrentValues.KeepShare));
        Assert.Equal("Burning II", Data.Auras.TryGet(procs.EffectAt(0).Aura, out AuraTemplate? second) ? second.Name : null);
        Hit(instance, wearer, boar, chance: []);

        equipment.Put(ring);
        ResonanceEffectCatalog reloaded = ItemModifierRepositories.SeededEffects();
        Assert.True(CharacterStatsRefresh.Apply(wearer, Data.ClassLevelStats, Data.Combat.Factors, Data.FindItemTemplate,
            Data.Items.Modifiers.Resonance, reloaded, CurrentValues.KeepShare, Data.Combat.Formula));
        Assert.Same(reloaded.Effects.Single(e => e.Aura.Value == Burning3), procs.EffectAt(0));
        Hit(instance, wearer, boar, chance: []);

        _clock.Advance(TimeSpan.FromMilliseconds(500));
        Hit(instance, wearer, boar, chance: [0.9]);

        // Rime 4 on a crit: Chilled III (Hit) misses, Rimebound (Crit) fires and is resisted whole.
        MapInstanceClient rime = Join(instance, Dressed(TestCharacters.New(1180_202, time: _clock), "Rime"));
        CreatureTemplate rootless = Loot.LootTestData.BoarTemplate(null);
        rootless.RootDurationPct = 0f;
        Creature rooted = Boar(instance, 1180_922, health: 100_000, template: rootless);
        Hit(instance, rime.Character, rooted, chance: [0.9, 0.1], crit: true);
        Assert.Empty(rooted.Auras.All);
        Assert.Single(rime.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED),
            p => p is { Result: HitResult.Immune, Damage: 0 } && p.Target == rooted.Guid.RawValue);

        _clock.Advance(TimeSpan.FromSeconds(5));
        Hit(instance, rime.Character, rooted, chance: [0.9], crit: true);
        Assert.Single(rime.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED));
    }

    /// <summary>
    /// A proc's own tick is no direct hit, so it fires no Hit, Crit or Struck effect: Barbs III that a Thorn 4 player's
    /// Struck placed on another Thorn 4 player hits it 0.2 s later, and puts no Barbs back. A killing tick is a kill,
    /// though: Burning III's last tick kills its creature and its wearer's Wild 4 places Regrowth III, with no Hit effect
    /// drawn for any tick (each tick draws only its crit roll).
    /// </summary>
    [Fact]
    public void Fire_no_direct_hit_effect_from_a_procs_own_tick_and_fire_kill_effects_from_its_killing_tick()
    {
        using MapInstance instance = Instance();
        CharacterEntity a = Wearer(instance, 1180_301, "Thorn");
        CharacterEntity b = Wearer(instance, 1180_302, "Thorn");
        a.Data!.PvpEnabled = b.Data!.PvpEnabled = true;

        _random.Doubles(Miss, Miss, Miss);
        instance.CombatService.ApplyDamage(a, b, 10);
        ActiveAura barbs = Assert.Single(a.Auras.All);
        Assert.Equal(("Barbs III", b.Guid), (barbs.Template.Name, barbs.CasterGuid));

        uint before = a.CurrentHealth;
        _random.Doubles(Miss);   // the tick's crit roll
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        instance.Auras.Update();
        Assert.True(a.CurrentHealth < before);
        Assert.Empty(b.Auras.All);
        Assert.Equal(4, _random.DoublesDrawn);

        // Burning III deals 7 a tick to a creature with no resistance: 22 health falls on the fourth.
        CharacterEntity wearer = Wearer(instance, 1180_303, "Ember", "Wild");
        Creature boar = Boar(instance, 1180_931);
        Hit(instance, wearer, boar, chance: [0.1]);
        boar.CurrentHealth = 22;
        for (int tick = 1; tick <= 4; tick++)
        {
            _random.Doubles(Miss);
            _clock.Advance(TimeSpan.FromSeconds(1));
            instance.Auras.Update();
        }

        Assert.Equal(0u, boar.CurrentHealth);
        Assert.Equal([Regrowth3], wearer.Auras.All.Select(aura => aura.Id.Value));
        Assert.Equal(12, _random.DoublesDrawn);
    }

    /// <summary>
    /// A proc's ticks take its wearer's increases of its own type and of damage over time (or of healing), and never
    /// crit: a held aura's +20 % fire damage and +15 % damage over time make Burning III's 28 deal 37.8, so 38 over its
    /// four ticks, its +50 % attack damage counting for nothing. A held +45 % healing, on top of the +70 % a level 1
    /// warrior's 20 intellect gives (3.5 each), makes Regrowth III's 32 restore 69 (32 x 2.15 = 68.8).
    /// The aura's +100 crit chance never reaches them: their snapshots keep a crit chance of 0, so no tick crits although
    /// every crit roll is scripted to.
    /// </summary>
    [Fact]
    public void Scale_proc_damage_and_heals_by_the_wearers_increases_and_never_crit()
    {
        using MapInstance instance = Instance();
        MapInstanceClient ember = Join(instance, Dressed(TestCharacters.New(1180_401, time: _clock), "Ember"));
        Empower(instance, ember.Character, (AuraStat.IncreasedFireDamage, 20f), (AuraStat.IncreasedDamageOverTime, 15f),
            (AuraStat.IncreasedAttackDamage, 50f), (AuraStat.CritPct, 100f));
        Creature boar = Boar(instance, 1180_941, health: 1000);

        Hit(instance, ember.Character, boar, chance: [0.1]);
        ActiveAura burning = Assert.Single(boar.Auras.All);
        Assert.Equal((Burning3, 0f), (burning.Id.Value, burning.Snapshot.CritPct));
        uint before = boar.CurrentHealth;
        Tick(instance, 4);

        Assert.Equal(38u, before - boar.CurrentHealth);
        List<SUnitDamagePacket> ticks = [.. ember.Read<SUnitDamagePacket>(NetworkPacketType.SMSG_CREATURE_DAMAGED)
            .Where(p => p.AuraId == Burning3)];
        Assert.Equal((4, 38u), (ticks.Count, (uint)ticks.Sum(p => p.Damage)));
        Assert.DoesNotContain(ticks, p => (p.Result & HitResult.Crit) != 0);

        MapInstanceClient wild = Join(instance, Dressed(TestCharacters.New(1180_402, time: _clock), "Wild"));
        Empower(instance, wild.Character, (AuraStat.IncreasedHealing, 45f), (AuraStat.CritPct, 100f));
        Creature prey = Boar(instance, 1180_942, health: 5);
        Hit(instance, wild.Character, prey, chance: []);
        Assert.Equal(0u, prey.CurrentHealth);
        wild.Character.CurrentHealth = wild.Character.Health - 100;
        before = wild.Character.CurrentHealth;
        Tick(instance, 4);

        Assert.Equal(69u, wild.Character.CurrentHealth - before);
        List<SUnitHealedPacket> heals = [.. wild.Read<SUnitHealedPacket>(NetworkPacketType.SMSG_UNIT_HEALED)
            .Where(p => p.AuraId == Regrowth3)];
        Assert.Equal((4, 69u), (heals.Count, (uint)heals.Sum(p => p.Amount)));
        Assert.DoesNotContain(heals, p => (p.Result & HitResult.Crit) != 0);
    }

    /// <summary>
    /// Storm 4's crit places Jolt III on the creature crit and chains it to the two nearest living creatures hostile to
    /// the wearer within 8 m of it that take it: past an invulnerable one, a corpse and a flagged player nearer, past two
    /// at the aura cap that refuse it (a refusal takes no place in the chain), to one at 7 m, and never to one at 9 m (its
    /// 0.5 m body 8.5 m away), though the chain still has room. A unit here holds at most one aura.
    /// </summary>
    [Fact]
    public void Chain_a_jolt_to_the_nearest_living_hostile_creatures_within_eight_metres()
    {
        IWorld world = NewWorld(Data);
        world.Configuration.Returns(new GameConfiguration { MaxAurasPerUnit = 1 });
        using MapInstance instance = TestMapInstances.BuildCasting(out _, world: world, random: _random, time: _clock);
        CharacterEntity wearer = Wearer(instance, 1180_501, new Vector3(0f, 0f, -2f), "Storm");
        CharacterEntity player = Wearer(instance, 1180_502, new Vector3(0f, 0f, 1f));
        wearer.Data!.PvpEnabled = player.Data!.PvpEnabled = true;
        Dictionary<string, IUnit> cast = new()
        {
            ["wearer"] = wearer,
            ["H"] = player,
            ["A"] = Boar(instance, 1180_951, position: Vector3.zero),
            ["B"] = Boar(instance, 1180_952, position: new Vector3(3f, 0f, 0f)),
            ["C"] = Boar(instance, 1180_953, position: new Vector3(-6f, 0f, 0f)),
            ["D"] = Boar(instance, 1180_954, position: new Vector3(0f, 0f, 7f)),
            ["E"] = Boar(instance, 1180_955, position: new Vector3(9f, 0f, 0f)),
            ["F"] = Boar(instance, 1180_956, position: new Vector3(0f, 0f, 2f), invulnerable: true),
            ["G"] = Boar(instance, 1180_957, position: new Vector3(-1f, 0f, 0f), health: 0),
        };
        foreach (string full in (string[])["B", "C"])
            Assert.Equal(AuraApplyResult.Applied, instance.Auras.Apply(null, cast[full], new AuraId(Burning1), AuraSource.None));

        _random.Doubles(Miss, 0.0, Miss);
        instance.CombatService.ApplyDamage(wearer, cast["A"], 10);

        Assert.Equal(
        [
            "Burning I on B from someone else", "Burning I on C from someone else", "Jolt III on A from wearer",
            "Jolt III on D from wearer",
        ], Holds(cast));
        Assert.Equal(3, _random.DoublesDrawn);
    }

    /// <summary>
    /// A reload reaches an online wearer's next firing with no stats refresh in between. An aura edit that gives
    /// Rimebound a stun leaves Rime 4's crit effect out at once: after its cooldown, the next crit draws Chilled III's
    /// chance alone and places no Rimebound. And with an Items reload not yet counted, the Guard III a strike places
    /// refreshes the wearer's stats, which counts its themes again and rebuilds its effects: the walk ends there, so
    /// Barbs III, in the slot after Guard's, is not placed by that strike.
    /// </summary>
    [Theory]
    [InlineData("an aura edit gives Rimebound a stun")]
    [InlineData("a Guard III placed over an Items reload")]
    public async Task Fire_an_online_wearers_effects_from_the_catalog_a_reload_left(string @case)
    {
        bool stuns = false;
        StaticData data = await ScenarioReferenceData.Shared.NewDataAsync(auras =>
        {
            foreach (AuraTemplate aura in auras.Where(a => stuns && a.Id.Value == Rimebound))
                aura.Control |= CrowdControl.Stun;
            return auras;
        });
        _data = data;
        using MapInstance instance = Instance(data: data);
        Dictionary<string, IUnit> cast = [];
        if (@case.StartsWith("an aura edit", StringComparison.Ordinal))
        {
            CharacterEntity wearer = Wearer(instance, 1180_701, "Rime");
            Creature first = Boar(instance, 1180_971, health: 100_000);
            Creature second = Boar(instance, 1180_972, health: 100_000);
            (cast["wearer"], cast["second"]) = (wearer, second);
            Hit(instance, wearer, first, chance: [Miss, 0.1], crit: true);
            Assert.Equal(["Rimebound"], first.Auras.All.Select(a => a.Template.Name));

            stuns = true;
            data.Apply(await data.PrepareAsync(ReloadArea.Auras));
            Assert.True(data.Auras.TryGet(new AuraId(Rimebound), out AuraTemplate? edited));
            Assert.Equal(CrowdControl.Root | CrowdControl.Stun, edited.Control);

            _clock.Advance(TimeSpan.FromSeconds(10));
            Hit(instance, wearer, second, chance: [Miss], crit: true);
        }
        else
        {
            CharacterEntity wearer = Wearer(instance, 1180_702, "Iron", "Thorn");
            Creature boar = Boar(instance, 1180_973, health: 100_000);
            (cast["wearer"], cast["boar"]) = (wearer, boar);
            data.Apply(await data.PrepareAsync(ReloadArea.Items));

            int drawn = _random.DoublesDrawn;
            _random.Doubles(Miss, Miss, Miss, 0.1);
            instance.CombatService.ApplyDamage(boar, wearer, 10);
            Assert.Equal(drawn + 4, _random.DoublesDrawn);
        }

        string[] expected = @case.StartsWith("an aura edit", StringComparison.Ordinal) ? [] : ["Guard III on wearer from wearer"];
        Assert.Equal(expected, Holds(cast));
        Assert.DoesNotContain(_logs.Entries, e => e.Level >= LogLevel.Error);
    }

    /// <summary>
    /// A hit allocates nothing for Resonance when nothing fires: 1,000 hits of an Ember 4 and Thorn 4 wearer whose
    /// Burning III is on cooldown read 0 B, as do 1,000 of a wearer with no theme, and 1,000 of an unthemed wearer of a
    /// sealed rare (#1180), whose every hit feeds its Awakening meter (item level 1000, so it never wakes). A hit that places a new Burning III
    /// allocates nothing either, once warm: its copy is the last one, removed and pooled by the instance's next aura pass,
    /// placed again on the same creature, which holds none and is already in the wearer's fight. The fewest bytes of three
    /// rounds, so a background collection in one cannot fail it.
    /// </summary>
    [Fact]
    public void Allocate_nothing_per_hit_nor_when_a_proc_places_a_new_aura()
    {
        using MapInstance instance = TestMapInstances.Build(new QuietWorld(Data), random: _random, time: _clock);
        CharacterEntity themed = Quiet(instance, Dressed(TestCharacters.New(1180_601, time: _clock), "Ember", "Thorn"));
        CharacterEntity plain = Quiet(instance, Dressed(TestCharacters.New(1180_602, time: _clock)));
        CharacterEntity sealedWearer = Quiet(instance, SealedRare(TestCharacters.New(1180_603, time: _clock)));
        Assert.Equal(1, TestCharacters.AwakeningOf(sealedWearer).SealedWorn);
        Creature dummy = Boar(instance, 1180_961, health: uint.MaxValue);

        // Warm: Burning III placed (its cooldown now runs), and a plain and a sealed wearer's first hits.
        Hit(instance, themed, dummy, chance: [0.1]);
        Hit(instance, plain, dummy, chance: []);
        Hit(instance, sealedWearer, dummy, chance: []);
        foreach (CharacterEntity wearer in (CharacterEntity[])[themed, plain, sealedWearer])
        {
            _random.Doubles([.. Enumerable.Repeat(Miss, 3 * 1000)]);
            long start = ThreadAllocations.BeforeQuietWindow();
            for (int i = 0; i < 1000; i++)
                instance.CombatService.ApplyDamage(wearer, dummy, 10);

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
            Assert.Equal(0, _random.DoublesLeft);
        }

        // The first round is the warm-up: the creature's first copy is a new one.
        Creature target = Boar(instance, 1180_962, health: uint.MaxValue);
        long proc = long.MaxValue;
        for (int round = 0; round < 4; round++)
        {
            // Its last Burning III ended, pooled as the next pass starts and its change sent; the cooldown ready again.
            instance.Auras.RemoveAll(target, AuraRemoveReason.Expired);
            instance.Auras.Update();
            instance.Auras.Roster.ClearChanges();
            _clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Empty(target.Auras.All);

            _random.Doubles(Miss, Miss, Miss, 0.1);
            long start = ThreadAllocations.BeforeQuietWindow();
            instance.CombatService.ApplyDamage(themed, target, 10);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            if (round > 0)
                proc = Math.Min(proc, allocated);

            Assert.Equal([Burning3], target.Auras.All.Select(a => a.Id.Value));
            Assert.Equal(0, _random.DoublesLeft);
        }

        Assert.Equal(0, proc);
    }

    private MapInstance Instance(MapType mapType = MapType.Normal, StaticData? data = null)
    {
        ILoggerFactory loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(_logs);
        return TestMapInstances.BuildCasting(out _, mapType, world: NewWorld(data ?? Data), random: _random, time: _clock,
            loggerFactory: loggers);
    }

    /// <summary>A character at the origin wearing four pieces with one T9 affix of each of <paramref name="themes" />, joined.</summary>
    private CharacterEntity Wearer(MapInstance instance, uint id, params string[] themes) =>
        Wearer(instance, id, Vector3.zero, themes);

    private CharacterEntity Wearer(MapInstance instance, uint id, Vector3 position, params string[] themes)
    {
        CharacterEntity character = Dressed(TestCharacters.New(id, time: _clock), themes);
        character.Position = position;
        return Join(instance, character).Character;
    }

    /// <summary>
    /// <paramref name="character" /> wearing the kit, each piece with one T9 affix of each of <paramref name="themes" />
    /// (its seeded first line's low end), its stats refreshed as it enters the world, over the test's reference data.
    /// </summary>
    private CharacterEntity Dressed(CharacterEntity character, params string[] themes)
    {
        StaticData data = _data;
        ILookup<int, ItemModifierStat> lines = ItemModifierSeed.Stats().ToLookup(line => line.ModifierId);
        int[] affixes = [.. themes.Select(theme => CatalogKeys.ModifierId(s_affixOf[theme]))];
        InventoryItem[] pieces =
        [
            .. s_kit.Select(piece => TestCharacters.Item(piece.Slot, data.FindItemTemplate(new ItemTemplateId(piece.Template))!)),
        ];
        character.ItemModifiers.Load(
        [
            .. pieces.Select(piece => new ItemInstance
            {
                Identified = true,
                Id = piece.InstanceId,
                TemplateId = piece.TemplateId,
                Modifiers =
                [
                    .. affixes.Select((id, ordinal) =>
                    {
                        ItemModifierStat line = lines[id].OrderBy(l => l.Index).First();
                        return TestModifiers.Modifier(piece.InstanceId, id, (byte)ordinal, (0, line.Stat, line.Min, line.Min2));
                    }),
                ],
            }),
        ]);
        character.Container(InventoryType.Equipment).Load(pieces);
        Assert.True(CharacterStatsRefresh.Apply(character, data, CurrentValues.Refill));
        return character;
    }

    /// <summary>
    /// <paramref name="character" /> wearing an identified rare amulet (template 69) of item level 1000 whose one affix,
    /// "S5 Fire T9", is sealed at 0 (#1180): no theme counts, and its bar never fills. Its stats refreshed, which counts
    /// the seal.
    /// </summary>
    private static CharacterEntity SealedRare(CharacterEntity character)
    {
        int affix = CatalogKeys.ModifierId("S5 Fire T9");
        ItemModifierStat line = ItemModifierSeed.Stats().Where(l => l.ModifierId == affix).OrderBy(l => l.Index).First();
        InventoryItem plainAmulet = TestCharacters.Item(EquipmentSlots.Neck, Data.FindItemTemplate(new ItemTemplateId(69))!);
        InventoryItem amulet = plainAmulet with { Rarity = ItemRarity.Rare, ItemLevel = 1000 };
        ItemInstanceModifier sealedRow = TestModifiers.Modifier(amulet.InstanceId, affix, 0, (0, line.Stat, line.Min, line.Min2));
        sealedRow.SealProgress = 0;
        character.ItemModifiers.Load(
        [
            new ItemInstance { Identified = true, Id = amulet.InstanceId, TemplateId = amulet.TemplateId, Modifiers = [sealedRow] },
        ]);
        character.Container(InventoryType.Equipment).Load([amulet]);
        Assert.True(CharacterStatsRefresh.Apply(character, Data, CurrentValues.Refill));
        return character;
    }

    /// <summary><paramref name="character" /> added to the instance through a connection that allocates nothing.</summary>
    private static CharacterEntity Quiet(MapInstance instance, CharacterEntity character)
    {
        character.Spells.Load(Array.Empty<IAbility>());
        character.InstanceId = instance.InstanceId;
        instance.AddCharacter(new QuietConnection(character));
        return character;
    }

    /// <summary>A level 1 creature, as CrowdControlShould places one: its resistance from its template and the seeded rows.</summary>
    private static Creature Boar(MapInstance instance, uint id, uint health = 100, Vector3? position = null,
        CreatureTemplate? template = null, bool invulnerable = false)
    {
        template ??= Loot.LootTestData.BoarTemplate(null);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = template,
            Position = position ?? new Vector3(0f, 0f, 2f),
            Level = 1,
            Health = Math.Max(health, 1u),
            CurrentHealth = health,
            Invulnerable = invulnerable,
            ControlResistance = ControlResistance.Of(template, Data.CreatureRarityModifiers),
        };
        creature.Script = new CombatResolutionShould.CountingWoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    /// <summary>
    /// A raw hit of 10 that is not evaded or blocked, and crits only when <paramref name="crit" /> says, then exactly the
    /// Resonance chance draws in <paramref name="chance" />: every one is drawn, and nothing more.
    /// </summary>
    private void Hit(MapInstance instance, CharacterEntity wearer, IUnit target, double[] chance, bool crit = false)
    {
        int drawn = _random.DoublesDrawn;
        _random.Doubles(Miss, crit ? 0.0 : Miss, Miss).Doubles(chance);
        instance.CombatService.ApplyDamage(wearer, target, 10);
        Assert.Equal(drawn + 3 + chance.Length, _random.DoublesDrawn);
    }

    /// <summary>A helpful aura holding <paramref name="modifiers" /> for a minute, on <paramref name="character" />.</summary>
    private static void Empower(MapInstance instance, CharacterEntity character, params (AuraStat Stat, float Value)[] modifiers)
    {
        var aura = new AuraTemplate
        {
            Id = new AuraId(1180_999),
            Name = "Empowered",
            Icon = "empowered",
            Kind = AuraKind.Helpful,
            DurationMs = 60_000,
            Stacking = AuraStacking.Refresh,
            MaxStacks = 1,
            Modifiers =
            [
                .. modifiers.Select(m => new AuraStatModifier
                {
                    AuraId = new AuraId(1180_999), Stat = m.Stat, Kind = AuraModifierKind.Flat, Value = m.Value,
                }),
            ],
        };
        Assert.Equal(AuraApplyResult.Applied, instance.Auras.Apply(character, character, aura, AuraSource.None));
    }

    /// <summary>Runs the clock on a second at a time, <paramref name="seconds" /> times, each with one tick's crit roll.</summary>
    private void Tick(MapInstance instance, int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            _random.Doubles(0.0);   // a crit, were the snapshot able to crit
            _clock.Advance(TimeSpan.FromSeconds(1));
            instance.Auras.Update();
        }

        Assert.Equal(0, _random.DoublesLeft);
    }

    /// <summary>Each aura the cast holds, as "{aura} on {holder} from {caster}", by name, sorted.</summary>
    private static List<string> Holds(Dictionary<string, IUnit> cast)
    {
        var names = cast.ToDictionary(pair => pair.Value.Guid, pair => pair.Key);
        return
        [
            .. cast
                .SelectMany(pair => (AuraHolders.Of(pair.Value)?.All ?? []).Select(aura =>
                    $"{aura.Template.Name} on {pair.Key} from {names.GetValueOrDefault(aura.CasterGuid, "someone else")}"))
                .Order(StringComparer.Ordinal),
        ];
    }
}
