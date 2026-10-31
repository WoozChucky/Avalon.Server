using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.State;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

public class CharacterDeltaReplicationShould
{
    [Fact]
    public void Acknowledge_idle_inputs_without_broadcasting_them_and_deliver_movement_and_the_stop()
    {
        IWorld world = NewWorld();
        IMapNavigator navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(c => c.ArgAt<Vector3>(1));
        using MapInstance instance = TestMapInstances.Build(world, navigator: navigator);
        world.InstanceRegistry.GetInstanceById(instance.InstanceId).Returns(instance);
        var clock = new FakeTimeProvider();
        var handler = new PlayerInputHandler(NullLogger<PlayerInputHandler>.Instance, world, clock,
            Options.Create(new GameConfiguration()));
        MapInstanceClient player = Join(instance, 910_101);
        MapInstanceClient watcher = Join(instance, 910_102);
        Broadcast(instance);
        ClearPackets(player);
        ClearPackets(watcher);

        for (uint seq = 1; seq <= 14; seq++)
        {
            handler.Execute(player.Connection, new CPlayerInputPacket { Seq = seq });
            instance.Update(TimeSpan.FromSeconds(1d / 60d));
            clock.Advance(TimeSpan.FromSeconds(1d / 60d));
        }
        Assert.Equal(14u, player.Connection.LastInputSeq);
        Assert.Equal(Enumerable.Range(1, 14).Select(n => (uint)n),
            player.Read<SPlayerStateAckPacket>(NetworkPacketType.SMSG_PLAYER_STATE_ACK).Select(a => a.Seq));
        Assert.Empty(watcher.StateUpdates());
        Assert.Empty(player.StateUpdates());

        handler.Execute(player.Connection, new CPlayerInputPacket { Seq = 15, DirX = 1, YawDeg = 90 });
        Broadcast(instance);
        ObjectState moving = Assert.Single(watcher.StateUpdates());
        Assert.True(moving.Position!.X > 0);
        Assert.True(moving.Velocity!.X > 0);
        Assert.Equal(90f, moving.Orientation);
        Assert.Null(moving.Health);
        Assert.Null(moving.CurrentHealth);
        Assert.Null(moving.Level);
        Assert.Empty(player.StateUpdates()); // the authoritative motion ACK already went to its owner
        ClearPackets(watcher);

        // Stopping changes velocity alone: zero must be present, not omitted. Repeated idle inputs
        // before the next cadence must retain that dirty mark, and later cadences send nothing.
        for (uint seq = 16; seq <= 22; seq++)
            handler.Execute(player.Connection, new CPlayerInputPacket { Seq = seq, YawDeg = 90 });
        Broadcast(instance);
        ObjectState stopped = Assert.Single(watcher.StateUpdates());
        Assert.Equal(0f, stopped.Velocity!.X);
        Assert.Null(stopped.Position);
        Assert.Null(stopped.Orientation);
        ClearPackets(watcher);
        handler.Execute(player.Connection, new CPlayerInputPacket { Seq = 23, YawDeg = 90 });
        Broadcast(instance);
        Assert.Empty(watcher.StateUpdates());

        // Exact equality: a small correction must not disappear under Vector3's epsilon operator.
        Vector3 corrected = player.Character.Position + new Vector3(0.000001f, 0, 0);
        player.Character.Position = corrected;
        Broadcast(instance);
        Assert.Equal(corrected.x, Assert.Single(watcher.StateUpdates()).Position!.X);
        ClearPackets(watcher);
        player.Character.Orientation = new Vector3(0, 180, 0);
        Broadcast(instance);
        ObjectState turned = Assert.Single(watcher.StateUpdates());
        Assert.Equal(180f, turned.Orientation);
        Assert.Null(turned.Position);
        Assert.Null(turned.Velocity);
        ClearPackets(player);
        ClearPackets(watcher);
    }

    [Fact]
    public void Deliver_resource_changes_without_requiring_a_power_type_change()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient player = Join(instance, 910_111);
        MapInstanceClient watcher = Join(instance, 910_112);
        player.Character.PowerType = PowerType.Mana;
        player.Character.Power = 100;
        player.Character.CurrentPower = 50;
        Broadcast(instance);
        ClearPackets(player);
        ClearPackets(watcher);

