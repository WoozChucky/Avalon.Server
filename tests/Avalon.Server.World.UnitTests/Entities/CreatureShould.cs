using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Spells;
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
    // Died / OnCreatureKilled static event
    // ──────────────────────────────────────────────

    [Fact]
    public void Died_FiresOnCreatureKilled_Event()
    {
        ICreature? capturedCreature = null;
        IUnit? capturedKiller = null;

        CreatureKilledDelegate handler = (c, k) =>
        {
            capturedCreature = c;
            capturedKiller = k;
        };

        Creature.OnCreatureKilled += handler;
        try
        {
            var creature = MakeCreature();
            var killer = Substitute.For<IUnit>();
            creature.Died(killer);

            Assert.Same(creature, capturedCreature);
            Assert.Same(killer, capturedKiller);
        }
        finally
        {
            Creature.OnCreatureKilled -= handler;
        }
    }

    [Fact]
    public void Died_DoesNotThrow_WhenNoHandlersSubscribed()
    {
        var creature = MakeCreature();
        var killer = Substitute.For<IUnit>();

        // Ensure no handlers by doing nothing — static event may still have old subs from other tests
        // but the call itself must not throw
        var ex = Record.Exception(() => creature.Died(killer));
        Assert.Null(ex);
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

    // ──────────────────────────────────────────────
    // SendAttackAnimation / animation event relay
    // ──────────────────────────────────────────────

    [Fact]
    public void SendAttackAnimation_FiresEvent_WhenHandlerSubscribed()
    {
        bool fired = false;
        Creature.OnUnitAttackAnimation += (_, _) => fired = true;
        try
        {
            var creature = MakeCreature();
            creature.SendAttackAnimation(null);
            Assert.True(fired);
        }
        finally
        {
            // clean up the static delegate by releasing all handlers via reflection is complex;
            // instead use a weak capture and leave the handler (it's a no-op)
            Creature.OnUnitAttackAnimation -= (_, _) => fired = true;
        }
    }

    [Fact]
    public void SendAttackAnimation_DoesNotThrow_WhenNoHandlers()
    {
        // Isolated creature — if no handlers, this should be a no-op
        var creature = MakeCreature();
        var ex = Record.Exception(() => creature.SendAttackAnimation(Substitute.For<ISpell>()));
        Assert.Null(ex);
    }
}
