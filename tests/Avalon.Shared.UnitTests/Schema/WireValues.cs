using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalon.SchemaGen;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Compares a packet contract instance against the same message as the reference
/// implementation read it, member by member.
/// </summary>
/// <remarks>
/// Byte equality alone would not catch the failure this exists for. Two fields with their
/// names swapped in the schema encode and re-encode identically and differ only in what a
/// client thinks it has been sent - a password read into a username, silently. So the field
/// number, the field name and the value are each held to the C# member they came from.
/// </remarks>
internal static class WireValues
{
    internal static IReadOnlyList<string> Differences(Type contract, object instance, IMessage reference)
    {
        List<string> found = [];

        Compare(contract, instance, reference, WireSchema.SchemaNameOf(contract), found);

        return found;
    }

    private static void Compare(Type contract, object instance, IMessage reference, string path, List<string> found)
    {
        foreach (WireMember member in WireFixtures.Members(contract))
        {
            string memberPath = $"{path}.{member.Name}";
            FieldDescriptor? field = reference.Descriptor.FindFieldByNumber(member.Tag);

            if (field is null)
            {
                found.Add($"{memberPath} is field {member.Tag} and the schema declares no such field");
                continue;
            }

            if (!string.Equals(field.Name, member.Name, StringComparison.Ordinal))
            {
                found.Add($"{memberPath} is field {member.Tag}, which the schema calls '{field.Name}'");
            }

            object? value = member.Read(instance);

            ComparePresence(member, field, reference, value, memberPath, found);
            CompareValue(member.DeclaredType, value, field.Accessor.GetValue(reference), memberPath, found);
        }
    }

    /// <summary>
    /// A nullable member is the case the schema's explicit presence exists for, so whether the
    /// reference reader agrees the field is set is checked on its own rather than folded into
    /// the value comparison, where null and zero would compare equal and prove nothing.
    /// </summary>
    private static void ComparePresence(
        WireMember member,
        FieldDescriptor field,
        IMessage reference,
        object? value,
        string path,
        List<string> found)
    {
        if (Nullable.GetUnderlyingType(member.DeclaredType) is null)
        {
            return;
        }

        if (!field.HasPresence)
        {
            found.Add($"{path} is nullable in C# but the schema gives field {member.Tag} no presence");
            return;
        }

        bool set = field.Accessor.HasValue(reference);

        if (set != (value is not null))
        {
            found.Add($"{path} is {(value is null ? "null" : "set")} but the reference reader says it is "
                + (set ? "set" : "absent"));
        }
    }

    private static void CompareValue(Type declared, object? value, object? reference, string path, List<string> found)
    {
        if (Nullable.GetUnderlyingType(declared) is { } underlying)
        {
            if (value is not null)
            {
                CompareValue(underlying, value, reference, path, found);
            }

            return;
        }

        if (WireFixtures.RepeatedElementType(declared) is { } element)
        {
            CompareElements(element, value, reference, path, found);
            return;
        }

        if (WireFixtures.IsContract(declared))
        {
            CompareMessage(declared, value, reference, path, found);
            return;
        }

        CompareScalar(declared, value, reference, path, found);
    }

    private static void CompareElements(Type element, object? value, object? reference, string path, List<string> found)
    {
        object?[] ours = value is IEnumerable items ? items.Cast<object?>().ToArray() : [];
        object?[] theirs = reference is IEnumerable read ? read.Cast<object?>().ToArray() : [];

        if (ours.Length != theirs.Length)
        {
            found.Add($"{path} has {Count(ours.Length)} and the reference reader found {Count(theirs.Length)}");
            return;
        }

        for (int i = 0; i < ours.Length; i++)
        {
            CompareValue(element, ours[i], theirs[i], $"{path}[{i.ToString(CultureInfo.InvariantCulture)}]", found);
        }
    }

    private static string Count(int length) =>
        length == 1 ? "1 element" : $"{length.ToString(CultureInfo.InvariantCulture)} elements";

