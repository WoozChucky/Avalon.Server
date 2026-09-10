namespace Avalon.SchemaGen;

/// <summary>
/// Numeric ladders. The interesting values for an integer are not "zero and something else":
/// they are the ones that change how many bytes a varint occupies, and the ones where a
/// signed type sign-extends to ten.
/// </summary>
internal static class FixtureNumbers
{
    private static readonly decimal[] SignedOrdinary = [11, -3, 1000, -9, 70000, -262144, 5_000_000_000, 19];
    private static readonly decimal[] UnsignedOrdinary = [7, 300, 65535, 4_000_000_000, 19, 1_000_000, 42];

    // 2^7, 2^14, 2^21, 2^28 and 2^35, each side of the boundary, plus -1, which is the
    // shortest negative and the longest varint a signed field can produce.
    private static readonly decimal[] Edges =
    [
        127, 128, 16383, 16384, 2097151, 2097152, 268435455, 268435456, 34359738367, 34359738368, -1,
    ];

    private static readonly float[] Ordinary = [1.5f, -2.25f, 3.1415927f, 1e10f, -7.5e-8f, 0.1f];

    internal static object Value(Type declared, FixtureVariant variant, string path, int ordinal)
    {
        if (declared == typeof(bool))
        {
            return variant is not (FixtureVariant.Absent or FixtureVariant.Empty or FixtureVariant.Minima);
        }

        if (declared == typeof(float))
        {
            return FixtureFloats.Value(variant, path, ordinal);
        }

        (decimal min, decimal max) = Range(declared);

        return Convert.ChangeType(Whole(min, max, variant, path, ordinal), declared, FixtureValues.Invariant);
    }

    /// <summary>
    /// The value as a <see cref="decimal"/>, which is the only primitive wide enough to hold
    /// every candidate for every integer type without wrapping on the way to the conversion.
    /// </summary>
    private static decimal Whole(decimal min, decimal max, FixtureVariant variant, string path, int ordinal) =>
        variant switch
        {
            FixtureVariant.Absent or FixtureVariant.Empty => 0m,
            FixtureVariant.Maxima => max,
            FixtureVariant.Minima => min,
            FixtureVariant.VarintEdges => Choose(Edges, min, max, path, ordinal),
            _ => Choose(min < 0 ? SignedOrdinary : UnsignedOrdinary, min, max, path, ordinal),
        };

    /// <summary>
    /// Picks from the candidates the member's type can actually hold. Folding an out-of-range
    /// candidate into range instead would land several members on the same value and, for the
    /// narrow types, often on zero - which encodes as nothing and tests nothing.
    /// </summary>
    private static decimal Choose(decimal[] candidates, decimal min, decimal max, string path, int ordinal)
    {
        decimal[] fits = candidates.Where(value => value >= min && value <= max).ToArray();

        return fits.Length == 0 ? max : fits[FixtureValues.Pick(path, ordinal, fits.Length)];
    }

    internal static (decimal Min, decimal Max) Range(Type declared) => Type.GetTypeCode(declared) switch
    {
        TypeCode.Byte => (byte.MinValue, byte.MaxValue),
        TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
        TypeCode.Int16 => (short.MinValue, short.MaxValue),
        TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
        TypeCode.Int32 => (int.MinValue, int.MaxValue),
        TypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
        TypeCode.Int64 => (long.MinValue, long.MaxValue),
        TypeCode.UInt64 => (ulong.MinValue, ulong.MaxValue),
        _ => throw new NotSupportedException($"No fixture ladder for {declared.FullName}."),
    };

    internal static float OrdinaryFloat(string path, int ordinal) =>
        Ordinary[FixtureValues.Pick(path, ordinal, Ordinary.Length)];
}

/// <summary>
/// Text ladders, including the lengths where a length prefix grows a byte and the character
/// ranges where a reader that assumes ASCII stops agreeing.
/// </summary>
internal static class FixtureText
{
    private static readonly string[] Ordinary =
    [
        "alpha",
        "héllo wörld",
        "日本語のテキスト",
        "line\nbreak\ttab",
        "emoji 🜁🚀 astral",
        "\"quoted\" and \\backslash\\",
        "Ωμέγα ΔΕΛΤΑ",
    ];

