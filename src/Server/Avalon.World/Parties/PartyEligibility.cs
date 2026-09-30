using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;

namespace Avalon.World.Parties;

/// <summary>
/// Who shares a kill (spec 2026-09-30 section 4, rule D), taken once, on the tick, at the kill. Pure. With no party,
/// the killer alone; in a party, every member present in this instance, not in a leave countdown, and in the creature's
/// encounter or within <c>range</c> of the corpse on X/Z; dead members count; the killer always counts. A killer in a
/// leave countdown, or a killer that is not a character, gets nothing.
/// </summary>
public static class PartyEligibility
{
    public static IReadOnlyList<ICharacter> For(ICharacter? killer, Party? party,
        IReadOnlyDictionary<ObjectGuid, ICharacter> present, Func<uint, bool> inCountdown, IEncounter? encounter,
        Vector3 corpse, float range)
    {
        if (killer is null || inCountdown(killer.Guid.Id))
            return [];

        if (party is null)
            return [killer];

        var eligible = new List<ICharacter>(party.Members.Count);
        float rangeSquared = range * range;
        foreach (PartyMember member in party.Members)
        {
            uint id = member.Id.Value;
            if (!present.TryGetValue(new ObjectGuid(ObjectType.Character, id), out ICharacter? character))
                continue;

            if (id == killer.Guid.Id)
            {
                eligible.Add(character);
                continue;
            }

            if (inCountdown(id))
                continue;

            float dx = character.Position.x - corpse.x;
            float dz = character.Position.z - corpse.z;
            bool near = dx * dx + dz * dz <= rangeSquared;
            if (near || encounter?.Players.Contains(character) == true)
                eligible.Add(character);
        }

        return eligible;
    }
}
