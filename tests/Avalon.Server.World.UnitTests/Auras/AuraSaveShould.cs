using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Persistence;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>
/// Every save writes the auras held with the time they have left; select brings them back, and their time stands still
/// until the character enters its instance.
/// </summary>
public class AuraSaveShould
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(T0);

    private CharacterEntity Character(uint id = 913_101, TimeProvider? clock = null)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(1),
            Name = $"Tester{id}",
            Class = CharacterClass.Warrior,
            CreationDate = DateTime.UtcNow,
        };
        return new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), clock ?? _clock) { Data = row };
    }

    private static ActiveAura Bleed(DateTimeOffset at, ObjectGuid caster) =>
        new(AuraTestData.Bleed(), caster, new AuraSource(new AbilityId(203), 1f, 0f, 0), 2, new AuraSnapshot(3f, 5f, 4),
            AuraSchedule.Start(at, 12000, 3000), 12000, at.UtcDateTime);

    [Fact]
    public void Write_every_held_aura_with_the_time_and_ticks_it_has_left()
    {
        CharacterEntity character = Character();
        var caster = new ObjectGuid(ObjectType.Character, 77);
        character.Auras.Add(Bleed(T0, caster), T0);
        _clock.Advance(TimeSpan.FromSeconds(4));   // one tick owed and taken
        character.Auras.All[0].Schedule = character.Auras.All[0].Schedule.AfterTicks(1);
        character.Auras.All[0].PeriodicCarry = 0.25d;

        CharacterSaveSnapshot snapshot = CharacterSaveSnapshot.Take(character);

        CharacterAura row = Assert.Single(snapshot.Batch.Auras!.Rows);
        Assert.Equal((0, 901u, caster.RawValue, (uint?)203u, 2), (row.Slot, row.AuraId, row.CasterGuid, row.SourceAbilityId, row.Stacks));
        Assert.Equal((8000u, 12000u, 3), (row.RemainingMs, row.DurationMs, row.TicksLeft));
        Assert.Equal((3f, 5f, 4), (row.TickAmount, row.CritPct, row.CasterLevel));
        Assert.Equal(0.25d, row.PeriodicCarry);
    }

    /// <summary>A creature's guid is numbered per run: after a restart it could name another creature, so it is not kept.</summary>
    [Fact]
    public void Save_a_creature_caster_as_nobody()
    {
        CharacterEntity character = Character();
        character.Auras.Add(Bleed(T0, new ObjectGuid(ObjectType.Creature, 77)), T0);

        Assert.Equal(0UL, Assert.Single(CharacterSaveSnapshot.Take(character).Batch.Auras!.Rows).CasterGuid);
    }

    [Fact]
    public void Write_nothing_for_a_character_that_never_held_an_aura() =>
        Assert.Null(CharacterSaveSnapshot.Take(Character()).Batch.Auras);

    /// <summary>The last aura ended since the last save: the save deletes the rows and writes none.</summary>
    [Fact]
    public void Delete_the_rows_once_the_last_aura_ended()
    {
        CharacterEntity character = Character();
        ActiveAura bleed = Bleed(T0, new ObjectGuid());
        character.Auras.Add(bleed, T0);
        character.Auras.Remove(bleed, T0);

        Assert.Empty(CharacterSaveSnapshot.Take(character).Batch.Auras!.Rows);
    }

    /// <summary>Death ends every aura; a save of a dead character writes none, whatever memory still holds.</summary>
    [Fact]
    public void Write_no_aura_for_a_dead_character()
    {
        CharacterEntity character = Character();
        character.Auras.Add(Bleed(T0, new ObjectGuid()), T0);
        character.IsDead = true;

        Assert.Empty(CharacterSaveSnapshot.Take(character).Batch.Auras!.Rows);
    }

    /// <summary>
    /// The last tick came due at expiry and the save was taken before the aura pass took it: no time is left, but the
    /// tick is still owed, so the row is kept.
    /// </summary>
    [Fact]
    public void Keep_an_aura_with_no_time_left_whose_last_tick_is_still_owed()
    {
        CharacterEntity character = Character();
        character.Auras.Add(Bleed(T0, new ObjectGuid()), T0);
        _clock.Advance(TimeSpan.FromSeconds(12));
        character.Auras.All[0].Schedule = character.Auras.All[0].Schedule.AfterTicks(3);

        CharacterAura row = Assert.Single(CharacterSaveSnapshot.Take(character).Batch.Auras!.Rows);
        Assert.Equal((0u, 1), (row.RemainingMs, row.TicksLeft));
    }

    private static async Task<StaticData> DataAsync(params AuraTemplate[] auras) =>
        await TestStaticData.LoadAsync(TestStaticData.Repositories(
            classStats: () => [new ClassLevelStat { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 }],
            auras: () => auras));

    private static CharacterAura Saved(uint auraId, uint remainingMs, int ticksLeft, int slot = 0) => new()
    {
        CharacterId = new CharacterId(913_102),
        Slot = slot,
        AuraId = auraId,
        CasterGuid = 0,
        Stacks = 2,
        RemainingMs = remainingMs,
        DurationMs = 12000,
        TicksLeft = ticksLeft,
        TickAmount = 3f,
        CritPct = 0f,
        CasterLevel = 1,
        AppliedAt = T0.UtcDateTime,
    };

    private static void Restore(CharacterEntity character, StaticData data, params CharacterAura[] rows) =>
        AuraRestore.Restore(character, rows, data, 32, NullLogger.Instance);

    /// <summary>Logged out with 7.5 s left and three ticks owed, back a day later: the same, held from select.</summary>
    [Fact]
    public async Task Resume_a_saved_aura_with_the_time_and_ticks_it_had_left()
    {
        StaticData data = await DataAsync(AuraTestData.Bleed());
        CharacterEntity character = Character(913_102);
        _clock.Advance(TimeSpan.FromDays(1));
        DateTimeOffset dayLater = _clock.GetUtcNow();
        CharacterAura saved = Saved(901, 7500, 3);
        saved.PeriodicCarry = 0.4d;

        Restore(character, data, saved);

        ActiveAura bleed = Assert.Single(character.Auras.All);
        Assert.Equal((dayLater.AddMilliseconds(7500), 3, 2u), (bleed.Schedule.ExpiresAt, bleed.Schedule.TicksLeft, bleed.Stacks));
        Assert.Equal(0, bleed.Schedule.Due(dayLater.AddMilliseconds(1499)));
        Assert.Equal(1, bleed.Schedule.Due(dayLater.AddMilliseconds(1500)));
        Assert.Equal(3f, bleed.Snapshot.PerTickPerStack);
        Assert.Equal(0.4d, bleed.PeriodicCarry);   // the fraction its ticks had earned and not yet dealt
        Assert.Equal(dayLater, character.Auras.HeldSince);   // its time stands still until it enters the world
        Assert.False(character.Auras.HasChanges);   // the client gets a list when it enters the world
    }

    /// <summary>A save taken while the character still waits to enter reads the time left when it stopped.</summary>
    [Fact]
    public async Task Save_the_time_left_when_it_stopped_while_the_character_waits_to_enter()
    {
        StaticData data = await DataAsync(AuraTestData.Bleed());
        CharacterEntity character = Character(913_102);
        Restore(character, data, Saved(901, 7500, 3));
        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(7500u, Assert.Single(CharacterSaveSnapshot.Take(character).Batch.Auras!.Rows).RemainingMs);
    }

    /// <summary>
    /// Both cases a save can leave owed: a tick that came due before the save with time still left, and the last tick
    /// at an end already reached. Each is kept, and owed at once; no more than one is believed.
    /// </summary>
    [Fact]
    public async Task Keep_one_tick_already_owed_at_save_and_owe_it_at_once()
    {
        StaticData data = await DataAsync(AuraTestData.Bleed());
        CharacterEntity character = Character(913_105);
        DateTimeOffset now = _clock.GetUtcNow();

        Restore(character, data, Saved(901, 7500, 4), Saved(901, 0, 1, slot: 1), Saved(901, 7500, 9, slot: 2));

        Assert.Equal([4, 1, 4], character.Auras.All.Select(a => a.Schedule.TicksLeft));
        Assert.Equal([1, 1, 1], character.Auras.All.Select(a => a.Schedule.Due(now)));
        Assert.Equal(2, character.Auras.All[0].Schedule.Due(now.AddMilliseconds(1500)));
    }

    [Fact]
    public async Task Drop_a_saved_aura_whose_template_is_no_longer_loaded()
    {
        CharacterEntity character = Character(913_103);

        Restore(character, await DataAsync(AuraTestData.Bleed()), Saved(999, 7500, 3), Saved(901, 3000, 1, slot: 1));

        Assert.Equal([901u], character.Auras.All.Select(a => a.Id.Value));
    }

    /// <summary>
    /// A row dropped at select (its template gone, its snapshot unbelievable, past the cap) is deleted by the next save:
    /// the restore marks the auras changed, so that save rewrites them. A restore that keeps every row marks nothing.
    /// </summary>
    [Theory]
    [InlineData("kept", false)]
    [InlineData("template gone", true)]
    [InlineData("bad snapshot", true)]
    [InlineData("over the cap", true)]
    public async Task Have_the_next_save_delete_a_row_dropped_at_select(string drop, bool marked)
    {
        CharacterEntity character = Character(913_113);
        CharacterAura second = Saved(901, 3000, 1, slot: 1);
        switch (drop)
        {
            case "template gone": second.AuraId = 999; break;
            case "bad snapshot": second.TickAmount = float.NaN; break;
        }

        AuraRestore.Restore(character, [Saved(901, 7500, 3), second], await DataAsync(AuraTestData.Bleed()),
            drop == "over the cap" ? 1 : 32, NullLogger.Instance);

        Assert.Equal(marked, character.SaveState.AurasDirty);
        Assert.Equal(marked ? 1 : 2, CharacterSaveSnapshot.Take(character).Batch.Auras!.Rows.Count);
    }

    [Fact]
    public async Task Bring_a_creature_caster_back_as_nobody()
    {
        CharacterEntity character = Character(913_106);
        CharacterAura fromBoar = Saved(901, 7500, 3);
        fromBoar.CasterGuid = new ObjectGuid(ObjectType.Creature, 77).RawValue;
        CharacterAura fromPlayer = Saved(901, 7500, 3, slot: 1);
        fromPlayer.CasterGuid = new ObjectGuid(ObjectType.Character, 12).RawValue;

        Restore(character, await DataAsync(AuraTestData.Bleed()), fromBoar, fromPlayer);

        Assert.Equal([0UL, fromPlayer.CasterGuid], character.Auras.All.Select(a => a.CasterGuid.RawValue));
    }

    /// <summary>After a restart the creature numbered as the saved caster is another creature: it is credited nothing.</summary>
    [Fact]
    public async Task Credit_no_creature_that_has_the_saved_casters_guid()
    {
        var h = new AuraHarness();
        h.Use(AuraTestData.Bleed());
        Creature boar = h.Creature(77);
        CharacterEntity player = Character(913_107, h.Time);
        player.Health = 500;
        player.CurrentHealth = 500;
        h.Characters[player.Guid] = player;
        CharacterAura row = Saved(901, 7500, 3);
        row.Stacks = 1;
        row.CasterGuid = boar.Guid.RawValue;

        Restore(player, await DataAsync(AuraTestData.Bleed()), row);
        player.Auras.ResumeHeld(h.Time.GetUtcNow());
        h.Advance(TimeSpan.FromMilliseconds(1500));
        h.Auras.Update();

        h.Outcomes.Received(1).PeriodicTick(null, player, 3u, new AuraId(901), HitResult.None, false);
        h.Outcomes.DidNotReceive().PeriodicTick(boar, Arg.Any<Avalon.World.Public.Units.IUnit>(), Arg.Any<uint>(),
            Arg.Any<AuraId>(), Arg.Any<HitResult>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Cap_restored_stacks_at_what_the_template_allows_now()
    {
        AuraTemplate bleed = AuraTestData.Bleed();
        bleed.MaxStacks = 2;
        CharacterEntity character = Character(913_108);
        CharacterAura row = Saved(901, 7500, 3);
        row.Stacks = 3;

        Restore(character, await DataAsync(bleed), row);

        Assert.Equal(2u, Assert.Single(character.Auras.All).Stacks);
    }

    [Fact]
    public async Task Restore_no_more_than_a_unit_may_hold_keeping_the_earliest_applied()
    {
        CharacterEntity character = Character(913_109);
        CharacterAura late = Saved(901, 7500, 3);
        late.AppliedAt = T0.UtcDateTime.AddSeconds(5);
        CharacterAura early = Saved(901, 7500, 3, slot: 1);
        CharacterAura middle = Saved(901, 7500, 3, slot: 2);
        middle.AppliedAt = T0.UtcDateTime.AddSeconds(1);

        AuraRestore.Restore(character, [late, early, middle], await DataAsync(AuraTestData.Bleed()), 2, NullLogger.Instance);

        Assert.Equal([T0.UtcDateTime, T0.UtcDateTime.AddSeconds(1)], character.Auras.All.Select(a => a.AppliedAt));
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(3f, float.NaN)]
    [InlineData(3f, -5f)]
    public async Task Drop_a_saved_aura_whose_snapshot_is_not_a_number_of_zero_or_more(float tickAmount, float critPct)
    {
        CharacterEntity character = Character(913_110);
        CharacterAura bad = Saved(901, 7500, 3);
        bad.TickAmount = tickAmount;
        bad.CritPct = critPct;

        Restore(character, await DataAsync(AuraTestData.Bleed()), bad, Saved(901, 3000, 1, slot: 1));

        Assert.Equal(3000u, UnitAuras.RemainingMs(Assert.Single(character.Auras.All), _clock.GetUtcNow()));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(1.0d)]
    [InlineData(-0.25d)]
    [InlineData(double.PositiveInfinity)]
    public async Task Start_a_carried_fraction_over_when_it_is_not_one_under_a_whole_point(double stored)
    {
        CharacterEntity character = Character(913_111);
        CharacterAura row = Saved(901, 7500, 3);
        row.PeriodicCarry = stored;

        Restore(character, await DataAsync(AuraTestData.Bleed()), row);

        Assert.Equal(0d, Assert.Single(character.Auras.All).PeriodicCarry);
    }

    /// <summary>A Warrior's 240 health under a restored +20 % health aura is 288, and select fills it.</summary>
    [Fact]
    public async Task Fold_a_restored_modifier_aura_into_the_characters_stats()
    {
        AuraTemplate vigour = AuraTestData.Fortified();
        vigour.Modifiers[0].Stat = AuraStat.MaxHealth;
        StaticData data = await DataAsync(vigour);
        CharacterEntity character = Character(913_104);
        Assert.True(Avalon.World.Characters.CharacterStatsRefresh.Apply(character, data,
            Avalon.World.Characters.CurrentValues.EnterWorld));
        Assert.Equal(240u, character.Health);
        CharacterAura row = Saved(905, 20000, 0);
        row.Stacks = 1;

        Restore(character, data, row);

        Assert.Equal(288u, character.Health);
        Assert.Equal(288u, character.CurrentHealth);
    }

    /// <summary>
    /// The restore throws once it has loaded the auras (here, while folding a broken modifier into the stats): the
    /// character enters with none, and its stats are refreshed again without them.
    /// </summary>
    [Fact]
    public async Task Leave_no_aura_and_no_aura_totals_when_the_restore_throws()
    {
        AuraTemplate vigour = AuraTestData.Fortified();
        vigour.Modifiers[0].Stat = AuraStat.MaxHealth;
        StaticData data = await DataAsync(vigour);
        vigour.Modifiers.Add(null!);   // the catalog holds this very template: folding it now throws
        CharacterEntity character = Character(913_112);
        Assert.True(Avalon.World.Characters.CharacterStatsRefresh.Apply(character, data,
            Avalon.World.Characters.CurrentValues.EnterWorld));
        character.CurrentHealth = 100;
        CharacterAura row = Saved(905, 20000, 0);
        row.Stacks = 1;

        AuraRestore.RestoreOrNone(character, [row], data, 32, NullLogger.Instance);

        Assert.Equal(0, character.Auras.Count);
        Assert.Equal(AuraStatTotals.Empty, character.Auras.StatTotals);
        Assert.Equal((240u, 240u), (character.Health, character.CurrentHealth));   // refreshed again, as select does
    }

    private async Task<MapInstance> InstanceAsync(StaticData data) =>
        TestMapInstances.BuildCasting(out _, world: NewWorld(data), time: _clock);

    /// <summary>
    /// Restored at select, its client then loads for 10 s: it enters with all 7.5 s and its three ticks, which land at
    /// 1.5, 4.5 and 7.5 s from its entry, each once, whether the instance had players or stood empty.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Start_the_restored_time_when_the_character_enters_its_instance(bool occupied)
    {
        StaticData data = await DataAsync(AuraTestData.Bleed());
        MapInstance instance = await InstanceAsync(data);
        if (occupied)
            Join(instance, Character(913_120));
        instance.Update(TimeSpan.FromSeconds(1d / 60d));   // empty, it now stands still

        CharacterEntity character = Character(913_121);
        character.Health = 500;
        character.CurrentHealth = 400;
        CharacterAura row = Saved(901, 7500, 3);
        row.Stacks = 1;
        Restore(character, data, row);

        _clock.Advance(TimeSpan.FromSeconds(10));   // the loading screen
        Join(instance, character);
        DateTimeOffset entered = _clock.GetUtcNow();

        ActiveAura bleed = Assert.Single(character.Auras.All);
        Assert.Equal((entered.AddMilliseconds(7500), 3), (bleed.Schedule.ExpiresAt, bleed.Schedule.TicksLeft));
        Assert.Null(character.Auras.HeldSince);

        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        Assert.Equal(400u, character.CurrentHealth);   // nothing paid for the wait

        uint[] expected = [397u, 394u, 391u];
        TimeSpan[] steps = [TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)];
        for (int i = 0; i < steps.Length; i++)
        {
            _clock.Advance(steps[i]);
            instance.Update(TimeSpan.FromSeconds(1d / 60d));
            Assert.Equal(expected[i], character.CurrentHealth);
        }

        Assert.Equal(0, character.Auras.Count);
    }

    /// <summary>A tick already owed at save is paid on the first pass after entry, even into an instance that stood empty.</summary>
    [Fact]
    public async Task Pay_a_tick_owed_at_save_on_entering_an_instance_that_stood_empty()
    {
        StaticData data = await DataAsync(AuraTestData.Bleed());
        MapInstance instance = await InstanceAsync(data);
        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        CharacterEntity character = Character(913_122);
        character.Health = 500;
        character.CurrentHealth = 400;
        CharacterAura row = Saved(901, 0, 1);
        row.Stacks = 1;
        Restore(character, data, row);
        _clock.Advance(TimeSpan.FromSeconds(10));
        Join(instance, character);

        _clock.Advance(TimeSpan.FromSeconds(1d / 60d));   // the next tick comes a moment after the entry
        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(397u, character.CurrentHealth);
        Assert.Equal(0, character.Auras.Count);
    }
}
