using System;
using System.Linq;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.State;
using Avalon.SchemaGen;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// The edges of what the schema can say about the bytes the server writes, each pinned with
/// the bytes rather than described.
/// </summary>
/// <remarks>
/// Nothing here corrupts a value and nothing here raises anything, which is exactly why these
/// are written down with their bytes in them: an edge nobody can point at gets rediscovered
/// instead of decided about.
///
/// The first three used to be losses. A singular string or bytes field carries explicit
/// presence now, so an empty one survives a reader generated from the schema; these hold that
/// open, from the production call site and the hot-path member that first showed it closed.
/// The rest are still open, and none of them is a disagreement between the two encoders: two
/// are the server's own behaviour with no schema involved, and the last is a value neither
/// side can send.
/// </remarks>
public class WireLimitsShould
{
    /// <summary>
    /// The empty byte array in this test is not hypothetical: <c>CWorldSelectHandler</c> sends
    /// it on every duplicate-session rejection, through the factory called here.
    /// </summary>
    [Fact]
    public void Carry_The_Empty_Byte_Array_A_World_Select_Rejection_Sends()
    {
        Avalon.Network.Packets.Abstractions.NetworkPacket rejection =
            SWorldSelectPacket.CreateError(WorldSelectResult.DuplicateSession, plaintext => plaintext.ToArray());

        // Field 1 is WorldKey, present and empty; field 2 is the result.
        Assert.Equal(new byte[] { 0x0a, 0x00, 0x10, 0x01 }, rejection.Payload);

        MessageDescriptor descriptor = ReferenceSchema.For(nameof(SWorldSelectPacket));
        IMessage read = descriptor.Parser.ParseFrom(rejection.Payload);

        // The two leading bytes survive, so a client can still tell this from a packet that
        // never carried a world key at all.
        Assert.True(descriptor.FindFieldByNumber(1)!.Accessor.HasValue(read));
        Assert.Equal(new byte[] { 0x0a, 0x00, 0x10, 0x01 }, read.ToByteArray());
    }

    /// <summary>
    /// The counterpart for strings, which behave the same way and are far more common: eleven
    /// members in the protocol initialize to <c>string.Empty</c>, so a default-constructed
    /// instance of one of those contracts sends two of these.
    /// </summary>
    [Fact]
    public void Carry_An_Empty_String_The_Same_Way()
    {
        byte[] bytes = WireCorpus.Serialize(new CAuthPacket { Username = string.Empty, Password = string.Empty });

        Assert.Equal(new byte[] { 0x0a, 0x00, 0x12, 0x00 }, bytes);

        MessageDescriptor descriptor = ReferenceSchema.For(nameof(CAuthPacket));

        Assert.Equal(bytes, descriptor.Parser.ParseFrom(bytes).ToByteArray());
    }

    /// <summary>
    /// An empty byte array is a present field of length zero, not an absent one, and the
    /// server sends exactly this on every duplicate-session rejection.
    /// </summary>
    /// <remarks>
    /// No member of any contract is a <c>ReadOnlyMemory&lt;byte&gt;</c> any more, so the case
    /// that a rule keyed on C# nullability would have missed - a value type, never null,
    /// written even where nothing was assigned - is not reachable from the protocol as it
    /// stands. <c>WireSchema</c> still recognises the type, so reintroducing one is covered;
    /// what is pinned here is the byte-level property the keyword exists for, on a member the
    /// server empties in production.
    /// </remarks>
    [Fact]
    public void Carry_An_Empty_Byte_Array_As_A_Present_Field()
    {
        byte[] bytes = WireCorpus.Serialize(
            new SWorldSelectPacket { WorldKey = Array.Empty<byte>(), Result = WorldSelectResult.Success });

        Assert.Equal(new byte[] { 0x0a, 0x00 }, bytes);

        MessageDescriptor descriptor = ReferenceSchema.For(nameof(SWorldSelectPacket));

        Assert.Equal(bytes, descriptor.Parser.ParseFrom(bytes).ToByteArray());
    }

