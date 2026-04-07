using Avalon.Domain.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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
        RegenConfiguration? config = null)
    {
        var character = new Character { Id = 1u, Health = (int)health, Power1 = (int)power };
        var entity = new CharacterEntity(
            NullLoggerFactory.Instance,
            character,
            config ?? new RegenConfiguration());

        entity.CurrentHealth = currentHealth;
        entity.CurrentPower = currentPower;
        entity.Stamina = stamina;
        entity.RegenStat = regenStat;
        entity.PowerType = powerType;
        entity.Spells.Load(Array.Empty<ISpell>());
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
        var entity = MakeCharacter(power: 100, currentPower: 50, regenStat: 10,
            powerType: PowerType.Fury, config: config);

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