        player.Character.CurrentPower = 0;
        Broadcast(instance);
        foreach (MapInstanceClient recipient in new[] { player, watcher })
        {
            ObjectState spent = Assert.Single(recipient.StateUpdates());
            Assert.Equal(0u, spent.CurrentPower);
            Assert.Null(spent.PowerType);
            Assert.Null(spent.Power);
            Assert.Null(spent.Position);
            ClearPackets(recipient);
        }
        player.Character.Power = 150;
        Broadcast(instance);
        ObjectState resized = Assert.Single(watcher.StateUpdates());
        Assert.Equal(150u, resized.Power);
        Assert.Null(resized.CurrentPower);
        Assert.Null(resized.PowerType);
        ClearPackets(player);
        ClearPackets(watcher);
    }

    [Fact]
    public void Deliver_gameplay_changes_to_stationary_characters_and_full_state_to_new_watchers()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient player = Join(instance, 910_121);
        MapInstanceClient watcher = Join(instance, 910_122);
        CharacterEntity character = player.Character;
        character.Health = 100;
        character.CurrentHealth = 100;
        Broadcast(instance);
        ClearPackets(player);
        ClearPackets(watcher);

        character.OnPeriodicHit(100);
        Broadcast(instance);
        foreach (MapInstanceClient recipient in new[] { player, watcher })
        {
            ObjectState dead = Assert.Single(recipient.StateUpdates());
            Assert.Equal(0u, dead.CurrentHealth);
            Assert.True(dead.IsDead);
            Assert.Null(dead.Position);
            Assert.Null(dead.Health);
            ClearPackets(recipient);
        }
        character.Revive();
        character.Health = 150;
        character.Level = 2;
        character.Data!.PvpEnabled = true;
        character.MarkPvpChanged();
        Broadcast(instance);
        foreach (MapInstanceClient recipient in new[] { player, watcher })
        {
            ObjectState revived = Assert.Single(recipient.StateUpdates());
            Assert.False(revived.IsDead);
            Assert.Equal(100u, revived.CurrentHealth);
            Assert.Equal(150u, revived.Health);
            Assert.Equal((ushort)2, revived.Level);
            Assert.True(revived.PvpEnabled);
            Assert.Equal(character.Name, revived.Name);
            Assert.Equal(character.Class, revived.Class);
            Assert.Equal(character.Gender, revived.Gender);
            ClearPackets(recipient);
        }
        character.Data.PvpEnabled = false;
        character.MarkPvpChanged();
        Broadcast(instance);
        Assert.Null(Assert.Single(watcher.StateUpdates()).PvpEnabled);

        MapInstanceClient newcomer = Join(instance, 910_123);
        Broadcast(instance);
        ObjectState added = Assert.Single(newcomer.Added(), s => s.Guid == character.Guid.RawValue);
        Assert.NotNull(added.Position);
        Assert.NotNull(added.Velocity);
        Assert.Equal(150u, added.Health);
        Assert.Equal(100u, added.CurrentHealth);
        ClearPackets(player);
        ClearPackets(watcher);
        ClearPackets(newcomer);
    }

    /// <summary>
    /// #993: a character's experience and the experience its next level needs go to its own client alone, on its add and
    /// on every update that changes them, and never to anyone who sees it. Two watchers, so the entry they share is
    /// encoded for the first and copied for the second (#980). Experience alone leaves a watcher nothing, so it is sent no
    /// update at all; a level-up still tells it the level.
    /// </summary>
    [Theory]
    [InlineData("add")]
    [InlineData("experience")]
    [InlineData("level-up")]
    public void Send_a_characters_experience_to_its_own_client_alone(string change)
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient player = Join(instance, 910_131);
        MapInstanceClient[] watchers = [Join(instance, 910_132), Join(instance, 910_133)];
        CharacterEntity character = player.Character;
        character.Level = 3;
        character.Experience = 120;
        character.RequiredExperience = 400;
        if (change != "add")
        {
            Broadcast(instance);
            ClearPackets(player);
            foreach (MapInstanceClient watcher in watchers)
                ClearPackets(watcher);
        }

        if (change == "experience")
        {
            character.Experience = 180;
        }
        else if (change == "level-up")
        {
            character.Level = 4;
            character.Experience = 30;
            character.RequiredExperience = 900;
        }

        Broadcast(instance);
        ulong raw = character.Guid.RawValue;
        Func<MapInstanceClient, List<ObjectState>> sent = change == "add" ? c => c.Added() : c => c.StateUpdates();
        ObjectState own = Assert.Single(sent(player), s => s.Guid == raw);
        Assert.Equal(character.Experience, own.Experience);
        // Experience alone leaves the requirement unchanged, so unmarked and not sent.
        Assert.Equal(change == "experience" ? (ulong?)null : character.RequiredExperience, own.RequiredExperience);
        foreach (MapInstanceClient watcher in watchers)
        {
            if (change == "experience")
            {
                Assert.Empty(watcher.Read<SInstanceStateUpdatePacket>(NetworkPacketType.SMSG_WORLD_STATE_UPDATE));
                continue;
            }

            ObjectState seen = Assert.Single(sent(watcher), s => s.Guid == raw);
            Assert.Equal((ushort?)character.Level, seen.Level);
            Assert.Null(seen.Experience);
            Assert.Null(seen.RequiredExperience);
        }

        ClearPackets(player);
        foreach (MapInstanceClient watcher in watchers)
            ClearPackets(watcher);
    }

    private static void ClearPackets(MapInstanceClient client)
    {
        foreach (OutboundPacket packet in client.Sent)
            packet.Release();
        client.Sent.Clear();
    }

    private static void Broadcast(MapInstance instance)
    {
        for (int i = 0; i < 7; i++)
            instance.Update(TimeSpan.FromSeconds(1d / 60d));
    }
}
