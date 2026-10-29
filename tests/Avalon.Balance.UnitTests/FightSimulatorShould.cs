using System.Text.Json;
using Avalon.Balance.Core;
using Avalon.Combat;
using Avalon.Common;
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

    [Fact(Skip = BalancePause.Reason)]
    public void Give_identical_results_for_the_same_seed()
    {
        string Once()
        {
            var random = new Random(672);
            SimCreature[] pack = [SimCreature.Create(Data, Data.Creature(5), 2, 0), SimCreature.Create(Data, Data.Creature(7), 3, 1)];
            var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 3, new ulong[] { 7, 12, 13 }.Select(Data.Worn));
            var fight = new FightSimulator(Data.Combat.Formula, warrior, pack, Rotation(201, 202, 200), new CombatRandom(random));
            return JsonSerializer.Serialize(fight.Run());
        }

        Assert.Equal(Once(), Once());
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Recast_cleave_no_sooner_than_its_cooldown_and_within_two_ticks_of_it()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Warrior, 1, []), Rotation(200), dummy);
        RunFor(fight, 5);

        double[] casts = fight.Result().Casts.Where(c => c.AbilityId == 200).Select(c => c.StartSeconds).ToArray();
        Assert.True(casts.Length >= 5);
        Assert.All(casts.Zip(casts.Skip(1), (a, b) => b - a), gap => Assert.InRange(gap, 0.8 - 1e-9, 0.8 + 2 * Tick));
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Wait_the_global_cooldown_between_two_ready_abilities()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Hunter, 1, []), Rotation(221, 222, 220), dummy);
        RunFor(fight, 1);

        double[] starts = fight.Result().Casts.Select(c => c.StartSeconds).ToArray();
        Assert.Equal(0d, starts[0]);
        Assert.InRange(starts[1] - starts[0], 0.2 - 1e-9, 0.2 + 2 * Tick);
        Assert.InRange(starts[2] - starts[1], 0.2 - 1e-9, 0.2 + 2 * Tick);
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Fire_a_cast_time_ability_after_its_cast_time()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Wizard, 1, []), Rotation(211), dummy);
        RunFor(fight, 2);

        // As on the server: the handler queues the cast in the character pass and the cast system counts it down in
        // the same tick, so a player's cast fires one tick short of its cast time, measured from the tick it started.
        CastEvent burst = fight.Result().Casts.First(c => c.AbilityId == 211);
        Assert.InRange(burst.FiredSeconds - burst.StartSeconds, 0.6 - Tick - 1e-9, 0.6 + Tick);
        Assert.True(dummy.CurrentHealth < dummy.Health);
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Land_a_creature_wind_up_after_its_cast_time()
    {
        var alpha = SimCreature.Create(Data, Data.Creature(8), 3, 0);   // Howling Roar, 1 s wind-up
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 3, []);
        warrior.Health = warrior.CurrentHealth = 1_000_000;
        FightSimulator fight = Fight(warrior, Rotation(201), alpha);   // 201 costs mana the warrior may lack: fine
        RunFor(fight, 3);

        // A creature queues in the creature pass, after the cast system ran, so its countdown starts next tick.
        CastEvent roar = fight.Result().Casts.First(c => c.AbilityId == 310);
        Assert.InRange(roar.FiredSeconds - roar.StartSeconds, 1.0 - 1e-9, 1.0 + 2 * Tick);
    }

    /// <summary>#1181: a cleave that damages three creatures gives no mana; one tick's regeneration is below a point.</summary>
    [Fact(Skip = BalancePause.Reason)]
    public void Gain_no_mana_for_each_creature_a_cleave_damages()
    {
        SimCreature[] pack = [Dummy(4, 0), Dummy(4, 1), Dummy(4, 2)];
        foreach (SimCreature c in pack) Hold(c);
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
        warrior.CurrentPower = 50;
        FightSimulator fight = Fight(warrior, Rotation(200), pack);

        fight.Tick();

        Assert.Equal(50u, fight.Player.CurrentPower);
    }

    /// <summary>#1181: damage taken gives no mana.</summary>
    [Fact(Skip = BalancePause.Reason)]
    public void Gain_no_mana_from_damage_taken()
    {
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
        warrior.CurrentPower = 10;
        FightSimulator fight = Fight(warrior, Rotation(201), Dummy());   // 201 costs 20 mana: never cast here

        fight.Tick();   // the boar tramples on the first tick

        Assert.True(warrior.Health - warrior.CurrentHealth > 0);
        Assert.Equal(10u, warrior.CurrentPower);
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Scale_cooldowns_by_haste()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Warrior, 1, [Data.Worn(7)]), Rotation(200), dummy);
        RunFor(fight, 3);

        double[] casts = fight.Result().Casts.Where(c => c.AbilityId == 200).Select(c => c.StartSeconds).ToArray();
        Assert.InRange(casts[1] - casts[0], 0.8 / 1.03 - 1e-9, 0.8 / 1.03 + 2 * Tick);
    }

    [Fact]
    public void Regenerate_mana_at_the_in_combat_rate_carrying_the_fraction()
    {
        var wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);   // 1 % of its maximum a second in combat
        double rate = wizard.ManaRegen.InCombat;
        wizard.CurrentPower = 10;
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(wizard, Rotation(210), dummy);   // Arcane Bolt: instant and free

        RunFor(fight, 1 - Tick / 2);    // sixty ticks: one second's whole points, the fraction carried
        Assert.Equal(10u + (uint)Math.Floor(rate + 1e-3), wizard.CurrentPower);

        RunFor(fight, 10 - Tick / 2);   // six hundred ticks: ten seconds' whole points
        Assert.Equal(10u + (uint)Math.Floor(10 * rate + 1e-3), wizard.CurrentPower);
    }

    /// <summary>#1181: a cast no longer pauses regeneration: mana comes back during and right after the cast.</summary>
    [Fact(Skip = BalancePause.Reason)]
    public void Keep_regenerating_through_a_cast_time_cast()
    {
        var wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(wizard, Rotation(211), dummy);   // Flame Burst: 0.6 s cast, 25 mana, 5 s cooldown

        RunFor(fight, 2);

        Assert.InRange(wizard.CurrentPower!.Value, wizard.Power!.Value - 25u + 1u, wizard.Power!.Value - 1u);
    }

    [Fact(Skip = BalancePause.Reason)]
    public void End_a_fight_nobody_can_finish_at_the_time_limit_as_a_loss()
    {
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
        warrior.Health = warrior.CurrentHealth = uint.MaxValue / 2;
        // No mana and no maximum to regenerate towards, so Ground Slam never becomes affordable and nobody damages
        // the boar.
        warrior.Power = warrior.CurrentPower = 0;
        FightSimulator fight = Fight(warrior, Rotation(201), Dummy());

        FightResult result = fight.Run();

        Assert.False(result.Won);
        Assert.InRange(result.Seconds, FightSimulator.MaxSeconds - 1e-6, FightSimulator.MaxSeconds + Tick);
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Win_when_every_creature_is_dead_and_record_what_happened()
    {
        var boar = SimCreature.Create(Data, Data.Creature(4), 1, 0);
        FightResult result = Fight(SimPlayer.Create(Data, CharacterClass.Warrior, 1, new ulong[] { 7, 12, 13, 14, 15, 16 }.Select(Data.Worn).ToArray()),
            Rotation(201, 202, 200), boar).Run();

        Assert.True(result.Won);
        Assert.True(result.Seconds > 0);
        Assert.InRange(result.HealthLeftPct, 0.01, 100);
        // #1181: the Warrior enters full, so its first entry, Ground Slam, is paid from the start.
        Assert.True(result.DamageDealt["Ground Slam"] > 0);
        Assert.True(result.DamageTaken.Keys.All(k => k.StartsWith("Thornback Boar: ", StringComparison.Ordinal)));
    }

    [Fact(Skip = BalancePause.Reason)]
    public void Count_starved_time_when_the_first_ready_entry_cannot_be_paid()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
        warrior.CurrentPower = 0;
        FightSimulator fight = Fight(warrior, Rotation(201, 200), dummy);   // Ground Slam first, 0 mana

        fight.Tick();

        Assert.Equal(Tick, fight.Result().StarvedSeconds, precision: 9);
        Assert.Null(fight.Result().FirstSpenderSeconds);
    }

    [Fact(Skip = BalancePause.Reason)]
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
            var fight = new FightSimulator(Data.Combat.Formula, SimPlayer.Create(Data, CharacterClass.Warrior, 1, []),
                pack, Rotation(200), rng);   // Cleave, a cone: in the melee model it reaches every living creature
            fight.Tick();
            return rng.Draws;
        }

        long living = Draws(withCorpse: false);
        Assert.True(living > 0);
        Assert.Equal(living, Draws(withCorpse: true));
    }

    /// <summary>
    /// B-R8, in the melee model: a cone or a circle takes the nearest living creatures up to its MaxTargets (Heavy Strike's
    /// 1), a cone also up to the scenario's coneHits; a projectile the first, or every one when it pierces.
    /// </summary>
    [Theory]
    [InlineData(319u, null, 1)]   // Heavy Strike: a cone, MaxTargets 1
    [InlineData(200u, null, 3)]   // Cleave: a cone, no cap
    [InlineData(200u, 2, 2)]      // Cleave capped by coneHits
    [InlineData(221u, null, 3)]   // Piercing Arrow
    [InlineData(202u, null, 1)]   // Hurled Axe
    public void Reach_as_many_creatures_as_the_shape_and_its_max_targets_allow(uint ability, int? coneHits, int damaged)
    {
        SimCreature[] pack = [Dummy(4, 0), Dummy(4, 1), Dummy(4, 2)];
        foreach (SimCreature c in pack) Hold(c);
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []).Holding(Data, ability);
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
        var wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
        wizard.Health = wizard.CurrentHealth = 1_000_000;
        SimAbility bolt = wizard.Ability(210);   // Arcane Bolt: an instant projectile
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
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
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
        var warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
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
