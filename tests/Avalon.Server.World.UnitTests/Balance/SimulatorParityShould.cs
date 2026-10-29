using System.Reflection;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Loot;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Reload;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Balance;

/// <summary>
/// The balance simulator (tools/Avalon.Balance) against the real server: the same seed, the same exchange, the same
/// numbers. A level-3 Warrior in forest gear and a level-3 Thornback Boar. Character ids 6760_1xx, creatures 6760_9xx.
/// </summary>
public class SimulatorParityShould
{
    private const int Seed = 672;
    private const ushort Level = 3;
    private static readonly Lazy<BalanceData> s_seededData = new(() => BalanceData.From(SeedSource.Load()));
    private static readonly Lazy<ScenarioFile> s_scenarios = new(() => ConfigFileStore.Load(
        Path.Combine(TownNavmesh.RepositoryRoot, "balance", "scenarios.json"), ConfigFiles.ParseScenarios));
    private static BalanceData Data => s_seededData.Value;

    /// <summary>The checked-in forest profile's Warrior gear (balance/scenarios.json).</summary>
    private static ulong[] ForestWarrior => WarriorGear("forest");

    /// <summary>A checked-in gear profile's Warrior gear; "none" is no gear.</summary>
    private static ulong[] WarriorGear(string profile) => s_scenarios.Value.GearFor(profile, CharacterClass.Warrior);

    /// <summary>
    /// Close enough to the warrior (at the origin) that Hurled Axe hits on its first step whatever its speed (9 m/s, 0.15 m
    /// a tick): the boar's 0.5 m body reaches the warrior's centre, where the axe starts.
    /// </summary>
    private static readonly Vector3 s_pointBlank = new(0f, 0f, 0.5f);

    private static byte SlotOf(ItemTemplate item) =>
        Enumerable.Range(0, EquipmentSlots.FirstReserved).Select(s => (byte)s).First(s => EquipmentSlots.TypeOf(s) == item.Slot);

    private static MapInstanceClient RealWarrior(MapInstance instance, uint id, params AbilityTemplate[] abilities) =>
        RealWarrior(instance, id, ForestWarrior, null, abilities);

    private static MapInstanceClient RealWarrior(MapInstance instance, uint id, TimeProvider? time,
        params AbilityTemplate[] abilities) =>
        RealWarrior(instance, id, ForestWarrior, time, abilities);

    private static MapInstanceClient RealWarrior(MapInstance instance, uint id, ulong[] gear, TimeProvider? time,
        params AbilityTemplate[] abilities) =>
        RealCharacter(instance, id, CharacterClass.Warrior, gear, time, abilities);

