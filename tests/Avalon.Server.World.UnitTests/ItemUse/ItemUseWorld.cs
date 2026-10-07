using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Public.Enums;
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>
/// A real character in a real MapInstance the world's registry finds, and an ItemUseService over test scripts.
/// The character has 100 of 240 health. Templates 700-710 exercise the flow; the equipment templates the equip path.
/// </summary>
internal sealed class ItemUseWorld : IDisposable
{
    public static readonly ItemTemplate Tonic = Usable(700, "Tonic", nameof(ConsumeOneScript), cooldownMs: 30000, group: "potion");
    public static readonly ItemTemplate Elixir = Usable(701, "Elixir", nameof(ConsumeOneScript), cooldownMs: 30000, group: "potion");
    public static readonly ItemTemplate Scroll = Usable(702, "Scroll", nameof(ConsumeOneScript), castMs: 3000);
    public static readonly ItemTemplate Trinket = Usable(703, "Trinket", script: null);
    public static readonly ItemTemplate Refuser = Usable(704, "Refuser", nameof(RefusingScript), cooldownMs: 30000);
    public static readonly ItemTemplate Thrower = Usable(705, "Thrower", nameof(OnUseThrowingScript), cooldownMs: 30000);
    public static readonly ItemTemplate Missing = Usable(706, "Missing", "NoSuchItemScript");
    public static readonly ItemTemplate Hungry = Usable(707, "Hungry", nameof(WorldHungryItemScript));
    public static readonly ItemTemplate Fizzler = Usable(708, "Fizzler", nameof(InterruptThrowingScript), castMs: 3000, cooldownMs: 30000);
    public static readonly ItemTemplate Sparkler = Usable(709, "Sparkler", nameof(CastStartThrowingScript), castMs: 3000, cooldownMs: 30000);
    public static readonly ItemTemplate Draught = Usable(710, "Draught", nameof(RefuseAtFullHealthScript), castMs: 3000, cooldownMs: 30000);

    private static ItemTemplate Usable(ulong id, string name, string? script, uint? castMs = null, uint? cooldownMs = null,
        string? group = null) => new()
        {
            Id = new ItemTemplateId(id),
            Name = name,
            Class = ItemClass.Consumable,
            MaxStackSize = 20,
            UseScript = script,
            UseCastTimeMs = castMs,
            UseCooldownMs = cooldownMs,
            UseCooldownGroup = group,
        };

    public FakeTimeProvider Time { get; } = new();
    public IWorld World { get; }
    public MapInstance Instance { get; }
    public MapInstanceClient Client { get; }
    public CharacterEntity Character => Client.Character;
    public ItemUseService Service { get; }
    public IInventoryService Inventory { get; }

    /// <summary>The character's class and level, so a gear change has stats to refresh.</summary>
    public static readonly ClassLevelStat WarriorLevel1 = new()
    {
        Class = CharacterClass.Warrior,
        Level = 1,
        BaseHp = 20,
        Stamina = 22,
        Strength = 23,
        Agility = 20,
        Intellect = 20,
    };

    public static async Task<ItemUseWorld> CreateAsync() => new(await TestStaticData.LoadAsync(
        classStats: [WarriorLevel1],
        items:
        [
            Tonic, Elixir, Scroll, Trinket, Refuser, Thrower, Missing, Hungry, Fizzler, Sparkler, Draught,
            EquipTemplates.Longsword, EquipTemplates.Greatsword, EquipTemplates.Buckler, EquipTemplates.Band,
            EquipTemplates.IronHelm, EquipTemplates.Circlet,
        ]));

    private ItemUseWorld(StaticData data)
    {
        World = MapInstanceClients.NewWorld(data);
        Instance = TestMapInstances.Build(World);
        World.InstanceRegistry.GetInstanceById(Instance.InstanceId).Returns(Instance);

        CharacterEntity character = TestCharacters.New(7);
        character.Health = 240;
        character.CurrentHealth = 100;
        Client = MapInstanceClients.Join(Instance, character);

        var scripts = Substitute.For<IScriptManager>();
        foreach (Type type in new[]
                 {
                     typeof(ConsumeOneScript), typeof(RefusingScript), typeof(OnUseThrowingScript), typeof(WorldHungryItemScript),
                     typeof(InterruptThrowingScript), typeof(CastStartThrowingScript), typeof(RefuseAtFullHealthScript),
                 })
            scripts.GetItemScript(type.Name).Returns(type);

        var economy = new CharacterEconomy(World, new ItemIdAllocator());
        Inventory = economy.InventoryOf(character);
        var tools = new ItemUseTools(World, economy,
            new TownReturn(NullLogger.Instance, World, Substitute.For<IRespawnTargetResolver>(), Substitute.For<IChunkLibrary>()),
            new MapTeleport(NullLogger<MapTeleport>.Instance, World, Substitute.For<IChunkLibrary>(), Substitute.For<Avalon.World.Persistence.ICharacterSaver>()),
            Substitute.For<ICreaturePlacementService>(), Time, NullLogger<ItemUseTools>.Instance);
        Service = new ItemUseService(tools, scripts, Substitute.For<IServiceProvider>(), NullLogger<ItemUseService>.Instance);
    }

    public void Bag(params Avalon.World.Public.Characters.InventoryItem[] items) =>
        Character.Container(InventoryType.Bag).Load(items);

    public void Use(uint request, ushort slot, uint container = (uint)InventoryType.Bag) =>
        Service.Use(Client.Connection, Character, request, container, slot);

    public void Tick(double seconds) => Instance.Update(TimeSpan.FromSeconds(seconds));

    public List<SItemUseResultPacket> Results() => Client.Read<SItemUseResultPacket>(NetworkPacketType.SMSG_ITEM_USE_RESULT);

    public List<string> Lines() =>
        Client.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE).Select(m => m.Message).ToList();

    public void Dispose() => Instance.Dispose();
}
