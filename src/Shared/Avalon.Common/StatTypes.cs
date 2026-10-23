namespace Avalon.Common;

/// <summary>Questions about a <see cref="StatType" /> that the stats fold, the seed checks and the API share.</summary>
public static class StatTypes
{
    /// <summary>
    /// Whether <paramref name="type" /> is a range (#1179): the added-damage keys, local (35-39) and global (45-52), whose
    /// line has a low and a high end. Every other stat is one number.
    /// </summary>
    public static bool IsRange(StatType type) =>
        type is >= StatType.AddedPhysicalDamage and <= StatType.AddedBlightDamage
            or >= StatType.AddedPhysicalDamageToAttacks and <= StatType.AddedLightningDamageToSpells;
}
