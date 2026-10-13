using System.Diagnostics;
using Avalon.Network.Packets.Party;
using Avalon.World.Public.Characters;

namespace Avalon.World.Parties;

public readonly record struct ExperienceShare(ICharacter Member, uint Experience);

/// <summary>
/// How a kill's experience is shared (spec 2026-09-30 section 4). Pure. A member <c>levelGap</c> or more
/// levels above the creature, or one <c>canGainExperience</c> refuses (<c>ExperienceAward.CanGainExperience</c>: at the
/// maximum level or before a gap in the levels, #735; null lets everyone gain), gets nothing and is not counted, solo
/// too. n = 1: everything, no bonus. Otherwise the
/// pool is xp × (1 + bonus × (n − 1)); Even gives floor(pool / n) each, LevelWeighted floor(pool × level / Σ levels).
/// The map's level band is applied afterwards, per member, by MapInstance.
/// </summary>
public static class PartyExperience
{
    /// <summary>The shares in a list of its own, in the order of <paramref name="eligible" />: for a caller off the kill path.</summary>
    public static IReadOnlyList<ExperienceShare> Split(uint experience, ushort creatureLevel,
        IReadOnlyList<ICharacter> eligible, PartyExperienceMode mode, float bonusPerExtra, int levelGap,
        Func<ICharacter, bool>? canGainExperience = null)
    {
        var shares = new List<ExperienceShare>();
        Fill(shares, experience, creatureLevel, eligible, mode, bonusPerExtra, levelGap, canGainExperience);
        return shares;
    }

    /// <summary>
    /// The same shares added to <paramref name="shares" />, an empty list the caller keeps: a kill allocates nothing
    /// here (#890). The members that count go in first, with nothing, and each is then given its share in place.
    /// </summary>
    public static void Fill(List<ExperienceShare> shares, uint experience, ushort creatureLevel,
        IReadOnlyList<ICharacter> eligible, PartyExperienceMode mode, float bonusPerExtra, int levelGap,
        Func<ICharacter, bool>? canGainExperience = null)
    {
        // Every member already in the list would be counted, and given a share, again.
        Debug.Assert(shares.Count == 0, "The share list still holds an earlier kill's shares");

        // By index: a foreach over the interface boxes the list's enumerator.
        for (int i = 0; i < eligible.Count; i++)
        {
            ICharacter member = eligible[i];
            if (member.Level < creatureLevel + levelGap && (canGainExperience is null || canGainExperience(member)))
                shares.Add(new ExperienceShare(member, 0));
        }

        int n = shares.Count;
        if (n == 0)
            return;
        if (n == 1)
        {
            shares[0] = new ExperienceShare(shares[0].Member, experience);
            return;
        }

        decimal pool = experience * (1m + (decimal)bonusPerExtra * (n - 1));
        decimal levels = 0m;
        for (int i = 0; i < n; i++)
            levels += shares[i].Member.Level;

        for (int i = 0; i < n; i++)
        {
            ICharacter member = shares[i].Member;
            decimal share = mode == PartyExperienceMode.LevelWeighted && levels > 0
                ? pool * member.Level / levels
                : pool / n;
            shares[i] = new ExperienceShare(member, Whole(share));
        }
    }

    private static uint Whole(decimal value) =>
        value >= uint.MaxValue ? uint.MaxValue : (uint)decimal.Floor(value);
}
