using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Vendor;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Quests;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Vendors;

/// <summary>
/// A town instance with shoppers standing at the origin and three NPCs:
/// <list type="bullet">
/// <item>the Smith (template 12, 3 m away), who sells VendorTestData's rows;</item>
/// <item>the Pedlar (template 13, 3 m the other way), who sells one Tonic row;</item>
/// <item>the Innkeeper (template 3, 2 m away), who only talks.</item>
/// </list>
/// Each vendor's root offers "Show me your wares." (OpenShop, back to the root) and "Farewell.".
/// The Smith also offers an OpenShop option that ends the conversation. The instance keeps real
/// VendorStocks behind a substituted IVendorHost. A second instance, <see cref="PlainInstanceId" />,
/// holds the same NPCs but keeps no stock. The main shopper is character 7, with 1000 copper.
/// </summary>
internal sealed class VendorWorld
{
    public static readonly ObjectGuid SmithGuid = new(ObjectType.Creature, 92);
    public static readonly ObjectGuid PedlarGuid = new(ObjectType.Creature, 93);
    public static readonly ObjectGuid InnkeeperGuid = new(ObjectType.Creature, 94);
    public static readonly CreatureTemplateId InnkeeperTemplate = new(3);
    public static readonly Guid InstanceId = new("43200000-0000-0000-0000-000000000432");

    /// <summary>An instance holding the same NPCs that is not an IVendorHost, so it keeps no stock.</summary>
    public static readonly Guid PlainInstanceId = new("43200000-0000-0000-0000-000000000433");

    public const int SmithRoot = 1;
    public const int PedlarRoot = 2;
    public const int InnkeeperRoot = 3;
    public const int SmithWares = 1;
    public const int SmithFarewell = 2;
    public const int PedlarWares = 3;
    public const int PedlarFarewell = 4;
    public const int InnkeeperFarewell = 5;

    /// <summary>An OpenShop option that also ends the conversation, so it opens nothing.</summary>
    public const int SmithWaresAndLeave = 6;

    private readonly List<Shopper> _shoppers = [];

    public sealed class Shopper
    {
        public required CharacterEntity Character { get; init; }

        public required IWorldConnection Connection { get; init; }

        public List<NetworkPacket> Sent { get; } = [];

        public List<T> Read<T>(NetworkPacketType type) => TestTown.Read<T>(Sent, type);

        public List<SVendorListPacket> Lists() => Read<SVendorListPacket>(NetworkPacketType.SMSG_VENDOR_LIST);

        public List<SDialogueEndPacket> Ends() => Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END);

        public List<SVendorResultPacket> Results() => Read<SVendorResultPacket>(NetworkPacketType.SMSG_VENDOR_RESULT);