    // 127, 128 and 129 UTF-8 bytes. The middle one is where the length prefix stops fitting
    // in a single varint byte; the third is deliberately not ASCII, so a reader that counts
    // characters rather than bytes disagrees about where the field ends.
    private static readonly string[] Edges =
    [
        new('x', 127),
        new('y', 128),
        new string('é', 40) + new string('z', 49),
    ];

    // The shortest strings that are not one another: a NUL, a control character, and a
    // space. The control character is written as an escape because the byte itself is
    // invisible in an editor, in a diff and in a terminal, so any pass that strips control
    // characters - a lint autofix, a copy through a channel that sanitises them - would
    // rewrite it into a second empty string and leave a diff that renders alike on both
    // sides. The empty string itself is what the empty variant carries.
    private static readonly string[] Minimal = ["\0", "\u0001", " "];

    internal static string? Value(FixtureVariant variant, string path, int ordinal) => variant switch
    {
        FixtureVariant.Absent => null,
        FixtureVariant.Empty => string.Empty,
        FixtureVariant.Minima => Minimal[FixtureValues.Pick(path, ordinal, Minimal.Length)],
        // 132 UTF-8 bytes: past the point where the length prefix needs a second byte,
        // without making every long-string vector in the corpus twice the size it has to be.
        FixtureVariant.Maxima => string.Concat(Enumerable.Repeat("Ωx", 44)),
        FixtureVariant.VarintEdges => Edges[FixtureValues.Pick(path, ordinal, Edges.Length)],
        _ => Ordinary[FixtureValues.Pick(path, ordinal, Ordinary.Length)],
    };
}

/// <summary>
/// Binary ladders. Lengths mirror the text ones, and the contents cover the byte values a
/// reader that round-trips through text would mangle.
/// </summary>
internal static class FixtureBinary
{
    private static readonly byte[][] Ordinary =
    [
        [0x01, 0x02, 0x03],
        [0x00],
        [0xff, 0x00, 0x7f, 0x80],
        [0xde, 0xad, 0xbe, 0xef],
        [0x0a, 0x0d, 0x09, 0x1b, 0x22, 0x5c],
    ];

    internal static byte[]? Value(FixtureVariant variant, string path, int ordinal) => variant switch
    {
        FixtureVariant.Absent => null,
        FixtureVariant.Empty => [],
        FixtureVariant.Minima => [0x00],
        FixtureVariant.Maxima => Pattern(140),
        FixtureVariant.VarintEdges => Pattern(127 + FixtureValues.Pick(path, ordinal, 3)),
        _ => Ordinary[FixtureValues.Pick(path, ordinal, Ordinary.Length)],
    };

    private static byte[] Pattern(int length)
    {
        byte[] bytes = new byte[length];

        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 7) ^ 0x5a);
        }

        return bytes;
    }
}

/// <summary>
/// The values that make a float encoding observable. Every one of these is a distinct bit
/// pattern, and a comparison that goes through text is the classic way to lose one.
/// </summary>
internal static class FixtureFloats
{
    internal static readonly float[] Specials =
    [
        float.NaN,
        BitConverter.Int32BitsToSingle(0x7FC00001),
        float.PositiveInfinity,
        float.NegativeInfinity,
        -0.0f,
        float.Epsilon,
        BitConverter.Int32BitsToSingle(0x007FFFFF),
        float.MaxValue,
    ];

    internal static float Value(FixtureVariant variant, string path, int ordinal) => variant switch
    {
        FixtureVariant.Absent or FixtureVariant.Empty => 0f,
        FixtureVariant.Maxima => float.MaxValue,
        FixtureVariant.Minima => float.MinValue,
        FixtureVariant.VarintEdges => BitConverter.Int32BitsToSingle(0x00800000 + ordinal),
        FixtureVariant.FloatSpecials => Special(path, ordinal),
        _ => FixtureNumbers.OrdinaryFloat(path, ordinal),
    };

