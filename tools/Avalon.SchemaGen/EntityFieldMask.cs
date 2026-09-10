using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Serialization;
using Avalon.World.Public.Enums;

namespace Avalon.SchemaGen;

/// <summary>
/// Exports <see cref="GameEntityFields"/>, the bitmask that heads the entity-state field blob.
/// </summary>
/// <remarks>
/// The blob travels as an opaque bytes member, so the schema describes nothing inside it, and
/// the enum could not be carried there in any case: proto3 requires the first enum member to be
/// zero and this one has no zero member at all. Left unexported, a client has to hand-copy
/// sixteen bit values and would have no way to notice a renumber, which is the hand-copying
/// this schema exists to remove.
/// </remarks>
public static class EntityFieldMask
{
    public static Document Describe()
    {
        List<(string Name, int Value)> members = Members();

        Bit[] bits = members
            .Where(member => BitOperations.PopCount((uint)member.Value) == 1)
            .Select(member => new Bit(
                member.Name,
                member.Value,
                Hex(member.Value),
                BitOperations.TrailingZeroCount((uint)member.Value)))
            .ToArray();

        Mask[] masks = members
            .Where(member => BitOperations.PopCount((uint)member.Value) != 1)
            .Select(member => new Mask(
                member.Name,
                member.Value,
                Hex(member.Value),
                bits.Where(bit => (member.Value & bit.Value) == bit.Value)
                    .Select(bit => bit.Name)
                    .ToArray()))
            .ToArray();

        return new Document(
            Comment: "The GameEntityFields bitmask that heads the entity-state field blob. The blob is an "
                + "opaque bytes member, so the schema describes nothing inside it, and proto3 requires a "
                + "zero-valued first enum member, which an enum reporting hasZeroValue false cannot supply. "
                + "Copy these values; do not renumber them.",
            Name: nameof(GameEntityFields),
            Source: "Avalon.World.Public.Enums." + nameof(GameEntityFields),
            UnderlyingType: Enum.GetUnderlyingType(typeof(GameEntityFields)).Name,
            HasZeroValue: members.Exists(member => member.Value == 0),
            Bits: bits,
            Masks: masks);
    }

    /// <summary>
    /// The declared members, ordered by value so that regenerating an unchanged enum produces an
    /// unchanged file. Reflection does not promise declaration order, so it is not relied on.
    /// </summary>
    private static List<(string Name, int Value)> Members()
    {
        List<(string Name, int Value)> members = typeof(GameEntityFields)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (field.Name, Value: (int)field.GetRawConstantValue()!))
            .ToList();

        members.Sort((left, right) => left.Value != right.Value
            ? left.Value.CompareTo(right.Value)
            : string.CompareOrdinal(left.Name, right.Name));

        return members;
    }

    private static string Hex(int value) => "0x" + value.ToString("X4", CultureInfo.InvariantCulture);

    public sealed record Document(
        [property: JsonPropertyName("$comment")] string Comment,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("underlyingType")] string UnderlyingType,
        [property: JsonPropertyName("hasZeroValue")] bool HasZeroValue,
        [property: JsonPropertyName("bits")] IReadOnlyList<Bit> Bits,
        [property: JsonPropertyName("masks")] IReadOnlyList<Mask> Masks);

    /// <summary>A member that sets exactly one bit.</summary>
    public sealed record Bit(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] int Value,
        [property: JsonPropertyName("hex")] string Hex,
        [property: JsonPropertyName("bit")] int Index);

    /// <summary>A member that names a combination of the single-bit members.</summary>
    public sealed record Mask(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] int Value,
        [property: JsonPropertyName("hex")] string Hex,
        [property: JsonPropertyName("bits")] IReadOnlyList<string> Bits);
}
