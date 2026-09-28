using System.Reflection;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Serialization;
using Xunit;

namespace Avalon.Server.World.UnitTests.Serialization;

/// <summary>
/// #640: the state broadcast's reused messages. A message handed out again holds only what a new one
/// would, however much it held before.
/// </summary>
public class ObjectStatePoolShould
{
    private static readonly PropertyInfo[] Members = typeof(ObjectState).GetProperties();

    /// <summary>Sets every member of <paramref name="state" /> to something no new message holds.</summary>
    internal static void FillEveryMember(ObjectState state)
    {
        foreach (PropertyInfo member in Members)
            member.SetValue(state, NotDefault(member.PropertyType));
    }

    private static object NotDefault(Type type)
    {
        Type value = Nullable.GetUnderlyingType(type) ?? type;
        if (value == typeof(string)) return "set";
        if (value == typeof(Vec3)) return new Vec3 { X = 1f, Y = 2f, Z = 3f };
        if (value == typeof(bool)) return true;
        if (value.IsEnum) return Enum.GetValues(value).GetValue(1)!;
        return Convert.ChangeType(1, value);
    }

    [Fact]
    public void Clear_every_member_a_message_has()
    {
        var state = new ObjectState();
        FillEveryMember(state);

        ObjectStatePool.Clear(state);

        var fresh = new ObjectState();
        foreach (PropertyInfo member in Members)
            Assert.True(Equals(member.GetValue(fresh), member.GetValue(state)), $"{member.Name} was not cleared");
    }

    [Fact]
    public void Hand_out_a_cleared_message_holding_its_guid_after_a_reset()
    {
        var pool = new ObjectStatePool();
        ObjectState first = pool.State(1);
        FillEveryMember(first);
        pool.Reset();

        ObjectState again = pool.State(42);

        Assert.Same(first, again);
        Assert.Equal(42UL, again.Guid);
        Assert.Null(again.Position);
        Assert.Null(again.Name);
    }

    [Fact]
    public void Hand_out_distinct_messages_and_vectors_until_a_reset()
    {
        var pool = new ObjectStatePool();

        ObjectState a = pool.State(1);
        ObjectState b = pool.State(2);
        Vec3 p = pool.Vector(new Vector3(1f, 2f, 3f));
        Vec3 q = pool.Vector(new Vector3(4f, 5f, 6f));

        Assert.NotSame(a, b);
        Assert.NotSame(p, q);
        Assert.Equal((1UL, 2UL), (a.Guid, b.Guid));
        Assert.Equal((1f, 2f, 3f), (p.X, p.Y, p.Z));
        Assert.Equal((4f, 5f, 6f), (q.X, q.Y, q.Z));
    }
}
