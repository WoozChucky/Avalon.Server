using Avalon.World.Parties;
using Avalon.World.Public.Characters;

namespace Avalon.World.Loot;

/// <summary>Who a drop is reserved for, and until when.</summary>
public readonly record struct LootAllocation(uint? OwnerCharacterId, DateTime FreeForAllAt);

/// <summary>Decides who each drop belongs to; called once per drop.</summary>
public interface ILootAllocator
{
    LootAllocation Allocate(uint? instanceOwner, PartyId? instanceParty, IReadOnlyList<ICharacter> eligible);
}
