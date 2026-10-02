using Avalon.Common;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Xunit;

namespace Avalon.Server.World.UnitTests.Parties;

public class PartyHealthScalingShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(16);

    private static Creature Boar(uint id) => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, id),
        Metadata = Loot.LootTestData.BoarTemplate(null),   // a template with an id: the state writer sends it
        BaseMaxHealth = 100, Health = 100, CurrentHealth = 50,
    };

    [Fact]
    public void Scale_once_per_tick_however_many_arrive_and_say_so_once()
    {
        MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), ownerPartyId: new PartyId(1));
        Creature boar = Boar(900_001);
        instance.AddCreature(boar);

        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        MapInstanceClient b = MapInstanceClients.Join(instance, 2);
        MapInstanceClient c = MapInstanceClients.Join(instance, 3);
        instance.Update(Tick);

        Assert.Equal(220u, boar.Health);
        Assert.Equal(110u, boar.CurrentHealth);
        Assert.Equal(["Creatures now have 220% health (3 players)."], SystemLines(a));
        Assert.Equal(SystemLines(a), SystemLines(c));
    }

    [Fact]
    public void Name_the_one_who_left_and_scale_back_down()
    {
        MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), ownerPartyId: new PartyId(1));
        Creature boar = Boar(900_002);
        instance.AddCreature(boar);
        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        MapInstanceClient b = MapInstanceClients.Join(instance, 2);
        instance.Update(Tick);
        a.Sent.Clear();

        instance.RemoveCharacter(b.Connection);
        instance.Update(Tick);

        Assert.Equal(100u, boar.Health);
        Assert.Equal(["Tester2 has left. Creatures now have 100% health (1 player)."], SystemLines(a));
    }

    [Fact]
    public void Start_a_later_spawn_at_the_current_factor()
    {
        MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), ownerPartyId: new PartyId(1));
        MapInstanceClients.Join(instance, 1);
        MapInstanceClients.Join(instance, 2);
        instance.Update(Tick);

        Creature late = Boar(900_003);
        late.CurrentHealth = 100;
        instance.AddCreature(late);

        Assert.Equal(160u, late.Health);
        Assert.Equal(160u, late.CurrentHealth);
    }

    [Fact]
    public void Never_scale_a_solo_or_town_instance()
    {
        MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld());
        Creature boar = Boar(900_004);
        instance.AddCreature(boar);
        MapInstanceClients.Join(instance, 1);
        MapInstanceClients.Join(instance, 2);

        instance.Update(Tick);

        Assert.Equal(100u, boar.Health);
    }

    [Fact]
    public void Send_the_new_maximum_to_a_client_already_watching_the_creature()
    {
        using MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), ownerPartyId: new PartyId(1));
        MapInstanceClient watcher = MapInstanceClients.Join(instance, 1);
        Creature boar = Boar(900_005);
        instance.AddCreature(boar);
        Ticks(instance, 7);   // the add, and a broadcast past it
        Assert.Single(watcher.Added(), s => s.Guid == boar.Guid.RawValue);
        watcher.Sent.Clear();

        MapInstanceClients.Join(instance, 2);
        Ticks(instance, 7);   // the rescale, and at least one broadcast

        ObjectState update = watcher.StateUpdates().First(s => s.Guid == boar.Guid.RawValue);
        Assert.Equal(160u, update.Health);
        Assert.Equal(80u, update.CurrentHealth);
    }

    /// <summary>The maximum rides only on the update that changed it: routine updates pay nothing for it.</summary>
    [Fact]
    public void Leave_the_maximum_out_of_a_routine_update()
    {
        using MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld());
        MapInstanceClient watcher = MapInstanceClients.Join(instance, 1);
        Creature boar = Boar(900_006);
        instance.AddCreature(boar);
        Ticks(instance, 7);
        watcher.Sent.Clear();

        boar.CurrentHealth = 40;
        Ticks(instance, 7);

        ObjectState update = watcher.StateUpdates().First(s => s.Guid == boar.Guid.RawValue);
        Assert.Equal(40u, update.CurrentHealth);
        Assert.Null(update.Health);
    }

    private static void Ticks(MapInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
        {
            instance.Update(TimeSpan.FromSeconds(1d / 60d));
        }
    }

    private static List<string> SystemLines(MapInstanceClient client) => client
        .Read<Avalon.Network.Packets.Social.SChatMessagePacket>(Avalon.Network.Packets.Abstractions.NetworkPacketType.SMSG_CHAT_MESSAGE)
        .Where(m => m.Channel == Avalon.Network.Packets.Social.ChatChannel.System)
        .Select(m => m.Message)
        .ToList();
}
