using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Testing.Fakes;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// One <c>CMSG_ITEM_MOVE</c> and one <c>CMSG_ITEM_DESTROY</c> through their handlers to the <c>SMSG_ITEM_RESULT</c>
/// each is answered with (#883). <see cref="Accepted_move" /> moves a stack between two Bag slots, there and back on
/// alternate calls, and is answered Ok with no slots; <see cref="Refused_move" /> aims a stack at a full one and is
/// answered TargetFull with the state of both slots; <see cref="Refused_destroy" /> names an empty slot and is answered
/// NotFound with that slot.
/// </summary>
/// <remarks>
/// The world holds the seeded reference data and the economy is production's <see cref="CharacterEconomy" />, which
/// keeps one inventory service on the character (#883), so a row is the handler, the move rules and the reply, built
/// as a real server builds them (#1198: a hand-made economy over a world with no data fell behind the handler). The
/// stack is the seeded consumable of lowest id that stacks to more than 5. Setup fails the run unless each row's first
/// call is answered once, with its intended result, as read back from the reply the connection was sent.
/// </remarks>
[MemoryDiagnoser]
public class ItemRequestBenchmarks
{
    private readonly CItemMovePacket _there = Move(0, 1);
    private readonly CItemMovePacket _back = Move(1, 0);
    private readonly CItemMovePacket _ontoAFullStack = Move(2, 3);
    private readonly CItemDestroyPacket _nothing = new() { RequestId = 9, Container = InventoryType.Bag, Slot = 20 };

    private ItemTemplate _stack = null!;
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

        StaticData data = ScenarioReferenceData.Shared.Data;
        _stack = data.Items.Templates.Where(t => t.Class == ItemClass.Consumable && t.MaxStackSize > 5).MinBy(t => t.Id.Value)
            ?? throw new InvalidOperationException("The seed has no consumable that stacks to more than 5.");
        _character.Container(InventoryType.Bag).Load([Stack(0, 5), Stack(2, 5), Stack(3, _stack.MaxStackSize)]);
        _connection = new QuietConnection(_character);

        var world = new QuietWorld(data);
        var ids = new ItemIdAllocator();
        var economy = new CharacterEconomy(world, ids,
            new ItemRoller(new LootRandom(new Random(0)), ids, Options.Create(new GameConfiguration())));
        _move = new ItemMoveHandler(NullLogger<ItemMoveHandler>.Instance, world, economy);
        _destroy = new ItemDestroyHandler(NullLogger<ItemDestroyHandler>.Instance, world, economy);

        _connection.KeepLastPayload = true;
        Accepted_move();
        Expect(ItemRequestResult.Ok, slots: 0);
        Accepted_move();
        Expect(ItemRequestResult.Ok, slots: 0);
        if (!_character.Container(InventoryType.Bag).TryGet(0, out InventoryItem home) || home.Count != 5)
            throw new InvalidOperationException("The accepted move did not move the stack there and back.");

        Refused_move();
        Expect(ItemRequestResult.TargetFull, slots: 2);
        Refused_destroy();
        Expect(ItemRequestResult.NotFound, slots: 1);
        _connection.KeepLastPayload = false;
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

    /// <summary>Fails unless the last reply answered <paramref name="intended" /> and reported that many slots.</summary>
    private void Expect(ItemRequestResult intended, int slots)
    {
        SItemResultPacket reply = Serializer.Deserialize<SItemResultPacket>(_connection.LastPayload);
        if (reply.Result != intended || reply.Slots.Length != slots)
        {
            throw new InvalidOperationException(
                $"A request was answered {reply.Result} with {reply.Slots.Length} slots, not {intended} with {slots}.");
        }
    }

    private InventoryItem Stack(byte slot, uint count) =>
        new(slot, new ItemInstanceId(Guid.CreateVersion7()), _stack.Id, count, 0, ItemInstanceFlags.None, ItemRarity.Common, 1, RareName: null, Identified: true, Tainted: false);
}
