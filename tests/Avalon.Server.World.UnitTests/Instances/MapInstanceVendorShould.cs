using System.IO;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.Cryptography;
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
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Instances;
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
    private static readonly ObjectGuid SmithGuid = new(ObjectType.Creature, 92);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(Now));
    private StaticData _data = null!;
    private IWorld _world = null!;

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

        _world = Substitute.For<IWorld>();
        _world.Configuration.Returns(new GameConfiguration());
        _world.MapTemplates.Returns(new List<MapTemplate>());
        _world.Data.Returns(_data);
        return Build(_world);
    }

    private MapInstance Build(IWorld world)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
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
            connection.CurrentDialogue = (SmithGuid, new DialogueNodeId(1));
            character.OpenShopNpc = SmithGuid;
        }

        instance.AddCharacter(connection);
        return new Client(connection, character, sent);
    }

    /// <summary>What choosing "Show me your wares." leaves in the instance.</summary>
    private VendorStockState SmithStock(MapInstance instance) =>
        instance.Vendors.For(SmithGuid, Smith, _data.Vendors.RowsFor(Smith));

    private static uint? BladesLeft(VendorStockState stock) =>
        stock.Available(stock.Rows.Single(r => r.Sequence == BladeSequence));

    private static VendorResult BuyCharm(CharacterEntity character, VendorStockState stock) =>
        VendorRules.DecideBuy(character, shopOpen: true, stock, CharmSequence, count: 1, Find, NoQuestProgress.Instance).Result;

    /// <summary>The time a vendor handler would take a sale at: the container's clock.</summary>
    private DateTime ClockNow => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Send_every_open_shop_the_list_when_the_last_unit_sells()
    {
        using MapInstance instance = await BuildAsync();
        Client first = Join(instance, 432_001, shopOpen: true);
        Client second = Join(instance, 432_002, shopOpen: true);
        Client browsing = Join(instance, 432_003, shopOpen: false);
        VendorStockState stock = SmithStock(instance);

        stock.Take(stock.Rows.Single(r => r.Sequence == BladeSequence), 2, ClockNow);
        instance.Update(Tick);

        Assert.Equal((uint?)0, Assert.Single(first.Lists()).Entries.Single(e => e.Sequence == BladeSequence).Stock);
        Assert.Equal((uint?)0, Assert.Single(second.Lists()).Entries.Single(e => e.Sequence == BladeSequence).Stock);
        Assert.Empty(browsing.Lists());
        Assert.False(stock.Changed);
    }

    [Fact]
    public async Task Refill_and_tell_everyone_with_the_shop_open_once_the_restock_is_due()
    {
        using MapInstance instance = await BuildAsync();
        Client first = Join(instance, 432_001, shopOpen: true);
        Client second = Join(instance, 432_002, shopOpen: true);
        VendorStockState stock = SmithStock(instance);
        stock.Take(stock.Rows.Single(r => r.Sequence == BladeSequence), 2, ClockNow);
        instance.Update(Tick);

        _clock.Now = _clock.Now.AddSeconds(59);
        instance.Update(Tick);
        Assert.Single(first.Lists());

        _clock.Now = _clock.Now.AddSeconds(1);
        instance.Update(Tick);

        foreach (Client client in new[] { first, second })
        {
            Assert.Equal(2, client.Lists().Count);
            Assert.Equal((uint?)2, client.Lists()[^1].Entries.Single(e => e.Sequence == BladeSequence).Stock);
        }
    }

    /// <summary>
    /// The pass reads the same catalog snapshot the shop was opened from: had it read another, its
    /// reconcile would mark the stock changed and every open shop would hear a list.
    /// </summary>
    [Fact]
    public async Task Send_nothing_on_a_tick_where_nothing_changed()
    {
        using MapInstance instance = await BuildAsync();
        Client first = Join(instance, 432_001, shopOpen: true);
        SmithStock(instance);

        instance.Update(Tick);
        instance.Update(Tick);

        Assert.Empty(first.Lists());
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
        Assert.True(instance.Vendors.TryGet(SmithGuid, out VendorStockState? stock));
        Assert.NotEqual(VendorResult.NotFound, BuyCharm(first.Character, stock));
        List<VendorStock> rows = Rows();
        rows.RemoveAll(r => r.Id == 4);

        _data.Apply(new VendorsPatch(Catalog(rows)));
        instance.Update(Tick);

        Assert.Equal([1u, 2u, 3u], Assert.Single(first.Lists()).Entries.Select(e => e.Sequence));
        Assert.Equal([1u, 2u, 3u], Assert.Single(second.Lists()).Entries.Select(e => e.Sequence));
        Assert.Equal(VendorResult.NotFound, BuyCharm(first.Character, stock));
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
        instance.Update(Tick);

        List<VendorStock> rows = Rows();
        rows.Single(r => r.Id == 2).MaxStock = 5;
        _data.Apply(new VendorsPatch(Catalog(rows)));
        instance.Update(Tick);

        _clock.Now = _clock.Now.AddSeconds(59);
        instance.Update(Tick);
        Assert.Equal((uint?)2, BladesLeft(stock));

        _clock.Now = _clock.Now.AddSeconds(1);
        instance.Update(Tick);

        Assert.Equal((uint?)5, BladesLeft(stock));
        Assert.Empty(first.Lists());
    }

    /// <summary>
    /// The pass's "now" is the container's TimeProvider, the clock the vendor handlers take their
    /// sales at, not the system clock. The clock here is decades from the system's, so a pass on
    /// the wrong clock never restocks.
    /// </summary>
    [Fact]
    public async Task Restock_on_the_container_clock()
    {
        _clock.Now = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using MapInstance instance = await BuildAsync();
        Join(instance, 432_001, shopOpen: true);
        VendorStockState stock = SmithStock(instance);
        stock.Take(stock.Rows.Single(r => r.Sequence == BladeSequence), 2, ClockNow);

        _clock.Now = _clock.Now.AddSeconds(59);
        instance.Update(Tick);
        Assert.Equal((uint?)0, BladesLeft(stock));

        _clock.Now = _clock.Now.AddSeconds(1);
        instance.Update(Tick);
        Assert.Equal((uint?)2, BladesLeft(stock));
    }

    /// <summary>One read of the world's static data per pass, so the whole pass works from one catalog.</summary>
    [Fact]
    public async Task Read_the_vendor_data_once_per_pass()
    {
        using MapInstance instance = await BuildAsync();
        Join(instance, 432_001, shopOpen: true);
        Join(instance, 432_002, shopOpen: true);
        SmithStock(instance);
        instance.Update(Tick);

        _world.ClearReceivedCalls();
        instance.Update(Tick);

        _ = _world.Received(1).Data;
    }

    [Fact]
    public void Tick_an_instance_nobody_shopped_in_without_reading_vendor_data()
    {
        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());
        world.MapTemplates.Returns(new List<MapTemplate>());
        // world.Data is deliberately left unconfigured, so it is null: the pass must not run.
        using MapInstance instance = Build(world);
        Join(instance, 432_001, shopOpen: true);

        instance.Update(Tick);

        Assert.Equal(0, instance.Vendors.Count);
        _ = world.DidNotReceive().Data;
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
        shopping.CurrentDialogue = (SmithGuid, new DialogueNodeId(1));
        ((CharacterEntity)shopping.Character!).OpenShopNpc = SmithGuid;
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

    /// <summary>Only what building an instance and running the vendor pass read.</summary>
    private sealed class QuietWorld(StaticData data) : IWorld
    {
        public StaticData Data { get; } = data;
        public GameConfiguration Configuration { get; } = new();
        public IReadOnlyList<MapTemplate> MapTemplates { get; } = [];

        public Avalon.Domain.Auth.WorldId Id => throw new NotSupportedException();
        public string MinVersion => throw new NotSupportedException();
        public string CurrentVersion => throw new NotSupportedException();
        public GameTime Time => throw new NotSupportedException();
        public IInstanceRegistry InstanceRegistry => throw new NotSupportedException();
        public void SpawnInInstance(IWorldConnection connection, IMapInstance instance) => throw new NotSupportedException();
        public void TransferPlayer(IWorldConnection connection, IMapInstance targetInstance) => throw new NotSupportedException();
        public Task DeSpawnPlayerAsync(IWorldConnection connection) => throw new NotSupportedException();
        public Task LoadAsync(CancellationToken token) => throw new NotSupportedException();
        public void Update(TimeSpan deltaTime) => throw new NotSupportedException();
    }

    /// <summary>Only what the vendor pass reads, plus a count of what it sends.</summary>
    private sealed class QuietConnection(CharacterEntity character) : IWorldConnection
    {
        public int Sent { get; private set; }

        public ICharacter? Character { get; set; } = character;

        public (ObjectGuid Npc, DialogueNodeId Node)? CurrentDialogue { get; set; }

        public IAvalonCryptoSession CryptoSession { get; } = new FakeAvalonCryptoSession();

        public void Send(NetworkPacket packet) => Sent++;

        public Guid Id => throw new NotSupportedException();
        public Task? ExecuteTask => throw new NotSupportedException();
        public string RemoteEndPoint => throw new NotSupportedException();
        public ICryptoManager ServerCrypto => throw new NotSupportedException();
        public AccountId? AccountId { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public PendingSpawn? PendingSpawn => throw new NotSupportedException();
        public bool SelectInProgress => throw new NotSupportedException();
        public long SelectStartedTicks => throw new NotSupportedException();
        public bool IsConnected => throw new NotSupportedException();
        public bool IsClosing => throw new NotSupportedException();
        public long Latency => throw new NotSupportedException();
        public long RoundTripTime => throw new NotSupportedException();
        public long CurrentPacketArrivedTicks => throw new NotSupportedException();
        public bool InGame => throw new NotSupportedException();
        public bool InMap => throw new NotSupportedException();
        public uint LastInputSeq { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public ulong? CurrentTargetGuid { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public AccountLocale Locale { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public AccountAccessLevel AccessLevel => throw new NotSupportedException();
        public bool RespawnInFlight { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public void Close(bool expected = true) => throw new NotSupportedException();
        public Task CloseAsync(bool expected = true) => throw new NotSupportedException();
        public Task StartAsync(CancellationToken token = default) => throw new NotSupportedException();
        public void BeginSelect(long nowTicks) => throw new NotSupportedException();
        public void CancelSelect() => throw new NotSupportedException();
        public void SetPendingSpawn(ICharacter character, IMapInstance instance, long sinceTicks) => throw new NotSupportedException();
        public PendingSpawn? TakePendingSpawn() => throw new NotSupportedException();
        public void SendTimeSyncPing() => throw new NotSupportedException();
        public void RequestInitialTimeSyncPing() => throw new NotSupportedException();
        public bool TakeInitialTimeSyncPingRequest() => throw new NotSupportedException();

        public void OnPongReceived(long lastServerTimestamp, long clientReceivedTimestamp, long clientSentTimestamp, long serverReceivedTicks) =>
            throw new NotSupportedException();

        public void UpdateSession() => throw new NotSupportedException();
        public void UpdateMap() => throw new NotSupportedException();
        public void FlushContinuations() => throw new NotSupportedException();
        public void FlushOutbox() => throw new NotSupportedException();
        public void EnqueueContinuation<T>(Task<T> task, Action<T> callback) => throw new NotSupportedException();
        public void EnqueueContinuation(Task task, Action callback) => throw new NotSupportedException();
    }
}
