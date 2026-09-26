using System.IO;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using NSubstitute;
using ProtoBuf;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>Casting through a real MapInstance. Character ids are unique to this class (164_1xx).</summary>
public class MapInstanceAbilityCastShould
{
    /// <summary>#521 item 9: other clients learn which ability a unit is casting, not just for how long.</summary>
    [Fact]
    public void Name_the_ability_on_the_start_cast_broadcast()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient caster = Join(instance, 164_101);
        MapInstanceClient watcher = Join(instance, 164_102);
        var ability = Substitute.For<IAbility>();
        ability.AbilityId.Returns(new AbilityId(211));
        ability.Metadata.Returns(new AbilityMetadata { Name = "Flame Burst", ScriptName = "x", CastTime = 0.6f });

        instance.BroadcastUnitStartCast(caster.Character, ability);

        foreach (MapInstanceClient client in new[] { caster, watcher })
        {
            SUnitStartCastPacket start = Assert.Single(client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
            Assert.Equal(caster.Character.Guid.RawValue, start.Caster);
            Assert.Equal(211u, start.AbilityId);
            Assert.Equal(0.6f, start.CastTime);
        }
    }

    [Fact]
    public void Carry_the_ability_id_through_a_protobuf_round_trip()
    {
        var original = new SUnitStartCastPacket { Caster = 42UL, CastTime = 1.25f, AbilityId = 232 };
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, original);
        stream.Position = 0;

        SUnitStartCastPacket decoded = Serializer.Deserialize<SUnitStartCastPacket>(stream);

        Assert.Equal(232u, decoded.AbilityId);
        Assert.Equal(42UL, decoded.Caster);
        Assert.Equal(1.25f, decoded.CastTime);
    }
}
