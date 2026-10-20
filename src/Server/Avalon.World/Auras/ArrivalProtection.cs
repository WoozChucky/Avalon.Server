using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// Arrival-specific eligibility and movement cancellation for the catalog's helpful buff.
/// Checks read a cached held copy, never scan a unit's buffs, and consult the clock only while that copy exists.
/// </summary>
public static class ArrivalProtection
{
    private static readonly AuraId s_id = new(ArrivalProtectionAura.Id);

    public static bool IsProtected(IUnit unit) =>
        unit is CharacterEntity character
        && character.Auras.Arrival is { } aura
        && character.Clock.GetUtcNow() < aura.Schedule.ExpiresAt;

    public static AuraApplyResult Start(CharacterEntity character, AuraSystem auras) =>
        auras.Apply(character, character, s_id, AuraSource.None);

    public static void End(IUnit unit)
    {
        if (unit is CharacterEntity character && character.Auras.Arrival is not null)
            character.Auras.Roster?.System?.Remove(character, s_id, AuraRemoveReason.Cancelled);
    }
}
