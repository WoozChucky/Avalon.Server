using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>
/// A unit's auras reach every client that sees it: a list when it comes into view (and for a client's own character on
/// every entry), then each tick's changes, once, to the unit and its watchers. Interest decides, as for the unit itself.
/// </summary>
public class AuraReplicationShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    private async Task<IWorld> WorldAsync() => NewWorld(await TestStaticData.LoadAsync(TestStaticData.Repositories(
        auras: () => [AuraTestData.Bleed(), AuraTestData.Fortified(), AuraTestData.Renew()])));

    private static MapInstanceClient Player(MapInstance instance, uint id, float z)
    {
        MapInstanceClient client = Join(instance, Inventory.TestCharacters.New(id));
        client.Character.Health = 500;
        client.Character.CurrentHealth = 500;
        client.Character.Position = new Vector3(0f, 0f, z);
        return client;
    }

    private static Creature Boar(MapInstance instance, uint id)
    {
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id), Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Vector3(0f, 0f, 3f), Level = 1, Health = 100, CurrentHealth = 100,
        };
        instance.AddCreature(boar);
        return boar;
    }

    [Fact]
    public async Task List_a_units_auras_when_it_comes_into_view_and_the_own_character_always()
    {
        using MapInstance instance = TestMapInstances.Build(await WorldAsync(), time: _clock);
        MapInstanceClient watcher = Player(instance, 915_101, 0f);
        Creature boar = Boar(instance, 915_901);
        instance.Auras.Apply(watcher.Character, boar, new AuraId(901), AuraSource.None);

        instance.Update(Tick);

        List<SAuraListPacket> lists = watcher.Read<SAuraListPacket>(NetworkPacketType.SMSG_AURA_LIST);
        SAuraListPacket ofBoar = Assert.Single(lists, l => l.UnitGuid == boar.Guid.RawValue);
        AuraEntryDto bleed = Assert.Single(ofBoar.Entries);
        Assert.Equal((901u, 1u, watcher.Character.Guid.RawValue, 1u, 12000u), (bleed.AuraId, bleed.InstanceKey, bleed.CasterGuid,
            bleed.Stacks, bleed.DurationMs));
        Assert.Empty(Assert.Single(lists, l => l.UnitGuid == watcher.Character.Guid.RawValue).Entries);
        Assert.Empty(watcher.Read<SAuraUpdatePacket>(NetworkPacketType.SMSG_AURA_UPDATE));   // the list carried it
    }

    [Fact]
    public async Task Send_each_change_once_to_the_unit_and_every_watcher_in_view()
    {
        using MapInstance instance = TestMapInstances.Build(await WorldAsync(), time: _clock);
        MapInstanceClient healer = Player(instance, 915_102, 0f);
        MapInstanceClient near = Player(instance, 915_103, 5f);
        MapInstanceClient far = Player(instance, 915_104, 200f);
        instance.Update(Tick);

        instance.Auras.Apply(healer.Character, near.Character, new AuraId(905), AuraSource.None);
        instance.Update(Tick);
        instance.Update(Tick);

        foreach (MapInstanceClient told in new[] { healer, near })
        {
            SAuraUpdatePacket update = Assert.Single(told.Read<SAuraUpdatePacket>(NetworkPacketType.SMSG_AURA_UPDATE));
            Assert.Equal(near.Character.Guid.RawValue, update.UnitGuid);
            Assert.Equal(AuraUpdateAction.Applied, Assert.Single(update.Entries).Action);
        }

        Assert.Empty(far.Read<SAuraUpdatePacket>(NetworkPacketType.SMSG_AURA_UPDATE));
        Assert.False(near.Character.Auras.HasChanges);
    }

    [Fact]
    public async Task Tell_watchers_when_an_aura_ends()
    {
        using MapInstance instance = TestMapInstances.Build(await WorldAsync(), time: _clock);
        MapInstanceClient player = Player(instance, 915_105, 0f);
        instance.Update(Tick);
        instance.Auras.Apply(player.Character, player.Character, new AuraId(904), AuraSource.None);
        instance.Update(Tick);

        _clock.Advance(TimeSpan.FromSeconds(13));
        instance.Update(Tick);

        SAuraUpdatePacket last = player.Read<SAuraUpdatePacket>(NetworkPacketType.SMSG_AURA_UPDATE)[^1];
        AuraEntryDto removed = Assert.Single(last.Entries);
        Assert.Equal((AuraUpdateAction.Removed, 0u), (removed.Action, removed.RemainingMs));
    }

    /// <summary>
    /// A unit nobody sees keeps no changes for later, and a client that then sees it is listed what it holds, never told
    /// of changes it was not there for.
    /// </summary>
    [Fact]
    public async Task Forget_the_changes_of_a_unit_nobody_sees_and_list_it_when_it_comes_into_view()
    {
        using MapInstance instance = TestMapInstances.Build(await WorldAsync(), time: _clock);
        MapInstanceClient watcher = Player(instance, 915_107, 200f);
        Creature boar = Boar(instance, 915_903);
        instance.Update(Tick);

        instance.Auras.Apply(watcher.Character, boar, new AuraId(901), AuraSource.None);
        instance.Update(Tick);

        Assert.False(boar.Auras.HasChanges);
        Assert.DoesNotContain(watcher.Read<SAuraUpdatePacket>(NetworkPacketType.SMSG_AURA_UPDATE),
            u => u.UnitGuid == boar.Guid.RawValue);

        watcher.Character.Position = new Vector3(0f, 0f, 0f);
        instance.Update(Tick);

        SAuraListPacket ofBoar = Assert.Single(watcher.Read<SAuraListPacket>(NetworkPacketType.SMSG_AURA_LIST),
            l => l.UnitGuid == boar.Guid.RawValue);
        Assert.Equal(AuraUpdateAction.Unknown, Assert.Single(ofBoar.Entries).Action);
        Assert.DoesNotContain(watcher.Read<SAuraUpdatePacket>(NetworkPacketType.SMSG_AURA_UPDATE),
            u => u.UnitGuid == boar.Guid.RawValue);
    }

    /// <summary>
    /// A character takes a portal with an aura on it. The aura keeps its time across the move, ticks in
    /// the new instance, and that instance lists it to the character itself.
    /// </summary>
    [Fact]
    public async Task Carry_an_aura_into_a_new_instance_and_list_it_there()
    {
        IWorld world = await WorldAsync();
        using MapInstance first = TestMapInstances.Build(world, time: _clock);
        using MapInstance second = TestMapInstances.Build(world, time: _clock);
        MapInstanceClient traveller = Player(first, 915_106, 0f);
        Creature boar = Boar(first, 915_902);
        first.Auras.Apply(boar, traveller.Character, new AuraId(901), AuraSource.None);
        first.Update(Tick);

        _clock.Advance(TimeSpan.FromSeconds(1));
        first.RemoveCharacter(traveller.Connection);
        traveller.Character.InstanceId = second.InstanceId;
        second.AddCharacter(traveller.Connection);
        second.Update(Tick);

        SAuraListPacket own = traveller.Read<SAuraListPacket>(NetworkPacketType.SMSG_AURA_LIST)[^1];
        Assert.Equal(traveller.Character.Guid.RawValue, own.UnitGuid);
        Assert.Equal(11000u, Assert.Single(own.Entries).RemainingMs);

        _clock.Advance(TimeSpan.FromSeconds(2));
        second.Update(Tick);
        Assert.Equal(497u, traveller.Character.CurrentHealth);   // its first tick, at 3 s, in the new instance
    }
}
