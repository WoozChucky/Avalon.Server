using System.Reflection;
using Avalon.Balance.Config;
using Avalon.Balance.Data;
using Avalon.Balance.Simulation;
using Avalon.Common;
using Avalon.Common.Mathematics;
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
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Configuration;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
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
    private static readonly ulong[] ForestWarrior = [7, 12, 13, 14, 15, 16];
    private static readonly Lazy<BalanceData> SeededData = new(() => BalanceData.From(SeedTables.Read()));
    private static BalanceData Data => SeededData.Value;

    /// <summary>Close enough to the warrior (at the origin) that Hurled Axe's first step, 18 m/s for one tick, reaches it.</summary>
    private static readonly Vector3 PointBlank = new(0f, 0f, 0.5f);

    private static ushort SlotOf(ItemTemplate item) =>
        Enumerable.Range(0, EquipmentSlots.FirstReserved).Select(s => (ushort)s).First(s => EquipmentSlots.TypeOf(s) == item.Slot);

    private static MapInstanceClient RealWarrior(MapInstance instance, uint id, params AbilityTemplate[] abilities)
    {
        MapInstanceClient warrior = Join(instance, Inventory.TestCharacters.New(id));   // a Warrior
        warrior.Character.PowerType = PowerType.Fury;
        warrior.Character.Level = Level;
        warrior.Character.Orientation = new Vector3(0f, 0f, 0f);
        warrior.Character.Container(InventoryType.Equipment)
            .Load(ForestWarrior.Select(Data.Item).Select(i => Inventory.TestCharacters.Item(SlotOf(i), i)).ToArray());
        Assert.True(CharacterStatsRefresh.Apply(warrior.Character, Data.Tables.ClassLevelStats, Data.Combat.Factors,
            tid => Data.Tables.ItemTemplates.FirstOrDefault(t => t.Id == tid), CurrentValues.Refill, Data.Combat.Formula));
        warrior.Character.Spells.Load(abilities.Select(a => (IAbility)AbilityTestData.Game(a)).ToArray());
        return warrior;
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
            DamageMin = twin.Derived.DamageMin,
            DamageMax = twin.Derived.DamageMax,
            Armor = twin.Derived.Armor,
            CritPct = twin.Derived.CritPct,
            DodgePct = twin.Derived.DodgePct,
            BlockPct = twin.Derived.BlockPct,
            BaseAttackTime = twin.Template.BaseAttackTime,
            HasteCap = Data.Combat.Formula.HasteCap,
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

    [Fact]
    public void Derive_the_same_character_stats_as_a_stats_refresh()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out _);
        MapInstanceClient real = RealWarrior(instance, 6760_101, Row(200));

        SimPlayer sim = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Item));

        Assert.Equal(real.Character.Stats, sim.Stats);
        Assert.Equal(real.Character.EffectiveHastePct, sim.HastePct);
        Assert.Equal(real.Character.RegenStat, sim.RegenStat);
        Assert.Equal(Internal<AttackerCombat>(real.Character, "Combat"), sim.Attack);
        Assert.Equal(Internal<DefenderCombat>(real.Character, "Defence"), sim.Defence);
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
                SimCreature sim = SimCreature.Create(Data, template, real.Level, 0);

                Assert.Equal((ushort)Math.Max((short)1, level(template)), real.Level);
                Assert.Equal(Internal<AttackerCombat>(real, "Combat"), sim.Attack);
                Assert.Equal(Internal<DefenderCombat>(real, "Defence"), sim.Defence);
                Assert.Equal(real.Health, sim.Health);
                Assert.Equal(real.HasteCap, sim.HasteCap);
                Assert.Equal(real.SwingInterval, sim.SwingInterval);
            }
        }
    }

    [Fact]
    public void Deal_the_same_cleave_damage_and_fury_hit_for_hit()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out _, random: new CombatRandom(new Random(Seed)));
        MapInstanceClient real = RealWarrior(instance, 6760_111, Row(200));
        SimPlayer simWarrior = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Item));
        SimCreature simBoar = SimCreature.Create(Data, Data.Creature(4), Level, 0);
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

            instance.CombatService.ApplyDamage(real.Character, realBoar, realCleave.Metadata.EffectValue, realCleave);
            (uint damage, _) = CombatRules.Damage(simWarrior, simBoar, simCleave, Data.Combat.Formula, simRng);
            CombatRules.HitCreature(simWarrior, simBoar, damage, simCleave);

            Assert.Equal(realBefore - realBoar.CurrentHealth, simBefore - simBoar.CurrentHealth);
            Assert.Equal(real.Character.CurrentPower, simWarrior.CurrentPower);
        }
    }

    [Fact]
    public void Take_the_same_gore_damage_and_fury_hit_for_hit()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out _, random: new CombatRandom(new Random(Seed)));
        MapInstanceClient real = RealWarrior(instance, 6760_121, Row(200));
        SimPlayer simWarrior = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Item));
        SimCreature simBoar = SimCreature.Create(Data, Data.Creature(4), Level, 0);
        Creature realBoar = RealBoar(instance, 6760_921, simBoar);
        IAbility realGore = AbilityTestData.Game(Row(300));
        var simRng = new CombatRandom(new Random(Seed));

        for (int hit = 0; hit < 60; hit++)
        {
            real.Character.CurrentHealth = real.Character.Health;
            real.Character.CurrentPower = 0;
            simWarrior.CurrentHealth = simWarrior.Health;
            simWarrior.CurrentPower = 0;

            instance.CombatService.ApplyDamage(realBoar, real.Character, realGore.Metadata.EffectValue, realGore);
            (uint damage, _) = CombatRules.Damage(simBoar, simWarrior, simBoar.Basic!, Data.Combat.Formula, simRng);
            CombatRules.HitPlayer(simWarrior, damage);

            Assert.Equal(real.Character.CurrentHealth, simWarrior.CurrentHealth);
            Assert.Equal(real.Character.CurrentPower, simWarrior.CurrentPower);
        }
    }

    /// <summary>
    /// A fight ten seconds long, tick by tick, through the real cast handler, cast system and combat service: every
    /// cast the simulated Warrior starts is sent to the real handler on the same tick, which must accept it (so the
    /// cooldowns, the costs and the Fury agree), and after every tick the boar's health and the Warrior's Fury must
    /// match. Cleave (a cone) hits as it fires; Hurled Axe (a projectile) hits in the cast system's script pass, and
    /// the boar stands inside its first step, so the tick it lands on is the simulator's.
    /// </summary>
    [Fact]
    public void Land_cleave_and_hurled_axe_on_the_same_ticks_through_the_cast_system()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            random: new CombatRandom(new Random(Seed)));
        MapInstanceClient real = RealWarrior(instance, 6760_151, Row(200), Row(202));
        SimPlayer simWarrior = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Item));
        SimCreature simBoar = SimCreature.Create(Data, Data.Creature(4), Level, 0);
        simBoar.Health = simBoar.CurrentHealth = 1_000_000;
        foreach (SimAbility a in simBoar.Abilities) a.CooldownLeft = 1_000f;   // the real boar's script never casts
        Creature realBoar = RealBoar(instance, 6760_951, simBoar, PointBlank);
        var fight = new FightSimulator(Data.Combat.Formula, simWarrior, [simBoar],
            [new CompiledRotationEntry(202, []), new CompiledRotationEntry(200, [])], new CombatRandom(new Random(Seed)));
        var aimAtBoar = new Vector3Dto { X = PointBlank.x, Y = PointBlank.y, Z = PointBlank.z };
        var cast = new Dictionary<uint, int> { [200] = 0, [202] = 0 };

        for (int tick = 0; tick < 600; tick++)
        {
            int before = fight.Result().Casts.Count;
            fight.Tick();

            foreach (CastEvent started in fight.Result().Casts.Skip(before).Where(c => c.Caster == simWarrior.Name))
            {
                // The simulator keeps the global cooldown on its tick clock; the handler's is the wall clock.
                real.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-1);
                handler.Execute(real.Connection, new CCastAbilityPacket { AbilityId = started.AbilityId, GroundPos = aimAtBoar });
                cast[started.AbilityId]++;
            }

            instance.Update(TimeSpan.FromSeconds(FightSimulator.StepSeconds));

            Assert.Empty(real.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
            Assert.True(simBoar.CurrentHealth == realBoar.CurrentHealth,
                $"tick {tick}: the boar has {realBoar.CurrentHealth} health on the server, {simBoar.CurrentHealth} simulated");
            Assert.True(simWarrior.CurrentPower == real.Character.CurrentPower,
                $"tick {tick}: the Warrior has {real.Character.CurrentPower} Fury on the server, {simWarrior.CurrentPower} simulated");
        }

        Assert.True(cast[200] >= 5, $"Cleave was cast {cast[200]} times");
        Assert.True(cast[202] >= 2, $"Hurled Axe was cast {cast[202]} times");
    }

    [Fact]
    public void Set_the_same_cooldown_when_cleave_fires()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient real = RealWarrior(instance, 6760_131, Row(200));
        real.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-1);
        SimPlayer sim = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Item));

        handler.Execute(real.Connection, new CCastAbilityPacket { AbilityId = 200 });

        IAbility cleave = real.Character.Spells[new Avalon.Common.ValueObjects.AbilityId(200)]!;
        Assert.Equal(cleave.CooldownTimer, CombatRules.CooldownAfterFire(sim, sim.Ability(200)));
    }

    [Fact]
    public void Swing_on_the_same_interval_as_a_spawned_creature()
    {
        SimCreature sim = SimCreature.Create(Data, Data.Creature(4), Level, 0);
        var real = new Creature { BaseAttackTime = sim.Template.BaseAttackTime, HasteCap = Data.Combat.Formula.HasteCap };

        Assert.Equal(real.SwingInterval, sim.SwingInterval);
        Assert.Equal(real.SwingInterval, CombatRules.CooldownAfterFire(sim, sim.Basic!));
    }

    [Fact]
    public void Regenerate_the_same_mana_in_combat_as_a_character_entity()
    {
        // The simulated Wizard casts Arcane Bolt (instant, free: never suppresses regen) at a boar that never swings.
        SimPlayer sim = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
        sim.CurrentPower = 10;
        SimCreature dummy = SimCreature.Create(Data, Data.Creature(4), 1, 0);
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
            CurrentHealth = sim.Health,
            CurrentPower = 10,
            RegenStat = sim.RegenStat,
        };
        real.Spells.Load(Array.Empty<IAbility>());
        real.MarkCombat();

        for (int tick = 0; tick < 600; tick++)
        {
            fight.Tick();
            real.Update(TimeSpan.FromSeconds(FightSimulator.StepSeconds));
            Assert.True(real.CurrentPower == sim.CurrentPower,
                $"tick {tick}: {real.CurrentPower} Mana on the server, {sim.CurrentPower} simulated");
        }

        Assert.Equal(21u, sim.CurrentPower);   // 10 + floor(23 x 0.05 x 10 s)
    }

    /// <summary>
    /// The real CreatureSpawner over a StaticData loaded from the seed the simulator reads (a copy of its own), with
    /// every creature template pinned to the level <paramref name="level" /> picks from it, so the spawn's roll has
    /// one outcome.
    /// </summary>
    private static CreatureSpawner SeededSpawner(Func<CreatureTemplate, short> level)
    {
        SeedTables seed = SeedTables.Read();
        foreach (CreatureTemplate template in seed.CreatureTemplates)
        {
            short pinned = level(template);
            template.MinLevel = pinned;
            template.MaxLevel = pinned;
        }

        var templates = Substitute.For<ICreatureTemplateRepository>();
        templates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(seed.CreatureTemplates);
        var baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>()).Returns(seed.CreatureBaseStats);
        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>()).Returns(seed.CreatureRarityModifiers);
        var combat = Substitute.For<ICombatDataRepository>();
        combat.GetFormulasAsync(Arg.Any<CancellationToken>()).Returns(seed.CombatFormulas);
        combat.GetClassStatFactorsAsync(Arg.Any<CancellationToken>()).Returns(seed.ClassStatFactors);

        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<DialogueNode>());
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<DialogueOption>());
        var text = Substitute.For<ILocalizedTextRepository>();
        text.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LocalizedText>());
        text.GetAllLocalesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LocalizedTextLocale>());
        text.GetAllClassNamesAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterClassName>());
        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterCreateInfo>());
        var classStats = Substitute.For<IClassLevelStatRepository>();
        classStats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClassLevelStat>());
        var items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ItemTemplate>());
        var abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<AbilityTemplate>());
        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterLevelExperience>());

        var data = new StaticData(createInfos, classStats, items, abilities, levels, templates, baseStats, rarities,
            text, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance, combatDataRepository: combat);
        data.LoadAsync().GetAwaiter().GetResult();

        var world = Substitute.For<IWorld>();
        world.Data.Returns(data);
        return new CreatureSpawner(NullLoggerFactory.Instance, world);
    }
}