        public List<SInventoryUpdatePacket> Updates() => Read<SInventoryUpdatePacket>(NetworkPacketType.SMSG_INVENTORY_UPDATE);
    }

    public IWorld World { get; } = Substitute.For<IWorld>();

    public StaticData Data { get; }

    public VendorStocks Stocks { get; } = new();

    public FixedTimeProvider Clock { get; } = new(new DateTimeOffset(VendorTestData.Now));

    public IQuestProgress Quests { get; set; } = NoQuestProgress.Instance;

    /// <summary>The real economy over this world; a test may swap in a substitute.</summary>
    public ICharacterEconomy Economy { get; set; } = null!;

    public Dictionary<ObjectGuid, ICreature> Creatures { get; } = [];

    public ICreature Smith { get; }

    public ICreature Pedlar { get; }

    public Shopper Main { get; }

    public static async Task<VendorWorld> CreateAsync() => new(await TestStaticData.LoadAsync(
        items: VendorTestData.Items,
        nodes:
        [
            new DialogueNode { Id = SmithRoot, CreatureTemplateId = VendorTestData.Smith, IsRoot = true, TextId = 1 },
            new DialogueNode { Id = PedlarRoot, CreatureTemplateId = VendorTestData.Pedlar, IsRoot = true, TextId = 3 },
            new DialogueNode { Id = InnkeeperRoot, CreatureTemplateId = InnkeeperTemplate, IsRoot = true, TextId = 4 },
        ],
        options:
        [
            new DialogueOption
            {
                Id = SmithWares, NodeId = SmithRoot, TextId = 2, NextNodeId = SmithRoot, SortOrder = 0,
                Action = DialogueOptionAction.OpenShop,
            },
            new DialogueOption { Id = SmithFarewell, NodeId = SmithRoot, TextId = 5, NextNodeId = null, SortOrder = 1 },
            new DialogueOption
            {
                Id = PedlarWares, NodeId = PedlarRoot, TextId = 2, NextNodeId = PedlarRoot, SortOrder = 0,
                Action = DialogueOptionAction.OpenShop,
            },
            new DialogueOption { Id = PedlarFarewell, NodeId = PedlarRoot, TextId = 5, NextNodeId = null, SortOrder = 1 },
            new DialogueOption { Id = InnkeeperFarewell, NodeId = InnkeeperRoot, TextId = 5, NextNodeId = null, SortOrder = 0 },
            new DialogueOption
            {
                Id = SmithWaresAndLeave, NodeId = SmithRoot, TextId = 2, NextNodeId = null, SortOrder = 2,
                Action = DialogueOptionAction.OpenShop,
            },
        ],
        texts:
        [
            new LocalizedText { Id = 1, Text = "Steel, stave or string, {name}?" },
            new LocalizedText { Id = 2, Text = "Show me your wares." },
            new LocalizedText { Id = 3, Text = "Potions, scrolls, supplies." },
            new LocalizedText { Id = 4, Text = "Room's upstairs." },
            new LocalizedText { Id = 5, Text = "Farewell." },
        ],
        vendors: VendorTestData.Rows()));

    private VendorWorld(StaticData data)
    {
        Data = data;
        // Neutral names, so nothing here can be mistaken for a seeded NPC.
        Smith = TestTown.AddNpc(Creatures, SmithGuid, VendorTestData.Smith, "WeaponVendor", new Vector3(0, 0, 3));
        Pedlar = TestTown.AddNpc(Creatures, PedlarGuid, VendorTestData.Pedlar, "GoodsVendor", new Vector3(0, 0, -3));
        TestTown.AddNpc(Creatures, InnkeeperGuid, InnkeeperTemplate, "Talker", new Vector3(0, 0, 2));

        IMapInstance instance = Substitute.For<IMapInstance, IVendorHost>();
        instance.Creatures.Returns(Creatures);
        ((IVendorHost)instance).Vendors.Returns(Stocks);

        IMapInstance plain = Substitute.For<IMapInstance>();
        plain.Creatures.Returns(Creatures);

        // Only these two instances are stubbed; any other id comes back null.
        TestTown.Stub(World, Data, (InstanceId, instance), (PlainInstanceId, plain));
        Economy = new CharacterEconomy(World, new ItemIdAllocator());

        Main = AddShopper(7, money: 1000);
    }

    /// <summary>A warrior at the origin of this instance, whose connection records what it is sent.</summary>
    public Shopper AddShopper(uint id, ulong money = 0)
    {
        CharacterEntity character = TestCharacters.New(id, money);
        character.InstanceId = InstanceId;
        var shopper = new Shopper { Character = character, Connection = Substitute.For<IWorldConnection>() };
        TestTown.Record(shopper.Connection, character, shopper.Sent);

        _shoppers.Add(shopper);
        return shopper;
    }

    public void Interact(Shopper shopper, ObjectGuid npc) =>
        new InteractHandler(NullLogger<InteractHandler>.Instance, World).Execute(shopper.Connection,
            new CInteractPacket { TargetGuid = npc.RawValue });

    public void Choose(Shopper shopper, ObjectGuid npc, int node, int option) =>
        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, World, Quests).Execute(shopper.Connection,
            new CDialogueChoosePacket { TargetGuid = npc.RawValue, NodeId = node, OptionId = option });

    /// <summary>Talks to the Smith and chooses "Show me your wares.".</summary>
    public void OpenShop(Shopper? shopper = null)
    {
        Shopper who = shopper ?? Main;
        Interact(who, SmithGuid);
        Choose(who, SmithGuid, SmithRoot, SmithWares);
    }

    /// <summary>One CMSG_VENDOR_BUY through the handler; the answer is the result it sent.</summary>
    public VendorResult Buy(Shopper shopper, uint request, uint sequence, uint? count = null)
    {
        new VendorBuyHandler(NullLogger<VendorBuyHandler>.Instance, World, Economy, Quests, Clock).Execute(
            shopper.Connection, new CVendorBuyPacket { RequestId = request, Sequence = sequence, Count = count });
        return shopper.Results()[^1].Result;
    }

    public VendorResult Sell(Shopper shopper, uint request, uint bagSlot, uint? count = null)
    {
        new VendorSellHandler(NullLogger<VendorSellHandler>.Instance, World, Economy).Execute(
            shopper.Connection, new CVendorSellPacket { RequestId = request, BagSlot = bagSlot, Count = count });
        return shopper.Results()[^1].Result;
    }

    public VendorResult Buyback(Shopper shopper, uint request, uint index)
    {
        new VendorBuybackHandler(NullLogger<VendorBuybackHandler>.Instance, World, Economy).Execute(
            shopper.Connection, new CVendorBuybackPacket { RequestId = request, Index = index });
        return shopper.Results()[^1].Result;
    }

    /// <summary>What the instance's vendor pass does after the tick's packets (#432): reconcile and restock, send what is owed, clear.</summary>
    public void EndOfTick()
    {
        Stocks.Update(Clock.GetUtcNow().UtcDateTime, Data.Vendors, Data.ItemTemplates);
        foreach (Shopper shopper in _shoppers)
            VendorListBuilder.SendIfOwed(shopper.Connection, Stocks, Data, Quests);
        Stocks.ClearChanged();
    }
}
