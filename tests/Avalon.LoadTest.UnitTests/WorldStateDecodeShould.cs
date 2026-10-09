using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.LoadTest.Wire;
using Avalon.LoadTest.World;
using Avalon.Network.Packets.State;
using Org.BouncyCastle.Crypto;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class WorldStateDecodeShould
{
    [Fact]
    public void Track_the_nearest_live_creature_through_packets_the_server_builds()
    {
        ulong nearCreature = new ObjectGuid(ObjectType.Creature, 11).RawValue;
        ulong farCreature = new ObjectGuid(ObjectType.Creature, 12).RawValue;
        ulong otherPlayer = new ObjectGuid(ObjectType.Character, 7).RawValue;
        AsymmetricCipherKeyPair clientKeys = AsymmetricCipher.GenerateECDHKeyPair();
        var client = new AvalonCryptoSession(CryptoRole.Client, clientKeys);
        var server = new AvalonCryptoSession(CryptoRole.Server);
        server.Initialize(AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeys)));
        client.Initialize(server.GetPublicKey());
        var codec = new PacketCodec(client);
        var table = new WorldStateTable();

        // An add carries everything (GameEntityFields.All): two live creatures and a character nearer than both.
        table.Apply(SInstanceStateAddPacket.Create(
        [
            Unit(nearCreature, 5, 0, 5, health: 50, dead: false),
            Unit(farCreature, 20, 0, 0, health: 80, dead: false),
            Unit(otherPlayer, 1, 0, 1, health: 120, dead: false),
        ], server.Encryptor), codec);

        Assert.Equal(3, table.Count);
        Assert.True(table.TryNearestLiveCreature(0, 0, 60, out TrackedObject target));
        Assert.Equal(new TrackedObject(nearCreature, 2, 5, 0, 5, false, 50), target);

        // An update carries only what changed, and an absent member keeps what the table knew: the far creature walks
        // up saying nothing of its health or death, the near one dies where it stands.
        table.Apply(SInstanceStateUpdatePacket.Create(
        [
            new ObjectState { Guid = farCreature, Position = new Vec3 { X = 3, Y = 1, Z = 4 } },
            new ObjectState { Guid = nearCreature, CurrentHealth = 0, IsDead = true },
        ], server.Encryptor), codec);

        Assert.True(table.TryGet(nearCreature, out TrackedObject corpse));
        Assert.Equal(new TrackedObject(nearCreature, 2, 5, 0, 5, true, 0), corpse);
        Assert.True(table.TryNearestLiveCreature(0, 0, 60, out target));
        Assert.Equal(new TrackedObject(farCreature, 2, 3, 1, 4, false, 80), target);
        Assert.False(table.TryNearestLiveCreature(0, 0, 4.9f, out _));

        // Gone from view: only the corpse and the character are left, and neither is a target.
        table.Apply(SInstanceStateRemovePacket.Create([new ObjectGuid(farCreature)], server.Encryptor), codec);

        Assert.Equal(2, table.Count);
        Assert.False(table.TryNearestLiveCreature(0, 0, 60, out _));
    }

    private static ObjectState Unit(ulong guid, float x, float y, float z, uint health, bool dead) => new()
    {
        Guid = guid,
        Position = new Vec3 { X = x, Y = y, Z = z },
        Velocity = new Vec3(),
        Orientation = 0,
        MoveState = MoveState.Idle,
        Health = health,
        CurrentHealth = health,
        Level = 1,
        IsDead = dead,
    };
}
