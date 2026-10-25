using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// #506 slice C: the character sheet and the heal event round-trip through ProtoBuf, on the opcodes and
/// field numbers the client reads.
/// </summary>
public class CharacterSheetAndHealPacketsShould
{
    private static T Read<T>(OutboundPacket packet)
    {
        using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
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
            Stamina = 22,
            Strength = 23,
            Agility = 20,
            Intellect = 21,
            Armor = 8,
            CritPct = 5.5f,
            EvasionPct = 30f,
            BlockPct = 2.25f,
            WeaponMin = 4,
            WeaponMax = 9,
            MovementSpeed = 4.4f,
            Evasion = 150,
            MaxWard = 12,
            FireResistance = 75f,
            ColdResistance = 10f,
            LightningResistance = 0.5f,
            BlightResistance = -30f,
            CritMultiplier = 1.75f,
            AttackSpeedPct = 2f,
            CastSpeedPct = 5f,
            CooldownRecoveryPct = 10f,
            AttackTimeMs = 792,
            IncreasedPct = [0f, 0f, 12f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 101.5f],
            Resonance = [new() { ThemeId = 1, Count = 2 }, new() { ThemeId = 6, Count = 1 }],
        };

        OutboundPacket packet = SCharacterStatsPacket.Create(sheet, PacketEncoder.Shared);
        SCharacterStatsPacket read = Read<SCharacterStatsPacket>(packet);

        Assert.Equal(NetworkPacketType.SMSG_CHARACTER_STATS, packet.Header.Type);
        Assert.Equal((22u, 23u, 20u, 21u, 8u), (read.Stamina, read.Strength, read.Agility, read.Intellect, read.Armor));
        Assert.Equal((5.5f, 2.25f, 4u, 9u), (read.CritPct, read.BlockPct, read.WeaponMin, read.WeaponMax));
        // #627: movement speed, on field 14.
        Assert.Equal(4.4f, read.MovementSpeed);
        // #1178: evasion, its chance, the ward's maximum and the four resistances, on fields 15 to 21.
        Assert.Equal((150u, 30f, 12u), (read.Evasion, read.EvasionPct, read.MaxWard));
        Assert.Equal((75f, 10f, 0.5f, -30f),
            (read.FireResistance, read.ColdResistance, read.LightningResistance, read.BlightResistance));
        // #1178: the crit multiplier, the three effective speeds, the attack time and the offence block, on 22 to 27.
        Assert.Equal((1.75f, 2f, 5f, 10f, 792u),
            (read.CritMultiplier, read.AttackSpeedPct, read.CastSpeedPct, read.CooldownRecoveryPct, read.AttackTimeMs));
        Assert.Equal(sheet.IncreasedPct, read.IncreasedPct);
        // #1180: each Resonance theme's id and count, on 28, in the order sent.
        Assert.Equal([(1u, 2u), (6u, 1u)], read.Resonance!.Select(r => (r.ThemeId, r.Count)));
    }

    [Theory]
    [InlineData(HitResult.None, null)]
    [InlineData(HitResult.Crit, 232u)]
    public void Round_trip_the_heal(HitResult result, uint? abilityId)
    {
        OutboundPacket packet = SUnitHealedPacket.Create(5, 9, 40, 65, abilityId, result, PacketEncoder.Shared);
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

        // #1178: field 26 varint 792 (a tag above 15 takes two bytes), field 27 one fixed32 per element, in index order.
        using var offence = new MemoryStream();
        Serializer.Serialize(offence, new SCharacterStatsPacket { AttackTimeMs = 792, IncreasedPct = [1f, 0f] });
        Assert.Equal("D0019806DD010000803FDD0100000000", Convert.ToHexString(offence.ToArray()));

        // #1180: field 28 length-delimited (a two-byte tag), holding field 1 varint 1 (the theme) and field 2 varint 3.
        using var resonance = new MemoryStream();
        Serializer.Serialize(resonance, new SCharacterStatsPacket { Resonance = [new() { ThemeId = 1, Count = 3 }] });
        Assert.Equal("E2010408011003", Convert.ToHexString(resonance.ToArray()));

        using var heal = new MemoryStream();
        Serializer.Serialize(heal, new SUnitHealedPacket { AbilityId = 232, Result = HitResult.Crit });
        Assert.Equal("28E8013001", Convert.ToHexString(heal.ToArray()));   // field 5 varint 232, field 6 varint 1
    }
}
