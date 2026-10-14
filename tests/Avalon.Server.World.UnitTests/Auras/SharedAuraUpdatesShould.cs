using Avalon.Combat;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Auras;
using Avalon.World.Entities;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>#980: a unit's aura update, encoded once per broadcast pass and shared by every recipient.</summary>
public class SharedAuraUpdatesShould
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every recipient gets a reference of its own to one payload, and the pass gives up its own at its end: the payload
    /// goes back to its pool once the last recipient's connection has let go of it, never before. A missing share would
    /// return it while a connection still reads it; a missing release would keep it out of the pool for good.
    /// </summary>
    [Fact]
    public void Share_one_payload_and_give_it_back_after_the_pass_and_the_last_recipient()
    {
        var encoder = new PacketEncoder(new PayloadSegmentPool(countOutstanding: true));
        var updates = new SharedAuraUpdates(new AuraWire(), encoder);
        var boar = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 980_960) };
        AuraTemplate bleed = AuraTestData.Bleed();
        boar.Auras.Add(new ActiveAura(bleed, new ObjectGuid(ObjectType.Character, 7), AuraSource.None, 1,
            new AuraSnapshot(5f, 0f, 1), AuraSchedule.Start(s_t0, bleed.DurationMs, bleed.TickIntervalMs),
            bleed.DurationMs, s_t0.UtcDateTime), s_t0);

        OutboundPacket[] sent = [.. Enumerable.Range(0, 3).Select(_ => updates.For(boar, boar.Auras))];

        Assert.All(sent, packet => Assert.Same(sent[0].Payload, packet.Payload));
        Assert.Equal(1, encoder.Pool.Outstanding);
        updates.ReleaseAll();
        sent[0].Release();
        sent[1].Release();
        Assert.Equal(1, encoder.Pool.Outstanding);   // the last recipient still holds it
        sent[2].Release();
        Assert.Equal(0, encoder.Pool.Outstanding);
    }
}
