using Avalon.Combat;
using Avalon.Database.World.Seeding;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Entities;

public class CharacterEntityRegenShould
{
    // ──────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────

    private static readonly DateTimeOffset s_now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static CharacterEntity MakeCharacter(
        uint health = 100u,
        uint currentHealth = 50u,
        uint power = 100u,
        uint currentPower = 50u,
        uint stamina = 10u,
        TimeProvider? clock = null)
    {
        var character = new Character { Id = 1u, Health = (int)health, Power1 = (int)power };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, character, new RegenConfiguration(), clock);

        entity.PowerType = PowerType.Mana;
        entity.ApplyStats(new DerivedCharacterStats(MaxHealth: health, MaxPower: power, Stamina: 0, Strength: 0,
            Agility: 0, Intellect: 0, Armor: 0, BlockPct: 0f, Evasion: 0, CritPct: 0f), CurrentValues.EnterWorld,
            CombatSeed.Formula());
        entity.CurrentHealth = currentHealth;
        entity.CurrentPower = currentPower;
        entity.Stamina = stamina;
        entity.Spells.Load(Array.Empty<IAbility>());
        return entity;
    }

    // ──────────────────────────────────────────────
    // Health regeneration
    // ──────────────────────────────────────────────

    [Fact]
    public void Update_SkipsHealthRegen_WhenEntityIsDead()
    {
        CharacterEntity entity = MakeCharacter(health: 100, currentHealth: 0, stamina: 10);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(0u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_RegeneratesHealth_OutOfCombat_AtTheConfiguredRate_AtTheServerTick()
    {
        // Default rate: 22 Stamina x 0.5 = 11 health a second out of combat. The old per-tick floor gave a whole
        // point every 1/60 s tick, 60 a second.
        CharacterEntity entity = MakeCharacter(health: 200, currentHealth: 100, stamina: 22);

        for (int tick = 0; tick < 60; tick++)
            entity.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(111u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_DropsTheCarriedHealthFraction_WhenHealthIsFull()
    {
        // 23 x 0.5 = 11.5 a second: 1.9 s from 199 fills the pool and leaves 0.85 carried.
        CharacterEntity entity = MakeCharacter(health: 200, currentHealth: 199, stamina: 23);
        entity.Update(TimeSpan.FromSeconds(1.9));
        Assert.Equal(200u, entity.CurrentHealth);

        entity.Update(TimeSpan.FromSeconds(1d / 60d));   // full: the 0.85 is dropped, never banked
        entity.CurrentHealth = 150;
        entity.Update(TimeSpan.FromSeconds(1d / 60d));   // 0.19 owed: no whole point yet (0.85 kept would give 151)

        Assert.Equal(150u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_DropsTheCarriedHealthFraction_InCombat()
    {
        // 23 x 0.5 = 11.5 a second: one second from 100 leaves 0.5 carried; combat must drop it.
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var entity = new CharacterEntity(NullLoggerFactory.Instance, new Character { Id = 1u, Health = 200 },
            new RegenConfiguration(), clock);
        entity.CurrentHealth = 100;
        entity.Stamina = 23;
        entity.Spells.Load(Array.Empty<IAbility>());
        entity.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(111u, entity.CurrentHealth);

        entity.MarkCombat();
        entity.Update(TimeSpan.FromSeconds(1d / 60d));   // in combat: no regen, the 0.5 is dropped
        clock.Now += TimeSpan.FromSeconds(10);            // out of combat again

        // 30 ticks give 5.75: 5 points; with the 0.5 kept it would be 6.25, 6 points.
        for (int tick = 0; tick < 30; tick++)
            entity.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(116u, entity.CurrentHealth);
    }

    // ──────────────────────────────────────────────
    // Mana regeneration
    // ──────────────────────────────────────────────

    /// <summary>
    /// #1181: mana regenerates 4 % of its maximum a second out of combat and 1 % in it, and a cast in progress does not
    /// pause it; the dead and a full pool regenerate nothing. A maximum of 400 over sixty 1/60 s ticks.
    /// </summary>
    [Theory]
    [InlineData("out of combat", 100u, 116u)]
    [InlineData("in combat", 100u, 104u)]
    [InlineData("casting", 100u, 116u)]
    [InlineData("dead", 100u, 100u)]
    [InlineData("full", 400u, 400u)]
    public void Regenerate_mana_by_its_share_of_the_maximum(string state, uint start, uint expected)
    {
        var clock = new FixedTimeProvider(s_now);
        CharacterEntity entity = MakeCharacter(power: 400, currentPower: start, clock: clock);
        switch (state)
        {
            case "in combat":
                entity.MarkCombat();   // the clock is frozen, so the fight never ends
                break;
            case "casting":
                IAbility casting = Substitute.For<IAbility>();
                casting.Casting.Returns(true);
                entity.Spells.Load([casting]);
                break;
            case "dead":
                entity.IsDead = true;
                break;
        }

        for (int tick = 0; tick < 60; tick++)
            entity.Update(TimeSpan.FromSeconds(1d / 60d));

        if (state == "casting")
            Assert.True(entity.Spells.IsCasting);
        Assert.Equal(expected, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_DropsTheCarriedFraction_WhenThePoolIsFull()
    {
        // Out of combat, 4 % of 245 = 9.8 a second: one second from 244 fills the pool and leaves 0.8 carried.
        CharacterEntity entity = MakeCharacter(power: 245, currentPower: 244);
        entity.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(245u, entity.CurrentPower!.Value);

        entity.Update(TimeSpan.FromSeconds(1d / 60d));   // full: the 0.8 is dropped, never banked
        entity.CurrentPower = 150;
        entity.Update(TimeSpan.FromSeconds(0.05));       // 0.49 owed: no whole point yet (0.8 kept would give 151)

        Assert.Equal(150u, entity.CurrentPower!.Value);
    }
}
