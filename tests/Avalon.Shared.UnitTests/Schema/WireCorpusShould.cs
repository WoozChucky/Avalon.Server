using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalon.SchemaGen;
using Xunit;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Holds the checked-in corpus to the fixtures it was generated from, and holds those
/// fixtures to covering the cases they exist for.
/// </summary>
/// <remarks>
/// The corpus is what a client tests against without running .NET, so a change in what the
/// server emits has to arrive here as a diff someone reads rather than as bytes that quietly
/// stopped matching. And a corpus that covered only ordinary values would agree with a reader
/// that gets presence wrong, which is the failure it is meant to catch, so the coverage of the
/// awkward cases is asserted rather than assumed.
/// </remarks>
public class WireCorpusShould
{
    [Fact]
    public void Match_The_Fixtures_It_Was_Exported_From()
    {
        foreach ((string name, string generated) in WireCorpus.Generate().OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            string path = Path.Combine(Corpus.Directory, name);

            Assert.True(
                File.Exists(path),
                $"schema/{WireCorpus.DirectoryName}/{name} is missing. Regenerate the corpus with:"
                + $"{Environment.NewLine}    {Corpus.RegenerateCommand}");

            string[] checkedIn = Lines(File.ReadAllText(path));
            string[] regenerated = Lines(generated);

            if (!checkedIn.SequenceEqual(regenerated, StringComparer.Ordinal))
            {
                Assert.Fail(Explain(name, checkedIn, regenerated));
            }
        }
    }

