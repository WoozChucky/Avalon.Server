using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

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

    /// <summary>The yaw is atan2(x, z) in degrees, measured from where the creature stands.</summary>
    [Theory]
    [InlineData(0f, 0f, 1f, 0f, 90f)]     // target right (+X)
    [InlineData(0f, 0f, 0f, 1f, 0f)]      // target ahead (+Z)
    [InlineData(0f, 0f, -1f, 0f, -90f)]   // target left (-X)
    [InlineData(5f, 5f, 6f, 5f, 90f)]     // one unit to the right of a creature away from the origin
    public void LookAt_SetsTheYawTowardTheTarget(float fromX, float fromZ, float toX, float toZ, float yaw)
    {
        Creature creature = MakeCreature(new Vector3(fromX, 0, fromZ));
        creature.LookAt(new Vector3(toX, 0, toZ));

        Assert.Equal(0, creature.Orientation.x);
        Assert.InRange(creature.Orientation.y, yaw - 0.1f, yaw + 0.1f);
        Assert.Equal(0, creature.Orientation.z);
    }

    // ──────────────────────────────────────────────
    // IsLookingAt
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData(1f, true)]     // the point it just turned to
    [InlineData(-1f, false)]   // the opposite way, 180 degrees off
    public void IsLookingAt_OnlyThePointItFaces(float x, bool expected)
    {
        Creature creature = MakeCreature(Vector3.zero);
        creature.LookAt(new Vector3(1, 0, 0));

        Assert.Equal(expected, creature.IsLookingAt(new Vector3(x, 0, 0)));
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
        const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Assert.Empty(typeof(Creature).GetEvents(Statics));
        Assert.Empty(typeof(CharacterEntity).GetEvents(Statics));
    }
}
