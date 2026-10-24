using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests;

/// <summary>The modifier catalog for tests that build StaticData, World or an ItemsPatch and roll nothing.</summary>
internal static class ItemModifierRepositories
{
    public static ItemModifierCatalog EmptyCatalog() => new([], [], NullLogger.Instance);

    public static IItemModifierRepository Empty()
    {
        IItemModifierRepository repository = Substitute.For<IItemModifierRepository>();
        repository.GetCatalogAsync(Arg.Any<CancellationToken>()).Returns(_ => EmptyCatalog());
        return repository;
    }
}