    /// <summary>
    /// A message removed from the protocol must lose its file. Otherwise the corpus keeps
    /// asserting bytes for a packet nobody sends, and reads as coverage.
    /// </summary>
    [Fact]
    public void Hold_One_File_Per_Message_And_Nothing_Else()
    {
        IEnumerable<string> present = Directory.EnumerateFiles(Corpus.Directory, "*.txt").Select(Path.GetFileName)!;
        IEnumerable<string> expected = WireCorpus.Generate().Keys;

        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            present.Order(StringComparer.Ordinal)!);
    }

    /// <summary>
    /// The three states a string or bytes member can be in, each seen at least once for each
    /// such member in the protocol.
    /// </summary>
    /// <remarks>
    /// Nullability does not decide this. <c>ReadOnlyMemory&lt;byte&gt;</c> is a value type and
    /// still has the empty-versus-filled split, and the empty case is one the server sends in
    /// production, so the states are read off the encoded bytes rather than inferred from the
    /// C# type.
    /// </remarks>
    [Fact]
    public void Cover_Absent_Empty_And_Filled_For_Every_String_And_Bytes_Member()
    {
        List<string> gaps = [];

        foreach ((Type contract, WireMember member) in LengthDelimitedMembers())
        {
            string message = WireSchema.SchemaNameOf(contract);
            HashSet<string> seen = [];

            foreach (WireVector vector in Corpus.VectorsFor(message))
            {
                WireField[] fields = WireBytes.Fields(vector.Bytes)
                    .Where(field => field.Number == member.Tag)
                    .ToArray();

                seen.Add(fields.Length == 0 ? "absent" : fields[0].PayloadLength == 0 ? "empty" : "filled");
            }

            // A member whose type is a value type cannot be null, so absent is unreachable
            // for it and its own test says so. Every other state is required of everything.
            string[] required = member.DeclaredType.IsValueType
                ? ["empty", "filled"]
                : ["absent", "empty", "filled"];

            gaps.AddRange(required
                .Where(state => !seen.Contains(state))
                .Select(state => $"{message}.{member.Name} is never {state} in the corpus"));
        }

        Assert.True(gaps.Count == 0, string.Join(Environment.NewLine, gaps));
    }

    /// <summary>
    /// The same three states for the members a length-delimited field cannot describe from the
    /// wire alone: an empty repeated field encodes as nothing at all, so it is indistinguishable
    /// from a null one and has to be checked on the values instead.
    /// </summary>
    [Fact]
    public void Cover_Absent_Empty_And_Filled_For_Every_Repeated_Member()
    {
        List<string> gaps = [];

        foreach (Type contract in WireSchema.ContractTypes())
        {
            foreach (WireMember member in WireFixtures.Members(contract)
                         .Where(member => WireFixtures.RepeatedElementType(member.DeclaredType) is not null))
            {
                HashSet<string> seen = [];

                foreach (FixtureVariant variant in WireFixtures.VariantsFor(contract))
                {
                    object? value = member.Read(WireFixtures.Build(contract, variant));

                    seen.Add(value switch
                    {
                        null => "absent",
                        IEnumerable items when !items.Cast<object?>().Any() => "empty",
                        _ => "filled",
                    });
                }

                gaps.AddRange(new[] { "absent", "empty", "filled" }
                    .Where(state => !seen.Contains(state))
                    .Select(state => $"{WireSchema.SchemaNameOf(contract)}.{member.Name} is never {state}"));
            }
        }

        Assert.True(gaps.Count == 0, string.Join(Environment.NewLine, gaps));
    }

    /// <summary>
    /// Every float bit pattern the fixtures are supposed to carry reaches at least one of
    /// them. Missing one would leave a class of value untested with nothing to say so.
    /// </summary>
    [Fact]
    public void Carry_Every_Float_Edge_Case()
    {
        HashSet<int> carried = [];

        foreach (Type contract in WireSchema.ContractTypes())
        {
            foreach (FixtureVariant variant in WireFixtures.VariantsFor(contract))
            {
                Collect(contract, WireFixtures.Build(contract, variant), carried);
            }
        }

        IEnumerable<string> missing = WireFixtures.FloatEdgeCases()
            .Where(value => !carried.Contains(BitConverter.SingleToInt32Bits(value)))
            .Select(value => $"0x{BitConverter.SingleToInt32Bits(value).ToString("x8", CultureInfo.InvariantCulture)}");

        Assert.True(
            !missing.Any(),
            "No fixture in the corpus carries the float bit pattern " + string.Join(", ", missing) + ".");
    }

    private static void Collect(Type contract, object instance, HashSet<int> carried)
    {
        foreach (WireMember member in WireFixtures.Members(contract))
        {
            Type element = WireFixtures.RepeatedElementType(member.DeclaredType) ?? member.DeclaredType;
            object? value = member.Read(instance);

            if (value is null)
            {
                continue;
            }

            foreach (object? item in WireFixtures.RepeatedElementType(member.DeclaredType) is null
                         ? [value]
                         : ((IEnumerable)value).Cast<object?>())
            {
                if (item is float number)
                {
                    carried.Add(BitConverter.SingleToInt32Bits(number));
                }
                else if (item is not null && WireFixtures.IsContract(element))
                {
                    Collect(element, item, carried);
                }
            }
        }
    }

    private static IEnumerable<(Type Contract, WireMember Member)> LengthDelimitedMembers() =>
        from contract in WireSchema.ContractTypes()
        from member in WireFixtures.Members(contract)
        where member.DeclaredType == typeof(string)
            || member.DeclaredType == typeof(byte[])
            || member.DeclaredType == typeof(ReadOnlyMemory<byte>)
        select (contract, member);

    private static string Explain(string name, string[] checkedIn, string[] regenerated)
    {
        int shared = Math.Min(checkedIn.Length, regenerated.Length);
        int first = 0;

        while (first < shared && string.Equals(checkedIn[first], regenerated[first], StringComparison.Ordinal))
        {
            first++;
        }

        return string.Join(Environment.NewLine,
            $"schema/{WireCorpus.DirectoryName}/{name} no longer matches the fixtures it was exported from.",
            string.Empty,
            "If a contract changed on purpose, re-export the corpus and commit it:",
            $"    {Corpus.RegenerateCommand}",
            string.Empty,
            $"First difference at line {(first + 1).ToString(CultureInfo.InvariantCulture)}"
                + $" (checked in has {checkedIn.Length.ToString(CultureInfo.InvariantCulture)} lines,"
                + $" generated has {regenerated.Length.ToString(CultureInfo.InvariantCulture)}).",
            $"    < {At(checkedIn, first)}      (checked in)",
            $"    > {At(regenerated, first)}      (generated)");
    }

    private static string At(string[] lines, int index) => index < lines.Length ? lines[index] : "<end of file>";

    // Compares content rather than encoding: a working tree checked out with CRLF is not drift.
    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
