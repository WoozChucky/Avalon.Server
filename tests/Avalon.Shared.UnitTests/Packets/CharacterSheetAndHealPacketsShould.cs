using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// #506 slice C: the character sheet and the heal event round-trip through ProtoBuf, on the opcodes and
/// field numbers the client reads.
/// </summary>
public class CharacterSheetAndHealPacketsShould
{
    private static byte[] Plain(ReadOnlySpan<byte> bytes) => bytes.ToArray();

    private static T Read<T>(NetworkPacket packet)
    {
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<T>(stream);
    }

    [Fact]
    public void Use_the_next_free_opcodes()
    {
        Assert.Equal(0x302A, (int)NetworkPacketType.SMSG_CHARACTER_STATS);
        Assert.Equal(0x310C, (int)NetworkPacketType.SMSG_UNIT_HEALED);
    }

    [Fact]
    public void Round_trip_the_character_sheet()
    {
        var sheet = new SCharacterStatsPacket
        {
            Stamina = 22, Strength = 23, Agility = 20, Intellect = 21, Armor = 8, AttackDamage = 50, AbilityDamage = 12,
            CritPct = 5.5f, DodgePct = 30f, BlockPct = 2.25f, WeaponMin = 4, WeaponMax = 9,
        };

        NetworkPacket packet = SCharacterStatsPacket.Create(sheet, Plain);
        SCharacterStatsPacket read = Read<SCharacterStatsPacket>(packet);

        Assert.Equal(NetworkPacketType.SMSG_CHARACTER_STATS, packet.Header.Type);
        Assert.Equal((22u, 23u, 20u, 21u, 8u, 50u, 12u), (read.Stamina, read.Strength, read.Agility, read.Intellect,
            read.Armor, read.AttackDamage, read.AbilityDamage));
        Assert.Equal((5.5f, 30f, 2.25f, 4u, 9u), (read.CritPct, read.DodgePct, read.BlockPct, read.WeaponMin, read.WeaponMax));
    }

    /// <summary>#627: haste and movement speed on the next free field numbers, 13 and 14.</summary>
    [Fact]
    public void Round_trip_the_haste_and_the_movement_speed()
    {
        var sheet = new SCharacterStatsPacket { Strength = 23, HastePct = 3f, MovementSpeed = 4.4f };

        SCharacterStatsPacket read = Read<SCharacterStatsPacket>(SCharacterStatsPacket.Create(sheet, Plain));

        Assert.Equal((23u, 3f, 4.4f), (read.Strength, read.HastePct, read.MovementSpeed));
    }

    /// <summary>The sheet as it was before #627, fields 1 to 12 only.</summary>
    [ProtoContract]
    public sealed class SheetBeforeSpeeds
    {
        [ProtoMember(2)] public uint Strength { get; set; }
        [ProtoMember(12)] public uint WeaponMax { get; set; }
    }

    [Fact]
    public void Read_a_sheet_from_before_the_speeds_as_no_haste_and_no_speed()
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, new SheetBeforeSpeeds { Strength = 23, WeaponMax = 9 });
        stream.Position = 0;

        SCharacterStatsPacket read = Serializer.Deserialize<SCharacterStatsPacket>(stream);

        Assert.Equal((23u, 9u, 0f, 0f), (read.Strength, read.WeaponMax, read.HastePct, read.MovementSpeed));
    }

    [Theory]
    [InlineData(HitResult.None, null)]
    [InlineData(HitResult.Crit, 232u)]
    public void Round_trip_the_heal(HitResult result, uint? abilityId)
    {
        NetworkPacket packet = SUnitHealedPacket.Create(5, 9, 40, 65, abilityId, result, Plain);
        SUnitHealedPacket read = Read<SUnitHealedPacket>(packet);

        Assert.Equal(NetworkPacketType.SMSG_UNIT_HEALED, packet.Header.Type);
        Assert.Equal((5ul, 9ul, 40u, 65u, abilityId, result),
            (read.Healer, read.Target, read.Amount, read.CurrentHealth, read.AbilityId, read.Result));
    }

    [Fact]
    public void Keep_the_field_numbers_the_client_reads()
    {
        using var sheet = new MemoryStream();
        Serializer.Serialize(sheet, new SCharacterStatsPacket { CritPct = 1f, WeaponMax = 7 });
        Assert.Equal("450000803F6007", Convert.ToHexString(sheet.ToArray()));   // field 8 fixed32 1.0, field 12 varint 7

        using var heal = new MemoryStream();
        Serializer.Serialize(heal, new SUnitHealedPacket { AbilityId = 232, Result = HitResult.Crit });
        Assert.Equal("28E8013001", Convert.ToHexString(heal.ToArray()));   // field 5 varint 232, field 6 varint 1
    }
}
