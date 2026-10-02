using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Items;

/// <summary>
/// <see cref="IItemUseContext" /> over one use of one item (item use). Built per hook call by
/// ItemUseService; the OnUse one's <see cref="Consumed" /> is what the service takes afterwards. Tick thread only.
/// Every member acts on the user, its own instance (<paramref name="host" />) or its party members in that instance,
/// and only through the service that owns the change.
/// </summary>
public sealed class ItemUseContext(
    IWorldConnection connection,
    CharacterEntity character,
    IItemUseHost host,
    InventoryItem item,
    ItemTemplate template,
    ItemUseTools tools) : IItemUseContext
{
    /// <summary>How far in front of the user a creature may be spawned.</summary>
    public const float MaxSpawnDistance = 10f;

    /// <summary>How long a summoned creature stays when the script gives no lifetime.</summary>
    public static readonly TimeSpan DefaultSummonLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How many of the used stack OnUse marked spent.</summary>
    public uint Consumed { get; private set; }

    // The user.
    public string Name => character.Name;
    public ushort Level => character.Level;
    public CharacterClass Class => character.Class;
    public uint Health => character.CurrentHealth;
    public uint MaxHealth => character.Health;
    public PowerType PowerType => character.PowerType;
    public uint Power => character.CurrentPower ?? 0u;
    public uint MaxPower => character.Power ?? 0u;
    public Vector3 Position => character.Position;
    public ushort MapId => character.Map.Value;
    public bool InTown => host.MapType == MapType.Town;
    public bool InCombat => character.IsInCombat;
    public bool InParty => character.PartyId is not null;
    public bool ReturningToTown => connection.RespawnInFlight;

    // The item.
    public ItemTemplateId ItemId => template.Id;
    public string ItemName => template.Name;
    public uint? UseValue => template.UseValue;
    public uint StackCount => item.Count;
    public TimeSpan Cooldown => TimeSpan.FromMilliseconds(template.UseCooldownMs ?? 0u);

    public void Consume(uint count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfZero(count);
        if ((ulong)Consumed + count > item.Count)
            throw new InvalidOperationException(
                $"Consuming {count} more of item {item.InstanceId} would spend {(ulong)Consumed + count} of a stack of {item.Count}.");
        Consumed += count;
    }

    // Inventory and money.
    public bool HasBagRoom(ItemTemplateId itemId, uint count) =>
        tools.Economy.InventoryOf(character).CanAdd(itemId, count) == InventoryAddResult.Ok;

    public bool GiveItem(ItemTemplateId itemId, uint count) =>
        tools.Economy.InventoryOf(character).TryAdd(itemId, count) == InventoryAddResult.Ok;

    public bool TakeItem(ItemTemplateId itemId, uint count) =>
        tools.Economy.InventoryOf(character).TryRemove(itemId, count) == InventoryRemoveResult.Ok;

    public ulong Money => tools.Economy.WalletOf(character).Balance;
    public bool GiveMoney(ulong copper) => tools.Economy.WalletOf(character).TryAddMoney(copper) == WalletResult.Ok;
    public bool TakeMoney(ulong copper) => tools.Economy.WalletOf(character).TrySpend(copper) == WalletResult.Ok;

    // Health, power and damage.
    public uint RestoreHealth(uint amount) => amount == 0 ? 0u : host.RestoreHealth(character, character, amount);

    public uint RestorePower(uint amount) => GainPower(character, amount);

    public IReadOnlyList<ObjectGuid> HostilesAround(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            return [];

        return host.Hits.InCircle(character.Position, radius)
            .Where(unit => Hostility.IsHostile(character, unit, host.MapType))
            .Select(unit => unit.Guid)
            .ToList();
    }

    public bool Damage(ObjectGuid target, uint amount)
    {
        if (amount == 0 || character.IsDead || UnitHere(target) is not { } unit || unit.CurrentHealth == 0
            || !Hostility.IsHostile(character, unit, host.MapType) || CombatService.IgnoresHits(unit))
            return false;

        // A raw hit: no ability, so the amount is the hit's base, resolved by the instance's combat service.
        host.CombatService.ApplyDamage(character, unit, amount);
        return true;
    }

    // Movement.
    public bool ReturnToTown()
    {
        if (connection.RespawnInFlight || InTown)
            return false;

        // The user leaves its encounter on the way, and a living user is not revived.
        connection.RespawnInFlight = true;
        tools.TownReturn.Start(connection, revive: false, dropEncounter: true);
        return true;
    }

    public bool Teleport(MapTemplateId map, Vector3? position = null) => tools.Teleport.Start(connection, map, position);

    // Abilities.
    public bool CastAbility(AbilityId abilityId, bool free = false, Vector3? point = null)
    {
        // The cast system's queue has no one-cast-at-a-time check of its own, so a cast under way refuses here.
        if (character.IsDead || character.Spells.IsCasting)
            return false;

        if (AbilityToCast(abilityId) is not { } ability || AimFor(ability, point) is not { } aim)
            return false;

        if (!host.CastForItem(character, aim, ability, free))
            return false;

        character.MarkCombat();   // as CastAbilityHandler does for every accepted cast
        return true;
    }

    // Progression.
    public void GrantExperience(uint amount) =>
        ExperienceAward.Grant(character, amount, tools.World.Data, tools.Parties, tools.Logger);

    public QuestResult StartQuest(uint questId) =>
        tools.Quests?.StartFromItem(character, questId) ?? QuestResult.NotAvailable;

    public bool AdvanceQuest(uint questId, uint objectiveId, uint amount = 1) =>
        tools.Quests?.AdvanceFromItem(character, questId, objectiveId, amount) ?? false;   // Kill and Talk only

    // Creatures.
    public ObjectGuid? SpawnCreature(CreatureTemplateId creature, float distance = 2f, TimeSpan? lifetime = null)
    {
        TimeSpan stays = lifetime ?? DefaultSummonLifetime;
        if (InTown || !float.IsFinite(distance) || distance < 0f || distance > MaxSpawnDistance || stays <= TimeSpan.Zero)
            return null;

        Vector3 near = character.Position + AbilityAim.FacingFromYaw(character.Orientation.y) * distance;
        if (tools.Placement.SpawnAt(host, creature, near) is not { } spawned)
            return null;

        host.DespawnAfter(spawned, stays);
        return spawned.Guid;
    }

    // Party.
    public IReadOnlyList<ObjectGuid> PartyMembersHere()
    {
        if (character.PartyId is not { } party)
            return [];

        return host.Characters.Values.OfType<CharacterEntity>()
            .Where(member => !ReferenceEquals(member, character) && party.Equals(member.PartyId))
            .Select(member => member.Guid)
            .OrderBy(guid => guid.RawValue)
            .ToList();
    }

    public uint RestoreHealthOf(ObjectGuid member, uint amount) =>
        amount > 0 && MemberHere(member) is { } m ? host.RestoreHealth(character, m, amount) : 0u;

    public uint RestorePowerOf(ObjectGuid member, uint amount) =>
        MemberHere(member) is { } m ? GainPower(m, amount) : 0u;

    // Messages.
    public void Tell(string line) => connection.Send(SChatMessagePacket.System(line,
        tools.Time.GetUtcNow().UtcDateTime, connection.CryptoSession.Encrypt));

    public void Whisper(string from, string text) => connection.Send(SChatMessagePacket.Create(0UL, 0UL, from, text,
        tools.Time.GetUtcNow().UtcDateTime, connection.CryptoSession.Encrypt, ChatChannel.Whisper));

    /// <summary>
    /// The ability to cast: one the user holds, refused while its cooldown runs even when free; or, from the catalog,
    /// one it does not hold, only when instant, since a queued cast of an ability the user does not hold would not
    /// show as the user's cast and could overlap another.
    /// </summary>
    private IAbility? AbilityToCast(AbilityId abilityId)
    {
        if (character.Spells[abilityId] is { } held)
            return held.CooldownTimer > 0 ? null : held;

        if (!tools.World.Data.Abilities.TryGet(abilityId, out AbilityTemplate? row))
            return null;

        var ability = new GameAbility
        {
            AbilityId = abilityId, Metadata = AbilityMetadataMapper.From(row),
            CastTimeTimer = (float)row.CastTime / 1000, CooldownTimer = 0f,
        };
        return ability.Metadata.CastTime > 0 ? null : ability;
    }

    /// <summary>
    /// A Cursor ability aims at <paramref name="point" />, which it needs, finite; any other aims along the user's
    /// facing, or toward the point when one is given.
    /// </summary>
    private AbilityAim? AimFor(IAbility ability, Vector3? point)
    {
        Vector3 facing = AbilityAim.FacingFromYaw(character.Orientation.y);
        if (ability.Metadata.AimMode == AbilityAimMode.Cursor)
        {
            if (point is not { } at || !float.IsFinite(at.x) || !float.IsFinite(at.y) || !float.IsFinite(at.z))
                return null;
            return new AbilityAim(facing, at);
        }

        return new AbilityAim(point is { } toward ? AbilityAim.Toward(character.Position, toward, facing) : facing, null);
    }

    private static uint GainPower(CharacterEntity target, uint amount)
    {
        uint before = target.CurrentPower ?? 0u;
        target.GainPower(amount);
        uint after = target.CurrentPower ?? 0u;
        return after > before ? after - before : 0u;
    }

    /// <summary>A unit of the user's own instance; never one elsewhere.</summary>
    private IUnit? UnitHere(ObjectGuid guid) =>
        host.Creatures.TryGetValue(guid, out ICreature? creature) ? creature
        : host.Characters.TryGetValue(guid, out ICharacter? other) ? other
        : null;

    /// <summary>One of the user's party members in its own instance, never the user.</summary>
    private CharacterEntity? MemberHere(ObjectGuid guid) =>
        character.PartyId is { } party
        && host.Characters.TryGetValue(guid, out ICharacter? found)
        && found is CharacterEntity member && !ReferenceEquals(member, character) && party.Equals(member.PartyId)
            ? member
            : null;
}
