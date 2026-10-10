using System.Reflection;
using System.Runtime.CompilerServices;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Serialization;

/// <summary>
/// #875: every server packet is built in one message per thread, reused for the next packet of its type. A member the
/// reset missed would carry one player's value into the packet sent to the next, so every member of every server
/// packet, collections, nested messages and nullable values included, must come back as a new message has it.
/// </summary>
public class PacketEncoderScratchShould
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly MethodInfo s_scratch = typeof(PacketEncoder).GetMethod(nameof(PacketEncoder.Scratch))!;

    /// <summary>Every packet a server factory encodes: a <see cref="Packet" /> with a static method returning one.</summary>
    public static TheoryData<Type> ServerPackets()
    {
        var data = new TheoryData<Type>();
        foreach (Type type in typeof(Packet).Assembly.GetTypes()
                     .Where(t => t.IsSubclassOf(typeof(Packet)) && !t.IsAbstract
                                 && t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                     .Any(m => m.ReturnType == typeof(OutboundPacket)))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    /// <summary>The world server prepares the resets at startup; one it missed would be compiled on the tick.</summary>
    [Fact]
    public void Prepare_every_server_packet_at_startup() =>
        Assert.Equal(ServerPackets().Count, PacketEncoder.PrepareServerPackets());

    [Theory]
    [MemberData(nameof(ServerPackets))]
    public void Reset_every_member_a_packet_set(Type type)
    {
        object scratch = Take(type);
        object fresh = Activator.CreateInstance(type)!;
        List<FieldInfo> fields = Fields(type);
        var dirty = new Dictionary<FieldInfo, object?>();
        foreach (FieldInfo field in fields)
        {
            object? value = Dirty(field.FieldType, field.GetValue(fresh));
            field.SetValue(scratch, value);
            dirty[field] = value;
        }

        object retaken = Take(type);

        Assert.Same(scratch, retaken);
        foreach (FieldInfo field in fields)
        {
            object? expected = field.GetValue(fresh);
            object? actual = field.GetValue(retaken);
            if (field.FieldType.IsValueType || field.FieldType == typeof(string))
            {
                Assert.True(Equals(expected, actual), $"{type.Name}.{field.Name} kept {actual}, a new message has {expected}.");
            }
            else if (expected is null || IsCollection(field.FieldType))
            {
                // A collection comes back null, never the template's own list, so no factory can add to that.
                Assert.True(actual is null, $"{type.Name}.{field.Name} kept a value, it should come back null.");
            }
            else
            {
                Assert.False(ReferenceEquals(dirty[field], actual), $"{type.Name}.{field.Name} kept the last packet's value.");
                Assert.Equal(expected.GetType(), actual?.GetType());
            }
        }

        // On the wire a reset message is a new one: protobuf-net writes a null repeated field as an empty one.
        Assert.Equal(Serialize(fresh), Serialize(retaken));
    }

    /// <summary>
    /// The reset copies a template every thread shares. A factory that wrote into a member it found on the scratch (a
    /// nested message the template created, say) would change that template, and every later packet of the type, on
    /// every thread, would carry the value. So every factory runs once, then a new thread's scratch must still encode
    /// as a new message does.
    /// </summary>
    [Theory]
    [MemberData(nameof(ServerPackets))]
    public void Leave_the_template_as_a_new_message_after_every_factory(Type type)
    {
        var encoder = new PacketEncoder(new PayloadSegmentPool());
        foreach (MethodInfo factory in type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.ReturnType == typeof(OutboundPacket)))
        {
            object?[] arguments = factory.GetParameters()
                .Select(p => p.ParameterType == typeof(PacketEncoder) ? encoder : Argument(p.ParameterType))
                .ToArray();
            ((OutboundPacket)factory.Invoke(null, arguments)!).Release();
        }

        byte[]? first = null;
        byte[]? second = null;
        var thread = new Thread(() =>
        {
            first = Serialize(Take(type));
            second = Serialize(Take(type));
        });
        thread.Start();
        thread.Join();

        byte[] expected = Serialize(Activator.CreateInstance(type)!);
        Assert.Equal(expected, first);
        Assert.Equal(expected, second);
    }

    private static bool IsCollection(Type type) =>
        type != typeof(string) && type != typeof(byte[]) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    /// <summary>A factory argument: a value other than the default, and a collection of one such element.</summary>
    private static object? Argument(Type type)
    {
        Type? underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return Argument(underlying);

        if (type == typeof(string) || type == typeof(byte[]) || type.IsValueType)
            return Dirty(type, current: null);

        if (type.IsArray)
        {
            var array = Array.CreateInstance(type.GetElementType()!, 1);
            array.SetValue(Argument(type.GetElementType()!), 0);
            return array;
        }

        if (type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            Type element = type.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            list.Add(Argument(element));
            return list;
        }

        return Dirty(type, current: null);
    }

    private static object Take(Type type) => s_scratch.MakeGenericMethod(type).Invoke(null, null)!;

    private static byte[] Serialize(object message)
    {
        using var stream = new MemoryStream();
        Serializer.NonGeneric.Serialize(stream, message);
        return stream.ToArray();
    }

    /// <summary>Every instance field down the hierarchy, backing fields and read-only ones included.</summary>
    private static List<FieldInfo> Fields(Type type)
    {
        var fields = new List<FieldInfo>();
        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
            fields.AddRange(current.GetFields(InstanceMembers));
        return fields;
    }

    /// <summary>A value of <paramref name="type" /> other than <paramref name="current" />, and never the same object.</summary>
    private static object? Dirty(Type type, object? current)
    {
        Type? underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return Dirty(underlying, current);

        if (type == typeof(string))
            return current as string == "dirty" ? "other" : "dirty";

        if (type == typeof(bool))
            return !(current is true);

        if (type.IsEnum)
        {
            object value = Enum.ToObject(type, 77);
            return value.Equals(current) ? Enum.ToObject(type, 78) : value;
        }

        if (type.IsPrimitive)
        {
            object value = Convert.ChangeType(77, type);
            return value.Equals(current) ? Convert.ChangeType(78, type) : value;
        }

        if (type == typeof(Guid))
            return Guid.NewGuid();

        if (type == typeof(DateTime))
            return new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        if (type == typeof(decimal))
            return 77m;

        if (type.IsValueType)
        {
            object boxed = RuntimeHelpers.GetUninitializedObject(type);
            FieldInfo first = Fields(type).First();
            first.SetValue(boxed, Dirty(first.FieldType, first.GetValue(boxed)));
            return boxed;
        }

        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType()!, 1);

        if (type.IsInterface && type.IsGenericType)
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));

        return type.GetConstructor(Type.EmptyTypes) is not null
            ? Activator.CreateInstance(type)
            : RuntimeHelpers.GetUninitializedObject(type);
    }
}
