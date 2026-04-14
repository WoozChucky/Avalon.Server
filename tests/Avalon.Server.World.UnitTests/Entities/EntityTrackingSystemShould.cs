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
    private static EntityTrackingSystem MakeSut() => new EntityTrackingSystem(capacity: 10);

    private static IWorldObject MakeObject(ObjectGuid? guid = null)
    {
        var obj = Substitute.For<IUnit>();
        obj.Guid.Returns(guid ?? new ObjectGuid(ObjectType.Creature, 1u));
        return obj;
    }

    private static Dictionary<ObjectGuid, GameEntityFields> EmptyDirty() => new();

    private static Dictionary<ObjectGuid, GameEntityFields> DirtyWith(ObjectGuid guid, GameEntityFields fields) =>
        new() { [guid] = fields };

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
        sut.Update([obj], EmptyDirty());

        Assert.NotNull(captured);
        Assert.Equal(obj.Guid, captured);
    }

    [Fact]
    public void NotFireEntityAdded_ForAlreadyTrackedObject()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        sut.Update([obj], EmptyDirty());

        int addCount = 0;
        sut.EntityAdded += _ => addCount++;
        sut.Update([obj], EmptyDirty());

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
        sut.Update([obj1, obj2, obj3], EmptyDirty());

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

        sut.Update([], EmptyDirty());

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
        sut.Update([obj], EmptyDirty());

        ObjectGuid? removed = null;
        sut.EntityRemoved += g => removed = g;
        sut.Update([], EmptyDirty());

        Assert.NotNull(removed);
        Assert.Equal(obj.Guid, removed);
    }

    [Fact]
    public void NotFireEntityRemoved_WhenObjectStillPresent()
    {
        var sut = MakeSut();
        var obj = MakeObject();
        sut.Update([obj], EmptyDirty());

        int removeCount = 0;
        sut.EntityRemoved += _ => removeCount++;
        sut.Update([obj], EmptyDirty());

        Assert.Equal(0, removeCount);
    }

    [Fact]
    public void FireEntityRemoved_ForEachDisappearedObject()
    {
        var sut = MakeSut();
        var obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([obj1, obj2], EmptyDirty());

        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;
        sut.Update([], EmptyDirty());

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
        sut.Update([staying, leaving], EmptyDirty());

        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;
        sut.Update([staying], EmptyDirty());

        Assert.Single(removed);
        Assert.Equal(leaving.Guid, removed[0]);
    }

    // ──────────────────────────────────────────────
    // EntityUpdated — dirty map driven
    // ──────────────────────────────────────────────

    [Fact]
    public void FireEntityUpdated_WithCorrectFields_WhenEntityIsInDirtyMap()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 3u));
        sut.Update([obj], EmptyDirty());

        GameEntityFields received = GameEntityFields.None;
        sut.EntityUpdated += (_, f) => received = f;
        sut.Update([obj], DirtyWith(obj.Guid, GameEntityFields.Position));

        Assert.Equal(GameEntityFields.Position, received);
    }

    [Fact]
    public void NotFireEntityUpdated_WhenEntityAbsentFromDirtyMap()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 4u));
        sut.Update([obj], EmptyDirty());

        int updateCount = 0;
        sut.EntityUpdated += (_, _) => updateCount++;
        sut.Update([obj], EmptyDirty()); // entity present but not dirty

        Assert.Equal(0, updateCount);
    }

    [Fact]
    public void FireEntityUpdated_ForEachTrackedObjectPresentInDirtyMap()
    {
        var sut = MakeSut();
        var obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([obj1, obj2], EmptyDirty());

        int updateCount = 0;
        sut.EntityUpdated += (_, _) => updateCount++;

        var dirty = new Dictionary<ObjectGuid, GameEntityFields>
        {
            [obj1.Guid] = GameEntityFields.CurrentHealth,
            [obj2.Guid] = GameEntityFields.Position,
        };
        sut.Update([obj1, obj2], dirty);

        Assert.Equal(2, updateCount);
    }

    [Fact]
    public void NotFireEntityUpdated_ForNewEntity_EvenIfInDirtyMap()
    {
        // New entities always trigger EntityAdded, never EntityUpdated
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 9u));

        int updateCount = 0;
        sut.EntityUpdated += (_, _) => updateCount++;
        sut.Update([obj], DirtyWith(obj.Guid, GameEntityFields.Position));

        Assert.Equal(0, updateCount);
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
        sut.Update([old], EmptyDirty());

        var added = new List<ObjectGuid>();
        var removed = new List<ObjectGuid>();
        sut.EntityAdded += added.Add;
        sut.EntityRemoved += removed.Add;
        sut.Update([incoming], EmptyDirty());

        Assert.Single(added);
        Assert.Equal(incoming.Guid, added[0]);
        Assert.Single(removed);
        Assert.Equal(old.Guid, removed[0]);
    }

    [Fact]
    public void HandleEmptyInitialUpdate_WithoutError()
    {
        var sut = MakeSut();
        var ex = Record.Exception(() => sut.Update([], EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void HandleRepeatedEmptyUpdates_WithoutError()
    {
        var sut = MakeSut();
        sut.Update([], EmptyDirty());
        var ex = Record.Exception(() => sut.Update([], EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void HandleLargeNumberOfObjects_WithoutError()
    {
        var sut = MakeSut();
        var objects = Enumerable.Range(1, 50)
            .Select(i => MakeObject(new ObjectGuid(ObjectType.Creature, (uint)i)))
            .ToArray();

        var ex = Record.Exception(() => sut.Update(objects, EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void FireEntityAdded_Again_WhenEntityReentersAfterRemoval()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 7u));
        sut.Update([obj], EmptyDirty()); // added
        sut.Update([], EmptyDirty());    // removed

        int addCount = 0;
        sut.EntityAdded += _ => addCount++;
        sut.Update([obj], EmptyDirty()); // re-enters

        Assert.Equal(1, addCount);
    }
}
