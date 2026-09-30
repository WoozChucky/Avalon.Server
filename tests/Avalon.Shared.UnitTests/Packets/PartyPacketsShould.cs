using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.State;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// The party packets (2026-09-30). The client decodes by field number, so each field is pinned alone
/// with its bytes: (number &lt;&lt; 3 | wire type), then the value; every pinned value is non-zero.
/// A non-null string is always written, an empty one as a field of length zero (schema/avalon.proto
/// marks every singular string optional for that reason), so the name fields' empty bytes appear
/// wherever another field is pinned.
/// </summary>
public class PartyPacketsShould
{
    /// <summary>Field 1, InviterName, empty.</summary>
    private const string EmptyInviterName = "0a00";

    /// <summary>Field 2, Name, empty.</summary>
    private const string EmptyMemberName = "1200";

    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    [Fact]
    public void Use_the_next_free_server_opcodes()
    {
        Assert.Equal(0x30B0, (short)NetworkPacketType.SMSG_PARTY_INVITE);
        Assert.Equal(0x30B1, (short)NetworkPacketType.SMSG_PARTY_RESULT);
        Assert.Equal(0x30B2, (short)NetworkPacketType.SMSG_PARTY_ROSTER);
        Assert.Equal(0x30B3, (short)NetworkPacketType.SMSG_PARTY_MEMBER_STATUS);
        Assert.Equal(NetworkPacketType.SMSG_PARTY_INVITE, SPartyInvitePacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_PARTY_RESULT, SPartyResultPacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_PARTY_ROSTER, SPartyRosterPacket.PacketType);
        Assert.Equal(NetworkPacketType.SMSG_PARTY_MEMBER_STATUS, SPartyMemberStatusPacket.PacketType);
    }

    [Fact]
    public void Keep_the_invite_field_numbers()
    {
        Assert.Equal("0a0141", Hex(new SPartyInvitePacket { InviterName = "A" }));
        Assert.Equal(EmptyInviterName + "1002", Hex(new SPartyInvitePacket { InviterClass = 2 }));
        Assert.Equal(EmptyInviterName + "1803", Hex(new SPartyInvitePacket { InviterLevel = 3 }));
        Assert.Equal(EmptyInviterName + "2004", Hex(new SPartyInvitePacket { ExpiresInMs = 4 }));
    }

    [Fact]
    public void Keep_the_result_field_numbers()
    {
        Assert.Equal("0801", Hex(new SPartyResultPacket { Result = PartyResult.Ok }));
        Assert.Equal("120141", Hex(new SPartyResultPacket { Name = "A" }));
    }

    [Fact]
    public void Keep_the_roster_field_numbers()
    {
        Assert.Equal("0801", Hex(new SPartyRosterPacket { PartyId = 1 }));
        Assert.Equal("1002", Hex(new SPartyRosterPacket { ExperienceMode = PartyExperienceMode.LevelWeighted }));
        Assert.Equal("1803", Hex(new SPartyRosterPacket { ModeLockedForMs = 3 }));
        Assert.Equal("2204" + "0805" + EmptyMemberName, Hex(new SPartyRosterPacket { Members = [new PartyMemberDto { CharacterId = 5 }] }));
    }

    [Fact]
    public void Keep_the_roster_member_field_numbers()
    {
        Assert.Equal("0801" + EmptyMemberName, Hex(new PartyMemberDto { CharacterId = 1 }));
        Assert.Equal("120141", Hex(new PartyMemberDto { Name = "A" }));
        Assert.Equal(EmptyMemberName + "1803", Hex(new PartyMemberDto { Class = 3 }));
        Assert.Equal(EmptyMemberName + "2004", Hex(new PartyMemberDto { Level = 4 }));
        Assert.Equal(EmptyMemberName + "2801", Hex(new PartyMemberDto { IsLeader = true }));
        Assert.Equal(EmptyMemberName + "3001", Hex(new PartyMemberDto { Online = true }));
        Assert.Equal(EmptyMemberName + "3801", Hex(new PartyMemberDto { SameInstance = true }));
    }

    [Fact]
    public void Keep_the_member_status_field_numbers()
    {
        Assert.Equal("0801", Hex(new SPartyMemberStatusPacket { CharacterId = 1 }));
        Assert.Equal("1002", Hex(new SPartyMemberStatusPacket { Health = 2 }));
        Assert.Equal("1803", Hex(new SPartyMemberStatusPacket { MaxHealth = 3 }));
        Assert.Equal("2004", Hex(new SPartyMemberStatusPacket { Power = 4 }));
        Assert.Equal("2805", Hex(new SPartyMemberStatusPacket { MaxPower = 5 }));
        Assert.Equal("3002", Hex(new SPartyMemberStatusPacket { PowerType = PowerType.Fury }));
        Assert.Equal("3801", Hex(new SPartyMemberStatusPacket { IsDead = true }));
    }

    [Fact]
    public void Keep_the_enum_values()
    {
        Assert.Equal(0, (int)PartyResult.Unknown);
        Assert.Equal(1, (int)PartyResult.Ok);
        Assert.Equal(13, (int)PartyResult.InviteDeclined);
        Assert.Equal(14, (int)PartyResult.Invalid);
        Assert.Equal(15, (int)PartyResult.Error);
        Assert.Equal(0, (int)PartyExperienceMode.Unknown);
        Assert.Equal(1, (int)PartyExperienceMode.Even);
        Assert.Equal(2, (int)PartyExperienceMode.LevelWeighted);
    }

    [Fact]
    public void Use_the_next_free_client_opcodes()
    {
        Assert.Equal(0x20B0, (short)NetworkPacketType.CMSG_PARTY_INVITE);
        Assert.Equal(0x20B1, (short)NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE);
        Assert.Equal(0x20B2, (short)NetworkPacketType.CMSG_PARTY_LEAVE);
        Assert.Equal(0x20B3, (short)NetworkPacketType.CMSG_PARTY_KICK);
        Assert.Equal(0x20B4, (short)NetworkPacketType.CMSG_PARTY_PROMOTE);
        Assert.Equal(0x20B5, (short)NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE);
    }

    [Fact]
    public void Keep_the_client_field_numbers()
    {
        Assert.Equal("0a0141", Hex(new CPartyInvitePacket { TargetName = "A" }));
        Assert.Equal("0801", Hex(new CPartyInviteResponsePacket { Accept = true }));
        Assert.Equal("0805", Hex(new CPartyKickPacket { CharacterId = 5 }));
        Assert.Equal("0805", Hex(new CPartyPromotePacket { CharacterId = 5 }));
        Assert.Equal("0802", Hex(new CPartyExperienceModePacket { Mode = PartyExperienceMode.LevelWeighted }));
    }
}
