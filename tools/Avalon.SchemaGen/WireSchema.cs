using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Avalon.SchemaGen;

/// <summary>
/// Exports the protobuf-net packet contracts as a language-neutral <c>.proto</c> schema.
/// The C# attributes are the definition; this is a rendering of them, and the direction
/// never reverses.
/// </summary>
public static class WireSchema
{
    public const string FileName = "avalon.proto";

    private const string PackageName = "avalon";

    /// <summary>
    /// Every <c>[ProtoContract]</c> type the wire protocol is built from, in a stable order
    /// so that regenerating an unchanged tree produces an unchanged file.
    /// </summary>
    public static IReadOnlyList<Type> ContractTypes()
    {
        Assembly[] assemblies = [typeof(Packet).Assembly, typeof(NetworkPacket).Assembly];

        return assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<ProtoContractAttribute>() is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    public static string Generate()
    {
        var options = new SchemaGenerationOptions
        {
            Syntax = ProtoSyntax.Proto3,

            // protobuf enum values are siblings of their type in the enclosing scope rather
            // than children of it, so the seven enums that each declare a "Success" member
            // would collide and the file would not compile. Prefixing every value with its
            // enum name removes the collision for the enums that exist today and for any
            // added later. It renames only; the wire carries the number.
            Flags = SchemaGenerationFlags.IncludeEnumNamePrefix,

            Package = PackageName,
        };

        foreach (Type type in ContractTypes())
        {
            options.Types.Add(type);
        }

        string schema = RuntimeTypeModel.Default
            .GetSchema(options)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        schema = ApplyExplicitPresence(schema);
        schema = InsertFileOptions(schema);

        return Header + schema;
    }

    /// <summary>
    /// Marks members whose C# type is a nullable value type as <c>optional</c>.
    /// </summary>
    /// <remarks>
    /// protobuf-net writes such a member only when it has a value, so null and zero are
    /// different bytes and the difference carries meaning: a null target guid means "clear
    /// the target". Emitted without the keyword, a reader generated from this schema
    /// re-encodes zero as absent and the distinction disappears with nothing raised.
    /// Deriving the set by reflection rather than naming the members keeps a nullable
    /// member added later from quietly missing out.
    /// </remarks>
    private static string ApplyExplicitPresence(string schema)
    {
        HashSet<(string Message, int FieldNumber)> targets = NullableValueMembers();
        HashSet<(string Message, int FieldNumber)> applied = [];

        var messageStart = new Regex(@"^message (?<name>\S+) \{$", RegexOptions.CultureInvariant);
        var field = new Regex(@"^(?<indent>\s+)(?<declaration>\S.*) = (?<number>\d+)(?<tail>.*);$", RegexOptions.CultureInvariant);

        string[] lines = schema.Split('\n');
        string? message = null;

        for (int i = 0; i < lines.Length; i++)
        {
            Match start = messageStart.Match(lines[i]);
            if (start.Success)
            {
                message = start.Groups["name"].Value;
                continue;
            }

            if (lines[i].StartsWith('}'))
            {
                message = null;
                continue;
            }

            if (message is null)
            {
                continue;
            }

            Match member = field.Match(lines[i]);
            if (!member.Success)
            {
                continue;
            }

            var key = (message, int.Parse(member.Groups["number"].Value, CultureInfo.InvariantCulture));
            if (!targets.Contains(key))
            {
                continue;
            }

            lines[i] = string.Concat(
                member.Groups["indent"].Value,
                "optional ",
                member.Groups["declaration"].Value,
                " = ",
                member.Groups["number"].Value,
                member.Groups["tail"].Value,
                ";");

            applied.Add(key);
        }

        if (!applied.SetEquals(targets))
        {
            IEnumerable<string> missed = targets
                .Except(applied)
                .Select(target => $"{target.Message}.{target.FieldNumber}")
                .OrderBy(name => name, StringComparer.Ordinal);

            throw new InvalidOperationException(
                "Could not mark every nullable member optional. The emitted schema no longer has the shape this " +
                "rewrite expects, so presence would be lost silently. Unmatched: " + string.Join(", ", missed) + ".");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The name a contract type carries in the emitted schema.
    /// </summary>
    public static string SchemaNameOf(Type type)
    {
        ProtoContractAttribute contract = type.GetCustomAttribute<ProtoContractAttribute>()
            ?? throw new ArgumentException($"{type.FullName} is not a protobuf-net contract.", nameof(type));

        return string.IsNullOrEmpty(contract.Name) ? type.Name : contract.Name;
    }

    /// <summary>
    /// The (message, field number) pairs whose C# member is a nullable value type. A
    /// nullable message is excluded: a message field already has presence in proto3.
    /// </summary>
    private static HashSet<(string Message, int FieldNumber)> NullableValueMembers()
    {
        HashSet<(string Message, int FieldNumber)> members = [];

        foreach (Type type in ContractTypes())
        {
            string schemaName = SchemaNameOf(type);

            const BindingFlags instanceMembers =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (MemberInfo member in type.GetMembers(instanceMembers))
            {
                ProtoMemberAttribute? tag = member.GetCustomAttribute<ProtoMemberAttribute>();
                if (tag is null)
                {
                    continue;
                }

                Type? declared = member switch
                {
                    PropertyInfo property => property.PropertyType,
                    FieldInfo backing => backing.FieldType,
                    _ => null,
                };

                Type? underlying = declared is null ? null : Nullable.GetUnderlyingType(declared);
                if (underlying is null || underlying.GetCustomAttribute<ProtoContractAttribute>() is not null)
                {
                    continue;
                }

                members.Add((schemaName, tag.Tag));
            }
        }

        return members;
    }

    /// <summary>
    /// Adds the file-level options protobuf-net has no reason to emit, after the import
    /// block so the file reads in the conventional order.
    /// </summary>
    private static string InsertFileOptions(string schema)
    {
        List<string> lines = [.. schema.Split('\n')];

        if (lines.Count < 3 || lines[0] != "syntax = \"proto3\";" || lines[1] != $"package {PackageName};")
        {
            throw new InvalidOperationException(
                "The emitted schema does not open with the expected syntax and package lines, so the file options " +
                "cannot be placed with confidence.");
        }

        int lastImport = lines.FindLastIndex(line => line.StartsWith("import ", StringComparison.Ordinal));
        if (lastImport < 0)
        {
            throw new InvalidOperationException(
                "The emitted schema declares no imports, which the option placement assumes.");
        }

        // Drops descriptors and reflection from generated C++, and DebugString() with them.
        // Inert for C#, which does not generate from this file.
        lines.Insert(lastImport + 1, "option optimize_for = LITE_RUNTIME;");

        return string.Join("\n", lines);
    }

    private const string Header =
        """
        // avalon.proto - the wire format of the Avalon TCP protocol.
        //
        // GENERATED FILE. An edit made here is discarded by the next regeneration and, until
        // then, describes bytes nobody sends.
        //
        //   Defined by  the [ProtoContract] types in Avalon.Network.Packets and
        //               Avalon.Network.Packets.Abstractions. Those attributes are the
        //               protocol; this file reports them.
        //   Emitted by  tools/Avalon.SchemaGen
        //   Regenerate  dotnet run --project tools/Avalon.SchemaGen
        //   Guarded by  WireSchemaShould in tests/Avalon.Shared.UnitTests, which regenerates
        //               and compares, so a contract change that is not re-exported is a red
        //               build rather than a wrong client.
        //
        // Ownership runs one way. A field a client needs is a C# edit followed by a
        // regeneration, never an edit here.
        //
        // Three things about this file are not protobuf-net defaults:
        //
        //   Enum values carry their enum's name as a prefix. protobuf enum values are
        //   siblings of their type in the enclosing scope rather than children of it, so the
        //   seven enums that each declare a "Success" member would collide and this file
        //   would not compile. Names only; the wire carries the number.
        //
        //   Members whose C# type is a nullable value type are marked "optional", because
        //   for those null and zero are different bytes and the difference is meaningful - a
        //   null target guid means "clear the target". Without the keyword a generated
        //   reader re-encodes zero as absent and the distinction is lost with nothing
        //   raised. The same null-versus-empty split exists for string and bytes members,
        //   which are not marked: absent and empty decode alike, so only a byte-for-byte
        //   re-encode can tell them apart.
        //
        //   "[packed = false]" on repeated scalars matches what the server writes. Readers
        //   accept either encoding, so losing it would not fail loudly.
        //
        // .bcl.DateTime and .bcl.Guid are protobuf-net's own representations and NOT the
        // google.protobuf well-known types. bcl.DateTime is a scaled offset from the Unix
        // epoch; bcl.Guid is two fixed64s in .NET's byte order rather than sixteen bytes in
        // RFC order. A consumer must convert, and one that assumes the well-known types
        // reads plausible wrong values with no error. protobuf-net/bcl.proto is vendored
        // next to this file. Whether those five fields could instead be int64 and bytes is
        // open: nothing has established whether a database column depends on the Guid form.
        //
        // optimize_for = LITE_RUNTIME governs generated C++ only and is inert for C#. Note
        // that bcl.proto is not itself lite.

        """;
}
