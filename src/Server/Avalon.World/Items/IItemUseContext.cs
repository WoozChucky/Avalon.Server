using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;

namespace Avalon.World.Items;

/// <summary>
/// The curated toolbox an ItemScript gets for one use (item use, 2026-10-02, owner decision 1): its user, its item,
/// and the effects an item may have, each going through the service that owns it (the inventory service, the wallet,
/// the combat service, the cast system, the quest service, the experience award, the creature placement, the town
/// return and the map teleport), so every change is saved and sent like any other. Everything acts on the user, on
/// units of the user's own instance, or on its party members in that instance; never a raw service, connection or
/// another instance. Tick thread only. World-side, not the modding API. Each member's rule is the owner's decision
/// (PD-A to PD-F), named in its summary.
/// </summary>
public interface IItemUseContext
{
    // The user.
    string Name { get; }
    ushort Level { get; }
    CharacterClass Class { get; }
    uint Health { get; }
    uint MaxHealth { get; }
    PowerType PowerType { get; }
    uint Power { get; }
    uint MaxPower { get; }
    Vector3 Position { get; }
    ushort MapId { get; }
    bool InTown { get; }
    bool InCombat { get; }
    bool InParty { get; }

    /// <summary>A move (a respawn, a party countdown's return, a scroll, a teleport) is already under way.</summary>
    bool ReturningToTown { get; }

    // The item.
    ItemTemplateId ItemId { get; }
    string ItemName { get; }
    uint? UseValue { get; }

    /// <summary>How many the used stack holds now.</summary>
    uint StackCount { get; }

    /// <summary>The item's configured cooldown (UseCooldownMs), started once the use succeeds.</summary>
    TimeSpan Cooldown { get; }

    /// <summary>Marks <paramref name="count" /> of the used stack spent; taken once OnUse returns. Throws for 0 or past the stack.</summary>
    void Consume(uint count = 1);

    // Inventory and money.
    bool HasBagRoom(ItemTemplateId item, uint count);
    bool GiveItem(ItemTemplateId item, uint count);
    bool TakeItem(ItemTemplateId item, uint count);
    ulong Money { get; }

    /// <summary>False, changing nothing, past Game:MaxMoney.</summary>
    bool GiveMoney(ulong copper);
    bool TakeMoney(ulong copper);

    // Health, power and damage.

    /// <summary>Heals the user through the combat service, never past its maximum; answers what was restored.</summary>
    uint RestoreHealth(uint amount);

    /// <summary>Adds to the user's power, capped; nothing for a dead user or a pool no cast spends.</summary>
    uint RestorePower(uint amount);

    /// <summary>Living units of the user's instance within <paramref name="radius" /> that are hostile to it, nearest first (PD-A).</summary>
    IReadOnlyList<ObjectGuid> HostilesAround(float radius);

    /// <summary>A raw hit from the user on a living unit of its instance that is hostile to it, through the combat service (PD-A). False, doing nothing, otherwise.</summary>
    bool Damage(ObjectGuid target, uint amount);

    // Movement.

    /// <summary>Starts the move to the town of the current map. False, doing nothing, in a town or while a move is under way.</summary>
    bool ReturnToTown();

    /// <summary>Starts a move to <paramref name="map" />, at <paramref name="position" /> snapped to the navmesh or at its entry spawn, through World.TransferPlayer, saved on arrival (PD-B).</summary>
    bool Teleport(MapTemplateId map, Vector3? position = null);

    // Abilities.

    /// <summary>
    /// Casts an ability through the cast system (PD-C): one the user holds as itself, one it does not only when
    /// instant; <paramref name="free" /> skips the cost only; a Cursor ability needs <paramref name="point" />, toward
    /// which a Movement ability aims when given. False, doing nothing, when the cast is refused.
    /// </summary>
    bool CastAbility(AbilityId ability, bool free = false, Vector3? point = null);

    // Progression.

    /// <summary>Experience through the award every kill and quest uses; the level cap applies.</summary>
    void GrantExperience(uint amount);

    /// <summary>Starts a quest under the accept's availability rules, with no giver (PD-D).</summary>
    QuestResult StartQuest(uint questId);

    /// <summary>Adds to a Kill or Talk objective of the current stage of an Active quest, capped; never a Scripted or a Collect one (PD-D).</summary>
    bool AdvanceQuest(uint questId, uint objectiveId, uint amount = 1);

    // Creatures.

    /// <summary>
    /// One creature on the ground up to 10 m in front of the user, in its own instance, never in a town, removed after
    /// <paramref name="lifetime" /> (5 minutes when null) unless killed first (PD-E); its guid, or null when refused.
    /// </summary>
    ObjectGuid? SpawnCreature(CreatureTemplateId creature, float distance = 2f, TimeSpan? lifetime = null);

    // Party.

    /// <summary>The user's party members in its own instance, the user excluded, by guid (PD-F).</summary>
    IReadOnlyList<ObjectGuid> PartyMembersHere();

    /// <summary>Heals a member from <see cref="PartyMembersHere" /> through the combat service; 0 for anyone else.</summary>
    uint RestoreHealthOf(ObjectGuid member, uint amount);

    /// <summary>Adds to a member's power, capped; 0 for anyone else.</summary>
    uint RestorePowerOf(ObjectGuid member, uint amount);

    // Messages.

    /// <summary>A system line to the user only.</summary>
    void Tell(string line);

    /// <summary>A whisper-style line to the user only, as from <paramref name="from" />.</summary>
    void Whisper(string from, string text);
}
