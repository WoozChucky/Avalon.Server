using System.Text.Json;
using Avalon.Balance.Config;
using Avalon.Balance.Data;
using Avalon.Balance.Simulation;
using Avalon.World.Combat;
using Avalon.World.Public.Enums;
using Xunit;
using Avalon.Combat;

namespace Avalon.Balance.UnitTests;

public class FightSimulatorShould
{
    private const double Tick = FightSimulator.StepSeconds;
    private static BalanceData Data => TestData.Seeded;

    private static CompiledRotationEntry[] Rotation(params uint[] abilities) =>
        abilities.Select(a => new CompiledRotationEntry(a, [])).ToArray();

    private static SimCreature Dummy(ulong template = 4, int index = 0)
    {
        SimCreature creature = SimCreature.Create(Data, Data.Creature(template), 1, index);
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
            SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 3, new ulong[] { 7, 12, 13 }.Select(Data.Item));
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
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Warrior, 1, []), Rotation(200), dummy);
        RunFor(fight, 5);

        double[] casts = fight.Result().Casts.Where(c => c.AbilityId == 200).Select(c => c.StartSeconds).ToArray();
        Assert.True(casts.Length >= 5);
        Assert.All(casts.Zip(casts.Skip(1), (a, b) => b - a), gap => Assert.InRange(gap, 0.8 - 1e-9, 0.8 + 2 * Tick));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public void Land_a_creature_wind_up_after_its_cast_time()
    {
        SimCreature alpha = SimCreature.Create(Data, Data.Creature(8), 3, 0);   // Howling Roar, 1 s wind-up
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 3, []);
        warrior.Health = warrior.CurrentHealth = 1_000_000;
        FightSimulator fight = Fight(warrior, Rotation(201), alpha);   // 201 costs Fury the warrior may lack: fine
        RunFor(fight, 3);

        // A creature queues in the creature pass, after the cast system ran, so its countdown starts next tick.
        CastEvent roar = fight.Result().Casts.First(c => c.AbilityId == 310);
        Assert.InRange(roar.FiredSeconds - roar.StartSeconds, 1.0 - 1e-9, 1.0 + 2 * Tick);
    }

    [Fact]
    public void Gain_cleaves_fury_for_each_creature_it_damages()
    {
        SimCreature[] pack = [Dummy(4, 0), Dummy(4, 1), Dummy(4, 2)];
        foreach (SimCreature c in pack) Hold(c);
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Warrior, 1, []), Rotation(200), pack);

        fight.Tick();

        Assert.Equal(24u, fight.Player.CurrentPower);   // 8 x 3
    }

    [Fact]
    public void Gain_fury_from_damage_taken()
    {
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
        FightSimulator fight = Fight(warrior, Rotation(201), Dummy());   // 201 costs 20 Fury: never cast here

        fight.Tick();   // the boar tramples on the first tick

        uint lost = warrior.Health - warrior.CurrentHealth;
        Assert.True(lost > 0);
        Assert.Equal(Fury.FromDamageTaken(lost, warrior.Health, warrior.Health, 50f), warrior.CurrentPower);
    }

    [Fact]
    public void Scale_cooldowns_by_haste()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(SimPlayer.Create(Data, CharacterClass.Warrior, 1, [Data.Item(7)]), Rotation(200), dummy);
        RunFor(fight, 3);

        double[] casts = fight.Result().Casts.Where(c => c.AbilityId == 200).Select(c => c.StartSeconds).ToArray();
        Assert.InRange(casts[1] - casts[0], 0.8 / 1.03 - 1e-9, 0.8 / 1.03 + 2 * Tick);
    }

    [Fact]
    public void Regenerate_mana_at_the_in_combat_rate_carrying_the_fraction()
    {
        SimPlayer wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);   // Intellect 23: 1.15 Mana a second
        wizard.CurrentPower = 10;
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(wizard, Rotation(210), dummy);   // Arcane Bolt: instant, free, never suppresses

        RunFor(fight, 1 - Tick / 2);    // sixty ticks: 1.15, one point and 0.15 carried
        Assert.Equal(11u, wizard.CurrentPower);

        RunFor(fight, 10 - Tick / 2);   // six hundred ticks: 11.5
        Assert.Equal(21u, wizard.CurrentPower);
    }

    [Fact]
    public void Suppress_regen_for_five_seconds_after_a_cast_time_cast()
    {
        SimPlayer wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
        SimCreature dummy = Dummy();
        Hold(dummy);
        FightSimulator fight = Fight(wizard, Rotation(211), dummy);   // Flame Burst: 0.6 s cast, 25 mana, 5 s cooldown

        RunFor(fight, 2);

        Assert.Equal(wizard.Power - 25u, wizard.CurrentPower);
    }

    [Fact]
    public void End_a_fight_nobody_can_finish_at_the_time_limit_as_a_loss()
    {
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
        warrior.Health = warrior.CurrentHealth = uint.MaxValue / 2;
        // Fury from damage taken is lost / max health x 50: nothing against this much health, so Ground Slam never
        // becomes affordable and nobody damages the boar.
        FightSimulator fight = Fight(warrior, Rotation(201), Dummy());

        FightResult result = fight.Run();

        Assert.False(result.Won);
        Assert.InRange(result.Seconds, FightSimulator.MaxSeconds - 1e-6, FightSimulator.MaxSeconds + Tick);
    }

    [Fact]
    public void Win_when_every_creature_is_dead_and_record_what_happened()
    {
        SimCreature boar = SimCreature.Create(Data, Data.Creature(4), 1, 0);
        FightResult result = Fight(SimPlayer.Create(Data, CharacterClass.Warrior, 1, new ulong[] { 7, 12, 13, 14, 15, 16 }.Select(Data.Item).ToArray()),
            Rotation(201, 202, 200), boar).Run();

        Assert.True(result.Won);
        Assert.True(result.Seconds > 0);
        Assert.InRange(result.HealthLeftPct, 0.01, 100);
        Assert.True(result.DamageDealt["Cleave"] > 0);
        Assert.True(result.DamageTaken.Keys.All(k => k.StartsWith("Thornback Boar: ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Count_starved_time_when_the_first_ready_entry_cannot_be_paid()
    {
        SimCreature dummy = Dummy();
        Hold(dummy);
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
        FightSimulator fight = Fight(warrior, Rotation(201, 200), dummy);   // Ground Slam first, 0 Fury

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
            var fight = new FightSimulator(Data.Combat.Formula, SimPlayer.Create(Data, CharacterClass.Warrior, 1, []),
                pack, Rotation(200), rng);   // Cleave, a cone: in the melee model it reaches every living creature
            fight.Tick();
            Assert.Equal(16u, fight.Player.CurrentPower);   // 8 x 2: the corpse was not hit
            return rng.Draws;
        }

        long living = Draws(withCorpse: false);
        Assert.True(living > 0);
        Assert.Equal(living, Draws(withCorpse: true));
    }

    [Fact]
    public void Land_a_projectile_after_a_wind_up_that_completes_on_the_same_tick()
    {
        SimCreature alpha = SimCreature.Create(Data, Data.Creature(8), 3, 0);
        Hold(alpha);
        alpha.Abilities.First(a => a.Id == 310).CooldownLeft = 0f;   // Howling Roar, 1 s wind-up: the only thing it may choose
        SimPlayer wizard = SimPlayer.Create(Data, CharacterClass.Wizard, 1, []);
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
        SimCreature tuskroot = SimCreature.Create(Data, Data.Creature(9), 3, 0);
        Hold(tuskroot);
        tuskroot.Abilities.First(a => a.Id == 313).CooldownLeft = 0f;   // Thorn Volley, an instant projectile
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
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
        SimCreature tuskroot = SimCreature.Create(Data, Data.Creature(9), 3, 0);
        Hold(tuskroot);
        tuskroot.Abilities.First(a => a.Id == 313).CooldownLeft = 0f;
        SimPlayer warrior = SimPlayer.Create(Data, CharacterClass.Warrior, 1, []);
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