    /// <summary>
    /// A level-3 character of <paramref name="class" /> wearing <paramref name="gear" />, each piece the simulator's fixed
    /// copy of it (B-R4): its base stats and modifiers loaded as the copy's rows under the instance id the equipment holds,
    /// then a stats refresh.
    /// </summary>
    private static MapInstanceClient RealCharacter(MapInstance instance, uint id, CharacterClass @class, ulong[] gear,
        TimeProvider? time, params AbilityTemplate[] abilities)
    {
        MapInstanceClient warrior = Join(instance, Inventory.TestCharacters.New(id, time: time));
        warrior.Character.Data!.Class = @class;
        warrior.Character.PowerType = PowerType.Mana;
        warrior.Character.Level = Level;
        warrior.Character.Orientation = new Vector3(0f, 0f, 0f);
        InventoryItem[] worn = [.. gear.Select(Data.Item).Select(i => Inventory.TestCharacters.Item(SlotOf(i), i))];
        warrior.Character.ItemModifiers.Load([.. worn.Select(i => CopyOf(i, Data.Worn(i.TemplateId.Value)))]);
        warrior.Character.Container(InventoryType.Equipment).Load(worn);
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, Data.Tables.ClassLevelStats,
            tid => Data.Tables.ItemTemplates.FirstOrDefault(t => t.Id == tid),
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, Data.Combat.Formula));
        warrior.Character.Spells.Load(abilities.Select(a => (IAbility)AbilityTestData.Game(a)).ToArray());
        return warrior;
    }

    /// <summary>
    /// The rows of the fixed copy <paramref name="copy" /> under the instance <paramref name="item" /> names, as a load reads
    /// them: its base stats, and copies of its modifiers (the simulator's cached copy is never handed to the store).
    /// </summary>
    private static ItemInstance CopyOf(InventoryItem item, WornItem copy)
    {
        ItemInstanceId id = item.InstanceId;
        List<ItemInstanceBaseStat> baseStats = [];
        foreach ((ItemBaseStat stat, int value) in copy.BaseStats)
            baseStats.Add(new ItemInstanceBaseStat { ItemInstanceId = id, Stat = stat, Value = value });

        return new ItemInstance
        {
            Identified = true,
            Id = id,
            TemplateId = item.TemplateId,
            BaseStats = baseStats,
            Modifiers = [.. (copy.Modifiers ?? []).Select(m => m.CopyAs(Guid.CreateVersion7(), id, m.Ordinal, imprinted: false))],
        };
    }

    private static Creature RealBoar(MapInstance instance, uint id, SimCreature twin, Vector3? position = null)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = twin.Template,
            Position = position ?? new Vector3(0f, 0f, 1.5f),
            Level = twin.Derived.Level,
            Health = 1_000_000,
            CurrentHealth = 1_000_000,
            BaseMaxHealth = 1_000_000,
            DamageMin = twin.Derived.DamageMin,
            DamageMax = twin.Derived.DamageMax,
            Armor = twin.Derived.Armor,
            CritPct = twin.Derived.CritPct,
            Evasion = twin.Derived.Evasion,
            BlockPct = twin.Derived.BlockPct,
            DamageType = twin.Template.DamageType,
            Resistances = twin.Derived.Resistances,
            BaseWard = twin.Derived.Ward,   // fills the ward, as CreatureSpawner's
            BaseAttackTime = twin.Template.BaseAttackTime,
            AttackSpeedCap = Data.Combat.Formula.AttackSpeedCap,
            CastSpeedCap = Data.Combat.Formula.CastSpeedCap,
            CooldownRecoveryCap = Data.Combat.Formula.CooldownRecoveryCap,
        };
        creature.Script = new CombatResolutionShould.CountingWoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    private static AbilityTemplate Row(uint id) => Data.Tables.AbilityTemplates.Single(a => a.Id.Value == id);

    /// <summary>
    /// A World-side unit's combat, which is internal to Avalon.World (and has no InternalsVisibleTo), read by name:
    /// <c>Combat</c> or <c>Defence</c> on a Creature or a CharacterEntity.
    /// </summary>
    private static T Internal<T>(object unit, string property) =>
        (T)(unit.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(unit)
            ?? throw new InvalidOperationException($"{unit.GetType().Name}.{property} is not there"));

    [Theory]
    [InlineData("none")]
    [InlineData("forest")]
    public void Derive_the_same_character_stats_as_a_stats_refresh(string gear)
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out _);
        MapInstanceClient real = RealWarrior(instance, 6760_101, WarriorGear(gear), null, Row(200));

        var sim = SimPlayer.Create(Data, CharacterClass.Warrior, Level, WarriorGear(gear).Select(Data.Worn));

        Assert.Equal(real.Character.Stats, sim.Stats);
        Assert.Equal(Internal<CasterPace>(real.Character, "Pace"), sim.Pace);
        Assert.Equal(Internal<ManaRegenRates>(real.Character, "ManaRegen"), sim.ManaRegen);
        Assert.Equal(Internal<AttackerCombat>(real.Character, "Combat"), sim.Attack);
        Assert.Equal(Internal<DefenderCombat>(real.Character, "Defence"), sim.Defence);
    }

    /// <summary>
    /// B-R4's fixed copy is the world's plain roll (ItemRoller.RollPlain) under a random that always lands mid-range:
    /// every gear-profile item, over the seed's item rows and the catalog the World database's read builds, must come
    /// out of both with the same base stats and the same modifiers, line for line.
    /// </summary>
    [Fact]
    public async Task Make_the_same_fixed_copy_as_a_plain_roll_at_the_midpoint()
    {
        SeedTables seed = SeedSource.Load();
        ILookup<ulong, ItemTemplateBaseStat> ranges = seed.ItemTemplateBaseStats.ToLookup(r => r.TemplateId.Value);
        foreach (ItemTemplate template in seed.ItemTemplates)
            template.BaseStats = [.. ranges[template.Id.Value]];

        var items = new ItemsPatch(seed.ItemTemplates, await ItemModifierRepositories.SeededCatalog());
        var roller = new ItemRoller(new MidpointRandom(), new ItemIdAllocator(), Options.Create(new GameConfiguration()));
        ulong[] ids = [.. s_scenarios.Value.GearProfiles.Values.SelectMany(p => p.Values).SelectMany(g => g).Distinct().Order()];
        Assert.NotEmpty(ids);

        foreach (ulong id in ids)
        {
            RolledItem rolled = roller.RollPlain(items.Find(new ItemTemplateId(id))!, 1, items);
            WornItem worn = Data.Worn(id);

            Assert.Equal((id, rolled.BaseStats), (id, worn.BaseStats));
            List<ItemInstanceModifier> copied = worn.Modifiers ?? [];
            Assert.Equal(Ids(id, rolled.Modifiers), Ids(id, copied));
            Assert.Equal(Lines(id, rolled.Modifiers), Lines(id, copied));
        }

        static List<(ulong Item, byte Ordinal, int ModifierId)> Ids(ulong item, List<ItemInstanceModifier> modifiers) =>
            [.. modifiers.OrderBy(m => m.Ordinal).Select(m => (item, m.Ordinal, m.ModifierId))];

        static List<(ulong Item, int ModifierId, StatType Stat, int Value, int? Value2)> Lines(ulong item,
            List<ItemInstanceModifier> modifiers) =>
            [.. modifiers.OrderBy(m => m.Ordinal).SelectMany(m => m.Values.Select(v => (item, m.ModifierId, v.Stat, v.Value, v.Value2)))];
    }

    /// <summary>
    /// The creature mapping is written twice, in Creature (Combat, Defence, from the fields CreatureSpawner copies)
    /// and in DerivedCreatureStats (Attacker, Defence, which the simulator fights with). Every hostile seeded
    /// template, spawned by the real spawner at its lowest and its highest level, must fight as its twin does.
    /// </summary>
    [Fact]
    public void Spawn_every_hostile_creature_with_the_combat_the_simulator_derives()
    {
        Assert.NotEmpty(Data.HostileTemplates);

        foreach (Func<CreatureTemplate, short> level in new Func<CreatureTemplate, short>[] { t => t.MinLevel, t => t.MaxLevel })
        {
            CreatureSpawner spawner = SeededSpawner(level);

            foreach (CreatureTemplate template in Data.HostileTemplates)
            {
                var real = (Creature)spawner.Spawn(template.Id);
                var sim = SimCreature.Create(Data, template, real.Level, 0);

                Assert.Equal((ushort)Math.Max((short)1, level(template)), real.Level);
                Assert.Equal(Internal<AttackerCombat>(real, "Combat"), sim.Attack);
                Assert.Equal(Internal<DefenderCombat>(real, "Defence"), sim.Defence);
                Assert.Equal(real.Health, sim.Health);
                Assert.Equal(Internal<CasterPace>(real, "Pace"), sim.Pace);
                Assert.Equal(real.SwingInterval, sim.SwingInterval);
                Assert.Equal(real.MaxWard, sim.MaxWard);
            }
        }
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Deal_the_same_cleave_damage_and_mana_hit_for_hit()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out _, random: new CombatRandom(new Random(Seed)));
        MapInstanceClient real = RealWarrior(instance, 6760_111, Row(200));
        var simWarrior = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Worn));
        var simBoar = SimCreature.Create(Data, Data.Creature(4), Level, 0);
        simBoar.Health = simBoar.CurrentHealth = 1_000_000;
        Creature realBoar = RealBoar(instance, 6760_911, simBoar);
        IAbility realCleave = real.Character.Spells[new Avalon.Common.ValueObjects.AbilityId(200)]!;
        SimAbility simCleave = simWarrior.Ability(200);
        var simRng = new CombatRandom(new Random(Seed));

        for (int hit = 0; hit < 60; hit++)
        {
            real.Character.CurrentPower = 0;
            simWarrior.CurrentPower = 0;
            uint realBefore = realBoar.CurrentHealth;
            uint simBefore = simBoar.CurrentHealth;

            instance.CombatService.ApplyDamage(real.Character, realBoar, realCleave);
            CombatRules.Land(simBoar, CombatRules.Damage(simWarrior, simBoar, simCleave, Data.Combat.Formula, simRng),
                DateTime.UtcNow);

            Assert.Equal(realBefore - realBoar.CurrentHealth, simBefore - simBoar.CurrentHealth);
            Assert.Equal(real.Character.CurrentPower, simWarrior.CurrentPower);
        }
    }

    /// <summary>
    /// A boar's hit on a character in its class's forest gear, sixty times, through the real combat service and the
    /// simulator's B-R6 landing: the same health and the same ward after every hit. Health is refilled each hit, the ward
    /// is not, and both clocks move 5 s between hits (the 4 s recharge delay and half the 2 s recharge), so the ward drains,
    /// pauses and recharges as the world's does. A Warrior's forest gear carries a small ward (5); a Wizard's cloth ward
    /// takes Gore (physical) and none of Rotting Burst (blight).
    /// </summary>
    [Theory]
    [InlineData(CharacterClass.Warrior, 300u)]
    [InlineData(CharacterClass.Wizard, 300u)]
    [InlineData(CharacterClass.Wizard, 307u)]
    public void Take_the_same_gore_damage_and_mana_hit_for_hit(CharacterClass @class, uint ability)
    {
        var clock = new FakeTimeProvider(s_clockStart);
        using MapInstance instance = TestMapInstances.BuildCasting(out _, random: new CombatRandom(new Random(Seed)), time: clock);
        ulong[] gear = s_scenarios.Value.GearFor("forest", @class);
        MapInstanceClient real = RealCharacter(instance, 6760_121, @class, gear, clock);
        var simPlayer = SimPlayer.Create(Data, @class, Level, gear.Select(Data.Worn));
        var simBoar = SimCreature.Create(Data, Data.Creature(4), Level, 0);
        Creature realBoar = RealBoar(instance, 6760_921, simBoar);
        IAbility realHit = AbilityTestData.Game(Row(ability));
        var simHit = new SimAbility(Row(ability));
        var simRng = new CombatRandom(new Random(Seed));
        Assert.Equal(real.Character.MaxWard, simPlayer.MaxWard);

        for (int hit = 0; hit < 60; hit++)
        {
            real.Character.CurrentHealth = real.Character.Health;
            real.Character.CurrentPower = 0;
            simPlayer.CurrentHealth = simPlayer.Health;
            simPlayer.CurrentPower = 0;
            DateTime now = s_clockStart.UtcDateTime + TimeSpan.FromSeconds(5 * hit);

            instance.CombatService.ApplyDamage(realBoar, real.Character, realHit);
            CombatRules.Land(simPlayer, CombatRules.Damage(simBoar, simPlayer, simHit, Data.Combat.Formula, simRng), now);

            Assert.Equal((hit, real.Character.CurrentHealth), (hit, simPlayer.CurrentHealth));
            Assert.Equal((hit, real.Character.Ward), (hit, simPlayer.Ward));
            Assert.Equal((hit, real.Character.CurrentPower), (hit, simPlayer.CurrentPower));
            clock.Advance(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// A fight ten seconds long, tick by tick, through the real cast handler, cast system and combat service (see
    /// <see cref="Exchange" />): the server must cast exactly what the simulated Warrior casts, on the same tick,
    /// and refuse every rotation entry the simulator passed over; after every tick the boar's health, the Warrior's
    /// mana, both cooldowns and the casting state must match. Cleave (a cone) hits as it fires; Hurled Axe (a
    /// projectile) hits in the cast system's script pass, and the boar stands inside its first step, so the tick it
    /// lands on is the simulator's.
    /// </summary>
    [Fact(Skip = BalancePause.Reason)]
    public void Land_cleave_and_hurled_axe_on_the_same_ticks_through_the_cast_system()
    {
        var clock = new FakeTimeProvider(s_clockStart);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            random: new CombatRandom(new Random(Seed)), time: clock);
        MapInstanceClient real = RealWarrior(instance, 6760_151, clock, Row(200), Row(202));
        var simWarrior = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Worn));
        SimCreature simBoar = PassiveBoar(Level);
        Creature realBoar = RealBoar(instance, 6760_951, simBoar, s_pointBlank);
        CompiledRotationEntry[] rotation = [new(202, []), new(200, [])];
        var fight = new FightSimulator(Data.Combat.Formula, simWarrior, [simBoar], rotation, new CombatRandom(new Random(Seed)));

        Dictionary<uint, int> cast = Exchange(fight, rotation, instance, handler, real, s_pointBlank, ticks: 600, tick =>
        {
            Assert.True(simBoar.CurrentHealth == realBoar.CurrentHealth,
                $"tick {tick}: the boar has {realBoar.CurrentHealth} health on the server, {simBoar.CurrentHealth} simulated");
            Assert.True(simWarrior.CurrentPower == real.Character.CurrentPower,
                $"tick {tick}: the Warrior has {real.Character.CurrentPower} mana on the server, {simWarrior.CurrentPower} simulated");
        }, clock);

        Assert.True(cast[200] >= 5, $"Cleave was cast {cast[200]} times");
        Assert.True(cast[202] >= 2, $"Hurled Axe was cast {cast[202]} times");
    }

    /// <summary>
    /// A level-1 Wizard casting Flame Burst (0.6 s wind-up, 25 Mana, 5 s cooldown) through the real handler and cast
    /// system, against the simulator, Mana compared after every tick for 30 s: the cast is paid when it is taken, and
    /// regeneration carries on while it winds up and after it (#1181); the second cast waits on the Mana that comes back.
    /// The character's clock is a test clock moved to each tick's simulated time, as the simulator's is. The simulator
    /// fights in continuous combat, so the character is
    /// kept in combat every tick (MarkCombat, as each hit would); without that its combat tag would lapse 5 s after the
    /// last hit and the out-of-combat rate would apply.
    /// </summary>
    [Fact(Skip = BalancePause.Reason)]
    public void Keep_regenerating_mana_through_a_wind_up_as_a_character_entity()
    {
        const uint StartMana = 40;
        var clock = new FakeTimeProvider(s_clockStart);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            random: new CombatRandom(new Random(Seed)), time: clock);
        var row = new Avalon.Domain.Characters.Character
        {
            Id = new Avalon.Common.ValueObjects.CharacterId(6760_161),
            AccountId = new Avalon.Common.ValueObjects.AccountId(1),
            Name = "Tester6760161",
            Class = CharacterClass.Wizard,
            Level = 1,
            CreationDate = DateTime.UtcNow,
        };
        MapInstanceClient real = Join(instance, new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), clock) { Data = row });
        real.Character.PowerType = PowerType.Mana;
        Assert.True(CharacterStatsRefresh.Apply(real.Character, Data.Tables.ClassLevelStats,
            tid => Data.Tables.ItemTemplates.FirstOrDefault(t => t.Id == tid),
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, Data.Combat.Formula));
        real.Character.Spells.Load([AbilityTestData.Game(Row(211))]);
        real.Character.CurrentPower = StartMana;

        var simWizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
        simWizard.CurrentPower = StartMana;
        Assert.True(simWizard.Power >= StartMana, $"a level-1 Wizard holds {simWizard.Power} Mana");
        SimCreature simBoar = PassiveBoar(1);
        RealBoar(instance, 6760_961, simBoar);
        CompiledRotationEntry[] rotation = [new(211, [])];
        var fight = new FightSimulator(Data.Combat.Formula, simWizard, [simBoar], rotation, new CombatRandom(new Random(Seed)));

        int heldTicks = 0, regenTicks = 0;
        uint? previous = null;
        Dictionary<uint, int> cast = Exchange(fight, rotation, instance, handler, real, new Vector3(0f, 0f, 1.5f), ticks: 1800,
            tick =>
            {
                Assert.True(simWizard.CurrentPower == real.Character.CurrentPower,
                    $"tick {tick}: the Wizard has {real.Character.CurrentPower} Mana on the server, {simWizard.CurrentPower} simulated");
                if (previous is { } p && simWizard.CurrentPower > p) regenTicks++;
                else if (previous == simWizard.CurrentPower) heldTicks++;
                previous = simWizard.CurrentPower;
            },
            clock, beforeUpdate: () => real.Character.MarkCombat());

        Assert.True(cast[211] >= 2, $"Flame Burst was cast {cast[211]} times");
        Assert.True(regenTicks > 0 && heldTicks > 0, $"{regenTicks} ticks regenerated, {heldTicks} held still");
    }

    /// <summary>
    /// With a base damage coefficient, so each application draws a roll of the Warrior's main hand right after the cone's
    /// own draws: a roll drawn in a different place, or not at all, moves every later roll and the healths part. Its
    /// total per tick is not a whole number, so every tick carries a fraction of a point to the next.
    /// </summary>
    private static AuraTemplate TestBleed() => new()
    {
        Id = new Avalon.Common.ValueObjects.AuraId(9901),
        Name = "Test Bleed",
        Icon = "bleed",
        Kind = AuraKind.Harmful,
        DurationMs = 12000,
        TickIntervalMs = 3000,
        PeriodicKind = AuraPeriodicKind.Damage,
        PeriodicBase = 12f,
        PeriodicEffectiveness = 50f,
        Stacking = AuraStacking.Stack,
        MaxStacks = 3,
    };

    private static AbilityTemplate TestRend(SpellEffect effects = SpellEffect.Damage,
        DamageType type = DamageType.Physical) => new()
        {
            Id = new Avalon.Common.ValueObjects.AbilityId(9203),
            Name = "Test Rend",
            ScriptName = "ConeAbilityScript",
            Shape = AbilityShape.Cone,
            AimMode = AbilityAimMode.Movement,
            Reach = 2.5f,
            ArcDegrees = 90f,
            Cooldown = 6000,
            Effects = effects,
            Tags = AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area,
            DamageType = type,
            DamageEffectiveness = 202f,
            AuraId = new Avalon.Common.ValueObjects.AuraId(9901),
            ThreatMultiplier = 1f,
        };

    /// <summary>
    /// Auras: a Warrior casting a bleeding cone through the real handler, cast system and aura system, against the
    /// simulator, the boar's health compared after every tick for 15 s: the hit, then each bleed tick (its base damage
    /// roll at apply, its crit roll, the boar's armour and the fraction of a point carried between ticks), its stacks
    /// and its refresh, on the same ticks. The instance's clock is moved to each tick's simulated time, as the
    /// simulator's is. The simulator never falls behind (it steps every server tick), so the server's catch-up after a
    /// stall is not exercised here. Without Damage the cone deals nothing but still rolls whether it lands on the boar
    /// (#1164): a dodge draw missing on either side would move every later roll and part the bleeds. The ticks take Rend's
    /// type (B-R7): a physical bleed meets the boar's armour and passes its ward, yet pauses the ward's recharge, so the
    /// next cone finds the ward as the first left it; a fire bleed meets the boar's fire resistance and its ward.
    /// </summary>
    [Theory]
    [InlineData(SpellEffect.Damage, DamageType.Physical)]
    [InlineData(SpellEffect.Debuff, DamageType.Physical)]
    [InlineData(SpellEffect.Damage, DamageType.Fire)]
    public async Task Bleed_the_same_as_the_aura_system_tick_for_tick(SpellEffect effects, DamageType type)
    {
        var clock = new FakeTimeProvider(s_clockStart);
        StaticData world = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            abilities: () => [TestRend(effects, type)], auras: () => [TestBleed()]));
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            world: MapInstanceClients.NewWorld(world), random: new CombatRandom(new Random(Seed)), time: clock);
        MapInstanceClient real = RealWarrior(instance, 6760_171, clock, TestRend(effects, type));

        SeedTables seed = SeedSource.Load();
        seed.AuraTemplates.Add(TestBleed());
        seed.AbilityTemplates.Add(TestRend(effects, type));
        seed.CreatureTemplates.Single(t => t.Id.Value == 4).WardModifier = 1f;   // a boar with a ward on both sides
        var data = BalanceData.From(seed);
        var simWarrior = SimPlayer.Create(data, CharacterClass.Warrior, Level, ForestWarrior.Select(data.Worn));
        simWarrior.Abilities.Add(new SimAbility(TestRend(effects, type)));
        Assert.True(simWarrior.Attack.Weapon.Max1 > 0, "the Warrior has no main hand to roll");
        SimCreature simBoar = PassiveBoar(Level, data);
        Assert.True(simBoar.MaxWard > 0, "the boar has no ward");
        Creature realBoar = RealBoar(instance, 6760_971, simBoar);
        CompiledRotationEntry[] rotation = [new(9203, [])];
        var fight = new FightSimulator(Data.Combat.Formula, simWarrior, [simBoar], rotation,
            new CombatRandom(new Random(Seed)), data: data);
        bool carried = false;

        Dictionary<uint, int> cast = Exchange(fight, rotation, instance, handler, real, s_pointBlank, ticks: 900, tick =>
            {
                Assert.True(simBoar.CurrentHealth == realBoar.CurrentHealth,
                    $"tick {tick}: the boar has {realBoar.CurrentHealth} health on the server, {simBoar.CurrentHealth} simulated");
                Assert.True(simBoar.Auras.Count == realBoar.Auras.Count,
                    $"tick {tick}: the boar holds {realBoar.Auras.Count} auras on the server, {simBoar.Auras.Count} simulated");
                if (simBoar.Auras is [{ } simBleed])
                {
                    ActiveAura realBleed = Assert.Single(realBoar.Auras.All);
                    Assert.Equal(realBleed.Stacks, simBleed.Stacks);
                    Assert.Equal(realBleed.Schedule, simBleed.Schedule);
                    Assert.Equal(realBleed.PeriodicCarry, simBleed.PeriodicCarry);
                    carried |= simBleed.PeriodicCarry > 0d;
                }
            },
            clock);

        Assert.True(cast[9203] >= 3, $"the bleeding cone was cast {cast[9203]} times");
        Assert.Equal(3u, Assert.Single(realBoar.Auras.All).Stacks);
        Assert.True(carried, "no bleed tick carried a fraction of a point");
        Assert.True(fight.Result().DamageDealt["Test Bleed"] > 0, "the bleed never ticked");
    }

    /// <summary>
    /// Auras from a creature: the boar's Gore carrying a poison scaled by a roll of its natural damage (a creature has no
    /// damage stats), cast once through the real cast system and the aura system and once by the simulated boar, the
    /// Warrior's health and mana compared after every tick for 10 s: the hit and its draws, the poison's roll right
    /// after them, each tick's crit roll, the Warrior's armour, the carry, and the mana no tick gives (#1181).
    /// </summary>
    [Fact]
    public async Task Poison_from_a_creatures_natural_damage_the_same_as_the_aura_system_tick_for_tick()
    {
        // Seed rolls the bottom of the boar's range for the poison, which a roll left out would match; this one does not.
        const int PoisonSeed = 676;

        AuraTemplate poison = new()
        {
            Id = new Avalon.Common.ValueObjects.AuraId(9902),
            Name = "Test Poison",
            Icon = "poison",
            Kind = AuraKind.Harmful,
            DurationMs = 9000,
            TickIntervalMs = 3000,
            PeriodicKind = AuraPeriodicKind.Damage,
            PeriodicBase = 3.5f,
            PeriodicEffectiveness = 100f,
            Stacking = AuraStacking.Refresh,
            MaxStacks = 1,
        };
        SeedTables seed = SeedSource.Load();
        AbilityTemplate gore = seed.AbilityTemplates.Single(a => a.Id.Value == 300);
        gore.AuraId = poison.Id;
        seed.AuraTemplates.Add(poison);
        var data = BalanceData.From(seed);

        var clock = new FakeTimeProvider(s_clockStart);
        StaticData world = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            abilities: () => [gore], auras: () => [poison]));
        using MapInstance instance = TestMapInstances.BuildCasting(out _,
            world: MapInstanceClients.NewWorld(world), random: new CombatRandom(new Random(PoisonSeed)), time: clock);
        MapInstanceClient real = RealWarrior(instance, 6760_191, clock, Row(200));

        var simWarrior = SimPlayer.Create(data, CharacterClass.Warrior, Level, ForestWarrior.Select(data.Worn));
        var simBoar = SimCreature.Create(data, data.Creature(4), Level, 0);
        simBoar.Health = simBoar.CurrentHealth = 1_000_000;
        foreach (SimAbility special in simBoar.Specials) special.CooldownLeft = 1_000f;   // only Gore, once
        Assert.True(simBoar.Attack.Weapon.Max1 > 0, "the boar has no natural damage to roll");
        Creature realBoar = RealBoar(instance, 6760_991, simBoar);
        var fight = new FightSimulator(Data.Combat.Formula, simWarrior, [simBoar], [], new CombatRandom(new Random(PoisonSeed)),
            data: data);
        IAbility realGore = AbilityTestData.Game(gore);
        bool carried = false;

        for (int tick = 0; tick < 600; tick++)
        {
            clock.SetUtcNow(s_clockStart + Ticks(tick));
            fight.Tick();
            if (tick == 0)
            {
                // The simulated boar swung Gore in this tick's creature phase; the real one swings it now, before the
                // instance ticks: the same draws, in the same order, at the same time on the aura clock.
                Assert.True(((ISimulationContext)instance).RunInstantAbility(realBoar,
                    new AbilityAim(new Vector3(0f, 0f, -1f), null), realGore));
                simBoar.Basic!.CooldownLeft = 1_000f;
                AuraSnapshot snapshot = Assert.Single(real.Character.Auras.All).Snapshot;
                Assert.Equal(snapshot, Assert.Single(simWarrior.Auras).Snapshot);
                // A roll above the bottom of the range, so a roll left out (or drawn elsewhere) would show.
                Assert.True(snapshot.PerTickPerStack > (3.5f + simBoar.Attack.Weapon.Min1) / 3f + 1e-4f,
                    $"the poison rolled the bottom of the boar's range: {snapshot.PerTickPerStack} a tick");
            }

            instance.Update(s_oneTick);

            Assert.True(simWarrior.CurrentHealth == real.Character.CurrentHealth,
                $"tick {tick}: the Warrior has {real.Character.CurrentHealth} health on the server, {simWarrior.CurrentHealth} simulated");
            Assert.True(simWarrior.CurrentPower == real.Character.CurrentPower,
                $"tick {tick}: the Warrior has {real.Character.CurrentPower} mana on the server, {simWarrior.CurrentPower} simulated");
            Assert.Equal(real.Character.Auras.Count, simWarrior.Auras.Count);
            if (simWarrior.Auras is [{ } simPoison])
            {
                Assert.Equal(Assert.Single(real.Character.Auras.All).PeriodicCarry, simPoison.PeriodicCarry);
                carried |= simPoison.PeriodicCarry > 0d;
            }
        }

        Assert.Empty(real.Character.Auras.All);   // its three ticks paid, it expired
        Assert.True(carried, "no poison tick carried a fraction of a point");
        Assert.True(fight.Result().DamageTaken["Test Poison"] > 0, "the poison never ticked");
    }

    /// <summary>
    /// Auras: a bleeding cone, whose hits and bleed ticks give no mana (#1181), through the real handler, cast system
    /// and aura system against the simulator, the boar's health and the Warrior's mana compared after every tick for 15 s.
    /// </summary>
    [Fact(Skip = BalancePause.Reason)]
    public async Task Gain_no_mana_from_a_bleeding_cone_or_its_ticks_as_the_aura_system()
    {
        var clock = new FakeTimeProvider(s_clockStart);
        StaticData world = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            abilities: () => [TestRend()], auras: () => [TestBleed()]));
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            world: MapInstanceClients.NewWorld(world), random: new CombatRandom(new Random(Seed)), time: clock);
        MapInstanceClient real = RealWarrior(instance, 6760_172, clock, TestRend());

        SeedTables seed = SeedSource.Load();
        seed.AuraTemplates.Add(TestBleed());
        seed.AbilityTemplates.Add(TestRend());
        var data = BalanceData.From(seed);
        var simWarrior = SimPlayer.Create(data, CharacterClass.Warrior, Level, ForestWarrior.Select(data.Worn));
        simWarrior.Abilities.Add(new SimAbility(TestRend()));
        SimCreature simBoar = PassiveBoar(Level);
        Creature realBoar = RealBoar(instance, 6760_972, simBoar);
        CompiledRotationEntry[] rotation = [new(9203, [])];
        var fight = new FightSimulator(Data.Combat.Formula, simWarrior, [simBoar], rotation,
            new CombatRandom(new Random(Seed)), data: data);

        Dictionary<uint, int> cast = Exchange(fight, rotation, instance, handler, real, s_pointBlank, ticks: 900, tick =>
            {
                Assert.True(simBoar.CurrentHealth == realBoar.CurrentHealth,
                    $"tick {tick}: the boar has {realBoar.CurrentHealth} health on the server, {simBoar.CurrentHealth} simulated");
                Assert.True(simWarrior.CurrentPower == real.Character.CurrentPower,
                    $"tick {tick}: the Warrior has {real.Character.CurrentPower} mana on the server, {simWarrior.CurrentPower} simulated");
            },
            clock);

        Assert.True(cast[9203] >= 3, $"the bleeding cone was cast {cast[9203]} times");
        Assert.Equal(real.Character.Power, real.Character.CurrentPower);   // the free cone neither took nor gave mana
    }

    /// <summary>
    /// Auras: a level-3 Cleric at half health casting the seeded Renew (233, an ally circle that heals nothing directly)
    /// on itself through the real handler, cast system and aura system, against the simulator, health and Mana compared
    /// after every tick for 20 s: each heal tick (its crit roll and the fraction of a point carried), the refresh on each
    /// recast, and the Mana it costs. The Cleric is kept in combat every tick, as the simulator's continuous fight is.
    /// </summary>
    [Fact(Skip = BalancePause.Reason)]
    public async Task Heal_over_time_the_same_as_the_aura_system_tick_for_tick()
    {
        AbilityTemplate renew = Row(233);
        AuraTemplate renewAura = Data.Tables.AuraTemplates.Single(a => a.Id.Value == renew.AuraId!.Value);
        Assert.Equal(AuraPeriodicKind.Heal, renewAura.PeriodicKind);
        var clock = new FakeTimeProvider(s_clockStart);
        StaticData world = await TestStaticData.LoadAsync(TestStaticData.Repositories(
            abilities: () => [renew], auras: () => [renewAura]));
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            world: MapInstanceClients.NewWorld(world), random: new CombatRandom(new Random(Seed)), time: clock);
        var row = new Avalon.Domain.Characters.Character
        {
            Id = new Avalon.Common.ValueObjects.CharacterId(6760_173),
            AccountId = new Avalon.Common.ValueObjects.AccountId(1),
            Name = "Tester6760173",
            Class = CharacterClass.Cleric,
            Level = Level,
            CreationDate = DateTime.UtcNow,
        };
        MapInstanceClient real = Join(instance, new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), clock) { Data = row });
        real.Character.PowerType = PowerType.Mana;
        Assert.True(CharacterStatsRefresh.Apply(real.Character, Data.Tables.ClassLevelStats,
            tid => Data.Tables.ItemTemplates.FirstOrDefault(t => t.Id == tid),
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.Refill, Data.Combat.Formula));
        real.Character.Spells.Load([AbilityTestData.Game(renew)]);

        var simCleric = SimPlayer.Create(Data, CharacterClass.Cleric, Level, []);
        Assert.Equal(real.Character.Health, simCleric.Health);
        Assert.Equal(real.Character.CurrentPower, simCleric.CurrentPower);
        real.Character.CurrentHealth = simCleric.CurrentHealth = simCleric.Health / 2;
        SimCreature simBoar = PassiveBoar(Level);
        RealBoar(instance, 6760_973, simBoar);
        CompiledRotationEntry[] rotation = [new(233, [])];
        var fight = new FightSimulator(Data.Combat.Formula, simCleric, [simBoar], rotation,
            new CombatRandom(new Random(Seed)), data: Data);
        bool carried = false;

        Dictionary<uint, int> cast = Exchange(fight, rotation, instance, handler, real, s_pointBlank, ticks: 1200, tick =>
            {
                Assert.True(simCleric.CurrentHealth == real.Character.CurrentHealth,
                    $"tick {tick}: the Cleric has {real.Character.CurrentHealth} health on the server, {simCleric.CurrentHealth} simulated");
                Assert.True(simCleric.CurrentPower == real.Character.CurrentPower,
                    $"tick {tick}: the Cleric has {real.Character.CurrentPower} Mana on the server, {simCleric.CurrentPower} simulated");
                Assert.Equal(real.Character.Auras.Count, simCleric.Auras.Count);
                if (simCleric.Auras is [{ } simRenew])
                {
                    ActiveAura realRenew = Assert.Single(real.Character.Auras.All);
                    Assert.Equal(realRenew.Schedule, simRenew.Schedule);
                    Assert.Equal(realRenew.PeriodicCarry, simRenew.PeriodicCarry);
                    carried |= simRenew.PeriodicCarry > 0d;
                }
            },
            clock, beforeUpdate: () => real.Character.MarkCombat());

        Assert.True(cast[233] >= 3, $"Renew was cast {cast[233]} times");
        Assert.True(carried, "no heal tick carried a fraction of a point");
        Assert.True(fight.Result().Healing > 0, "Renew never healed");
        Assert.True(real.Character.CurrentHealth > simCleric.Health / 2, "the Cleric gained no health");
    }

    /// <summary>Auras: a percentage armour and a flat crit aura fold into the same derived stats on the server and in the simulator.</summary>
    [Fact(Skip = BalancePause.Reason)]
    public void Fold_a_stat_aura_into_the_same_stats_as_a_stats_refresh()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out _);
        MapInstanceClient real = RealWarrior(instance, 6760_181, Row(200));
        AuraTemplate ward = new()
        {
            Id = new Avalon.Common.ValueObjects.AuraId(9905),
            Name = "Test Ward",
            Icon = "ward",
            Kind = AuraKind.Helpful,
            DurationMs = 30000,
            Stacking = AuraStacking.Refresh,
            MaxStacks = 1,
            Modifiers =
            [
                new AuraStatModifier { AuraId = new Avalon.Common.ValueObjects.AuraId(9905), Stat = AuraStat.Armor, Kind = AuraModifierKind.Increased, Value = 20f },
                new AuraStatModifier { AuraId = new Avalon.Common.ValueObjects.AuraId(9905), Stat = AuraStat.CritPct, Kind = AuraModifierKind.Flat, Value = 3f },
                new AuraStatModifier { AuraId = new Avalon.Common.ValueObjects.AuraId(9905), Stat = AuraStat.MaxHealth, Kind = AuraModifierKind.Flat, Value = 25f },
            ],
        };
        real.Character.Auras.Add(new ActiveAura(ward, real.Character.Guid, AuraSource.None, 1, default,
            AuraSchedule.Start(s_clockStart, 30000, 0), 30000, s_clockStart.UtcDateTime), s_clockStart);
        Assert.True(CharacterStatsRefresh.Apply(real.Character, Data.Tables.ClassLevelStats,
            tid => Data.Tables.ItemTemplates.FirstOrDefault(t => t.Id == tid),
            ResonanceTable.Empty, ResonanceEffectCatalog.Empty, CurrentValues.KeepShare, Data.Combat.Formula));

        var sim = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Worn));
        sim.Auras.Add(new SimAura(ward, sim, 1, default, AuraSchedule.Start(s_clockStart, 30000, 0)));
        sim.ApplyAuraStats(Data, s_clockStart.UtcDateTime);

        Assert.Equal(real.Character.Stats, sim.Stats);
        Assert.Equal(Internal<CasterPace>(real.Character, "Pace"), sim.Pace);
        Assert.Equal(Internal<ManaRegenRates>(real.Character, "ManaRegen"), sim.ManaRegen);
        Assert.Equal(Internal<AttackerCombat>(real.Character, "Combat"), sim.Attack);
        Assert.Equal(Internal<DefenderCombat>(real.Character, "Defence"), sim.Defence);
        Assert.Equal(real.Character.Health, sim.Health);
        Assert.Equal(real.Character.CurrentHealth, sim.CurrentHealth);
        Assert.Equal(real.Character.CurrentPower, sim.CurrentPower);
    }

    /// <summary>
    /// A level-<paramref name="level" /> Thornback Boar that never swings, with the health to outlast every exchange, its
    /// base maximum the same as RealBoar's, so a stat aura folds its health as the server's does. From
    /// <paramref name="data" /> when given (a test's own seed), else the seed.
    /// </summary>
    private static SimCreature PassiveBoar(ushort level, BalanceData? data = null)
    {
        data ??= Data;
        var boar = SimCreature.Create(data, data.Creature(4), level, 0);
        boar.Health = boar.CurrentHealth = boar.BaseMaxHealth = 1_000_000;
        foreach (SimAbility a in boar.Abilities) a.CooldownLeft = 1_000f;   // the real boar's script never casts
        return boar;
    }

    private static readonly DateTimeOffset s_clockStart = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary><paramref name="ticks" /> server ticks as a TimeSpan, rounded to its 100 ns units so whole seconds stay exact.</summary>
    private static TimeSpan Ticks(long ticks) =>
        TimeSpan.FromTicks((long)Math.Round(ticks * (double)TimeSpan.TicksPerSecond * FightSimulator.StepSeconds));

    /// <summary>
    /// The tick the real side is given: the TimeSpan nearest 1/60 s, 166667 of its 100 ns units (0.0166667 s). A
    /// TimeSpan cannot hold 1/60 s; TimeSpan.FromSeconds truncates it to 166666 (0.0166666 s), which is far enough
    /// short that a 0.6 s wind-up fires a tick later on the server than at the simulator's (float)(1/60) =
    /// 0.016666668 s, an artefact of the test's clock and not of either side.
    /// </summary>
    private static readonly TimeSpan s_oneTick = Ticks(1);

    /// <summary>
    /// How far a real cooldown may sit from the simulated one. The real container subtracts (float)OneTick.TotalSeconds
    /// = 0.0166667 s each tick and the simulator 0.016666668 s: 3.1e-8 s a tick, under 1e-5 s over the longest seeded
    /// player cooldown (5 s, 300 ticks), plus float rounding of the running value. 1e-4 s covers that and is under a
    /// hundredth of a tick, so a cooldown one tick off (0.0167 s) fails.
    /// </summary>
    private const float CooldownTolerance = 1e-4f;

    /// <summary>
    /// Drives the simulator and the real instance side by side, one tick at a time. Each tick the simulator ticks
    /// first; then every entry of its rotation, in order, is offered to the real handler. The instance, handler and
    /// character share <paramref name="clock" />, moved only to each tick's simulated time; the handler owns the last
    /// cast start, so scheduling pauses cannot advance the global cooldown. The entry the simulator started this tick
    /// must be accepted; every entry before it, or every
    /// entry when it started nothing, must be refused, so the server can start neither later nor earlier than the
    /// simulator. Then the instance ticks, and each ability's cooldown and the casting state must match.
    /// </summary>
    private static Dictionary<uint, int> Exchange(FightSimulator fight, IReadOnlyList<CompiledRotationEntry> rotation,
        MapInstance instance, CastAbilityHandler handler, MapInstanceClient real, Vector3 aim, int ticks,
        Action<int> afterTick, FakeTimeProvider clock, Action? beforeUpdate = null)
    {
        SimPlayer sim = fight.Player;
        var groundPos = new Vector3Dto { X = aim.x, Y = aim.y, Z = aim.z };
        var casts = rotation.ToDictionary(e => e.AbilityId, _ => 0);

        for (int tick = 0; tick < ticks; tick++)
        {
            clock.SetUtcNow(s_clockStart + Ticks(tick));
            double now = tick * FightSimulator.StepSeconds;
            int castsBefore = fight.Result().Casts.Count;
            fight.Tick();

            // An instant cast shows as a cast event started this tick; a wind-up as the pending cast it started.
            uint? started = fight.Result().Casts.Skip(castsBefore)
                .Where(c => c.Caster == sim.Name && Math.Abs(c.StartSeconds - now) < 1e-9)
                .Select(c => (uint?)c.AbilityId)
                .SingleOrDefault() ?? (sim.Casting is { } pending && Math.Abs(pending.Started - now) < 1e-9 ? pending.Ability.Id : null);

            foreach (CompiledRotationEntry entry in rotation)
            {
                int sentBefore = real.Sent.Count;
                handler.Execute(real.Connection, new CCastAbilityPacket { AbilityId = entry.AbilityId, GroundPos = groundPos });
                int refused = real.Sent.Skip(sentBefore).Count(p => p.Header.Type == NetworkPacketType.SMSG_ABILITY_NOT_READY);

                if (entry.AbilityId == started)
                {
                    Assert.True(refused == 0, $"tick {tick}: the server refused ability {entry.AbilityId}, which the simulator cast");
                    casts[entry.AbilityId]++;
                    break;
                }

                Assert.True(refused == 1, $"tick {tick}: the server cast ability {entry.AbilityId}, which the simulator did not");
            }

            beforeUpdate?.Invoke();
            instance.Update(s_oneTick);

            foreach (CompiledRotationEntry entry in rotation)
            {
                float realCooldown = real.Character.Spells[new Avalon.Common.ValueObjects.AbilityId(entry.AbilityId)]!.CooldownTimer;
                float simCooldown = sim.Ability(entry.AbilityId).CooldownLeft;
                Assert.True(Math.Abs(realCooldown - simCooldown) <= CooldownTolerance,
                    $"tick {tick}: ability {entry.AbilityId} has {realCooldown} s of cooldown on the server, {simCooldown} s simulated");
            }

            Assert.True(real.Character.Spells.IsCasting == sim.Casting is not null,
                $"tick {tick}: casting on the server {real.Character.Spells.IsCasting}, simulated {sim.Casting is not null}");
            afterTick(tick);
        }

        return casts;
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Set_the_same_cooldown_when_cleave_fires()
    {
        var clock = new FakeTimeProvider(s_clockStart);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler, time: clock);
        MapInstanceClient real = RealWarrior(instance, 6760_131, clock, Row(200));
        real.Character.LastCastStartTime = clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
        var sim = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Worn));

        handler.Execute(real.Connection, new CCastAbilityPacket { AbilityId = 200 });

        IAbility cleave = real.Character.Spells[new Avalon.Common.ValueObjects.AbilityId(200)]!;
        Assert.Equal(cleave.CooldownTimer, CombatRules.CooldownAfterFire(sim, sim.Ability(200)));
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Swing_on_the_same_interval_as_a_spawned_creature()
    {
        var sim = SimCreature.Create(Data, Data.Creature(4), Level, 0);
        var real = new Creature { BaseAttackTime = sim.Template.BaseAttackTime, AttackSpeedCap = Data.Combat.Formula.AttackSpeedCap };

        Assert.Equal(real.SwingInterval, sim.SwingInterval);
        Assert.Equal(real.SwingInterval, CombatRules.CooldownAfterFire(sim, sim.Basic!));
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Regenerate_the_same_mana_in_combat_as_a_character_entity()
    {
        // The simulated Wizard casts Arcane Bolt (instant and free) at a boar that never swings.
        var sim = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
        sim.CurrentPower = 10;
        var dummy = SimCreature.Create(Data, Data.Creature(4), 1, 0);
        dummy.Health = dummy.CurrentHealth = 1_000_000;
        foreach (SimAbility a in dummy.Abilities) a.CooldownLeft = 1_000f;
        var fight = new FightSimulator(Data.Combat.Formula, sim, [dummy], [new CompiledRotationEntry(210, [])],
            CombatRandom.Steady);

        var row = new Avalon.Domain.Characters.Character { Id = 6760_141u, Health = (int)sim.Health, Power1 = (int)sim.Power!.Value };
        var real = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)))
        {
            Data = row,
            PowerType = PowerType.Mana,
        };
        real.ApplyStats(sim.Stats, CurrentValues.EnterWorld, Data.Combat.Formula);
        real.CurrentPower = 10;
        real.Spells.Load(Array.Empty<IAbility>());
        real.MarkCombat();

        for (int tick = 0; tick < 600; tick++)
        {
            fight.Tick();
            real.Update(s_oneTick);
            Assert.True(real.CurrentPower == sim.CurrentPower,
                $"tick {tick}: {real.CurrentPower} Mana on the server, {sim.CurrentPower} simulated");
        }

        Assert.Equal(10u + (uint)Math.Floor(sim.ManaRegen.InCombat * 10 + 1e-3), sim.CurrentPower);   // 10 s at 1 % a second
    }

    /// <summary>
    /// The real CreatureSpawner over a StaticData loaded from the seed the simulator reads (a copy of its own), with
    /// every creature template pinned to the level <paramref name="level" /> picks from it, so the spawn's roll has
    /// one outcome.
    /// </summary>
    private static CreatureSpawner SeededSpawner(Func<CreatureTemplate, short> level)
    {
        SeedTables seed = SeedSource.Load();
        foreach (CreatureTemplate template in seed.CreatureTemplates)
        {
            short pinned = level(template);
            template.MinLevel = pinned;
            template.MaxLevel = pinned;
        }

        ICreatureTemplateRepository templates = Substitute.For<ICreatureTemplateRepository>();
        templates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(seed.CreatureTemplates);
        ICreatureBaseStatRepository baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>()).Returns(seed.CreatureBaseStats);
        ICreatureRarityModifierRepository rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>()).Returns(seed.CreatureRarityModifiers);
        ICombatDataRepository combat = Substitute.For<ICombatDataRepository>();
        combat.GetFormulasAsync(Arg.Any<CancellationToken>()).Returns(seed.CombatFormulas);

        IDialogueRepository dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<DialogueNode>());
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<DialogueOption>());
        ILocalizedTextRepository text = Substitute.For<ILocalizedTextRepository>();
        text.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LocalizedText>());
        text.GetAllLocalesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LocalizedTextLocale>());
        text.GetAllClassNamesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterClassName>());
        ICharacterCreateInfoRepository createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterCreateInfo>());
        IClassLevelStatRepository classStats = Substitute.For<IClassLevelStatRepository>();
        classStats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClassLevelStat>());
        IItemTemplateRepository items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ItemTemplate>());
        IAbilityTemplateRepository abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<AbilityTemplate>());
        ICharacterLevelExperienceRepository levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterLevelExperience>());

        var data = new StaticData(createInfos, classStats, items, ItemModifierRepositories.Empty(), abilities, levels, templates, baseStats, rarities,
            text, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance, combatDataRepository: combat);
        data.LoadAsync().GetAwaiter().GetResult();

        IWorld world = Substitute.For<IWorld>();
        world.Data.Returns(data);
        return new CreatureSpawner(NullLoggerFactory.Instance, world);
    }

    /// <summary>A loot random that lands mid-range: <c>(lo + hi) / 2</c> of an integer range, 0.5 of a double.</summary>
    private sealed class MidpointRandom : ILootRandom
    {
        public double NextDouble() => 0.5;

        public long NextInt64(long minInclusive, long maxExclusive) => (minInclusive + maxExclusive) / 2;
    }
}