    private static void CompareMessage(Type declared, object? value, object? reference, string path, List<string> found)
    {
        switch (value, reference)
        {
            case (null, null):
                return;

            case (null, not null):
                found.Add($"{path} is null but the reference reader found a message");
                return;

            case (not null, null):
                found.Add($"{path} is set but the reference reader found nothing");
                return;

            default:
                Compare(declared, value!, (IMessage)reference!, path, found);
                return;
        }
    }

    private static void CompareScalar(Type declared, object? value, object? reference, string path, List<string> found)
    {
        (object? ours, object? theirs) = Normalize(declared, value, reference);

        bool same = (ours, theirs) switch
        {
            (float a, float b) => SameFloat(a, b),
            (byte[] a, byte[] b) => a.SequenceEqual(b),

            // Ticks only. protobuf-net never writes bcl.DateTime's kind field, so a UTC value
            // is indistinguishable on the wire from an unspecified one and no reader can
            // recover which it was. WireLimitsShould pins that down with the bytes.
            (DateTime a, DateTime b) => a.Ticks == b.Ticks,

            _ => Equals(ours, theirs),
        };

        if (!same)
        {
            found.Add($"{path} is {Show(ours)} and the reference reader found {Show(theirs)}");
        }
    }

    /// <summary>
    /// Brings the two object models onto common ground: C# null strings and arrays become the
    /// empty ones the reference reader produces, its ByteString becomes bytes, and its two
    /// protobuf-net submessages become the .NET values they encode.
    /// </summary>
    private static (object? Ours, object? Theirs) Normalize(Type declared, object? value, object? reference)
    {
        if (declared == typeof(string))
        {
            return (value ?? string.Empty, reference ?? string.Empty);
        }

        if (declared == typeof(byte[]) || declared == typeof(ReadOnlyMemory<byte>))
        {
            byte[] ours = value switch
            {
                byte[] bytes => bytes,
                ReadOnlyMemory<byte> memory => memory.ToArray(),
                _ => [],
            };

            return (ours, reference is ByteString read ? read.ToByteArray() : Array.Empty<byte>());
        }

        if (declared == typeof(DateTime))
        {
            return (value ?? default(DateTime), reference is IMessage moment ? BclValues.ToDateTime(moment) : default(DateTime));
        }

        if (declared == typeof(Guid))
        {
            return (value ?? Guid.Empty, reference is IMessage identifier ? BclValues.ToGuid(identifier) : Guid.Empty);
        }

        if (declared.IsEnum)
        {
            return (Convert.ToInt64(value, CultureInfo.InvariantCulture),
                reference is null ? 0L : Convert.ToInt64(reference, CultureInfo.InvariantCulture));
        }

        if (declared == typeof(float) || declared == typeof(bool))
        {
            return (value, reference);
        }

        return (Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            reference is null ? 0m : Convert.ToDecimal(reference, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Bit equality, so that NaN matches NaN, except that the two zeros are treated as equal:
    /// neither encoder writes a field holding negative zero, because IEEE says it is zero, so
    /// negative zero is not transmissible in either direction and both sides read back +0.
    /// </summary>
    private static bool SameFloat(float ours, float theirs) =>
        BitConverter.SingleToInt32Bits(ours) == BitConverter.SingleToInt32Bits(theirs)
        || (ours == 0f && theirs == 0f);

    private static string Show(object? value) => value switch
    {
        null => "null",
        float number => $"{number.ToString("G9", CultureInfo.InvariantCulture)} (0x{BitConverter.SingleToInt32Bits(number).ToString("x8", CultureInfo.InvariantCulture)})",
        byte[] bytes => WireBytes.Hex(bytes),
        string text => $"\"{text}\"",
        DateTime moment => $"{moment.ToString("O", CultureInfo.InvariantCulture)} ({moment.Kind})",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "?",
    };
}
