using System;
using System.Globalization;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Reads <c>bcl.DateTime</c> and <c>bcl.Guid</c> back into the .NET values they stand for.
/// </summary>
/// <remarks>
/// Five fields in the protocol travel through protobuf-net's own representations rather than
/// the well-known types, and neither one is what a reader would guess: a bcl.DateTime is a
/// scaled offset from the Unix epoch, not a Timestamp, and a bcl.Guid is two fixed64s in the
/// order .NET's own Guid.ToByteArray uses, not sixteen bytes in RFC order. A consumer that
/// assumes otherwise reads a plausible wrong value and reports nothing.
///
/// This is the conversion a non-.NET client has to write, so writing it here and holding it
/// against the values the server actually serialized is what turns the schema note into
/// something checked. It reads the submessage through the descriptor rather than through
/// generated accessors, so it says out loud which field numbers it depends on.
/// </remarks>
internal static class BclValues
{
    private const int MinMaxScale = 15;

    internal static DateTime ToDateTime(IMessage message)
    {
        long value = Read<long>(message, 1);
        long scale = Convert.ToInt64(Read<object>(message, 2), CultureInfo.InvariantCulture);
        long kind = Convert.ToInt64(Read<object>(message, 3), CultureInfo.InvariantCulture);

        if (scale == MinMaxScale)
        {
            return value < 0 ? DateTime.MinValue : DateTime.MaxValue;
        }

        return new DateTime(DateTime.UnixEpoch.Ticks + (value * TicksPer(scale)), KindOf(kind));
    }

    internal static Guid ToGuid(IMessage message)
    {
        byte[] bytes = BitConverter.GetBytes(Read<ulong>(message, 1))
            .Concat(BitConverter.GetBytes(Read<ulong>(message, 2)))
            .ToArray();

        return new Guid(bytes);
    }

    private static long TicksPer(long scale) => scale switch
    {
        0 => TimeSpan.TicksPerDay,
        1 => TimeSpan.TicksPerHour,
        2 => TimeSpan.TicksPerMinute,
        3 => TimeSpan.TicksPerSecond,
        4 => TimeSpan.TicksPerMillisecond,
        5 => 1,
        _ => throw new NotSupportedException(
            $"bcl.DateTime scale {scale.ToString(CultureInfo.InvariantCulture)} has no meaning in protobuf-net's schema."),
    };

    private static DateTimeKind KindOf(long kind) => kind switch
    {
        1 => DateTimeKind.Utc,
        2 => DateTimeKind.Local,
        _ => DateTimeKind.Unspecified,
    };

    private static T Read<T>(IMessage message, int number)
    {
        FieldDescriptor field = message.Descriptor.FindFieldByNumber(number)
            ?? throw new InvalidOperationException(
                $"{message.Descriptor.FullName} has no field {number.ToString(CultureInfo.InvariantCulture)}. " +
                "The vendored protobuf-net/bcl.proto no longer describes what the library writes.");

        return (T)field.Accessor.GetValue(message);
    }
}
