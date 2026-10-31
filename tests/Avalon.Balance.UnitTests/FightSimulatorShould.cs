using System.Text.Json;
using Avalon.Balance.Core;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class FightSimulatorShould
{
    private const double Tick = FightSimulator.StepSeconds;
    private static BalanceData Data => TestData.Seeded;

    private static CompiledRotationEntry[] Rotation(params uint[] abilities) =>
        abilities.Select(a => new CompiledRotationEntry(a, [])).ToArray();

    private static SimCreature Dummy(ulong template = 4, int index = 0)
    {
        var creature = SimCreature.Create(Data, Data.Creature(template), 1, index);
        creature.Health = creature.CurrentHealth = 1_000_000;
        return creature;
    }

    private static void Hold(SimCreature creature)
    {
        foreach (SimAbility a in creature.Abilities) a.CooldownLeft = 1_000f;
    }

    private static FightSimulator Fight(SimPlayer player, CompiledRotationEntry[] rotation, params SimCreature[] creatures) =>
        new(Data.Combat.Formula, player, creatures, rotation, CombatRandom.Steady);

    private static void RunFor(FightSimulator fight, double seconds)
    {
        while (fight.Time < seconds && !fight.Over) fight.Tick();
    }

    [Fact]
    public void Give_identical_results_for_the_same_seed()
    {
        string Once()
        {
            var random = new Random(672);
            SimCreature[] pack = [SimCreature.Create(Data, Data.Creature(5), 2, 0), SimCreature.Create(Data, Data.Creature(7), 3, 1)];
            SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 3, new ulong[] { 7, 12, 13 }.Select(Data.Worn),
                Data.Talents.Starter(CharacterClass.Warrior))
                .Holding(Data, 201, 202, 200);
            var fight = new FightSimulator(Data.Combat.Formula, warrior, pack, Rotation(201, 202, 200), new CombatRandom(random));
            return JsonSerializer.Serialize(fight.Run());
        }

        Assert.Equal(Once(), Once());
    }

    [Fact]
    public void Recast_cleave_no_sooner_than_its_cooldown_and_within_two_ticks_of_it()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, [],
            Data.Talents.Starter(CharacterClass.Warrior)).Holding(Data, 200);
        FightSimulator fight = Fight(warrior, Rotation(200), dummy);
        RunFor(fight, 5);

        // #1178: Cleave is an attack, paced by its weapon's time over the attack speed.
        double cooldown = CombatRules.CooldownAfterFire(warrior, warrior.Ability(200));
        double[] casts = fight.Result().Casts.Where(c => c.AbilityId == 200).Select(c => c.StartSeconds).ToArray();
        Assert.True(casts.Length >= 5);
        Assert.All(casts.Zip(casts.Skip(1), (a, b) => b - a), gap => Assert.InRange(gap, cooldown - 1e-9, cooldown + 2 * Tick));
    }

    [Fact]
    public void Wait_the_global_cooldown_between_two_ready_abilities()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Hunter, 1, [],
            Data.Talents.Starter(CharacterClass.Hunter)).Holding(Data, 221, 222, 220), Rotation(221, 222, 220), dummy);
        RunFor(fight, 1);

        double[] starts = fight.Result().Casts.Select(c => c.StartSeconds).ToArray();
        Assert.Equal(0d, starts[0]);
        Assert.InRange(starts[1] - starts[0], 0.2 - 1e-9, 0.2 + 2 * Tick);
        Assert.InRange(starts[2] - starts[1], 0.2 - 1e-9, 0.2 + 2 * Tick);
    }

    [Fact]
    public void Fire_a_cast_time_ability_after_its_cast_time()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Wizard, 1, [],
            Data.Talents.Starter(CharacterClass.Wizard)).Holding(Data, 211), Rotation(211), dummy);
        RunFor(fight, 2);

        // As on the server: the handler queues the cast in the character pass and the cast system counts it down in
        // the same tick, so a player's cast fires one tick short of its cast time, measured from the tick it started.
        CastEvent burst = fight.Result().Casts.First(c => c.AbilityId == 211);
        Assert.InRange(burst.FiredSeconds - burst.StartSeconds, 0.6 - Tick - 1e-9, 0.6 + Tick);
        Assert.True(dummy.CurrentHealth < dummy.Health);
    }

    [Fact]
    public void Land_a_creature_wind_up_after_its_cast_time()
    {
        var alpha = SimCreature.Create(Data, Data.Creature(8), 3, 0);   // Howling Roar, 1 s wind-up
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 3, [],
            Data.Talents.Starter(CharacterClass.Warrior)).Holding(Data, 201);
        warrior.Health = warrior.CurrentHealth = 1_000_000;
        FightSimulator fight = Fight(warrior, Rotation(201), alpha);   // 201 costs mana the warrior may lack: fine
        RunFor(fight, 3);

        // A creature queues in the creature pass, after the cast system ran, so its countdown starts next tick.
        CastEvent roar = fight.Result().Casts.First(c => c.AbilityId == 310);
        Assert.InRange(roar.FiredSeconds - roar.StartSeconds, 1.0 - 1e-9, 1.0 + 2 * Tick);
    }

    /// <summary>
    /// #1181: a cleave that damages three creatures gives no mana: it pays its cost and nothing more; one tick's
    /// regeneration is below a point.
    /// </summary>
    [Fact]
    public void Gain_no_mana_for_each_creature_a_cleave_damages()
    {
        SimCreature[] pack = [Dummy(4, 0), Dummy(4, 1), Dummy(4, 2)];
        foreach (SimCreature c in pack) Hold(c);
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, [],
            Data.Talents.Starter(CharacterClass.Warrior)).Holding(Data, 200);
        warrior.CurrentPower = 50;
        FightSimulator fight = Fight(warrior, Rotation(200), pack);

        fight.Tick();

        Assert.Equal(50u - warrior.Ability(200).Metadata.Cost, fight.Player.CurrentPower);
    }

    /// <summary>#1181: damage taken gives no mana.</summary>
    [Fact]
    public void Gain_no_mana_from_damage_taken()
    {
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, [],
            Data.Talents.Starter(CharacterClass.Warrior)).Holding(Data, 201);
        warrior.ResetWard(0);   // a bare Warrior carries a small ward: the trample must meet health
        warrior.CurrentPower = 10;
        FightSimulator fight = Fight(warrior, Rotation(201), Dummy());   // 201 costs 20 mana: never cast here

        fight.Tick();   // the boar tramples on the first tick

        Assert.True(warrior.Health - warrior.CurrentHealth > 0);
        Assert.Equal(10u, warrior.CurrentPower);
    }

    [Fact]
    public void Regenerate_mana_at_the_in_combat_rate_carrying_the_fraction()
    {
        var wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, [],
            Data.Talents.Starter(CharacterClass.Wizard));   // 1 % of its maximum a second in combat
        double rate = wizard.ManaRegen.InCombat;
        wizard.CurrentPower = 10;
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(wizard, Rotation(210), dummy);   // Spark Bolt: instant and free

        RunFor(fight, 1 - Tick / 2);    // sixty ticks: one second's whole points, the fraction carried
        Assert.Equal(10u + (uint)Math.Floor(rate + 1e-3), wizard.CurrentPower);

        RunFor(fight, 10 - Tick / 2);   // six hundred ticks: ten seconds' whole points
        Assert.Equal(10u + (uint)Math.Floor(10 * rate + 1e-3), wizard.CurrentPower);
    }

    /// <summary>#1181: a cast no longer pauses regeneration: mana comes back during and right after the cast.</summary>
    [Fact]
    public void Keep_regenerating_through_a_cast_time_cast()
    {
        SimPlayer wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, [],
            Data.Talents.Starter(CharacterClass.Wizard)).Holding(Data, 211);
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(wizard, Rotation(211), dummy);   // Flame Burst: 0.6 s cast, 25 mana, 5 s cooldown

        RunFor(fight, 2);

        Assert.InRange(wizard.CurrentPower!.Value, wizard.Power!.Value - 25u + 1u, wizard.Power!.Value - 1u);
    }

    [Fact]
    public void End_a_fight_nobody_can_finish_at_the_time_limit_as_a_loss()
    {
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, [],
            Data.Talents.Starter(CharacterClass.Warrior)).Holding(Data, 201);
        warrior.Health = warrior.CurrentHealth = uint.MaxValue / 2;
        // No mana and no maximum to regenerate towards, so Ground Slam never becomes affordable and nobody damages
        // the boar.
        warrior.Power = warrior.CurrentPower = 0;
        FightSimulator fight = Fight(warrior, Rotation(201), Dummy());

        FightResult result = fight.Run();

        Assert.False(result.Won);
        Assert.InRange(result.Seconds, FightSimulator.MaxSeconds - 1e-6, FightSimulator.MaxSeconds + Tick);
    }

    [Fact]
    public void Win_when_every_creature_is_dead_and_record_what_happened()
    {
        var boar = SimCreature.Create(Data, Data.Creature(4), 1, 0);
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, new ulong[] { 7, 12, 13, 14, 15, 16 }.Select(Data.Worn),
            Data.Talents.Starter(CharacterClass.Warrior))
            .Holding(Data, 201, 202, 200);
        FightResult result = Fight(warrior, Rotation(201, 202, 200), boar).Run();

        Assert.True(result.Won);
        Assert.True(result.Seconds > 0);
        Assert.InRange(result.HealthLeftPct, 0.01, 100);
        // #1181: the Warrior enters full, so its first entry, Ground Slam, is paid from the start.
        Assert.True(result.DamageDealt["Ground Slam"] > 0);
        Assert.True(result.DamageTaken.Keys.All(k => k.StartsWith("Thornback Boar: ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Count_starved_time_when_the_first_ready_entry_cannot_be_paid()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, [],
            Data.Talents.Starter(CharacterClass.Warrior)).Holding(Data, 201, 200);
        warrior.CurrentPower = 0;
        FightSimulator fight = Fight(warrior, Rotation(201, 200), dummy);   // Ground Slam first, 0 mana

        fight.Tick();

        Assert.Equal(Tick, fight.Result().StarvedSeconds, precision: 9);
        Assert.Null(fight.Result().FirstSpenderSeconds);
    }

    [Fact]
    public void Draw_no_roll_for_a_creature_that_is_already_dead()
    {
        long Draws(bool withCorpse)
        {
            List<SimCreature> pack = [Dummy(4, 0), Dummy(4, 1)];
            if (withCorpse)
            {
                SimCreature corpse = Dummy(4, 2);
                corpse.CurrentHealth = 0;
                pack.Insert(1, corpse);
            }

            foreach (SimCreature c in pack) Hold(c);
            var rng = new CountingRandom();
            // Cleave, a cone: in the melee model it reaches every living creature.
            var fight = new FightSimulator(Data.Combat.Formula,
                SimPlayer.Create(Data, CharacterClass.Warrior, 1, [],
                    Data.Talents.Starter(CharacterClass.Warrior)).Holding(Data, 200), pack, Rotation(200), rng);
            fight.Tick();
            return rng.Draws;
        }

        long living = Draws(withCorpse: false);
        Assert.True(living > 0);
        Assert.Equal(living, Draws(withCorpse: true));
    }

    /// <summary>
    /// B-R8, in the melee model: a cone or a circle takes the nearest living creatures up to its MaxTargets (Heavy Strike's
    /// 1), a cone also up to the scenario's coneHits; a projectile the first, or when it pierces every one up to its
    /// MaxTargets. A row's maxTargets replaces the seeded one (null keeps it), on the ability's own copy of its row.
    /// </summary>
    [Theory]
    [InlineData(319u, null, null, 1)]   // Heavy Strike: a cone, MaxTargets 1
    [InlineData(200u, null, null, 3)]   // Cleave: a cone, no cap
    [InlineData(200u, null, 2, 2)]      // Cleave capped by coneHits
    [InlineData(200u, 1, 2, 1)]         // Cleave capped by both: the lower, its MaxTargets
    [InlineData(200u, 2, 1, 1)]         // Cleave capped by both: the lower, the coneHits
    [InlineData(201u, null, null, 3)]   // Ground Slam: a circle, no cap
    [InlineData(201u, 2, null, 2)]      // Ground Slam capped by its MaxTargets
    [InlineData(221u, null, null, 3)]   // Piercing Arrow
    [InlineData(221u, 2, null, 2)]      // Piercing Arrow ending on its MaxTargets-th
    [InlineData(202u, null, null, 1)]   // Hurled Axe
    public void Reach_as_many_creatures_as_the_shape_and_its_max_targets_allow(uint ability, int? maxTargets, int? coneHits,
        int damaged)
    {
        SimCreature[] pack = [Dummy(4, 0), Dummy(4, 1), Dummy(4, 2)];
        foreach (SimCreature c in pack) Hold(c);
        AbilityTemplate row = TestData.Seed().AbilityTemplates.Single(a => a.Id.Value == ability);
        row.MaxTargets = maxTargets ?? row.MaxTargets;
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, [], Data.Talents.Starter(CharacterClass.Warrior));
        warrior.Abilities.RemoveAll(a => a.Id == ability);   // Heavy Strike is its starter
        warrior.Abilities.Add(new SimAbility(row));
        var fight = new FightSimulator(Data.Combat.Formula, warrior, pack, Rotation(ability), CombatRandom.Steady,
            coneHits);

        fight.Tick();

        Assert.Equal(damaged, pack.Count(c => c.CurrentHealth < c.Health));
    }

    [Fact]
    public void Land_a_projectile_after_a_wind_up_that_completes_on_the_same_tick()
    {
        var alpha = SimCreature.Create(Data, Data.Creature(8), 3, 0);
        Hold(alpha);
        alpha.Abilities.First(a => a.Id == 310).CooldownLeft = 0f;   // Howling Roar, 1 s wind-up: the only thing it may choose
        var wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, [], Data.Talents.Starter(CharacterClass.Wizard));
        wizard.Health = wizard.CurrentHealth = 1_000_000;
        SimAbility bolt = wizard.Ability(210);   // Spark Bolt: an instant projectile
        bolt.CooldownLeft = 1_000f;
        FightSimulator fight = Fight(wizard, Rotation(210), alpha);

        fight.Tick();   // the alpha starts its roar in the creature pass
        Assert.NotNull(alpha.Casting);
        while (alpha.Casting!.TimeLeft > (float)Tick) fight.Tick();

        // Next tick the roar completes in the cast pass; the bolt is loosed in the character pass before it and
        // would kill the alpha. The server lands the bolt in the script pass, after the roar has fired.
        bolt.CooldownLeft = 0f;
        alpha.CurrentHealth = 1;
        fight.Tick();

        Assert.Contains(fight.Result().Casts, c => c.AbilityId == 310);
        Assert.True(wizard.CurrentHealth < wizard.Health);
        Assert.True(alpha.IsDead);
    }

    [Fact]
    public void Land_a_creatures_projectile_on_the_next_tick()
    {
        var tuskroot = SimCreature.Create(Data, Data.Creature(9), 3, 0);
        Hold(tuskroot);
        tuskroot.Abilities.First(a => a.Id == 313).CooldownLeft = 0f;   // Thorn Volley, an instant projectile
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, [], Data.Talents.Starter(CharacterClass.Warrior));
        warrior.Health = warrior.CurrentHealth = 1_000_000;
        FightSimulator fight = Fight(warrior, Rotation(), tuskroot);

        fight.Tick();
        Assert.Contains(fight.Result().Casts, c => c.AbilityId == 313);
        Assert.Equal(warrior.Health, warrior.CurrentHealth);

        fight.Tick();
        Assert.True(warrior.CurrentHealth < warrior.Health);
    }

    [Fact]
    public void Drop_a_creatures_projectile_when_the_creature_dies_before_it_lands()
    {
        var tuskroot = SimCreature.Create(Data, Data.Creature(9), 3, 0);
        Hold(tuskroot);
        tuskroot.Abilities.First(a => a.Id == 313).CooldownLeft = 0f;
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, [], Data.Talents.Starter(CharacterClass.Warrior));
        warrior.Health = warrior.CurrentHealth = 1_000_000;
        var rng = new CountingRandom();
        var fight = new FightSimulator(Data.Combat.Formula, warrior, [tuskroot], Rotation(), rng);

        fight.Tick();
        Assert.Contains(fight.Result().Casts, c => c.AbilityId == 313);
        tuskroot.CurrentHealth = 0;
        fight.Tick();

        Assert.Equal(warrior.Health, warrior.CurrentHealth);
        Assert.Equal(0, rng.Draws);
    }

    /// <summary>
    /// B-R15, as AbilityEffect.Apply: Double Tap's second hit (50 %) draws its own rolls, as many as a whole hit, and the
    /// aura is placed once after both; a first hit that kills is never followed by a second, so the corpse draws nothing.
    /// One hit's draws are an unshaped Crippling Shot's (its aura, Crippled, draws nothing: it does not tick).
    /// </summary>
    [Theory]
    [InlineData(1_000_000u, 2)]
    [InlineData(1u, 1)]
    public void Land_a_second_hit_with_its_own_rolls_and_one_aura(uint health, int hits)
    {
        (long Draws, SimCreature Dummy) Cast(SimPlayer hunter)
        {
            SimCreature dummy = Dummy();
            dummy.Health = dummy.CurrentHealth = health;
            Hold(dummy);
            var rng = new CountingRandom();
            new FightSimulator(Data.Combat.Formula, hunter, [dummy], Rotation(223), rng, data: Data).Tick();
            return (rng.Draws, dummy);
        }

        (long oneHit, _) = Cast(SimPlayer.Create(Data, CharacterClass.Hunter, 10, [],
            Data.Talents.Starter(CharacterClass.Hunter)).Holding(Data, 223));
        (long draws, SimCreature dummy) = Cast(TestData.Whole("hunter-double-tap"));

        Assert.True(oneHit > 0);
        Assert.Equal(hits * oneHit, draws);
        SimAura[] crippled = [.. dummy.Auras.Where(a => a.Template.Name == "Crippled")];
        Assert.Equal(dummy.IsDead ? 0 : 1, crippled.Length);
        Assert.All(crippled, a => Assert.Equal(1u, a.Stacks));
    }

    /// <summary>
    /// B-R15: Twin Axes fires Hurled Axe in two lanes that share one set of creatures hit, so each lane takes the first
    /// creature the cast has not hit; a lane with no creature left hits nothing and draws nothing.
    /// </summary>
    [Theory]
    [InlineData(3, 2)]
    [InlineData(1, 1)]
    public void Spread_a_fan_over_distinct_creatures(int creatures, int damaged)
    {
        long Draws(SimPlayer warrior, SimCreature[] pack)
        {
            foreach (SimCreature c in pack) Hold(c);
            var rng = new CountingRandom();
            new FightSimulator(Data.Combat.Formula, warrior, pack, Rotation(202), rng).Tick();   // instant: lands this tick
            return rng.Draws;
        }

        long unshaped = Draws(SimPlayer.Create(Data, CharacterClass.Warrior, 10, [],
            Data.Talents.Starter(CharacterClass.Warrior)).Holding(Data, 202), [Dummy()]);
        SimCreature[] pack = [.. Enumerable.Range(0, creatures).Select(i => Dummy(4, i))];
        SimPlayer axes = TestData.Whole("warrior-axes");
        Assert.Equal(2, axes.Ability(202).Metadata.ProjectileCount);

        long draws = Draws(axes, pack);

        Assert.Equal(damaged, pack.Count(c => c.CurrentHealth < c.Health));
        Assert.Equal(damaged * unshaped, draws);
    }

    /// <summary>
    /// B-R15: Rimeburst's Frostbolt ends on the first creature and explodes there; in melee the burst reaches every other
    /// living creature, each hit once at 60 % with the Area tag, damage only (no aura, no second hit).
    /// </summary>
    [Fact]
    public void Explode_on_the_creature_a_projectile_ends_on()
    {
        // Lane A to the joint, Frostbolt, its branch, then Rimeburst: not a B-R20 build (none reaches an explosion).
        int[] rimeburst = [148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 294, 297, 298, 299, 300, 301, 331];
        var wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 10, [],
            Data.Talents.Hold(CharacterClass.Wizard, rimeburst));
        SimAbility frostbolt = wizard.Ability(320);
        // Steady rolls the same every draw, so the burst's hit on an identical dummy is the one each burst creature takes.
        uint burstHit = CombatRules.Damage(wizard, Dummy(), frostbolt, Data.Combat.Formula, CombatRandom.Steady,
            new HitShaping(frostbolt.Metadata.ExplosionEffectiveness, AbilityTags.Area)).Total;
        SimCreature[] pack = [Dummy(4, 0), Dummy(4, 1), Dummy(4, 2)];
        foreach (SimCreature c in pack) Hold(c);
        var fight = new FightSimulator(Data.Combat.Formula, wizard, pack, Rotation(320), CombatRandom.Steady, data: Data);

        RunFor(fight, 1);   // a 0.5 s cast, then a 2 s cooldown: one Frostbolt

        Assert.Single(fight.Result().Casts, c => c.AbilityId == 320);
        Assert.True(burstHit < pack[0].Health - pack[0].CurrentHealth);   // the burst is a share of the first hit
        Assert.True(burstHit > 0);
        foreach (SimCreature burst in pack[1..])
        {
            Assert.Equal(burstHit, burst.Health - burst.CurrentHealth);
            Assert.Empty(burst.Auras);
        }
    }

    private sealed class CountingRandom : ICombatRandom
    {
        public long Draws { get; private set; }

        public double NextDouble()
        {
            Draws++;
            return CombatRandom.Steady.NextDouble();
        }

        public long NextInt64(long minInclusive, long maxInclusive)
        {
            Draws++;
            return CombatRandom.Steady.NextInt64(minInclusive, maxInclusive);
        }
    }
}
