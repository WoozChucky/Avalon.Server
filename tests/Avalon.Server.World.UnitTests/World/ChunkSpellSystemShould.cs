using Avalon.Common.ValueObjects;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Spells;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.World;

public class ChunkSpellSystemShould
{
    private readonly IScriptManager _scriptManager = Substitute.For<IScriptManager>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly ChunkSpellSystem _sut;

    public ChunkSpellSystemShould()
    {
        _serviceProvider.GetService(typeof(IScriptManager)).Returns(_scriptManager);
        _sut = new ChunkSpellSystem(NullLoggerFactory.Instance, _serviceProvider, _scriptManager);
    }

    private static ISpell MakeSpell(uint cost) =>
        MakeSpell(cost, PowerType.Mana); // default power type for spell metadata is irrelevant here

    private static ISpell MakeSpell(uint cost, PowerType _)
    {
        var spell = Substitute.For<ISpell>();
        spell.Metadata.Returns(new SpellMetadata
        {
            Name = "TestSpell",
            Cost = cost,
            CastTime = 0,
            Cooldown = 0,
            ScriptName = "TestScript",
            Effects = SpellEffect.None
        });
        spell.CooldownTimer.Returns(0f);
        spell.CastTimeTimer.Returns(0f);
        spell.Casting.Returns(false);
        return spell;
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
        var spell = MakeSpell(cost: 30);

        bool queued = _sut.QueueSpell(character, null, spell);

        Assert.True(queued);
        character.Received(1).CurrentPower = 100u - 30u;
    }

    [Fact]
    public void DeductPower_WhenEnergyCharacterHasSufficientEnergy()
    {
        var character = MakeCharacter(PowerType.Energy, currentPower: 50);
        var spell = MakeSpell(cost: 20);

        bool queued = _sut.QueueSpell(character, null, spell);

        Assert.True(queued);
        character.Received(1).CurrentPower = 50u - 20u;
    }

    [Fact]
    public void QueueSpell_WhenCostIsZero_RegardlessOfPowerType()
    {
        foreach (var pt in new[] { PowerType.Mana, PowerType.Energy, PowerType.Fury, PowerType.None })
        {
            var character = MakeCharacter(pt, currentPower: 0);
            var spell = MakeSpell(cost: 0);

            bool queued = _sut.QueueSpell(character, null, spell);

            Assert.True(queued, $"Expected spell to be queued for PowerType.{pt} with zero cost");
            character.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        }
    }

    // ── insufficient power ─────────────────────────────────────────────────────

    [Fact]
    public void ReturnFalse_WhenManaIsInsufficient()
    {
        var character = MakeCharacter(PowerType.Mana, currentPower: 10);
        var spell = MakeSpell(cost: 30);

        bool queued = _sut.QueueSpell(character, null, spell);

        Assert.False(queued);
        character.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }

    [Fact]
    public void ReturnFalse_WhenEnergyIsInsufficient()
    {
        var character = MakeCharacter(PowerType.Energy, currentPower: 5);
        var spell = MakeSpell(cost: 10);

        bool queued = _sut.QueueSpell(character, null, spell);

        Assert.False(queued);
    }

    // ── Fury and None blocked ──────────────────────────────────────────────────

    [Fact]
    public void ReturnFalse_ForFuryCasterWithCostSpell()
    {
        var character = MakeCharacter(PowerType.Fury, currentPower: 999);
        var spell = MakeSpell(cost: 1);

        bool queued = _sut.QueueSpell(character, null, spell);

        Assert.False(queued);
        character.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }

    [Fact]
    public void ReturnFalse_ForNonePowerTypeCasterWithCostSpell()
    {
        var character = MakeCharacter(PowerType.None, currentPower: 999);
        var spell = MakeSpell(cost: 1);

        bool queued = _sut.QueueSpell(character, null, spell);

        Assert.False(queued);
        character.DidNotReceive().CurrentPower = Arg.Any<uint?>();
    }
}
