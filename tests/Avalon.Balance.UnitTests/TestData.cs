using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Balance.UnitTests;

/// <summary>The seeded data, loaded once. Read-only: a test that changes tables reads its own SeedTables.</summary>
internal static class TestData
{
    private static readonly Lazy<BalanceData> s_seededData = new(() => BalanceData.From(SeedSource.Load()));
    private static readonly Lazy<SeedTables> s_seedCache = new(SeedSource.Load);

    public static BalanceData Seeded => s_seededData.Value;

    /// <summary>A private copy of the seed tables: a test may override it freely.</summary>
    public static SeedTables Seed() => s_seedCache.Value.Clone();

    /// <summary>The fixed copies a class wears in a checked-in gear profile ("none" is no gear).</summary>
    public static IReadOnlyList<WornItem> Gear(string profile, CharacterClass characterClass) =>
        [.. Config().Scenarios.GearFor(profile, characterClass).Select(Seeded.Worn)];

    /// <summary>The midpoint of a seeded item's base-stat range, as its fixed copy holds it.</summary>
    public static int MidpointOf(ulong item, ItemBaseStat stat)
    {
        ItemTemplateBaseStat row = Seeded.Tables.ItemTemplateBaseStats.Single(r => r.TemplateId.Value == item && r.Stat == stat);
        return FixedGear.Midpoint(row.Min, row.Max);
    }

    /// <summary>
    /// <paramref name="player" />, given each of <paramref name="abilities" /> it does not hold yet (B-R9): until builds
    /// land, a test hands a character its abilities directly.
    /// </summary>
    public static SimPlayer Holding(this SimPlayer player, BalanceData data, params uint[] abilities)
    {
        foreach (uint id in abilities)
        {
            if (player.Abilities.All(a => a.Id != id))
                player.Abilities.Add(new SimAbility(data.Abilities[new AbilityId(id)]));
        }

        return player;
    }

    /// <summary>The checked-in balance files, freshly parsed so a test may change them.</summary>
    public static BalanceConfig Config()
    {
        string dir = Path.Combine(RepositoryRoot.Find(), "balance");
        return new BalanceConfig(
            ConfigFileStore.Load(Path.Combine(dir, "scenarios.json"), ConfigFiles.ParseScenarios),
            ConfigFileStore.Load(Path.Combine(dir, "targets.json"), ConfigFiles.ParseTargets),
            ConfigFileStore.Load(Path.Combine(dir, "rotations.json"), ConfigFiles.ParseRotations));
    }
}
