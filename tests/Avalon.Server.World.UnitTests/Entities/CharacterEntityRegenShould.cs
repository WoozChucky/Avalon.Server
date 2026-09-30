using Avalon.Domain.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Loot;

namespace Avalon.Server.World.UnitTests.Entities;

public class CharacterEntityRegenShould
{
    // ──────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────

    private static CharacterEntity MakeCharacter(
        uint health = 100u,
        uint currentHealth = 50u,
        uint power = 100u,
        uint currentPower = 50u,
        uint stamina = 10u,
        uint regenStat = 10u,
        PowerType powerType = PowerType.Mana,
        RegenConfiguration? config = null,
        float furyDecayPerSecond = GameConfiguration.DefaultFuryDecayPerSecond)
    {
        var character = new Character { Id = 1u, Health = (int)health, Power1 = (int)power };
        var entity = new CharacterEntity(
            NullLoggerFactory.Instance,
            character,
            config ?? new RegenConfiguration(),
            furyDecayPerSecond: furyDecayPerSecond);

        entity.CurrentHealth = currentHealth;
        entity.CurrentPower = currentPower;
        entity.Stamina = stamina;
        entity.RegenStat = regenStat;
        entity.PowerType = powerType;
        entity.Spells.Load(Array.Empty<IAbility>());
        return entity;
    }

    // ──────────────────────────────────────────────
    // Health regeneration
    // ──────────────────────────────────────────────

