using System.Collections;
using System.Reflection;
using ProtoBuf;

namespace Avalon.Network.Packets.Serialization;

/// <summary>
/// A message with something in every member protobuf-net writes (#883): a value that is not the member's default, one
/// element in each list and array, and a message, filled the same way, in each member that holds one. Writing it once
/// makes protobuf-net build, and the runtime compile, everything a real message of the type will need: its own
/// serializer, those of the messages inside it, and the code that writes its lists. A new message would not: a member
/// left at its default is skipped, and what would have written it is built by the first packet that sets it.
/// </summary>
/// <remarks>
/// Only <see cref="PacketEncoder.PrepareServerPacket" /> uses it, at startup. It fills the shapes the packet contracts
/// have, and refuses any other by name, so a contract with a member it cannot fill fails
/// <c>PacketEncoderScratchShould</c> rather than leaving part of a packet to be built by its first send.
/// </remarks>
internal static class SampleMessage
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static object Of(Type contract) => Fill(contract, []);

    /// <param name="open">The messages this one is inside of, so that a contract reached through itself ends there.</param>
    private static object Fill(Type contract, List<Type> open)
    {
        object message = Activator.CreateInstance(contract)
            ?? throw new NotSupportedException($"{contract.FullName} has no parameterless constructor.");
        open.Add(contract);

        foreach (FieldInfo field in contract.GetFields(InstanceMembers))
        {
            if (field.GetCustomAttribute<ProtoMemberAttribute>() is not null)
                throw Unfillable(contract, field.Name, "is a field, and only properties are filled");
        }

        foreach (PropertyInfo property in contract.GetProperties(InstanceMembers))
        {
            if (property.GetCustomAttribute<ProtoMemberAttribute>() is null)
                continue;

            if (property.GetSetMethod(nonPublic: true) is null)
                throw Unfillable(contract, property.Name, "has no setter");

            // Null for a message already open above this one: left as a new message has it.
            if (Value(contract, property.Name, property.PropertyType, open) is { } value)
                property.SetValue(message, value);
        }

        open.RemoveAt(open.Count - 1);
        return message;
    }

    private static object? Value(Type contract, string member, Type type, List<Type> open)
    {
        // Set through reflection, a boxed T becomes the T? the member declares.
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Value(contract, member, underlying, open);

        if (type.IsEnum)
        {
            // A value the enum declares, and its last so that it is not zero where the enum has another.
            Array declared = Enum.GetValues(type);
            return declared.Length == 0 ? null : declared.GetValue(declared.Length - 1);
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Boolean:
                return true;
            case TypeCode.String:
                return "a";
            case TypeCode.DateTime:
                return new DateTime(2000, 1, 1, 0, 0, 1, DateTimeKind.Utc);
            case TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
                or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double:
                return Convert.ChangeType(1, type, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type == typeof(byte[]))
            return new byte[] { 1 };

        if (type == typeof(ReadOnlyMemory<byte>))
            return new ReadOnlyMemory<byte>([1]);

        if (type == typeof(Guid))
            return new Guid(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        if (type.IsArray && type.GetElementType() is { } element)
        {
            object? item = Value(contract, member, element, open);
            var array = Array.CreateInstance(element, item is null ? 0 : 1);
            if (item is not null)
                array.SetValue(item, 0);
            return array;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            object? item = Value(contract, member, type.GetGenericArguments()[0], open);
            var list = (IList)Activator.CreateInstance(type)!;
            if (item is not null)
                list.Add(item);
            return list;
        }

        if (type.GetCustomAttribute<ProtoContractAttribute>() is not null)
            return open.Contains(type) ? null : Fill(type, open);

        throw Unfillable(contract, member, $"is a {type}, which is not a shape this fills");
    }

    private static NotSupportedException Unfillable(Type contract, string member, string why) =>
        new($"{contract.FullName}.{member} {why}, so the serializer of a packet that carries it would be left for its "
            + "first send to build. Teach SampleMessage the shape (#883).");
}
