using Avalon.Network.Packets.Party;
using Avalon.World.Public.Characters;

namespace Avalon.World.Parties;

public readonly record struct ExperienceShare(ICharacter Member, uint Experience);

/// <summary>
/// How a kill's experience is shared (spec 2026-09-30 section 4). Pure. A member <c>levelGap</c> or more
/// levels above the creature gets nothing and is not counted, solo too. n = 1: everything, no bonus. Otherwise the
/// pool is xp × (1 + bonus × (n − 1)); Even gives floor(pool / n) each, LevelWeighted floor(pool × level / Σ levels).
/// The map's level band is applied afterwards, per member, by MapInstance.
/// </summary>
public static class PartyExperience
{
    public static IReadOnlyList<ExperienceShare> Split(uint experience, ushort creatureLevel,
        IReadOnlyList<ICharacter> eligible, PartyExperienceMode mode, float bonusPerExtra, int levelGap)
    {
        var counted = new List<ICharacter>(eligible.Count);
        foreach (ICharacter member in eligible)
        {
            if (member.Level < creatureLevel + levelGap)
                counted.Add(member);
        }

        int n = counted.Count;
        if (n == 0)
            return [];
        if (n == 1)
            return [new ExperienceShare(counted[0], experience)];

        decimal pool = experience * (1m + (decimal)bonusPerExtra * (n - 1));
        decimal levels = 0m;
        foreach (ICharacter member in counted)
            levels += member.Level;

        var shares = new List<ExperienceShare>(n);
        foreach (ICharacter member in counted)
        {
            decimal share = mode == PartyExperienceMode.LevelWeighted && levels > 0
                ? pool * member.Level / levels
                : pool / n;
            shares.Add(new ExperienceShare(member, Whole(share)));
        }

        return shares;
    }

    private static uint Whole(decimal value) =>
        value >= uint.MaxValue ? uint.MaxValue : (uint)decimal.Floor(value);
}