    /// <summary>
    /// Assigns specials by the member's position in the sorted set of every float member in
    /// the protocol, so that all of them appear somewhere in the corpus rather than probably
    /// appearing. A hash would leave whichever one it happened to miss untested, silently.
    /// </summary>
    private static float Special(string path, int ordinal)
    {
        IReadOnlyList<string> paths = WireFixtures.FloatMemberPaths();

        for (int position = 0; position < paths.Count; position++)
        {
            if (string.Equals(paths[position], path, StringComparison.Ordinal))
            {
                return Specials[(position + ordinal) % Specials.Length];
            }
        }

        return Specials[FixtureValues.Pick(path, ordinal, Specials.Length)];
    }
}

/// <summary>
/// The two .NET types protobuf-net carries through its own representations rather than the
/// well-known ones, which is exactly where a client author's assumption is most expensive.
/// </summary>
internal static class FixtureBcl
{
    private static readonly DateTime[] Moments =
    [
        new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
        new(1999, 12, 31, 23, 59, 59, DateTimeKind.Unspecified),
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(1),
        new(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc),
    ];

    private static readonly Guid[] Identifiers =
    [
        new("0f8fad5b-d9cb-469f-a165-70867728950e"),

        // Byte-ordered so that a consumer reading a bcl.Guid as sixteen bytes in RFC order
        // produces a visibly wrong value rather than a plausible one.
        new("01020304-0506-0708-090a-0b0c0d0e0f10"),
    ];

    internal static DateTime Moment(FixtureVariant variant, string path, int ordinal) => variant switch
    {
        FixtureVariant.Absent or FixtureVariant.Empty => default,
        FixtureVariant.Maxima => DateTime.MaxValue,

        // Not DateTime.MinValue: that is the type's default, so it is omitted entirely and
        // says nothing about how a pre-epoch offset is encoded.
        FixtureVariant.Minima => DateTime.UnixEpoch.AddTicks(-1),
        FixtureVariant.VarintEdges => DateTime.UnixEpoch.AddSeconds(128),
        _ => Moments[FixtureValues.Pick(path, ordinal, Moments.Length)],
    };

    internal static Guid Identifier(FixtureVariant variant, string path, int ordinal) => variant switch
    {
        FixtureVariant.Absent or FixtureVariant.Empty or FixtureVariant.Minima => Guid.Empty,
        FixtureVariant.Maxima => new Guid(Enumerable.Repeat((byte)0xff, 16).ToArray()),
        FixtureVariant.VarintEdges => Identifiers[1],
        _ => Identifiers[FixtureValues.Pick(path, ordinal, Identifiers.Length)],
    };
}

/// <summary>
/// Enum ladders, including a value the enum does not declare. protobuf enums are open, and
/// <c>NetworkPacketFlags</c> is a composite type whose everyday values - a flag set with two
/// bits - are not members of it.
/// </summary>
internal static class FixtureEnums
{
    internal static object Value(Type declared, FixtureVariant variant, string path, int ordinal)
    {
        Type underlying = Enum.GetUnderlyingType(declared);

        long[] declared64 = Enum.GetValues(declared)
            .Cast<object>()
            .Select(value => Convert.ToInt64(value, FixtureValues.Invariant))
            .Distinct()
            .Order()
            .ToArray();

        long chosen = variant switch
        {
            FixtureVariant.Absent or FixtureVariant.Empty => 0,
            FixtureVariant.Maxima => declared64[^1],
            FixtureVariant.Minima => declared64[0],
            FixtureVariant.VarintEdges => Undeclared(underlying, declared64),
            _ => NonZero(declared64, path, ordinal),
        };

        return Enum.ToObject(declared, Convert.ChangeType(chosen, underlying, FixtureValues.Invariant));
    }

    private static long NonZero(long[] declared64, string path, int ordinal)
    {
        long[] candidates = declared64.Where(value => value != 0).ToArray();

        return candidates.Length == 0 ? 0 : candidates[FixtureValues.Pick(path, ordinal, candidates.Length)];
    }

    private static long Undeclared(Type underlying, long[] declared64)
    {
        (decimal min, decimal max) = FixtureNumbers.Range(underlying);

        for (long candidate = declared64[^1] + 1; candidate <= max; candidate++)
        {
            if (!declared64.Contains(candidate))
            {
                return candidate;
            }
        }

        for (long candidate = declared64[0] - 1; candidate >= min; candidate--)
        {
            if (!declared64.Contains(candidate))
            {
                return candidate;
            }
        }

        return declared64[^1];
    }
}