    [Fact]
    public void Update_RegeneratesHealth_OutOfCombat()
    {
        var config = new RegenConfiguration { HealthRegenOutOfCombatPerStamina = 1.0f };
        var entity = MakeCharacter(health: 100, currentHealth: 50, stamina: 10, config: config);

        entity.Update(TimeSpan.FromSeconds(1));

        // Stamina(10) * coeff(1.0) * dt(1s) = 10 → 50 + 10 = 60
        Assert.Equal(60u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_SkipsHealthRegen_WhenInCombat()
    {
        var entity = MakeCharacter(health: 100, currentHealth: 50, stamina: 10);
        entity.MarkCombat();

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(50u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_SkipsHealthRegen_WhenEntityIsDead()
    {
        var entity = MakeCharacter(health: 100, currentHealth: 0, stamina: 10);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(0u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_SkipsHealthRegen_WhenHealthAtMax()
    {
        var entity = MakeCharacter(health: 100, currentHealth: 100, stamina: 10);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(100u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_SkipsHealthRegen_WhenStaminaIsZero()
    {
        var entity = MakeCharacter(health: 100, currentHealth: 50, stamina: 0);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(50u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_CapsHealthAtMax_WhenRegenWouldExceed()
    {
        var config = new RegenConfiguration { HealthRegenOutOfCombatPerStamina = 100.0f };
        var entity = MakeCharacter(health: 100, currentHealth: 99, stamina: 100, config: config);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(100u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_RegeneratesHealth_OutOfCombat_AtTheConfiguredRate_AtTheServerTick()
    {
        // Default rate: 22 Stamina x 0.5 = 11 health a second out of combat. The old per-tick floor gave a whole
        // point every 1/60 s tick, 60 a second.
        var entity = MakeCharacter(health: 200, currentHealth: 100, stamina: 22);

        for (int tick = 0; tick < 60; tick++)
            entity.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(111u, entity.CurrentHealth);
    }

    [Fact]
    public void Update_DropsTheCarriedHealthFraction_WhenHealthIsFull()
    {
        // 23 x 0.5 = 11.5 a second: 1.9 s from 199 fills the pool and leaves 0.85 carried.
        var entity = MakeCharacter(health: 200, currentHealth: 199, stamina: 23);
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
    // Power regeneration
    // ──────────────────────────────────────────────

    [Fact]
    public void Update_RegeneratesMana_OutOfCombat()
    {
        var config = new RegenConfiguration { PowerRegenOutOfCombatPerStat = 1.0f };
        var entity = MakeCharacter(power: 100, currentPower: 50, regenStat: 10,
            powerType: PowerType.Mana, config: config);

        entity.Update(TimeSpan.FromSeconds(1));

        // RegenStat(10) * coeff(1.0) * dt(1s) = 10 → 50 + 10 = 60
        Assert.Equal(60u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_RegeneratesMana_InCombat_AtLowerRate()
    {
        var config = new RegenConfiguration
        {
            PowerRegenOutOfCombatPerStat = 1.0f,
            PowerRegenInCombatPerStat = 0.1f
        };
        var entity = MakeCharacter(power: 100, currentPower: 50, regenStat: 10,
            powerType: PowerType.Mana, config: config);
        entity.MarkCombat();

        entity.Update(TimeSpan.FromSeconds(1));

        // RegenStat(10) * coeff(0.1) * dt(1s) = 1 → 50 + 1 = 51
        Assert.Equal(51u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_RegeneratesEnergy_OutOfCombat()
    {
        var config = new RegenConfiguration { PowerRegenOutOfCombatPerStat = 1.0f };
        var entity = MakeCharacter(power: 100, currentPower: 50, regenStat: 10,
            powerType: PowerType.Energy, config: config);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(60u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_SkipsPowerRegen_ForFuryType()
    {
        var config = new RegenConfiguration { PowerRegenOutOfCombatPerStat = 1.0f };
        // Decay off: this pins that Fury never regenerates; its decay (#526) is FuryLifecycleShould's.
        var entity = MakeCharacter(power: 100, currentPower: 50, regenStat: 10,
            powerType: PowerType.Fury, config: config, furyDecayPerSecond: 0f);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(50u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_SkipsPowerRegen_ForNoneType()
    {
        var config = new RegenConfiguration { PowerRegenOutOfCombatPerStat = 1.0f };
        var entity = MakeCharacter(power: 100, currentPower: 50, regenStat: 10,
            powerType: PowerType.None, config: config);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(50u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_SkipsPowerRegen_WhenRegenStatIsZero()
    {
        var config = new RegenConfiguration { PowerRegenOutOfCombatPerStat = 1.0f };
        var entity = MakeCharacter(power: 100, currentPower: 50, regenStat: 0,
            powerType: PowerType.Mana, config: config);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(50u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_SkipsPowerRegen_WhenPowerAtMax()
    {
        var config = new RegenConfiguration { PowerRegenOutOfCombatPerStat = 1.0f };
        var entity = MakeCharacter(power: 100, currentPower: 100, regenStat: 10,
            powerType: PowerType.Mana, config: config);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(100u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_CapsCurrentPowerAtMax_WhenRegenWouldExceed()
    {
        var config = new RegenConfiguration { PowerRegenOutOfCombatPerStat = 100.0f };
        var entity = MakeCharacter(power: 100, currentPower: 99, regenStat: 100,
            powerType: PowerType.Mana, config: config);

        entity.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(100u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_RegeneratesMana_InCombat_AtTheConfiguredRate_AtTheServerTick()
    {
        // Default rates: 23 Intellect x 0.05 = 1.15 Mana a second in combat. The old per-tick floor gave a whole point
        // every 1/60 s tick, 60 a second, and filled this pool within a second.
        var entity = MakeCharacter(power: 100, currentPower: 50, regenStat: 23, powerType: PowerType.Mana);
        entity.MarkCombat();

        for (int tick = 0; tick < 60; tick++)
            entity.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(51u, entity.CurrentPower!.Value);
    }

    [Fact]
    public void Update_DropsTheCarriedFraction_WhenThePoolIsFull()
    {
        // Out of combat, 23 x 0.3 = 6.9 a second: one second from 99 fills the pool and leaves 0.9 carried.
        var entity = MakeCharacter(power: 100, currentPower: 99, regenStat: 23, powerType: PowerType.Mana);
        entity.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(100u, entity.CurrentPower!.Value);

        entity.Update(TimeSpan.FromSeconds(1d / 60d));   // full: the 0.9 is dropped, never banked
        entity.CurrentPower = 50;
        entity.Update(TimeSpan.FromSeconds(1d / 60d));   // 0.115 owed: no whole point yet (0.9 kept would give 51)

        Assert.Equal(50u, entity.CurrentPower!.Value);
    }

    // ──────────────────────────────────────────────
    // Combat state
    // ──────────────────────────────────────────────

    [Fact]
    public void IsInCombat_ReturnsFalse_BeforeMarkCombat()
    {
        var entity = MakeCharacter();

        Assert.False(entity.IsInCombat);
    }

    [Fact]
    public void IsInCombat_ReturnsTrue_ImmediatelyAfterMarkCombat()
    {
        var config = new RegenConfiguration { CombatLeaveDelaySeconds = 5f };
        var entity = MakeCharacter(config: config);

        entity.MarkCombat();

        Assert.True(entity.IsInCombat);
    }
}
