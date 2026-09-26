using Avalon.Domain.World;
using Avalon.World;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>
/// A loaded StaticData whose items and loot tables come from the test, read on every prepare so a
/// test can change them and reload. Every other area is empty, except one creature base-stat row
/// and one level row, which the kill path reads. Built on <see cref="TestStaticData" />.
/// </summary>
internal static class LootStaticData
{
    public static Task<StaticData> LoadAsync(
        Func<IReadOnlyCollection<ItemTemplate>> items,
        Func<IReadOnlyCollection<LootTable>> tables,
        IReadOnlyCollection<CharacterLevelExperience>? levels = null)
    {
        IReadOnlyCollection<CharacterLevelExperience> levelRows =
            levels ?? new[] { new CharacterLevelExperience { Level = 1, Experience = 1_000_000 } };

        return TestStaticData.LoadAsync(TestStaticData.Repositories(
            items: items,
            levels: () => levelRows,
            loot: LootRepositories.Of(tables)));
    }
}
