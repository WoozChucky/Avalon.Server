using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Extensions;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World;
using Avalon.Database.World.Extensions;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>
/// The api's repositories are singletons over one context factory per database kind; that factory
/// opens the request's world (#523). Two worlds over SQLite hold different data for the same row.
/// </summary>
public sealed class CurrentWorldContextsShould : IDisposable
{
    private const string Renamed = "Asthoria Potion";

    private readonly SqliteWorlds _sqlite = new(1, 2);
    private readonly HttpContextAccessor _http = new();
    private readonly ServiceProvider _provider;

    public CurrentWorldContextsShould()
    {
        using (WorldDbContext world2 = _sqlite.CreateWorld(new WorldId(2)))
        {
            ItemTemplate item = world2.ItemTemplates.Single(t => t.Id == new ItemTemplateId(1));
            item.Name = Renamed;
            world2.SaveChanges();
        }

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor>(_http);
        services.AddWorldDatabases(new WorldDatabases(
        [
            new ConfiguredWorld(new WorldId(1), "Host=w1", "Host=c1"),
            new ConfiguredWorld(new WorldId(2), "Host=w2", "Host=c2"),
        ]));
        services.AddSingleton<IWorldDbContextFactory>(_sqlite);
        services.AddWorldRepositories();
        services.AddCharacterRepositories();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _sqlite.Dispose();
    }

    private AsyncServiceScope Request(ushort? world)
    {
        AsyncServiceScope scope = _provider.CreateAsyncScope();
        if (world is { } id) scope.ServiceProvider.GetRequiredService<CurrentWorld>().Select(new WorldId(id), $"World {id}");
        _http.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return scope;
    }

    [Fact]
    public async Task Read_the_selected_worlds_own_database()
    {
        IItemTemplateRepository items = _provider.GetRequiredService<IItemTemplateRepository>();

        string? first, second;
        await using (Request(1)) first = (await items.FindByIdAsync(new ItemTemplateId(1)))?.Name;
        await using (Request(2)) second = (await items.FindByIdAsync(new ItemTemplateId(1)))?.Name;

        Assert.Equal(Renamed, second);
        Assert.NotNull(first);
        Assert.NotEqual(second, first);
    }

    [Fact]
    public async Task Refuse_a_context_when_no_world_is_selected()
    {
        IItemTemplateRepository items = _provider.GetRequiredService<IItemTemplateRepository>();

        await using (Request(null))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => items.FindByIdAsync(new ItemTemplateId(1)));
            Assert.StartsWith("No world selected for this request", ex.Message);
        }
    }

    [Fact]
    public async Task Refuse_a_context_outside_any_request()
    {
        _http.HttpContext = null;
        ICharacterRepository characters = _provider.GetRequiredService<ICharacterRepository>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => characters.FindByAccountAsync(new AccountId(7)));
    }

    [Fact]
    public async Task Read_a_named_world_outside_any_request()
    {
        _http.HttpContext = null;
        IWorldRepositories perWorld = _provider.GetRequiredService<IWorldRepositories>();

        Assert.NotNull(await perWorld.MapTemplates(new WorldId(2)).FindByIdAsync(new MapTemplateId(1)));
        Assert.Empty(await perWorld.Characters(new WorldId(2)).FindByAccountAsync(new AccountId(7)));
    }

    [Fact]
    public void Refuse_to_select_a_second_world()
    {
        CurrentWorld world = new();
        world.Select(new WorldId(1), "Development");

        Assert.Throws<InvalidOperationException>(() => world.Select(new WorldId(2), "Asthoria"));
        Assert.Equal((ushort)1, world.Id!.Value);
    }

    [Fact]
    public void Open_each_configured_worlds_own_strings()
    {
        ConfiguredWorldDbContextFactory factory = new(new WorldDatabases(
        [
            new ConfiguredWorld(new WorldId(1), "Host=w1;Database=world_one", "Host=c1;Database=characters_one"),
            new ConfiguredWorld(new WorldId(2), "Host=w2;Database=world_two", "Host=c2;Database=characters_two"),
        ]), NullLoggerFactory.Instance);

        using WorldDbContext world = factory.CreateWorld(new WorldId(2));
        using var characters = factory.CreateCharacters(new WorldId(1));

        Assert.Equal("Host=w2;Database=world_two", world.Database.GetConnectionString());
        Assert.Equal("Host=c1;Database=characters_one", characters.Database.GetConnectionString());
    }

    [Fact]
    public void Refuse_a_world_it_is_not_configured_for_without_naming_any_string()
    {
        ConfiguredWorldDbContextFactory factory = new(new WorldDatabases(
            [new ConfiguredWorld(new WorldId(1), "Host=w1;Password=secret", "Host=c1;Password=secret")]),
            NullLoggerFactory.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateWorld(new WorldId(9)));

        Assert.StartsWith("World 9 is not configured", ex.Message);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
    }
}
