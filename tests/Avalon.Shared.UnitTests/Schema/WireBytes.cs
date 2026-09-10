using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>One top-level field as it sits in an encoded message.</summary>
internal readonly record struct WireField(int Number, WireFormat.WireType Type, int Start, int Length, int PayloadLength);

/// <summary>
/// Reads an encoded message at the tag level, using the reference implementation's own
/// decoder rather than a hand-rolled one.
/// </summary>
internal static class WireBytes
{
    internal static IReadOnlyList<WireField> Fields(byte[] bytes)
    {
        List<WireField> fields = [];
        var input = new CodedInputStream(bytes);

        while (!input.IsAtEnd)
        {
            int start = (int)input.Position;
            uint tag = input.ReadTag();

            if (tag == 0)
            {
                break;
            }

            WireFormat.WireType type = WireFormat.GetTagWireType(tag);
            int payload = -1;

            if (type == WireFormat.WireType.LengthDelimited)
            {
                payload = input.ReadBytes().Length;
            }
            else
            {
                input.SkipLastField();
            }

            fields.Add(new WireField(
                WireFormat.GetTagFieldNumber(tag),
                type,
                start,
                (int)input.Position - start,
                payload));
        }

        return fields;
    }

    /// <summary>
    /// The same message without any length-delimited field that carries no bytes, whatever its
    /// declared type.
    /// </summary>
    /// <remarks>
    /// This is the wider transformation, and it is needed only in the direction that goes back
    /// through the server. Eleven string members and four message members in the protocol carry
    /// a C# initializer, so protobuf-net deserializes an absent field into an empty value and
    /// writes it out again - which makes its own encode-decode-encode cycle non-idempotent,
    /// independently of any schema. Holding the return trip to equality outside these fields is
    /// the strongest statement that stays true.
    /// </remarks>
    internal static byte[] WithoutEmptyLengthDelimited(byte[] bytes)
    {
        var kept = new MemoryStream(bytes.Length);

        foreach (WireField field in Fields(bytes))
        {
            if (field.Type == WireFormat.WireType.LengthDelimited && field.PayloadLength == 0)
            {
                continue;
            }

            kept.Write(bytes, field.Start, field.Length);
        }

        return kept.ToArray();
    }

    internal static string Hex(byte[] bytes) =>
        bytes.Length == 0
            ? "(empty)"
            : string.Join(" ", bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Says where two encodings part company, at the byte and then at the field, because
    /// "expected 41 bytes, got 39" on its own sends the reader back to a hex dump.
    /// </summary>
    internal static string Explain(byte[] expected, byte[] actual, MessageDescriptor descriptor)
    {
        int shared = Math.Min(expected.Length, actual.Length);
        int first = 0;

        while (first < shared && expected[first] == actual[first])
        {
            first++;
        }

        var report = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"First difference at byte {first} of {expected.Length}/{actual.Length}.")
            .Append(Environment.NewLine)
            .Append(CultureInfo.InvariantCulture, $"  expected  {Hex(expected)}")
            .Append(Environment.NewLine)
            .Append(CultureInfo.InvariantCulture, $"  actual    {Hex(actual)}")
            .Append(Environment.NewLine);

        report.Append("  fields expected: ").Append(Describe(expected, descriptor)).Append(Environment.NewLine);
        report.Append("  fields actual:   ").Append(Describe(actual, descriptor));

        return report.ToString();
    }

    private static string Describe(byte[] bytes, MessageDescriptor descriptor)
    {
        IEnumerable<string> described = Fields(bytes).Select(field =>
        {
            string name = descriptor.FindFieldByNumber(field.Number)?.Name ?? "<not in schema>";
            string size = field.PayloadLength < 0
                ? string.Empty
                : $", {field.PayloadLength.ToString(CultureInfo.InvariantCulture)} bytes";

            return $"{field.Number.ToString(CultureInfo.InvariantCulture)} {name} ({field.Type}{size})";
        });

        return string.Join("; ", described);
    }
}
