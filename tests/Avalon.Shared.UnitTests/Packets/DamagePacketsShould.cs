using System;
using System.IO;
using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// #506: both damage packets say how the hit went. HitResult is flags, numbered append-only; a payload
/// from before the field decodes as None, which is what an old server sent for every hit.
/// </summary>
public class DamagePacketsShould
{
    private static byte[] Plain(ReadOnlySpan<byte> bytes) => bytes.ToArray();

    private static T Read<T>(NetworkPacket packet)
    {
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<T>(stream);
    }

    [Theory]
    [InlineData(HitResult.None, 0)]
    [InlineData(HitResult.Crit, 1)]
    [InlineData(HitResult.Dodged, 2)]
    [InlineData(HitResult.Blocked, 4)]
    public void Number_every_hit_result_append_only(HitResult result, int value) => Assert.Equal(value, (int)result);

    [Theory]
    [InlineData(HitResult.Crit)]
    [InlineData(HitResult.Dodged)]
    [InlineData(HitResult.Crit | HitResult.Blocked)]
    public void Carry_the_hit_result_on_the_unit_damage_packet(HitResult result)
    {
        SUnitDamagePacket read = Read<SUnitDamagePacket>(
            SUnitDamagePacket.Create(new ObjectGuid(ObjectType.Creature, 5), 9, 40, 12, Plain, result));

        Assert.Equal((40u, 12u, result), (read.CurrentHealth, read.Damage, read.Result));
    }

    [Theory]
    [InlineData(HitResult.Crit)]
    [InlineData(HitResult.Dodged)]
    [InlineData(HitResult.Crit | HitResult.Blocked)]
    public void Carry_the_hit_result_on_the_character_damage_packet(HitResult result)
    {
        SCharacterDamagePacket read = Read<SCharacterDamagePacket>(
            SCharacterDamagePacket.Create(5, 9, 40, 12, 200, Plain, result));

        Assert.Equal((40u, 12u, (uint?)200u, result), (read.CurrentHealth, read.Damage, read.AbilityId, read.Result));
    }

    [Fact]
    public void Keep_the_field_numbers_the_client_reads()
    {
        using var unit = new MemoryStream();
        Serializer.Serialize(unit, new SUnitDamagePacket { Result = HitResult.Crit | HitResult.Blocked });
        Assert.Equal("2805", Convert.ToHexString(unit.ToArray()));   // field 5, varint 5

        using var character = new MemoryStream();
        Serializer.Serialize(character, new SCharacterDamagePacket { Result = HitResult.Dodged });
        Assert.Equal("3002", Convert.ToHexString(character.ToArray()));   // field 6, varint 2
    }

    [Fact]
    public void Decode_a_payload_without_the_field_as_none()
    {
        // Damage 12 alone, as a server before #506 sent it.
        using var unit = new MemoryStream(Convert.FromHexString("200C"));
        Assert.Equal(HitResult.None, Serializer.Deserialize<SUnitDamagePacket>(unit).Result);

        using var character = new MemoryStream(Convert.FromHexString("200C"));
        Assert.Equal(HitResult.None, Serializer.Deserialize<SCharacterDamagePacket>(character).Result);
    }
}
