using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Parties;

/// <summary>
/// #1199 (owner decisions): each party member is sent where the members of its party in its own instance stand that its
/// world-state view does not hold, about once a second, never a member it holds, never one elsewhere, never to anyone
/// outside the party. A member leaves the list on the tick its world-state add is sent and joins it on the tick its remove
/// is sent, so the client always has one of the two and never both.
/// </summary>
/// <remarks>
/// Real instances over a real <see cref="PartyService" />, ticked as WorldServer does: the instance's update (its views
/// and world state), then the positions flush, then the clock moves on 1/60 s. The views are decided every sixth tick
/// (#981), and a list is due once a second after the last: on ticks 1, 62 and 123 for a member out of view from the start.
/// The viewer V stands at the origin; 200 m is out of view and 5 m in it.
/// </remarks>
public class PartyPositionsShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);
    private const int LastTick = 130;
    private const int ChangeAt = 30;
    private const int LeaveAt = 62;

    public enum Case
    {
        FarAway,
        InView,
        NonMemberFarAway,
        InAnotherInstance,
        WalksIntoView,
        WalksOutOfView,
        ViewerLeavesTheParty,
        MemberLeavesTheInstance,
    }

    /// <summary>
    /// What V is told of member M, tick by tick: M's world-state add and remove, and every positions list V is sent with
    /// who is in it (M at its X,Z, or a non-member N). Nobody outside the party is ever sent a list, and after V leaves,
    /// neither V nor anyone left in the party is sent one that lists the other.
    /// </summary>
    [Theory]
    [InlineData(Case.FarAway, "1 [M@0,200], 62 [M@0,200], 123 [M@0,200]")]
    [InlineData(Case.InView, "1 add")]
    [InlineData(Case.NonMemberFarAway, "1 add")]
    [InlineData(Case.InAnotherInstance, "")]
    [InlineData(Case.WalksIntoView, "1 [M@0,200], 31 add, 62 []")]
    [InlineData(Case.WalksOutOfView, "1 add, 31 remove, 31 [M@0,200], 92 [M@0,200]")]
    [InlineData(Case.ViewerLeavesTheParty, "1 [M@0,200]")]
    [InlineData(Case.MemberLeavesTheInstance, "1 [M@0,200], 62 []")]
    public void Send_each_member_its_far_members_once_a_second_handing_over_with_the_world_state(Case @case, string expected)
    {
        var clock = new ManualTimerClock();
        var parties = new PartyService(Options.Create(new Avalon.World.Configuration.GameConfiguration()), clock,
            NullLogger<PartyService>.Instance);
        IWorld world = NewWorld();
        using MapInstance here = TestMapInstances.Build(world, time: clock, parties: parties);
        using MapInstance elsewhere = TestMapInstances.Build(world, time: clock, parties: parties);

        MapInstanceClient v = Player(parties, here, 1_199_001, 0f);
        float mAt = @case is Case.InView or Case.NonMemberFarAway or Case.WalksOutOfView ? 5f : 200f;
        MapInstanceClient m = Player(parties, @case == Case.InAnotherInstance ? elsewhere : here, 1_199_002, mAt);
        MapInstanceClient n = Player(parties, here, 1_199_003, 200f);   // never in the party
        MapInstanceClient w = Player(parties, here, 1_199_004, 5f);     // a third member, by V, so V can leave
        Form(parties, v, m, w);

        var told = new List<string>();
        var leftOver = new List<string>();
        for (int tick = 1; tick <= LastTick; tick++)
        {
            if (tick == ChangeAt && @case == Case.WalksIntoView)
                m.Character.Position = new Vector3(0f, 0f, 5f);
            if (tick == ChangeAt && @case == Case.WalksOutOfView)
                m.Character.Position = new Vector3(0f, 0f, 200f);
            if (tick == LeaveAt && @case == Case.ViewerLeavesTheParty)
                Assert.Equal(PartyResult.Ok, parties.Leave(v.Id()));
            if (tick == LeaveAt && @case == Case.MemberLeavesTheInstance)
                Move(parties, m, here, elsewhere);

            int vFrom = v.Sent.Count, mFrom = m.Sent.Count, wFrom = w.Sent.Count;
            here.Update(s_tick);
            elsewhere.Update(s_tick);
            parties.FlushPositions();
            clock.Advance(s_tick);

            told.AddRange(ToldOf(v.Sent.Skip(vFrom), m, n).Select(entry => $"{tick} {entry}"));
            if (@case == Case.ViewerLeavesTheParty && tick >= LeaveAt)
            {
                leftOver.AddRange(Lists(m.Sent.Skip(mFrom)).Concat(Lists(w.Sent.Skip(wFrom)))
                    .Where(list => list.Any(p => p.CharacterId == v.Id())).Select(_ => $"{tick} V listed"));
            }
        }

        Assert.Equal(expected, string.Join(", ", told));
        Assert.Empty(Lists(n.Sent));
        Assert.Empty(leftOver);
    }

    private static MapInstanceClient Player(PartyService parties, MapInstance instance, uint id, float z)
    {
        MapInstanceClient client = Join(instance, Inventory.TestCharacters.New(id));
        client.Character.Position = new Vector3(0f, 0f, z);
        parties.CharacterOnline(client.Connection);
        return client;
    }

    private static void Form(PartyService parties, MapInstanceClient leader, params MapInstanceClient[] others)
    {
        foreach (MapInstanceClient other in others)
        {
            Assert.Equal(PartyResult.Ok, parties.Invite(leader.Id(), other.Character.Name));
            Assert.Equal(PartyResult.Ok, parties.Respond(other.Id(), accept: true));
        }
    }

    /// <summary>As World.TransferPlayer moves a character: out of one instance, into the other, and the party told.</summary>
    private static void Move(PartyService parties, MapInstanceClient client, MapInstance from, MapInstance to)
    {
        from.RemoveCharacter(client.Connection);
        client.Character.InstanceId = to.InstanceId;
        to.AddCharacter(client.Connection);
        parties.InstanceChanged(client.Connection);
    }

    /// <summary>What these packets told V of M (its add and remove) and every positions list, naming M and N in it.</summary>
    private static IEnumerable<string> ToldOf(IEnumerable<OutboundPacket> packets, MapInstanceClient m, MapInstanceClient n)
    {
        ulong guid = m.Character.Guid.RawValue;
        foreach (OutboundPacket packet in packets)
        {
            switch (packet.Header.Type)
            {
                case NetworkPacketType.SMSG_WORLD_STATE_ADD:
                    if (Decode<SInstanceStateAddPacket>(packet).Adds?.Any(s => s.Guid == guid) == true)
                        yield return "add";
                    break;
                case NetworkPacketType.SMSG_WORLD_STATE_REMOVE:
                    if (Decode<SInstanceStateRemovePacket>(packet).Removes?.Contains(guid) == true)
                        yield return "remove";
                    break;
                case NetworkPacketType.SMSG_PARTY_POSITIONS:
                    IEnumerable<string> names = (Decode<SPartyPositionsPacket>(packet).Positions ?? []).Select(p =>
                        p.CharacterId == m.Id() ? $"M@{p.X:0},{p.Z:0}" : p.CharacterId == n.Id() ? "N" : $"#{p.CharacterId}");
                    yield return $"[{string.Join(" ", names)}]";
                    break;
            }
        }
    }

    private static IEnumerable<List<PartyMemberPositionDto>> Lists(IEnumerable<OutboundPacket> packets) => packets
        .Where(p => p.Header.Type == NetworkPacketType.SMSG_PARTY_POSITIONS)
        .Select(p => Decode<SPartyPositionsPacket>(p).Positions ?? []);

    private static T Decode<T>(OutboundPacket packet)
    {
        using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
        return Serializer.Deserialize<T>(stream);
    }
}

internal static class PartyPositionsClientExtensions
{
    public static uint Id(this MapInstanceClient client) => client.Character.Guid.Id;
}
