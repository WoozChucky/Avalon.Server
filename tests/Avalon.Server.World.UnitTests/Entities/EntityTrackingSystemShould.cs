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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Keep_reporting_dirty_fields_when_membership_is_unchanged(int copies)
    {
        EntityTrackingSystem sut = MakeSut();
        var obj = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 1u) };
        sut.Update([obj], EmptyDirty());
        var updates = new List<(ObjectGuid Guid, GameEntityFields Fields)>();
        sut.EntityUpdated += (guid, fields) => updates.Add((guid, fields));
        List<IWorldObject> visible = copies == 1 ? [obj] : [obj, obj];

        sut.Update(visible, DirtyWith(obj.Guid, GameEntityFields.Position));

        Assert.Equal(copies, updates.Count);
        Assert.All(updates, update => Assert.Equal((obj.Guid, GameEntityFields.Position), update));
        Assert.True(sut.IsTracked(obj.Guid));
    }

    [Fact]
    public void Remove_disappeared_entities_when_duplicate_inputs_keep_the_list_count_unchanged()
    {
        EntityTrackingSystem sut = MakeSut();
        var kept = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 1u) };
        var gone = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 2u) };
        sut.Update([kept, gone], EmptyDirty());
        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;

        sut.Update([kept, kept], EmptyDirty());

        Assert.Equal([gone.Guid], removed);
        Assert.True(sut.IsTracked(kept.Guid));
        Assert.False(sut.IsTracked(gone.Guid));
    }

    [Fact]
    public void Finish_pending_removals_after_a_callback_throws_even_when_membership_matches()
    {
        EntityTrackingSystem sut = MakeSut();
        var gone = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 1u) };
        var kept = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 2u) };
        sut.Update([gone, kept], EmptyDirty());
        bool fail = true;
        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += guid =>
        {
            if (fail)
                throw new InvalidOperationException();
            removed.Add(guid);
        };
        Assert.Throws<InvalidOperationException>(() => sut.Update([kept], EmptyDirty()));
        fail = false;

        sut.Update([kept], EmptyDirty());
        sut.Update([kept], EmptyDirty());

        Assert.Equal([gone.Guid], removed);
        Assert.True(sut.IsTracked(kept.Guid));
        Assert.False(sut.IsTracked(gone.Guid));
    }

    [Fact]
    public void Keep_membership_and_finish_removals_across_frame_wrap_after_an_interrupted_update()
    {
        EntityTrackingSystem sut = MakeSut();
        var kept = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 31u) };
        var gone = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 32u) };
        var incoming = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 33u) };
        sut.Update([kept, gone], EmptyDirty()); // both last seen in frame 1

        // Seed the clock only, avoiding billions of updates. All assertions observe normal notifications.
        typeof(EntityTrackingSystem).GetField("_frame", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.SetValue(sut, uint.MaxValue);
        bool fail = true;
        sut.EntityAdded += _ =>
        {
            if (fail)
                throw new InvalidOperationException();
        };
        Assert.Throws<InvalidOperationException>(() => sut.Update([incoming], EmptyDirty()));
        fail = false;
        var removed = new List<ObjectGuid>();
        var updated = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;
        sut.EntityUpdated += (guid, _) => updated.Add(guid);

        sut.Update([incoming, kept], DirtyWith(kept.Guid, GameEntityFields.CurrentHealth));
        Assert.Equal([gone.Guid], removed); // removal must happen in this update, not a later frame
        sut.Update([incoming, kept], EmptyDirty());

        Assert.Equal([gone.Guid], removed);
        Assert.Equal([kept.Guid], updated);
        Assert.True(sut.IsTracked(kept.Guid));
        Assert.True(sut.IsTracked(incoming.Guid));
        Assert.False(sut.IsTracked(gone.Guid));
    }
}
