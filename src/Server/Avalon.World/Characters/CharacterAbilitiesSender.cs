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
        int i = 0;
        foreach (IAbility ability in held)
        {
            AbilityMetadata metadata = ability.Metadata;
            AbilityAmount amount = AbilityAmounts.For(combat, metadata, pace);
            amounts[i] = amount;
            infos[i] = new AbilityInfo
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
            i++;
        }

        character.AbilityAmountsSentFor = (combat, pace);
        character.AbilityAmountsInputs = (character.StatsVersion, character.Level);
        character.AbilityAmountsSent = amounts;

        connection.Send(SCharacterAbilitiesPacket.Create(infos, PacketEncoder.Shared));
    }
}
