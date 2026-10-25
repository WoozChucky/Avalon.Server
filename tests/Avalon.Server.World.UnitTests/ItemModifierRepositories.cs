using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests;

/// <summary>The modifier catalog for tests that build StaticData, World or an ItemsPatch and roll nothing.</summary>
internal static class ItemModifierRepositories
{
    private static readonly Lazy<Task<ResonanceTable>> s_seededResonance = new(async () =>
    {
        using var database = SqliteDatabase.World();
        return (await new ItemModifierRepository(database, NullLoggerFactory.Instance).GetCatalogAsync(CancellationToken.None))
            .Resonance;
    });

    public static ItemModifierCatalog EmptyCatalog() => new([], [], NullLogger.Instance);

    /// <summary>The seeded catalog's Resonance table (#1180), as the world reads it from the World database; read once.</summary>
    public static Task<ResonanceTable> SeededResonance() => s_seededResonance.Value;

    /// <summary>An items generation of <paramref name="templates" /> over an empty catalog: what a flush names copies by.</summary>
    public static ItemsPatch Items(params ItemTemplate[] templates) => new(templates, EmptyCatalog());

    public static IItemModifierRepository Empty()
    {
        IItemModifierRepository repository = Substitute.For<IItemModifierRepository>();
        repository.GetCatalogAsync(Arg.Any<CancellationToken>()).Returns(_ => EmptyCatalog());
        return repository;
    }

    /// <summary>
    /// A catalog of one implicit row (+1 Health), which each of <paramref name="bases" /> links: a common base links
    /// exactly one implicit, or the items area leaves it out (#1179). For tests that load a base through StaticData.
    /// </summary>
    public static IItemModifierRepository ImplicitFor(params ItemTemplate[] bases)
    {
        var row = new ItemModifier
        {
            Id = 1,
            GroupId = 1,
            Group = new ItemModifierGroup { Id = 1, Kind = ItemModifierKind.Implicit, Name = "Health" },
            Tier = 9,
            RequiredItemLevel = 1,
            Stats = [new ItemModifierStat { ModifierId = 1, Index = 0, Stat = StatType.Health, Min = 1, Max = 1 }],
        };
        ItemTemplateModifier[] links = [.. bases.Select(b => new ItemTemplateModifier { TemplateId = b.Id, Index = 0, ModifierId = 1 })];
        IItemModifierRepository repository = Substitute.For<IItemModifierRepository>();
        repository.GetCatalogAsync(Arg.Any<CancellationToken>()).Returns(_ => new ItemModifierCatalog([row], links, NullLogger.Instance));
        return repository;
    }
}
