using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Maps;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Avalon.World.Scripts;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// A real MapInstance's vendor pass (spec #432). VendorStocksShould and VendorListBuilderShould
/// test the parts; these pin the wiring, which is the only thing that fails if Update stops
/// running the pass. Every list reaches every connection whose shop with that vendor is open, and
/// no one else.
/// </summary>
public class MapInstanceVendorShould
{
    private static readonly ObjectGuid s_smithGuid = new(ObjectType.Creature, 92);
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(Now));
    private StaticData _data = null!;

    private sealed record Client(IWorldConnection Connection, CharacterEntity Character, List<NetworkPacket> Sent)
    {
        public List<SVendorListPacket> Lists() => Sent
            .Where(p => p.Header.Type == NetworkPacketType.SMSG_VENDOR_LIST)
            .Select(p =>
            {
                using var stream = new MemoryStream(p.Payload);
                return Serializer.Deserialize<SVendorListPacket>(stream);
            })
            .ToList();
    }

    private async Task<MapInstance> BuildAsync()
    {
        _data = await TestStaticData.LoadAsync(items: Items, vendors: Rows());

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate>());
        world.Data.Returns(_data);
        return Build(world);
    }

    private MapInstance Build(IWorld world)
    {
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        serviceProvider.GetService(typeof(TimeProvider)).Returns(_clock);

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        return new MapInstance(NullLoggerFactory.Instance, serviceProvider, world, new MapTemplateId(1),
            ownerCharacterId: null, layout, Substitute.For<IMapNavigator>(), seed: 0);
    }

    /// <summary>A character in the instance, with the Smith's shop open or not.</summary>
    private static Client Join(MapInstance instance, uint id, bool shopOpen)
    {
        CharacterEntity character = Inventory.TestCharacters.New(id);
        character.Spells.Load(Array.Empty<IAbility>());   // the tick updates abilities; an unloaded list throws

        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));

        if (shopOpen)
        {
            connection.CurrentDialogue = (s_smithGuid, new DialogueNodeId(1));
            character.OpenShopNpc = s_smithGuid;
        }

        instance.AddCharacter(connection);
        return new Client(connection, character, sent);
    }

    /// <summary>What choosing "Show me your wares." leaves in the instance.</summary>
    private VendorStockState SmithStock(MapInstance instance) =>
        instance.Vendors.For(s_smithGuid, Smith, _data.Vendors.RowsFor(Smith));

    private static uint? BladesLeft(VendorStockState stock) =>
        stock.Available(stock.Rows.Single(r => r.Sequence == BladeSequence));

    private static VendorResult BuyCharm(CharacterEntity character, VendorStockState stock) =>
        VendorRules.DecideBuy(character, shopOpen: true, stock, CharmSequence, count: 1, Find, NoQuestProgress.Instance).Result;

    /// <summary>The time a vendor handler would take a sale at: the container's clock.</summary>
    private DateTime ClockNow => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Refill_and_tell_everyone_with_the_shop_open_once_the_restock_is_due()
    {
        using MapInstance instance = await BuildAsync();
        Client first = Join(instance, 432_001, shopOpen: true);
        Client second = Join(instance, 432_002, shopOpen: true);
        VendorStockState stock = SmithStock(instance);
        stock.Take(stock.Rows.Single(r => r.Sequence == BladeSequence), 2, ClockNow);
        instance.Update(s_tick);

        _clock.Now = _clock.Now.AddSeconds(59);
        instance.Update(s_tick);
        Assert.Single(first.Lists());

        _clock.Now = _clock.Now.AddSeconds(1);
        instance.Update(s_tick);

        foreach (Client client in new[] { first, second })
        {
            Assert.Equal(2, client.Lists().Count);
            Assert.Equal((uint?)2, client.Lists()[^1].Entries.Single(e => e.Sequence == BladeSequence).Stock);
        }
    }

    /// <summary>
    /// A /reload vendors reaches every open shop on the next tick with the removed row gone, and
    /// buying that row is NotFound (#432).
    /// </summary>
    [Fact]
    public async Task Resend_the_list_to_every_open_shop_after_a_vendor_reload()
    {
        using MapInstance instance = await BuildAsync();
        Client first = Join(instance, 432_001, shopOpen: true);
        Client second = Join(instance, 432_002, shopOpen: true);
        SmithStock(instance);
        Assert.True(instance.Vendors.TryGet(s_smithGuid, out VendorStockState? stock));
        Assert.NotEqual(VendorResult.NotFound, BuyCharm(first.Character, stock));
        List<VendorStock> rows = Rows();
        rows.RemoveAll(r => r.Id == 4);

        _data.Apply(new VendorsPatch(Catalog(rows)));
        instance.Update(s_tick);

        Assert.Equal([1u, 2u, 3u], Assert.Single(first.Lists()).Entries.Select(e => e.Sequence));
        Assert.Equal([1u, 2u, 3u], Assert.Single(second.Lists()).Entries.Select(e => e.Sequence));
        Assert.Equal(VendorResult.NotFound, BuyCharm(first.Character, stock));
    }

    /// <summary>
    /// A /reload items that changes a BuyPrice reaches every open shop on the next tick with the new
    /// price, the one the next buy charges, although the vendor catalog itself did not change (#432).
    /// </summary>
    [Fact]
    public async Task Resend_the_list_to_every_open_shop_after_an_item_reload()
    {
        using MapInstance instance = await BuildAsync();
        Client first = Join(instance, 432_001, shopOpen: true);
        Client second = Join(instance, 432_002, shopOpen: true);
        Client browsing = Join(instance, 432_003, shopOpen: false);
        SmithStock(instance);
        instance.Update(s_tick);

        var dearerTonic = new ItemTemplate
        {
            Id = Tonic.Id,
            Name = Tonic.Name,
            Class = Tonic.Class,
            SubClass = Tonic.SubClass,
            MaxStackSize = Tonic.MaxStackSize,
            BuyPrice = 15,
            SellPrice = Tonic.SellPrice,
        };
        _data.Apply(new ItemsPatch(Items.Select(t => t.Id == Tonic.Id ? dearerTonic : t).ToList()));
        instance.Update(s_tick);

        Assert.Equal(15u, Assert.Single(first.Lists()).Entries.Single(e => e.Sequence == TonicSequence).Price);
        Assert.Equal(15u, Assert.Single(second.Lists()).Entries.Single(e => e.Sequence == TonicSequence).Price);
        Assert.Empty(browsing.Lists());

        instance.Update(s_tick);
        Assert.Single(first.Lists());
    }

    /// <summary>With no reload, an open shop hears nothing, however many ticks pass (#432).</summary>
    [Fact]
    public async Task Send_nothing_while_no_item_reload_lands()
    {
        using MapInstance instance = await BuildAsync();
        Client first = Join(instance, 432_001, shopOpen: true);
        SmithStock(instance);

        for (int tick = 0; tick < 10; tick++)
            instance.Update(s_tick);

        Assert.Empty(first.Lists());
    }

    /// <summary>
    /// The pass runs on every tick while the instance keeps stock, whether or not a shop is open or
    /// anything changed: a row whose MaxStock a reload raised starts its restock timer on the next
    /// tick, not at its next sale (#432).
    /// </summary>
    [Fact]
    public async Task Restock_a_row_a_reload_raised_while_no_shop_is_open()
    {
        using MapInstance instance = await BuildAsync();
        Client first = Join(instance, 432_001, shopOpen: true);
        VendorStockState stock = SmithStock(instance);
        first.Connection.CurrentDialogue = null;
        first.Character.CloseNpcWindows();
        instance.Update(s_tick);

        List<VendorStock> rows = Rows();
        rows.Single(r => r.Id == 2).MaxStock = 5;
        _data.Apply(new VendorsPatch(Catalog(rows)));
        instance.Update(s_tick);

        _clock.Now = _clock.Now.AddSeconds(59);
        instance.Update(s_tick);
        Assert.Equal((uint?)2, BladesLeft(stock));

        _clock.Now = _clock.Now.AddSeconds(1);
        instance.Update(s_tick);

        Assert.Equal((uint?)5, BladesLeft(stock));
        Assert.Empty(first.Lists());
    }

    /// <summary>
    /// A pass with a restock timer running but not due, and nothing owed to anyone, allocates
    /// nothing. The world and the connections are hand-written because a substitute allocates on
    /// every call it records.
    /// </summary>
    [Fact]
    public async Task Run_a_quiet_pass_without_allocating()
    {
        _data = await TestStaticData.LoadAsync(items: Items, vendors: Rows());
        using MapInstance instance = Build(new QuietWorld(_data));
        var shopping = new QuietConnection(Inventory.TestCharacters.New(432_001));
        var browsing = new QuietConnection(Inventory.TestCharacters.New(432_002));
        shopping.CurrentDialogue = (s_smithGuid, new DialogueNodeId(1));
        ((CharacterEntity)shopping.Character!).OpenShopNpc = s_smithGuid;
        instance.AddCharacter(shopping);
        instance.AddCharacter(browsing);
        VendorStockState stock = SmithStock(instance);
        stock.Take(stock.Rows.Single(r => r.Sequence == BladeSequence), 1, ClockNow);
        instance.RunVendorPass();   // sends the changed list, and warms the path up
        Assert.Equal(1, shopping.Sent);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int pass = 0; pass < 100; pass++)
            instance.RunVendorPass();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, shopping.Sent);
        Assert.Equal(0, browsing.Sent);
    }
}
