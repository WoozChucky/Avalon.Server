using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;
using Avalon.World.Testing.Fakes;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// One <c>CMSG_ITEM_MOVE</c> and one <c>CMSG_ITEM_DESTROY</c> through their handlers to the <c>SMSG_ITEM_RESULT</c>
/// each is answered with (#883). <see cref="Accepted_move" /> moves a stack between two Bag slots, there and back on
/// alternate calls, and is answered Ok with no slots; <see cref="Refused_move" /> aims a stack at a full one and is
/// answered TargetFull with the state of both slots; <see cref="Refused_destroy" /> names an empty slot and is answered
/// NotFound with that slot.
/// </summary>
/// <remarks>
/// The economy here hands back one inventory service kept for the character, so a row is the handler, the move rules
/// and the reply. Production's <c>CharacterEconomy.InventoryOf</c> does the same since #883 (the service is kept on
/// the character); before, it built a service and its template lookup per request on top of this, 104 B.
/// </remarks>
[MemoryDiagnoser]
public class ItemRequestBenchmarks
{
    private static readonly ItemTemplate s_potion = new()
    {
        Id = new ItemTemplateId(1),
        Name = "Potion",
        Class = ItemClass.Consumable,
        MaxStackSize = 20,
    };

    private readonly CItemMovePacket _there = Move(0, 1);
    private readonly CItemMovePacket _back = Move(1, 0);
    private readonly CItemMovePacket _ontoAFullStack = Move(2, 3);
    private readonly CItemDestroyPacket _nothing = new() { RequestId = 9, Container = InventoryType.Bag, Slot = 20 };

    private CharacterEntity _character = null!;
    private QuietConnection _connection = null!;
    private ItemMoveHandler _move = null!;
    private ItemDestroyHandler _destroy = null!;
    private bool _moved;

    [GlobalSetup]
    public void Setup()
    {
        var row = new Character
        {
            Id = new CharacterId(1),
            AccountId = new AccountId(1),
            Name = "Bench",
            Class = CharacterClass.Warrior,
            CreationDate = DateTime.UtcNow,
        };
        _character = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration()) { Data = row };
        _character.Container(InventoryType.Bag).Load([Stack(0, 5), Stack(2, 5), Stack(3, 20)]);
        _connection = new QuietConnection(_character);

        var ids = new ItemIdAllocator();
        var items = new ItemsPatch([s_potion], new ItemModifierCatalog([], [], NullLogger.Instance));
        var economy = new OneCharacterEconomy(new CharacterInventoryService(_character, static _ => s_potion, ids,
            new ItemRoller(new LootRandom(new Random(0)), ids, Options.Create(new GameConfiguration())), () => items));
        var world = new QuietWorld(null!);
        _move = new ItemMoveHandler(NullLogger<ItemMoveHandler>.Instance, world, economy);
        _destroy = new ItemDestroyHandler(NullLogger<ItemDestroyHandler>.Instance, world, economy);

        Accepted_move();
        Accepted_move();
        if (!_character.Container(InventoryType.Bag).TryGet(0, out InventoryItem home) || home.Count != 5)
            throw new InvalidOperationException("The accepted move did not move the stack there and back.");

        Refused_move();
        Refused_destroy();
        if (_connection.Sent != 4 || _character.ClientChanges.HasChanges)
            throw new InvalidOperationException("A request was not answered once, or a refusal changed something.");
    }

    [Benchmark]
    public int Accepted_move()
    {
        _move.Execute(_connection, _moved ? _back : _there);
        _moved = !_moved;
        // The tick's flush would send these two slots and forget them; this row is the request alone.
        _character.ClientChanges.Clear();
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_move()
    {
        _move.Execute(_connection, _ontoAFullStack);
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_destroy()
    {
        _destroy.Execute(_connection, _nothing);
        return _connection.Sent;
    }

    private static CItemMovePacket Move(byte from, byte to) => new()
    {
        RequestId = 7,
        FromContainer = InventoryType.Bag,
        FromSlot = from,
        ToContainer = InventoryType.Bag,
        ToSlot = to,
    };

    private static InventoryItem Stack(byte slot, uint count) =>
        new(slot, new ItemInstanceId(Guid.CreateVersion7()), s_potion.Id, count, 0, ItemInstanceFlags.None, ItemRarity.Common, 1, RareName: null);

    private sealed class OneCharacterEconomy(IInventoryService inventory) : ICharacterEconomy
    {
        public IInventoryService InventoryOf(CharacterEntity character) => inventory;

        public IWallet WalletOf(CharacterEntity character) => throw new NotSupportedException();

        public VendorTrade VendorOf(CharacterEntity character) => throw new NotSupportedException();
    }
}
