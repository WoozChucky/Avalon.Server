using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Parties;

/// <summary>A PartyService over a manual clock, with characters brought online through connections that record what they are sent.</summary>
internal sealed class PartyTestWorld
{
    public PartyTestWorld(Action<GameConfiguration>? configure = null, ILogger<PartyService>? logger = null)
    {
        configure?.Invoke(Config);
        Parties = new PartyService(Options.Create(Config), Clock, logger ?? NullLogger<PartyService>.Instance);
        Parties.AttachInstances(Instances);
    }

    public ManualTimerClock Clock { get; } = new();
    public GameConfiguration Config { get; } = new();
    public FakePartyInstances Instances { get; } = new();
    public PartyService Parties { get; }

    public PartyClient Online(uint id, string? name = null, ushort level = 1, Guid? instance = null)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Name = name ?? $"Tester{id}";
        character.Level = level;
        character.InstanceId = instance ?? Guid.Empty;

        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.AccountId.Returns(new AccountId(id));
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));

        Parties.CharacterOnline(connection);
        return new PartyClient(connection, character, sent);
    }

    /// <summary>Forms a party led by <paramref name="leader" /> with each of <paramref name="others" /> in turn, and clears what they were sent.</summary>
    public void Form(PartyClient leader, params PartyClient[] others)
    {
        foreach (PartyClient other in others)
        {
            Assert.Equal(PartyResult.Ok, Parties.Invite(leader.Id, other.Character.Name));
            Assert.Equal(PartyResult.Ok, Parties.Respond(other.Id, accept: true));
        }

        leader.Clear();
        foreach (PartyClient other in others)
            other.Clear();
    }
}

internal sealed record PartyClient(IWorldConnection Connection, CharacterEntity Character, List<NetworkPacket> Sent)
{
    public uint Id => Character.Guid.Id;

    public List<T> Read<T>(NetworkPacketType type) => Sent
        .Where(p => p.Header.Type == type)
        .Select(p =>
        {
            using var stream = new MemoryStream(p.Payload);
            return Serializer.Deserialize<T>(stream);
        })
        .ToList();

    public List<SPartyRosterPacket> Rosters() => Read<SPartyRosterPacket>(NetworkPacketType.SMSG_PARTY_ROSTER);
    public List<SPartyResultPacket> Results() => Read<SPartyResultPacket>(NetworkPacketType.SMSG_PARTY_RESULT);
    public List<SPartyInvitePacket> Invites() => Read<SPartyInvitePacket>(NetworkPacketType.SMSG_PARTY_INVITE);
    public List<SPartyMemberStatusPacket> Statuses() => Read<SPartyMemberStatusPacket>(NetworkPacketType.SMSG_PARTY_MEMBER_STATUS);

    public List<string> Lines() => Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)
        .Where(m => m.Channel == ChatChannel.System)
        .Select(m => m.Message)
        .ToList();

    public void Clear() => Sent.Clear();
}

/// <summary>A party index the test fills: which instance belongs to which party, and which parties were forgotten.</summary>
internal sealed class FakePartyInstances : IPartyInstanceRegistry
{
    public Dictionary<Guid, PartyId> Owned { get; } = [];
    public List<PartyId> Forgotten { get; } = [];

    public bool IsPartyInstance(PartyId party, Guid instanceId) =>
        Owned.TryGetValue(instanceId, out PartyId? owner) && owner.Equals(party);

    public void ForgetParty(PartyId party) => Forgotten.Add(party);

    public Task<IMapInstance> GetOrCreatePartyInstanceAsync(PartyId party, MapTemplateId templateId) =>
        throw new NotSupportedException();
}
