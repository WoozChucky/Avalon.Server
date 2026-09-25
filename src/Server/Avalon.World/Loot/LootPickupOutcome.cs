using Avalon.Network.Packets.Loot;

namespace Avalon.World.Loot;

/// <summary>What a pickup answered, and whether the drop left the ground (and needs a despawn broadcast).</summary>
public readonly record struct LootPickupOutcome(LootPickupResult Result, bool Removed);
