using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests;

/// <summary>The modifier catalog for tests that build StaticData, World or an ItemsPatch and roll nothing.</summary>
internal static class ItemModifierRepositories
{
    public static ItemModifierCatalog EmptyCatalog() => new([], [], NullLogger.Instance);

    /// <summary>An items generation of <paramref name="templates" /> over an empty catalog: what a flush names copies by.</summary>
    public static ItemsPatch Items(params ItemTemplate[] templates) => new(templates, EmptyCatalog());

    public static IItemModifierRepository Empty()
    {
        IItemModifierRepository repository = Substitute.For<IItemModifierRepository>();
        repository.GetCatalogAsync(Arg.Any<CancellationToken>()).Returns(_ => EmptyCatalog());
        return repository;
    }
}
