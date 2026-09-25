using Avalon.World.Configuration;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Options;

namespace Avalon.World.Loot;

/// <summary>Who a kill's drops are reserved for, and until when.</summary>
public readonly record struct LootAllocation(uint? OwnerCharacterId, DateTime FreeForAllAt);

/// <summary>
/// Decides who a kill's drops belong to. The extension point for groups: a group allocation later
/// replaces the implementation, and the drop packet already carries an owner and a free-for-all
/// time, so the wire format does not change.
/// </summary>
public interface ILootAllocator
{
    LootAllocation Allocate(ICreature creature, IMapInstance instance);
}

/// <summary>
/// The instance's owner gets everything, reserved for <see cref="GameConfiguration.LootGracePeriod"/>.
/// An instance with no owner (a town) makes every drop free for all at once.
/// </summary>
public sealed class InstanceOwnerLootAllocator(IOptions<GameConfiguration> configuration, TimeProvider time)
    : ILootAllocator
{
    public LootAllocation Allocate(ICreature creature, IMapInstance instance)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;

        return instance.OwnerCharacterId is { } owner
            ? new LootAllocation(owner, now + configuration.Value.LootGracePeriod)
            : new LootAllocation(null, now);
    }
}
