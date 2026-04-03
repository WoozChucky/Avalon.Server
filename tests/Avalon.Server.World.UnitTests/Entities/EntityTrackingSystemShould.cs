using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class EntityTrackingSystemShould
{
    // changedFieldsHandler variants for tests
    private static GameEntityFields AlwaysPosition(IWorldObject _, IWorldObject __) => GameEntityFields.Position;
    private static GameEntityFields AlwaysNoFields(IWorldObject _, IWorldObject __) => (GameEntityFields)0;

    private static EntityTrackingSystem MakeSut(
        Func<IWorldObject, IWorldObject, GameEntityFields>? handler = null) =>
        new EntityTrackingSystem(
            capacity: 10,
            createObjectHandler: o => o,
            updateObjectHandler: (_, incoming) => incoming,
            changedFieldsHandler: handler ?? AlwaysNoFields);

    private static IWorldObject MakeObject(ObjectGuid? guid = null)
    {
        var obj = Substitute.For<IUnit>();
        obj.Guid.Returns(guid ?? new ObjectGuid(ObjectType.Creature, 1u));
        return obj;
    }

    // ──────────────────────────────────────────────
    // EntityAdded
    // ──────────────────────────────────────────────

    [Fact]
    public void FireEntityAdded_WhenNewObjectAppears()
    {
        var sut = MakeSut();
        ObjectGuid? captured = null;
        sut.EntityAdded += g => captured = g;

        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        sut.Update([obj]);

        Assert.NotNull(captured);
        Assert.Equal(obj.Guid, captured);
    }

    [Fact]
    public void NotFireEntityAdded_ForAlreadyTrackedObject()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        sut.Update([obj]); // first pass adds it

        int addCount = 0;
        sut.EntityAdded += _ => addCount++;
        sut.Update([obj]); // second pass — already tracked

        Assert.Equal(0, addCount);
    }

    [Fact]
    public void FireEntityAdded_ForEachOfMultipleNewObjects()
    {
        var sut = MakeSut();
        var added = new List<ObjectGuid>();
        sut.EntityAdded += added.Add;

        var obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        var obj3 = MakeObject(new ObjectGuid(ObjectType.Character, 1u));
        sut.Update([obj1, obj2, obj3]);

        Assert.Equal(3, added.Count);
        Assert.Contains(obj1.Guid, added);
        Assert.Contains(obj2.Guid, added);
        Assert.Contains(obj3.Guid, added);
    }

    [Fact]
    public void NotFireEntityAdded_ForEmptyUpdate()
    {
        var sut = MakeSut();
        int addCount = 0;
        sut.EntityAdded += _ => addCount++;

        sut.Update([]);

        Assert.Equal(0, addCount);
    }

    // ──────────────────────────────────────────────
    // EntityRemoved
    // ──────────────────────────────────────────────

    [Fact]
    public void FireEntityRemoved_WhenObjectDisappears()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 5u));
        sut.Update([obj]);

        ObjectGuid? removed = null;
        sut.EntityRemoved += g => removed = g;
        sut.Update([]); // obj not present in new list

        Assert.NotNull(removed);
        Assert.Equal(obj.Guid, removed);
    }

    [Fact]
    public void NotFireEntityRemoved_WhenObjectStillPresent()
    {
        var sut = MakeSut();
        var obj = MakeObject();
        sut.Update([obj]);

        int removeCount = 0;
        sut.EntityRemoved += _ => removeCount++;
        sut.Update([obj]);

        Assert.Equal(0, removeCount);
    }

    [Fact]
    public void FireEntityRemoved_ForEachDisappearedObject()
    {
        var sut = MakeSut();
        var obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([obj1, obj2]);

        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;
        sut.Update([]); // both gone

        Assert.Equal(2, removed.Count);
        Assert.Contains(obj1.Guid, removed);
        Assert.Contains(obj2.Guid, removed);
    }

    [Fact]
    public void OnlyRemoveDisappearedObjects_LeavingRemainingIntact()
    {
        var sut = MakeSut();
        var staying = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var leaving = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([staying, leaving]);

        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;
        sut.Update([staying]); // only leaving disappears

        Assert.Single(removed);
        Assert.Equal(leaving.Guid, removed[0]);
    }

    // ──────────────────────────────────────────────
    // EntityUpdated
    // ──────────────────────────────────────────────

    [Fact]
    public void FireEntityUpdated_WhenObjectAlreadyTracked()
    {
        var sut = MakeSut(AlwaysPosition);
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 3u));
        sut.Update([obj]); // primes tracking

        GameEntityFields? received = null;
        sut.EntityUpdated += (_, f) => received = f;
        sut.Update([obj]); // second pass triggers update

        Assert.NotNull(received);
    }

    [Fact]
    public void FireEntityUpdated_WithFieldsReturnedByHandler()
    {
        var sut = MakeSut(AlwaysPosition);
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 4u));
        sut.Update([obj]);

        GameEntityFields received = (GameEntityFields)0;
        sut.EntityUpdated += (_, f) => received = f;
        sut.Update([obj]);

        Assert.Equal(GameEntityFields.Position, received);
    }

    [Fact]
    public void FireEntityUpdated_WithZeroFields_WhenHandlerReturnsZero()
    {
        var sut = MakeSut(AlwaysNoFields);
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 6u));
        sut.Update([obj]);

        GameEntityFields received = GameEntityFields.None; // start with non-zero sentinel
        sut.EntityUpdated += (_, f) => received = f;
        sut.Update([obj]);

        Assert.Equal((GameEntityFields)0, received);
    }

    [Fact]
    public void FireEntityUpdated_ForEachTrackedObjectInUpdate()
    {
        var sut = MakeSut(AlwaysPosition);
        var obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([obj1, obj2]);

        int updateCount = 0;
        sut.EntityUpdated += (_, _) => updateCount++;
        sut.Update([obj1, obj2]);

        Assert.Equal(2, updateCount);
    }

    // ──────────────────────────────────────────────
    // Compound / edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public void HandleAddAndRemoveInSameUpdate()
    {
        var sut = MakeSut();
        var old = MakeObject(new ObjectGuid(ObjectType.Creature, 10u));
        var incoming = MakeObject(new ObjectGuid(ObjectType.Creature, 20u));
        sut.Update([old]);

        var added = new List<ObjectGuid>();
        var removed = new List<ObjectGuid>();
        sut.EntityAdded += added.Add;
        sut.EntityRemoved += removed.Add;
        sut.Update([incoming]); // old removed, new added

        Assert.Single(added);
        Assert.Equal(incoming.Guid, added[0]);
        Assert.Single(removed);
        Assert.Equal(old.Guid, removed[0]);
    }

    [Fact]
    public void HandleEmptyInitialUpdate_WithoutError()
    {
        var sut = MakeSut();
        var ex = Record.Exception(() => sut.Update([]));
        Assert.Null(ex);
    }

    [Fact]
    public void HandleRepeatedEmptyUpdates_WithoutError()
    {
        var sut = MakeSut();
        sut.Update([]);
        var ex = Record.Exception(() => sut.Update([]));
        Assert.Null(ex);
    }

    [Fact]
    public void HandleLargeNumberOfObjects_WithoutError()
    {
        var sut = MakeSut();
        var objects = Enumerable.Range(1, 50)
            .Select(i => MakeObject(new ObjectGuid(ObjectType.Creature, (uint)i)))
            .ToArray();

        var ex = Record.Exception(() => sut.Update(objects));
        Assert.Null(ex);
    }
}
