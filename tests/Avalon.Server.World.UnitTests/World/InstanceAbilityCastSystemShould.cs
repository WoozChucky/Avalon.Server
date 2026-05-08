using Avalon.Common.ValueObjects;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.World;

public class InstanceAbilityCastSystemShould
{
    private readonly IScriptManager _scriptManager = Substitute.For<IScriptManager>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly ISimulationContext _simulationContext = Substitute.For<ISimulationContext>();
    private readonly InstanceAbilityCastSystem _sut;

    public InstanceAbilityCastSystemShould()
    {
        _serviceProvider.GetService(typeof(IScriptManager)).Returns(_scriptManager);
        _sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, _serviceProvider, _scriptManager, _simulationContext);
    }

    private static IAbility MakeAbility(uint cost) =>
        MakeAbility(cost, PowerType.Mana); // default power type for ability metadata is irrelevant here

    private static IAbility MakeAbility(uint cost, PowerType _)
    {
        var ability = Substitute.For<IAbility>();
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "TestAbility",
            Cost = cost,
            CastTime = 0,
            Cooldown = 0,
            ScriptName = "TestScript",
            Effects = SpellEffect.None
        });
        ability.CooldownTimer.Returns(0f);
        ability.CastTimeTimer.Returns(0f);
        ability.Casting.Returns(false);
        return ability;
    }

    private static ICharacter MakeCharacter(PowerType powerType, uint currentPower)
    {
        var character = Substitute.For<ICharacter>();
        character.PowerType.Returns(powerType);
        character.CurrentPower.Returns(currentPower);
        character.Position.Returns(default(Avalon.Common.Mathematics.Vector3));
        return character;
    }

    // ── power deduction ────────────────────────────────────────────────────────

    [Fact]
    public void DeductPower_WhenManaCharacterHasSufficientMana()
    {
        var character = MakeCharacter(PowerType.Mana, currentPower: 100);
        var ability = MakeAbility(cost: 30);

        bool queued = _sut.QueueAbility(character, null, ability);

        Assert.True(queued);
        character.Received(1).CurrentPower = 100u - 30u;
    }

    [Fact]
    public void DeductPower_WhenEnergyCharacterHasSufficientEnergy()
    {
        var character = MakeCharacter(PowerType.Energy, currentPower: 50);
        var ability = MakeAbility(cost: 20);

        bool queued = _sut.QueueAbility(character, null, ability);

        Assert.True(queued);
        character.Received(1).CurrentPower = 50u - 20u;
    }

    [Fact]
    public void QueueAbility_WhenCostIsZero_RegardlessOfPowerType()
    {
        foreach (var pt in new[] { PowerType.Mana, PowerType.Energy, PowerType.Fury, PowerType.None })
        {
            var character = MakeCharacter(pt, currentPower: 0);
            var ability = MakeAbility(cost: 0);

            bool queued = _sut.QueueAbility(character, null, ability);

            Assert.True(queued, $"Expected ability to be queued for PowerType.{pt} with zero cost");
            character.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        }
    }

    // ── insufficient power ─────────────────────────────────────────────────────

    [Fact]
    public void ReturnFalse_WhenManaIsInsufficient()
    {
        var character = MakeCharacter(PowerType.Mana, currentPower: 10);
        var ability = MakeAbility(cost: 30);

        bool queued = _sut.QueueAbility(character, null, ability);

        Assert.False(queued);
        character.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }

    [Fact]
    public void ReturnFalse_WhenEnergyIsInsufficient()
    {
        var character = MakeCharacter(PowerType.Energy, currentPower: 5);
        var ability = MakeAbility(cost: 10);

        bool queued = _sut.QueueAbility(character, null, ability);

        Assert.False(queued);
    }

    // ── Fury and None blocked ──────────────────────────────────────────────────

    [Fact]
    public void ReturnFalse_ForFuryCasterWithCostAbility()
    {
        var character = MakeCharacter(PowerType.Fury, currentPower: 999);
        var ability = MakeAbility(cost: 1);

        bool queued = _sut.QueueAbility(character, null, ability);

        Assert.False(queued);
        character.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }

    [Fact]
    public void ReturnFalse_ForNonePowerTypeCasterWithCostAbility()
    {
        var character = MakeCharacter(PowerType.None, currentPower: 999);
        var ability = MakeAbility(cost: 1);

        bool queued = _sut.QueueAbility(character, null, ability);

        Assert.False(queued);
        character.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }
}
