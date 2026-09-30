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
using Avalon.Combat;

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
    /// A fight ten seconds long, tick by tick, through the real cast handler, cast system and combat service (see
    /// <see cref="Exchange" />): the server must cast exactly what the simulated Warrior casts, on the same tick,
    /// and refuse every rotation entry the simulator passed over; after every tick the boar's health, the Warrior's
    /// Fury, both cooldowns and the casting state must match. Cleave (a cone) hits as it fires; Hurled Axe (a
    /// projectile) hits in the cast system's script pass, and the boar stands inside its first step, so the tick it
    /// lands on is the simulator's.
    /// </summary>
    [Fact]
    public void Land_cleave_and_hurled_axe_on_the_same_ticks_through_the_cast_system()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            random: new CombatRandom(new Random(Seed)));
        MapInstanceClient real = RealWarrior(instance, 6760_151, Row(200), Row(202));
        SimPlayer simWarrior = SimPlayer.Create(Data, CharacterClass.Warrior, Level, ForestWarrior.Select(Data.Item));
        SimCreature simBoar = PassiveBoar(Level);
        Creature realBoar = RealBoar(instance, 6760_951, simBoar, PointBlank);
        CompiledRotationEntry[] rotation = [new(202, []), new(200, [])];
        var fight = new FightSimulator(Data.Combat.Formula, simWarrior, [simBoar], rotation, new CombatRandom(new Random(Seed)));

        Dictionary<uint, int> cast = Exchange(fight, rotation, instance, handler, real, PointBlank, ticks: 600, tick =>
        {
            Assert.True(simBoar.CurrentHealth == realBoar.CurrentHealth,
                $"tick {tick}: the boar has {realBoar.CurrentHealth} health on the server, {simBoar.CurrentHealth} simulated");
            Assert.True(simWarrior.CurrentPower == real.Character.CurrentPower,
                $"tick {tick}: the Warrior has {real.Character.CurrentPower} Fury on the server, {simWarrior.CurrentPower} simulated");
        });

        Assert.True(cast[200] >= 5, $"Cleave was cast {cast[200]} times");
        Assert.True(cast[202] >= 2, $"Hurled Axe was cast {cast[202]} times");
    }

    /// <summary>
    /// A level-1 Wizard casting Flame Burst (0.6 s wind-up, 25 Mana, 5 s cooldown) through the real handler and cast
    /// system, against the simulator, Mana compared after every tick for 30 s: the cast is paid when it is taken,
    /// regeneration stops while it winds up and for PowerRegenCastSuppressSeconds after, and resumes; the second cast
    /// waits on the Mana that comes back. The character's clock is a test clock moved to each tick's simulated time,
    /// because the suppression window is timed by it. The simulator fights in continuous combat, so the character is
    /// kept in combat every tick (MarkCombat, as each hit would); without that its combat tag would lapse 5 s after the
    /// last hit and the out-of-combat rate would apply.
    /// </summary>
    [Fact]
    public void Suppress_and_resume_mana_regen_around_a_wind_up_as_a_character_entity()
    {
        const uint StartMana = 40;
        var clock = new FixedTimeProvider(ClockStart);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler,
            random: new CombatRandom(new Random(Seed)));
        var row = new Avalon.Domain.Characters.Character
        {
            Id = new Avalon.Common.ValueObjects.CharacterId(6760_161), AccountId = new Avalon.Common.ValueObjects.AccountId(1),
            Name = "Tester6760161", Class = CharacterClass.Wizard, Level = 1, CreationDate = DateTime.UtcNow,
        };
        MapInstanceClient real = Join(instance, new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), clock) { Data = row });
        real.Character.PowerType = PowerType.Mana;
        Assert.True(CharacterStatsRefresh.Apply(real.Character, Data.Tables.ClassLevelStats, Data.Combat.Factors,
            tid => Data.Tables.ItemTemplates.FirstOrDefault(t => t.Id == tid), CurrentValues.Refill, Data.Combat.Formula));
        real.Character.Spells.Load([AbilityTestData.Game(Row(211))]);
        real.Character.CurrentPower = StartMana;

        SimPlayer simWizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
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

    /// <summary>A level-<paramref name="level" /> Thornback Boar that never swings, with the health to outlast every exchange.</summary>
    private static SimCreature PassiveBoar(ushort level)
    {
        SimCreature boar = SimCreature.Create(Data, Data.Creature(4), level, 0);
        boar.Health = boar.CurrentHealth = 1_000_000;
        foreach (SimAbility a in boar.Abilities) a.CooldownLeft = 1_000f;   // the real boar's script never casts
        return boar;
    }

    private static readonly DateTimeOffset ClockStart = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary><paramref name="ticks" /> server ticks as a TimeSpan, rounded to its 100 ns units so whole seconds stay exact.</summary>
    private static TimeSpan Ticks(long ticks) =>
        TimeSpan.FromTicks((long)Math.Round(ticks * (double)TimeSpan.TicksPerSecond * FightSimulator.StepSeconds));

    /// <summary>
    /// The tick the real side is given: the TimeSpan nearest 1/60 s, 166667 of its 100 ns units (0.0166667 s). A
    /// TimeSpan cannot hold 1/60 s; TimeSpan.FromSeconds truncates it to 166666 (0.0166666 s), which is far enough
    /// short that a 0.6 s wind-up fires a tick later on the server than at the simulator's (float)(1/60) =
    /// 0.016666668 s, an artefact of the test's clock and not of either side.
    /// </summary>
    private static readonly TimeSpan OneTick = Ticks(1);

    /// <summary>
    /// How far a real cooldown may sit from the simulated one. The real container subtracts (float)OneTick.TotalSeconds
    /// = 0.0166667 s each tick and the simulator 0.016666668 s: 3.1e-8 s a tick, under 1e-5 s over the longest seeded
    /// player cooldown (5 s, 300 ticks), plus float rounding of the running value. 1e-4 s covers that and is under a
    /// hundredth of a tick, so a cooldown one tick off (0.0167 s) fails.
    /// </summary>
    private const float CooldownTolerance = 1e-4f;

    /// <summary>
    /// Drives the simulator and the real instance side by side, one tick at a time. Each tick the simulator ticks
    /// first; then every entry of its rotation, in order, is offered to the real handler, with the simulator's own
    /// global cooldown (the handler's clock is the wall clock, so the last cast start is set the simulated time since
    /// it back from now). The entry the simulator started this tick must be accepted; every entry before it, or every
    /// entry when it started nothing, must be refused, so the server can start neither later nor earlier than the
    /// simulator. Then the instance ticks, and each ability's cooldown and the casting state must match.
    /// </summary>
    private static Dictionary<uint, int> Exchange(FightSimulator fight, IReadOnlyList<CompiledRotationEntry> rotation,
        MapInstance instance, CastAbilityHandler handler, MapInstanceClient real, Vector3 aim, int ticks,
        Action<int> afterTick, FixedTimeProvider? clock = null, Action? beforeUpdate = null)
    {
        SimPlayer sim = fight.Player;
        var groundPos = new Vector3Dto { X = aim.x, Y = aim.y, Z = aim.z };
        Dictionary<uint, int> casts = rotation.ToDictionary(e => e.AbilityId, _ => 0);
        long? lastStart = null;

        for (int tick = 0; tick < ticks; tick++)
        {
            if (clock is not null) clock.Now = ClockStart + Ticks(tick);
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
                real.Character.LastCastStartTime = DateTime.UtcNow - (lastStart is { } last ? Ticks(tick - last) : TimeSpan.FromHours(1));
                int sentBefore = real.Sent.Count;
                handler.Execute(real.Connection, new CCastAbilityPacket { AbilityId = entry.AbilityId, GroundPos = groundPos });
                int refused = real.Sent.Skip(sentBefore).Count(p => p.Header.Type == NetworkPacketType.SMSG_ABILITY_NOT_READY);

                if (entry.AbilityId == started)
                {
                    Assert.True(refused == 0, $"tick {tick}: the server refused ability {entry.AbilityId}, which the simulator cast");
                    casts[entry.AbilityId]++;
                    lastStart = tick;
                    break;
                }

                Assert.True(refused == 1, $"tick {tick}: the server cast ability {entry.AbilityId}, which the simulator did not");
            }

            beforeUpdate?.Invoke();
            instance.Update(OneTick);

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
            real.Update(OneTick);
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
