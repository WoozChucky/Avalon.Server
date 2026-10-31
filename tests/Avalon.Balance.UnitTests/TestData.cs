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

    // B-R20's builds, their node paths verbatim, until balance/builds.json carries them (#1183 Task 10).
    private static readonly Dictionary<string, (CharacterClass Class, int[] Nodes)> s_paths = new(StringComparer.Ordinal)
    {
        ["warrior-axes"] = (CharacterClass.Warrior, [57, 58, 59, 60, 61, 69, 70, 71, 72, 73, 311, 2, 3]),
        ["warrior-blood-price"] = (CharacterClass.Warrior, [57, 58, 59, 60, 61, 62, 66, 67, 68, 302, 332, 2, 3]),
        ["hunter-double-tap"] = (CharacterClass.Hunter, [105, 106, 107, 108, 109, 125, 126, 127, 128, 129, 315, 75, 76]),
        ["hunter-glass-edge"] = (CharacterClass.Hunter, [135, 136, 137, 138, 139, 140, 144, 145, 146, 303, 333, 75, 76]),
        ["wizard-fire"] = (CharacterClass.Wizard, [178, 179, 180, 181, 182, 148, 149, 150, 151, 152, 183, 184, 185]),
        ["wizard-steady-hand"] = (CharacterClass.Wizard, [208, 209, 210, 211, 212, 213, 217, 218, 219, 304, 334, 178, 179]),
        ["cleric-steadfast"] = (CharacterClass.Cleric, [276, 277, 278, 279, 280, 288, 289, 290, 291, 292, 329, 221, 222]),
        ["cleric-unbroken"] = (CharacterClass.Cleric, [276, 277, 278, 279, 280, 281, 285, 286, 287, 305, 335, 246, 247]),
    };

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

    /// <summary>A B-R20 build's class and its whole allocation path, in the order its nodes are taken.</summary>
    public static (CharacterClass Class, int[] Nodes) BuildPath(string build) => s_paths[build];

    /// <summary>A level-10 character of a B-R20 build holding its whole path, wearing nothing.</summary>
    public static SimPlayer Whole(string build)
    {
        (CharacterClass characterClass, int[] nodes) = BuildPath(build);
        return SimPlayer.Create(Seeded, characterClass, 10, [], Seeded.Talents.Hold(characterClass, nodes));
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
