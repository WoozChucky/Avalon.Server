using Avalon.Network.Packets.Character;
using Avalon.World.Abilities;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Characters;

/// <summary>
/// Keeps a character's own client told what each of its abilities deals or heals per hit (#669). Called by
/// WorldServer once per tick per connection, right after <see cref="CharacterSheetFlusher" />, so a stats
/// refresh (a gear change, a level-up) sends the new sheet and the new amounts in the same flush.
/// </summary>
public static class AbilityAmountsFlusher
{
    /// <summary>
    /// The amounts follow only the character's combat stats (its damage stats and main-hand range), and the
    /// abilities it learned at select. So they are worked out again only when those stats differ from the
    /// ones last used, and sent, whole, only when an amount actually changed: a level-up that moves no damage
    /// stat sends nothing. The amounts at select travel in SCharacterAbilitiesPacket, which records them here,
    /// so nothing is sent again on the tick the character enters the world. A new session builds a new
    /// entity, which starts from that session's select.
    /// </summary>
    public static void Flush(IWorldConnection connection)
    {
        if (connection.Character is not CharacterEntity { Spells: CharacterAbilityContainer spells } character)
            return;

        AttackerCombat combat = character.Combat;
        if (character.AbilityAmountsSentFor == combat)
            return;

        character.AbilityAmountsSentFor = combat;

        IReadOnlyCollection<IAbility> abilities = spells.All;
        var amounts = new AbilityAmount[abilities.Count];
        int i = 0;
        foreach (IAbility ability in abilities)
            amounts[i++] = AbilityAmounts.For(combat, ability.Metadata);

        if (character.AbilityAmountsSent is { } sent && sent.AsSpan().SequenceEqual(amounts))
            return;

        character.AbilityAmountsSent = amounts;

        var infos = new AbilityAmountInfo[amounts.Length];
        i = 0;
        foreach (IAbility ability in abilities)
        {
            infos[i] = AbilityAmounts.ToInfo(ability, amounts[i]);
            i++;
        }

        connection.Send(SCharacterAbilityAmountsPacket.Create(infos, connection.CryptoSession.Encrypt));
    }
}
