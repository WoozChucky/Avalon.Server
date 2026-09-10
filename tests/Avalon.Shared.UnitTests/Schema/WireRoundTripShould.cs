using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalon.SchemaGen;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Holds the bytes the server writes against a reader generated from the checked-in schema.
/// </summary>
/// <remarks>
/// The schema is exported from the C# contracts and guarded against drifting from them, so it
/// describes the contracts. That is not the same as the bytes agreeing: a reader generated
/// from the schema can parse what the server writes, re-encode it, and produce something
/// different, and nothing on either side raises anything. These tests are the difference
/// between those two claims.
///
/// The reference implementation is <c>Avalon.Wire.Reference</c>, which is
/// <c>schema/avalon.proto</c> compiled by <c>protoc</c> at build time. Comparison is on bytes
/// rather than through protobuf's text format, so NaN, the infinities, denormals and negative
/// zero are compared as the bit patterns they are.
/// </remarks>
public class WireRoundTripShould
{
    [Theory]
    [MemberData(nameof(Corpus.Messages), MemberType = typeof(Corpus))]
    public void Re_Encode_The_Server_Bytes_Unchanged(string message)
    {
        MessageDescriptor descriptor = ReferenceSchema.For(message);

        foreach (WireVector vector in Corpus.VectorsFor(message))
        {
            IMessage parsed = Parse(descriptor, vector);
            byte[] reEncoded = parsed.ToByteArray();

            // Not vector.Bytes: proto3 cannot express a present-but-empty string or bytes
            // field, so a reader generated from this schema drops one. That is the single
            // documented difference, and stating it as a transformation rather than as a
            // tolerance means any other difference still fails.
            byte[] expected = WireBytes.WithoutEmptyStringsAndBytes(vector.Bytes, descriptor);

            Assert.True(
                expected.AsSpan().SequenceEqual(reEncoded),
                Because(vector, "re-encoding by the reference reader changed the bytes", expected, reEncoded, descriptor));
        }
    }

    [Theory]
    [MemberData(nameof(Corpus.Messages), MemberType = typeof(Corpus))]
    public void Read_Back_What_The_Reference_Writes(string message)
    {
        MessageDescriptor descriptor = ReferenceSchema.For(message);
        Type contract = Corpus.ContractFor(message);

        foreach (WireVector vector in Corpus.VectorsFor(message))
        {
            byte[] fromReference = Parse(descriptor, vector).ToByteArray();
            byte[] andBack = WireCorpus.Serialize(WireCorpus.Deserialize(contract, fromReference));

            // Empty fields are set aside on both sides. They move in this direction rather
            // than out of it: a member the contract initializes comes back empty rather than
            // null, so the server writes a field the reference reader had left out. That is
            // the server's own encode-decode asymmetry, not a disagreement about the bytes.
            byte[] expected = WireBytes.WithoutEmptyLengthDelimited(fromReference);
            byte[] actual = WireBytes.WithoutEmptyLengthDelimited(andBack);

            Assert.True(
                expected.AsSpan().SequenceEqual(actual),
                Because(vector, "the server did not read back what the reference reader wrote", expected, actual, descriptor));
        }
    }

    [Theory]
    [MemberData(nameof(Corpus.Messages), MemberType = typeof(Corpus))]
    public void Carry_Every_Field_Into_The_Member_It_Came_From(string message)
    {
        MessageDescriptor descriptor = ReferenceSchema.For(message);
        Type contract = Corpus.ContractFor(message);
        List<string> differences = [];

        foreach (WireVector vector in Corpus.VectorsFor(message))
        {
            object fixture = WireFixtures.Build(contract, Variant(vector));
            IMessage parsed = Parse(descriptor, vector);

            differences.AddRange(WireValues
                .Differences(contract, fixture, parsed)
                .Select(difference => $"[{vector.Variant}] {difference}"));
        }

        Assert.True(
            differences.Count == 0,
            $"The reference reader read {message} into different values than the server serialized:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", differences));
    }

    [Theory]
    [MemberData(nameof(Corpus.Messages), MemberType = typeof(Corpus))]
    public void Write_Only_Fields_The_Schema_Declares(string message)
    {
        MessageDescriptor descriptor = ReferenceSchema.For(message);

        foreach (WireVector vector in Corpus.VectorsFor(message))
        {
            IEnumerable<int> undeclared = WireBytes.Fields(vector.Bytes)
                .Select(field => field.Number)
                .Where(number => descriptor.FindFieldByNumber(number) is null)
                .Distinct();

            Assert.True(
                !undeclared.Any(),
                $"{message} [{vector.Variant}] puts field "
                + string.Join(", ", undeclared.Select(number => number.ToString(CultureInfo.InvariantCulture)))
                + " on the wire and the schema does not declare it, so a client would skip it as unknown."
                + Environment.NewLine + "    " + WireBytes.Hex(vector.Bytes));
        }
    }

    /// <summary>
    /// The schema and the corpus have to describe the same set of messages, or a packet is
    /// being exported without being verified, or verified without being exported.
    /// </summary>
    [Fact]
    public void Cover_Every_Message_The_Schema_Declares()
    {
        IEnumerable<string> exported = ReferenceSchema.Messages.Select(message => message.Name);
        IEnumerable<string> covered = WireSchema.ContractTypes().Select(WireSchema.SchemaNameOf);

        Assert.Equal(exported.Order(StringComparer.Ordinal), covered.Order(StringComparer.Ordinal));
    }

    private static IMessage Parse(MessageDescriptor descriptor, WireVector vector)
    {
        try
        {
            return descriptor.Parser.ParseFrom(vector.Bytes);
        }
        catch (InvalidProtocolBufferException exception)
        {
            Assert.Fail(
                $"The reference reader could not parse {vector.Message} [{vector.Variant}]: {exception.Message}"
                + Environment.NewLine + "    " + WireBytes.Hex(vector.Bytes));
            throw;
        }
    }

    private static FixtureVariant Variant(WireVector vector) =>
        Enum.GetValues<FixtureVariant>().Single(variant =>
            string.Equals(WireFixtures.NameOf(variant), vector.Variant, StringComparison.Ordinal));

    private static string Because(
        WireVector vector,
        string what,
        byte[] expected,
        byte[] actual,
        MessageDescriptor descriptor) =>
        $"{vector.Message} [{vector.Variant}]: {what}."
        + Environment.NewLine
        + WireBytes.Explain(expected, actual, descriptor)
        + Environment.NewLine
        + $"If the contract changed on purpose, re-export first: {Corpus.RegenerateCommand}";
}
