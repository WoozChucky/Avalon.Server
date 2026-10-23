using Avalon.Combat;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Characters;

/// <summary>
/// Keeps a character's own client told what each of its abilities deals or heals per hit (#669), and how long its cooldown
/// and cast take (#1178). Called by WorldServer once per tick per connection, right after
/// <see cref="CharacterSheetFlusher" />, so a stats refresh (a gear change, a level-up) sends the new sheet and the new
/// amounts in the same flush.
/// </summary>
public static class AbilityAmountsFlusher
{
    /// <summary>
    /// The amounts follow only the character's combat stats (#1178: its level, weapon and offence block), its pace (the
    /// speeds, bounded by the formula its last refresh read, and its weapon's attack time) and the abilities it learned at
    /// select. Both are fixed by a stats refresh and the level, so most ticks compare those two counts
    /// (<see cref="CharacterEntity.AbilityAmountsInputs" />) and read nothing else. Once one moved, the combat stats and the
    /// pace are compared with the ones last used, so a refresh that changed neither (an aura with no offence) works nothing
    /// out; the amounts are then worked out again and sent, whole, only when an amount or a pace actually changed. The
    /// amounts at select travel in SCharacterAbilitiesPacket, which records them here, so nothing is sent again on the tick
    /// the character enters the world. A new session builds a new entity, which starts from that session's select.
    /// </summary>
    public static void Flush(IWorldConnection connection)
    {
        if (connection.Character is not CharacterEntity { Spells: CharacterAbilityContainer spells } character)
            return;

        (int StatsVersion, ushort Level) inputs = (character.StatsVersion, character.Level);
        if (character.AbilityAmountsSentFor is not null && character.AbilityAmountsInputs == inputs)
            return;

        character.AbilityAmountsInputs = inputs;
        AttackerCombat combat = character.Combat;
        CasterPace pace = character.Pace;
        if (character.AbilityAmountsSentFor is { } sentFor && sentFor.Combat == combat && sentFor.Pace == pace)
            return;

        character.AbilityAmountsSentFor = (combat, pace);

        IReadOnlyCollection<IAbility> abilities = spells.All;
        var amounts = new AbilityAmount[abilities.Count];
        int i = 0;
        foreach (IAbility ability in abilities)
            amounts[i++] = AbilityAmounts.For(combat, ability.Metadata, pace);

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

        connection.Send(SCharacterAbilityAmountsPacket.Create(infos, PacketEncoder.Shared));
    }
}
