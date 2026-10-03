using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Persistence;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>Every save writes the auras held with the time they have left; select brings them back, the time paused.</summary>
public class AuraSaveShould
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(T0);

    private CharacterEntity Character(uint id = 913_101)
    {
        var row = new Character
        {
            Id = new CharacterId(id), AccountId = new AccountId(1), Name = $"Tester{id}", Class = CharacterClass.Warrior,
            CreationDate = DateTime.UtcNow,
        };
        return new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration(), _clock) { Data = row };
    }

    private static ActiveAura Bleed(DateTimeOffset at, ObjectGuid caster) =>
        new(AuraTestData.Bleed(), caster, new AuraSource(new AbilityId(203), 1f, 0f, 0), 2, new AuraSnapshot(3f, 5f, 4),
            AuraSchedule.Start(at, 12000, 3000), 12000, at.UtcDateTime);

    [Fact]
    public void Write_every_held_aura_with_the_time_and_ticks_it_has_left()
    {
        CharacterEntity character = Character();
        var caster = new ObjectGuid(ObjectType.Creature, 77);
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

    private static async Task<StaticData> DataAsync(params AuraTemplate[] auras) =>
        await TestStaticData.LoadAsync(TestStaticData.Repositories(
            classStats: () => [new ClassLevelStat { Class = CharacterClass.Warrior, Level = 1, BaseHp = 20, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 }],
            auras: () => auras));

    private static CharacterAura Saved(uint auraId, uint remainingMs, int ticksLeft) => new()
    {
        CharacterId = new CharacterId(913_102), Slot = 0, AuraId = auraId, CasterGuid = 0, Stacks = 2,
        RemainingMs = remainingMs, DurationMs = 12000, TicksLeft = ticksLeft, TickAmount = 3f, CritPct = 0f, CasterLevel = 1,
        AppliedAt = T0.UtcDateTime,
    };

    /// <summary>Logged out with 7.5 s left and three ticks owed, back a day later: the same, from now.</summary>
    [Fact]
    public async Task Resume_a_saved_aura_with_the_time_and_ticks_it_had_left()
    {
        CharacterEntity character = Character(913_102);
        DateTimeOffset dayLater = T0.AddDays(1);
        CharacterAura saved = Saved(901, 7500, 3);
        saved.PeriodicCarry = 0.4d;

        AuraRestore.Restore(character, [saved], await DataAsync(AuraTestData.Bleed()), dayLater, NullLogger.Instance);

        ActiveAura bleed = Assert.Single(character.Auras.All);
        Assert.Equal((dayLater.AddMilliseconds(7500), 3, 2u), (bleed.Schedule.ExpiresAt, bleed.Schedule.TicksLeft, bleed.Stacks));
        Assert.Equal(0, bleed.Schedule.Due(dayLater.AddMilliseconds(1499)));
        Assert.Equal(1, bleed.Schedule.Due(dayLater.AddMilliseconds(1500)));
        Assert.Equal(3f, bleed.Snapshot.PerTickPerStack);
        Assert.Equal(0.4d, bleed.PeriodicCarry);   // the fraction its ticks had earned and not yet dealt
        Assert.False(character.Auras.HasChanges);   // the client gets a list when it enters the world
    }

    [Fact]
    public async Task Drop_a_saved_aura_whose_template_is_no_longer_loaded()
    {
        CharacterEntity character = Character(913_103);
        CharacterAura second = Saved(901, 3000, 1);
        second.Slot = 1;

        AuraRestore.Restore(character, [Saved(999, 7500, 3), second], await DataAsync(AuraTestData.Bleed()), T0,
            NullLogger.Instance);

        Assert.Equal([901u], character.Auras.All.Select(a => a.Id.Value));
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

        AuraRestore.Restore(character, [row], data, T0, NullLogger.Instance);

        Assert.Equal(288u, character.Health);
        Assert.Equal(288u, character.CurrentHealth);
    }
}
