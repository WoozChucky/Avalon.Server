using Avalon.Common;

namespace Avalon.Exporter;

/// <summary>
/// Exports the raw values <see cref="ObjectGuid"/> packs a (type, id) pair into. Every pair below
/// goes through the real <see cref="ObjectGuid"/>, so the constants are read off the server's own
/// type rather than re-typed here -- a transcription would agree with whatever it transcribed,
/// which is the failure this file exists to catch.
/// </summary>
public static class ObjectGuidVectors
{
    public const string FileName = "object-guid-v1.txt";

    public static void Write(string outputPath)
    {
        var w = new StringWriter { NewLine = "\n" };
        w.Write(Header);

        var types = new[] { ObjectType.None, ObjectType.Character, ObjectType.Creature,
                            ObjectType.Spell, ObjectType.SpellProjectile, ObjectType.Portal };
        // Zero, one, a value with bits in every byte of the low 32, and the top of the range.
        var ids = new uint[] { 0u, 1u, 0x12345678u, uint.MaxValue };

        var rows = 0;
        foreach (ObjectType t in types)
            foreach (uint id in ids)
            {
                var g = new ObjectGuid(t, id);
                w.WriteLine($"raw {g.RawValue:x16} type {(int)g.Type} id {g.Id}");
                ++rows;
            }

        Lf.Write(outputPath, w.ToString());
        Console.WriteLine($"wrote {outputPath} ({rows} vectors; {types.Length} types x {ids.Length} ids)");
    }

    private const string Header = """
        # ObjectGuid - known-answer vectors for the (type, id) <-> raw packing.
        #
        # GENERATED FILE. Every raw value below is what the server's own ObjectGuid packs the type and
        # the id on the same line into, so a client is conformant when it reproduces them, and a change
        # in how the server addresses objects arrives here as a diff.
        #
        #   Emitted by  tools/Avalon.Exporter  (Avalon.Server)
        #   Regenerate  dotnet run --project tools/Avalon.Exporter -- object-guid
        #   Source      src/Shared/Avalon.Common/ObjectGuid.cs
        #
        # The guid is the only name the world-state stream gives an object, so a client that unpacks one
        # wrongly does not fail -- it addresses a different object, consistently, and every test written
        # against its own packing agrees with it. Only these numbers disagree.
        #
        #   raw   = ((ulong)type << 56) | (id & 0x000000FFFFFFFFFF)
        #   type  = (raw & 0xFF00000000000000) >> 56
        #   id    = (uint)(raw & 0x000000FFFFFFFFFF)
        #
        # MIRROR THE TRUNCATION, DO NOT FIX IT: the id mask is 40 bits wide and the accessor returns a
        # 32-bit uint, so bits 32..39 of a raw value are masked in and then dropped on the way out. The
        # rows cannot show it, being built from a uint id that has no such bits -- a raw value that did
        # would be a server change, and it would arrive here as one.
        #
        # Types are None=0, Character=1, Creature=2, Spell=3, SpellProjectile=4, Portal=5. The ids are
        # chosen so a wrong shift or a wrong mask lands somewhere else: zero, one, a value with bits in
        # every byte of the low 32, and the top of the range.
        #
        # Format: raw <hex16> type <n> id <n>


        """;
}
