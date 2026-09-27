using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class CreatureShould
{
    private static Creature MakeCreature(Vector3 position = default) => new Creature
    {
        Guid = new ObjectGuid(ObjectType.Creature, 1u),
        Position = position,
        Orientation = Vector3.zero
    };

    // ──────────────────────────────────────────────
    // LookAt
    // ──────────────────────────────────────────────

    [Fact]
    public void LookAt_SetsYOrientation_WhenTargetIsToTheRight()
    {
        // target right (+X), atan2(1,0) = π/2 ≈ 90°
        var creature = MakeCreature(Vector3.zero);
        creature.LookAt(new Vector3(1, 0, 0));

        Assert.Equal(0, creature.Orientation.x);
        Assert.InRange(creature.Orientation.y, 89f, 91f);
        Assert.Equal(0, creature.Orientation.z);
    }

    [Fact]
    public void LookAt_SetsZeroYOrientation_WhenTargetIsAhead()
    {
        // target ahead (+Z), atan2(0,1) = 0
        var creature = MakeCreature(Vector3.zero);
        creature.LookAt(new Vector3(0, 0, 1));

        Assert.Equal(0f, creature.Orientation.x, precision: 4);
        Assert.InRange(creature.Orientation.y, -0.1f, 0.1f);
        Assert.Equal(0f, creature.Orientation.z, precision: 4);
    }

    [Fact]
    public void LookAt_SetsNegativeY_WhenTargetIsToTheLeft()
    {
        // target left (-X), atan2(-1,0) = -π/2 ≈ -90°
        var creature = MakeCreature(Vector3.zero);
        creature.LookAt(new Vector3(-1, 0, 0));

        Assert.InRange(creature.Orientation.y, -91f, -89f);
    }

    [Fact]
    public void LookAt_HandlesNonOriginPosition()
    {
        var creature = MakeCreature(new Vector3(5, 0, 5));
        // target directly one unit to the right from creature
        creature.LookAt(new Vector3(6, 0, 5));

        Assert.InRange(creature.Orientation.y, 89f, 91f);
    }

    // ──────────────────────────────────────────────
    // IsLookingAt
    // ──────────────────────────────────────────────

    [Fact]
    public void IsLookingAt_ReturnsTrue_WhenAlreadyFacingExactly()
    {
        var creature = MakeCreature(Vector3.zero);
        creature.LookAt(new Vector3(1, 0, 0));

        // Should be looking at the same direction now
        Assert.True(creature.IsLookingAt(new Vector3(1, 0, 0)));
    }

    [Fact]
    public void IsLookingAt_ReturnsFalse_WhenFacingAwayFromTarget()
    {
        var creature = MakeCreature(Vector3.zero);
        // Face right (+X, y≈90°)
        creature.LookAt(new Vector3(1, 0, 0));

        // Check against the opposite direction (-X, y≈-90°) — diff ≈ 180°
        Assert.False(creature.IsLookingAt(new Vector3(-1, 0, 0)));
    }

    [Fact]
    public void IsLookingAt_ReturnsTrue_WithinThreshold()
    {
        var creature = MakeCreature(Vector3.zero);
        // Face right so orientation.y ≈ 90°
        creature.LookAt(new Vector3(1, 0, 0));

        // IsLookingAt computes target yaw and compares — using same target should be within threshold
        Assert.True(creature.IsLookingAt(new Vector3(1, 0, 0), threshold: 5f));
    }

    [Fact]
    public void IsLookingAt_ReturnsFalse_WhenDiffExceedsThreshold()
    {
        var creature = MakeCreature(Vector3.zero);
        creature.LookAt(new Vector3(1, 0, 0)); // y ≈ 90°

        // Check off-axis target where diff is large
        Assert.False(creature.IsLookingAt(new Vector3(-1, 0, 0), threshold: 5f));
    }

    // ──────────────────────────────────────────────
    // The modding API cannot raise a kill or a broadcast (#546)
    // ──────────────────────────────────────────────

    /// <summary>
    /// A kill grants loot and experience, so only the World-side combat service reports one. A
    /// <c>Died</c> on <see cref="ICreature" />, part of the modding API, would let a mod grant both.
    /// </summary>
    [Fact]
    public void Offer_No_Way_To_Report_A_Kill_On_The_Modding_API()
    {
        Assert.Null(typeof(ICreature).GetMethod("Died"));
        Assert.Null(typeof(Creature).GetMethod("Died"));
    }

    /// <summary>A unit's broadcasts go through its own instance, never through the unit.</summary>
    [Theory]
    [InlineData("SendAttackAnimation")]
    [InlineData("SendFinishCastAnimation")]
    [InlineData("SendInterruptedCastAnimation")]
    public void Offer_No_Broadcast_On_The_Unit(string method)
    {
        Assert.Null(typeof(IUnit).GetMethod(method));
    }

    /// <summary>No static event is left on either entity for an instance to subscribe to.</summary>
    [Fact]
    public void Declare_No_Static_Events()
    {
        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Assert.Empty(typeof(Creature).GetEvents(statics));
        Assert.Empty(typeof(CharacterEntity).GetEvents(statics));
    }

    // ──────────────────────────────────────────────
    // OnHit / Script delegation
    // ──────────────────────────────────────────────

    [Fact]
    public void OnHit_DoesNotThrow_WhenScriptIsNull()
    {
        var creature = MakeCreature();
        creature.Script = null;

        var ex = Record.Exception(() => creature.OnHit(Substitute.For<IUnit>(), 50u));
        Assert.Null(ex);
    }
}
