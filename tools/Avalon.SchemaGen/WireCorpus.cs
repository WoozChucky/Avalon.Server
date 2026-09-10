using System.Globalization;
using System.Text;
using ProtoBuf;

namespace Avalon.SchemaGen;

/// <summary>One frozen vector: the bytes protobuf-net writes for one fixture.</summary>
public sealed record WireVector(string Message, string Variant, byte[] Bytes);

/// <summary>
/// The golden corpus: for every message in the schema, the bytes the server's serializer
/// produces for a set of deliberately awkward values.
/// </summary>
/// <remarks>
/// The schema says what the protocol is meant to be; this says what it is. A client is
/// conformant when it reproduces these bytes, which it can check without running .NET, and a
/// change in what protobuf-net emits shows up as a diff here rather than as a client that
/// decodes a field it was never sent.
/// </remarks>
public static class WireCorpus
{
    public const string DirectoryName = "corpus";

    private const string Extension = ".txt";

    /// <summary>The corpus files, keyed by file name, in the order they should be written.</summary>
    public static IReadOnlyDictionary<string, string> Generate()
    {
        Dictionary<string, string> files = [];

        foreach (Type contract in WireSchema.ContractTypes())
        {
            files.Add(FileNameFor(contract), Render(contract));
        }

        return files;
    }

    public static string FileNameFor(Type contract) => WireSchema.SchemaNameOf(contract) + Extension;

    /// <summary>
    /// Reads back what <see cref="Generate"/> wrote. The corpus is only a conformance artifact
    /// if something reads the checked-in bytes rather than regenerating them, so this is the
    /// half that makes the files load-bearing.
    /// </summary>
    public static IReadOnlyList<WireVector> Parse(string message, string content)
    {
        List<WireVector> vectors = [];
        string? variant = null;
        List<byte> bytes = [];

        foreach (string line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.Trim();

            if (line.StartsWith("variant ", StringComparison.Ordinal))
            {
                Flush(vectors, message, ref variant, bytes);
                variant = line["variant ".Length..].Trim();
            }
            else if (line.StartsWith("    ", StringComparison.Ordinal) && IsHex(trimmed))
            {
                foreach (string pair in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    bytes.Add(byte.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }
            }
        }

        Flush(vectors, message, ref variant, bytes);

        return vectors;
    }

    private static void Flush(List<WireVector> vectors, string message, ref string? variant, List<byte> bytes)
    {
        if (variant is not null)
        {
            vectors.Add(new WireVector(message, variant, bytes.ToArray()));
        }

        variant = null;
        bytes.Clear();
    }

    private static bool IsHex(string trimmed) =>
        trimmed.Length > 0 && trimmed.All(c => c is ' ' or (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>
    /// The bytes the server writes for a message, through the same entry point
    /// <c>PacketSerializationHelper</c> uses.
    /// </summary>
    public static byte[] Serialize(object fixture)
    {
        using var buffer = new MemoryStream();
        Serializer.Serialize(buffer, fixture);
        return buffer.ToArray();
    }

    /// <summary>The reverse, for checking that what a client sends the server can read.</summary>
    public static object Deserialize(Type contract, byte[] bytes)
    {
        using var buffer = new MemoryStream(bytes);
        return Serializer.Deserialize(contract, buffer);
    }

    private static string Render(Type contract)
    {
        string name = WireSchema.SchemaNameOf(contract);
        var text = new StringBuilder();

        text.Append(Header(name));
        text.Append("\nmessage ").Append(name).Append('\n');

        foreach (FixtureVariant variant in WireFixtures.VariantsFor(contract))
        {
            object fixture = WireFixtures.Build(contract, variant);

            text.Append("\nvariant ").Append(WireFixtures.NameOf(variant)).Append('\n');
            FixtureRendering.WriteMembers(text, contract, fixture, indent: 2);
            WriteBytes(text, Serialize(fixture));
        }

        return text.ToString();
    }

    private static void WriteBytes(StringBuilder text, byte[] bytes)
    {
        text.Append("  bytes ").Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');

        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            text.Append("    ");

            for (int i = offset; i < Math.Min(offset + 16, bytes.Length); i++)
            {
                if (i > offset)
                {
                    text.Append(' ');
                }

                text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            text.Append('\n');
        }
    }

    private static string Header(string name) =>
        $"""
         # {name} - golden wire vectors.
         #
         # GENERATED FILE. The bytes below are what the server's serializer writes for the
         # values shown above them, so a client is conformant when it produces the same ones,
         # and a change in what the server emits arrives here as a diff.
         #
         #   Emitted by  tools/Avalon.SchemaGen
         #   Regenerate  dotnet run --project tools/Avalon.SchemaGen
         #   Format      described in schema/README.md
         #   Schema      the message of this name in schema/avalon.proto
         #
         # A variant is one shape of the same message. "absent" sets nothing, "empty" sets
         # everything to a present-but-empty value, and the rest carry values chosen to make
         # a particular encoding decision observable.

         """;
}
