using Avalon.Combat;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Characters;

/// <summary>
/// Tells a character's own client every ability it holds (SMSG_CHARACTER_ABILITIES): at select, and after a talent
/// change changes what it holds (#1181). Each ability travels with its per-hit amount, tags and pace (#669, #1178),
/// worked out from the stats the character's last refresh derived, and the entity records them, so
/// <see cref="AbilityAmountsFlusher" /> sends no update on top until one of them moves. A list of the same length as
/// the last one overwrites that one's entries and amounts (<see cref="CharacterEntity.AbilityInfosSent" />), so a
/// resend after a talent change that kept the count allocates nothing.
/// </summary>
public static class CharacterAbilitiesSender
{
    public static void Send(IWorldConnection connection, CharacterEntity character)
    {
        IReadOnlyList<IAbility> held = character.Spells.All;
        AttackerCombat combat = character.Combat;
        CasterPace pace = character.Pace;

        // The entity is their only holder (the amounts flusher replaces the amounts, never keeps the old array), and
        // the encoder writes the entries before Send returns, so overwriting them is safe.
        AbilityInfo[] infos = character.AbilityInfosSent is { } keptInfos && keptInfos.Length == held.Count
            ? keptInfos
            : NewInfos(held.Count);
        AbilityAmount[] amounts = character.AbilityAmountsSent is { } keptAmounts && keptAmounts.Length == held.Count
            ? keptAmounts
            : new AbilityAmount[held.Count];
        // By index: a foreach over the interface boxes an enumerator.
        for (int i = 0; i < held.Count; i++)
            Describe(held[i], in combat, in pace, infos[i], out amounts[i]);

        character.AbilityAmountsSentFor = (combat, pace);
        character.AbilityAmountsInputs = (character.StatsVersion, character.Level);
        character.AbilityAmountsSent = amounts;
        character.AbilityInfosSent = infos;

        connection.Send(SCharacterAbilitiesPacket.Create(infos, PacketEncoder.Shared));
    }

    private static AbilityInfo[] NewInfos(int count)
    {
        var infos = new AbilityInfo[count];
        for (int i = 0; i < count; i++)
            infos[i] = new AbilityInfo();

        return infos;
    }

    /// <summary>
    /// Sets every field the list sends, so an entry reused from the last list keeps nothing of its ability.
    /// </summary>
    private static void Describe(IAbility ability, in AttackerCombat combat, in CasterPace pace, AbilityInfo info,
        out AbilityAmount amount)
    {
        AbilityMetadata metadata = ability.Metadata;
        amount = AbilityAmounts.For(combat, metadata, pace);
        info.AbilityId = ability.AbilityId;
        info.Name = metadata.Name;
        info.Cost = metadata.Cost;
        info.Range = (ushort)metadata.Range;
        info.AimMode = metadata.AimMode;
        info.Shape = metadata.Shape;
        info.Anchor = metadata.Anchor;
        info.Reach = metadata.Reach;
        info.Radius = metadata.Radius;
        info.ArcDegrees = metadata.ArcDegrees;
        info.ProjectileSpeed = metadata.ProjectileSpeed;
        info.Pierce = metadata.Pierce;
        info.Affects = metadata.Affects;
        info.AmountKind = amount.Kind;
        info.AmountMin = amount.Min;
        info.AmountMax = amount.Max;
        info.Tags = metadata.Tags;
        info.AmountTypes = amount.Types;
        info.CooldownMs = amount.CooldownMs;
        info.CastTimeMs = amount.CastTimeMs;
    }
}
