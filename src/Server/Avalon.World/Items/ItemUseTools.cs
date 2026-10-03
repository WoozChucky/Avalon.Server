using Avalon.World.ChunkLayouts;
using Avalon.World.Inventory;
using Avalon.World.Parties;
using Avalon.World.Quests;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Items;

/// <summary>
/// The services an item use reaches through its context (item use): one DI singleton, so ItemUseService
/// and every ItemUseContext share them. World-side. A script never sees this class, only IItemUseContext.
/// </summary>
public sealed class ItemUseTools(
    IWorld world,
    ICharacterEconomy economy,
    TownReturn townReturn,
    MapTeleport teleport,
    ICreaturePlacementService placement,
    TimeProvider time,
    ILogger<ItemUseTools> logger,
    QuestService? quests = null,
    PartyService? parties = null)
{
    public IWorld World { get; } = world;
    public ICharacterEconomy Economy { get; } = economy;
    public TownReturn TownReturn { get; } = townReturn;
    public MapTeleport Teleport { get; } = teleport;
    public ICreaturePlacementService Placement { get; } = placement;
    public TimeProvider Time { get; } = time;
    public ILogger Logger { get; } = logger;
    public QuestService? Quests { get; } = quests;
    public PartyService? Parties { get; } = parties;

    /// <summary>An item's aura that threw on the way on, logged at most once per ThrottledErrorLog.Interval.</summary>
    internal ThrottledErrorLog AuraFailures { get; } = new(logger, time, "Applying an item's aura");
}
