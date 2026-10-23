using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// Ability amounts for tooltips (#669). The client decodes by field number and enum value, so each is pinned
/// with its bytes: (number &lt;&lt; 3 | wire type), then the value.
/// </summary>
public class AbilityAmountPacketsShould
{
    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    [Fact]
    public void Use_its_own_opcode()
    {
        Assert.Equal(0x302C, (short)NetworkPacketType.SMSG_CHARACTER_ABILITY_AMOUNTS);
        Assert.Equal(NetworkPacketType.SMSG_CHARACTER_ABILITY_AMOUNTS, SCharacterAbilityAmountsPacket.PacketType);
        Assert.Equal(NetworkPacketFlags.Encrypted, SCharacterAbilityAmountsPacket.Flags);
    }

    [Fact]
    public void Number_the_kinds_append_only()
    {
        Assert.Equal(0, (int)AbilityAmountKind.None);
        Assert.Equal(1, (int)AbilityAmountKind.Damage);
        Assert.Equal(2, (int)AbilityAmountKind.Healing);
    }

    [Fact]
    public void Carry_the_amount_on_ability_info_in_fields_18_to_20()
    {
        // A field above 15 takes a two-byte tag: (18 << 3) is 144, 0x90 0x01.
        Assert.Equal("900101", Hex(new AbilityInfo { AmountKind = AbilityAmountKind.Damage }));
        Assert.Equal("980101", Hex(new AbilityInfo { AmountMin = 1 }));
        Assert.Equal("a00101", Hex(new AbilityInfo { AmountMax = 1 }));
    }

    /// <summary>#1178: the tags, the amount's damage types and the effective pace the server computed, on 21 to 24.</summary>
    [Fact]
    public void Carry_the_tags_types_and_pace_on_ability_info_in_fields_21_to_24()
    {
        Assert.Equal("a80101", Hex(new AbilityInfo { Tags = AbilityTags.Attack }));
        Assert.Equal("b00101", Hex(new AbilityInfo { AmountTypes = DamageTypeMask.Physical }));
        Assert.Equal("b801a006", Hex(new AbilityInfo { CooldownMs = 800 }));
        Assert.Equal("c00101", Hex(new AbilityInfo { CastTimeMs = 1 }));
    }

    [Fact]
    public void Carry_each_update_entrys_fields_in_order()
    {
        Assert.Equal("0801", Hex(new AbilityAmountInfo { AbilityId = 1 }));
        Assert.Equal("1002", Hex(new AbilityAmountInfo { Kind = AbilityAmountKind.Healing }));
        Assert.Equal("182c", Hex(new AbilityAmountInfo { Min = 44 }));
        Assert.Equal("2030", Hex(new AbilityAmountInfo { Max = 48 }));
        Assert.Equal("2804", Hex(new AbilityAmountInfo { Types = DamageTypeMask.Cold }));
        Assert.Equal("30a006", Hex(new AbilityAmountInfo { CooldownMs = 800 }));
        Assert.Equal("3801", Hex(new AbilityAmountInfo { CastTimeMs = 1 }));
        Assert.Equal("0a020801", Hex(new SCharacterAbilityAmountsPacket { Amounts = [new AbilityAmountInfo { AbilityId = 1 }] }));
    }
}
