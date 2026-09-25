using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Xunit;

namespace Avalon.Database.UnitTests;

public class ItemTemplateRepositoryShould
{
    /// <summary>Templates are looked up by id now that an instance can no longer join its template.</summary>
    [Fact]
    public async Task Return_only_the_templates_asked_for()
    {
        using SqliteDatabase<World.WorldDbContext> database = SqliteDatabase.World();
        var templates = new ItemTemplateRepository(database);

        var found = await templates.GetByIdsAsync([new ItemTemplateId(1), new ItemTemplateId(1), new ItemTemplateId(2)]);

        Assert.Equal<ulong>([1ul, 2ul], found.Select(t => t.Id.Value).Order());
    }

    [Fact]
    public async Task Return_nothing_for_no_ids()
    {
        using SqliteDatabase<World.WorldDbContext> database = SqliteDatabase.World();

        Assert.Empty(await new ItemTemplateRepository(database).GetByIdsAsync([]));
    }
}
