using System;
using System.IO;
using Avalon.Network.Packets.Combat;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// Auras on the wire. Each field is pinned alone with its bytes: (number &lt;&lt; 3 | wire type), then the value. Zero
/// writes nothing, so every pinned value is non-zero.
/// </summary>
public class AuraPacketsShould
{
    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    /// <summary>A tick names its aura on the damage and heal packets it reuses; absent on every other hit.</summary>
    [Fact]
    public void Name_the_aura_on_a_ticks_damage_and_heal()
    {
        Assert.Equal("3005", Hex(new SUnitDamagePacket { AuraId = 5 }));
        Assert.Equal("3805", Hex(new SCharacterDamagePacket { AuraId = 5 }));
        Assert.Equal("3805", Hex(new SUnitHealedPacket { AuraId = 5 }));
        Assert.Equal("", Hex(new SUnitDamagePacket()));
    }
}
