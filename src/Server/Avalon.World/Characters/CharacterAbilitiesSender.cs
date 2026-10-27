using Avalon.Combat;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Characters;

/// <summary>
/// Tells a character's own client every ability it holds (SMSG_CHARACTER_ABILITIES): at select, and after a talent change
/// changes what it holds (#1181). Each ability travels with its per-hit amount, tags and pace (#669, #1178), worked out
/// from the stats the character's last refresh derived, and the entity records them, so
/// <see cref="AbilityAmountsFlusher" /> sends no update on top until one of them moves.
/// </summary>
public static class CharacterAbilitiesSender
{
    public static void Send(IWorldConnection connection, CharacterEntity character)
    {
        IReadOnlyCollection<IAbility> held = character.Spells.All;
        AttackerCombat combat = character.Combat;
        CasterPace pace = character.Pace;

        var infos = new AbilityInfo[held.Count];
        var amounts = new AbilityAmount[held.Count];
        if (held is IReadOnlyList<IAbility> list)
        {
            // By index: the talent tree loads a List (TalentAbilities), and a foreach over the interface boxes an
            // enumerator.
            for (int i = 0; i < list.Count; i++)
                Describe(list[i], in combat, in pace, out infos[i], out amounts[i]);
        }
        else
        {
            int i = 0;
            foreach (IAbility ability in held)
            {
                Describe(ability, in combat, in pace, out infos[i], out amounts[i]);
                i++;
            }
        }

        character.AbilityAmountsSentFor = (combat, pace);
        character.AbilityAmountsInputs = (character.StatsVersion, character.Level);
        character.AbilityAmountsSent = amounts;

        connection.Send(SCharacterAbilitiesPacket.Create(infos, PacketEncoder.Shared));
    }

    private static void Describe(IAbility ability, in AttackerCombat combat, in CasterPace pace, out AbilityInfo info,
        out AbilityAmount amount)
    {
        AbilityMetadata metadata = ability.Metadata;
        amount = AbilityAmounts.For(combat, metadata, pace);
        info = new AbilityInfo
        {
            AbilityId = ability.AbilityId,
            Name = metadata.Name,
            Cost = metadata.Cost,
            Range = (ushort)metadata.Range,
            AimMode = metadata.AimMode,
            Shape = metadata.Shape,
            Anchor = metadata.Anchor,
            Reach = metadata.Reach,
            Radius = metadata.Radius,
            ArcDegrees = metadata.ArcDegrees,
            ProjectileSpeed = metadata.ProjectileSpeed,
            Pierce = metadata.Pierce,
            Affects = metadata.Affects,
            AmountKind = amount.Kind,
            AmountMin = amount.Min,
            AmountMax = amount.Max,
            Tags = metadata.Tags,
            AmountTypes = amount.Types,
            CooldownMs = amount.CooldownMs,
            CastTimeMs = amount.CastTimeMs,
        };
    }
}
