using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Entities;

public class EntityTrackingSystemShould
{
    private static EntityTrackingSystem MakeSut() => new EntityTrackingSystem(capacity: 10);

    private static IWorldObject MakeObject(ObjectGuid? guid = null)
    {
        IUnit obj = Substitute.For<IUnit>();
        obj.Guid.Returns(guid ?? new ObjectGuid(ObjectType.Creature, 1u));
        return obj;
    }

    private static Dictionary<ObjectGuid, GameEntityFields> EmptyDirty() => new();

    private static Dictionary<ObjectGuid, GameEntityFields> DirtyWith(ObjectGuid guid, GameEntityFields fields) =>
        new() { [guid] = fields };

    // ──────────────────────────────────────────────
    // EntityRemoved
    // ──────────────────────────────────────────────

    [Fact]
    public void FireEntityRemoved_ForEachDisappearedObject()
    {
        EntityTrackingSystem sut = MakeSut();
        IWorldObject obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        IWorldObject obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([obj1, obj2], EmptyDirty());

        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;
        sut.Update([], EmptyDirty());

        Assert.Equal(2, removed.Count);
        Assert.Contains(obj1.Guid, removed);
        Assert.Contains(obj2.Guid, removed);
    }

    // ──────────────────────────────────────────────
    // EntityUpdated — dirty map driven
    // ──────────────────────────────────────────────

    [Fact]
    public void NotFireEntityUpdated_ForNewEntity_EvenIfInDirtyMap()
    {
        // New entities always trigger EntityAdded, never EntityUpdated
        EntityTrackingSystem sut = MakeSut();
        IWorldObject obj = MakeObject(new ObjectGuid(ObjectType.Creature, 9u));

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
        EntityTrackingSystem sut = MakeSut();
        IWorldObject old = MakeObject(new ObjectGuid(ObjectType.Creature, 10u));
        IWorldObject incoming = MakeObject(new ObjectGuid(ObjectType.Creature, 20u));
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

    /// <summary>#593: the interest rule asks whether an object is already in view.</summary>
    [Fact]
    public void Report_what_it_tracks_as_of_its_last_update()
    {
        EntityTrackingSystem sut = MakeSut();
        IWorldObject obj = MakeObject(new ObjectGuid(ObjectType.Creature, 8u));
        Assert.False(sut.IsTracked(obj.Guid));

        sut.Update([obj], EmptyDirty());
        Assert.True(sut.IsTracked(obj.Guid));

        sut.Update([], EmptyDirty());
        Assert.False(sut.IsTracked(obj.Guid));
    }
}
