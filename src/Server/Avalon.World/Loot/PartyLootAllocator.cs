using Avalon.Combat;
using Avalon.World.Configuration;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Options;

namespace Avalon.World.Loot;

/// <summary>
/// A solo instance's drops are reserved for its owner and a town's are free for all at once, as before. In a party
/// instance (2026-09-30) each drop is reserved for one eligible member drawn uniformly through the combat random —
/// no draw when only one is eligible — and free for all once LootGracePeriod has passed; with nobody eligible it is
/// free for all at once.
/// </summary>
public sealed class PartyLootAllocator(IOptions<GameConfiguration> configuration, TimeProvider time, ICombatRandom random)
    : ILootAllocator
{
    public LootAllocation Allocate(uint? instanceOwner, PartyId? instanceParty, IReadOnlyList<ICharacter> eligible)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        DateTime reservedUntil = now + configuration.Value.LootGracePeriod;

        if (instanceOwner is { } owner)
            return new LootAllocation(owner, reservedUntil);

        if (instanceParty is null || eligible.Count == 0)
            return new LootAllocation(null, now);

        int pick = eligible.Count == 1 ? 0 : (int)random.NextInt64(0, eligible.Count - 1);
        return new LootAllocation(eligible[pick].Guid.Id, reservedUntil);
    }
}
