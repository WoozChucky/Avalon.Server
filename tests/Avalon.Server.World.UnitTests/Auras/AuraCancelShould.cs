using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>CMSG_AURA_CANCEL: the owner's helpful auras only, the copy it names, exactly one answer.</summary>
public class AuraCancelShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    private static async Task<(MapInstance Instance, AuraCancelHandler Handler, MapInstanceClient Player)> SetUpAsync()
    {
        IWorld world = NewWorld(await TestStaticData.LoadAsync(TestStaticData.Repositories(
            auras: () => [AuraTestData.Fortified(), AuraTestData.Bleed(), AuraTestData.Independent(), Blessing()])));
        MapInstance instance = TestMapInstances.Build(world);
        world.InstanceRegistry.GetInstanceById(instance.InstanceId).Returns(instance);
        MapInstanceClient player = Join(instance, 916_101);
        player.Character.Health = 500;
        player.Character.CurrentHealth = 500;
        return (instance, new AuraCancelHandler(world, NullLogger<AuraCancelHandler>.Instance), player);
    }

    /// <summary>A helpful aura each caster keeps its own copy of, with no periodic effect.</summary>
    private static AuraTemplate Blessing()
    {
        AuraTemplate template = AuraTestData.Independent(907);
        template.Name = "Blessing";
        template.Kind = AuraKind.Helpful;
        template.PeriodicKind = AuraPeriodicKind.None;
        template.PeriodicBase = 0f;
        template.TickIntervalMs = 0;
        return template;
    }

    /// <summary>The player holding two copies of the Blessing, its own and one from a second player.</summary>
    private static (ActiveAura Own, ActiveAura Other) TwoBlessings(MapInstance instance, MapInstanceClient player)
    {
        MapInstanceClient friend = Join(instance, 916_102);
        instance.Auras.Apply(player.Character, player.Character, new AuraId(907), AuraSource.None);
        instance.Auras.Apply(friend.Character, player.Character, new AuraId(907), AuraSource.None);
        Assert.Equal(2, player.Character.Auras.Count);
        return (player.Character.Auras.All[0], player.Character.Auras.All[1]);
    }

    private static SAuraCancelResultPacket Reply(MapInstanceClient client) =>
        Assert.Single(client.Read<SAuraCancelResultPacket>(NetworkPacketType.SMSG_AURA_CANCEL_RESULT));

    private static AuraCancelResult Answer(MapInstanceClient client) => Reply(client).Result;

    [Fact]
    public async Task End_an_own_helpful_aura()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        instance.Auras.Apply(player.Character, player.Character, new AuraId(905), AuraSource.None);

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 905 });

        Assert.Equal(AuraCancelResult.Ok, Answer(player));
        Assert.Equal(0, player.Character.Auras.Count);
    }

    [Fact]
    public async Task Echo_the_aura_it_named()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        instance.Auras.Apply(player.Character, player.Character, new AuraId(905), AuraSource.None);

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 905 });

        Assert.Equal(905u, Reply(player).AuraId);
    }

    [Fact]
    public async Task Refuse_to_end_a_harmful_aura()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        instance.Auras.Apply(null, player.Character, new AuraId(901), AuraSource.None);

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 901 });

        Assert.Equal(AuraCancelResult.NotCancellable, Answer(player));
        Assert.Equal(1, player.Character.Auras.Count);
    }

    [Fact]
    public async Task Refuse_to_end_a_harmful_copy_named_by_its_key()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        instance.Auras.Apply(null, player.Character, new AuraId(901), AuraSource.None);
        uint key = player.Character.Auras.All[0].Key;

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 901, InstanceKey = key });

        Assert.Equal(AuraCancelResult.NotCancellable, Answer(player));
        Assert.Equal(1, player.Character.Auras.Count);
    }

    [Fact]
    public async Task Answer_NotFound_for_an_aura_not_held()
    {
        (_, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 905 });

        Assert.Equal(AuraCancelResult.NotFound, Answer(player));
    }

    [Fact]
    public async Task Answer_Dead_first_and_end_nothing()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        instance.Auras.Apply(player.Character, player.Character, new AuraId(905), AuraSource.None);
        player.Character.IsDead = true;

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 905 });

        Assert.Equal(AuraCancelResult.Dead, Answer(player));
        Assert.Equal(1, player.Character.Auras.Count);
    }

    [Fact]
    public async Task Answer_Dead_even_for_an_aura_not_held()
    {
        (_, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        player.Character.IsDead = true;

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 905 });

        Assert.Equal(AuraCancelResult.Dead, Answer(player));
    }

    [Fact]
    public async Task End_only_the_copy_its_key_names()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        (ActiveAura own, ActiveAura other) = TwoBlessings(instance, player);

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 907, InstanceKey = other.Key });

        Assert.Equal(AuraCancelResult.Ok, Answer(player));
        Assert.Same(own, Assert.Single(player.Character.Auras.All));
    }

    [Fact]
    public async Task End_every_copy_when_it_names_no_key()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        TwoBlessings(instance, player);

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 907 });

        Assert.Equal(AuraCancelResult.Ok, Answer(player));
        Assert.Equal(0, player.Character.Auras.Count);
    }

    [Fact]
    public async Task Answer_NotFound_for_a_key_no_copy_holds_and_end_nothing()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        (ActiveAura own, ActiveAura other) = TwoBlessings(instance, player);
        uint unused = Math.Max(own.Key, other.Key) + 1;

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 907, InstanceKey = unused });

        Assert.Equal(AuraCancelResult.NotFound, Answer(player));
        Assert.Equal(2, player.Character.Auras.Count);
    }

    [Fact]
    public async Task Match_a_key_of_0_literally()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        TwoBlessings(instance, player);

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 907, InstanceKey = 0 });

        // Keys start at 1, so 0 names no copy: nothing ends, rather than every copy.
        Assert.Equal(AuraCancelResult.NotFound, Answer(player));
        Assert.Equal(2, player.Character.Auras.Count);
    }

    [Fact]
    public async Task Answer_NotFound_for_a_key_another_aura_holds()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        instance.Auras.Apply(player.Character, player.Character, new AuraId(905), AuraSource.None);
        uint fortifiedKey = player.Character.Auras.All[0].Key;

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 907, InstanceKey = fortifiedKey });

        Assert.Equal(AuraCancelResult.NotFound, Answer(player));
        Assert.Equal(1, player.Character.Auras.Count);
    }

    [Fact]
    public async Task Send_the_answer_ahead_of_the_ticks_aura_update()
    {
        (MapInstance instance, AuraCancelHandler handler, MapInstanceClient player) = await SetUpAsync();
        instance.Auras.Apply(player.Character, player.Character, new AuraId(905), AuraSource.None);
        instance.Update(Tick);
        player.Sent.Clear();

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 905 });
        instance.Update(Tick);

        List<NetworkPacketType> order = player.Sent.Select(p => p.Header.Type)
            .Where(t => t is NetworkPacketType.SMSG_AURA_CANCEL_RESULT or NetworkPacketType.SMSG_AURA_UPDATE).ToList();
        Assert.Equal([NetworkPacketType.SMSG_AURA_CANCEL_RESULT, NetworkPacketType.SMSG_AURA_UPDATE], order);
    }

    [Fact]
    public async Task Answer_nothing_to_a_connection_with_no_character()
    {
        (_, AuraCancelHandler handler, _) = await SetUpAsync();
        IWorldConnection connection = Substitute.For<IWorldConnection>();

        handler.Execute(connection, new CAuraCancelPacket { AuraId = 905 });

        connection.DidNotReceiveWithAnyArgs().Send(default!);
    }

    [Fact]
    public async Task Answer_NotFound_when_the_cancel_throws()
    {
        (MapInstance instance, _, MapInstanceClient player) = await SetUpAsync();
        IWorld throwing = NewWorld();
        throwing.InstanceRegistry.GetInstanceById(instance.InstanceId)
            .Returns<IMapInstance?>(_ => throw new InvalidOperationException("boom"));
        var handler = new AuraCancelHandler(throwing, NullLogger<AuraCancelHandler>.Instance);

        handler.Execute(player.Connection, new CAuraCancelPacket { AuraId = 905 });

        Assert.Equal(AuraCancelResult.NotFound, Answer(player));
    }
}
