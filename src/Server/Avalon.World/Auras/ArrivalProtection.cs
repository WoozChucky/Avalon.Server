using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// Arrival-specific eligibility and cancellation for the catalog's helpful buff.
/// Checks read a cached held copy, never scan a unit's buffs, and consult the clock only while that copy exists.
/// </summary>
public static class ArrivalProtection
{
    private static readonly AuraId s_id = new(ArrivalProtectionAura.Id);

    /// <summary>Whether <paramref name="auraId" /> is the arrival buff, which is never saved and holds a slot of its own.</summary>
    public static bool Is(uint auraId) => auraId == ArrivalProtectionAura.Id;

    public static bool IsProtected(IUnit unit) =>
        unit is CharacterEntity character
        && character.Auras.HeldArrivalProtection is { } aura
        && character.Clock.GetUtcNow() < aura.Schedule.ExpiresAt;

    public static AuraApplyResult Start(CharacterEntity character, AuraSystem auras) =>
        auras.Apply(character, character, s_id, AuraSource.None);

    /// <summary>
    /// Ends every held copy through the aura system of the character's instance: its movement, a cast or an item use it
    /// started, or its leaving. Nothing for a creature or a character holding none.
    /// </summary>
    public static void End(IUnit unit)
    {
        if (unit is CharacterEntity character && character.Auras.HeldArrivalProtection is not null)
            character.Auras.Roster?.System.Remove(character, s_id, AuraRemoveReason.Cancelled);
    }
}