    /// <summary>
    /// A <c>DateTime</c>'s kind does not travel. protobuf-net writes bcl.DateTime's value and
    /// scale and never its kind field, so a UTC timestamp arrives indistinguishable from an
    /// unspecified one and comes back from the server's own deserializer as Unspecified.
    /// Both chat timestamps are affected, and a client has to be told out of band which zone
    /// they are in.
    /// </summary>
    [Fact]
    public void Not_Carry_The_Kind_Of_A_DateTime()
    {
        var sent = new Avalon.Network.Packets.Social.SChatMessagePacket
        {
            DateTime = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
        };

        byte[] bytes = WireCorpus.Serialize(sent);

        // Field 5, a submessage of {value = 496956 zigzagged, scale = HOURS}. Field 3 of
        // bcl.DateTime is the kind and it is not there.
        Assert.Equal(new byte[] { 0x2a, 0x06, 0x08, 0xf8, 0xd4, 0x3c, 0x10, 0x01 }, bytes);

        var read = (Avalon.Network.Packets.Social.SChatMessagePacket)WireCorpus.Deserialize(
            typeof(Avalon.Network.Packets.Social.SChatMessagePacket),
            bytes);

        Assert.Equal(sent.DateTime.Ticks, read.DateTime.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, read.DateTime.Kind);
    }

    /// <summary>
    /// A submessage member that the contract initializes cannot stay absent through a round
    /// trip: deserializing bytes that never carried it still leaves the member set, and
    /// serializing again writes an empty submessage. The server's own encode-decode-encode is
    /// therefore not idempotent, with no reader from the schema involved at all.
    /// </summary>
    [Fact]
    public void Turn_An_Absent_Submessage_Into_An_Empty_One_On_A_Return_Trip()
    {
        Assert.Empty(WireCorpus.Serialize(
            new Avalon.Network.Packets.World.PortalPlacementDto { WorldPos = null! }));

        object read = WireCorpus.Deserialize(typeof(Avalon.Network.Packets.World.PortalPlacementDto), []);

        // Field 2 is WorldPos, now present and carrying nothing.
        Assert.Equal(new byte[] { 0x12, 0x00 }, WireCorpus.Serialize(read));
    }

    /// <summary>
    /// Negative zero is not transmissible, in either direction and by both encoders, because
    /// IEEE says it equals zero and proto3 omits a float field that equals its default. This
    /// is agreement rather than divergence - neither side can send it, so neither side can
    /// disagree about it - but it is the one float value the wire cannot carry.
    /// </summary>
    [Fact]
    public void Not_Transmit_Negative_Zero()
    {
        Assert.Empty(WireCorpus.Serialize(new Avalon.Network.Packets.World.Vector3Dto { X = -0.0f }));

        MessageDescriptor descriptor = ReferenceSchema.For("Vector3Dto");
        IMessage vector = descriptor.Parser.ParseFrom([]);

        descriptor.FindFieldByNumber(1)!.Accessor.SetValue(vector, -0.0f);

        Assert.Empty(vector.ToByteArray());
    }

    /// <summary>
    /// Every other float value does survive, bit for bit, in both directions. The round-trip
    /// here goes through bytes rather than through protobuf's text format, which is what makes
    /// that true: a text comparison is where NaN payloads and denormals are lost.
    /// </summary>
    [Fact]
    public void Carry_Every_Other_Float_Bit_Pattern_Unchanged()
    {
        MessageDescriptor descriptor = ReferenceSchema.For("Vector3Dto");

        foreach (float special in WireFixtures.FloatEdgeCases().Where(value => value != 0.0f))
        {
            var written = new Avalon.Network.Packets.World.Vector3Dto { X = special };
            byte[] bytes = WireCorpus.Serialize(written);
            byte[] reEncoded = descriptor.Parser.ParseFrom(bytes).ToByteArray();

            Assert.Equal(bytes, reEncoded);

            var read = (Avalon.Network.Packets.World.Vector3Dto)WireCorpus.Deserialize(
                typeof(Avalon.Network.Packets.World.Vector3Dto),
                reEncoded);

            Assert.Equal(
                BitConverter.SingleToInt32Bits(special),
                BitConverter.SingleToInt32Bits(read.X));
        }
    }
}
