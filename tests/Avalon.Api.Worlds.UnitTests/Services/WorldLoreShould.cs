using Avalon.Api.Contract;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Testing;
using Avalon.Api.Worlds.Services;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Worlds.UnitTests.Services;

/// <summary>A world's lore: a short text admins enter in the world editor. Every check reads the database, not the tracked entity.</summary>
public sealed class WorldLoreShould : IDisposable
{
    private const string Existing = "The last lit kingdom.";
    private readonly SqliteAuthDatabase _database = new();

    public WorldLoreShould()
    {
        using AuthDbContext context = _database.CreateDbContext();
        context.Worlds.RemoveRange(context.Worlds.ToList());
        context.Worlds.Add(new WorldEntity
        {
            Id = new WorldId(101),
            Name = "World101",
            Host = "h",
            Port = 21101,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            Lore = Existing,
            UpdatedAt = DateTime.UtcNow,
        });
        context.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    private WorldService Sut() => new(new WorldRepository(_database), new WorldDatabases([]),
        Substitute.For<IWorldReadiness>(), Substitute.For<IWorldOccupancy>(), NullLogger<WorldService>.Instance);

    private string? StoredLore(ushort id)
    {
        using AuthDbContext context = _database.CreateDbContext();
        return context.Worlds.Single(w => w.Id == new WorldId(id)).Lore;
    }

    private static CreateWorldRequest Create(string? lore) => new()
    {
        Name = "Created",
        Host = "h",
        Port = 1,
        MinVersion = "0.0.1",
        Version = "0.0.1",
        Lore = lore,
    };

    [Fact]
    public async Task Store_lore_given_on_create()
    {
        WorldDto created = await Sut().CreateAsync(Create("The last lit kingdom."));

        Assert.Equal("The last lit kingdom.", created.Lore);
        Assert.Equal("The last lit kingdom.", StoredLore(created.Id));
    }

    [Fact]
    public async Task Store_no_lore_for_a_blank_create()
    {
        WorldDto created = await Sut().CreateAsync(Create("   "));

        Assert.Null(created.Lore);
        Assert.Null(StoredLore(created.Id));
    }

    [Fact]
    public async Task Leave_lore_unchanged_when_the_update_omits_it()
    {
        WorldDto? updated = await Sut().UpdateAsync(101, new UpdateWorldRequest { Name = "Renamed", Lore = null });

        Assert.Equal(Existing, updated!.Lore);
        using AuthDbContext context = _database.CreateDbContext();
        WorldEntity stored = context.Worlds.Single(w => w.Id == new WorldId(101));
        Assert.Equal((Existing, "Renamed"), (stored.Lore, stored.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Clear_lore_on_an_empty_update(string lore)
    {
        WorldDto? updated = await Sut().UpdateAsync(101, new UpdateWorldRequest { Lore = lore });

        Assert.Null(updated!.Lore);
        Assert.Null(StoredLore(101));
    }

    [Fact]
    public async Task Replace_lore_on_update()
    {
        await Sut().UpdateAsync(101, new UpdateWorldRequest { Lore = "A new tale." });

        Assert.Equal("A new tale.", StoredLore(101));
    }
}
