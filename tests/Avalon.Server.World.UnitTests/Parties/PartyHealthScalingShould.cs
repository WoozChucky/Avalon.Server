using Avalon.Common;
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

    private static List<string> SystemLines(MapInstanceClient client) => client
        .Read<Avalon.Network.Packets.Social.SChatMessagePacket>(Avalon.Network.Packets.Abstractions.NetworkPacketType.SMSG_CHAT_MESSAGE)
        .Where(m => m.Channel == Avalon.Network.Packets.Social.ChatChannel.System)
        .Select(m => m.Message)
        .ToList();
}
