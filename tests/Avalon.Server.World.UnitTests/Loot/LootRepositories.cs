using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>Loot table repositories for tests that build StaticData or World.</summary>
internal static class LootRepositories
{
    public static ILootTableRepository Empty() => Of(() => []);

    /// <summary>Read on every call, so a test can change what the next reload sees.</summary>
    public static ILootTableRepository Of(Func<IReadOnlyCollection<LootTable>> tables)
    {
        var repository = Substitute.For<ILootTableRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(tables()));
        return repository;
    }
}
